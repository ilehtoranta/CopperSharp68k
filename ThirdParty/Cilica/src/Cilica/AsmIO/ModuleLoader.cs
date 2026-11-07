namespace Cilica.AsmIO;

using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

internal class ModuleLoader
{
    public readonly string _path;
    public readonly PEReader _pe;
    public readonly MetadataReader _reader;
    public readonly ModuleDef _mod;
    private readonly TableEntryCache _tables;
    private readonly HashSet<EntityHandle> _resolving = new();
    private Dictionary<(string? Namespace, string Name), ExportedTypeHandle>? _exportedTypeNames;

    public ModuleLoader(string path, ModuleDef mod)
    {
        _path = path;
        _pe = new PEReader(File.OpenRead(path), PEStreamOptions.PrefetchEntireImage);

        try {
            _reader = _pe.GetMetadataReader();
            _mod = mod;
            _tables = new TableEntryCache(_reader);

            var asmDef = _reader.GetAssemblyDefinition();
            _mod.AsmName = asmDef.GetAssemblyName();

            var modDef = _reader.GetModuleDefinition();
            _mod.ModName = _reader.GetString(modDef.Name);
            _mod.ModuleVersionId = _reader.GetGuid(modDef.Mvid);
            _mod.RuntimeVersion = _reader.MetadataVersion;
            _mod.Image = ModuleImage.Read(_pe);
        } catch {
            _pe.Dispose();
            throw;
        }
    }

    public void Load()
    {
        foreach (var handle in _reader.TypeDefinitions) {
            var info = _reader.GetTypeDefinition(handle);
            var type = new TypeDef(_mod, GetOptString(info.Namespace), _reader.GetString(info.Name), info.Attributes);

            type._handle = handle;
            type._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            // Preserve reflection-visible size and packing independently of
            // the layout flags when round-tripping metadata.
            var layout = info.GetLayout();
            type.LayoutPack = layout.PackingSize;
            type.LayoutSize = layout.Size;
            if (type.IsNested) {
                // ECMA says that nested type rows should come after their parents,
                // thus doing this here should be fine for conforming files.
                type.SetDeclaringType(GetType(info.GetDeclaringType()));
            }

            _mod._typeDefs.Add(type);
            _tables.Set(handle, type);
        }
        foreach (var handle in _reader.ManifestResources) {
            var info = _reader.GetManifestResource(handle);
            string name = _reader.GetString(info.Name);
            var customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            if (info.Implementation.IsNil) {
                _mod._manifestResources.Add(new EmbeddedResource(_mod, name, info.Attributes, checked((int)info.Offset)) {
                    _customAttribs = customAttribs
                });
            } else if (info.Implementation.Kind == HandleKind.AssemblyReference) {
                var parent = (ModuleDef)GetEntity(info.Implementation);
                var resource = parent.GetManifestResource(name) ?? throw new BadImageFormatException("Invalid resource reference");
                _mod._manifestResources.Add(resource!);

                Ensure.That(customAttribs.Length == 0); // design limitation
            } else if (info.Implementation.Kind == HandleKind.AssemblyFile) {
                Ensure.That(info.Offset == 0); // forbidden by spec

                var fileInfo = _reader.GetAssemblyFile((AssemblyFileHandle)info.Implementation);

                _mod._manifestResources.Add(new LinkedResource(
                    _mod, name, info.Attributes, _reader.GetString(fileInfo.Name), 
                    _reader.GetBlobBytes(fileInfo.HashValue), fileInfo.ContainsMetadata
                ) { _customAttribs = customAttribs });
            } else {
                throw new BadImageFormatException("Invalid ManifestResource implementation");
            }
        }

        // Other module stuff
        int entryPointToken = _pe.PEHeaders.CorHeader?.EntryPointTokenOrRelativeVirtualAddress ?? 0;
        if (entryPointToken != 0 && !_pe.PEHeaders.CorHeader!.Flags.HasFlag(CorFlags.NativeEntryPoint)) {
            _mod.EntryPoint = GetMethod(MetadataTokens.MethodDefinitionHandle(entryPointToken));
        }

        var asmCustomAttribs = LoadCustomAttribs(_reader.GetAssemblyDefinition().GetCustomAttributes());
        if (asmCustomAttribs != null) {
            _mod._asmCustomAttribs.AddRange(asmCustomAttribs);
        }
        var modCustomAttribs = LoadCustomAttribs(_reader.GetModuleDefinition().GetCustomAttributes());
        if (modCustomAttribs != null) {
            _mod._modCustomAttribs.AddRange(modCustomAttribs);
        }
    }

