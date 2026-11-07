namespace Cilica.Passes;

using System.Reflection;
using MethodBody = IR.MethodBody;

using Cilica.IR.Utils;
using Cilica.Analysis;

public class InlineMethods : IMethodPass
{
    readonly Options _opts;

    public InlineMethods(Options? opts = null)
    {
        _opts = opts ?? new();
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new InlineMethods();

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        if (!ctx.Compilation.Settings.EnableInlining) return MethodInvalidations.None;
        long initialRevision = ctx.Method.Revision;
        var root = ImmutableArray.Create(ctx.Definition);
        var ancestry = new Dictionary<CallInst, ImmutableArray<MethodDef>>();
        var worklist = new Queue<CallInst>();
        var decisions = ctx.Logger.IsEnabled(LogLevel.Trace) ? new DecisionTree() : null;
        EnqueueCalls();

        var advisor = ctx.Compilation.GetAnalysis<InliningAdvisor>();
        var ownMetrics = advisor.GetMetrics(ctx.Method).Metrics;
        int budget = _opts.InitialBudget + (int)(ownMetrics == null ? 0 : ownMetrics.BaseCost * _opts.BudgetFactor);
        int expansions = 0;
        var sizeBudget = new InliningSizeBudget(ctx, _opts.MaxExpandedInstructions);
        var devirtualize = new Devirtualize();
        var simplify = new SimplifyInsts(ctx.Compilation.Resolver) {
            OnCallReplaced = (previous, replacement) => {
                ancestry[replacement] = ancestry.GetValueOrDefault(previous, root);
                decisions?.Replace(previous, replacement);
            }
        };

        // Keep one budget and each call's ancestry across the whole fixed point.
        // A new factory result can make a previously rejected consumer inlinable.
        while (true) {
            long revision = ctx.Method.Revision;
            PassRunner.Run(devirtualize, ctx);

            while (HasBudget() && worklist.TryDequeue(out var call)) {
                var ancestors = ancestry[call];
                if (call.Block?.Method == null) continue;
                if (call.IsTailCall || call.IsReadOnly || ancestors.Length >= _opts.MaxDepth) {
                    decisions?.Record(call, call.Method, call.IsTailCall ? "tail-call semantics must be preserved"
                        : call.IsReadOnly ? "readonly receiver semantics must be preserved"
                        : $"maximum inline depth {_opts.MaxDepth} reached");
                    continue;
                }

                var inlinee = ResolveTarget(ctx, advisor, call, out var rejection);
                var target = inlinee?.Definition;
                if (target == null || ancestors.Contains(target)) {
                    decisions?.Record(call, inlinee ?? call.Method, target == null ? rejection : "recursive inline ancestry");
                    continue;
                }

                int cost = advisor.EvaluateInliningCost(target.Body!, call.Args);
                if (cost > _opts.CostThreshold || cost > budget) {
                    decisions?.Record(call, inlinee!, cost == int.MaxValue ? advisor.GetMetrics(target.Body!).Rejection.ToString()
                        : $"cost {cost} exceeds limit {Math.Min(_opts.CostThreshold, budget)}");
                    continue;
                }
                if (!sizeBudget.AllowsExpansion(call, inlinee!, probe => {
                    var clonedCall = (CallInst)probe;
                    var value = Inline(clonedCall, inlinee!, clonedCall.Args, ctx.Compilation);
                    if (value != null) clonedCall.ReplaceUses(value);
                    clonedCall.Remove();
                }, out rejection)) {
                    decisions?.Record(call, inlinee!, rejection);
                    continue;
                }

                budget -= Math.Max(cost, -_opts.NegativeCostLimit);
                expansions++;

                var chain = ancestors.Add(target);
                var result = Inline(call, inlinee!, call.Args, ctx.Compilation, onNewCall: nested => {
                    ancestry[nested] = chain;
                    decisions?.Register(nested, call);
                    worklist.Enqueue(nested);
                });
                if (result != null) call.ReplaceUses(result);
                var continuation = call.Block;
                call.Remove();
                if (continuation != ctx.Method.EntryBlock && continuation.NumPreds == 0) {
                    // Non-returning specializations disconnect the continuation and its queued calls.
                    ControlFlowUtils.RemoveUnreachableBlocks(ctx.Method);
                }
                sizeBudget.RecordExpansion();
                decisions?.Record(call, inlinee!, rejection: null);
            }

            if (ctx.Method.Revision == revision) break;
            // Expose concrete receivers and delegate targets hidden by the
            // locals, return phis, and branches introduced by inlining.
            InliningSizeBudget.Cleanup(ctx, simplify);
            if (!HasBudget()) {
                PassRunner.Run(devirtualize, ctx);
                break;
            }
            EnqueueCalls();
        }

        decisions?.Write(ctx.Logger, budget <= 0 ? "inline cost budget exhausted"
            : expansions >= _opts.MaxExpansions ? $"maximum inline expansion count {_opts.MaxExpansions} reached"
            : !sizeBudget.HasBudget ? "instruction cloning budget exhausted" : null);
        if (ctx.Method.Revision == initialRevision) return MethodInvalidations.None;
        advisor.InvalidateMetrics(ctx.Method);
        return MethodInvalidations.Everything;

        bool HasBudget() => budget > 0 && expansions < _opts.MaxExpansions && sizeBudget.HasBudget;

        void EnqueueCalls()
        {
            worklist.Clear();
            foreach (var call in ctx.Method.Instructions().OfType<CallInst>()) {
                ancestry.TryAdd(call, root);
                decisions?.Register(call);
                worklist.Enqueue(call);
            }
        }
    }

