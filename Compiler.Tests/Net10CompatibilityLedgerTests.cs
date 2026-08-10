/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using CopperSharp.Compiler.Tests.PackageDependency;
using CopperSharp.Targets.Amiga;
using Xunit.Sdk;

namespace CopperSharp.Compiler.Tests;

public sealed class Net10CompatibilityLedgerTests
{
	[Fact]
	public void CheckedInCompatibilityLedgerMatchesRepresentativeClosedWorldRoots()
	{
		var ledger = CreateLedger();
		var options = new JsonSerializerOptions
		{
			WriteIndented = true,
			DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
			Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
		};
		var json = JsonSerializer.Serialize(ledger, options);
		var expectedPath = Path.Combine(
			AppContext.BaseDirectory,
			"Baselines",
			"net10.0-compatibility-ledger.json");
		if (!File.Exists(expectedPath))
		{
			throw WriteActualLedger(json, "The checked-in compatibility ledger is missing");
		}

		var expected = JsonNode.Parse(File.ReadAllText(expectedPath));
		var actual = JsonNode.Parse(json);
		if (!JsonNode.DeepEquals(expected, actual))
		{
			throw WriteActualLedger(json, "The checked-in compatibility ledger is stale");
		}

		Assert.Equal("net10.0", ledger.Contract.TargetFramework);
		Assert.Equal("10.0.9", ledger.Contract.ReferencePackVersion);
		Assert.All(ledger.Roots, static root =>
		{
			Assert.NotEmpty(root.EntryPoint);
			Assert.NotEmpty(root.ManagedAssembly.Name);
			Assert.NotEmpty(root.ManagedAssembly.Version);
			Assert.NotEmpty(root.Owner);
			Assert.All(root.Members, member =>
			{
				if (member.Status is
					M68kFrameworkCompatibilityStatus.Implemented or
					M68kFrameworkCompatibilityStatus.Intrinsic or
					M68kFrameworkCompatibilityStatus.Platform)
				{
					Assert.False(string.IsNullOrWhiteSpace(member.Binding));
				}
				else
				{
					Assert.False(string.IsNullOrWhiteSpace(member.Reason));
					Assert.Contains(
						root.NonSupportedMembers,
						disposition => disposition.Member == member.Member);
				}
			});
		});
	}

	private static CompatibilityCorpusLedger CreateLedger()
	{
		var specifications = new[]
		{
			Fixture(
				"portable-strings",
				"IntegerFormatStringEntry"),
			Fixture(
				"spans-and-memory",
				"MemoryCopyOperationsEntry"),
			Fixture(
				"collections",
				"DictionaryStringGcEntry"),
			Fixture(
				"linq",
				"LinqDictionaryValuesOrderByThenByEntry"),
			Fixture(
				"delegates",
				"MulticastDelegateRemoveEntry"),
			Fixture(
				"exceptions",
				"CustomExceptionCatchEntry"),
			IffInspect(),
			Fixture(
				"excluded-string-overload",
				"UnsupportedStringConcatRootEntry",
				"profile-exclusion",
				"framework-profile"),
			Fixture(
				"excluded-memory-materialization",
				"UnsupportedMemoryToArrayEntry",
				"profile-exclusion",
				"framework-profile"),
			Fixture(
				"excluded-linq-provenance-merge",
				"UnsupportedLinqMixedFactoryMergeEntry",
				"profile-exclusion",
				"framework-profile"),
			Fixture(
				"missing-directory-create-pal",
				"UnsupportedDirectoryCreateEntry",
				"pal-gap",
				"Runtime.AmigaPal"),
			PackageDependencyIncompatible()
		};

		var roots = specifications.Select(CreateRoot).ToArray();
		var contract = roots[0].Contract;
		Assert.All(roots, root => Assert.Equal(contract, root.Contract));
		return new CompatibilityCorpusLedger(
			SchemaVersion: 1,
			Compiler: PackageIdentity.For("CopperSharp.Compiler", typeof(M68kCompiler).Assembly),
			Target: PackageIdentity.For("CopperSharp.Targets.Amiga", typeof(AmigaM68kCompiler).Assembly),
			Contract: contract,
			Roots: roots,
			Summary: new LedgerSummary(
				roots.Length,
				roots.Count(static root => root.IsCompatible),
				roots.Count(static root => !root.IsCompatible),
				roots.Sum(static root => root.Members.Count),
				roots.Sum(static root => root.NonSupportedMembers.Count)));
	}