    public TypeDef? FindExportedType(string? ns, string name)
    {
        if (_exportedTypeNames == null) {
            _exportedTypeNames = new();

            foreach (var handle in _reader.ExportedTypes) {
                var info = _reader.GetExportedType(handle);

                if (info.Implementation.Kind != HandleKind.ExportedType) {
                    _exportedTypeNames.Add((GetOptString(info.Namespace), _reader.GetString(info.Name)), handle);
                }
            }
        }

        return _exportedTypeNames.TryGetValue((ns, name), out var match) ? (TypeDef)GetEntity(match) : null;
    }

    public ModuleDef.TypeList LoadExportedTypes()
    {
        var types = new ModuleDef.TypeList();

        foreach (var handle in _reader.ExportedTypes) {
            types.Add((TypeDef)GetEntity(handle));
        }

        return types;
    }

    private TypeDef ResolveTypeRef(TypeReference info)
    {
        string? ns = GetOptString(info.Namespace);
        string name = _reader.GetString(info.Name);
        var scope = GetEntity(info.ResolutionScope);
        var type = scope switch {
            ModuleDef mod => mod.FindType(ns, name),
            TypeDef parent => parent.FindNestedType(name),
            _ => null
        } ?? throw new InvalidOperationException($"Could not resolve referenced type '{ns}.{name}'");

        var rootScope = info.ResolutionScope;
        while (rootScope.Kind == HandleKind.TypeReference) {
            rootScope = _reader.GetTypeReference((TypeReferenceHandle)rootScope).ResolutionScope;
        }
        var root = (ModuleDef)GetEntity(rootScope);
        if (type.Module != root) _mod._typeRefRoots[type] = root;
        return type;
    }
    private TypeDef ResolveExportedType(ExportedTypeHandle handle)
    {
        var info = _reader.GetExportedType(handle);
        string name = _reader.GetString(info.Name);
        string? ns = GetOptString(info.Namespace);
        TypeDef? impl;

        if (info.Implementation.Kind == HandleKind.ExportedType) {
            var parent = (TypeDef)GetEntity(info.Implementation);
            impl = parent.FindNestedType(name);
        } else {
            var asm = (ModuleDef)GetEntity(info.Implementation);
            impl = asm.FindType(ns, name);
        }
        return impl ?? throw new InvalidOperationException($"Could not resolve forwarded type '{ns}.{name}'");
    }

    private MethodDesc ResolveMethod(MemberReference info)
    {
        var entity = GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);
        var signature = new SignatureDecoder(this, info.Signature).DecodeMethodSig();

        if (entity is MethodDef or GenericParamType or TypeDef { IsGeneric: true }) return new MethodReference(entity, name, signature);
        var rootParent = (TypeDesc)entity;