    private static MethodDefOrSpec? ResolveTarget(MethodTransformContext ctx, InliningAdvisor advisor, CallInst call, out string? rejection)
    {
        rejection = null;
        Devirtualize.TryDevirtualize(ctx, call);
        // Must be a non-recursive MethodDef
        var parent = ctx.Method.Definition;

        if (call.Method is not MethodDefOrSpec inlinee || inlinee.Definition == parent) {
            rejection = call.Method is MethodDefOrSpec ? "recursive call" : "method definition is unavailable";
            return null;
        }
        var target = inlinee.Definition;
        if (target.Module != ctx.Compilation.Module && !ctx.Compilation.Settings.AllowCrossAssemblyIPO) {
            rejection = "cross-assembly optimization is disabled";
            return null;
        }

        // Static constraints must resolve before inlining; a default body may be overridden.
        // Instance constraints require an exact value-type target with the same receiver convention.
        if (call.Constraint is { } constraint &&
            (call.IsStatic || !constraint.IsValueType || constraint != inlinee.DeclaringType)) {
            rejection = "constrained dispatch is unresolved or requires an incompatible receiver";
            return null;
        }

        // Known virtual target
        if (call.IsVirtual && target.Attribs.HasFlag(MethodAttributes.Virtual) && !target.Attribs.HasFlag(MethodAttributes.Final)) {
            if (Devirtualize.ResolveTarget(ctx, call) is not MethodDefOrSpec actualTarget || actualTarget == parent) {
                rejection = "virtual dispatch has no known non-recursive target";
                return null;
            }
            if (actualTarget.DeclaringType.IsValueType && call.Args[0].ResultType.StackType != StackType.ByRef) {
                rejection = "value-type target requires a byref receiver";
                return null;
            }
            // CoreLib cannot persist a devirtualization that needs access-check
            // suppression. It can still inline the target if its body is safe.
            if (ctx.Compilation.Module != ctx.Compilation.Resolver.CoreLib ||
                new InliningAccess(parent.DeclaringType).CanUseMember(actualTarget)) {
                call.Method = actualTarget;
            }
            inlinee = actualTarget;
            target = actualTarget.Definition;
        }

        if (ctx.Compilation.HasFailed(target)) {
            rejection = "target failed compilation";
            return null;
        }

        var eligibility = advisor.EarlyCheck(target);
        if (eligibility != InlineRejectReason.Accepted) {
            rejection = $"{eligibility} ({target.ImplAttribs})";
            return null;
        }

        // If there's no body after devirt, there's no way we can inline this method.
        if (target.Body == null && advisor.GetMethodBodyForIPO(target) == null) {
            rejection = "body unavailable under the inline import policy";
            return null;
        }
        if (!ctx.Compilation.CanInlineBody(parent, inlinee)) {
            rejection = "body requires its original frame, exception policy, or member access context";
            return null;
        }
        // CIL filters cannot contain nested exception regions. Keep a call to an
        // EH-bearing method here instead of cloning its try/handler into a filter.
        if (target.Body!.Instructions().Any(inst => inst is GuardInst)) {
            var region = new ProtectedRegionAnalysis(ctx.Method).GetBlockRegion(call.Block);
            if (region.GetHandlerGuard() is { HasFilter: true } guard && guard.FilterBlock == region.StartBlock) {
                rejection = "exception regions cannot be nested in a filter";
                return null;
            }
        }
        return inlinee;
    }

