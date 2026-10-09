/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderApplicationJoinAdmissionTests
{
	[Fact]
	public void DirectValueFormattingRequiresTheActualApplicationMethodAndSlot()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericWideValueTransportEntry");
		var call = entry.Instructions.Single(instruction => instruction.ConstrainedTypeToken is not null &&
			module.DescribeFrameworkMethodToken((int)instruction.Operand!, entry, instruction.Offset).Name == "ToString");
		var member = module.DescribeFrameworkMethodToken((int)call.Operand!, entry, call.Offset);
		Assert.True(module.TryCreatePinnedIntegralToStringBinding(member, entry, call.Offset, out var binding, out var selected));
		Assert.Equal(entry.ModuleName, selected.ModuleName);
		Assert.Equal("ToString", selected.Name);
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, entry with { Handle = default }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, entry with { ModuleName = "Application" }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, entry with { Name = "Adjacent" }, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(new(member.DeclaringType, "Adjacent", member.Signature), entry, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, entry, call.Offset + 1, out _, out _));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.False(adjacent.TryCreatePinnedIntegralToStringBinding(member, entry, call.Offset, out _, out _));
	}

	internal static readonly string[] BehaviorEntries = [
		nameof(CompilerFixtures.CoreLibGenericApplicationReferenceJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationReferenceOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationReferenceCallbacksEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationReferenceCapacityEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationReferenceFormatterEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationValueSnapshotEntry),
		nameof(CompilerFixtures.CoreLibGenericWideValueTransportEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationValueJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationValueCallbackEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationValueOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationValueCapacityEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationValueFormatterContractsEntry),
		nameof(CompilerFixtures.CoreLibApplicationNullableTransportEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationNullableJoinsEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationNullableCallbacksEntry),
		nameof(CompilerFixtures.CoreLibApplicationNullablePublicOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationNullableOwnershipEntry),
		nameof(CompilerFixtures.CoreLibGenericApplicationNullableCapacityEntry)];
	public static IEnumerable<object[]> OriginalBehaviorCases => BehaviorEntries.Select(entry => new object[] { entry });

	[Theory]
	[MemberData(nameof(OriginalBehaviorCases))]
	public void OriginalBehaviorGraphsRequireDisabledUnlistedBodies(string entry) =>
		new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(entry);

	[Fact]
	public void ApplicationNullableStorageRequiresReleasedInputAndRetainsPayloadRoots()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibApplicationNullableTransportEntry");
			var array = entry.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newarr);
			var nullable = module.ResolveTypeToken((int)array.Operand!, entry, array.Offset);
			Assert.Equal(admitted, module.IsExperimentalNullableJoinValue(nullable));
			if (!admitted) return;
			Assert.True(module.TryGetStructLayout(nullable.NullableElementType!, entry.ModuleName, out var payload));
			Assert.NotEqual(0u, payload.ReferenceBitmap);
			Assert.True(module.TryGetStructLayout(nullable, entry.ModuleName, out var storage));
			Assert.Equal(payload.Size + 4, storage.Size);
			Assert.Equal(payload.ReferenceBitmap, storage.ReferenceBitmap);
			Assert.Equal(new[] { 0, storage.Size - 1 }, storage.FieldOffsets.Values.Order());
			Assert.False(module.IsExperimentalNullableJoinValue(nullable with { Size = nullable.Size + 4 }));
			Assert.False(module.IsExperimentalNullableJoinValue(nullable with { DisplayName = "Application.Adjacent" }));
		}
	}
}

public sealed partial class CompilerExecutionTests
{
	public static IEnumerable<object[]> StableApplicationBehaviorCpuCases()
	{
		foreach (var cpu in CpuTargets)
		foreach (var entry in StringBuilderApplicationJoinAdmissionTests.BehaviorEntries)
			yield return [cpu[0], cpu[1], entry];
	}
	[Theory]
	[MemberData(nameof(StableApplicationBehaviorCpuCases))]
	public void StringBuilderStableApplicationJoinsPreserveBehaviorAndOwners(M68kCpuTarget target, Copper68k.M68kCpuModel model, string entry) =>
		VerifyStableStringBuilderEntries(target, model, [entry], M68kFloatingPointMode.SoftFloat);
}
