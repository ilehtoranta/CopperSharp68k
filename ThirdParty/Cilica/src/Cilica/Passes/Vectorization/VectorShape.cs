namespace Cilica.Passes.Vectorization;

internal readonly struct VectorShape : IEquatable<VectorShape>
{
    public TypeKind ElemKind { get; }

    public int Count { get; }

    public int BitWidth => ElemKind.BitSize() * Count;

    public TypeDesc ElemType => PrimType.GetFromKind(ElemKind);

    public VectorShape(TypeKind elemKind, int count)
    {
        ElemKind = elemKind == TypeKind.Char ? TypeKind.UInt16 : elemKind;
        Count = count;
    }

    public VectorShape(TypeDesc elemType, int count)
        : this(elemType.Kind, count) { }

    public static bool IsSupportedElemType(TypeDesc elemType)
    {
        return elemType.Kind is (>= TypeKind.SByte and <= TypeKind.Double) or TypeKind.Char;
    }

    public bool Equals(VectorShape other) => other.Count == Count && other.ElemKind == ElemKind;

    public override bool Equals(object? obj) => obj is VectorShape vt && Equals(vt);

    public override int GetHashCode() => Count * 10000 + (int)ElemKind;

    public override string ToString() => $"Vector{BitWidth}<{ElemKind}>";
}
