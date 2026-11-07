namespace Cilica.AsmIO;

using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;

public class ModuleResolver : IDisposable
{
    internal readonly Dictionary<string, ModuleDef> _cache = new(StringComparer.OrdinalIgnoreCase);
    internal readonly Dictionary<ResolvingUtils.MethodSelector, MethodDesc> FunctionCache = new();
    internal readonly Dictionary<string, TypeDefOrSpec> TypeCache = new();

    private string[] _searchPaths = [];
    internal readonly ICompilationLogger? _logger;
    readonly Dictionary<string, ModuleDef> _paths = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    bool _disposed;

    /// <summary> A reference to the <c>System.Private.CoreLib</c> assembly. </summary>
    public ModuleDef CoreLib => _coreLib ??= Resolve("System.Private.CoreLib");
    private ModuleDef? _coreLib;

    public SystemTypes SysTypes => _sysTypes ??= new(CoreLib);
    private SystemTypes? _sysTypes;

    public ModuleResolver(ICompilationLogger? logger = null)
    {
        _logger = logger;
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void AddSearchPaths(IEnumerable<string> paths)
    {
        ThrowIfDisposed();
        _searchPaths = _searchPaths
            .Concat(paths)
            .Select(FixRuntimePackRefPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // Runtime implementation paths provide the canonical definitions behind reference-assembly type forwarders.
        static string FixRuntimePackRefPath(string path)
        {
            // e.g. "C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\10.0.14\ref\net10.0"
            //                              shared                     ****      ***********
            string normPath = Path.GetFullPath(path).Replace('\\', '/');
            string implPath = Regex.Replace(normPath, @"(.+?\/)packs(\/Microsoft\.NETCore\.App)\.Ref(\/.+?)\/.+", "$1shared$2$3");
            return implPath != normPath && Directory.Exists(implPath) ? implPath : path;
        }
    }

    public void AddTrustedSearchPaths()
    {
        // https://docs.microsoft.com/en-us/dotnet/core/dependency-loading/default-probing
        string searchPaths = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        AddSearchPaths(searchPaths.Split(Path.PathSeparator).Select(Path.GetDirectoryName)!);
    }

    public TypeDefOrSpec? Import(Type type, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        ThrowIfDisposed();
        Ensure.That(type.IsTypeDefinition || type.IsConstructedGenericType);

        var mod = Resolve(type.Assembly.GetName().Name!, throwIfNotFound);
        TypeDefOrSpec? resolved = null;

        if (mod != null) {
            resolved = type.IsNested || type.IsGenericType
                ? ImportGenericOrNestedType(mod, type, throwIfNotFound)
                : mod.FindType(type.Namespace, type.Name, throwIfNotFound);
        }
        if (resolved == null && throwIfNotFound) {
            throw new InvalidOperationException("Could not import the specified type");
        }
        return resolved;
    }

    private TypeDefOrSpec? ImportGenericOrNestedType(ModuleDef scope, Type type, bool throwIfNotFound)
    {
        var resolved = type.IsNested
            ? (Import(type.DeclaringType!, throwIfNotFound) as TypeDef)?.FindNestedType(type.Name)
            : scope.FindType(type.Namespace, type.Name, throwIfNotFound);

        if (resolved != null && type.IsConstructedGenericType) {
            var genArgs = type.GenericTypeArguments;
            var finalArgs = ImmutableArray.CreateBuilder<TypeDesc>(genArgs.Length);

            foreach (var genArg in genArgs) {
                var resolvedArg = Import(genArg, throwIfNotFound);
                if (resolvedArg == null) {
                    return null;
                }
                finalArgs.Add(resolvedArg);
            }
            return resolved.GetSpec(finalArgs.MoveToImmutable());
        }
        return resolved;
    }

    public ModuleDef? Resolve(AssemblyName name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
        => Resolve(name.Name ?? throw new ArgumentException("Assembly name is required", nameof(name)), throwIfNotFound);

    public ModuleDef? Resolve(string name, [DoesNotReturnIf(true)] bool throwIfNotFound = true)
    {
        ThrowIfDisposed();

        if (_cache.TryGetValue(name, out var module)) {
            return module;
        }

        module = ResolveImpl(name);

        if (module == null && throwIfNotFound) {
            throw new FileNotFoundException($"Could not resolve assembly '{name}'");
        }

        return module!;
    }

    protected virtual ModuleDef? ResolveImpl(string name)
    {
        foreach (string basePath in _searchPaths) {
            string path = Path.Combine(basePath, name + ".dll");

            if (File.Exists(path)) {
                return Load(path);
            }
        }

        return null;
    }

    public ModuleDef Load(string path)
    {
        ThrowIfDisposed();
        path = Path.GetFullPath(path);

        if (_paths.TryGetValue(path, out var cached)) {
            return cached;
        }

        var module = new ModuleDef(this);
        module._loader = new ModuleLoader(path, module);

        try {
            if (_cache.TryGetValue(module.AsmName.Name!, out var existing)) {
                module.ReleaseReaders();
                _paths.Add(path, existing);

                return existing;
            }

            AddToCache(module.AsmName.Name!, module);
            _logger?.Debug($"Loading module '{module.AsmName.Name}, v{module.AsmName.Version}'");
            module._loader.Load();
            _paths.Add(path, module);

            return module;
        } catch {
            if (_cache.GetValueOrDefault(module.AsmName.Name!) == module) {
                _cache.Remove(module.AsmName.Name!);
            }

            module.ReleaseReaders();
            throw;
        }
    }

    public ModuleDef Create(string asmName, Version? ver = null)
    {
        ThrowIfDisposed();
        var module = new ModuleDef(this) {
            AsmName = new AssemblyName() {
                Name = asmName,
                Version = ver ?? new Version(1, 0, 0, 0),
            },
            ModName = asmName + ".dll"
        };
        module.CreateType(null, "<Module>", TypeAttributes.NotPublic); // global type required by the writer
        AddToCache(asmName, module);
        return module;
    }

    private void AddToCache(string name, ModuleDef module)
    {
        _cache.Add(name, module);
    }
    
    public void Dispose()
    {
        if (_disposed) {
            return;
        }

        _disposed = true;

        foreach (var module in _cache.Values) {
            module.ReleaseReaders();
        }

        _cache.Clear();
        _paths.Clear();
        TypeCache.Clear();
        FunctionCache.Clear();
        _coreLib = null;
        _sysTypes = null;
    }
}
