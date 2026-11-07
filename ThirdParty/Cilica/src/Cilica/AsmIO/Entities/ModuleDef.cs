namespace Cilica.AsmIO;

using System.Collections;
using System.IO;
using System.Reflection;

public class ModuleDef : EntityDesc
{
    public string ModName { get; set; } = null!;
    public AssemblyName AsmName { get; set; } = null!;

    public ModuleImage? Image { get; set; }

    public string RuntimeVersion { get; set; } = "v4.0.30319";

    public Guid ModuleVersionId { get; internal set; }

    public MethodDef? EntryPoint { get; set; }

    /// <summary> All types defined in this module (incl. nested). </summary>
    public IReadOnlyCollection<TypeDef> TypeDefs => _typeDefs;

    /// <summary> All types exported by this module (incl. nested). </summary>
    public IReadOnlyCollection<TypeDef> ExportedTypes => _exportedTypes ??= _loader?.LoadExportedTypes() ?? new();

    /// <summary> A list of resources embedded in, or referenced by this module. </summary>
    /// <remarks> For more information, see <c>II.6.2.2 Manifest resources</c> of the ECMA335 spec. </remarks>
    public ICollection<ResourceDesc> ManifestResources => _manifestResources;

    public ModuleResolver Resolver { get; }

    internal TypeList _typeDefs = new();
    private TypeList? _exportedTypes;
    internal List<ResourceDesc> _manifestResources = new();

    internal Dictionary<TypeDef, ModuleDef> _typeRefRoots = new(); // root assemblies for references of forwarded types
    internal List<CustomAttrib> _asmCustomAttribs = new(), _modCustomAttribs = new();

    internal ModuleLoader? _loader;
    private DebugSymbolStore? _debugSymbols;
    private bool _triedToLoadDebugSymbols = false;

    internal ModuleDef(ModuleResolver resolver)
    {
        Resolver = resolver;
    }

    public TypeDef? FindType(string? ns, string name, bool includeExports = true, [DoesNotReturnIf(true)] bool throwIfNotFound = false)
    {
        var type = _typeDefs.Find(ns, name);

        if (type == null && includeExports) {
            type = _exportedTypes?.Find(ns, name) ?? _loader?.FindExportedType(ns, name);
        }
        if (type == null && throwIfNotFound) {
            throw new InvalidOperationException($"Type {ns}.{name} not found");
        }
        return type;
    }

    public TypeDef CreateType(
        string? ns, string name, 
        TypeAttributes attrs = TypeAttributes.Public,
        TypeDefOrSpec? baseType = null,
        GenericParamType[]? genericParams = null)
    {
        if (FindType(ns, name) != null) {
            throw new InvalidOperationException("A type with the same name already exists");
        }
        var type = new TypeDef(
            this, ns, name, attrs, genericParams ?? [],
            baseType ?? Resolver.SysTypes.Object
        );
        _typeDefs.Add(type);
        return type;
    }

    public IEnumerable<MethodDef> MethodDefs()
        => TypeDefs.SelectMany(t => t.Methods);

    internal void RemoveType(TypeDef type)
    {
        Ensure.That(type.Module == this);

        foreach (var nested in type.NestedTypes.ToArray()) {
            RemoveType(nested);
        }

        type.DeclaringType?.NestedTypes.Remove(type);
        _typeDefs.Remove(type);
    }

    // TODO: consider deprecating this in favor of exposing CustomAttribs and AssemblyCustomAttribs properties directly
    public List<CustomAttrib> GetCustomAttribs(bool forAssembly)
        => forAssembly ? _asmCustomAttribs : _modCustomAttribs;

    public ResourceDesc? GetManifestResource(string name)
    {
        return ManifestResources.FirstOrDefault(m => m.Name == name);
    }

    /// <summary> Adds or replaces an embedded manifest resource. </summary>
    /// <param name="overwrite"> If true, indicates that existing resources should be replaced with the new contents. Otherwise, an exception is thrown. </param>
    public ResourceDesc CreateEmbeddedResource(string name, byte[] data, bool overwrite = false, ManifestResourceAttributes attrs = ManifestResourceAttributes.Public)
    {
        int index = _manifestResources.FindIndex(e => e.Name == name);
        Ensure.That(index < 0 || overwrite, "A resource with the same name already exists");

        var resource = new EmbeddedResource(this, name, attrs, data);

        if (index < 0) {
            _manifestResources.Add(resource);
        } else {
            _manifestResources[index] = resource;
        }
        return resource;
    }

    /// <summary> Serializes this module to the specified stream. </summary>
    /// <param name="pdbStream">
    /// If non-null, specifies the stream where the PDB data should be serialized into.
    /// If the module does not have debug symbols, nothing will be written to this stream.
    /// </param>
    public void Save(Stream stream, Stream? pdbStream, string path) => Save(stream, pdbStream, path, publicSign: false);

