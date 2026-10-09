/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class DecimalCustomEnumerableTests
{
	[Fact]
	public void CustomDecimalSourcesHaveCompleteOptInEnumerationMaps()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowDecimalJoinEnumeration).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateOwnedCustomDecimalAdapter");
		var constructor = entry.Instructions.Last(instruction => instruction.OpCode == OpCodes.Newobj);
		var source = module.GetTypeLayout(module.ResolveMethodToken((int)constructor.Operand!, entry, constructor.Offset).Definition!);
		var factory = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.Single(method => method?.DisplayName == "CopperSharp.Runtime.ShadowDecimalJoinEnumeration::GetEnumerator")!;
		var definition = module.GetRuntimeInterfaceDefinition(module.ResolveRuntimeTypeIdentity(factory.Signature.ParameterTypes[0], factory.ModuleName));
		var implementation = module.TryGetInterfaceImplementation(source, definition);
		Assert.NotNull(implementation);
		Assert.Equal(2, implementation.Methods.Length);
		Assert.All(implementation.Methods, method => Assert.StartsWith("CustomDecimalJoinSource::", method.DisplayName));
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.Null(ordinary.TryGetInterfaceImplementation(source, definition));
	}

	[Fact]
	public void DecimalListEnumeratorTransportRetainsOnlyItsListReference()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true }));
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		foreach (var name in new[] { "System.Collections.Generic.List`1/Enumerator<System.Decimal>", "CopperSharp.Runtime.ShadowListEnumerator`1<System.Decimal>" })
		{
			var type = new CilType(CilTypeKind.ValueType, 0, name,
				GenericArguments: [new CilType(CilTypeKind.ValueType, 0, "System.Decimal")]);
			Assert.True(module.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out var layout));
			Assert.Equal(28, layout.Size);
			Assert.Equal(1u, layout.ReferenceBitmap);
			Assert.False(ordinary.TryGetReferenceFreeStructLayout(type, "System.Private.CoreLib", out _));
		}
	}
	[Fact]
	public void CallbackContractsMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibDecimalCustomCallbackContractsEntry());
	[Fact]
	public void ManagedAdaptersPreserveCoreLibCallbackContracts() => Assert.Equal(42, CompilerFixtures.ManagedDecimalCustomCallbackContractsEntry());
	[Fact]
	public void DistinctIteratorsRetainPrivateValuesThroughCollection() => Assert.Equal(42, CompilerFixtures.CoreLibDecimalCustomIteratorOwnershipEntry());
	[Fact]
	public void CoreLibListIteratorsRetainAggregateWordsAndOwners() => Assert.Equal(42, CompilerFixtures.CoreLibDecimalListIteratorOwnershipEntry());
}
