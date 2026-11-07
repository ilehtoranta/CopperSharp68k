namespace Cilica.Passes;

using Cilica.IR.Utils;

using Bin = IR.BinaryOp;
using Cmp = IR.CompareOp;

/// <summary> Implements peepholes/combining/scalar transforms that don't affect control flow. </summary>
public partial class SimplifyInsts : IMethodPass
{
    readonly TypeDefOrSpec t_Delegate, t_DictionaryOfTT;
    // The inliner uses this to retain recursion ancestry through call rewrites.
    internal Action<CallInst, CallInst>? OnCallReplaced { get; init; }

    public SimplifyInsts(ModuleResolver resolver)
    {
        t_Delegate = resolver.Import(typeof(Delegate));
        t_DictionaryOfTT = resolver.Import(typeof(Dictionary<,>));
    }

    static IMethodPass IMethodPass.Create<TSelf>(Compilation comp)
        => new SimplifyInsts(comp.Module.Resolver);

    public MethodPassResult Run(MethodTransformContext ctx)
    {
        long revision = ctx.Method.Revision;

        foreach (var inst in ctx.Method.Instructions()) {
            var oldInst = inst.ToString();
            var newValue = inst switch {
                BinaryInst c    => SimplifyBinary(c),
                CompareInst c   => SimplifyCompare(c),
                UnaryInst c     => SimplifyUnary(c),
                ConvertInst c   => SimplifyConvert(c),
                CallInst c      => SimplifyCall(c, ctx.Logger),
                IntrinsicInst c => SimplifyIntrinsic(c),
                SelectInst c    => SimplifySelect(c),
                _ => null
            };
            if (newValue != null) {
                inst.ReplaceWith(newValue, insertIfInst: true);
                ctx.Logger.Trace($"Simplified '{oldInst}' to '{inst}'.");
            }
        }

        return ctx.Method.Revision != revision ? MethodInvalidations.DataFlow : MethodInvalidations.None;
    }

    private Value? SimplifyCall(CallInst call, ICompilationLogger logger)
    {
        var method = call.Method;
        var declType = method.DeclaringType;

        bool changed = method.Name switch {
            "Invoke" when declType.Inherits(t_Delegate)
                => DevirtualizeLambda(call, logger),

            "ContainsKey" when declType is TypeDefOrSpec s && s.Definition == t_DictionaryOfTT
                => SimplifyDictLookup(call),

            _ => false
        };

        return changed ? null : ConstFolding.FoldCall(call.Method, call.Args);
    }

    private Value? SimplifyIntrinsic(IntrinsicInst inst)
    {
        if (inst is CilIntrinsic.NullCheck check && IsRedundantNullCheck(check)) {
            check.Remove();
            return null;
        }

        // unbox_obj(T, box(T, value))  ->  value
        // unbox_ref(T, box(T, value))  ->  store $tmp, value;  $tmp
        if (inst is CilIntrinsic.UnboxObj or CilIntrinsic.UnboxRef && inst.Args[0] is CilIntrinsic.Box box && box.NumUses == 1 &&
            (inst is not CilIntrinsic.UnboxRef || !ScalarReplacement.AddressEscapes(inst))) {
            var unboxType = inst is CilIntrinsic.UnboxRef ? inst.ResultType.ElemType : inst.ResultType;

            if (box.Args[0].ResultType == box.SourceType && box.SourceType == unboxType) {
                if (inst is CilIntrinsic.UnboxRef) {
                    var slot = inst.Block.Method.CreateVar(box.SourceType, "boxFoldCopy");
                    var copy = new StoreInst(slot, box.Args[0]);
                    // Initialize at the original boxing point. The unbox may
                    // execute repeatedly and mutate the same boxed value.
                    copy.InsertBefore(box);
                    box.Remove();
                    return slot;
                } else {
                    box.Remove();
                    return box.Args[0];
                }
            }
        }
        return ConstFolding.FoldIntrinsic(inst);
    }

    private static bool IsRedundantNullCheck(CilIntrinsic.NullCheck check)
    {
        var receiver = check.Args[0];

        if (ConstFolding.FoldCondition(receiver) == true) {
            return true;
        }

        for (var next = check.Next; next != null; next = next.Next) {
            bool dereferencesReceiver = next switch {
                FieldAddrInst { IsInstance: true, InBounds: false } address => address.Obj == receiver,
                ArrayAddrInst { InBounds: false } address => address.Array == receiver,
                CilIntrinsic.ArrayLen length => length.Args[0] == receiver,
                CallInst { IsVirtual: true, IsStatic: false, InBounds: false, Constraint: null } call => call.Args[0] == receiver,
                CilIntrinsic.NullCheck other => other.Args[0] == receiver,
                _ => false
            };

            if (dereferencesReceiver) {
                return true;
            }

            if (next.HasSideEffects || next.MayReadFromMemory || next is GuardInst) {
                return false;
            }
        }

        return false;
    }

