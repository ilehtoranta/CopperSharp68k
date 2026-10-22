/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ConstructedTypeDefinitionLookupTests
{
	[Fact]
	public void DefinitionLookupPreservesConstructionsInterfaceFlagsAndMisses()
	{
		using var module = new CompilationModule(typeof(ConstructedTypeDefinitionLookupTests).Assembly.Location);
		CilType ReturnType(string entry) => module.ResolveEntryPoint(
			$"CopperSharp.Compiler.Tests.ConstructedTypeLookupFixtures::{entry}").Signature.ReturnType;
		var integer = ReturnType(nameof(ConstructedTypeLookupFixtures.IntegerBox));
		var text = ReturnType(nameof(ConstructedTypeLookupFixtures.StringBox));
		var integerTarget = module.ResolveRuntimeTypeIdentity(integer, module.AssemblyName);
		var textTarget = module.ResolveRuntimeTypeIdentity(text, module.AssemblyName);
		Assert.False(integerTarget.Handle.IsNil);
		Assert.Equal(integerTarget.Handle, textTarget.Handle);
		Assert.Equal(integer, integerTarget.Type);
		Assert.Equal(text, textTarget.Type);
		Assert.NotEqual(integerTarget.Type, textTarget.Type);
		Assert.True(integerTarget.IsConstructedGeneric);
		Assert.False(integerTarget.IsInterface);

		var interfaceType = ReturnType(nameof(ConstructedTypeLookupFixtures.InterfaceBox));
		var interfaceTarget = module.ResolveRuntimeTypeIdentity(interfaceType, module.AssemblyName);
		Assert.False(interfaceTarget.Handle.IsNil);
		Assert.True(interfaceTarget.IsInterface);
		Assert.True(interfaceTarget.IsConstructedGeneric);
		Assert.Equal(interfaceType, interfaceTarget.Type);

		// A populated index must neither resolve adjacent names nor cache a
		// constructed target (and its type arguments) as the open definition.
		foreach (var name in new[] { integer.DisplayName.ToUpperInvariant(), "Application.Missing`1<int>" })
		{
			var missing = integer with { DisplayName = name };
			Assert.True(module.ResolveRuntimeTypeIdentity(missing, module.AssemblyName).Handle.IsNil);
			Assert.True(module.ResolveRuntimeTypeIdentity(missing, module.AssemblyName).Handle.IsNil);
		}
		Assert.Equal(integerTarget, module.ResolveRuntimeTypeIdentity(integer, module.AssemblyName));
		Assert.Equal(textTarget, module.ResolveRuntimeTypeIdentity(text, module.AssemblyName));
	}
}

internal static class ConstructedTypeLookupFixtures
{
	internal sealed class Box<T>;
	internal interface IBox<T>;
	public static Box<int>? IntegerBox() => null;
	public static Box<string>? StringBox() => null;
	public static IBox<int>? InterfaceBox() => null;
}
