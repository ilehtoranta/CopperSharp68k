namespace Cilica.AsmIO;

using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

partial class ModuleWriter
{
    private void EncodeType(SignatureTypeEncoder enc, TypeSig sig)
    {
        foreach (var mod in sig.Modifiers) {
            enc.CustomModifiers().AddModifier(GetHandle(mod.Type), !mod.IsRequired);
        }
        
        switch (sig.Type) {
            case PrimType t: {
                if (ReferenceEquals(t, PrimType.Array)) {
                    enc.Type(GetHandle(_mod.Resolver.SysTypes.Array), false);
                    return;
                }
                if (ReferenceEquals(t, PrimType.ValueType)) {
                    enc.Type(GetHandle(_mod.Resolver.SysTypes.ValueType), false);
                    return;
                }
                enc.Builder.WriteByte((byte)t.Kind.ToSrmTypeCode());
                break;
            }
            case ArrayType t: {
                EncodeType(enc.SZArray(), new TypeSig(t.ElemType, t.ElementModifiers));
                break;
            }
            case MDArrayType t: {
                enc.Array(out var elemTypeEnc, out var shapeEnc);
                EncodeType(elemTypeEnc, new TypeSig(t.ElemType, t.ElementModifiers));
                shapeEnc.Shape(t.Rank, t.Sizes, t.LowerBounds);
                break;
            }
            case ByrefType t: {
                enc.Builder.WriteByte((byte)SignatureTypeCode.ByReference);
                EncodeType(enc, new TypeSig(t.ElemType, t.ElementModifiers));
                break;
            }
            case PointerType t: {
                EncodeType(enc.Pointer(), new TypeSig(t.ElemType, t.ElementModifiers));
                break;
            }
            case TypeDef t: {
                enc.Type(GetHandle(t), t.IsValueType);
                break;
            }
            case TypeSpec t: {
                var argEnc = enc.GenericInstantiation(GetHandle(t.Definition), t.GenericParams.Count, t.IsValueType);
                foreach (var arg in t.GenericParams) {
                    EncodeType(argEnc.AddArgument(), arg);
                }
                break;
            }
            case FuncPtrType t: {
                enc.Builder.WriteByte((byte)SignatureTypeCode.FunctionPointer);
                EncodeMethodSig(new BlobEncoder(enc.Builder), t.Signature, isPropSig: false);
                break;
            }
            case GenericParamType t: {
                if (t.IsMethodParam) {
                    enc.GenericMethodTypeParameter(t.Index);
                } else {
                    enc.GenericTypeParameter(t.Index);
                }
                break;
            }
            default: throw new NotImplementedException();
        }
    }

    private void EncodeMethodSig(BlobEncoder b, MethodSig sig, bool isPropSig = false)
    {
        var pars = sig.ParamTypes;
        bool isInstance = sig.IsInstance ?? throw new InvalidOperationException();

        // Preserve the complete call-site header, including explicit-this, and
        // parameter modifiers used by unmanaged function pointer signatures.
        b.Builder.WriteByte(isPropSig
            ? new SignatureHeader(SignatureKind.Property, default, isInstance ? SignatureAttributes.Instance : 0).RawValue
            : sig.Header.RawValue);
        if (sig.IsGeneric) b.Builder.WriteCompressedInteger(sig.NumGenericParams);
        b.Builder.WriteCompressedInteger(pars.Count);
        EncodeType(new SignatureTypeEncoder(b.Builder), sig.ReturnType);
        for (int i = 0; i < pars.Count; i++) {
            if (i == sig.RequiredParameterCount) b.Builder.WriteByte((byte)SignatureTypeCode.Sentinel);
            EncodeType(new SignatureTypeEncoder(b.Builder), pars[i]);
        }
    }

    private BlobHandle EncodeMethodSig(MethodDesc method)
    {
        if (method is MethodReference reference) return EncodeSig(b => EncodeMethodSig(b, reference.Signature));
        return EncodeSig(b => {
            var pars = method.ParamSig;
            int offset = method.IsInstance ? 1 : 0;

            b.MethodSignature((SignatureCallingConvention)method.CallConv, method.GenericParams.Count, method.IsInstance)
                .Parameters(pars.Count - offset, out var retTypeEnc, out var parsEnc);

            EncodeType(retTypeEnc.Type(), method.ReturnSig);

            for (int i = offset; i < pars.Count; i++) {
                EncodeType(parsEnc.AddParameter().Type(), pars[i]);
            }
        });
    }

    private BlobHandle EncodeMethodSpecSig(MethodDesc method)
    {
        return EncodeSig(b => {
            var genArgEnc = b.MethodSpecificationSignature(method.GenericParams.Count);

            foreach (var par in method.GenericParams) {
                EncodeType(genArgEnc.AddArgument(), par);
            }
        });
    }

    private BlobHandle EncodeFieldSig(FieldDef field)
    {
        return EncodeSig(b => EncodeType(b.FieldSignature(), field.Sig));
    }

    private BlobHandle EncodeSig(Action<BlobEncoder> encode)
    {
        var builder = new BlobBuilder();
        var encoder = new BlobEncoder(builder);
        encode(encoder);
        return _builder.GetOrAddBlob(builder);
    }

    private StringHandle AddString(string? str)
    {
        return str == null ? default : _builder.GetOrAddString(str);
    }
    private BlobHandle AddBlob(byte[]? data)
    {
        return data == null ? default : _builder.GetOrAddBlob(data);
    }
}
