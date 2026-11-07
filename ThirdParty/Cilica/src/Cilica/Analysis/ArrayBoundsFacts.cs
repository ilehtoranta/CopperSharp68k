namespace Cilica.Analysis;

/// <summary> Array extent bounds established by dominating comparison guards. </summary>
internal static class ArrayBoundsFacts
{
    public static bool IsLengthBound(Value bound, Value array, BasicBlock block, DominatorTree dominators, bool exact = false)
    {
        if (LengthSource(bound) == array) return true;

        for (var current = block; ; ) {
            var parent = dominators.IDom(current);
            if (parent == current) return false;
            // A merge can be reached without taking this branch's edge. Only
            // learn a fact from a successor with that edge as its sole entry.
            if (current.NumPreds == 1 && current.Preds.First() == parent &&
                parent.Last is BranchInst { Cond: CompareInst compare } branch &&
                (branch.Then == current || branch.Else == current)) {
                var op = branch.Then == current ? compare.Op : compare.Op.GetNegated();
                if (!exact || op == CompareOp.Eq) {
                    if (SameBound(bound, compare.Left) && LengthSource(compare.Right) == array &&
                        op is CompareOp.Eq or CompareOp.Sle or CompareOp.Slt or CompareOp.Ule or CompareOp.Ult) return true;
                    if (SameBound(bound, compare.Right) && LengthSource(compare.Left) == array &&
                        op is CompareOp.Eq or CompareOp.Sge or CompareOp.Sgt or CompareOp.Uge or CompareOp.Ugt) return true;
                }
            }
            current = parent;
        }
    }

    private static bool SameBound(Value left, Value right)
        => left.Equals(right) || LengthSource(left) is { } source && source == LengthSource(right);

    private static Value? LengthSource(Value value)
    {
        if (value is ConvertInst { CheckOverflow: false, ResultType.Kind: TypeKind.Int32 } conversion) value = conversion.Value;
        return value is CilIntrinsic.ArrayLen length ? length.Args[0] : null;
    }
}
