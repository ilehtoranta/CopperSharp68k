/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class GuidAdmissionTests
{
	[Theory]
	[InlineData("RandomnessRejectionEntry", "NewGuid")]
	[InlineData("Version7RejectionEntry", "CreateVersion7")]
	[InlineData("ProviderRejectionEntry", "ToString")]
	[InlineData("Utf8RejectionEntry", "TryFormat")]
	[InlineData("SpanParseRejectionEntry", "Parse")]
	public void UnlistedGuidCallsRemainFrameworkGaps(string entry, string member)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var request = Request(pack, entry);
		var analysis = M68kCompiler.AnalyzeFramework(request);
		Assert.False(analysis.IsCompatible);
		Assert.Contains(analysis.Members, m => m.Member.TypeName == "System.Guid" && m.Member.Name == member &&
			m.Status == M68kFrameworkCompatibilityStatus.Unsupported);
		Assert.ThrowsAny<Exception>(() => M68kCompiler.Compile(request));
	}

	[Fact]
	public void AdjacentPackCannotEnableGuidAdmission()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		pack.Replace("packVersion", "10.0.10");
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var member = new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Guid"), "ToByteArray",
			new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.SzArray(FrameworkTypeId.Primitive("System.Byte")), []));
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
		using var module = new CompilationModule(typeof(GuidCoreLibFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		Assert.False(module.IsSupportedStructType(new CilType(CilTypeKind.ValueType, 0, "System.Guid")));
	}

	[Fact]
	public void GuidClosureUsesPinnedConstructorAndComparisonWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		Assert.False(catalog.EnableUnlistedManagedBodies);
		var analysis = M68kCompiler.AnalyzeFramework(Request(pack, "BinaryEntry"));
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members.Where(m => m.Status != M68kFrameworkCompatibilityStatus.Implemented).Select(m => m.Member.DisplayName + ": " + m.Reason)));
		foreach (var name in new[] { ".ctor", "CompareTo", "GetResult" })
			Assert.Contains(analysis.Members, m => m.Member.TypeName == "System.Guid" && m.Member.Name == name && m.Binding?.StartsWith("pinned:", StringComparison.Ordinal) == true);
	}

	[Fact]
	public void PrivateHelperRequiresItsExactOwnedCallerAndPublicSignaturesAreExact()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath);
		var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(t =>
			module.Reader.GetString(t.Namespace) == "System" && module.Reader.GetString(t.Name) == "Guid");
		var members = type.GetMethods().Select(h => module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(h), module.GetMethod(h), 0)).ToArray();
		var helper = Assert.Single(members.Where(m => m.Name == "GetResult"));
		var comparison = Assert.Single(members.Where(m => m.Name == "CompareTo" && m.Signature.ParameterTypes.Single().FullMetadataName == "System.Guid"));
		Assert.True(GuidSurface.TryCreatePinnedBinding(helper, comparison, out _));
		Assert.False(GuidSurface.TryCreatePinnedBinding(helper, null, out _));
		Assert.False(GuidSurface.TryCreatePinnedBinding(helper, FrameworkImplementationProfile.Canonicalize(comparison), out _));
		Assert.False(GuidSurface.TryCreatePinnedBinding(helper, new FrameworkMemberId(comparison.DeclaringType, "Unlisted", comparison.Signature), out _));
		foreach (var member in members.Where(m => GuidSurface.TryCreatePinnedBinding(m, null, out _) || GuidSurface.TryCreateShadowBinding(m, out _)))
		{
			var sig = member.Signature;
			Reject(new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Guid"), member.Name, sig));
			Reject(new FrameworkMemberId(member.DeclaringType, "Unlisted", sig));
			Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId((byte)(sig.Header ^ 0x20), 0, sig.RequiredParameterCount, sig.ReturnType, sig.ParameterTypes)));
			Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(sig.Header, 1, sig.RequiredParameterCount, sig.ReturnType, sig.ParameterTypes)));
			Reject(new FrameworkMemberId(member.DeclaringType, member.Name, new FrameworkMethodSignatureId(sig.Header, 0, sig.RequiredParameterCount + 1, sig.ReturnType, sig.ParameterTypes)));
			Reject(new FrameworkMemberId(member.DeclaringType, member.Name, sig, [FrameworkTypeId.Primitive("System.Int32")]));
		}
		static void Reject(FrameworkMemberId member)
		{
			Assert.False(GuidSurface.TryCreatePinnedBinding(member, null, out _), member.DisplayName);
			Assert.False(GuidSurface.TryCreateShadowBinding(member, out _), member.DisplayName);
		}
	}

	private static M68kCompilationRequest Request(FrameworkImplementationPackTests.CoreLibPack pack, string entry) => new()
	{
		AssemblyPath = typeof(GuidCoreLibFixtures).Assembly.Location,
		EntryPoint = $"CopperSharp.Compiler.Tests.GuidCoreLibFixtures::{entry}",
		IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full,
		FloatingPoint = M68kFloatingPointMode.SoftFloat,
		MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
		Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 32768 },
		ManagedAssemblyPaths = [typeof(ShadowGuid).Assembly.Location, typeof(EnvironmentPal).Assembly.Location],
		FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
	};
}
