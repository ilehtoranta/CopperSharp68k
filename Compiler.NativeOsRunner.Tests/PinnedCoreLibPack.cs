using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace CopperSharp.Compiler.NativeOsRunner.Tests;

internal sealed class PinnedCoreLibPack : IDisposable
{
    public const string ExpectedSha256 = "dc1945de746f94987ec705a1f27d512abf72a41a1da37e414d1951e4e823037a";
    private readonly string directory = Path.Combine(Path.GetTempPath(), "CopperSharpNativeCoreLib", Guid.NewGuid().ToString("N"));
    public string ManifestPath => Path.Combine(directory, "corelib-pack.json");

    public PinnedCoreLibPack()
    {
        var source = Path.Combine(AppContext.BaseDirectory, "PinnedCoreLib", "10.0.9", "System.Private.CoreLib.dll");
        var bytes = File.ReadAllBytes(source);
        Assert.Equal(ExpectedSha256, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        Directory.CreateDirectory(directory);
        try
        {
            var destination = Path.Combine(directory, "System.Private.CoreLib.dll");
            File.WriteAllBytes(destination, bytes);
            using var pe = new PEReader(new MemoryStream(bytes));
            var metadata = pe.GetMetadataReader();
            var assembly = metadata.GetAssemblyDefinition();
            var manifest = new
            {
                schemaVersion = 1, packId = "Microsoft.NETCore.App.Runtime.win-x64", packVersion = "10.0.9",
                runtimeIdentifier = "win-x64", targetFramework = "net10.0",
                referencePack = "Microsoft.NETCore.App.Ref", referencePackVersion = "10.0.9",
                implementationProfile = "corelib-common-il-v1",
                assemblies = new[] { new {
                    name = metadata.GetString(assembly.Name), file = "System.Private.CoreLib.dll",
                    version = assembly.Version.ToString(),
                    publicKeyToken = Convert.ToHexString(AssemblyName.GetAssemblyName(destination).GetPublicKeyToken()!).ToLowerInvariant(),
                    mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid).ToString("D"), sha256 = ExpectedSha256
                } }
            };
            File.WriteAllText(ManifestPath, JsonSerializer.Serialize(manifest));
        }
        catch { Dispose(); throw; }
    }

    public void Dispose()
    {
        // Remove only the unique staging directory owned by this instance.
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CopperSharpNativeCoreLib")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("CoreLib staging directory escaped its temporary root.");
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
