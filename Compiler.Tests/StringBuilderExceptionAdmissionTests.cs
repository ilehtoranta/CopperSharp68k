/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderExceptionAdmissionTests
{
	[Fact]
	public void ExceptionBodiesRequireTheBoundedCallerAndRejectOtherGenericArguments()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
		foreach (var member in StringBuilderExceptionSurface.Members)
		{
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding), member.DisplayName);
			Assert.Contains(FrameworkFeature.ManagedExceptions, binding.EffectSummary.RequiredFeatures);
			var publicConstructor = StringBuilderExceptionSurface.IsPublicCallbackConstructor(member);
			Assert.Equal(publicConstructor, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
			Assert.Equal(publicConstructor, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, constructor, out _));
			Assert.False(StringBuilderExceptionSurface.Contains(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature)));
			if (member.Signature.GenericParameterCount == 1)
			{
				Assert.True(StringBuilderExceptionSurface.Contains(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int32")])));
				Assert.False(StringBuilderExceptionSurface.Contains(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Int64")])));
			}
		}
		pack.Replace("packVersion", "10.0.10");
		var adjacent = Load(pack);
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(StringBuilderExceptionSurface.Members[0], null, adjacent, caller, out _));
	}

	[Fact]
	public void ReleasedInt32ValidationUsesPrimitiveAbiWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: Load(pack));
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderBenchmarkPresizedTextEntry");
		var create = entry.Instructions.First(instruction => instruction.OpCode == OpCodes.Newobj);
		var constructor = module.ResolveMethodToken((int)create.Operand!, entry, create.Offset).Definition!;
		var delegateCall = constructor.Instructions.First(instruction => instruction.OpCode == OpCodes.Call &&
			module.DescribeMethodToken((int)instruction.Operand!, constructor, instruction.Offset) is
				{ Name: ".ctor", TypeName: "System.Text.StringBuilder" });
		constructor = module.ResolveMethodToken((int)delegateCall.Operand!, constructor, delegateCall.Offset).Definition!;
		var validate = constructor.Instructions.First(instruction => instruction.OpCode == OpCodes.Call &&
			module.DescribeMethodToken((int)instruction.Operand!, constructor, instruction.Offset)?.Name == "ThrowIfNegative");
		var helper = module.ResolveMethodToken((int)validate.Operand!, constructor, validate.Offset).Definition!;
		var argument = Assert.Single(helper.MethodTypeArguments);
		Assert.Equal(CilTypeKind.SignedInteger, argument.Kind);
		Assert.Equal(4, argument.Size);
		Assert.Equal("int", argument.DisplayName);
	}

	[Fact]
	public void ExceptionResourceLeavesRequireAnOwnedExceptionCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = Load(pack);
		var definition = StringBuilderExceptionSurface.Members[0];
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.ArgumentOutOfRangeException"), definition.Name, definition.Signature);
		var text = FrameworkTypeId.Primitive("System.String");
		var value = FrameworkTypeId.Primitive("System.Object");
		var resource = FrameworkTypeId.Named("System.Private.CoreLib", "System.SR");
		var leaves = new[] {
			new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.OutOfMemoryException"), "GetDefaultMessage", new FrameworkMethodSignatureId(0, 0, 0, text, [])),
			new FrameworkMemberId(resource, "Format", new FrameworkMethodSignatureId(0, 0, 3, text, [text, value, value]))
		};
		foreach (var leaf in leaves)
		{
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, caller, out var binding));
			Assert.StartsWith("shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowSystemResources::", binding.Target);
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaf, catalog, definition, out _));
		}
		pack.Replace("packVersion", "10.0.10");
		Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(leaves[0], Load(pack), caller, out _));
	}

	[Fact]
	public void IntegerPredicateIdentityRejectsOtherClosedTypesAndMethods()
	{
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var owner = FrameworkTypeId.Named("System.Runtime", "System.Numerics.INumberBase`1");
		var signature = new FrameworkMethodSignatureId(0, 0, 1, FrameworkTypeId.Primitive("System.Boolean"), [FrameworkTypeId.GenericTypeParameter(0)]);
		Assert.True(StringBuilderExceptionSurface.IsIntegerPredicate(new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(owner, [integer]), "IsNegative", signature)));
		Assert.False(StringBuilderExceptionSurface.IsIntegerPredicate(new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(owner, [FrameworkTypeId.Primitive("System.Double")]), "IsNegative", signature)));
		Assert.False(StringBuilderExceptionSurface.IsIntegerPredicate(new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(owner, [integer]), "IsPositive", signature)));
	}

	private static FrameworkImplementationPackCatalog Load(FrameworkImplementationPackTests.CoreLibPack pack) =>
		FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
}
