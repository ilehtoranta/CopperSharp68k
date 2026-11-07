namespace Cilica.IR;

public abstract class MemoryInst : Instruction
{
    public Value Address {
        get => _operands[0];
        set => ReplaceOperand(0, value);
    }
    public override bool MayThrow =>
        Address switch {
            AddressInst addr and (FieldAddrInst or ArrayAddrInst)
                => addr.MayThrow || ElemType != addr.ElemType,

            LocalSlot slot
                => ElemType != slot.Type,

            _ => true
        };

    public abstract TypeDesc ElemType { get; }

    public PointerFlags Flags { get => _flags; set => SetProperty(ref _flags, value); }

    private PointerFlags _flags;

    public bool IsUnaligned => (Flags & PointerFlags.Unaligned) != 0;
    public bool IsVolatile => (Flags & PointerFlags.Volatile) != 0;
    public override bool IsMemoryBarrier => IsVolatile;

    protected MemoryInst(PointerFlags flags, params Value[] operands)
        : base(operands)
    {
        Flags = flags;
    }

    protected string FormatName(string name)
    {
        if (IsUnaligned) name += ".un";
        if (IsVolatile) name += ".volatile";
        return name;
    }
}
public class LoadInst : MemoryInst
{
    public override TypeDesc ElemType => ResultType;

    public override string InstName => FormatName("load");
    public override bool MayReadFromMemory => true;

    public LoadInst(Value addr, TypeDesc? elemType = null, PointerFlags flags = 0)
        : base(flags, addr)
    {
        ResultType = elemType ?? ((PointerType)addr.ResultType).ElemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);
}
public class StoreInst : MemoryInst
{
    public Value Value {
        get => _operands[1];
        set => ReplaceOperand(1, value);
    }
    public override TypeDesc ElemType { get; }

    /// <summary> Checks if <see cref="AddressInst.ElemType"/> differs from the value type, ignoring sign/unsigned mismatches. </summary>
    public bool IsCasting {
        get {
            if (MustBeCoerced(ElemType, Value)) return true;
            if (ElemType == Value.ResultType) {
                return false;
            }
            if (ElemType.IsInt() || ElemType.IsPointerLike()) {
                return ElemType.Kind.GetSigned() != Value.ResultType.Kind.GetSigned();
            }
            return true;
        }
    }

    public override string InstName => FormatName("store");
    public override bool HasSideEffects => true;
    public override bool MayWriteToMemory => true;

    public StoreInst(Value addr, Value value, TypeDesc? elemType = null, PointerFlags flags = 0)
        : base(flags, addr, value)
    {
        ElemType = elemType ?? ((PointerType)addr.ResultType).ElemType;
    }

    public override void Accept(InstVisitor visitor) => visitor.Visit(this);

    protected override void PrintOperands(PrintContext ctx)
    {
        base.PrintOperands(ctx);

        if (IsCasting) {
            ctx.Print(" as ", PrintToner.InstName);
            ElemType.Print(ctx);
        }
    }

    /// <summary> Coerces a value as a store/load roundtrip to <paramref name="destType"/> would, including numeric precision and managed-reference conversions. </summary>
    public static Value Coerce(TypeDesc destType, Value val, Instruction insertBefore)
    {
        var conv = CreateCoercion(destType, val);
        if (conv == null) return val;
        conv.InsertBefore(insertBefore);
        return conv;
    }

    internal static Instruction? CreateCoercion(TypeDesc destType, Value value)
    {
        if (!MustBeCoerced(destType, value)) return null;
        return destType.StackType == StackType.ByRef
            ? new CilIntrinsic.RefCast(destType, value)
            : new ConvertInst(value, destType);
    }

    public static bool MustBeCoerced(TypeDesc destType, Value srcValue)
    {
        if (destType.IsPointerLike() && srcValue is ConstInt { IsInt: true, Value: 0 }) {
            return true;
        }

        if (destType.Kind.IsSmallInt() && srcValue is ConstInt cons) {
            return !cons.FitsInType(destType);
        }
        return MustBeCoerced(destType, srcValue.ResultType);
    }

    public static bool MustBeCoerced(TypeDesc destType, TypeDesc srcType)
    {
        // III.1.6 Implicit argument coercion

        return (destType.Kind.IsSmallInt() && (destType.Kind.Size() < srcType.Kind.Size() ||
                   destType.Kind.Size() == srcType.Kind.Size() && destType.Kind.IsSigned() != srcType.Kind.IsSigned())) ||
               (destType.StackType == StackType.Int && srcType.StackType == StackType.NInt) ||
               (destType.StackType == StackType.ByRef && srcType.StackType == StackType.NInt) ||
               (destType.StackType == StackType.NInt && srcType.StackType == StackType.ByRef) ||
               (destType.Kind == TypeKind.Single && srcType.Kind == TypeKind.Double);
    }
}

[Flags]
public enum PointerFlags : byte
{
    None = 0,
    Unaligned   = 1 << 0,
    Volatile    = 1 << 1
}