	private static CorpusRoot CreateRoot(RootSpecification specification)
	{
		var request = specification.Request();
		M68kFrameworkAnalysisResult analysis;
		analysis = AmigaM68kCompiler.AnalyzeFramework(request, specification.Options);

		Assert.Equal(
			specification.ExpectedClassification == "implemented",
			analysis.IsCompatible);
		var nonSupported = analysis.Members
			.Where(static member => member.Status is
				M68kFrameworkCompatibilityStatus.Deferred or
				M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => new NonSupportedDisposition(
				member.Member.DisplayName,
				specification.ExpectedClassification,
				specification.Owner,
				member.Reason!,
				member.CallSites))
			.ToArray();
		if (!analysis.IsCompatible)
		{
			Assert.NotEmpty(nonSupported);
		}

		var assemblyName = AssemblyName.GetAssemblyName(request.AssemblyPath);
		return new CorpusRoot(
			specification.Id,
			new ManagedAssemblyIdentity(
				assemblyName.Name ?? throw new InvalidOperationException("Assembly name is missing."),
				assemblyName.Version?.ToString() ?? "unknown"),
			request.EntryPoint ?? throw new InvalidOperationException("Corpus roots require an explicit entry point."),
			"amiga-m68k",
			request.Cpu,
			request.RuntimeProfile,
			request.OutputFormat,
			specification.ExpectedClassification,
			specification.Owner,
			analysis.IsCompatible,
			analysis.Contract,
			analysis.ImplementationPack,
			analysis.Members.Select(static member => new LedgerMember(
				member.Member.DisplayName,
				member.Status,
				member.Binding,
				member.Reason,
				member.Effects,
				member.RequiredFeatures)).ToArray(),
			analysis.ManagedAllocationSites.Select(static site => new LedgerAllocationSite(
				site.Caller,
				site.IlOffset,
				site.Kind,
				StableAllocatedType(site.AllocatedType))).ToArray(),
			analysis.Members
				.SelectMany(static member => member.RequiredFeatures)
				.Distinct(StringComparer.Ordinal)
				.Order(StringComparer.Ordinal)
				.ToArray(),
			nonSupported,
			specification.MetricsReference);
	}

	private static string StableAllocatedType(string allocatedType) =>
		allocatedType.StartsWith("<>c__DisplayClass", StringComparison.Ordinal)
			? "<compiler-generated-display-class>"
			: allocatedType;

	private static RootSpecification Fixture(
		string id,
		string method,
		string classification = "implemented",
		string owner = "CopperSharp.Compiler",
		string? metricsReference = null) =>
		new(
			id,
			() => new M68kCompilationRequest
			{
				AssemblyPath = Assembly.GetExecutingAssembly().Location,
				EntryPoint = $"CopperSharp.Compiler.Tests.CompilerFixtures::{method}",
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application,
				Imports = new Dictionary<string, uint>
				{
					[M68kRuntimeImports.Allocate] = 0x0000_2800
				}
			},
			new AmigaCompilationOptions(),
			classification,
			owner,
			metricsReference);

	private static RootSpecification IffInspect() =>
		new(
			"amiga-pal-iff",
			() => new M68kCompilationRequest
			{
				AssemblyPath = typeof(IFFInspect.Program).Assembly.Location,
				EntryPoint = "IFFInspect.Program::Main",
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				Heap = new M68kHeapOptions
				{
					StartAddress = 0x0001_0000,
					Size = 0x0000_8000
				}
			},
			new AmigaCompilationOptions
			{
				LibraryBases = new Dictionary<string, uint>
				{
					["exec.library"] = 0x0000_0400
				}
			},
			"implemented",
			"Runtime.AmigaPal",
			"Compiler.Tests/Baselines/net10.0-profile-baseline.json#example:IFFInspect");

