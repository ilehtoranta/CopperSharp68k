/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;

namespace CopperSharp.Compiler.Framework;

internal sealed class FrameworkImplementationPackCatalog
{
	private readonly IReadOnlyDictionary<string, string> _assemblyPaths;

	public FrameworkImplementationPackCatalog(
		IReadOnlyDictionary<string, string> assemblyPaths,
		M68kFrameworkImplementationPackProvenance provenance)
	{
		_assemblyPaths = assemblyPaths;
		Provenance = provenance;
	}

	public M68kFrameworkImplementationPackProvenance Provenance { get; }

	public string ImplementationProfile => Provenance.ImplementationProfile;

	public bool TryGetAssemblyPath(string assemblyName, out string path) =>
		_assemblyPaths.TryGetValue(assemblyName, out path!);
}

internal static class FrameworkImplementationPackLoader
{
	public const int SchemaVersion = 1;
	public const string TargetFramework = "net10.0";
	public const string ReferencePack = "Microsoft.NETCore.App.Ref";
	public const string ReferencePackVersion = "10.0.9";
	public const string CoreLibProfile = "corelib-common-il-v1";
	private const string CoreLibAssembly = "System.Private.CoreLib";

	public static FrameworkImplementationPackCatalog? Load(
		M68kFrameworkImplementationPackOptions? options)
	{
		if (options is null)
		{
			return null;
		}
		if (string.IsNullOrWhiteSpace(options.ManifestPath))
		{
			throw Invalid("Framework implementation manifest path is empty.");
		}

		var manifestPath = Path.GetFullPath(options.ManifestPath);
		if (!File.Exists(manifestPath))
		{
			throw Invalid($"Framework implementation manifest '{manifestPath}' does not exist.");
		}

		ImplementationPackManifest manifest;
		try
		{
			using var stream = File.OpenRead(manifestPath);
			manifest = JsonSerializer.Deserialize<ImplementationPackManifest>(
				stream,
				new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ??
				throw Invalid($"Framework implementation manifest '{manifestPath}' is empty.");
		}
		catch (M68kCompilationException)
		{
			throw;
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or JsonException)
		{
			throw Invalid(
				$"Could not read framework implementation manifest '{manifestPath}': {exception.Message}",
				exception);
		}

		ValidateManifest(manifest, manifestPath);
		var directory = Path.GetDirectoryName(manifestPath)!;
		var paths = new Dictionary<string, string>(StringComparer.Ordinal);
		var provenance = new List<M68kFrameworkImplementationAssemblyProvenance>();
		foreach (var assembly in manifest.Assemblies)
		{
			var path = ResolveContainedPath(directory, assembly.File, manifestPath);
			var verified = VerifyAssembly(path, assembly, manifestPath);
			if (!paths.TryAdd(verified.Name, path))
			{
				throw Invalid(
					$"Framework implementation manifest '{manifestPath}' contains duplicate assembly identity '{verified.Name}'.");
			}
			provenance.Add(verified);
		}

		return new FrameworkImplementationPackCatalog(
			paths,
			new M68kFrameworkImplementationPackProvenance(
				manifest.SchemaVersion,
				manifest.PackId,
				manifest.PackVersion,
				manifest.RuntimeIdentifier,
				manifest.TargetFramework,
				manifest.ReferencePack,
				manifest.ReferencePackVersion,
				manifest.ImplementationProfile,
				provenance));
	}

	private static void ValidateManifest(
		ImplementationPackManifest manifest,
		string manifestPath)
	{
		RequireEqual("schemaVersion", SchemaVersion, manifest.SchemaVersion, manifestPath);
		RequireText("packId", manifest.PackId, manifestPath);
		RequireText("packVersion", manifest.PackVersion, manifestPath);
		RequireText("runtimeIdentifier", manifest.RuntimeIdentifier, manifestPath);
		RequireEqual("targetFramework", TargetFramework, manifest.TargetFramework, manifestPath);
		RequireEqual("referencePack", ReferencePack, manifest.ReferencePack, manifestPath);
		RequireEqual(
			"referencePackVersion",
			ReferencePackVersion,
			manifest.ReferencePackVersion,
			manifestPath);
		RequireEqual(
			"implementationProfile",
			CoreLibProfile,
			manifest.ImplementationProfile,
			manifestPath);
		if (manifest.Assemblies is not [var assembly])
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' must contain exactly one assembly.");
		}
		RequireEqual("assemblies[0].name", CoreLibAssembly, assembly.Name, manifestPath);
		RequireText("assemblies[0].file", assembly.File, manifestPath);
		RequireText("assemblies[0].version", assembly.Version, manifestPath);
		RequireHex("assemblies[0].publicKeyToken", assembly.PublicKeyToken, 16, manifestPath);
		if (!Guid.TryParse(assembly.Mvid, out _))
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' field 'assemblies[0].mvid' " +
				$"has invalid value '{assembly.Mvid}'.");
		}
		RequireHex("assemblies[0].sha256", assembly.Sha256, 64, manifestPath);
	}

	private static string ResolveContainedPath(
		string directory,
		string relativePath,
		string manifestPath)
	{
		if (Path.IsPathFullyQualified(relativePath))
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' assembly file '{relativePath}' must be relative.");
		}
		var path = Path.GetFullPath(Path.Combine(directory, relativePath));
		var relative = Path.GetRelativePath(directory, path);
		if (relative == ".." ||
			relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
			Path.IsPathFullyQualified(relative))
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' assembly file '{relativePath}' escapes its directory.");
		}
		if (!File.Exists(path))
		{
			throw Invalid(
				$"Framework implementation assembly '{path}' declared by '{manifestPath}' does not exist.");
		}
		RejectReparsePoints(directory, path, manifestPath);
		return path;
	}

	private static void RejectReparsePoints(
		string directory,
		string path,
		string manifestPath)
	{
		FileSystemInfo? current = new FileInfo(path);
		while (current is not null &&
			!string.Equals(current.FullName, directory, StringComparison.OrdinalIgnoreCase))
		{
			if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
			{
				throw Invalid(
					$"Framework implementation manifest '{manifestPath}' assembly path '{path}' contains a symbolic link or reparse point.");
			}
			current = current switch
			{
				FileInfo file => file.Directory,
				DirectoryInfo parent => parent.Parent,
				_ => null
			};
		}
	}

	private static M68kFrameworkImplementationAssemblyProvenance VerifyAssembly(
		string path,
		ImplementationAssemblyManifest expected,
		string manifestPath)
	{
		try
		{
			using var stream = File.OpenRead(path);
			var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			RequireEqual("assemblies[0].sha256", expected.Sha256.ToLowerInvariant(), sha256, manifestPath);
			stream.Position = 0;
			using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
			if (!peReader.HasMetadata)
			{
				throw Invalid($"Framework implementation assembly '{path}' is not a managed PE image.");
			}
			var reader = peReader.GetMetadataReader();
			var definition = reader.GetAssemblyDefinition();
			var module = reader.GetModuleDefinition();
			var name = reader.GetString(definition.Name);
			var version = definition.Version.ToString();
			var mvid = reader.GetGuid(module.Mvid);
			var token = GetPublicKeyToken(reader.GetBlobBytes(definition.PublicKey));
			RequireEqual("assemblies[0].name", expected.Name, name, manifestPath);
			RequireEqual("assemblies[0].version", expected.Version, version, manifestPath);
			RequireEqual(
				"assemblies[0].publicKeyToken",
				expected.PublicKeyToken.ToLowerInvariant(),
				token,
				manifestPath);
			RequireEqual(
				"assemblies[0].mvid",
				Guid.Parse(expected.Mvid),
				mvid,
				manifestPath);
			return new M68kFrameworkImplementationAssemblyProvenance(
				name,
				version,
				token,
				mvid,
				sha256);
		}
		catch (M68kCompilationException)
		{
			throw;
		}
		catch (Exception exception) when (
			exception is IOException or UnauthorizedAccessException or BadImageFormatException)
		{
			throw Invalid(
				$"Could not verify framework implementation assembly '{path}': {exception.Message}",
				exception);
		}
	}

	private static string GetPublicKeyToken(byte[] publicKey)
	{
		if (publicKey.Length == 0)
		{
			return string.Empty;
		}
		var hash = SHA1.HashData(publicKey);
		Span<byte> token = stackalloc byte[8];
		for (var index = 0; index < token.Length; index++)
		{
			token[index] = hash[hash.Length - 1 - index];
		}
		return Convert.ToHexString(token).ToLowerInvariant();
	}

	private static void RequireText(string field, string value, string manifestPath)
	{
		if (string.IsNullOrWhiteSpace(value) || value.IndexOfAny(['\r', '\n']) >= 0)
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' field '{field}' must be a non-empty single-line value.");
		}
	}

	private static void RequireHex(
		string field,
		string value,
		int length,
		string manifestPath)
	{
		if (value.Length != length || value.Any(static character => !Uri.IsHexDigit(character)))
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' field '{field}' must contain exactly {length} hexadecimal characters.");
		}
	}

	private static void RequireEqual<T>(
		string field,
		T expected,
		T actual,
		string manifestPath)
		where T : notnull
	{
		if (!EqualityComparer<T>.Default.Equals(expected, actual))
		{
			throw Invalid(
				$"Framework implementation manifest '{manifestPath}' field '{field}' expected '{expected}' but found '{actual}'.");
		}
	}

	private static M68kCompilationException Invalid(
		string message,
		Exception? exception = null) =>
		new(M68kDiagnosticIds.InvalidInput, message, innerException: exception);

	private sealed class ImplementationPackManifest
	{
		public int SchemaVersion { get; init; }
		public string PackId { get; init; } = string.Empty;
		public string PackVersion { get; init; } = string.Empty;
		public string RuntimeIdentifier { get; init; } = string.Empty;
		public string TargetFramework { get; init; } = string.Empty;
		public string ReferencePack { get; init; } = string.Empty;
		public string ReferencePackVersion { get; init; } = string.Empty;
		public string ImplementationProfile { get; init; } = string.Empty;
		public List<ImplementationAssemblyManifest> Assemblies { get; init; } = [];
	}

	private sealed class ImplementationAssemblyManifest
	{
		public string Name { get; init; } = string.Empty;
		public string File { get; init; } = string.Empty;
		public string Version { get; init; } = string.Empty;
		public string PublicKeyToken { get; init; } = string.Empty;
		public string Mvid { get; init; } = string.Empty;
		public string Sha256 { get; init; } = string.Empty;
	}
}
