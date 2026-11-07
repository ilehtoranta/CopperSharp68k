namespace Cilica.Passes;

using Cilica.IR.Utils;

using Bin = IR.BinaryOp;

public partial class SimplifyInsts
{
    // These identities preserve fixed-width, unchecked integer arithmetic.
    private static Bin? GetDistributiveInnerOp(Bin outer) => outer switch {
        Bin.Add or Bin.Sub => Bin.Mul,
        Bin.And => Bin.Or,
        Bin.Or or Bin.Xor => Bin.And,
        _ => null
    };

    private static Value? SimplifyDistributive(BinaryInst inst)
    {
        if (inst.ResultType.StackType is not (StackType.Int or StackType.Long) ||
            inst.Op is not (Bin.Add or Bin.Sub or Bin.Mul or Bin.And or Bin.Or or Bin.Xor)) return null;

        var builder = new IRBuilder(inst, InsertionDir.Before).SetNextDebugLocation(inst.DebugLocation);

        if (Factor(inst.Op, inst.Left, inst.Right) is { } factored) return factored;

        // Only associative operations may regroup adjacent terms.
        if (inst.IsAssociative) {
            if (Reassociate(inst.Left, inst.Right) is { } left) return left;
            if (Reassociate(inst.Right, inst.Left) is { } right) return right;
        }
        return DistributeConstant(inst, builder);

        Value? Reassociate(Value tree, Value other)
        {
            if (tree is not BinaryInst { NumUses: 1 } nested || nested.Op != inst.Op) return null;

            if (Factor(inst.Op, nested.Right, other) is { } right) {
                return new BinaryInst(inst.Op, nested.Left, right, inst.ResultType);
            }
            if (Factor(inst.Op, nested.Left, other) is { } left) {
                return new BinaryInst(inst.Op, nested.Right, left, inst.ResultType);
            }
            return null;
        }

        Value? Factor(Bin op, Value left, Value right)
        {
            // Factor complemented masks through De Morgan's laws as well.
            if (op is Bin.And or Bin.Or or Bin.Xor &&
                left is UnaryInst { Op: UnaryOp.Not, NumUses: 1, Value: var l } &&
                right is UnaryInst { Op: UnaryOp.Not, NumUses: 1, Value: var r }) {
                var inner = op == Bin.And ? Bin.Or : op == Bin.Or ? Bin.And : Bin.Xor;
                if (Factor(inner, l, r) is { } combinedBits) {
                    return op == Bin.Xor ? combinedBits : builder.CreateNot(combinedBits);
                }
            }
            if (GetDistributiveInnerOp(op) is not { } innerOp ||
                MatchCommonOperand(left, right, innerOp) is not { } match) return null;

            var combined = ConstFolding.FoldBinary(op, match.Left, match.Right);
            if (combined == null) {
                if (left is not BinaryInst { NumUses: 1 } || right is not BinaryInst { NumUses: 1 }) return null;
                combined = builder.CreateBin(op, match.Left, match.Right);
            }
            return builder.CreateBin(innerOp, match.Common, combined);
        }
    }

    private static Value? DistributeConstant(BinaryInst inst, IRBuilder builder)
    {
        if (inst.Right is not ConstInt scale || inst.Left is not BinaryInst { NumUses: 1 } nested ||
            GetDistributiveInnerOp(nested.Op) != inst.Op) return null;

        var (term, constant, constantFirst) = nested switch {
            { Left: ConstInt c, Right: not Const and var value } => (value, c, true),
            { Left: not Const and var value, Right: ConstInt c } => (value, c, false),
            _ => default
        };
        if (constant == null) return null;

        var product = ConstFolding.FoldBinary(inst.Op, constant, scale)!;
        // Expand only when the constant absorbs the other term or is an identity:
        // (x | 1) & 2 -> x & 2, rather than adding another runtime operation.
        var folded = constantFirst
            ? ConstFolding.FoldBinary(nested.Op, product, term)
            : ConstFolding.FoldBinary(nested.Op, term, product);
        return folded == term ? builder.CreateBin(inst.Op, term, scale) : folded;
    }

    private static Value? SimplifyDistributiveSelect(SelectInst inst)
    {
        if (inst.ResultType.StackType is not (StackType.Int or StackType.Long)) return null;

        var builder = new IRBuilder(inst, InsertionDir.Before).SetNextDebugLocation(inst.DebugLocation);
        if (FoldBitTestSelect(builder, inst.Cond, inst.IfTrue, inst.IfFalse) is { } folded) {
            return builder.CreateConvert(folded, inst.ResultType);
        }

        // select(c, x op a, x op b) -> x op select(c, a, b).
        // An unchanged arm supplies the operation's identity element.
        ReadOnlySpan<Bin> ops = [Bin.Add, Bin.Mul, Bin.And, Bin.Or, Bin.Xor];
        foreach (var op in ops) {
            if (MatchCommonOperand(inst.IfTrue, inst.IfFalse, op) is not { } match) continue;

            var selected = ConstFolding.FoldSelect(inst.Cond, match.Left, match.Right)
                ?? FoldBitTestSelect(builder, inst.Cond, match.Left, match.Right);
            if (selected == null) {
                // Factoring must remove an operation if the inner select remains.
                if (inst.IfTrue is not BinaryInst { NumUses: 1 } t || t.Op != op ||
                    inst.IfFalse is not BinaryInst { NumUses: 1 } f || f.Op != op) continue;
                selected = builder.CreateSelect(inst.Cond, match.Left, match.Right,
                    inst.ResultType.StackType.GetPrimType(signed: true));
            }
            return new BinaryInst(op, match.Common, selected, inst.ResultType);
        }
        return null;
    }

    private static (Value Common, Value Left, Value Right)? MatchCommonOperand(Value left, Value right, Bin op)
    {
        if (left is BinaryInst l && l.Op == op) {
            if (MatchOperand(right, l.Left) is { } r1) return (l.Left, l.Right, r1);
            if (MatchOperand(right, l.Right) is { } r2) return (l.Right, l.Left, r2);
        }
        if (right is BinaryInst r && r.Op == op) {
            if (MatchOperand(left, r.Left) is { } l1) return (r.Left, l1, r.Right);
            if (MatchOperand(left, r.Right) is { } l2) return (r.Right, l2, r.Left);
        }
        return null;

        Value? MatchOperand(Value value, Value common)
        {
            if (value is BinaryInst bin && bin.Op == op) {
                if (bin.Left.Equals(common)) return bin.Right;
                if (bin.Right.Equals(common)) return bin.Left;
            }
            long identity = op == Bin.And ? -1 : op == Bin.Mul ? 1 : 0;
            return value.Equals(common)
                ? ConstInt.Create(common.ResultType.StackType.GetPrimType(signed: true), identity)
                : null;
        }
    }
}