    /// <summary> Writes the module, optionally keeping its public key with an empty strong-name signature. </summary>
    public void Save(Stream stream, Stream? pdbStream, string path, bool publicSign)
        => Save(stream, pdbStream, path, publicSign ? ModuleWriteOptions.PublicSign : ModuleWriteOptions.None);

    /// <summary> Writes the module with explicit signing and native-code removal options. </summary>
    public void Save(Stream stream, Stream? pdbStream, string path, ModuleWriteOptions options)
    {
        Resolver.ThrowIfDisposed();
        var writer = new ModuleWriter(this, options);
        writer.BuildTables();
        writer.Serialize(stream, pdbStream, path);
    }

    public void Save(string path, bool savePdb, string? backupPath = null) => Save(path, savePdb, publicSign: false, backupPath);

    /// <summary> Atomically writes the module, optionally public-signing it without a private key. </summary>
    public void Save(string path, bool savePdb, bool publicSign, string? backupPath = null)
        => Save(path, savePdb, publicSign ? ModuleWriteOptions.PublicSign : ModuleWriteOptions.None, backupPath);

    /// <summary> Atomically writes the module with explicit signing and native-code removal options. </summary>
    public void Save(string path, bool savePdb, ModuleWriteOptions options, string? backupPath = null)
    {
        if (savePdb && GetDebugSymbols() == null && _loader is { } loader &&
            File.Exists(Path.ChangeExtension(loader._path, ".pdb")) && loader._pe.ReadDebugDirectory().Any(entry =>
                entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView && !entry.IsPortableCodeView)) {
            throw new NotSupportedException("Native PDB symbols cannot be rewritten; retain the input assembly or explicitly discard symbols");
        }
        using var output = new AtomicOutput();
        string pdbPath = Path.ChangeExtension(path, ".pdb");
        var pdbStream = savePdb && GetDebugSymbols() != null
            ? output.Create(pdbPath, backupPath == null ? null : pdbPath + ".bak") : null;
        var stream = output.Create(path, backupPath);
        Save(stream, pdbStream, path, options);
        output.Commit();
    }

    public DebugSymbolStore? GetDebugSymbols(bool create = false, Func<string, Stream?>? pdbFileStreamProvider = null)
    {
        Resolver.ThrowIfDisposed();

        if (_debugSymbols != null) {
            return _debugSymbols;
        }

        if (_loader != null && !_triedToLoadDebugSymbols) {
            _triedToLoadDebugSymbols = true;

            try {
                _loader._pe.TryOpenAssociatedPortablePdb(_loader._path, OpenPdbStream, out var provider, out string? pdbPath);

                if (provider != null) {
                    try {
                        _debugSymbols = new PortablePdbSymbolStore(this, provider);
                    } catch {
                        provider.Dispose();
                        throw;
                    }
                }
            } catch (Exception ex) {
                Resolver._logger?.Error($"Failed to load debug symbols for module '{ModName}'", ex);
            }
        }

        if (_debugSymbols == null && create) {
            _debugSymbols = new DebugSymbolStore(this);
        }
        return _debugSymbols;

        Stream? OpenPdbStream(string name)
        {
            try {
                using var stream = pdbFileStreamProvider?.Invoke(name) ?? File.OpenRead(name);

                // Read file to memory to avoid locking it and allow for overwrites in Save().
                // This isn't quite efficient and will probably result in two copies of the file
                // in memory (one internal to MetadataReader/MemoryBlock).
                //
                // In the future, we could avoid this by replicating TryOpenAssociatedPortablePdb() and
                // directly calling FromPortablePdbStream() with the Prefetch flag.
                var ms = new MemoryStream((int)stream.Length);
                stream.CopyTo(ms);
                ms.Position = 0;
                return ms;
            } catch (FileNotFoundException) {
                // nop
            }
            return null;
        }
    }

    internal void ReleaseReaders()
    {
        _debugSymbols?.Dispose();
        _debugSymbols = null;
        _loader?._pe.Dispose();
        _loader = null;
    }

    public override string ToString()
        => AsmName.ToString();

    public override void Print(PrintContext ctx)
        => ctx.Print(AsmName.ToString());

    internal class TypeList : IReadOnlyCollection<TypeDef>
    {
        readonly Dictionary<(string? Ns, string Name), TypeDef> _roots = new();
        readonly List<TypeDef> _nested = new();

        public int Count => _roots.Count + _nested.Count;

        public void Remove(TypeDef type)
        {
            if (type.IsNested) {
                _nested.Remove(type);
            } else {
                _roots.Remove((type.Namespace, type.Name));
            }
        }

        public void Add(TypeDef type)
        {
            if (type.IsNested) {
                _nested.Add(type);
            } else {
                _roots.Add((type.Namespace, type.Name), type);
            }
        }
        
        public TypeDef? Find(string? ns, string name)
            => _roots.GetValueOrDefault((ns, name));

        public IEnumerator<TypeDef> GetEnumerator() => _roots.Values.Concat(_nested).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
