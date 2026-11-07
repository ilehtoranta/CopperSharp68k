namespace Cilica.AsmIO;

/// <summary> Represents a type that has an element type (array, pointer, byref) </summary>
public abstract class CompoundType : TypeDesc
{
    public override TypeDesc ElemType { get; }
    /// <summary> Custom modifiers on the element, inside the pointer or array signature. </summary>
    public ImmutableArray<TypeModifier> ElementModifiers { get; }
    public override TypeDesc? BaseType => ElemType.BaseType;
    public override bool IsUnboundGeneric => ElemType.IsUnboundGeneric;

    public override string? Namespace => ElemType.Namespace;
    public override string Name => ElemType.Name + Postfix;

    protected abstract string Postfix { get; }

    public CompoundType(TypeDesc elemType, ImmutableArray<TypeModifier> modifiers = default)
    {
        ElemType = elemType;
        ElementModifiers = modifiers.EmptyIfDefault();
    }
    
    public override TypeDesc GetSpec(GenericContext context)
    {
        var specElemType = ElemType.GetSpec(context);
        return specElemType == ElemType ? this : New(specElemType);
    }
    protected abstract CompoundType New(TypeDesc elemType);

    public override void Print(PrintContext ctx, bool includeNs = false)
    {
        new TypeSig(ElemType, ElementModifiers).Print(ctx, includeNs);
        ctx.Print(Postfix);
    }

    public override int GetHashCode() => HashCode.Combine(Kind, ElemType);
    public override bool Equals(TypeDesc? other) 
        => other != null && other.GetType() == GetType() && ((CompoundType)other).ElemType == ElemType &&
            ElementModifiers.SequenceEqual(((CompoundType)other).ElementModifiers);
}
