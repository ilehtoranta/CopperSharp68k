/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using CopperSharp.Compiler.Framework;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameworkImplementationPackTests
{
	private static readonly string FixtureAssembly = typeof(CompilerFixtures).Assembly.Location;

	[Fact]
	public void ExperimentalProfileCutsOffExceptionToStringOnlyWhenUnlistedBodiesAreEnabled()
	{
		var member = new FrameworkMemberId(
			FrameworkTypeId.Named("System.Runtime", "System.Exception"),
			"ToString",
			new FrameworkMethodSignatureId(
				0x20,
				0,
				0,
				FrameworkTypeId.Primitive("System.String"),
				[]));

		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: false,
			out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(
			member,
			enableUnlistedManagedBodies: true,
			out var binding));
		Assert.Equal(FrameworkBindingKind.ShadowMethod, binding.Kind);
		Assert.Equal(
			"shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowException::ToString",
			binding.Target);
		Assert.True(binding.PreservesVirtualDispatch);
		Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
	}

	[Fact]
	public void ValidCoreLibPackIsReportedAndSelectsPinnedStopwatchBodies()
	{
		using var pack = CoreLibPack.Create();
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath));

		var provenance = Assert.IsType<M68kFrameworkImplementationPackProvenance>(
			analysis.ImplementationPack);
		Assert.Equal("corelib-common-il-v1", provenance.ImplementationProfile);
		var assembly = Assert.Single(provenance.Assemblies);
		Assert.Equal("System.Private.CoreLib", assembly.Name);
		Assert.Equal(pack.Sha256, assembly.Sha256);
		var stopwatch = analysis.Members
			.Where(static member =>
				member.Member.TypeName == "System.Diagnostics.Stopwatch" &&
				member.Member.Name is ".ctor" or "Start" or "Stop" or "Reset" or
					"Restart" or "StartNew" or "get_IsRunning" or "get_ElapsedTicks")
			.ToArray();
		Assert.NotEmpty(stopwatch);
		Assert.All(stopwatch, member =>
		{
			Assert.Equal("System.Runtime", member.Member.AssemblyName);
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith("pinned:[System.Private.CoreLib]", member.Binding, StringComparison.Ordinal);
		});
	}

	[Fact]
	public void PackEnabledCompilationLinksOnlyReachableCoreLibStopwatchBodies()
	{
		using var pack = CoreLibPack.Create();
		var result = AmigaM68kCompiler.Compile(Request(pack.ManifestPath));

		Assert.Contains("IMPLEMENTATION PACK", result.Map, StringComparison.Ordinal);
		Assert.Contains($"sha256={pack.Sha256}", result.Map, StringComparison.Ordinal);
		Assert.Contains(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.ctor");
		Assert.Contains(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::Reset");
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.cctor");
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name is
				"System.Diagnostics.Stopwatch::Start" or
				"System.Diagnostics.Stopwatch::StartNew" or
				"System.Diagnostics.Stopwatch::Stop" or
				"System.Diagnostics.Stopwatch::Restart" or
				"System.Diagnostics.Stopwatch::get_ElapsedTicks");
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
	}

	[Fact]
	public void PinnedDependenciesRetainPublicIdentityAndPalPrecedence()
	{
		using var pack = CoreLibPack.Create();
		var result = AmigaM68kCompiler.Compile(Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchInstanceEntry"));

		var stopwatch = result.FrameworkAnalysis.Members
			.Where(static member => member.Member.TypeName == "System.Diagnostics.Stopwatch")
			.ToArray();
		Assert.NotEmpty(stopwatch);
		Assert.All(stopwatch, static member =>
			Assert.Equal("System.Runtime", member.Member.AssemblyName));
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.EndsWith("ClockPal::GetTimestamp", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.Diagnostics.Stopwatch::GetTimestamp");
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.Object::.ctor");
	}

	[Fact]
	public void PublicGetTimestampStillUsesTheAmigaPalWithAPack()
	{
		using var pack = CoreLibPack.Create();
		var analysis = AmigaM68kCompiler.AnalyzeFramework(Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchTimestampEntry"));

		var timestamp = Assert.Single(analysis.Members, static member =>
			member.Member.TypeName == "System.Diagnostics.Stopwatch" &&
			member.Member.Name == "GetTimestamp");
		Assert.Equal("System.Runtime", timestamp.Member.AssemblyName);
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, timestamp.Status);
		Assert.Equal("platform:amiga-stopwatch-get-timestamp", timestamp.Binding);
	}

	[Fact]
	public void StopwatchElapsedValuesUseTargetOwnedScalingWhenPackIsConfigured()
	{
		using var pack = CoreLibPack.Create();
		var request = Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchElapsedValuesEntry");
		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);

		var elapsed = Assert.Single(analysis.Members, static member =>
			member.Member.TypeName == "System.Diagnostics.Stopwatch" &&
			member.Member.Name == "get_ElapsedMilliseconds");
		Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, elapsed.Status);
		Assert.Equal("platform:amiga-stopwatch-elapsed-milliseconds", elapsed.Binding);
		var result = AmigaM68kCompiler.Compile(request);
		Assert.DoesNotContain(
			result.Symbols,
			static symbol => symbol.Name == "System.Diagnostics.Stopwatch::.cctor");
	}

	[Fact]
	public void ValidCoreLibPackSelectsPinnedTimeSpanLeafBodies()
	{
		using var pack = CoreLibPack.Create();
		var request = Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortablePinnedTimeSpanEntry");
		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);

		var members = analysis.Members
			.Where(static member => member.Member.TypeName == "System.TimeSpan")
			.ToArray();
		Assert.Equal(14, members.Length);
		Assert.All(
			members.Where(static member => member.Member.Name is
				".ctor" or "FromTicks" or "get_Days" or "get_Hours" or
				"get_Minutes" or "get_Seconds" or "get_Milliseconds"),
			static member => Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status));
		Assert.All(members.Where(static member => member.Member.Name is
			not ".ctor" and not "FromTicks" and not "get_Days" and not "get_Hours" and
			not "get_Minutes" and not "get_Seconds" and not "get_Milliseconds"), static member =>
		{
			Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status);
			Assert.StartsWith(
				"pinned:[System.Private.CoreLib]System.TimeSpan::",
				member.Binding,
				StringComparison.Ordinal);
		});

		var result = AmigaM68kCompiler.Compile(request);
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name == "System.TimeSpan::get_Ticks");
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowTimeSpan::Initialize", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowTimeSpan::Equal", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::LessThan", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GreaterThan", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name == "System.TimeSpan::.cctor");
		Assert.Equal(42, CompilerFixtures.PortablePinnedTimeSpanEntry());
	}

	[Fact]
	public void TimeSpanTotalGettersUseExactPalOverrides()
	{
		using var pack = CoreLibPack.Create();
		var request = Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableTimeSpanTotalsEntry",
			M68kCpuTarget.M68040,
			M68kFloatingPointMode.M68040);
		var analysis = AmigaM68kCompiler.AnalyzeFramework(request);

		var members = analysis.Members.Where(static candidate =>
			candidate.Member.TypeName == "System.TimeSpan").ToArray();
		Assert.Contains(members, static member => member.Member.Name == "FromTicks");
		Assert.Equal(5, members.Count(static member =>
			member.Member.Name.StartsWith("get_Total", StringComparison.Ordinal)));
		Assert.Equal(
			M68kFrameworkCompatibilityStatus.Platform,
			Assert.Single(members, static member => member.Member.Name == "FromTicks").Status);
		Assert.All(members.Where(static member =>
			member.Member.Name != "FromTicks"), static member =>
			Assert.Equal(M68kFrameworkCompatibilityStatus.Platform, member.Status));

		var result = AmigaM68kCompiler.Compile(request);
		Assert.Contains(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowTimeSpan::GetTotalDays", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GetTotalHours", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GetTotalMinutes", StringComparison.Ordinal) ||
			symbol.Name.Contains("ShadowTimeSpan::GetTotalSeconds", StringComparison.Ordinal));
		Assert.Equal(42, CompilerFixtures.PortableTimeSpanTotalsEntry());
	}

	[Fact]
	public void UnchangedPackProducesByteIdenticalOutputAndProvenance()
	{
		using var pack = CoreLibPack.Create();
		var first = AmigaM68kCompiler.Compile(Request(pack.ManifestPath));
		var second = AmigaM68kCompiler.Compile(Request(pack.ManifestPath));

		Assert.Equal(first.Image, second.Image);
		Assert.Equal(first.Map, second.Map);
		var firstPack = Assert.IsType<M68kFrameworkImplementationPackProvenance>(
			first.FrameworkAnalysis.ImplementationPack);
		var secondPack = Assert.IsType<M68kFrameworkImplementationPackProvenance>(
			second.FrameworkAnalysis.ImplementationPack);
		Assert.Equal(firstPack with { Assemblies = [] }, secondPack with { Assemblies = [] });
		Assert.Equal(firstPack.Assemblies.ToArray(), secondPack.Assemblies.ToArray());
	}

	[Fact]
	public void StaticFieldOnlyUseDoesNotLinkCoreLibInitializationOrClockPal()
	{
		using var pack = CoreLibPack.Create();
		var result = AmigaM68kCompiler.Compile(Request(
			pack.ManifestPath,
			"CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchHighResolutionEntry"));

		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ClockPal", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.StartsWith("System.Diagnostics.Stopwatch::", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, static symbol =>
			symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
	}

	[Theory]
	[InlineData("schemaVersion", "2")]
	[InlineData("targetFramework", "net9.0")]
	[InlineData("referencePackVersion", "10.0.8")]
	[InlineData("implementationProfile", "automatic")]
	public void ManifestContractMismatchFailsBeforeAnalysis(string property, string replacement)
	{
		using var pack = CoreLibPack.Create();
		pack.Replace(property, replacement);

		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath)));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains(property, exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ManifestHashMismatchFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly("sha256", new string('0', 64));

		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath)));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains("sha256", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void MalformedManifestFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		File.WriteAllText(pack.ManifestPath, "{ definitely-not-json");

		AssertInvalidPack(pack, "manifest");
	}

	[Fact]
	public void MissingImplementationAssemblyFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		File.Delete(pack.AssemblyPath);

		AssertInvalidPack(pack, "does not exist");
	}

	[Theory]
	[InlineData("name", "Other.CoreLib")]
	[InlineData("version", "0.0.0.0")]
	[InlineData("publicKeyToken", "0000000000000000")]
	[InlineData("mvid", "00000000-0000-0000-0000-000000000001")]
	public void AssemblyMetadataMismatchFailsClosed(string property, string replacement)
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly(property, replacement);

		AssertInvalidPack(pack, property);
	}

	[Fact]
	public void DuplicateCoreLibIdentityFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		var document = JsonNode.Parse(File.ReadAllText(pack.ManifestPath))!.AsObject();
		var assemblies = document["assemblies"]!.AsArray();
		assemblies.Add(assemblies[0]!.DeepClone());
		File.WriteAllText(pack.ManifestPath, document.ToJsonString());

		AssertInvalidPack(pack, "exactly one assembly");
	}

	[Theory]
	[InlineData("../System.Private.CoreLib.dll")]
	[InlineData("sub/../../System.Private.CoreLib.dll")]
	public void AssemblyPathEscapeFailsClosed(string path)
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly("file", path);

		AssertInvalidPack(pack, "escapes its directory");
	}

	[Fact]
	public void AbsoluteAssemblyPathFailsClosed()
	{
		using var pack = CoreLibPack.Create();
		pack.ReplaceAssembly("file", pack.AssemblyPath);

		AssertInvalidPack(pack, "must be relative");
	}

	[Fact]
	public void NoPackRetainsStopwatchShadowFallback()
	{
		var result = AmigaM68kCompiler.Compile(Request(manifestPath: null));

		Assert.Null(result.FrameworkAnalysis.ImplementationPack);
		Assert.Contains(
			result.Symbols,
			static symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
		Assert.DoesNotContain("IMPLEMENTATION PACK", result.Map, StringComparison.Ordinal);
	}

	private static M68kCompilationRequest Request(
		string? manifestPath,
		string entry = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchResetOnlyEntry",
		M68kCpuTarget cpu = M68kCpuTarget.M68000,
		M68kFloatingPointMode floatingPoint = M68kFloatingPointMode.Disabled) => new()
	{
		AssemblyPath = FixtureAssembly,
		EntryPoint = entry,
		Cpu = cpu,
		FloatingPoint = floatingPoint,
		OutputFormat = M68kOutputFormat.Hunk,
		RuntimeProfile = M68kRuntimeProfile.Application,
		Imports = new Dictionary<string, uint>
		{
			[M68kRuntimeImports.Allocate] = 0x2800
		},
		FrameworkImplementationPack = manifestPath is null
			? null
			: new M68kFrameworkImplementationPackOptions(manifestPath)
	};

	private static void AssertInvalidPack(CoreLibPack pack, string expectedText)
	{
		var exception = Assert.Throws<M68kCompilationException>(() =>
			AmigaM68kCompiler.AnalyzeFramework(Request(pack.ManifestPath)));
		Assert.Equal(M68kDiagnosticIds.InvalidInput, exception.DiagnosticId);
		Assert.Contains(expectedText, exception.Message, StringComparison.OrdinalIgnoreCase);
	}

	internal sealed class CoreLibPack : IDisposable
	{
		private CoreLibPack(
			string directory,
			string manifestPath,
			string assemblyPath,
			string sha256)
		{
			Directory = directory;
			ManifestPath = manifestPath;
			AssemblyPath = assemblyPath;
			Sha256 = sha256;
		}

		public string Directory { get; }
		public string ManifestPath { get; }
		public string AssemblyPath { get; }
		public string Sha256 { get; }

		public static CoreLibPack Create()
		{
			var directory = Path.Combine(
				Path.GetTempPath(),
				"CopperSharpCoreLibPackTests",
				Guid.NewGuid().ToString("N"));
			System.IO.Directory.CreateDirectory(directory);
			var source = typeof(object).Assembly.Location;
			var destination = Path.Combine(directory, "System.Private.CoreLib.dll");
			File.Copy(source, destination);
			using var stream = File.OpenRead(destination);
			var sha256 = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
			stream.Position = 0;
			using var peReader = new PEReader(stream, PEStreamOptions.PrefetchEntireImage);
			var reader = peReader.GetMetadataReader();
			var definition = reader.GetAssemblyDefinition();
			var module = reader.GetModuleDefinition();
			var token = Convert.ToHexString(
				AssemblyName.GetAssemblyName(destination).GetPublicKeyToken() ?? [])
				.ToLowerInvariant();
			var manifest = new
			{
				schemaVersion = 1,
				packId = "Microsoft.NETCore.App.Runtime.test",
				packVersion = "10.0.9",
				runtimeIdentifier = "test-host",
				targetFramework = "net10.0",
				referencePack = "Microsoft.NETCore.App.Ref",
				referencePackVersion = "10.0.9",
				implementationProfile = "corelib-common-il-v1",
				assemblies = new[]
				{
					new
					{
						name = reader.GetString(definition.Name),
						file = "System.Private.CoreLib.dll",
						version = definition.Version.ToString(),
						publicKeyToken = token,
						mvid = reader.GetGuid(module.Mvid).ToString("D"),
						sha256
					}
				}
			};
			var manifestPath = Path.Combine(directory, "corelib-pack.json");
			File.WriteAllText(
				manifestPath,
				JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
			return new CoreLibPack(directory, manifestPath, destination, sha256);
		}

		public void Replace(string property, string replacement)
		{
			var document = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
			document[property] = property == "schemaVersion"
				? JsonValue.Create(int.Parse(replacement))
				: JsonValue.Create(replacement);
			File.WriteAllText(ManifestPath, document.ToJsonString());
		}

		public void ReplaceAssembly(string property, string replacement)
		{
			var document = JsonNode.Parse(File.ReadAllText(ManifestPath))!.AsObject();
			document["assemblies"]!.AsArray()[0]!.AsObject()[property] = replacement;
			File.WriteAllText(ManifestPath, document.ToJsonString());
		}

		public void Dispose()
		{
			if (System.IO.Directory.Exists(Directory))
			{
				System.IO.Directory.Delete(Directory, recursive: true);
			}
		}
	}
}
