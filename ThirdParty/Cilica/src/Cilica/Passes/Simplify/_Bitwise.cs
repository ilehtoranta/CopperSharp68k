namespace Cilica.Passes;

using System.Numerics;
using Cilica.IR.Utils;

using Bin = IR.BinaryOp;
using Cmp = IR.CompareOp;

public partial class SimplifyInsts
{
    private static Value? FoldBitTestSelect(IRBuilder builder, Value cond, Value ifTrue, Value ifFalse)
    {
        bool trueWhenSet = true;
        ConstInt? compared = null;
        if (cond is CompareInst { Op: Cmp.Eq or Cmp.Ne, Right: ConstInt c } cmp) {
            cond = cmp.Left;
            compared = c;
            trueWhenSet = cmp.Op == Cmp.Ne;
        }
        if (cond is not BinaryInst { Op: Bin.And, Right: ConstInt mask } bits ||
            bits.ResultType.StackType != mask.ResultType.StackType ||
            !BitOperations.IsPow2(mask.UnsignedStackValue)) return null;

        if (compared != null && compared.Value != 0) {
            if (compared.UnsignedStackValue != mask.UnsignedStackValue) return null;
            trueWhenSet = !trueWhenSet;
        }
        if (ifTrue is not ConstInt t || ifFalse is not ConstInt f ||
            t.ResultType.StackType != bits.ResultType.StackType || f.ResultType.StackType != bits.ResultType.StackType ||
            (t.UnsignedStackValue ^ f.UnsignedStackValue) != mask.UnsignedStackValue) return null;

        // The two results differ only in the tested bit. XOR supplies their
        // common bits and also handles an inverted test, bit clear, and toggle.
        var zeroValue = trueWhenSet ? f : t;
        return zeroValue.SignedStackValue == -1 ? builder.CreateNot(bits)
            : builder.CreateXor(bits, zeroValue);
    }
}
