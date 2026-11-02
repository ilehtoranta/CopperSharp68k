/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameworkTypeIdentityStorageTests
{
	[Fact]
	public void ConcurrentCanonicalizationPreservesClosedSignaturesAndAssemblyScopes()
	{
		FrameworkMemberId Create(string assembly, FrameworkTypeId argument) => new(
			FrameworkTypeId.Named(assembly, "Caller.Owner"), "Method",
			new FrameworkMethodSignatureId(0x10, 1, 1, FrameworkTypeId.ByReference(argument),
				[FrameworkTypeId.SzArray(argument)]), [argument]);
		var implementation = Create("System.Private.CoreLib", FrameworkTypeId.Named("System.Private.CoreLib", "Caller.Argument"));
		var expected = Create("System.Runtime", FrameworkTypeId.Named("System.Runtime", "Caller.Argument"));
		Parallel.For(0, 64, _ =>
		{
			var canonical = FrameworkImplementationProfile.Canonicalize(implementation);
			Assert.Equal(expected, canonical);
			Assert.Equal(canonical.GetHashCode(), expected.GetHashCode());
			Assert.NotEqual(Create("System.Runtime", FrameworkTypeId.Named("Application", "Caller.Argument")), canonical);
			Assert.Same(canonical, FrameworkImplementationProfile.Canonicalize(implementation));
			Assert.Same(expected, FrameworkImplementationProfile.Canonicalize(expected));
		});
	}

	[Fact]
	public void PrimitiveReuseRetainsStructuralAndCaseDistinctions()
	{
		foreach (var name in new[] { "System.Void", "System.Boolean", "System.Char", "System.SByte", "System.Byte",
			"System.Int16", "System.UInt16", "System.Int32", "System.UInt32", "System.Int64", "System.UInt64",
			"System.IntPtr", "System.UIntPtr", "System.Single", "System.Double", "System.String", "System.Object", "System.TypedReference" })
		{
			var first = FrameworkTypeId.Primitive(name);
			var second = FrameworkTypeId.Primitive(new string(name.ToCharArray()));
			Assert.Same(first, second);
			Assert.Equal(name, first.DisplayName);
			Assert.Equal(FrameworkTypeKind.Primitive, first.Kind);
			Assert.Equal(first.GetHashCode(), second.GetHashCode());
			Assert.NotEqual(first, FrameworkTypeId.Named("System.Runtime", name));
			Assert.NotEqual(first, FrameworkTypeId.Primitive(name.ToLowerInvariant()));
		}
		var unknown = FrameworkTypeId.Primitive("Caller.ArbitraryType");
		var equalUnknown = FrameworkTypeId.Primitive("Caller.ArbitraryType");
		Assert.Equal(unknown, equalUnknown);
		Assert.NotSame(unknown, equalUnknown);
	}

	[Fact]
	public void ParameterReuseRetainsKindsAndBoundaryIndices()
	{
		foreach (var index in new[] { -1, 0, 1, 15, 16, 4096, int.MaxValue })
		{
			var type = FrameworkTypeId.GenericTypeParameter(index);
			var method = FrameworkTypeId.GenericMethodParameter(index);
			Assert.Equal(index, type.GenericParameterIndex);
			Assert.Equal(index, method.GenericParameterIndex);
			Assert.Equal("!" + index, type.DisplayName);
			Assert.Equal("!!" + index, method.DisplayName);
			Assert.NotEqual(type, method);
			Assert.Equal(type, FrameworkTypeId.GenericTypeParameter(index));
			Assert.Equal(method, FrameworkTypeId.GenericMethodParameter(index));
			if (index is >= 0 and < 16)
			{
				Assert.Same(type, FrameworkTypeId.GenericTypeParameter(index));
				Assert.Same(method, FrameworkTypeId.GenericMethodParameter(index));
			}
			else
			{
				Assert.NotSame(type, FrameworkTypeId.GenericTypeParameter(index));
				Assert.NotSame(method, FrameworkTypeId.GenericMethodParameter(index));
			}
		}
	}

	[Fact]
	public void ConcurrentReuseKeepsConstructedArgumentsIndependentOfCallerMutation()
	{
		var primitive = FrameworkTypeId.Primitive("System.Int32");
		var generic = FrameworkTypeId.Named("Caller.Assembly", "Caller.Generic`1");
		Parallel.For(0, 64, _ =>
		{
			var arguments = new[] { FrameworkTypeId.Primitive("System.Int32") };
			var constructed = FrameworkTypeId.GenericInstantiation(generic, arguments);
			arguments[0] = FrameworkTypeId.Primitive("System.Char");
			Assert.Same(primitive, Assert.Single(constructed.GenericArguments));
			Assert.Equal(FrameworkTypeId.GenericInstantiation(generic, [primitive]), constructed);
			Assert.NotEqual(FrameworkTypeId.GenericInstantiation(generic, arguments), constructed);
		});
	}
}
