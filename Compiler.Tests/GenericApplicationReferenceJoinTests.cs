/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class GenericApplicationReferenceJoinTests
{
	[Fact]
	public void ReferenceAdaptersRetainConcreteTypesRootsAndExactAdmission()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibGenericApplicationReferenceJoinsEntry");
		var helpers = entry.Instructions.Where(i => i.OpCode == OpCodes.Call)
			.Select(i => module.ResolveMethodToken((int)i.Operand!, entry, i.Offset).Definition)
			.Where(method => method?.Name == "CheckApplicationReferenceJoin").ToArray();
		Assert.Equal(7, helpers.Length);
		foreach (var helper in helpers)
		{
			var element = Assert.Single(helper!.MethodTypeArguments);
			Assert.Equal(CilTypeKind.ManagedReference, element.Kind);
			var join = helper.Instructions.Where(i => i.OpCode == OpCodes.Call)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, helper, i.Offset).Definition)
				.First(method => method?.Name == "CheckGenericJoin")!;
			var wrapper = join.Instructions.Where(i => i.OpCode == OpCodes.Callvirt)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, join, i.Offset).Definition)
				.First(method => method?.Name == "AppendJoin")!;
			var core = wrapper.Instructions.Where(i => i.OpCode == OpCodes.Call)
				.Select(i => module.ResolveMethodToken((int)i.Operand!, wrapper, i.Offset).Definition)
				.First(method => method?.Name == "AppendJoinCore")!;
			foreach (var call in core.Instructions.Where(i => i.OpCode == OpCodes.Callvirt))
			{
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, core, call.Offset);
				if (member.Name is not ("GetEnumerator" or "get_Current" or "MoveNext" or "Dispose")) continue;
				Assert.True(module.TryCreateExperimentalJoinEnumerationBinding(member, core, out _));
				Assert.False(stable.TryCreateExperimentalJoinEnumerationBinding(member, core, out _));
				Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, helper, out _));
				Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { ModuleName = "Application" }, out _));
				Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { MethodTypeArguments = [element with { Size = 8 }] }, out _));
				if (member.Name != "get_Current") continue;
				var current = module.ResolveMethodToken((int)call.Operand!, core, call.Offset).Definition!;
				Assert.Equal(element, current.Signature.ReturnType);
				var layout = module.GetTypeLayout(current);
				Assert.Equal(32, layout.Size);
				Assert.Equal(0x33u, layout.ReferenceBitmap);
			}
			var listEnumerator = new CilType(CilTypeKind.ValueType, 0,
				$"System.Collections.Generic.List`1/Enumerator<{element.DisplayName}>", GenericArguments: [element]);
			Assert.True(module.TryGetStructLayout(listEnumerator, helper.ModuleName, out var list));
			Assert.Equal(16, list.Size);
			Assert.Equal(9u, list.ReferenceBitmap);
		}
	}
	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
