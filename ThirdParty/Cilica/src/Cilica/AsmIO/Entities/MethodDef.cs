namespace Cilica.AsmIO;

using System.Collections;
using System.Reflection;
using System.Reflection.Metadata;

/// <summary> Base class for all method entities. </summary>
public abstract class MethodDesc : MemberDesc
{
    public abstract MethodAttributes Attribs { get; set; }
    public abstract MethodImplAttributes ImplAttribs { get; set; }
    public virtual CallConvention CallConv { get; set; }

    public abstract TypeSig ReturnSig { get; }
    public abstract IReadOnlyList<TypeSig> ParamSig { get; }
    public abstract IReadOnlyList<TypeDesc> GenericParams { get; }

    public bool IsStatic => (Attribs & MethodAttributes.Static) != 0;
    public bool IsInstance => !IsStatic;
    public bool IsGeneric => GenericParams.Count > 0;
    public bool IsPublic => (Attribs & MethodAttributes.MemberAccessMask) == MethodAttributes.Public;

    /// <summary> Whether the runtime rewrites this method's async body and return convention. </summary>
    // MethodImplAttributes.Async is not available in our target framework yet.
    public bool IsRuntimeAsync => (ImplAttribs & (MethodImplAttributes)0x2000) != 0;

    public bool IsConstructor => IsSpecialMethod(".ctor");
    public bool IsStaticConstructor => IsSpecialMethod(".cctor");

    public bool IsSpecialName => Attribs.HasFlag(MethodAttributes.SpecialName);

    public bool IsDestructor => Name == "Finalize";

    public TypeDesc ReturnType => ReturnSig.Type;

    private bool IsSpecialMethod(string name)
    {
        return Name == name && Attribs.HasFlag(MethodAttributes.SpecialName);
    }

    public override void Print(PrintContext ctx)
    {
        if (IsStatic) ctx.Print("static ", PrintToner.Keyword);
        ReturnSig.Print(ctx);
        ctx.Print($" {DeclaringType}::{PrintToner.MethodName}{Name}");
        if (IsGeneric) {
            ctx.PrintSequence("<", ">", GenericParams, ctx.Print);
        }
        ctx.PrintSequence("(", ")", ParamSig, p => p.Print(ctx));
    }

    /// <summary> Binds generic type parameters using the given context. </summary>
    /// <remarks>
    /// If neither the method nor its declaring type are generic, or if the
    /// context is empty, the current instance may be returned unchanged. 
    /// </remarks>
    public abstract MethodDesc GetSpec(GenericContext ctx);

    /// <summary> Creates a new generic method specialization with the given type arguments. </summary>
    /// <exception cref="InvalidOperationException"> If this is not a generic method. </exception>
    public virtual MethodDesc GetSpec(ImmutableArray<TypeDesc> genArgs) => throw new InvalidOperationException();
}
public abstract class MethodDefOrSpec : MethodDesc, ModuleEntity
{
    /// <summary> Returns the parent definition if this is a MethodSpec, or the current instance if already a MethodDef. </summary>
    public abstract MethodDef Definition { get; }
    public ModuleDef Module => Definition.DeclaringType.Module;

    public abstract override TypeDefOrSpec DeclaringType { get; }

    public virtual IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => Definition.GetCustomAttribs(readOnly);

    public override MethodDefOrSpec GetSpec(GenericContext ctx)
    {
        if (!IsGeneric && !DeclaringType.IsGeneric) return this;
        
        var newParent = DeclaringType.GetSpec(ctx);
        bool hasChangedParams = ctx.TryFillParams(GenericParams, out var genArgs);

        if (hasChangedParams || newParent != DeclaringType) {
            if (!hasChangedParams && this is not MethodSpec { IsBoundGeneric: true }) {
                // Return cached spec for unbound spec
                return ((TypeSpec)newParent).GetMapping(Definition);
            }
            return new MethodSpec(newParent, Definition, genArgs);
        }
        return this;
    }
    public override MethodSpec GetSpec(ImmutableArray<TypeDesc> genArgs)
    {
        Ensure.That(IsGeneric && genArgs.Length == GenericParams.Count);
        return new MethodSpec(DeclaringType, Definition, genArgs);
    }
}
public class MethodDef : MethodDefOrSpec
{
    public override MethodDef Definition => this;
    public override TypeDef DeclaringType { get; }
    public override string Name { get; set; }

    public override MethodAttributes Attribs { get; set; }
    public override MethodImplAttributes ImplAttribs { get; set; }

    public override TypeSig ReturnSig => ReturnParam.Sig;

    private ParamSigProxyList? _paramSig;
    public override IReadOnlyList<TypeSig> ParamSig => _paramSig ??= new() { Method = this };

