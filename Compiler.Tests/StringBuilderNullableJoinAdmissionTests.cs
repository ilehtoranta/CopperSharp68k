/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderNullableJoinAdmissionTests
{
	[Fact]
	public void DecimalOwnershipCallsResolveToReleasedManagedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericDecimalNullableOwnershipEntry");
		var call = entry.Instructions.Single(instruction => instruction.Offset == 0x53);
		var target = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset);
		Assert.True(target.Definition is not null, $"{module.DescribeFrameworkMethodToken((int)call.Operand!, entry, call.Offset)} => {target.ImportName}");
	}

	[Fact]
	public void ApplicationScalarFormattingRequiresTheActualClosedMethodDefinition()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericSmallNullableOwnershipEntry");
		var helper = entry.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(method => method?.Name == "CheckSmallNullableOwnership" && method.MethodTypeArguments[0].DisplayName == "bool")!;
		var call = helper.Instructions.First(instruction => instruction.ConstrainedTypeToken is not null);
		var member = module.DescribeFrameworkMethodToken((int)call.Operand!, helper, call.Offset);
		Assert.True(module.TryCreatePinnedIntegralToStringBinding(member, helper, call.Offset, out var binding, out var selected));
		Assert.Equal("System.Boolean::ToString", selected.DisplayName);
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, helper with { ModuleName = "Application" }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, helper with { Name = "Adjacent" }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, helper with { Handle = default }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, helper with { DisplayName = "Adjacent" }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, helper with { MethodTypeArguments = [new CilType(CilTypeKind.Character, 2, "char")] }, call.Offset, out _, out _));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.False(adjacent.TryCreatePinnedIntegralToStringBinding(member, helper, call.Offset, out _, out _));
	}

	[Fact]
	public void ReleasedNullableEnumFormattingRetainsItsConstrainedReceiver()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibSmallNullableEnumTransportEntry");
		var call = entry.Instructions.Single(instruction => instruction.Offset == 0x21);
		var type = module.ResolveTypeToken(call.ConstrainedTypeToken!.Value, entry, call.Offset);
		var declaration = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
		Assert.True(CompilationModule.AreSameClosedJoinType(type, declaration.ConstructedDeclaringType));
		Assert.False(CompilationModule.AreSameClosedJoinType(type with { Size = type.Size + 4 }, declaration.ConstructedDeclaringType));
		Assert.False(CompilationModule.AreSameClosedJoinType(type with { GenericArguments = [type.NullableElementType! with { Size = 4 }] }, declaration.ConstructedDeclaringType));
		Assert.Equal("System.Private.CoreLib", declaration.ModuleName);
		Assert.Equal("ToString", declaration.Name);
		Assert.Equal(declaration.Identity, module.ResolveConstrainedInterfaceImplementation(entry, call.ConstrainedTypeToken.Value, call.Offset, declaration).Identity);
	}

	internal static readonly string[] BehaviorEntries = [
		nameof(CompilerFixtures.CoreLibGenericNullableIntJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericNullableCallbackEntry),
		nameof(CompilerFixtures.CoreLibGenericNullableOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericNullableCapacityEntry),
		nameof(CompilerFixtures.CoreLibSmallNullableEnumTransportEntry),
		nameof(CompilerFixtures.CoreLibGenericSmallNullablePrimitiveJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericSmallNullableEnumJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericSmallNullableCallbacksEntry),
		nameof(CompilerFixtures.CoreLibGenericSmallNullableOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericSmallNullableCapacityEntry),
		nameof(CompilerFixtures.CoreLibWideNullableTransportEntry),
		nameof(CompilerFixtures.CoreLibGenericWideNullableJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericWideNullableCallbacksEntry),
		nameof(CompilerFixtures.CoreLibGenericWideNullableOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericWideNullableCapacityEntry),
		nameof(CompilerFixtures.CoreLibFloatingNullableTransportEntry),
		nameof(CompilerFixtures.CoreLibGenericSingleNullableJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericDoubleNullableJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericFloatingNullableCallbacksEntry),
		nameof(CompilerFixtures.CoreLibGenericFloatingNullableOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericFloatingNullableCapacityEntry),
		nameof(CompilerFixtures.CoreLibGenericFloatingNullableCultureEntry),
		nameof(CompilerFixtures.CoreLibDecimalNullableTransportEntry),
		nameof(CompilerFixtures.CoreLibDecimalNullableReuseEntry),
		nameof(CompilerFixtures.CoreLibGenericDecimalNullableJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericDecimalNullableCallbacksEntry),
		nameof(CompilerFixtures.CoreLibGenericDecimalNullableOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericDecimalNullableCapacityEntry),
		nameof(CompilerFixtures.CoreLibGenericDecimalNullableCultureEntry)];

	public static IEnumerable<object[]> OriginalBehaviorCases => BehaviorEntries.Select(entry => new object[] { entry });

	[Theory]
	[MemberData(nameof(OriginalBehaviorCases))]
	public void OriginalBehaviorGraphsRequireDisabledUnlistedBodies(string entry)
		=> new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(entry);

	[Fact]
	public void NullableStorageAndMembersRequireExactRepresentationsAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			foreach (var element in new[] {
				new CilType(CilTypeKind.Boolean, 1, "bool"), new CilType(CilTypeKind.Character, 2, "char"),
				new CilType(CilTypeKind.SignedInteger, 1, "sbyte"), new CilType(CilTypeKind.UnsignedInteger, 1, "byte"),
				new CilType(CilTypeKind.SignedInteger, 2, "short"), new CilType(CilTypeKind.UnsignedInteger, 2, "ushort"),
				new CilType(CilTypeKind.SignedInteger, 4, "int"), new CilType(CilTypeKind.UnsignedInteger, 4, "uint"),
				new CilType(CilTypeKind.SignedInteger, 8, "long"), new CilType(CilTypeKind.UnsignedInteger, 8, "ulong"),
				new CilType(CilTypeKind.FloatingPoint, 4, "float"), new CilType(CilTypeKind.FloatingPoint, 8, "double"),
				new CilType(CilTypeKind.ValueType, 0, "System.Decimal") })
			{
				var nullable = Wrap(element);
				Assert.Equal(admitted, module.IsExperimentalNullableJoinValue(nullable));
				Assert.False(module.IsExperimentalNullableJoinValue(nullable with { Size = nullable.Size + 4 }));
				Assert.False(module.IsExperimentalNullableJoinValue(nullable with { DisplayName = nullable.DisplayName + "Adjacent" }));
				Assert.False(module.IsExperimentalNullableJoinValue(nullable with { IsEnum = true }));
				if (!admitted) continue;
				Assert.True(module.TryGetReferenceFreeStructLayout(nullable, "System.Private.CoreLib", out var layout));
				Assert.Equal(nullable.Size, layout.Size); Assert.Equal(0u, layout.ReferenceBitmap);
				Assert.Equal(new[] { element.DisplayName == "System.Decimal" ? 0 : Math.Max(4, element.Size) - element.Size,
					nullable.Size - 1 }, layout.FieldOffsets.Values.Order().ToArray());
			}
			foreach (var rejected in new[] { new CilType(CilTypeKind.ManagedReference, 4, "object"),
				new CilType(CilTypeKind.NativeInteger, 4, "nint"), new CilType(CilTypeKind.FloatingPoint, 8, "float"),
				new CilType(CilTypeKind.ValueType, 16, "System.Decimal"), Wrap(new CilType(CilTypeKind.SignedInteger, 4, "int")) })
				Assert.False(module.IsExperimentalNullableJoinValue(Wrap(rejected)));
			var wide = Wrap(new CilType(CilTypeKind.SignedInteger, 8, "long"));
			var definition = FrameworkTypeId.Named("System.Runtime", "System.Nullable`1");
			var owner = FrameworkTypeId.GenericInstantiation(definition, [FrameworkTypeId.Primitive("System.Int64")]);
			var signature = new FrameworkMethodSignatureId(0x20, 0, 1, FrameworkTypeId.Primitive("System.Void"), [FrameworkTypeId.GenericTypeParameter(0)]);
			var member = new FrameworkMemberId(owner, ".ctor", signature);
			Assert.Equal(admitted, module.TryCreatePinnedNullableMemberBinding(member, wide, null, null, out var binding));
			if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect | FrameworkEffects.MayThrow));
			Assert.False(module.TryCreatePinnedNullableMemberBinding(new(owner, "Adjacent", signature), wide, null, null, out _));
			Assert.False(module.TryCreatePinnedNullableMemberBinding(new(owner, ".ctor", new FrameworkMethodSignatureId(0, 0, 1, signature.ReturnType, signature.ParameterTypes)), wide, null, null, out _));
			Assert.False(module.TryCreatePinnedNullableMemberBinding(new(FrameworkTypeId.GenericInstantiation(definition, [FrameworkTypeId.Primitive("System.Int32")]), ".ctor", signature), wide, null, null, out _));
		}
		static CilType Wrap(CilType element) => new(CilTypeKind.ValueType, CilType.NullableStorageSize(element), $"System.Nullable<{element.DisplayName}>", GenericArguments: [element]);
	}
}

public sealed partial class CompilerExecutionTests
{
	public static IEnumerable<object[]> StableNullableBehaviorCpuCases()
	{
		foreach (var cpu in CpuTargets)
		foreach (var entry in StringBuilderNullableJoinAdmissionTests.BehaviorEntries)
			yield return [cpu[0], cpu[1], entry];
	}

	[Theory]
	[MemberData(nameof(StableNullableBehaviorCpuCases))]
	public void StringBuilderStableNullableJoinsPreserveBehaviorAndOwners(M68kCpuTarget target, Copper68k.M68kCpuModel model, string entry)
		=> VerifyStableStringBuilderEntries(target, model, [entry], M68kFloatingPointMode.SoftFloat);
}
