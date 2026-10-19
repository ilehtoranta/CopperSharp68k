/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderIntegralEnumJoinAdmissionTests
{
	[Theory]
	[InlineData(nameof(CompilerFixtures.CoreLibStringBuilderGenericIntegralJoinsEntry))]
	[InlineData(nameof(CompilerFixtures.CoreLibStringBuilderGenericEnumJoinsEntry))]
	[InlineData(nameof(CompilerFixtures.CoreLibGenericJoinCallbackContractsEntry))]
	[InlineData(nameof(CompilerFixtures.CoreLibGenericJoinIteratorOwnershipEntry))]
	[InlineData(nameof(CompilerFixtures.CoreLibGenericJoinBoxedIteratorContractsEntry))]
	[InlineData(nameof(CompilerFixtures.CoreLibGenericEnumJoinCapacityContractsEntry))]
	public void OriginalBehaviorGraphsRequireDisabledUnlistedBodies(string entry)
		=> new StringBuilderStableAllocationGraphTests().ReleasedAllocationGraphsRequireDisabledUnlistedBodies(entry);

	[Fact]
	public void NestedEnumInterfacesRetainMetadataIdentityAndRejectAdjacentRepresentations()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericJoinAdapterAllocationCleanupEntry");
			var array = entry.Instructions.Single(instruction => instruction.OpCode == OpCodes.Newarr);
			var element = module.ResolveTypeToken((int)array.Operand!, entry, array.Offset);
			Assert.True(element.IsEnum); Assert.Equal(8, element.Size);
			var construction = new CilType(CilTypeKind.ManagedReference, 4,
				$"System.Collections.Generic.IEnumerator`1<{element.DisplayName}>", GenericArguments: [element]);
			Assert.Equal(admitted, module.IsPinnedGenericJoinInterface(construction));
			Assert.False(module.IsPinnedGenericJoinInterface(new CilType(CilTypeKind.ManagedReference, 4, "object")));
			Assert.False(module.IsPinnedGenericJoinInterface(construction with { DisplayName = "Application.Iterator" }));
			Assert.False(module.IsPinnedGenericJoinInterface(construction with { GenericArguments = [element with { Size = 3 }] }));
			var call = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Callvirt &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, entry, instruction.Offset).Name == "get_Current");
			var member = module.DescribeFrameworkMethodToken((int)call.Operand!, entry, call.Offset);
			Assert.NotNull(member.DeclaringType.GenericArguments[0].DeclaringType);
			Assert.Equal(admitted, module.TryCreatePinnedGenericJoinInterfaceBinding(member, construction, entry, null, out _));
			Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(new(member.DeclaringType, "Adjacent", member.Signature), construction, entry, null, out _));
			Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(new(member.DeclaringType, member.Name,
				new FrameworkMethodSignatureId(0, 0, 0, member.Signature.ReturnType, [])), construction, entry, null, out _));
			if (admitted)
			{
				Assert.Throws<M68kCompilationException>(() => module.IsPinnedGenericJoinInterface(construction with { GenericArguments = [element with { Size = 4 }] }));
				var resolved = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
				Assert.Equal(element, resolved.Signature.ReturnType);
				Assert.True(resolved.DeclaringTypeIsInterface);
				Assert.Empty(module.GetInterfaceImplementations(resolved));
			}
			// Adjacent packs reject the wide generic helper before its body can
			// be constructed; their closed-interface rejection is checked above.
			if (!admitted) return;
			var ownership = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericJoinIteratorOwnershipEntry");
			var helper = ownership.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
				.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, ownership, instruction.Offset).Definition)
				.Single(method => method?.Name == "GenericJoinOwnership" && method.MethodTypeArguments[0].IsEnum)!;
			var openCall = helper.Instructions.First(instruction => instruction.OpCode == OpCodes.Callvirt &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, helper, instruction.Offset).Name == "get_Current");
			var openMember = module.DescribeFrameworkMethodToken((int)openCall.Operand!, helper, openCall.Offset);
			Assert.Equal(admitted, module.TryCreatePinnedGenericJoinInterfaceBinding(openMember, construction, helper, null, out _));
			// Decoded metadata retains the closed caller argument. Check the
			// genuinely open slot separately: it still requires a matching caller.
			openMember = new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(openMember.DeclaringType.ElementType!,
				[FrameworkTypeId.GenericMethodParameter(0)]), openMember.Name, openMember.Signature);
			Assert.True(module.TryCreatePinnedGenericJoinInterfaceBinding(openMember, construction, helper, null, out _));
			Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(openMember, construction, null, null, out _));
			Assert.False(module.TryCreatePinnedGenericJoinInterfaceBinding(openMember, construction,
				helper with { MethodTypeArguments = [new CilType(CilTypeKind.SignedInteger, 8, "long")] }, null, out _));
		}
	}
}

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderStableIntegralEnumJoinsPreserveBehaviorAndOwners(M68kCpuTarget target, Copper68k.M68kCpuModel model)
		=> VerifyStableStringBuilderEntries(target, model, [
			nameof(CompilerFixtures.CoreLibStringBuilderGenericIntegralJoinsEntry),
			nameof(CompilerFixtures.CoreLibStringBuilderGenericEnumJoinsEntry),
			nameof(CompilerFixtures.CoreLibGenericJoinCallbackContractsEntry),
			nameof(CompilerFixtures.CoreLibGenericJoinIteratorOwnershipEntry),
			nameof(CompilerFixtures.CoreLibGenericJoinBoxedIteratorContractsEntry),
			nameof(CompilerFixtures.CoreLibGenericEnumJoinCapacityContractsEntry)]);
}