    GenericParamType[] _genericParams;
    public override GenericParamType[] GenericParams => _genericParams;

    /// <summary> Placeholder for the <c>return</c> parameter, this contains the signature and custom attributes. </summary>
    public ParamDef ReturnParam { get; }

    public ImmutableArray<ParamDef> Params { get; }

    /// <summary> Returns a view over <see cref="Params"/>, excluding the instance parameter if this is not a static method. </summary>
    public ReadOnlySpan<ParamDef> StaticParams => Params.AsSpan(IsStatic ? 0 : 1);

    public ImportDesc? ImportInfo { get; set; }

    private ILMethodBody? _ilBody;
    public ILMethodBody? ILBody {
        get => _ilBody ??= (_bodyRva != 0 ? new ILMethodBody(Module._loader!, _bodyRva) : null);
        set => _ilBody = value;
    }
    public IR.MethodBody? Body { get; set; }

    internal int _bodyRva;
    internal MethodDefinitionHandle _handle;
    internal IList<CustomAttrib>? _customAttribs;

    public MethodDef(
        TypeDef declaringType,
        TypeSig retSig, ImmutableArray<ParamDef> pars, string name,
        MethodAttributes attribs = default, MethodImplAttributes implAttribs = default,
        GenericParamType[]? genericParams = null)
    {
        DeclaringType = declaringType;
        ReturnParam = new ParamDef(retSig, null);
        Params = pars;
        Name = name;
        Attribs = attribs;
        ImplAttribs = implAttribs;
        _genericParams = genericParams ?? [];

        Ensure.That(
            IsStatic || !declaringType.IsGeneric || pars[0].Type is TypeSpec or ByrefType { ElemType: TypeSpec },
            "`this` parameter for generic type must be specialized with the default parameters");
    }
    
    /// <summary> Updates the generic signature of the metho. </summary>
    /// <remarks> Upon changes, all existing MethodSpec instances deriving from this definition will be invalidated. Malformed IL may be generated if they are referenced. </remarks>
    public void SetGenericParams(GenericParamType[] genericPars)
    {
        _genericParams = genericPars;
    }

    public override IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

    public MethodDebugSymbols? GetDebugSymbols() => Module.GetDebugSymbols()?.GetMethodSymbols(this);

    class ParamSigProxyList : IReadOnlyList<TypeSig>
    {
        public MethodDef Method = null!;

        public TypeSig this[int index] => Method.Params[index].Sig;
        public int Count => Method.Params.Length;

        public IEnumerator<TypeSig> GetEnumerator() => Method.Params.Select(p => p.Sig).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary> Represents a generic method instantiation. </summary>
public class MethodSpec : MethodDefOrSpec
{
    public override CallConvention CallConv { get => Definition.CallConv; set => Definition.CallConv = value; }
    public override MethodDef Definition { get; }
    public override TypeDefOrSpec DeclaringType { get; }
    public override string Name {
        get => Definition.Name;
        set => throw new InvalidOperationException();
    }

    public override MethodAttributes Attribs {
        get => Definition.Attribs;
        set => Definition.Attribs = value;
    }
    public override MethodImplAttributes ImplAttribs {
        get => Definition.ImplAttribs;
        set => Definition.ImplAttribs = value;
    }

    public override TypeSig ReturnSig { get; }
    public override IReadOnlyList<TypeSig> ParamSig { get; }
    public override IReadOnlyList<TypeDesc> GenericParams { get; }

    /// <summary> Whether the generic parameters from this spec are different from its open-form definition. </summary>
    public bool IsBoundGeneric => IsGeneric && GenericParams != Definition.GenericParams;

    internal MethodSpec(TypeDefOrSpec declaringType, MethodDef def, IReadOnlyList<TypeDesc> genArgs)
    {
        Debug.Assert(!def.IsGeneric || genArgs.Count > 0);
        
        Definition = def;
        DeclaringType = declaringType;
        GenericParams = genArgs;

        var ctx = new GenericContext(this);
        ReturnSig = def.ReturnSig.GetSpec(ctx);
        ParamSig = GetParamsSpec(ctx);
    }