	private static RootSpecification PackageDependencyIncompatible() =>
		new(
			"package-dependency-incompatible",
			() => new M68kCompilationRequest
			{
				AssemblyPath = typeof(PortableDependency).Assembly.Location,
				EntryPoint =
					"CopperSharp.Compiler.Tests.PackageDependency.PortableDependency::UnsupportedAnswer",
				Cpu = M68kCpuTarget.M68000,
				OutputFormat = M68kOutputFormat.Hunk,
				RuntimeProfile = M68kRuntimeProfile.Application
			},
			new AmigaCompilationOptions(),
			"dependency-incompatible",
			"CopperSharp.Compiler.Tests.PackageDependency",
			MetricsReference: null);

	private static XunitException WriteActualLedger(string json, string message)
	{
		var path = Path.Combine(
			Path.GetTempPath(),
			$"CopperSharp-net10-compatibility-ledger-{Guid.NewGuid():N}.json");
		File.WriteAllText(path, json);
		return new XunitException($"{message}. Generated actual ledger: {path}");
	}

	private sealed record RootSpecification(
		string Id,
		Func<M68kCompilationRequest> Request,
		AmigaCompilationOptions Options,
		string ExpectedClassification,
		string Owner,
		string? MetricsReference);

	private sealed record CompatibilityCorpusLedger(
		int SchemaVersion,
		PackageIdentity Compiler,
		PackageIdentity Target,
		M68kFrameworkContract Contract,
		IReadOnlyList<CorpusRoot> Roots,
		LedgerSummary Summary);

	private sealed record PackageIdentity(string Name, string Version)
	{
		public static PackageIdentity For(string name, Assembly assembly)
		{
			var version = assembly
				.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
				.InformationalVersion;
			if (string.IsNullOrWhiteSpace(version))
			{
				version = assembly.GetName().Version?.ToString() ?? "unknown";
			}
			var separator = version.IndexOf('+');
			return new PackageIdentity(
				name,
				separator < 0 ? version : version[..separator]);
		}
	}

	private sealed record ManagedAssemblyIdentity(string Name, string Version);

	private sealed record CorpusRoot(
		string Id,
		ManagedAssemblyIdentity ManagedAssembly,
		string EntryPoint,
		string RuntimeIdentifier,
		M68kCpuTarget Cpu,
		M68kRuntimeProfile RuntimeProfile,
		M68kOutputFormat OutputFormat,
		string FinalClassification,
		string Owner,
		bool IsCompatible,
		[property: JsonIgnore] M68kFrameworkContract Contract,
		M68kFrameworkImplementationPackProvenance? ImplementationPack,
		IReadOnlyList<LedgerMember> Members,
		IReadOnlyList<LedgerAllocationSite> ManagedAllocationSites,
		IReadOnlyList<string> RequiredFeatures,
		IReadOnlyList<NonSupportedDisposition> NonSupportedMembers,
		string? MetricsReference);

	private sealed record NonSupportedDisposition(
		string Member,
		string Classification,
		string Owner,
		string Reason,
		IReadOnlyList<M68kFrameworkCallSite> CallSites);

	private sealed record LedgerMember(
		string Member,
		M68kFrameworkCompatibilityStatus Status,
		string? Binding,
		string? Reason,
		IReadOnlyList<string> Effects,
		IReadOnlyList<string> RequiredFeatures);

	private sealed record LedgerAllocationSite(
		string Caller,
		int IlOffset,
		string Kind,
		string AllocatedType);

	private sealed record LedgerSummary(
		int Roots,
		int CompatibleRoots,
		int NonCompatibleRoots,
		int ReachableMemberOccurrences,
		int NonSupportedMemberOccurrences);
}
