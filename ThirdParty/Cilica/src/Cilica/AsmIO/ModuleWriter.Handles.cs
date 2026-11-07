namespace Cilica.AsmIO;

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

partial class ModuleWriter
{
    private void AllocHandles()
    {
        int typeIdx = 1, fieldIdx = 1, methodIdx = 1;

        // Global type must be at exactly the first table entry
        var globalType = _mod.FindType(null, "<Module>") 
            ?? throw new InvalidOperationException("Module is missing its global type");
        _handleMap.Add(globalType, MetadataTokens.TypeDefinitionHandle(typeIdx++));

        foreach (var type in _mod.TypeDefs) {
            if (type != globalType) {
                _handleMap.Add(type, MetadataTokens.TypeDefinitionHandle(typeIdx++));
            }
            foreach (var field in type.Fields) {
                _handleMap.Add(field, MetadataTokens.FieldDefinitionHandle(fieldIdx++));
            }
            foreach (var method in type.Methods) {
                _handleMap.Add(method, MetadataTokens.MethodDefinitionHandle(methodIdx++));
            }
        }
    }

    // Add reference to an entity defined in another module
    private EntityHandle CreateHandle(EntityDesc entity)
    {
        switch (entity) {
            case TypeDef type: {
                return GetTypeReference(type, _mod._typeRefRoots.GetValueOrDefault(type, type.Module));
            }
            case TypeDesc type: {
                return _builder.AddTypeSpecification(
                    EncodeSig(b => EncodeType(b.TypeSpecificationSignature(), type))
                );
            }
            case MethodDesc method: {
                if (method is MethodReference { GenericDefinition: { } genericDefinition } genericReference) {
                    return _builder.AddMethodSpecification(GetHandle(genericDefinition), EncodeMethodSpecSig(genericReference));
                }
                var parent = method is MethodReference reference ? reference.Parent : method.DeclaringType;
                if (parent is MethodDef definition && definition.Module != _mod) parent = definition.DeclaringType;
                var defHandle = _builder.AddMemberReference(
                    GetHandle(parent),
                    AddString(method.Name),
                    EncodeMethodSig((method as MethodDefOrSpec)?.Definition ?? method)
                );
                return method is MethodSpec { IsBoundGeneric: true } spec
                    ? _builder.AddMethodSpecification(defHandle, EncodeMethodSpecSig(spec))
                    : defHandle;
            }
            case FieldDefOrSpec field: {
                return _builder.AddMemberReference(
                    GetHandle(field.DeclaringType),
                    AddString(field.Name),
                    EncodeFieldSig(field.Definition)
                );
            }
            case ModuleDef module: {
                var name = module.AsmName;
                return _builder.AddAssemblyReference(
                    AddString(name.Name),
                    name.Version!,
                    AddString(name.CultureName),
                    AddBlob(name.GetPublicKey() ?? name.GetPublicKeyToken()),
                    (AssemblyFlags)name.Flags,
                    default
                );
            }
            default: throw new NotImplementedException();
        }
    }

    private TypeReferenceHandle GetTypeReference(TypeDef type, ModuleDef root)
    {
        if (!_typeRefs.TryGetValue((type, root), out var handle)) {
            // A facade may forward the public enclosing type without forwarding
            // its private nested types. Each reference chain keeps its own root.
            var scope = type.DeclaringType is { } parent ? GetTypeReference(parent, root) : GetHandle(root);
            handle = _builder.AddTypeReference(scope, AddString(type.Namespace), AddString(type.Name));
            _typeRefs.Add((type, root), handle);
        }
        return handle;
    }

    public EntityHandle GetHandle(EntityDesc entity)
    {
        if (entity is PrimType primType) {
            entity = primType.GetDefinition(_mod.Resolver);
        }
        if (!_handleMap.TryGetValue(entity, out var handle)) {
            _handleMap[entity] = handle = CreateHandle(entity);
        }
        return handle;
    }

    private EntityHandle GetSigHandle(TypeSig sig)
    {
        if (!sig.HasModifiers) {
            return GetHandle(sig.Type);
        }
        return _builder.AddTypeSpecification(
            EncodeSig(b => EncodeType(b.TypeSpecificationSignature(), sig))
        );
    }
}
