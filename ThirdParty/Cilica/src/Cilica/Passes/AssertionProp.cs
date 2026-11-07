namespace Cilica.Passes;

using Cilica.Analysis;
using Cilica.IR.Utils;

/// <summary> Propagates comparison and nullness facts along dominating control-flow paths. </summary>
public class AssertionProp : IMethodPass
{
    public MethodPassResult Run(MethodTransformContext ctx) => new Worker(ctx).Run();

    sealed class Worker(MethodTransformContext ctx)
    {
        readonly Dictionary<Value, List<Assertion>> _assertions = new();
        readonly Dictionary<BasicBlock, HashSet<BasicBlock>> _exceptionalPaths = new();
        DominatorTree _domTree = null!;

        // Entry facts also hold in handlers. Facts learned while executing a
        // block require normal completion of the instructions preceding them.
        readonly record struct Assertion(CompareOp Op, Value Right, BasicBlock Block, bool AtEntry);

        public MethodPassResult Run()
        {
            long revision = ctx.Method.Revision;
            _domTree = ctx.GetAnalysis<DominatorTree>();
            _domTree.Traverse(preVisit: ProcessBlock);
            return ctx.Method.Revision != revision ? MethodInvalidations.DataFlow : MethodInvalidations.None;
        }

        private void ProcessBlock(BasicBlock block)
        {
            if (block.NumPreds == 1 && TryGetEdgeComparison(block.Preds.First(), block, out var cmp, out var op)) {
                Imply(op, cmp.Left, cmp.Right, block, atEntry: true);
                ReplaceConditionUses(cmp, block, op == cmp.Op);
            }

            foreach (var phi in block.Phis()) {
                if (phi.ResultType.StackType != StackType.Object) continue;

                bool? commonResult = null;
                foreach (var (pred, arg) in phi) {
                    var result = EvaluateOnEdge(CompareOp.Ne, arg, ConstNull.Create(), pred, block);
                    if (result == null || ((commonResult ??= result) != result)) {
                        commonResult = null;
                        break;
                    }
                }
                if (commonResult is bool nonNull) {
                    Imply(nonNull ? CompareOp.Ne : CompareOp.Eq, phi, ConstNull.Create(), block, atEntry: true);
                }
            }

            // Forward instruction order makes local facts available only after
            // their establishing instruction. Phi queries above inspect edges,
            // never facts from later instructions in this block.
            foreach (var inst in block.NonPhis()) {
                if (inst is CompareInst { ResultType.Kind: TypeKind.Bool } compare) {
                    if (Evaluate(compare.Op, compare.Left, compare.Right, block) is bool result) {
                        ctx.Logger.Trace($"Propagated '{compare}' in '{block}' as constant '{result}'.");
                        compare.ReplaceUses(ConstInt.CreateI(result ? 1 : 0));
                    }
                } else if (GetNullCheckedValue(inst) is { } obj) {
                    if (Evaluate(CompareOp.Ne, obj, ConstNull.Create(), block) == true) {
                        switch (inst) {
                            case FieldAddrInst address:
                                if (!address.InBounds) {
                                    ctx.Logger.Trace($"Removed null check in '{block}' by propagating '{address}'.");
                                }
                                address.InBounds = true; break;
                            case CallInst call:
                                if (!call.InBounds) {
                                    ctx.Logger.Trace($"Removed null check in '{block}' by propagating '{call}'.");
                                }
                                call.InBounds = true; break;
                            case CilIntrinsic.NullCheck check:
                                ctx.Logger.Trace($"Removed null check for in '{block}' by propagating '{check}'.");
                                check.Remove(); break;
                        }
                    } else {
                        Imply(CompareOp.Ne, obj, ConstNull.Create(), block, atEntry: false);
                    }
                }
            }
        }

        private static Value? GetNullCheckedValue(Instruction inst)
        {
            // Taking a field address through a byref, calling a value-type
            // method, or accessing empty value-type storage need not check for
            // null. Only operations with an actual object null check qualify.
            var value = inst switch {
                FieldAddrInst { IsInstance: true } address => address.Obj,
                CallInst { IsVirtual: true, IsStatic: false, Constraint: null } call => call.Args[0],
                FuncAddrInst { IsVirtual: true } address => address.Object,
                ArrayAddrInst address => address.Array,
                CilIntrinsic.ArrayLen length => length.Args[0],
                CilIntrinsic.NullCheck check => check.Args[0],
                _ => null
            };
            return value?.ResultType.StackType == StackType.Object ? value : null;
        }

