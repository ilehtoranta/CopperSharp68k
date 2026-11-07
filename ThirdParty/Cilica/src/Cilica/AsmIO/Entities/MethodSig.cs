namespace Cilica.AsmIO;

using System.Collections.Generic;
using System.Reflection.Metadata;

/// <summary> Represents the signature of a method declared in a type. </summary>
public readonly struct MethodSig : IPrintable, IEquatable<MethodSig>
{
    const SignatureAttributes kMaybeInstance = (SignatureAttributes)0x80;

    public TypeSig ReturnType { get; }
    public IReadOnlyList<TypeSig> ParamTypes { get; }

    public int NumGenericParams { get; }
    public int RequiredParameterCount { get; }

    public bool? IsInstance => (_header.RawValue & (byte)kMaybeInstance) != 0 ? null : _header.IsInstance;
    public bool IsGeneric => _header.IsGeneric;
    public CallConvention CallConv => (CallConvention)_header.CallingConvention;
    public bool HasExplicitThis => _header.HasExplicitThis;
    public int NumCallArgs => ParamTypes.Count + (IsInstance == true && !HasExplicitThis ? 1 : 0);
    internal SignatureHeader Header => _header;

    readonly SignatureHeader _header;

    public bool IsNull => ReturnType == null!;

    /// <remarks> Note that <paramref name="paramTypes"/> should not include the instance type (<see langword="true"/> parameter). </remarks>
    public MethodSig(TypeSig retType, IReadOnlyList<TypeSig> paramTypes, bool? isInstance = null, int numGenPars = 0)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = numGenPars;
        RequiredParameterCount = paramTypes.Count;
        
        var attrs = isInstance == null ? kMaybeInstance : 
                    isInstance.Value ? SignatureAttributes.Instance : 
                    0;
        if (numGenPars != 0) attrs |= SignatureAttributes.Generic;
        _header = new(SignatureKind.Method, default, attrs);
    }

    /// <remarks> Note that <paramref name="paramTypes"/> should not include the instance type (<see langword="this"/> parameter). </remarks>
    public MethodSig(TypeSig retType, IReadOnlyList<TypeSig> paramTypes, SignatureHeader header, int numGenPars = 0, int requiredParameterCount = -1)
    {
        ReturnType = retType;
        ParamTypes = paramTypes;
        NumGenericParams = numGenPars;
        RequiredParameterCount = requiredParameterCount < 0 ? paramTypes.Count : requiredParameterCount;
        Ensure.That(RequiredParameterCount <= paramTypes.Count);
        _header = header;
        Ensure.That(!header.Attributes.HasFlag(kMaybeInstance));
    }

    public bool Matches(MethodDesc method)
    {
        return method.GenericParams.Count == NumGenericParams &&
            (IsInstance == null || IsInstance == method.IsInstance) &&
            CallConv == method.CallConv &&
            method.ReturnSig == ReturnType &&
            CompareParams(method);
    }

    public MethodSig GetSpec(GenericContext context)
        => new(ReturnType.GetSpec(context), ParamTypes.Select(p => p.GetSpec(context)).ToArray(), _header, NumGenericParams, RequiredParameterCount);

    public bool Equals(MethodSig other)
        => IsNull || other.IsNull ? IsNull && other.IsNull :
            _header == other._header && NumGenericParams == other.NumGenericParams && RequiredParameterCount == other.RequiredParameterCount &&
            ReturnType == other.ReturnType && ParamTypes.SequenceEqual(other.ParamTypes);

    public override bool Equals(object? other) => other is MethodSig signature && Equals(signature);

    public override int GetHashCode()
    {
        if (IsNull) return 0;
        var hash = new HashCode();
        hash.Add(_header);
        hash.Add(NumGenericParams);
        hash.Add(RequiredParameterCount);
        hash.Add(ReturnType);
        foreach (var parameter in ParamTypes) hash.Add(parameter);
        return hash.ToHashCode();
    }

    private bool CompareParams(MethodDesc method)
    {
        var pars1 = method.ParamSig;
        int offset = method.IsInstance ? 1 : 0;

        int count = CallConv == CallConvention.VarArgs ? RequiredParameterCount : ParamTypes.Count;
        if ((pars1.Count - offset) != count) {
            return false;
        }
        for (int i = 0; i < count; i++) {
            var type1 = pars1[i + offset];
            if (type1 != ParamTypes[i]) {
                return false;
            }
        }
        return true;
    }

    public void Print(PrintContext ctx, bool includeNs = false)
    {
        if (CallConv != CallConvention.Managed) {
            ctx.Print($"[{PrintToner.ClassName}{CallConv.ToString()}]");
        }

        ReturnType.Print(ctx, includeNs);

        if (NumGenericParams > 0) {
            ctx.PrintSequence(
                "<", ">", Enumerable.Range(0, NumGenericParams), 
                i => ctx.Print("?", PrintToner.Number));
        }
        ctx.PrintSequence("(", ")", ParamTypes, p => p.Print(ctx, includeNs));
    }
    void IPrintable.Print(PrintContext ctx) => Print(ctx, false);
    void IPrintable.PrintAsOperand(PrintContext ctx) => Print(ctx, false);

    public override string ToString() => PrintContext.ToString(this);
}

//Copied from SignatureCallingConvention
/// <summary>
/// Specifies how arguments in a given signature are passed from the caller to the
/// callee. The underlying values of the fields in this type correspond to the representation
/// in the leading signature byte represented by a System.Reflection.Metadata.SignatureHeader
/// structure.
/// </summary>
public enum CallConvention : byte
{
    /// <summary> A managed calling convention with a fixed-length argument list. </summary>
    Managed = 0,
    /// <summary> An unmanaged C/C++ style calling convention where the call stack is cleaned by the caller. </summary>
    CDecl = 1,
    /// <summary> An unmanaged calling convention where the call stack is cleaned up by the callee. </summary>
    StdCall = 2,
    /// <summary> An unmanaged C++ style calling convention for calling instance member functions with a fixed argument list. </summary>
    ThisCall = 3,
    /// <summary> An unmanaged calling convention where arguments are passed in registers when possible. </summary>
    FastCall = 4,
    /// <summary> A managed calling convention for passing extra arguments. </summary>
    VarArgs = 5,
    /// <summary> Indicates that the specifics of the unmanaged calling convention are encoded as modopts. </summary>
    Unmanaged = 9
}
