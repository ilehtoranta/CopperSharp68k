/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using System.Reflection.Metadata;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class InterpolatedHandlerSpecializationTests
{
	[Fact]
	public void TemporaryHandlerStorageLayoutsRetainProviderArrayAndSpanOwner()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CopperSharp.Runtime.ShadowInterpolatedStringHandlerBuffer).Assembly.Location,
			frameworkImplementationPack: catalog);
		foreach (var (assembly, name) in new[] {
			("System.Runtime", "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler"),
			("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowInterpolatedStringHandlerBuffer") })
		{
			var identity = module.ResolveRuntimeTypeIdentity(new CilType(CilTypeKind.ValueType, 0, name), assembly);
			var layout = module.GetRuntimeTypeLayout(identity);
			Assert.Equal(28, layout.Size);
			Assert.Equal(19u, layout.ReferenceBitmap);
			Assert.Equal(new[] { 0, 4, 8, 20, 24 }, layout.FieldOffsets.Values.Order().ToArray());
		}
	}
	[Fact]
	public void EnumPredicatesFoldBothOutcomesForConstructedTypes()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibHandlerGenericEnumBranchEntry");
		var calls = caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call).ToArray();
		Assert.Equal(6, calls.Length);
		foreach (var call in calls)
		{
			var constructed = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
			var raw = module.GetMethod(constructed.Handle);
			var specialized = CilTypePredicateSpecializer.Specialize(raw with { MethodTypeArguments = constructed.MethodTypeArguments }, module);
			Assert.DoesNotContain(specialized.Instructions, instruction => instruction.OpCode == OpCodes.Ldtoken || instruction.OpCode == OpCodes.Callvirt);
			Assert.Single(specialized.Instructions, instruction => instruction.OpCode == OpCodes.Ret);
			var isEnum = module.ResolveRuntimeTypeIdentity(constructed.MethodTypeArguments[0], caller.ModuleName).Type.IsEnum;
			Assert.Equal(isEnum ? 17 : 29, Convert.ToInt32(Assert.Single(specialized.Instructions,
				instruction => instruction.OpCode == OpCodes.Ldc_I4_S).Operand));
			// Exercise both branch polarities, independently of the C# encoding.
			var reversed = raw with { MethodTypeArguments = constructed.MethodTypeArguments, Instructions = raw.Instructions.Select(instruction =>
				instruction.OpCode == OpCodes.Brfalse_S ? instruction with { OpCode = OpCodes.Brtrue_S } :
				instruction.OpCode == OpCodes.Brtrue_S ? instruction with { OpCode = OpCodes.Brfalse_S } : instruction).ToArray() };
			var inverse = CilTypePredicateSpecializer.Specialize(reversed, module);
			Assert.Equal(isEnum ? 29 : 17, Convert.ToInt32(Assert.Single(inverse.Instructions,
				instruction => instruction.OpCode == OpCodes.Ldc_I4_S).Operand));
		}
		// Type-only specialization does not depend on the generic value ABI.
		// Include 64-bit enums and nullable types without passing their values.
		var classification = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibHandlerTypeClassificationEntry");
		var template = module.GetMethod(module.ResolveMethodToken((int)calls[0].Operand!, caller, calls[0].Offset).Definition!.Handle);
		foreach (var token in classification.Instructions.Where(instruction => instruction.OpCode == OpCodes.Ldtoken))
		{
			var type = module.ResolveTypeToken((int)token.Operand!, classification, token.Offset);
			var specialized = CilTypePredicateSpecializer.Specialize(template with { MethodTypeArguments = [type] }, module);
			var isEnum = module.ResolveRuntimeTypeIdentity(type, caller.ModuleName).Type.IsEnum;
			Assert.Equal(isEnum ? 17 : 29, Convert.ToInt32(Assert.Single(specialized.Instructions,
				instruction => instruction.OpCode == OpCodes.Ldc_I4_S).Operand));
		}
	}

	[Fact]
	public void EnumPredicateSpecializationPreservesUnsupportedControlFlowAndRequiresOptIn()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location);
		var caller = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibHandlerGenericEnumBranchEntry");
		var call = caller.Instructions.First(instruction => instruction.OpCode == OpCodes.Call);
		var constructed = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		var raw = module.GetMethod(constructed.Handle);
		Assert.Same(raw, CilTypePredicateSpecializer.Specialize(raw, module));
		var method = raw with { MethodTypeArguments = constructed.MethodTypeArguments };
		Assert.Same(method, CilTypePredicateSpecializer.Specialize(method, ordinary));
		var withException = method with { ExceptionRegions = [new CilExceptionRegion(ExceptionRegionKind.Finally, 0, 1, 1, 1, default, 0)] };
		Assert.Same(withException, CilTypePredicateSpecializer.Specialize(withException, module));
		var sequence = method.Instructions.Take(4).ToArray();
		foreach (var interior in sequence.Skip(1))
		{
			var entered = method with { Instructions = [new CilInstruction(-1, OpCodes.Br, interior.Offset, 0), .. method.Instructions] };
			Assert.Same(entered, CilTypePredicateSpecializer.Specialize(entered, module));
			var switched = method with { Instructions = [new CilInstruction(-1, OpCodes.Switch, new[] { interior.Offset }, 0), .. method.Instructions] };
			Assert.Same(switched, CilTypePredicateSpecializer.Specialize(switched, module));
		}
		var wrongGetter = method with { Instructions = method.Instructions.Select(instruction =>
			instruction.Offset == sequence[2].Offset ? instruction with { Operand = sequence[1].Operand } : instruction).ToArray() };
		Assert.Same(wrongGetter, CilTypePredicateSpecializer.Specialize(wrongGetter, module));
	}

	[Theory]
	[InlineData("GrowCore")]
	[InlineData("Clear")]
	public void TemporaryHandlerStorageOverridesRequireExactSignaturesAndOptIn(string name)
	{
		var result = FrameworkTypeId.Primitive("System.Void");
		var parameters = name == "GrowCore" ? new[] { FrameworkTypeId.Primitive("System.UInt32") } : Array.Empty<FrameworkTypeId>();
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.Runtime.CompilerServices.DefaultInterpolatedStringHandler");
			var signature = new FrameworkMethodSignatureId(0x20, 0, parameters.Length, result, parameters);
			var member = new FrameworkMemberId(owner, name, signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal("CopperSharp.Runtime.ShadowInterpolatedStringHandlerBuffer", binding.ShadowMethod!.TypeName);
			Assert.Equal(name == "GrowCore", binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			foreach (var wrong in new[] {
				new FrameworkMethodSignatureId(0, 0, parameters.Length, result, parameters),
				new FrameworkMethodSignatureId(0x20, 1, parameters.Length, result, parameters),
				new FrameworkMethodSignatureId(0x20, 0, parameters.Length, FrameworkTypeId.Primitive("System.Int32"), parameters),
				new FrameworkMethodSignatureId(0x20, 0, parameters.Length + 1, result, [.. parameters, FrameworkTypeId.Primitive("System.Int32")]) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, wrong), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, signature, [result]), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named("User.Assembly", owner.MetadataName!), name, signature), true, out _));
			if (name == "GrowCore") Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name,
				new FrameworkMethodSignatureId(0x20, 0, 1, result, [FrameworkTypeId.Primitive("System.Int32")])), true, out _));
		}
	}
}
