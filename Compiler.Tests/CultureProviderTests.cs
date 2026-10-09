/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class CultureProviderTests
{
	[Theory]
	[InlineData("get_CurrentCulture")]
	[InlineData("set_CurrentCulture")]
	[InlineData("get_DefaultThreadCurrentCulture")]
	[InlineData("set_DefaultThreadCurrentCulture")]
	public void AmbientCultureBindingsRequireExactStaticFrameworkSignaturesAndOptIn(string name)
	{
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var culture = FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo");
			var owner = FrameworkTypeId.Named(assembly, "System.Globalization.CultureInfo");
			bool setter = name.StartsWith("set_", StringComparison.Ordinal);
			var signature = new FrameworkMethodSignatureId(0, 0, setter ? 1 : 0,
				setter ? FrameworkTypeId.Primitive("System.Void") : culture, setter ? [culture] : []);
			var member = new FrameworkMemberId(owner, name, signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, binding.TypeInitializerPolicy);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			var instance = new FrameworkMethodSignatureId(0x20, 0, setter ? 1 : 0,
				setter ? FrameworkTypeId.Primitive("System.Void") : culture, setter ? [culture] : []);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, instance), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(FrameworkTypeId.Named("Application", "System.Globalization.CultureInfo"), name, signature), true, out _));
		}
	}

	[Fact]
	public void InvariantCultureBindingRequiresTheExactStaticFrameworkSignature()
	{
		var result = FrameworkTypeId.Named("System.Runtime", "System.Globalization.CultureInfo");
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.Globalization.CultureInfo");
			var signature = new FrameworkMethodSignatureId(0, 0, 0, result, []);
			var member = new FrameworkMemberId(owner, "get_InvariantCulture", signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal("shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCultureInfo::GetInvariantCulture", binding.Target);
			Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, binding.TypeInitializerPolicy);
			Assert.False(binding.PreservesVirtualDispatch);
			foreach (var wrong in new[] {
				new FrameworkMethodSignatureId(0x20, 0, 0, result, []),
				new FrameworkMethodSignatureId(0, 1, 0, result, []),
				new FrameworkMethodSignatureId(0, 0, 1, result, [result]),
				new FrameworkMethodSignatureId(0, 0, 0, FrameworkTypeId.Primitive("System.Object"), []) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, "get_InvariantCulture", wrong), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named("Application", "System.Globalization.CultureInfo"), "get_InvariantCulture", signature), true, out _));
		}
	}

	[Fact]
	public void PrivateReadOnlyWritesUseTheLoadedCoreLibBooleanFieldsAndRequireOptIn()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		foreach (var (methodName, owner) in new[] { ("GetInvariantCulture", "System.Globalization.CultureInfo"), ("GetNumberFormat", "System.Globalization.NumberFormatInfo") })
		{
			var caller = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo::" + methodName);
			var freezeCall = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call &&
				module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset).ImportName?.StartsWith("intrinsic:runtime-freeze-number-provider:", StringComparison.Ordinal) == true);
			var target = module.ResolveMethodToken((int)freezeCall.Operand!, caller, freezeCall.Offset);
			var field = module.GetExperimentalReadOnlyField(owner);
			Assert.Equal("bool", field.Type.DisplayName);
			Assert.False(field.IsStatic);
			Assert.Equal("System.Private.CoreLib", field.ModuleName);
			Assert.Equal("intrinsic:runtime-freeze-number-provider:" + module.GetTypeLayout(field).FieldOffsets[field.Handle], target.ImportName);
			Assert.Null(target.Definition);
		}
		Assert.Throws<M68kCompilationException>(() => module.GetExperimentalReadOnlyField("Application.CultureInfo"));
		using var stable = Open(pack, false);
		Assert.Throws<M68kCompilationException>(() => stable.GetExperimentalReadOnlyField("System.Globalization.CultureInfo"));
		var raw = stable.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowCultureInfo::GetInvariantCulture");
		Assert.DoesNotContain(raw.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => stable.ResolveMethodToken((int)instruction.Operand!, raw, instruction.Offset)),
			target => target.ImportName?.StartsWith("intrinsic:runtime-freeze-number-provider:", StringComparison.Ordinal) == true);
	}

	[Theory]
	[InlineData(".ctor", false)]
	[InlineData(".ctor", true)]
	[InlineData("get_NumberFormat", false)]
	[InlineData("set_NumberFormat", false)]
	public void CultureStorageBindingsRequireExactSignaturesAndOptIn(string name, bool overrides)
	{
		var text = FrameworkTypeId.Primitive("System.String");
		var number = FrameworkTypeId.Named("System.Runtime", "System.Globalization.NumberFormatInfo");
		var result = name == "get_NumberFormat" ? number : FrameworkTypeId.Primitive("System.Void");
		FrameworkTypeId[] arguments = name switch
		{
			".ctor" => overrides ? [text, FrameworkTypeId.Primitive("System.Boolean")] : [text],
			"set_NumberFormat" => [number],
			_ => []
		};
		foreach (var assembly in new[] { "System.Runtime", "System.Private.CoreLib" })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.Globalization.CultureInfo");
			var signature = new FrameworkMethodSignatureId(0x20, 0, arguments.Length, result, arguments);
			var member = new FrameworkMemberId(owner, name, signature);
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, false, out _));
			Assert.True(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(member, true, out var binding));
			Assert.Equal(name != ".ctor", binding.PreservesVirtualDispatch);
			Assert.Equal(FrameworkTypeInitializerPolicy.TargetOwned, binding.TypeInitializerPolicy);
			foreach (var wrong in new[] {
				new FrameworkMethodSignatureId(0, 0, arguments.Length, result, arguments),
				new FrameworkMethodSignatureId(0x20, 1, arguments.Length, result, arguments),
				new FrameworkMethodSignatureId(0x20, 0, arguments.Length + 1, result, [.. arguments, text]),
				new FrameworkMethodSignatureId(0x20, 0, arguments.Length, FrameworkTypeId.Primitive("System.Int32"), arguments) })
				Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(owner, name, wrong), true, out _));
			Assert.False(FrameworkImplementationProfile.TryCreateTargetRuntimeOverride(new FrameworkMemberId(
				FrameworkTypeId.Named("Application", "System.Globalization.CultureInfo"), name, signature), true, out _));
		}
	}

	[Fact]
	public void CultureConstructorAdapterAllocatesTheOfficialTypeAndSuppressesHostInitialization()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderCultureExactEntry");
		var allocation = entry.Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj);
		var constructor = module.ResolveMethodToken((int)allocation.Operand!, entry, allocation.Offset);
		Assert.Equal("CopperSharp.Runtime.ShadowCultureInfo::InitializeName", constructor.Definition!.DisplayName);
		Assert.Equal("System.Globalization.CultureInfo", module.GetAllocationLayout(constructor).DisplayName);
		Assert.Equal("System.Private.CoreLib", module.GetAllocationLayout(constructor).ModuleName);
		Assert.False(constructor.IsConstructorFactory);
		Assert.Null(module.GetTriggeredTypeInitializer(entry, allocation));
	}

	[Fact]
	public void DerivedCultureRetainsBaseStorageAndGcReferencesWithoutMixingFieldHandles()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var derived = module.GetTypeLayout(module.ResolveEntryPoint("HandlerDerivedCulture::GetFormat"));
		var culture = Assert.IsType<CilTypeLayout>(module.GetExperimentalCultureBase(derived));
		Assert.Equal("System.Private.CoreLib", culture.ModuleName);
		Assert.Equal(60, culture.Size);
		Assert.Equal(0x1F7Eu, culture.ReferenceBitmap);
		Assert.Equal(culture.Size + 12, derived.Size);
		Assert.Equal(culture.ReferenceBitmap | (1u << ((culture.Size - 8) / 4)), derived.ReferenceBitmap);
		Assert.Equal(3, derived.FieldOffsets.Count);
		var info = module.ResolveManagedField(derived.ModuleName, "HandlerDerivedCulture", "Info");
		Assert.Equal(culture.Size, derived.FieldOffsets[info.Handle]);
		var leaf = module.GetTypeLayout(module.ResolveEntryPoint("HandlerCultureLeaf::ToString"));
		Assert.Equal(derived.Size + 4, leaf.Size);
		Assert.Equal(derived.ReferenceBitmap | (1u << ((derived.Size - 8) / 4)), leaf.ReferenceBitmap);
		Assert.Equal(4, leaf.FieldOffsets.Count);
		Assert.Equal(culture.Size, leaf.FieldOffsets[info.Handle]);
		using var stable = Open(pack, false);
		var stableLayout = stable.GetTypeLayout(stable.ResolveEntryPoint("HandlerDerivedCulture::GetFormat"));
		Assert.Equal(20, stableLayout.Size);
		Assert.Null(stable.GetExperimentalCultureBase(stableLayout));
	}

	[Fact]
	public void CultureVirtualSlotsAndInheritedProviderMapSelectMostDerivedOverrides()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var derivedMethod = module.ResolveEntryPoint("HandlerDerivedCulture::GetFormat");
		var derived = module.GetTypeLayout(derivedMethod);
		var leafMethod = module.ResolveEntryPoint("HandlerCultureLeaf::ToString");
		var leaf = module.GetTypeLayout(leafMethod);
		var getFormat = module.ResolveManagedMethod("System.Private.CoreLib", "System.Globalization.CultureInfo::GetFormat");
		var getNumber = module.ResolveManagedMethod("System.Private.CoreLib", "System.Globalization.CultureInfo::get_NumberFormat");
		var toString = module.ResolveManagedMethod("System.Private.CoreLib", "System.Object::ToString");
		Assert.Equal(derivedMethod.Identity, module.TryGetVirtualImplementation(derived, getFormat)!.Identity);
		Assert.Equal(derivedMethod.Identity, module.TryGetVirtualImplementation(leaf, getFormat)!.Identity);
		Assert.Equal("HandlerDerivedCulture::get_NumberFormat", module.TryGetVirtualImplementation(leaf, getNumber)!.DisplayName);
		Assert.Equal(leafMethod.Identity, module.TryGetVirtualImplementation(leaf, toString)!.Identity);
		var provider = module.GetInterfaceDefinition(module.ResolveManagedMethod("System.Private.CoreLib", "System.IFormatProvider::GetFormat"));
		var map = Assert.IsType<CilInterfaceImplementation>(module.TryGetInterfaceImplementation(leaf, provider));
		Assert.Equal(leaf.Identity, map.Type.Identity);
		Assert.Equal(derivedMethod.Identity, Assert.Single(map.Methods).Identity);
		var explicitMethod = module.ResolveEntryPoint("HandlerExplicitCulture::System.IFormatProvider.GetFormat");
		var explicitMap = Assert.IsType<CilInterfaceImplementation>(module.TryGetInterfaceImplementation(module.GetTypeLayout(explicitMethod), provider));
		Assert.Equal(explicitMethod.Identity, Assert.Single(explicitMap.Methods).Identity);
		var reimplemented = module.GetTypeLayout(module.ResolveEntryPoint("HandlerReimplementedCulture::.ctor"));
		Assert.Equal(getFormat.Identity, Assert.Single(module.TryGetInterfaceImplementation(reimplemented, provider)!.Methods).Identity);
		var custom = module.GetInterfaceDefinition(module.ResolveManagedMethod("System.Private.CoreLib", "System.ICustomFormatter::Format"));
		Assert.Null(module.TryGetInterfaceImplementation(leaf, custom));
	}

	[Fact]
	public void CultureRuntimeFallbackUsesOfficialSlotAndDiscoversApplicationOverrides()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var getter = module.ResolveManagedMethod("System.Private.CoreLib", "System.Globalization.CultureInfo::get_DateTimeFormat");
		var fallback = module.ApplyTargetRuntimeOverride(getter);
		Assert.Equal("CopperSharp.Runtime.ShadowCultureInfo::GetDateTimeFormat", fallback.DisplayName);
		Assert.Equal(module.GetVirtualSlot(getter), module.GetVirtualSlot(fallback));
		var leaf = module.GetTypeLayout(module.ResolveEntryPoint("HandlerCultureLeaf::ToString"));
		module.RegisterReachableDispatchLayout(leaf);
		var implementation = module.TryGetVirtualImplementation(leaf, fallback)!;
		Assert.Equal("HandlerCultureLeaf::get_DateTimeFormat", implementation.DisplayName);
		Assert.Contains(module.GetVirtualImplementations(fallback), method => method.Identity == implementation.Identity);
	}

	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled)
	{
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled });
		return new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowCultureInfo).Assembly.Location], frameworkImplementationPack: catalog);
	}
}