    private TypeSig[] GetParamsSpec(GenericContext genCtx)
    {
        if (Definition.Params.Length == 0) {
            return [];
        }
        var types = new TypeSig[Definition.Params.Length];
        int index = 0;

        if (Definition.IsInstance) {
            types[index++] = DeclaringType.IsValueType ? DeclaringType.CreateByref() : DeclaringType;
        }
        foreach (var par in Definition.StaticParams) {
            types[index++] = par.Sig.GetSpec(genCtx);
        }
        return types;
    }
}

/// <summary> Preserves a call-site signature when its target or optional arguments cannot be represented by a definition. </summary>
public sealed class MethodReference : MethodDesc
{
    public EntityDesc Parent { get; }
    public MethodSig Signature { get; }
    public MethodReference? GenericDefinition { get; }
    public MethodDesc? Target { get; }
    public override TypeDesc DeclaringType => Parent is TypeDesc type ? type : ((MethodDesc)Parent).DeclaringType;
    public override string Name { get; set; }
    public override MethodAttributes Attribs { get; set; }
    public override MethodImplAttributes ImplAttribs { get; set; }
    public override CallConvention CallConv { get => Signature.CallConv; set => throw new InvalidOperationException(); }
    readonly MethodSig _boundSignature;
    public override TypeSig ReturnSig => _boundSignature.ReturnType;
    public override IReadOnlyList<TypeSig> ParamSig { get; }
    public override IReadOnlyList<TypeDesc> GenericParams { get; }

    public MethodReference(EntityDesc parent, string name, MethodSig signature, MethodReference? genericDefinition = null,
        ImmutableArray<TypeDesc> genericArguments = default, MethodDesc? resolvedTarget = null)
    {
        Parent = parent;
        Name = name;
        Signature = signature;
        GenericDefinition = genericDefinition;
        Target = resolvedTarget ?? parent as MethodDesc;
        Attribs = Target is { } method ? method.Attribs
            : MethodAttributes.Public | (signature.IsInstance == true ? 0 : MethodAttributes.Static);
        ImplAttribs = Target?.ImplAttribs ?? default;
        GenericParams = genericArguments.IsDefault
            ? Enumerable.Range(0, signature.NumGenericParams).Select(i => (TypeDesc)GenericParamType.GetUnbound(i, true)).ToArray()
            : genericArguments;
        _boundSignature = signature.GetSpec(new GenericContext(DeclaringType.GenericParams, GenericParams));
        ParamSig = signature.IsInstance == true
            ? [DeclaringType.IsValueType ? DeclaringType.CreateByref() : DeclaringType, .._boundSignature.ParamTypes]
            : _boundSignature.ParamTypes;
    }

    public override MethodDesc GetSpec(GenericContext context)
    {
        if (GenericDefinition != null) {
            return GenericDefinition.GetSpec(context).GetSpec(context.FillParams(GenericParams));
        }
        var parent = Parent is TypeDesc type ? (EntityDesc)type.GetSpec(context) : ((MethodDesc)Parent).GetSpec(context);
        var signature = Signature;
        if (CallConv == CallConvention.VarArgs) {
            // Optional varargs belong to the caller's generic context, unlike
            // the fixed signature declared by the referenced method.
            var parameters = signature.ParamTypes.Select((parameter, index) => index < signature.RequiredParameterCount
                ? parameter : parameter.GetSpec(context)).ToArray();
            signature = new MethodSig(signature.ReturnType, parameters, signature.Header, signature.NumGenericParams,
                signature.RequiredParameterCount);
        }
        return parent == Parent && signature.Equals(Signature) ? this : new MethodReference(parent, Name, signature,
            resolvedTarget: Target?.GetSpec(context));
    }

    public override MethodDesc GetSpec(ImmutableArray<TypeDesc> arguments)
    {
        Ensure.That(GenericDefinition == null && arguments.Length == Signature.NumGenericParams);
        return new MethodReference(Parent, Name, Signature, this, arguments, Target);
    }
}

public class ParamDef
{
    public TypeSig Sig { get; set; }
    public string? Name { get; set; }
    public ParameterAttributes Attribs { get; set; }
    public object? DefaultValue { get; set; }
    public byte[]? MarshallingDesc { get; set; }

    public TypeDesc Type => Sig.Type;

    internal IList<CustomAttrib>? _customAttribs;

    public ParamDef(TypeSig sig, string? name, ParameterAttributes attribs = default)
    {
        Sig = sig;
        Name = name;
        Attribs = attribs;
    }

    public IList<CustomAttrib> GetCustomAttribs(bool readOnly = true)
        => CustomAttribUtils.GetOrInitList(ref _customAttribs, readOnly);

    public override string ToString() => Sig.ToString();
}

/// <summary> Describes a method's DllImport. </summary>
public class ImportDesc
{
    public string ModuleName { get; set; }
    public string FunctionName { get; set; }
    public MethodImportAttributes Attribs { get; set; }

    public ImportDesc(string modName, string funcName, MethodImportAttributes attribs = 0)
    {
        ModuleName = modName;
        FunctionName = funcName;
        Attribs = attribs;
    }
}