    private Value? SimplifyBinary(BinaryInst inst)
    {
        // (const op x)  ->  (x op const), if op is commutative
        if (inst is { Left: Const, Right: not Const, IsCommutative: true } b) {
            (b.Left, b.Right) = (b.Right, b.Left);
        }
        // ((x op const) op const)  ->  (x op (const op const)), if op is associative
        if (inst is {
            Left: BinaryInst { Left: not Const, Right: Const } l_nc_c,
            Right: Const,
            IsAssociative: true
        } &&
            l_nc_c.Op == inst.Op &&
            ConstFolding.FoldBinary(inst.Op, l_nc_c.Right, inst.Right) is Value lr_op_r
        ) {
            inst.Left = l_nc_c.Left;
            inst.Right = lr_op_r;
        }
        if (ConstFolding.FoldBinary(inst.Op, inst.Left, inst.Right) is { } folded) {
            return folded;
        }
        if (SimplifyDistributive(inst) is { } distributed) {
            return distributed;
        }
        if (inst.ResultType.IsPointerLike() && SimplifyAddress(inst) is { } addr) {
            return addr;
        }
        return null;
    }

    private Value? SimplifyCompare(CompareInst inst)
    {
        // (const op x)  ->  (x swapped_op const)
        // (x op cmp)    ->  (cmp op x)
        if (inst is { Left: Const, Right: not Const } or { Left: not CompareInst, Right: CompareInst }) {
            inst.Op = inst.Op.GetSwapped();
            (inst.Left, inst.Right) = (inst.Right, inst.Left);
        }
        // ((x op y) == 0)  ->  (x !op y)
        // ((x op y) != 0)  ->  (x op y)
        // ((x op y) == 1)  ->  (x op y)
        // ((x op y) != 1)  ->  (x !op y)
        if (inst is {
            Op: Cmp.Eq or Cmp.Ne,
            Left: CompareInst { NumUses: 1 } lhs,
            Right: (ConstInt or ConstNull) and var rhs
        }) {
            bool predNe = inst.Op == Cmp.Ne;
            bool isRhsOne = rhs is ConstInt { Value: not 0 };

            if (rhs is ConstInt { Value: not (0 or 1) } ) {
                return ConstInt.CreateI(predNe ? 1 : 0);
            }
            if (predNe == isRhsOne) {
                lhs.Op = lhs.Op.GetNegated();
            }
            return lhs;
        }
        return ConstFolding.FoldCompare(inst.Op, inst.Left, inst.Right);
    }

    private Value? SimplifyUnary(UnaryInst inst)
    {
        // -(-x)  ->  x
        if (inst.Value is UnaryInst sub && sub.Op == inst.Op && 
            inst.Op is UnaryOp.Neg or UnaryOp.Not or UnaryOp.FNeg
        ) {
            return sub.Value;
        }
        return ConstFolding.FoldUnary(inst.Op, inst.Value);
    }

    private Value? SimplifyConvert(ConvertInst inst)
    {
        return ConstFolding.FoldConvert(inst.Value, inst.ResultType, inst.CheckOverflow, inst.SrcUnsigned);
    }

    private Value? SimplifySelect(SelectInst inst)
    {
        return ConstFolding.FoldSelect(inst.Cond, inst.IfTrue, inst.IfFalse) ?? SimplifyDistributiveSelect(inst);
    }

    // Fold native-width offset arithmetic without changing intermediate conversions.
    private static Value? SimplifyAddress(BinaryInst? inst)
    {
        if (!IRMatcher.Add(inst, out var basePtr, out var disp)) return null;

        // Only native-width multiplication has pointer-offset arithmetic.
        // Keep conversions and narrower products intact: they may sign-extend,
        // zero-extend, truncate, overflow, or throw before the pointer addition.
        if (disp.ResultType.StackType == StackType.NInt && IRMatcher.Mul(disp, out var index, out var stride)) {
            if (stride is CilIntrinsic.SizeOf sz) {
                return new PtrOffsetInst(basePtr, index, sz.ObjType);
            } else if (stride is ConstInt { Value: > 0 and <= int.MaxValue } cstride) {
                return new PtrOffsetInst(basePtr, index, (int)cstride.Value);
            }
        }
        return new PtrOffsetInst(basePtr, disp, stride: 1);
    }
}
