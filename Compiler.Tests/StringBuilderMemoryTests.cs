/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderMemoryTests
{
	[Theory]
	[InlineData("System.Memory`1", "System.Span`1")]
	[InlineData("System.ReadOnlyMemory`1", "System.ReadOnlySpan`1")]
	public void CharacterMemorySpanGettersRequireOptInAndExactConstructedSignatures(string memory, string span)
	{
		var character = FrameworkTypeId.Primitive("System.Char");
		var owner = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", memory), [character]);
		var result = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", span), [FrameworkTypeId.GenericTypeParameter(0)]);
		var signature = new FrameworkMethodSignatureId(0x20, 0, 0, result, []);
		bool Pin(FrameworkMemberId member, bool enabled, out FrameworkBinding binding) => FrameworkImplementationProfile.TryCreatePinnedBinding(member,
			new FrameworkBinding(member, FrameworkBindingKind.Intrinsic, "intrinsic:memory-span:char", FrameworkEffectSummary.None), enabled, out binding);
		var member = new FrameworkMemberId(owner, "get_Span", signature);
		Assert.False(Pin(member, false, out _));
		Assert.True(Pin(member, true, out var binding));
		Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
		Assert.False(Pin(new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", memory), [FrameworkTypeId.Primitive("System.Int32")]), "get_Span", signature), true, out _));
		Assert.False(Pin(new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("Application", memory), [character]), "get_Span", signature), true, out _));
		Assert.False(Pin(new FrameworkMemberId(owner, "get_Span", new FrameworkMethodSignatureId(0, 0, 0, result, [])), true, out _));
		Assert.False(Pin(new FrameworkMemberId(owner, "get_Span", new FrameworkMethodSignatureId(0x20, 0, 1, result, [character])), true, out _));
		Assert.False(Pin(new FrameworkMemberId(owner, "get_Span", new FrameworkMethodSignatureId(0x20, 0, 0, character, [])), true, out _));
		Assert.False(Pin(new FrameworkMemberId(owner, "get_Span", signature, [character]), true, out _));
	}

	[Fact]
	public void CharacterMemoryIntrinsicsRequireTheirExactFrameworkSignatures()
	{
		var character = FrameworkTypeId.Primitive("System.Char");
		var parameter = FrameworkTypeId.GenericMethodParameter(0);
		var unsafeType = FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.Unsafe");
		var nullSignature = new FrameworkMethodSignatureId(0x10, 1, 0, FrameworkTypeId.ByReference(parameter), []);
		var members = new List<FrameworkMemberId> { new(unsafeType, "NullRef", nullSignature, [character]) };
		foreach (var target in new[] { FrameworkTypeId.Primitive("System.String"), FrameworkTypeId.SzArray(character), FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Buffers.MemoryManager`1"), [character]) })
			members.Add(new FrameworkMemberId(unsafeType, "As", new FrameworkMethodSignatureId(0x10, 1, 1, parameter, [FrameworkTypeId.Primitive("System.Object")]), [target]));
		members.Add(new FrameworkMemberId(FrameworkTypeId.Named("System.Runtime", "System.Runtime.CompilerServices.RuntimeHelpers"), "ObjectHasComponentSize",
			new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.Boolean"), [FrameworkTypeId.Primitive("System.Object")])));
		foreach (var member in members)
		{
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(FrameworkBindingKind.Intrinsic, binding.Kind);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(FrameworkTypeId.Named("Application", member.DeclaringType.MetadataName!), member.Name, member.Signature, member.MethodTypeArguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(member.DeclaringType, member.Name,
				new FrameworkMethodSignatureId((byte)(member.Signature.Header | 0x20), member.Signature.GenericParameterCount, member.Signature.RequiredParameterCount, member.Signature.ReturnType, member.Signature.ParameterTypes), member.MethodTypeArguments), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")]), true, out _));
		}
	}

	[Fact]
	public void PinnedMemoryConstructorsRetainTheirClosedDeclaringType()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowStringData).Assembly.Location], frameworkImplementationPack: catalog);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateBuilderMemory");
		var factoryCall = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Call &&
			module.DescribeMethodToken((int)instruction.Operand!, caller, instruction.Offset)?.Name == "AsMemory");
		caller = module.ResolveMethodToken((int)factoryCall.Operand!, caller, factoryCall.Offset).Definition!;
		var call = caller.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newobj);
		var constructor = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset);
		Assert.Equal("System.ReadOnlyMemory`1<char>", constructor.ConstructedDeclaringType!.DisplayName);
		Assert.NotNull(constructor.Definition);
		Assert.Equal(constructor.ConstructedDeclaringType, module.GetMethodDeclaringType(constructor.Definition!));
	}

	[Fact]
	public void MemoryManagerApplicationOverridesInheritTheVerifiedCoreLibSlotOrder()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderManagerMemoryAppendEntry");
		var call = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Newobj);
		var constructor = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
		var layout = module.GetTypeLayout(constructor);
		var memoryBase = module.GetExperimentalCharacterMemoryManagerBase(layout)!;
		Assert.Equal("System.Private.CoreLib", memoryBase.ModuleName);
		Assert.Equal("System.Buffers.MemoryManager`1<char>", memoryBase.ConstructedType!.DisplayName);
		Assert.Equal(8, memoryBase.Size); Assert.Equal(0u, memoryBase.ReferenceBitmap);
		var declaration = module.GetVirtualTable(memoryBase).Slots.Single(method => method.Name == "GetSpan");
		var implementation = module.TryGetVirtualImplementation(layout, declaration)!;
		Assert.Equal("BuilderCharacterMemoryManager::GetSpan", implementation.DisplayName);
		Assert.True(module.IsExperimentalCharacterMemoryManagerSpanMethod(implementation));
		Assert.Equal(implementation.Identity, module.GetVirtualTable(layout).Slots[module.GetVirtualSlot(declaration)].Identity);
		Assert.Equal(1u, layout.ReferenceBitmap);
	}
}