    public static Value? Inline(Instruction call, MethodDefOrSpec target, ReadOnlySpan<Value> args, Compilation comp, Action<CallInst>? onNewCall = null)
    {
        var callerBody = call.Block.Method;
        var targetBody = Ensure.NotNull(target.Definition.Body);
        Ensure.That(callerBody != targetBody, "Cannot inline method into itself");

        var builder = new IRBuilder(call, InsertionDir.Before);
        if (call is CallInst { IsVirtual: true, InBounds: false } && args[0].ResultType.IsManagedObject()) {
            builder.CreateNullCheck(args[0]);
        }
        comp.EmitInlineInitialization(builder, target);

        var cloner = new InlinerIRCloner(callerBody, target, onNewCall);

        // Add argument mappings
        for (int i = 0; i < targetBody.Args.Length; i++) {
            var arg = targetBody.Args[i];
            var val = StoreInst.Coerce(arg.ResultType, args[i], insertBefore: call);
            cloner.AddMapping(arg, val);
        }

        // Clone blocks
        var returningBlocks = new List<BasicBlock>();
        var lastBlock = call.Block;

        foreach (var block in targetBody) {
            var newBlock = callerBody.CreateBlock(insertAfter: lastBlock);
            cloner.AddMapping(block, newBlock);
            lastBlock = newBlock;

            if (block.Last is ReturnInst) {
                returningBlocks.Add(newBlock);
            }
        }

        cloner.Run(targetBody.EntryBlock);
        returningBlocks.RemoveAll(cloner.IsDead);

        // If the target only has a single block ending with return,
        // the entire code can be moved at once without creating new blocks.
        var result = targetBody.NumBlocks == 1 && returningBlocks.Count == 1
            ? InlineOneBlock(call, returningBlocks[0])
            : InlineManyBlocks(call, cloner.GetMapping(targetBody.EntryBlock), returningBlocks);

        if (result != null) {
            // After inlining, `call` will be at the start of the continuation block.
            return result;
        } else if (target.ReturnType != PrimType.Void) {
            // This could happen if we inline a method that ends with ThrowInst, but it still returns something.
            // Code after `call` should be unreachable now, but we need to replace its uses with undef to avoid making the IR invalid.
            return new Undef(call.ResultType);
        }
        return null;
    }

    private static Value? InlineOneBlock(Instruction call, BasicBlock block)
    {
        var ret = (ReturnInst)block.Last;

        // Move code (if not a single return)
        if (ret.Prev != null) {
            block.MoveRange(call.Block, call.Prev, block.First, ret.Prev);
        }
        block.Remove();
        
        if (ret.Value != null) {
            return StoreInst.Coerce(call.ResultType, ret.Value, insertBefore: call);
        }
        return null;
    }

