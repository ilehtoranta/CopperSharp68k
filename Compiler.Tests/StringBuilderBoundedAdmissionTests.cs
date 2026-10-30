/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderBoundedAdmissionTests
{
	[Fact]
	public void PublicDefinitionsAreAdmittedWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		Assert.False(catalog.EnableUnlistedManagedBodies);
		foreach (var member in StringBuilderFrameworkSurface.PublicMembers)
		{
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out var binding), member.DisplayName);
			Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
			Assert.True(FrameworkImplementationProfile.IsPinnedBinding(binding));
			Assert.Contains(FrameworkFeature.StringBuilder, binding.EffectSummary.RequiredFeatures);
		}
	}

	[Fact]
	public void NonPublicDefinitionsRequireAnOwnedImplementationCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
		var definitions = StringBuilderFrameworkSurface.Helpers.Concat(StringBuilderFrameworkSurface.NestedMembers)
			.Except(StringBuilderFrameworkSurface.PublicMembers).ToArray();
		Assert.Equal(30, definitions.Length);
		foreach (var member in definitions)
		{
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, constructor, out _));
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out _), member.DisplayName);
		}
		var unknownCaller = new FrameworkMemberId(caller.DeclaringType, "UnlistedHelper", caller.Signature);
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(definitions[0], null, catalog, unknownCaller, out _));
	}

	[Fact]
	public void AdjacentInputCannotEnableTheBoundedBodyProfile()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		pack.Replace("packVersion", "10.0.10");
		var catalog = Load(pack);
		foreach (var member in StringBuilderFrameworkSurface.PublicMembers)
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
	}

	[Fact]
	public void StableTextGraphAdmitsTheVerifiedClosureWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var request = new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderBenchmarkPresizedTextEntry",
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		};
		var analysis = M68kCompiler.AnalyzeFramework(request);
		Assert.True(analysis.IsCompatible);
		Assert.All(analysis.Members.Where(member => member.Member.TypeName == "System.Text.StringBuilder"),
			member => Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
		var boundary = Assert.Single(analysis.Members.Where(member => member.Member.TypeName == "System.Buffer" && member.Member.Name == "Memmove"));
		Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, boundary.Status);
		Assert.Contains(boundary.CallSites, site => site.RootPath.Any(path => path == "System.Text.StringBuilder::Append"));
		Assert.All(analysis.Members.Where(member => member.Member.TypeName == "System.SR"),
			member => Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
		Assert.All(analysis.Members.Where(member => member.Member.TypeName == "System.OutOfMemoryException"),
			member => Assert.Equal(M68kFrameworkCompatibilityStatus.Implemented, member.Status));
		Assert.DoesNotContain(analysis.Members, member => member.Member.TypeName == "System.SpanHelpers");
		var dispatch = Assert.Single(analysis.Members.Where(member => member.Member.TypeName == "System.Object" && member.Member.Name == "ToString"));
		Assert.Equal("managed:sealed-stringbuilder-tostring", dispatch.Binding);
		var compiled = M68kCompiler.Compile(request);
		Assert.True(compiled.FrameworkAnalysis.IsCompatible);
		Assert.DoesNotContain(compiled.Symbols, symbol => symbol.Name == "System.Object::ToString");
	}

	[Fact]
	public void ReleasedPackPreservesTheOriginalStopwatchSlice()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::PortableStopwatchInstanceEntry",
			IncludedExportNames = [], Imports = new Dictionary<string, uint> { [M68kRuntimeImports.Allocate] = 0x2800 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		Assert.True(result.FrameworkAnalysis.IsCompatible);
		Assert.Contains(result.Symbols, symbol => symbol.Name.EndsWith("ClockPal::GetTimestamp", StringComparison.Ordinal));
		Assert.DoesNotContain(result.Symbols, symbol => symbol.Name.Contains("ShadowStopwatch", StringComparison.Ordinal));
	}

	private static FrameworkImplementationPackCatalog Load(FrameworkImplementationPackTests.CoreLibPack pack) =>
		FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
}
