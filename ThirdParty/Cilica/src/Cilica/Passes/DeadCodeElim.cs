namespace Cilica.Passes;

using Cilica.Analysis;
using Cilica.IR.Utils;

public class DeadCodeElim : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx)
    {
        var changes = MethodInvalidations.None;
        var funcInfo = ctx.Compilation.GetAnalysis<GlobalFunctionEffects>();
        bool trace = ctx.Logger.IsEnabled(LogLevel.Trace);
        int blocksBefore = ctx.Method.NumBlocks;
        int instructionsBefore = trace ? ctx.Method.Instructions().Count() : 0;
        int variablesBefore = trace ? ctx.Method.LocalVars().Count() : 0;

        changes |= RemoveUnreachableBlocks(ctx.Method) ? MethodInvalidations.ControlFlow : 0;
        changes |= RemoveUnusedVars(ctx.Method) ? MethodInvalidations.DataFlow : 0;
        changes |= RemoveUselessCode(ctx.Method, funcInfo) ? MethodInvalidations.DataFlow : 0;

        if (changes != MethodInvalidations.None) {
            ctx.Logger.Trace($"Removed {blocksBefore - ctx.Method.NumBlocks} blocks, {instructionsBefore - ctx.Method.Instructions().Count()} instructions, and {variablesBefore - ctx.Method.LocalVars().Count()} variables.");
        }
        return changes;
    }

    public static bool RemoveUnreachableBlocks(MethodBody method)
    {
        bool changed = ControlFlowUtils.RemoveUnreachableBlocks(method, foldBranches: true);
        bool removedGuards = false;

        // Remove empty try-finally regions.
        foreach (var block in method) {
            if (block.First is GuardInst { Kind: GuardKind.Finally, Next: not GuardInst, HandlerBlock.First: ResumeInst } guard) {
                var regionAnalysis = new ProtectedRegionAnalysis(method); // this is not ideal but this transform should be quite rare

                foreach (var exitBlock in regionAnalysis.GetBlockRegion(block).GetExitBlocks()) {
                    exitBlock.SetBranch(exitBlock.Succs.First());
                }
                guard.Remove();
                removedGuards = true;
            }
        }
        if (removedGuards) ControlFlowUtils.RemoveUnreachableBlocks(method);
        return changed || removedGuards;
    }

    public static bool RemoveUselessCode(MethodBody method, GlobalFunctionEffects? funcInfo)
    {
        var worklist = new DiscreteStack<Instruction>();

        // Marking does not mutate bodies, so effect queries can share validation.
        using (funcInfo?.BeginRead()) {
            foreach (var inst in method.Instructions()) {
                if (inst.SafeToRemove) continue;
                if (funcInfo != null && IsSafeToRemoveCall(inst, funcInfo)) continue;

                // Mark `inst` and its entire dependency chain
                worklist.Push(inst);

                while (worklist.TryPop(out var chainInst)) {
                    foreach (var oper in chainInst.Operands) {
                        if (oper is Instruction operI) {
                            worklist.Push(operI);
                        }
                    }
                }
            }
        }

        // Sweep useless instructions
        bool changed = false;

        foreach (var inst in method.Instructions()) {
            if (!worklist.WasPushed(inst)) {
                inst.Remove();
                changed = true;
            }
            else if (inst is PhiInst phi) {
                RemoveTrivialPhi(phi, peel: true);
                changed |= phi.Block == null;
            }
        }

        return changed;
    }

    private static bool IsSafeToRemoveCall(Instruction inst, GlobalFunctionEffects funcInfo)
    {
        if (inst is not CallInst call || (call.IsVirtual && !call.InBounds)) return false;

        var effects = funcInfo.GetEffects(call);

        return effects.IsPure;
    }

    // Remove phi-webs where all arguments have the same value
    public static void RemoveTrivialPhi(PhiInst phi, bool peel)
    {
        while (true) {
            var firstArg = phi.GetValue(0);

            for (int i = 1; i < phi.NumArgs; i++) {
                var arg = phi.GetValue(i);

                if (!arg.Equals(firstArg) && arg != phi) return;
            }
            phi.ReplaceWith(firstArg);

            if (peel && firstArg is PhiInst nextPhi) {
                phi = nextPhi;
            } else break;
        }
    }

    public static bool RemoveUnusedVars(MethodBody method)
    {
        bool changed = false;

        foreach (var slot in method.LocalVars()) {
            // Remove if unused, or if non-pinned and all uses are from stores
            if (slot.NumUses == 0 || (!slot.IsPinned && slot.Uses().All(u =>
                u.OperIndex == 0 && u.Parent is StoreInst { IsVolatile: false } or CilIntrinsic.MemSet { Flags: PointerFlags.None }))) {
                foreach (var user in slot.Users()) {
                    user.Remove();
                }
                slot.Remove();
                changed = true;
            }
        }
        return changed;
    }
}