    private static Value? InlineManyBlocks(Instruction call, BasicBlock entryBlock, List<BasicBlock> returningBlocks)
    {
        var startBlock = call.Block;
        var endBlock = startBlock.Split(call, branchTo: entryBlock);

        var results = new PhiArg[returningBlocks.Count];
        int resultIdx = 0;

        // Rewrite exit blocks to jump into endBlock
        foreach (var block in returningBlocks) {
            if (block.Last is ReturnInst { HasValue: true } ret) {
                var result = StoreInst.Coerce(call.ResultType, ret.Value, insertBefore: ret);
                results[resultIdx++] = (block, result);
            }
            block.SetBranch(endBlock);
        }

        // Return the generated value
        if (resultIdx >= 2) {
            return endBlock.InsertPhi(new PhiInst(call.ResultType, results));
        }
        return results.FirstOrDefault().Value;
    }

    sealed class InlinerIRCloner(MethodBody caller, MethodDefOrSpec target, Action<CallInst>? onNewCall)
        : IRCloner(caller, new GenericContext(target))
    {
        protected override Value CreateClone(Instruction inst)
        {
            var clonedVal = base.CreateClone(inst);

            // Recursive calls also need their ancestry recorded so later
            // fixed-point rounds cannot mistake them for fresh root calls.
            if (clonedVal is CallInst call) onNewCall?.Invoke(call);
            return clonedVal;
        }
    }

    sealed class DecisionTree
    {
        readonly Dictionary<CallInst, Node> _nodes = new();
        readonly List<Node> _roots = new();

        public void Register(CallInst call, CallInst? parent = null)
        {
            if (_nodes.ContainsKey(call)) return;
            var node = new Node(call);
            _nodes.Add(call, node);
            (parent == null ? _roots : _nodes[parent].Children).Add(node);
        }

        public void Replace(CallInst previous, CallInst replacement)
        {
            if (_nodes.Remove(previous, out var node)) {
                node.Call = replacement;
                node.Decision = null;
                _nodes.Add(replacement, node);
            } else {
                Register(replacement);
            }
        }

        public void Record(CallInst call, MethodDesc target, string? rejection)
            => _nodes[call].Decision = new(target, rejection);

        public void Write(ICompilationLogger logger, string? budgetRejection)
        {
            var text = new StringBuilder();
            foreach (var root in _roots) Append(root, 0);
            if (text.Length != 0) {
                text.Length -= Environment.NewLine.Length;
                logger.Trace($"{text}");
            }

            void Append(Node node, int depth)
            {
                var decision = node.Decision;
                if (decision == null) {
                    if (budgetRejection == null || node.Call.Block == null) return;
                    decision = new(node.Call.Method, budgetRejection);
                }
                text.Append(' ', depth * 2).Append(decision.Rejection == null ? "[INLINE] '" : "[FAIL] '")
                    .Append(decision.Target).Append('\'');
                if (decision.Rejection != null) text.Append(": ").Append(decision.Rejection);
                text.AppendLine();
                foreach (var child in node.Children) Append(child, depth + 1);
            }
        }

        sealed record Decision(MethodDesc Target, string? Rejection);

        sealed class Node(CallInst call)
        {
            public CallInst Call = call;
            public Decision? Decision;
            public List<Node> Children = new();
        }
    }

    public class Options
    {
        public int MaxDepth { get; init; } = 16;

        public int MaxExpansions { get; init; } = 128;

        /// <summary> Instruction cloning budget, including speculative size checks. </summary>
        public int MaxExpandedInstructions { get; init; } = InliningSizeBudget.DefaultInstructionBudget;

        /// <summary> Maximum profitability cost of an inline candidate. </summary>
        public int CostThreshold { get; init; } = 250;

        /// <summary> Initial inlining budget. </summary>
        public int InitialBudget { get; init; } = 80;

        /// <summary> A factor used to derive inlining budget from method's own cost. </summary>
        public double BudgetFactor { get; init; } = 0.5;

        /// <summary> For inlinees with negative cost, sets the limit to increase inlining budget. </summary>
        public int NegativeCostLimit { get; init; } = 50;
    }
}
