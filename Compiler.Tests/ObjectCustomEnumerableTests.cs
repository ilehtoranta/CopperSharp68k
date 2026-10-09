/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ObjectCustomEnumerableTests
{
	[Fact]
	public void CustomObjectSourcesHaveCompleteOptInEnumerationMaps()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = true });
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowObjectJoinEnumeration).Assembly.Location], frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CreateOwnedCustomObjectAdapter");
		var constructor = entry.Instructions.Last(instruction => instruction.OpCode == OpCodes.Newobj);
		var source = module.GetTypeLayout(module.ResolveMethodToken((int)constructor.Operand!, entry, constructor.Offset).Definition!);
		var factory = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.Single(method => method?.DisplayName == "CopperSharp.Runtime.ShadowObjectJoinEnumeration::GetEnumerator")!;
		var definition = module.GetRuntimeInterfaceDefinition(module.ResolveRuntimeTypeIdentity(factory.Signature.ParameterTypes[0], factory.ModuleName));
		var implementation = module.TryGetInterfaceImplementation(source, definition);
		Assert.NotNull(implementation);
		Assert.Equal(2, implementation.Methods.Length);
		Assert.All(implementation.Methods, method => Assert.StartsWith("CustomObjectJoinSource::", method.DisplayName));
		using var ordinary = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		Assert.Null(ordinary.TryGetInterfaceImplementation(source, definition));
	}

	[Fact]
	public void CallbackContractsMatchCoreLib() => Assert.Equal(42, CompilerFixtures.CoreLibObjectCustomCallbackContractsEntry());
	[Fact]
	public void ManagedAdaptersPreserveCoreLibCallbackContracts() => Assert.Equal(42, CompilerFixtures.ManagedObjectCustomCallbackContractsEntry());
	[Fact]
	public void DistinctIteratorsRetainPrivateValuesThroughCollection() => Assert.Equal(42, CompilerFixtures.CoreLibObjectCustomIteratorOwnershipEntry());
}