        var visited = new HashSet<TypeDesc>();
        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            if (parent is PrimType primitive) parent = primitive.GetDefinition(_mod.Resolver);
            if (!visited.Add(parent)) break;
            var method = parent.FindMethod(name, signature, throwIfNotFound: false);
            if (method != null) {
                if (parent != rootParent && rootParent is not PrimType) {
                    // An inherited MemberRef's type variables belong to its
                    // lookup type, not the base definition. Keep that context;
                    // a different generic mapping is not a proven target.
                    bool genericSignature = signature.ReturnType.Type.IsUnboundGeneric ||
                        signature.ParamTypes.Any(parameter => parameter.Type.IsUnboundGeneric);
                    var target = !genericSignature || rootParent.GenericParams.SequenceEqual(parent.GenericParams) ? method : null;
                    return new MethodReference(entity, name, signature, resolvedTarget: target);
                }
                return signature.CallConv == CallConvention.VarArgs ? new MethodReference(entity, name, signature, resolvedTarget: method) : method;
            }
        }
        // Preserve the call-site signature when no definition is available
        // to the resolver.
        return new MethodReference(entity, name, signature);
    }
    private FieldDesc ResolveField(MemberReference info)
    {
        var rootParent = (TypeDesc)GetEntity(info.Parent);
        string name = _reader.GetString(info.Name);

        for (var parent = rootParent; parent != null; parent = parent.BaseType) {
            var field = parent.FindField(name);
            if (field != null) {
                return field;
            }
        }
        throw new InvalidOperationException($"Could not resolve referenced field '{rootParent}::{name}'");
    }

    public List<FieldDef> LoadFields(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<FieldDef>();

        foreach (var handle in typeInfo.GetFields()) {
            var info = _reader.GetFieldDefinition(handle);

            var sigDecoder = new SignatureDecoder(this, info.Signature, new GenericContext(parent));
            sigDecoder.ExpectHeader(SignatureKind.Field);
            var type = sigDecoder.DecodeTypeSig();

            var field = new FieldDef(parent, type, _reader.GetString(info.Name), info.Attributes);

            if (field.HasDefaultValue) {
                field.DefaultValue = GetConst(info.GetDefaultValue());
            }
            if (field.HasLayoutOffset) {
                field.LayoutOffset = info.GetOffset();
            }
            if (field.Attribs.HasFlag(FieldAttributes.HasFieldRVA)) {
                int rva = info.GetRelativeVirtualAddress();
                var data = _pe.GetSectionData(rva);
                int size = FieldDef.GetMappedDataSize(field.Type);
                if (size == 0) size = data.Length;
                field.MappedData = data.GetContent(0, size).ToArray();
                field._originalRva = rva;
                field._originalDataSize = size;
            }
            if (field.Attribs.HasFlag(FieldAttributes.HasFieldMarshal)) {
                field.MarshallingDesc = _reader.GetBlobBytes(info.GetMarshallingDescriptor());
            }
            field._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            list.Add(field);
            _tables.Set(handle, field);
        }
        return list;
    }
    public List<MethodDef> LoadMethods(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<MethodDef>();

        foreach (var handle in typeInfo.GetMethods()) {
            var info = _reader.GetMethodDefinition(handle);

            // II.2.3.2.1 MethodDefSig
            var sigReader = _reader.GetBlobReader(info.Signature);
            var header = sigReader.ReadSignatureHeader();
            Ensure.That(header.Kind == SignatureKind.Method);
            Ensure.That(!header.HasExplicitThis); // not impl
            Ensure.That(header.IsInstance == !info.Attributes.HasFlag(MethodAttributes.Static));

            var genPars = Array.Empty<GenericParamType>();

            if (header.IsGeneric) {
                LoadGenericParams(handle, out genPars);
                Ensure.That(sigReader.ReadCompressedInteger() == genPars.Length);
            }
            int numParams = sigReader.ReadCompressedInteger();

            var sigDec = new SignatureDecoder(this, sigReader, new GenericContext(parent.GenericParams, genPars));
            var retSig = sigDec.DecodeTypeSig();

            var pars = ImmutableArray.CreateBuilder<ParamDef>(numParams + (header.IsInstance ? 1 : 0));

            if (header.IsInstance) {
                // It's illegal for local variables to be typed with a generic type def.
                // Use a unbound spec for `this` type instead.
                var instanceType = parent.GetSpec(GenericContext.Empty);
                pars.Add(new ParamDef(instanceType.IsValueType ? instanceType.CreateByref() : instanceType, "this"));
            }
            for (int i = 0; i < numParams; i++) {
                pars.Add(new ParamDef(sigDec.DecodeTypeSig(), null, 0));
            }

            var method = new MethodDef(
                parent, retSig, pars.MoveToImmutable(),
                _reader.GetString(info.Name), info.Attributes, info.ImplAttributes, genPars
            );
            method.CallConv = (CallConvention)header.CallingConvention;

            LoadParams(method, info);
            method._handle = handle;
            method._bodyRva = info.RelativeVirtualAddress;
            method._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            if ((info.Attributes & MethodAttributes.PinvokeImpl) != 0) {
                var imp = info.GetImport();
                var mod = _reader.GetModuleReference(imp.Module);

                method.ImportInfo = new ImportDesc(
                    _reader.GetString(mod.Name),
                    _reader.GetString(imp.Name),
                    imp.Attributes);
            }

            list.Add(method);
            _tables.Set(handle, method);
        }
        return list;
    }
    private void LoadParams(MethodDef method, MethodDefinition info)
    {
        foreach (var parHandle in info.GetParameters()) {
            var parInfo = _reader.GetParameter(parHandle);
            int index = parInfo.SequenceNumber;
            ParamDef par;

            if (index > 0 && index <= method.Params.Length) {
                par = method.Params[index - (method.IsStatic ? 1 : 0)]; // we always have a `this` param
            } else {
                par = method.ReturnParam;
            }
            par.Name = _reader.GetString(parInfo.Name);
            par.Attribs = parInfo.Attributes;

            if (par.Attribs.HasFlag(ParameterAttributes.HasDefault)) {
                par.DefaultValue = GetConst(parInfo.GetDefaultValue());
            }
            if (par.Attribs.HasFlag(ParameterAttributes.HasFieldMarshal)) {
                par.MarshallingDesc = _reader.GetBlobBytes(parInfo.GetMarshallingDescriptor());
            }
            par._customAttribs = LoadCustomAttribs(parInfo.GetCustomAttributes());
        }
    }

    public List<PropertyDef> LoadProperties(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<PropertyDef>();

        foreach (var handle in typeInfo.GetProperties()) {
            var info = _reader.GetPropertyDefinition(handle);

            var sig = new SignatureDecoder(this, info.Signature, new GenericContext(parent)).DecodeMethodSig();
            var accs = info.GetAccessors();

            var prop = new PropertyDef(
                parent, _reader.GetString(info.Name), sig,
                accs.Getter.IsNil ? null : GetMethod(accs.Getter),
                accs.Setter.IsNil ? null : GetMethod(accs.Setter),
                accs.Others.Select(GetMethod).ToArray(),
                GetConst(info.GetDefaultValue()),
                info.Attributes
            );
            prop._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            list.Add(prop);
        }
        return list;
    }
    public List<EventDef> LoadEvents(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<EventDef>();

        foreach (var handle in typeInfo.GetEvents()) {
            var info = _reader.GetEventDefinition(handle);

            var type = (TypeDesc)GetEntity(info.Type);
            var accs = info.GetAccessors();

            var evt = new EventDef(
                parent, _reader.GetString(info.Name), type,
                accs.Adder.IsNil ? null : GetMethod(accs.Adder),
                accs.Remover.IsNil ? null : GetMethod(accs.Remover),
                accs.Raiser.IsNil ? null : GetMethod(accs.Raiser),
                accs.Others.Select(GetMethod).ToArray(),
                info.Attributes
            );
            evt._customAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            list.Add(evt);
        }
        return list;
    }

    public TypeDefOrSpec? GetBaseType(TypeDefinitionHandle parentHandle)
    {
        var baseTypeHandle = _reader.GetTypeDefinition(parentHandle).BaseType;
        return baseTypeHandle.IsNil ? null : (TypeDefOrSpec)GetEntity(baseTypeHandle);
    }
    public List<TypeDesc> LoadInterfaces(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var list = new List<TypeDesc>();

        foreach (var itfHandle in typeInfo.GetInterfaceImplementations()) {
            var itf = _reader.GetInterfaceImplementation(itfHandle);
            var type = (TypeDesc)GetEntity(itf.Interface);

            list.Add(type);
            parent.SetImplCustomAttribs((type, null), LoadCustomAttribs(itf.GetCustomAttributes()));
        }
        return list;
    }
    public Dictionary<MethodDesc, MethodDef> LoadMethodImpls(TypeDef parent)
    {
        var typeInfo = _reader.GetTypeDefinition(parent._handle);
        var map = new Dictionary<MethodDesc, MethodDef>();

        foreach (var implHandle in typeInfo.GetMethodImplementations()) {
            var impl = _reader.GetMethodImplementation(implHandle);
            var body = (MethodDef)GetEntity(impl.MethodBody);
            var decl = (MethodDesc)GetEntity(impl.MethodDeclaration);

            map.Add(decl, body);
            parent.SetImplCustomAttribs((decl, body), LoadCustomAttribs(impl.GetCustomAttributes()));
        }
        return map;
    }

    public void LoadGenericParams(EntityHandle parentHandle, out GenericParamType[] genPars)
    {
        GenericParameterHandleCollection collection;

        if (parentHandle.Kind == HandleKind.TypeDefinition) {
            var info = _reader.GetTypeDefinition((TypeDefinitionHandle)parentHandle);
            collection = info.GetGenericParameters();
        } else if (parentHandle.Kind == HandleKind.MethodDefinition) {
            var info = _reader.GetMethodDefinition((MethodDefinitionHandle)parentHandle);
            collection = info.GetGenericParameters();
        } else {
            throw new UnreachableException();
        }

        var list = new List<GenericParamType>();
        bool isMethod = parentHandle.Kind == HandleKind.MethodDefinition;

        foreach (var handle in collection) {
            var info = _reader.GetGenericParameter(handle);

            var par = new GenericParamType(info.Index, isMethod, _reader.GetString(info.Name), info.Attributes);
            par.CustomAttribs = LoadCustomAttribs(info.GetCustomAttributes());

            Debug.Assert(par.Index == list.Count);
            list.Add(par);
        }

        // Output field *must* be assigned before we start reading
        // constraint types in order to prevent infinite recursion.
        genPars = list.ToArray();

        foreach (var handle in collection) {
            var info = _reader.GetGenericParameter(handle);
            genPars[info.Index].Constraints = LoadGenericParamConstraints(info.GetConstraints());
        }
    }
    private ImmutableArray<GenericParamConstraint> LoadGenericParamConstraints(GenericParameterConstraintHandleCollection list)
    {
        var builder = ImmutableArray.CreateBuilder<GenericParamConstraint>(list.Count);

        foreach (var handle in list) {
            var info = _reader.GetGenericParameterConstraint(handle);

            builder.Add(new GenericParamConstraint(
                GetTypeSig(info.Type),
                LoadCustomAttribs(info.GetCustomAttributes())
            ));
        }
        return builder.DrainToImmutable();
    }

    public CustomAttrib[] LoadCustomAttribs(CustomAttributeHandleCollection handles)
    {
        if (handles.Count == 0) {
            return [];
        }
        var attribs = new CustomAttrib[handles.Count];
        int index = 0;

        foreach (var handle in handles) {
            attribs[index++] = new CustomAttrib(_mod, handle);
        }
        return attribs;
    }

    public FuncPtrType DecodeMethodSig(StandaloneSignatureHandle handle)
    {
        var info = _reader.GetStandaloneSignature(handle);
        Ensure.That(info.GetCustomAttributes().Count == 0);

        return new FuncPtrType(new SignatureDecoder(this, info.Signature).DecodeMethodSig());
    }

    public string? GetOptString(StringHandle handle)
        => handle.IsNil ? null : _reader.GetString(handle);

    public object? GetConst(ConstantHandle handle)
    {
        if (handle.IsNil) {
            return null;
        }
        var cst = _reader.GetConstant(handle);
        var blob = _reader.GetBlobReader(cst.Value);

        return blob.ReadConstant(cst.TypeCode);
    }

    public EntityDesc GetEntity(EntityHandle handle)
    {
        var entity = _tables.Get(handle);

        if (entity == null) {
            if (!_resolving.Add(handle)) {
                throw new BadImageFormatException($"Cyclic metadata reference in '{_mod.ModName}': {handle.Kind}");
            }

            try {
                entity = LoadEntity(handle);
                _tables.Set(handle, entity);
            } finally {
                _resolving.Remove(handle);
            }
        }
        return entity ?? throw new InvalidOperationException("Table entry is not yet populated");
    }
    public TypeDef GetType(TypeDefinitionHandle handle) => (TypeDef)GetEntity(handle);
    public MethodDef GetMethod(MethodDefinitionHandle handle) => (MethodDef)GetEntity(handle);
    public TypeSig GetTypeSig(EntityHandle handle)
    {
        if (handle.Kind == HandleKind.TypeSpecification) {
            var info = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
            return new SignatureDecoder(this, info.Signature).DecodeTypeSig();
        }
        return (TypeDesc)GetEntity(handle);
    }

    private EntityDesc LoadEntity(EntityHandle handle)
    {
        switch (handle.Kind) {
            case HandleKind.AssemblyReference: {
                var info = _reader.GetAssemblyReference((AssemblyReferenceHandle)handle);
                return _mod.Resolver.Resolve(info.GetAssemblyName());
            }
            case HandleKind.TypeReference:
                return ResolveTypeRef(_reader.GetTypeReference((TypeReferenceHandle)handle));
            case HandleKind.ExportedType:
                return ResolveExportedType((ExportedTypeHandle)handle);
            case HandleKind.ModuleDefinition:
                return _mod;
            // For any given parent type, all belonging methods and fields will be initialized
            // once the respective properties in TypeDef are accessed.
            case HandleKind.MethodDefinition: {
                var info = _reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                var type = GetType(info.GetDeclaringType());
                _ = type.Methods; // init lazy property
                return _tables.Get(handle)!;
            }
            case HandleKind.FieldDefinition: {
                var info = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                var type = GetType(info.GetDeclaringType());
                _ = type.Fields; // init lazy property
                return _tables.Get(handle)!;
            }
            case HandleKind.TypeSpecification: {
                var info = _reader.GetTypeSpecification((TypeSpecificationHandle)handle);
                var sig = new SignatureDecoder(this, info.Signature).DecodeTypeSig();
                Ensure.That(!sig.HasModifiers); // should use GetTypeSig() instead of GetEntity()
                return sig.Type;
            }
            case HandleKind.MethodSpecification: {
                var info = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
                var method = (MethodDesc)GetEntity(info.Method);

                var sigDec = new SignatureDecoder(this, info.Signature);
                sigDec.ExpectHeader(SignatureKind.MethodSpecification);

                return method.GetSpec(sigDec.DecodeGenArgs());
            }
            case HandleKind.MemberReference: {
                var info = _reader.GetMemberReference((MemberReferenceHandle)handle);

                return info.GetKind() switch {
                    MemberReferenceKind.Field => ResolveField(info),
                    MemberReferenceKind.Method => ResolveMethod(info)
                };
            }
            default: throw new NotSupportedException();
        }
    }

    class TableEntryCache
    {
        static readonly TableIndex[] s_Tables = {
            TableIndex.AssemblyRef,
            TableIndex.Module, TableIndex.ExportedType,
            TableIndex.TypeRef, TableIndex.MemberRef,
            TableIndex.TypeDef, TableIndex.TypeSpec,
            TableIndex.Field,
            TableIndex.MethodDef, TableIndex.MethodSpec
        };
        readonly EntityDesc[] _entities; // entities laid out linearly
        readonly AbsRange[] _ranges; // inclusive range in _entities for each TableIndex

        public TableEntryCache(MetadataReader reader)
        {
            _ranges = new AbsRange[s_Tables.Max(v => (int)v) + 1];

            int startIdx = 0;
            foreach (var tableIdx in s_Tables) {
                int numRows = reader.GetTableRowCount(tableIdx);
                _ranges[(int)tableIdx] = AbsRange.FromSlice(startIdx, numRows);
                startIdx += numRows;
            }
            _entities = new EntityDesc[startIdx + 1];
        }
        public EntityDesc? Get(EntityHandle handle)
        {
            int index = GetIndex(handle);
            return _entities[index];
        }

        public void Set(EntityHandle handle, EntityDesc entity)
        {
            int idx = GetIndex(handle);
            Debug.Assert(_entities[idx] == null || _entities[idx] == entity, "Trying to replace already existing entity");
            _entities[idx] = entity;
        }

        private int GetIndex(EntityHandle handle)
        {
            var tableIdx = (int)handle.Kind;
            Debug.Assert(Array.IndexOf(s_Tables, (TableIndex)tableIdx) >= 0);

            var (start, end) = _ranges[tableIdx];
            int index = start + MetadataTokens.GetRowNumber(handle) - 1; // rows are 1 based
            Ensure.That(index >= start && index < end);
            return index;
        }
    }
}
