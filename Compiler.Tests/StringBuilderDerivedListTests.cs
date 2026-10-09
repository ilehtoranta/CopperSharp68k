/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderDerivedListTests
{
	[Fact]
	public void BoxedShadowListEnumeratorRetainsItsVerifiedDispatchLayout()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var element = new CilType(CilTypeKind.ManagedReference, 4, "string");
		var type = new CilType(CilTypeKind.ValueType, 16, "CopperSharp.Runtime.ShadowListEnumerator`1<string>", GenericArguments: [element]);
		var layout = Assert.IsType<CilTypeLayout>(module.RegisterBoxedDispatchLayout(type, "CopperSharp.Runtime.Managed"));
		Assert.Equal("CopperSharp.Runtime.Managed", layout.ModuleName);
		Assert.False(layout.Handle.IsNil);
		Assert.Equal(16, layout.Size);
		Assert.Equal(9u, layout.ReferenceBitmap);
		Assert.Equal(new[] { 0, 4, 8, 12 }, layout.FieldOffsets.Values.Order());
		var decimalElement = new CilType(CilTypeKind.ValueType, 16, "System.Decimal");
		var decimalType = new CilType(CilTypeKind.ValueType, 28, "System.Collections.Generic.List`1/Enumerator<System.Decimal>", GenericArguments: [decimalElement]);
		var decimalLayout = Assert.IsType<CilTypeLayout>(module.RegisterBoxedDispatchLayout(decimalType, "System.Private.CoreLib"));
		Assert.Equal("System.Private.CoreLib", decimalLayout.ModuleName);
		Assert.False(decimalLayout.Handle.IsNil);
		Assert.Equal(28, decimalLayout.Size);
		Assert.Equal(1u, decimalLayout.ReferenceBitmap);
		var referenceValue = new CilType(CilTypeKind.ValueType, 12, "CopperSharp.Compiler.Tests.CompilerFixtures/JoinNestedReferenceValue");
		var nullable = new CilType(CilTypeKind.ValueType, 16, $"System.Nullable<{referenceValue.DisplayName}>", GenericArguments: [referenceValue]);
		var nullableEnumerator = new CilType(CilTypeKind.ValueType, 28, $"CopperSharp.Runtime.ShadowListEnumerator`1<{nullable.DisplayName}>", GenericArguments: [nullable]);
		var nullableLayout = Assert.IsType<CilTypeLayout>(module.RegisterBoxedDispatchLayout(nullableEnumerator, "CopperSharp.Runtime.Managed"));
		Assert.Equal(28, nullableLayout.Size);
		Assert.Equal(0x31u, nullableLayout.ReferenceBitmap);
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.Null(adjacent.RegisterBoxedDispatchLayout(type, "CopperSharp.Runtime.Managed"));
		Assert.Null(adjacent.RegisterBoxedDispatchLayout(decimalType, "System.Private.CoreLib"));
		Assert.Null(adjacent.RegisterBoxedDispatchLayout(nullableEnumerator, "CopperSharp.Runtime.Managed"));
	}

	[Fact]
	public void OpenDecimalListMembersRequireAnActualMatchingSubclass()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderDerivedGenericListDispatchEntry");
		var helper = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.Single(method => method?.Name == "CheckDerivedGenericJoin" && method.MethodTypeArguments[0].DisplayName == "System.Decimal")!;
		var constructor = helper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Newobj)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, helper, instruction.Offset).Definition)
			.Single(method => method?.DisplayName.Contains("ReimplementedGenericJoinList", StringComparison.Ordinal) == true)!;
		foreach (var call in constructor.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call))
		{
			var member = module.DescribeFrameworkMethodToken((int)call.Operand!, constructor, call.Offset);
			if (member.Name is not (".ctor" or "Add")) continue;
			Assert.Equal("System.Private.CoreLib", module.ResolveMethodToken((int)call.Operand!, constructor, call.Offset).Definition!.ModuleName);
		}
		var element = constructor.ConstructedDeclaringType!.GenericArguments[0];
		var construction = new CilType(CilTypeKind.ManagedReference, 4,
			$"System.Collections.Generic.IEnumerable`1<{element.DisplayName}>", GenericArguments: [element]);
		var parameter = FrameworkTypeId.GenericTypeParameter(0);
		var memberId = new FrameworkMemberId(
			FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1"), [parameter]),
			"GetEnumerator", new FrameworkMethodSignatureId(0x20, 0, 0,
				FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1"), [parameter]), []));
		Assert.True(module.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction, constructor, null, out _));
		Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction, constructor with { Handle = default }, null, out _));
		Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction, constructor with { Name = "Adjacent" }, null, out _));
		Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction, constructor with { ModuleName = "Application" }, null, out _));
		Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction, helper, null, out _));
		Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction with { DisplayName = "Application.Iterator" }, constructor, null, out _));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.False(adjacent.TryCreatePinnedGenericJoinInterfaceBinding(memberId, construction, constructor, null, out _));
	}

	[Fact]
	public void DerivedListStorageAndTypeTokensRequireVerifiedMetadata()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			var definition = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "ReimplementedStringJoinList");
			var constructor = module.GetMethod(module.Reader.GetTypeDefinition(definition).GetMethods().Single(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == ".ctor"));
			var layout = module.GetTypeLayout(constructor);
			Assert.Equal(admitted, module.GetExperimentalListBase(layout) is not null);
			if (admitted) {
				Assert.Equal(28, layout.Size);
				Assert.Equal(25u, layout.ReferenceBitmap);
				Assert.Equal(new[] { 20, 24 }, layout.FieldOffsets.Values.Order());
			}
			var factory = module.ResolveManagedMethod("CopperSharp.Runtime.Managed", "CopperSharp.Runtime.ShadowStringJoinEnumeration::GetEnumerator");
			var token = factory.Instructions.Single(instruction => instruction.OpCode == OpCodes.Ldtoken);
			var type = module.ResolveTypeToken((int)token.Operand!, factory, token.Offset);
			Assert.Equal(admitted, module.IsPinnedJoinListTypeToken(factory, type));
			Assert.False(module.IsPinnedJoinListTypeToken(factory with { Handle = default }, type));
			Assert.False(module.IsPinnedJoinListTypeToken(factory with { ModuleName = "Application" }, type));
			Assert.False(module.IsPinnedJoinListTypeToken(factory with { Name = "Adjacent" }, type));
			Assert.False(module.IsPinnedJoinListTypeToken(factory, type with { Size = 8 }));
			Assert.False(module.IsPinnedJoinListTypeToken(factory, type with { DisplayName = "Application.Adjacent" }));
			foreach (var call in factory.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Callvirt)) {
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, factory, call.Offset);
				if (member.Name is not ("GetType" or "GetTypeFromHandle" or "op_Equality")) continue;
				Assert.Equal(admitted, module.TryCreatePinnedApplicationFormattingLeafBinding(member, factory, call.Offset, out _));
				Assert.False(module.TryCreatePinnedApplicationFormattingLeafBinding(member, factory with { Handle = default }, call.Offset, out _));
				Assert.False(module.TryCreatePinnedApplicationFormattingLeafBinding(member, factory, call.Offset + 1, out _));
			}
		}
	}

	[Fact]
	public void InheritedListEnumerationMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderInheritedListDispatchEntry());
	[Fact]
	public void InheritedListEnumerationHasAStableGraph() =>
		new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(nameof(CompilerFixtures.StringBuilderInheritedListDispatchEntry));
	[Fact]
	public void InheritedGenericListEnumerationMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderInheritedGenericListDispatchEntry());
	[Fact]
	public void InheritedGenericListEnumerationHasAStableGraph() =>
		new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(nameof(CompilerFixtures.StringBuilderInheritedGenericListDispatchEntry));
	[Fact]
	public void InheritedNullableListOwnershipMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderInheritedNullableListOwnershipEntry());
	[Fact]
	public void InheritedNullableListOwnershipHasAStableGraph() =>
		new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(nameof(CompilerFixtures.StringBuilderInheritedNullableListOwnershipEntry));

	[Fact]
	public void ReimplementedListEnumerationMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderDerivedListDispatchEntry());
	[Fact]
	public void ReimplementedGenericListEnumerationMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderDerivedGenericListDispatchEntry());

	[Fact]
	public void ReimplementedListEnumerationHasAStableGraph() =>
		new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(nameof(CompilerFixtures.StringBuilderDerivedListDispatchEntry));
	[Fact]
	public void ReimplementedGenericListEnumerationHasAStableGraph() =>
		new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(nameof(CompilerFixtures.StringBuilderDerivedGenericListDispatchEntry));
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderInheritedListEnumerationPreservesInterfaceDispatch(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderInheritedListDispatchEntry)]);
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderInheritedGenericListEnumerationPreservesInterfaceDispatch(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderInheritedGenericListDispatchEntry)], M68kFloatingPointMode.SoftFloat);
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderInheritedIntListEnumerationPreservesInterfaceDispatch(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderInheritedIntListDispatchEntry)]);
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderInheritedDecimalListEnumerationPreservesInterfaceDispatch(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderInheritedDecimalListDispatchEntry)]);
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderInheritedNullableListRetainsReferences(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderInheritedNullableListOwnershipEntry)]);

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderDerivedListEnumerationPreservesInterfaceDispatch(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderDerivedListDispatchEntry)]);
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderDerivedGenericListEnumerationPreservesInterfaceDispatch(M68kCpuTarget target, Copper68k.M68kCpuModel model) =>
		VerifyStableStringBuilderEntries(target, model, [nameof(CompilerFixtures.StringBuilderDerivedGenericListDispatchEntry)], M68kFloatingPointMode.SoftFloat);
}
