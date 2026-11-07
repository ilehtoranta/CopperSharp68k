namespace Cilica.Analysis;

using System.Reflection;
using MethodBody = IR.MethodBody;

/// <summary> Checks that an inline body can preserve its member references and required access. </summary>
internal sealed class InliningAccess(TypeDef caller, bool ignoreVisibility = false)
{
    public bool CheckBody(MethodBody body, bool scalarReplacement)
    {
        if (body.Definition.DeclaringType == caller) return true;

        foreach (var inst in body.Instructions()) {
            if (!CanUseType(inst.ResultType)) return false;
            foreach (var operand in inst.Operands) {
                if (!CanUseType(operand.ResultType)) return false;
            }

            MemberDesc? member = inst switch {
                CallInst call => call.Method,
                NewObjInst allocation => allocation.Constructor,
                FuncAddrInst address => address.Method,
                // These fields disappear into locals during scalar replacement.
                FieldAddrInst field when scalarReplacement && field.Obj == body.Args[0] => null,
                FieldAddrInst field => field.Field,
                FieldExtractInst field => field.Field,
                FieldInsertInst field => field.Field,
                _ => null
            };
            if (member != null && !CanUseMember(member)) return false;

            if (inst is IntrinsicInst intrinsic) {
                foreach (var entity in intrinsic.StaticArgs) {
                    if (entity is TypeDesc type && !CanUseType(type) ||
                        entity is MemberDesc referenced && !CanUseMember(referenced)) return false;
                }
            }
            if (inst is CallInst { Constraint: { } constraint } && !CanUseType(constraint) ||
                inst is FuncAddrInst { Constraint: { } addressConstraint } && !CanUseType(addressConstraint) ||
                inst is GuardInst { CatchType: { } catchType } && !CanUseType(catchType) ||
                inst is MemoryInst memory && !CanUseType(memory.ElemType)) return false;
        }
        return true;
    }

    public bool CanUseMember(MemberDesc member)
    {
        // A bare MemberRef does not establish visibility of its target.
        if (member is MethodReference reference) {
            return reference.Parent is MethodDesc target ? CanUseMember(target)
                : ignoreVisibility && CanUseType(reference.DeclaringType) && CanUseSignature(reference.ReturnSig) &&
                    reference.ParamSig.All(CanUseSignature) && reference.GenericParams.All(CanUseType);
        }
        if (!CanUseType(member.DeclaringType)) return false;
        int visibility;
        if (member is MethodDesc method) {
            visibility = (int)(method.Attribs & MethodAttributes.MemberAccessMask);
            if (!CanUseSignature(method.ReturnSig) || !method.ParamSig.All(CanUseSignature) ||
                !method.GenericParams.All(CanUseType)) return false;
        } else if (member is FieldDesc field) {
            visibility = (int)(field.Attribs & FieldAttributes.FieldAccessMask);
            if (!CanUseSignature(field.Sig)) return false;
        } else {
            return false;
        }

        return ignoreVisibility || visibility == (int)MethodAttributes.Public ||
               member.DeclaringType is TypeDefOrSpec owner && (owner.Definition == caller ||
                   owner.Module == caller.Module && visibility is (int)MethodAttributes.Assembly or (int)MethodAttributes.FamORAssem);
    }

    private bool CanUseSignature(TypeSig signature)
        => CanUseType(signature.Type) && signature.Modifiers.All(modifier => CanUseType(modifier.Type));

    private bool CanUseType(TypeDesc type)
    {
        if (type is PrimType or GenericParamType) return true;
        if (type is CompoundType compound) return CanUseType(compound.ElemType) &&
            compound.ElementModifiers.All(modifier => CanUseType(modifier.Type));
        if (type is FuncPtrType pointer) {
            return CanUseSignature(pointer.Signature.ReturnType) && pointer.Signature.ParamTypes.All(CanUseSignature);
        }
        if (type is not TypeDefOrSpec nominal || !nominal.GenericParams.All(CanUseType)) return false;

        for (var definition = nominal.Definition; definition != null; definition = definition.DeclaringType) {
            // Global members have no externally addressable declaring type.
            if (definition.Name == "<Module>" && definition.Module != caller.Module) return false;
            if (ignoreVisibility) continue;
            if (definition == caller) return true;
            var visibility = definition.Attribs & TypeAttributes.VisibilityMask;
            if (visibility is TypeAttributes.Public or TypeAttributes.NestedPublic) continue;
            if (definition.Module == caller.Module && visibility is
                TypeAttributes.NotPublic or TypeAttributes.NestedAssembly or TypeAttributes.NestedFamORAssem) continue;
            // Private/protected nesting and inheritance need fuller CLR access
            // analysis. Keep the call instead of attempting to reproduce it here.
            return false;
        }
        return true;
    }
}
