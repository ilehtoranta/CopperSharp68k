/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Tests;

public sealed class PointerStringTests
{
	[Fact]
	public void PointerStringConstructorBindingRequiresAnExactFrameworkSignatureAndOptIn()
	{
		var characters = FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Char"));
		var result = FrameworkTypeId.Primitive("System.Void");
		var signature = new FrameworkMethodSignatureId(0x20, 0, 1, result, [characters]);
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.String");
			var member = new FrameworkMemberId(owner, ".ctor", signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal("FromNullTerminatedCharacters", binding.ShadowMethod!.MethodName);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.ReadsNativeMemory));
			Assert.False(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.RetainsNativePointer));
			Assert.Contains(FrameworkFeature.NativeMemory, binding.EffectSummary.RequiredFeatures);
			Assert.DoesNotContain(FrameworkFeature.Spans, binding.EffectSummary.RequiredFeatures);
			foreach (var wrong in new[] {
				new FrameworkMethodSignatureId(0, 0, 1, result, [characters]),
				new FrameworkMethodSignatureId(0x20, 1, 1, result, [characters]),
				new FrameworkMethodSignatureId(0x20, 0, 2, result, [characters, characters]),
				new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.String"), [characters]),
				new FrameworkMethodSignatureId(0x20, 0, 1, result, [FrameworkTypeId.Pointer(FrameworkTypeId.Primitive("System.Byte"))]) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, ".ctor", wrong), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, ".ctor", signature,
				[FrameworkTypeId.Primitive("System.Char")]), true, out _));
		}
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
			FrameworkTypeId.Named("Application", "System.String"), ".ctor", signature), true, out _));
	}
}
