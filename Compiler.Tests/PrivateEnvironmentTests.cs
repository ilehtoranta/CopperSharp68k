/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class PrivateEnvironmentTests
{
	[Fact]
	public void ProcessorCountLeafRequiresTheExactImplementationSignatureAndOptIn()
	{
		var owner = FrameworkTypeId.Named("System.Private.CoreLib", "System.Environment");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var signature = new FrameworkMethodSignatureId(0, 0, 0, integer, []);
		var member = new FrameworkMemberId(owner, "GetProcessorCount", signature);
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
		Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
		Assert.Equal(FrameworkBindingKind.PlatformOperation, binding.Kind);
		Assert.Equal("platform:amiga-environment-processor-count", binding.Target);
		Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, binding.TypeInitializerPolicy);
		Assert.Equal(FrameworkEffects.None, binding.EffectSummary.Effects);
		Assert.Equal("CopperSharp.Runtime.AmigaPal", binding.ShadowMethod!.AssemblyName);
		foreach (var wrong in new[] {
			new FrameworkMethodSignatureId(0x20, 0, 0, integer, []),
			new FrameworkMethodSignatureId(0, 1, 0, integer, []),
			new FrameworkMethodSignatureId(0, 0, 1, integer, [integer]),
			new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Int64"), []) })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, "GetProcessorCount", wrong), true, out _));
		foreach (var assembly in new[] { "System.Runtime", "Application" })
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named(assembly, "System.Environment"), "GetProcessorCount", signature), true, out _));
		Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, "GetProcessorCount", signature, [integer]), true, out _));
	}

	[Fact]
	public void PrivateAndPublicProcessorCountUseTheSameTargetLeafWithoutHostInitialization()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var path = PrivateEnvironmentFixtureBuilder.Create(pack.Directory);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(path,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("PrivateEnvironmentProbe::Entry");
		var calls = entry.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call).ToArray();
		Assert.Equal(2, calls.Length);
		var targets = calls.Select(call => module.ResolveMethodToken((int)call.Operand!, entry, call.Offset)).ToArray();
		Assert.All(targets, target => Assert.Equal("CopperSharp.Runtime.AmigaPal.EnvironmentPal::GetProcessorCount", target.Definition!.DisplayName));
		Assert.Equal(targets[0].Definition!.Identity, targets[1].Definition!.Identity);
		Assert.Null(module.GetTriggeredTypeInitializer(entry, calls[0]));
	}
}
