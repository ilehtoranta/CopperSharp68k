namespace Cilica.Analysis;

using Cilica.CodeGen.Cil;
using Cilica.IR.Utils;
using Cilica.Passes;

/// <summary> Limits surviving caller growth independently of inlining profitability. </summary>
internal sealed class InliningSizeBudget(MethodTransformContext context, int instructionBudget = InliningSizeBudget.DefaultInstructionBudget)
{
    internal const int DefaultInstructionBudget = 4096;
    readonly bool _recursiveCaller = context.Method.Instructions().OfType<CallInst>()
        .Any(call => call.Method is MethodDefOrSpec target && target.Definition == context.Definition);

    long _estimatedRevision = -1, _measuredRevision = -1;
    long _estimatedSize, _reservedSize;
    int _measuredSize, _reservedInstructions, _remainingInstructions = instructionBudget;
    bool _measuredReservation;

    public bool HasBudget => _remainingInstructions > 0;

    public bool AllowsExpansion(Instruction site, MethodDefOrSpec target, Action<Instruction> expand, out string? rejection, int initializedFields = 0)
    {
        rejection = null;
        var caller = context.Method;
        _reservedInstructions = target.Definition.Body!.Instructions().Count();
        if (_reservedInstructions > _remainingInstructions) {
            rejection = $"needs {_reservedInstructions} instructions, remaining cloning budget {_remainingInstructions}";
            return false;
        }
        int limit = context.Compilation.Settings.MaxInlinedMethodILSize;
        if (_estimatedRevision != caller.Revision) {
            _estimatedSize = EstimateSize(caller);
            _estimatedRevision = caller.Revision;
        }

        long growth = EstimateGrowth(site, target, initializedFields);
        if (_recursiveCaller && growth > 0) {
            rejection = "inlining would grow a recursive caller";
            return false;
        }
        // Shrinking estimates do not buy room for later expansions. Cleanup
        // and emission can recover that space when the budget is exhausted.
        _reservedSize = _estimatedSize + Math.Max(0, growth);
        _measuredReservation = false;
        if (growth <= 0 || _reservedSize <= limit) return true;

        // Trial clones spend the same work budget as committed expansions.
        // Leave enough for the real inline if specialization makes it fit.
        int trialInstructions = caller.Instructions().Count() + _reservedInstructions;
        if (trialInstructions > _remainingInstructions - _reservedInstructions) {
            rejection = "insufficient instruction budget for a size probe and the committed expansion";
            return false;
        }
        _remainingInstructions -= trialInstructions;
        var copy = IRCloner.CloneBody(caller, out var cloner);
        expand((Instruction)cloner.GetMapping(site));
        int expandedSize = MeasureAfterCleanup(copy);
        _reservedSize = expandedSize;
        _measuredReservation = true;
        if (expandedSize <= limit) return true;

        // Already-large inputs may still benefit from size-neutral or shrinking
        // inlines. Compare the same cleanup/emission on both sides.
        if (_estimatedSize <= limit) {
            rejection = $"resulting IL size {expandedSize} bytes exceeds limit {limit}";
            return false;
        }
        if (_measuredRevision != caller.Revision) {
            int callerInstructions = caller.Instructions().Count();
            if (callerInstructions > _remainingInstructions - _reservedInstructions) {
                rejection = "insufficient instruction budget to measure the original caller";
                return false;
            }
            _remainingInstructions -= callerInstructions;
            _measuredSize = MeasureAfterCleanup(IRCloner.CloneBody(caller));
            _measuredRevision = caller.Revision;
        }
        if (expandedSize > _measuredSize) {
            rejection = $"already oversized caller would grow from {_measuredSize} to {expandedSize} IL bytes (limit {limit})";
            return false;
        }
        return true;
    }

    public void RecordExpansion()
    {
        // Reuse the measured, cleaned size until another pass changes the body.
        // Subsequent candidates only need to reserve their additional growth.
        _estimatedSize = _reservedSize;
        _estimatedRevision = context.Method.Revision;
        _remainingInstructions -= _reservedInstructions;
        if (_measuredReservation) {
            _measuredSize = (int)_reservedSize;
            _measuredRevision = context.Method.Revision;
        }
    }

    int MeasureAfterCleanup(MethodBody body)
    {
        using var scope = context.Logger.IsEnabled(LogLevel.Trace)
            ? context.Logger.Push(new("Cilica.Inlining.SizeProbe"), $"Speculative size probe for '{body.Definition}' on a working copy")
            : default;
        var ctx = new MethodTransformContext(context.Compilation, body);
        Cleanup(ctx, new SimplifyInsts(ctx.Compilation.Resolver));
        return ILGenerator.GenerateCode(body).GetCodeSize();
    }

    internal static void Cleanup(MethodTransformContext ctx, SimplifyInsts simplify)
    {
        PassRunner.Run(new SimplifyCFG(), ctx);
        PassRunner.Run(new SsaPromotion(), ctx);
        PassRunner.Run(simplify, ctx);
        PassRunner.Run(new DeadCodeElim(), ctx);
    }

    static long EstimateGrowth(Instruction site, MethodDefOrSpec target, int initializedFields)
    {
        var body = target.Definition.Body!;
        // The call/newobj opcode occupies five bytes. Inline returns disappear;
        // their values replace the caller's uses, including repeated constants.
        long size = EstimateSize(body, inlined: true, resultUses: site.NumUses) - 5 + 32L * initializedFields;
        if (body.NumBlocks > 1) size += 64;
        if (site is CallInst { IsVirtual: true, InBounds: false } && site.Operands[0].ResultType.IsManagedObject()) size += 16;
        if (target.RequiresTypeInitialization()) size += 16;
        if (target.Module != site.Block.Method.Definition.Module && target.DeclaringType.RequiresModuleInitialization()) size += 32;
        for (int i = 0; i < body.Args.Length; i++) {
            var argument = site is NewObjInst ? i == 0 ? site : site.Operands[i - 1] : site.Operands[i];
            if (StoreInst.MustBeCoerced(body.Args[i].ResultType, argument.ResultType)) size += 16;
        }
        if (StoreInst.MustBeCoerced(site.ResultType, target.ReturnType)) size += 16;
        return size;
    }

    static long EstimateSize(MethodBody body, bool inlined = false, int resultUses = 1)
    {
        long size = 0;
        foreach (var inst in body.Instructions()) {
            if (inlined && inst is ReturnInst ret) {
                if (ret.HasValue) size += OperandSize(ret.Value) * Math.Max(1, resultUses);
                continue;
            }
            // Account for long opcodes/prefixes, temporary stores and loads.
            // Switch tables are proportional to cases, not distinct targets;
            // phis can require copies and edge-splitting branches per input.
            size += inst switch {
                SwitchInst table => 10 + 4L * table.NumTargets,
                PhiInst phi => 13L * phi.NumArgs,
                _ => 16
            };
            if (inst.HasResult) size += 4;
            foreach (var operand in inst.Operands) {
                size += OperandSize(operand);
            }
        }
        return size;
    }

    static int OperandSize(Value value) => value switch {
        BasicBlock => 0,
        ConstInt { IsInt: true, Value: >= -1 and <= 8 } => 1,
        ConstInt { IsInt: true, Value: >= sbyte.MinValue and <= sbyte.MaxValue } => 2,
        ConstInt { IsInt: true } or ConstFloat { IsSingle: true } or ConstString => 5,
        ConstNull => 1,
        _ => 9
    };
}
