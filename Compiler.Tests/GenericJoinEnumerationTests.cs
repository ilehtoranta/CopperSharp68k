/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class GenericJoinEnumerationTests
{
	[Fact]
	public void ManagedWideNullableConstructorsRetainTheConstructedSize()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibWideNullableTransportEntry");
		var constructors = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Newobj)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset))
			.Where(target => target.Definition?.Name == ".ctor" && target.ConstructedDeclaringType?.IsNullable == true).ToArray();
		Assert.NotEmpty(constructors);
		Assert.Contains(constructors, target => target.ConstructedDeclaringType!.NullableElementType!.IsEnum);
		foreach (var target in constructors)
		{
			Assert.Null(target.ImportName);
			Assert.Equal(12, target.ConstructedDeclaringType!.Size);
			Assert.Equal(12, target.Definition!.ConstructedDeclaringType!.Size);
			Assert.Equal(8, Assert.Single(target.Signature.ParameterTypes).Size);
			Assert.True(module.TryGetReferenceFreeStructLayout(target.ConstructedDeclaringType, entry.ModuleName, out var layout));
			Assert.Equal(12, layout.Size);
		}
	}

	[Fact]
	public void NullableJoinAdmissionKeepsUnsupportedRepresentationsExcluded()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		foreach (var element in new[] {
			new CilType(CilTypeKind.SignedInteger, 4, "int"), new CilType(CilTypeKind.UnsignedInteger, 4, "uint"),
			new CilType(CilTypeKind.SignedInteger, 8, "long"), new CilType(CilTypeKind.FloatingPoint, 4, "float"), new CilType(CilTypeKind.FloatingPoint, 8, "double"),
			new CilType(CilTypeKind.FloatingPoint, 8, "float"), new CilType(CilTypeKind.FloatingPoint, 4, "double"),
			new CilType(CilTypeKind.ValueType, 0, "System.Decimal"), new CilType(CilTypeKind.ValueType, 16, "System.Decimal"),
			new CilType(CilTypeKind.Boolean, 1, "bool"), new CilType(CilTypeKind.ManagedReference, 4, "object") })
		{
			var type = new CilType(CilTypeKind.ValueType, CilType.NullableStorageSize(element), $"System.Nullable<{element.DisplayName}>", GenericArguments: [element]);
			Assert.Equal(element.DisplayName is "int" or "uint" or "bool" or "long" ||
				element.DisplayName == "float" && element.Size == 4 || element.DisplayName == "double" && element.Size == 8 ||
				element.DisplayName == "System.Decimal" && element.Size == 0,
				module.IsExperimentalNullableJoinValue(type));
			Assert.False(stable.IsExperimentalNullableJoinValue(type));
			Assert.False(module.IsExperimentalNullableJoinValue(type with { Size = type.Size + 4 }));
		}
	}

	[Fact]
	public void ManagedDecimalNullableConstructorsRetainTheVerifiedAggregateSize()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibDecimalNullableTransportEntry");
		var constructors = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Newobj || instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset))
			.Where(target => target.Definition?.Name == ".ctor" && target.ConstructedDeclaringType?.IsNullable == true).ToArray();
		Assert.NotEmpty(constructors);
		foreach (var target in constructors)
		{
			Assert.Null(target.ImportName);
			Assert.Equal(20, target.ConstructedDeclaringType!.Size);
			Assert.Equal(20, target.Definition!.ConstructedDeclaringType!.Size);
			var payload = Assert.Single(target.Signature.ParameterTypes);
			Assert.Equal("System.Decimal", payload.DisplayName);
			Assert.True(module.TryGetReferenceFreeStructLayout(payload, entry.ModuleName, out var payloadLayout));
			Assert.Equal(16, payloadLayout.Size);
			Assert.True(module.TryGetReferenceFreeStructLayout(target.ConstructedDeclaringType, entry.ModuleName, out var layout));
			Assert.Equal(20, layout.Size);
			Assert.Equal(0u, layout.ReferenceBitmap);
		}
	}

	[Theory]
	[InlineData("bool")]
	[InlineData("char")]
	[InlineData("System.Nullable<int>")]
	[InlineData("System.Nullable<uint>")]
	[InlineData("System.Nullable<float>")]
	[InlineData("System.Nullable<double>")]
	[InlineData("System.Nullable<System.Decimal>")]
	[InlineData("System.Nullable<long>")]
	[InlineData("System.Nullable<ulong>")]
	[InlineData("System.Nullable<JoinInt64>")]
	[InlineData("System.Nullable<JoinUInt64>")]
	[InlineData("System.Nullable<bool>")]
	[InlineData("System.Nullable<char>")]
	[InlineData("System.Nullable<sbyte>")]
	[InlineData("System.Nullable<byte>")]
	[InlineData("System.Nullable<short>")]
	[InlineData("System.Nullable<ushort>")]
	[InlineData("System.Nullable<JoinSByte>")]
	[InlineData("System.Nullable<JoinByte>")]
	[InlineData("System.Nullable<JoinInt16>")]
	[InlineData("System.Nullable<JoinUInt16>")]
	[InlineData("System.Nullable<JoinInt32>")]
	[InlineData("System.Nullable<JoinUInt32>")]
	[InlineData("sbyte")]
	[InlineData("byte")]
	[InlineData("short")]
	[InlineData("ushort")]
	[InlineData("uint")]
	[InlineData("long")]
	[InlineData("ulong")]
	[InlineData("JoinSByte")]
	[InlineData("JoinByte")]
	[InlineData("JoinInt16")]
	[InlineData("JoinUInt16")]
	[InlineData("JoinInt32")]
	[InlineData("JoinUInt32")]
	[InlineData("JoinInt64")]
	[InlineData("JoinUInt64")]
	public void ClosedIntegralAndEnumAdaptersRequireExactCallersSignaturesAndOptIn(string name)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		using var module = Open(pack, true);
		using var stable = Open(pack, false);
		var isEnum = name.StartsWith("Join", StringComparison.Ordinal);
		var isNullable = name.StartsWith("System.Nullable<", StringComparison.Ordinal);
		var smallNullable = isNullable && name is not ("System.Nullable<int>" or "System.Nullable<uint>");
		var requestedElement = isNullable ? name["System.Nullable<".Length..^1] : name;
		var wideNullable = isNullable && requestedElement is "long" or "ulong" or "JoinInt64" or "JoinUInt64";
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::" +
			(isNullable && requestedElement == "System.Decimal" ? "CoreLibGenericDecimalNullableJoinsEntry" :
			 isNullable && requestedElement == "float" ? "CoreLibGenericSingleNullableJoinsEntry" :
			 isNullable && requestedElement == "double" ? "CoreLibGenericDoubleNullableJoinsEntry" :
			 wideNullable ? "CoreLibGenericWideNullableJoinsEntry" : smallNullable ? requestedElement.StartsWith("Join", StringComparison.Ordinal) ? "CoreLibGenericSmallNullableEnumJoinsEntry" : "CoreLibGenericSmallNullablePrimitiveJoinsEntry" :
			 isNullable ? "CoreLibGenericNullableIntJoinsEntry" : isEnum ? "CoreLibStringBuilderGenericEnumJoinsEntry" : name is "bool" or "char" ? "CoreLibStringBuilderGenericBooleanCharJoinsEntry" : "CoreLibStringBuilderGenericIntegralJoinsEntry"));
		var helper = entry.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, entry, instruction.Offset).Definition)
			.First(method => method?.Name == (smallNullable ? "CheckSmallNullableJoin" : "CheckGenericJoin") && method.MethodTypeArguments is [var type] &&
				(type.DisplayName == name || type.DisplayName == requestedElement || type.DisplayName.EndsWith("/" + requestedElement, StringComparison.Ordinal)))!;
		if (smallNullable)
			helper = helper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
				.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, helper, instruction.Offset).Definition)
				.First(method => method?.Name == "CheckGenericJoin")!;
		var wrapper = helper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, helper, instruction.Offset).Definition)
			.First(method => method?.Name == "AppendJoin")!;
		var core = wrapper.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call)
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, wrapper, instruction.Offset).Definition)
			.First(method => method?.DisplayName.StartsWith("System.Text.StringBuilder::AppendJoinCore<", StringComparison.Ordinal) == true)!;
		var element = Assert.Single(core.MethodTypeArguments);
		Assert.Equal(isEnum, element.IsEnum);
		foreach (var operation in new[] { "GetEnumerator", "get_Current", "MoveNext", "Dispose" })
		{
			var enumerable = FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerable`1");
			var enumerator = FrameworkTypeId.Named("System.Runtime", "System.Collections.Generic.IEnumerator`1");
			var parameter = FrameworkTypeId.GenericTypeParameter(0);
			var declaringType = operation == "GetEnumerator" ? FrameworkTypeId.GenericInstantiation(enumerable, [FrameworkTypeId.GenericMethodParameter(0)]) :
				operation == "get_Current" ? FrameworkTypeId.GenericInstantiation(enumerator, [FrameworkTypeId.GenericMethodParameter(0)]) :
				FrameworkTypeId.Named("System.Runtime", operation == "MoveNext" ? "System.Collections.IEnumerator" : "System.IDisposable");
			var resultType = operation == "GetEnumerator" ? FrameworkTypeId.GenericInstantiation(enumerator, [parameter]) :
				operation == "get_Current" ? parameter : FrameworkTypeId.Primitive(operation == "MoveNext" ? "System.Boolean" : "System.Void");
			var signature = new FrameworkMethodSignatureId(0x20, 0, 0, resultType, []);
			var member = new FrameworkMemberId(declaringType, operation, signature);
			Assert.True(module.TryCreateExperimentalJoinEnumerationBinding(member, core, out var binding));
			Assert.Equal(operation == "GetEnumerator" ? "CopperSharp.Runtime.ShadowGenericJoinEnumeration" :
				"CopperSharp.Runtime.ShadowGenericJoinEnumerator`1", binding.ShadowMethod!.TypeName);
			Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect |
				FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory));
			Assert.False(stable.TryCreateExperimentalJoinEnumerationBinding(member, core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, helper, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, wrapper, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { ModuleName = "Application" }, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(member, core with { MethodTypeArguments = [element with { Size = 3 }] }, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(declaringType, operation,
				new FrameworkMethodSignatureId(0, 0, 0, resultType, [])), core, out _));
			Assert.False(module.TryCreateExperimentalJoinEnumerationBinding(new FrameworkMemberId(declaringType, operation,
				new FrameworkMethodSignatureId(0x20, 0, 1, resultType, [parameter])), core, out _));
		}
		var current = Assert.Single(core.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt &&
			module.DescribeFrameworkMethodToken((int)instruction.Operand!, core, instruction.Offset).Name == "get_Current")
			.Select(instruction => module.ResolveMethodToken((int)instruction.Operand!, core, instruction.Offset).Definition!)
			.DistinctBy(method => method.Identity));
		var layout = module.GetTypeLayout(current);
		var payloadSize = Math.Max(4, element.Size);
		Assert.Equal(28 + payloadSize, layout.Size);
		Assert.Equal(3u | (1u << ((16 + payloadSize) / 4)), layout.ReferenceBitmap);
		Assert.Equal(element, current.Signature.ReturnType);
		if (isEnum || isNullable || name is "bool" or "char")
		{
			var calls = core.Instructions.Where(instruction => instruction.ConstrainedTypeToken != null).ToArray();
			Assert.NotEmpty(calls);
			foreach (var call in calls)
			{
				var declaration = module.ResolveMethodToken((int)call.Operand!, core, call.Offset).Definition!;
				var implementation = module.ResolveConstrainedInterfaceImplementation(core, call.ConstrainedTypeToken!.Value, call.Offset, declaration);
				Assert.Equal(isNullable ? element.DisplayName + "::ToString" : isEnum ? "CopperSharp.Runtime.ShadowBoxedEnum::ToString" : name == "bool" ? "System.Boolean::ToString" : "System.Char::ToString", implementation.DisplayName);
				if (isNullable)
				{
					var identity = module.ResolveRuntimeTypeIdentity(element, core.ModuleName);
					Assert.False(identity.Handle.IsNil);
					Assert.Equal("System.Private.CoreLib", identity.ModuleName);
					Assert.True(identity.IsConstructedGeneric);
					var valueLayout = module.GetRuntimeTypeLayout(identity);
					Assert.Equal(element.Size, valueLayout.Size); Assert.Equal(0u, valueLayout.ReferenceBitmap);
					Assert.Equal(new[] { element.NullableElementType!.DisplayName == "System.Decimal" ? 0 : Math.Max(4, element.NullableElementType.Size) - element.NullableElementType.Size,
						element.Size - 1 }, valueLayout.FieldOffsets.Values.Order().ToArray());
				}
				Assert.Throws<M68kCompilationException>(() => stable.ResolveConstrainedInterfaceImplementation(core, call.ConstrainedTypeToken.Value, call.Offset, declaration));
				Assert.Throws<M68kCompilationException>(() => module.ResolveConstrainedInterfaceImplementation(core, call.ConstrainedTypeToken.Value, call.Offset,
					declaration with { ModuleName = "Application" }));
			}
		}
	}
	private static CompilationModule Open(FrameworkImplementationPackTests.CoreLibPack pack, bool enabled) =>
		new(typeof(CompilerFixtures).Assembly.Location, managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowGenericJoinEnumeration).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath) { EnableUnlistedManagedBodies = enabled }));
}
