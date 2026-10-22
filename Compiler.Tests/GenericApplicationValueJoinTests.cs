/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class GenericApplicationValueJoinTests
{
	[Fact]
	public void NullableApplicationAdaptersRetainPayloadSizesAndReferenceMaps()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericApplicationNullableJoinsEntry");
		var helpers = entry.Instructions.Where(i => i.OpCode == OpCodes.Call)
			.Select(i => module.ResolveMethodToken((int)i.Operand!, entry, i.Offset).Definition)
			.Where(method => method?.Name == "CheckSmallNullableJoin").ToArray();
		Assert.Equal(6, helpers.Length);
		foreach (var helper in helpers)
		{
			var payloadType = Assert.Single(helper!.MethodTypeArguments);
			Assert.True(module.TryGetStructLayout(payloadType, helper.ModuleName, out var payload));
			var join = helper.Instructions.Where(i => i.OpCode == OpCodes.Call)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, helper, i.Offset).Definition)
				.First(method => method?.Name == "CheckGenericJoin")!;
			var nullable = Assert.Single(join.MethodTypeArguments);
			Assert.Equal(payload.Size + 4, nullable.Size);
			Assert.True(module.IsExperimentalNullableJoinValue(nullable));
			Assert.False(stable.IsExperimentalNullableJoinValue(nullable));
			Assert.False(module.IsExperimentalNullableJoinValue(nullable with { Size = nullable.Size + 4 }));
			Assert.False(module.IsCompactNullableType(nullable));
			Assert.True(module.TryGetStructLayout(nullable, helper.ModuleName, out var layout));
			Assert.Equal(payload.ReferenceBitmap, layout.ReferenceBitmap);
			Assert.Equal(new[] { 0, nullable.Size - 1 }, layout.FieldOffsets.Values.Order());
			var listEnumerator = new CilType(CilTypeKind.ValueType, 0,
				$"System.Collections.Generic.List`1/Enumerator<{nullable.DisplayName}>", GenericArguments: [nullable]);
			Assert.True(module.TryGetStructLayout(listEnumerator, helper.ModuleName, out var list));
			Assert.Equal(12 + nullable.Size, list.Size);
			Assert.Equal(1u | (payload.ReferenceBitmap << 3), list.ReferenceBitmap);
			var wrapper = join.Instructions.Where(i => i.OpCode == OpCodes.Callvirt)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, join, i.Offset).Definition)
				.First(method => method?.Name == "AppendJoin")!;
			var core = wrapper.Instructions.Where(i => i.OpCode == OpCodes.Call)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, wrapper, i.Offset).Definition)
				.First(method => method?.Name == "AppendJoinCore")!;
			var current = core.Instructions.Where(i => i.OpCode == OpCodes.Callvirt)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, core, i.Offset).Definition)
				.First(method => method?.Name == "get_Current")!;
			var adapter = module.GetTypeLayout(current);
			Assert.Equal(28 + nullable.Size, adapter.Size);
			Assert.Equal(3u | (payload.ReferenceBitmap << 4) | (1u << ((nullable.Size + 16) / 4)), adapter.ReferenceBitmap);
		}
	}

	[Fact]
	public void ApplicationAdaptersRetainExactAggregateWidthsRootsAndOptIn()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericApplicationValueJoinsEntry");
		var helpers = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.Where(method => method?.Name == "CheckGenericJoin").ToArray();
		Assert.Equal(7, helpers.Length);
		foreach (var helper in helpers)
		{
			var element = Assert.Single(helper!.MethodTypeArguments);
			Assert.Equal(CilTypeKind.ValueType, element.Kind);
			Assert.True(module.TryGetStructLayout(element, helper.ModuleName, out var value));
			var wrapper = helper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt)
				.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, helper, instruction.Offset).Definition)
				.First(method => method?.Name == "AppendJoin")!;
			var core = wrapper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
				.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, wrapper, instruction.Offset).Definition)
				.First(method => method?.Name == "AppendJoinCore")!;
			foreach (var operation in core.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt))
			{
				var member = module.DescribeFrameworkMethodToken((int)operation.Operand!, core, operation.Offset);
				if (member.Name is not ("GetEnumerator" or "get_Current" or "MoveNext" or "Dispose")) continue;
				Assert.True(module.TryCreateExperimentalJoinEnumerationBinding(member, core, out var binding));
				Assert.Equal("CopperSharp.Runtime.ShadowGenericJoin" + (member.Name == "GetEnumerator" ? "Enumeration" : "Enumerator`1"), binding.ShadowMethod!.TypeName);
				Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
				Assert.False(stable.TryCreateExperimentalJoinEnumerationBinding(member, core, out _));
				Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, helper, out _));
				Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { ModuleName = "Application" }, out _));
				if (member.Name != "get_Current") continue;
				var current = module.ResolveMethodToken((int)operation.Operand!, core, operation.Offset).Definition!;
				Assert.Equal(element, current.Signature.ReturnType);
				var layout = module.GetTypeLayout(current);
				Assert.Equal(28 + value.Size, layout.Size);
				Assert.Equal(3u | (value.ReferenceBitmap << 4) | (1u << ((value.Size + 16) / 4)), layout.ReferenceBitmap);
			}
		}
	}
	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
