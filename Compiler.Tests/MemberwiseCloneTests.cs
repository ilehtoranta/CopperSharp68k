/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class MemberwiseCloneTests
{
	[Fact]
	public void MemberwiseCloneRequiresExactFrameworkIdentityAndOptIn()
	{
		var result = FrameworkTypeId.Primitive("System.Object");
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.Object");
			var signature = new FrameworkMethodSignatureId(0x20, 0, 0, result, []);
			var member = new FrameworkMemberId(owner, "MemberwiseClone", signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(FrameworkBindingKind.Intrinsic, binding.Kind);
			Assert.Equal("intrinsic:runtime-object-memberwise-clone", binding.Target);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
			foreach (var wrong in new[] {
				new FrameworkMethodSignatureId(0, 0, 0, result, []),
				new FrameworkMethodSignatureId(0x20, 1, 0, result, []),
				new FrameworkMethodSignatureId(0x20, 0, 1, result, [result]),
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Int32"), []) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, "MemberwiseClone", wrong), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named("Application", "System.Object"), "MemberwiseClone", signature), true, out _));
		}
	}

	[Fact]
	public void ProviderCloningIsReportedAsManagedAllocation()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderCultureNumberCloneEntry",
			ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.ShadowCultureInfo).Assembly.Location],
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }
		});
		Assert.Contains(analysis.ManagedAllocationSites, site => site.Kind == "clone" && site.AllocatedType == "object");
	}
}