        private void ReplaceConditionUses(CompareInst compare, BasicBlock block, bool result)
        {
            var constant = ConstInt.CreateI(result ? 1 : 0);
            foreach (var use in compare.Uses()) {
                // Phi operands are consumed on their own incoming edges, not
                // in the block containing the phi.
                var useBlock = use.Parent is PhiInst phi ? phi.GetBlock(use.OperIndex / 2) : use.Parent.Block;
                if (_domTree.Dominates(block, useBlock) ||
                    use.Parent is PhiInst { Block: var target } && target == block) {
                    use.Operand = constant;
                    ctx.Logger.Trace($"Propagated '{compare}' in '{block}' as constant '{result}' to '{use.Parent}' in '{useBlock}'.");
                }
            }
        }

        private static bool TryGetEdgeComparison(BasicBlock pred, BasicBlock target, out CompareInst compare, out CompareOp op)
        {
            if (pred.Last is BranchInst { Cond: CompareInst cmp } branch &&
                (branch.Then == target || branch.Else == target)) {
                compare = cmp;
                op = branch.Then == target ? cmp.Op : cmp.Op.GetNegated();
                return true;
            }
            // A guard also creates CFG edges, but its handlers and filters do
            // not imply anything about the block's terminating branch.
            compare = null!;
            op = default;
            return false;
        }

        private bool? EvaluateOnEdge(CompareOp op, Value left, Value right, BasicBlock pred, BasicBlock target)
        {
            if (TryGetEdgeComparison(pred, target, out var compare, out var edgeOp)) {
                var edgeLeft = compare.Left;
                var edgeRight = compare.Right;
                if (edgeLeft.Equals(right) && edgeRight.Equals(left)) {
                    (edgeLeft, edgeRight) = (edgeRight, edgeLeft);
                    edgeOp = edgeOp.GetSwapped();
                }
                if (edgeLeft.Equals(left) && edgeRight.Equals(right) && EvaluatePredicate(edgeOp, op) is bool result) {
                    return result;
                }
            }
            bool exceptional = pred.Guards().Any(g => g.HandlerBlock == target || g.FilterBlock == target);
            return Evaluate(op, left, right, pred, atEntry: exceptional);
        }

        private void Imply(CompareOp op, Value left, Value right, BasicBlock block, bool atEntry)
        {
            Assert(op, left, right);
            Assert(op.GetSwapped(), right, left);

            void Assert(CompareOp predicate, Value value, Value other)
            {
                if (value is not TrackedValue) return;
                var list = _assertions.GetOrAddRef(value) ??= new();

                // Bound lookup cost; forgetting a fact only loses optimization.
                if (list.Count == 16) list.RemoveAt(0);
                list.Add(new(predicate, other, block, atEntry));
            }
        }

        private bool? Evaluate(CompareOp op, Value left, Value right, BasicBlock block, bool atEntry = false)
        {
            return EvaluateRelated(left, right, op) ?? EvaluateRelated(right, left, op.GetSwapped()) ??
                (ConstFolding.FoldCompare(op, left, right) is ConstInt constant ? constant.Value != 0 : null);

            bool? EvaluateRelated(Value value, Value other, CompareOp predicate)
            {
                if (!_assertions.TryGetValue(value, out var assertions)) return null;
                for (int i = assertions.Count - 1; i >= 0; i--) {
                    var assertion = assertions[i];
                    if (!assertion.Right.Equals(other) || !IsAvailable(assertion, block, atEntry)) continue;

                    if (EvaluatePredicate(assertion.Op, predicate) is bool result) return result;
                }
                return null;
            }
        }

        private bool IsAvailable(Assertion assertion, BasicBlock block, bool atEntry)
        {
            if (!_domTree.Dominates(assertion.Block, block)) return false;
            if (assertion.AtEntry) return true;
            if (assertion.Block == block) return !atEntry;
            if (assertion.Block.First is not GuardInst) return true;

            if (!_exceptionalPaths.TryGetValue(assertion.Block, out var reachable)) {
                reachable = new();
                _exceptionalPaths.Add(assertion.Block, reachable);
                var pending = new Stack<BasicBlock>();
                foreach (var guard in assertion.Block.Guards()) {
                    pending.Push(guard.HandlerBlock);
                    if (guard.HasFilter) pending.Push(guard.FilterBlock);
                }
                while (pending.TryPop(out var current)) {
                    // Re-entry executes the establishing instruction again.
                    if (current == assertion.Block || !reachable.Add(current)) continue;
                    foreach (var successor in current.Succs) pending.Push(successor);
                }
            }
            // Include finally/fault/filter paths and continuations after a
            // caught exception, not just handler entry blocks.
            return !reachable.Contains(block);
        }

        private static bool? EvaluatePredicate(CompareOp known, CompareOp tested)
        {
            if (Implies(known, tested)) return true;
            if (Implies(known, tested.GetNegated())) return false;
            return null;

            // Do not mix signed/unsigned or ordered/unordered predicates.
            static bool Implies(CompareOp a, CompareOp b)
                => a == b || a.IsStrict() && a == b.GetStrict();
        }
    }
}
