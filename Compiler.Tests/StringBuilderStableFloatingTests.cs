/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderStableFloatingGraphTests
{
	[Fact]
	public void FloatingJoinFallbackRequiresTheExactSpecializationAndCall()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "StringBuilder");
			var definitions = module.Reader.GetTypeDefinition(owner).GetMethods().Select(module.GetMethod)
				.Where(method => method.Name == "AppendJoinCore" && method.Signature.GenericParameterCount == 1).ToArray();
			Assert.Equal(2, definitions.Length);
			foreach (var definition in definitions)
			foreach (var (name, size, primitive, kind) in new[] {
				("float", 4, "Single", CilTypeKind.FloatingPoint), ("double", 8, "Double", CilTypeKind.FloatingPoint),
				("bool", 1, "Boolean", CilTypeKind.Boolean), ("char", 2, "Char", CilTypeKind.Character) })
			{
				var caller = definition with { MethodTypeArguments = [new CilType(kind, size, name)] };
				var calls = caller.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt &&
					module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).Name == "ToString").ToArray();
				Assert.Equal(2, calls.Length);
				foreach (var call in calls)
				{
					var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
					Assert.Equal(admitted, module.TryCreatePinnedIntegralToStringBinding(member, caller, call.Offset, out var binding, out var implementation));
					if (admitted) { Assert.Equal("System." + primitive + "::ToString", implementation.DisplayName); Assert.Equal("managed:constrained-join-tostring", binding.Target); }
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller, -1, out _, out _));
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller with { ModuleName = "Application" }, call.Offset, out _, out _));
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller with { MethodTypeArguments = [new CilType(kind, size == 4 ? 8 : 4, name)] }, call.Offset, out _, out _));
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(member, caller with { MethodTypeArguments = [new CilType(CilTypeKind.ManagedReference, size, name)] }, call.Offset, out _, out _));
				}
			}
		}
	}

	[Fact]
	public void FloatingEnumerationDiscoversOnlyAllocatedImplementations()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		foreach (var (name, size, kind) in new[] {
			("float", 4, CilTypeKind.FloatingPoint), ("double", 8, CilTypeKind.FloatingPoint),
			("bool", 1, CilTypeKind.Boolean), ("char", 2, CilTypeKind.Character) })
		foreach (var (ownerName, methodName) in new[] { ("IEnumerable`1", "GetEnumerator"), ("IEnumerator`1", "get_Current") })
		{
			var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == ownerName);
			var definition = module.GetMethod(module.Reader.GetTypeDefinition(owner).GetMethods().Single(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == methodName));
			var type = new CilType(CilTypeKind.ManagedReference, 4, $"System.Collections.Generic.{ownerName}<{name}>",
				GenericArguments: [new CilType(kind, size, name)]);
			var declaration = definition with { ConstructedDeclaringType = type };
			Assert.Equal(type.DisplayName, module.GetInterfaceDefinition(declaration).DisplayName);
			// No allocations have been registered. A whole-CoreLib scan instead
			// constructs unrelated async enumerators and fails on CancellationToken.
			Assert.Empty(module.GetInterfaceImplementations(declaration));
		}
	}

	[Fact]
	public void FloatingEnumerationRequiresExactTypedContractsAndPinnedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var members = StringBuilderEnumerationInterfaces.Members.Where(member => member.DeclaringType.GenericArguments is [var element] &&
			element.MetadataName is "System.Single" or "System.Double").ToArray();
		Assert.Equal(4, members.Length);
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var member in members)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				var wrongName = new FrameworkMemberId(member.DeclaringType, "AdjacentMethod", member.Signature);
				Assert.False(StringBuilderEnumerationInterfaces.Contains(wrongName));
				var wrongSignature = new FrameworkMethodSignatureId(0, 0, 0, member.Signature.ReturnType, []);
				Assert.False(StringBuilderEnumerationInterfaces.Contains(new FrameworkMemberId(member.DeclaringType, member.Name, wrongSignature)));
				var wrongOwner = FrameworkTypeId.GenericInstantiation(member.DeclaringType.ElementType!, [FrameworkTypeId.Primitive("System.Byte")]);
				Assert.False(StringBuilderEnumerationInterfaces.Contains(new FrameworkMemberId(wrongOwner, member.Name, member.Signature)));
			}
		}
	}

	[Fact]
	public void FloatingSpanAbiRequiresTheExactReleasedTryFormatDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			foreach (var name in new[] { "Single", "Double" })
			{
				var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == name);
				var methods = module.Reader.GetTypeDefinition(owner).GetMethods().Select(module.GetMethod)
					.Where(method => method.Name == "TryFormat").ToArray();
				Assert.Equal(2, methods.Length); // Character and UTF-8 overloads.
				Assert.Single(methods.Where(method => module.IsPinnedFloatingTryFormatMethod(method) == admitted &&
					method.Signature.ParameterTypes[0].DisplayName == "System.Span`1<char>"));
				foreach (var method in methods)
				{
					Assert.Equal(admitted && method.Signature.ParameterTypes[0].DisplayName == "System.Span`1<char>", module.IsPinnedFloatingTryFormatMethod(method));
					Assert.False(module.IsPinnedFloatingTryFormatMethod(method with { ModuleName = "Application" }));
					Assert.False(module.IsPinnedFloatingTryFormatMethod(method with { Name = "AdjacentTryFormat" }));
				}
			}
		}
	}

	[Fact]
	public void FloatingStaticDispatchSelectsTheSameTargetBodyAsReachability()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowSingle).Assembly.Location],
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Number");
		var definition = module.GetMethod(module.Reader.GetTypeDefinition(owner).GetMethods().Single(handle =>
			module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "FormatFloat" && module.Reader.GetMethodDefinition(handle).GetGenericParameters().Count == 2));
		foreach (var (name, size, shadow) in new[] { ("float", 4, "ShadowSingle"), ("double", 8, "ShadowDouble") })
		{
			var caller = definition with { MethodTypeArguments = [new CilType(CilTypeKind.FloatingPoint, size, name), new CilType(CilTypeKind.Character, 2, "char")] };
			var predicates = caller.Instructions.Where(instruction => instruction.ConstrainedTypeToken is not null &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).Name is "IsFinite" or "IsNaN" or "IsNegative").ToArray();
			Assert.Equal(5, predicates.Length);
			Assert.Equal(new[] { "IsFinite", "IsNaN", "IsNegative" }, predicates.Select(instruction =>
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).Name).Distinct().OrderBy(value => value, StringComparer.Ordinal));
			foreach (var instruction in predicates)
			{
				var declaration = module.ResolveMethodToken((int)instruction.Operand!, caller, instruction.Offset).Definition!;
				var implementation = module.ResolveConstrainedInterfaceImplementation(caller, instruction.ConstrainedTypeToken!.Value, instruction.Offset, declaration);
				Assert.Equal("CopperSharp.Runtime.Managed", implementation.ModuleName);
				Assert.Equal("CopperSharp.Runtime." + shadow + "::" + declaration.Name, implementation.DisplayName);
				Assert.Equal(implementation.Identity, module.ApplyTargetRuntimeOverride(implementation).Identity);
			}
		}
	}

	[Fact]
	public void FloatingSnapshotsRequireReleasedFormattersAndConstrainedCalls()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Number");
			var definition = module.GetMethod(module.Reader.GetTypeDefinition(owner).GetMethods().Single(handle =>
				module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "FormatFloat" && module.Reader.GetMethodDefinition(handle).GetGenericParameters().Count == 1));
			foreach (var (name, size) in new[] { ("float", 4), ("double", 8) })
			{
				var caller = definition with { MethodTypeArguments = [new CilType(CilTypeKind.FloatingPoint, size, name)] };
				var instruction = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt && instruction.ConstrainedTypeToken is not null);
				var member = module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset);
				Assert.Equal(admitted, module.TryCreatePinnedCompositeBufferToStringBinding(member, caller, instruction.Offset, out _, out var selected));
				if (admitted) Assert.Equal("System.ReadOnlySpan`1<char>::ToString", selected.DisplayName);
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, caller with { ModuleName = "Application" }, instruction.Offset, out _, out _));
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, caller with { MethodTypeArguments = [new CilType(CilTypeKind.Character, 2, "char")] }, instruction.Offset, out _, out _));
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, caller, -1, out _, out _));
				var direct = caller with { Instructions = caller.Instructions.Select(operation => operation.Offset == instruction.Offset ? operation with { OpCode = System.Reflection.Emit.OpCodes.Call } : operation).ToArray() };
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, direct, instruction.Offset, out _, out _));
			}
		}
	}

	[Fact]
	public void FloatingCharacterCopyRequiresTheActualClosedSpecialization()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var owner = module.Reader.TypeDefinitions.Single(handle => module.Reader.GetString(module.Reader.GetTypeDefinition(handle).Name) == "Number");
		var definition = module.GetMethod(module.Reader.GetTypeDefinition(owner).GetMethods().Single(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "TryCopyTo"));
		var caller = definition with { MethodTypeArguments = [new CilType(CilTypeKind.Character, 2, "char")] };
		Assert.True(module.IsPinnedNumericSpecialization(caller));
		var instruction = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call && instruction.Offset == 0x1D &&
			module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).Name == "BitCast");
		var member = module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset);
		Assert.True(module.TryCreatePinnedNumericSpanIdentityBinding(member, caller, instruction.Offset, out _), member.DisplayName);
		Assert.False(module.TryCreatePinnedNumericSpanIdentityBinding(member, caller with { MethodTypeArguments = [new CilType(CilTypeKind.UnsignedInteger, 1, "byte")] }, instruction.Offset, out _));
		Assert.False(module.TryCreatePinnedNumericSpanIdentityBinding(member, caller with { ModuleName = "Application" }, instruction.Offset, out _));
		Assert.False(module.TryCreatePinnedNumericSpanIdentityBinding(member, caller, -1, out _));
	}

	[Fact]
	public void FloatingIntegerMemoryLeavesRequireExactOwnedMethods()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(pack.AssemblyPath,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowBuffer).Assembly.Location], frameworkImplementationPack: catalog);
		pack.Replace("packVersion", "10.0.10");
		var adjacent = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var seen = new HashSet<string>();
		foreach (var handle in module.Reader.MethodDefinitions)
		{
			var definition = module.Reader.GetMethodDefinition(handle);
			var name = module.Reader.GetString(definition.Name);
			if (name is not ("SetValue" or "Clear")) continue;
			var owner = module.Reader.GetTypeDefinition(definition.GetDeclaringType());
			if (module.Reader.GetString(owner.Name) != "BigInteger") continue;
			var method = module.GetMethod(handle);
			var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), method, -1);
			if (!StringBuilderFloatingSurface.IsBigIntegerCaller(caller, name)) continue;
			foreach (var instruction in method.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call))
			{
				var member = module.DescribeFrameworkMethodToken((int)instruction.Operand!, method, instruction.Offset);
				if (member.Name is not ("Memmove" or "Clear")) continue;
				Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, caller, out var binding));
				Assert.Equal(name == "SetValue" ? "intrinsic:corelib-memmove-uint32" : "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowBuffer::ZeroMemoryInternal", binding.Target);
				Assert.Equal(binding.Target, module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset).FrameworkBinding!.Target);
				seen.Add(name);
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, adjacent, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, new(caller.DeclaringType, "Unlisted", caller.Signature), out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(new(member.DeclaringType, "Unlisted", member.Signature, member.MethodTypeArguments), catalog, caller, out _));
				if (member.MethodTypeArguments.Length != 0)
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(new(member.DeclaringType, member.Name, member.Signature,
						[FrameworkTypeId.Primitive("System.UInt64")]), catalog, caller, out _));
			}
		}
		Assert.Equal(2, seen.Count);
	}

	[Fact]
	public void FloatingDataLeavesRequireTheirReleasedCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(pack.AssemblyPath,
			managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowFloatingPointBits).Assembly.Location], frameworkImplementationPack: catalog);
		pack.Replace("packVersion", "10.0.10");
		var adjacent = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var seen = new HashSet<string>();
		foreach (var handle in module.Reader.MethodDefinitions)
		{
			var definition = module.Reader.GetMethodDefinition(handle);
			var name = module.Reader.GetString(definition.Name);
			if (name is not ("get_CachedPowersBinaryExponent" or "get_CachedPowersDecimalExponent" or "get_CachedPowersSignificand" or "get_SmallPowersOfTen" or "get_Pow10BigNumTable" or "get_Pow10BigNumTableIndices" or "get_Pow10UInt32Table" or "get_Log2DeBruijn") && !name.EndsWith(".FloatToBits", StringComparison.Ordinal)) continue;
			var method = module.GetMethod(handle);
			var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), method, -1);
			foreach (var instruction in method.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call || instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj))
			{
				var member = module.DescribeFrameworkMethodToken((int)instruction.Operand!, method, instruction.Offset);
				if (!StringBuilderFloatingSurface.IsCachedDataCall(member, caller) && !StringBuilderFloatingSurface.IsBitProjectionCall(member, caller) && !StringBuilderFloatingSurface.IsByteTablePointerCall(member, caller)) continue;
				seen.Add(caller.DisplayName);
				Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, caller, out var binding));
				Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
				var resolved = module.ResolveMethodToken((int)instruction.Operand!, method, instruction.Offset);
				Assert.Equal(binding.Target, resolved.FrameworkBinding!.Target);
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, adjacent, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
				foreach (var changedCaller in new[] {
					new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature),
					new FrameworkMemberId(FrameworkTypeId.Named("Application", "Impostor"), caller.Name, caller.Signature),
					new FrameworkMemberId(caller.DeclaringType, caller.Name, new FrameworkMethodSignatureId(0x20, caller.Signature.GenericParameterCount,
						caller.Signature.RequiredParameterCount, caller.Signature.ReturnType, caller.Signature.ParameterTypes))
				}) Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, changedCaller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(new(member.DeclaringType, "Unlisted", member.Signature, member.MethodTypeArguments), catalog, caller, out _));
				if (member.MethodTypeArguments.Length != 0)
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(new(member.DeclaringType, member.Name, member.Signature,
						[FrameworkTypeId.Primitive(member.MethodTypeArguments[0].Equals(FrameworkTypeId.Primitive("System.Byte")) ? "System.UInt64" : "System.Byte")]), catalog, caller, out _));
			}
		}
		Assert.Equal(10, seen.Count);
	}

	[Fact]
	public void FloatingNumericInterfacesRequireExactPrecisionAndSignature()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Single"), "ToString", StringBuilderFloatingSurface.PublicMembers[0].Signature);
		pack.Replace("packVersion", "10.0.10");
		var adjacent = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		foreach (var definition in StringBuilderFloatingSurface.Interfaces)
		{
			foreach (var name in new[] { "System.Single", "System.Double", "System.Decimal", "System.Int32", "System.Char" })
			{
				var precision = FrameworkTypeId.Primitive(name);
				var member = new FrameworkMemberId(FrameworkTypeId.GenericInstantiation(definition.DeclaringType.ElementType!,
					definition.DeclaringType.GenericArguments.Select(argument => argument.Equals(FrameworkTypeId.GenericMethodParameter(0)) ? precision : argument).ToArray()),
					definition.Name, definition.Signature);
				var expected = name is "System.Single" or "System.Double";
				Assert.Equal(expected, StringBuilderFloatingSurface.ContainsInterface(member));
				Assert.Equal(expected, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, adjacent, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				Assert.False(StringBuilderFloatingSurface.ContainsInterface(new(member.DeclaringType, "Unlisted", member.Signature)));
				Assert.False(StringBuilderFloatingSurface.ContainsInterface(new(member.DeclaringType, member.Name,
					new FrameworkMethodSignatureId(member.Signature.Header, 1, member.Signature.RequiredParameterCount, member.Signature.ReturnType, member.Signature.ParameterTypes))));
			}
		}
	}

	[Fact]
	public void StableFloatingBoxesRequireTheReleasedPayloadLayouts()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack:
				FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
			foreach (var (name, size) in new[] { ("float", 4), ("double", 8) })
			{
				var type = new CilType(CilTypeKind.FloatingPoint, size, name);
				var layout = module.RegisterBoxedDispatchLayout(type, module.AssemblyName);
				if (!admitted) { Assert.Null(layout); continue; }
				Assert.NotNull(layout);
				Assert.True(module.TryGetExperimentalFloatingFormattingType(layout, out var payload));
				Assert.Equal(type, payload);
				Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { Size = size + 4 }, out _));
				Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { ReferenceBitmap = 1 }, out _));
				Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { ModuleName = "Application" }, out _));
				Assert.False(module.TryGetExperimentalFloatingFormattingType(layout with { FieldOffsets = new Dictionary<System.Reflection.Metadata.FieldDefinitionHandle, int>() }, out _));
			}
		}
	}

	[Fact]
	public void ReleasedFloatingFormatterDefinitionsCanBeAudited()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack:
			FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath)));
		var members = new List<string>();
		var definitions = new List<FrameworkMemberId>();
		foreach (var owner in module.Reader.TypeDefinitions)
		{
			var definition = module.Reader.GetTypeDefinition(owner);
			var ownerName = module.Reader.GetString(definition.Name);
			if (ownerName is not ("Grisu3" or "DiyFp" or "NumberBuffer" or "BigInteger" or "BitOperations") && (module.Reader.GetString(definition.Namespace) != "System" || ownerName is not ("Single" or "Double" or "Number"))) continue;
			foreach (var handle in definition.GetMethods())
			{
				var name = module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name);
				if (ownerName is not ("Grisu3" or "DiyFp" or "NumberBuffer" or "BigInteger") && name is not ("ToString" or "TryFormat" or "FormatFloat" or "TryFormatFloat" or "Dragon4" or "ExtractFractionAndBiasedExponent" or "GetFloatingPointMaxDigitsAndPrecision" or "LeadingZeroCount" or "Log2" or "Log2SoftwareFallback" or "get_Log2DeBruijn") && !name.EndsWith(".FloatToBits", StringComparison.Ordinal)) continue;
				var method = module.GetMethod(handle);
				var member = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), method, -1);
				members.Add(member.DisplayName);
				definitions.Add(FrameworkImplementationProfile.Canonicalize(member));
				if (name is "FormatFloat" or "TryRun" or "Dragon4")
					foreach (var instruction in method.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call))
					{
						var callMember = module.DescribeFrameworkMethodToken((int)instruction.Operand!, method, instruction.Offset);
						members.Add($"  IL_{instruction.Offset:X4}: " + callMember.DisplayName);
						if (callMember.DeclaringType.ElementType?.FullMetadataName?.Contains("IBinaryFloatParseAndFormatInfo", StringComparison.Ordinal) == true || callMember.Name is "IsFinite" or "IsNaN" or "IsNegative" or "op_Inequality" or "op_UnaryNegation")
							members.Add($"    InterfaceMatch={StringBuilderFloatingSurface.ContainsInterface(callMember)} header={callMember.Signature.Header:X2} " + System.Text.Json.JsonSerializer.Serialize(FrameworkImplementationProfile.Canonicalize(callMember)));
					}
			}
		}
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-released-floating-definitions.txt"), members);
		Assert.Equal(10, StringBuilderFloatingSurface.PublicMembers.Count);
		Assert.Equal(16, StringBuilderFloatingSurface.Formatters.Count);
		Assert.Equal(52, StringBuilderFloatingSurface.Helpers.Count);
		foreach (var bitCaller in StringBuilderFloatingSurface.BitProjectionCallers)
			Assert.Contains(bitCaller, definitions);
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Single"), "ToString", StringBuilderFloatingSurface.PublicMembers[0].Signature);
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		pack.Replace("packVersion", "10.0.10");
		var adjacent = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		foreach (var member in StringBuilderFloatingSurface.PublicMembers.Concat(StringBuilderFloatingSurface.Formatters).Concat(StringBuilderFloatingSurface.Helpers))
		{
			Assert.Contains(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature), definitions);
			Assert.True(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding));
			Assert.Equal(FrameworkBindingKind.PinnedManagedBody, binding.Kind);
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, adjacent, caller, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new(member.DeclaringType, "Unlisted", member.Signature, member.MethodTypeArguments), null, catalog, caller, out _));
			if (!StringBuilderFloatingSurface.IsPublic(member))
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
			if (member.MethodTypeArguments.Length != 0)
			{
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Decimal")]), null, catalog, caller, out _));
			}
		}
	}

	[Theory]
	[InlineData("CoreLibStringBuilderFloatingPointSmokeEntry")]
	[InlineData("CoreLibFloatingBoxedFormatProbeEntry")]
	[InlineData("CoreLibFloatingHandlerFormatProbeEntry")]
	[InlineData("CoreLibFloatingParsedFormatProbeEntry")]
	public void ReleasedFloatingGraphsRequireDisabledUnlistedBodies(string entry)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry, IncludedExportNames = [],
			ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
			FloatingPoint = M68kFloatingPointMode.SoftFloat, ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		Assert.Equal(pack.Sha256, Assert.Single(result.ImplementationPack!.Assemblies).Sha256);
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-floating-graph-" + entry + ".json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}
