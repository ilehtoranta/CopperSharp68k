/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderIntegralAdmissionTests
{
	[Fact]
	public void NumericArithmeticAndPointerSpansRequireExactPinnedDefinitions()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var number = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
				module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == "Number");
			var callerBody = module.GetMethod(number.GetMethods().Single(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "FormatExponent"));
			var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(callerBody.Handle), callerBody, 0);
			foreach (var owner in new[] { "System.Math", "System.ReadOnlySpan`1" })
			{
				var member = StringBuilderNumericSurface.Members.Single(member =>
					(member.DeclaringType.ElementType ?? member.DeclaringType).FullMetadataName == owner && member.Name == (owner == "System.Math" ? "DivRem" : ".ctor"));
				var typeName = owner[(owner.LastIndexOf('.') + 1)..];
				var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
					module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == typeName);
				var definitions = type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == member.Name).Select(module.GetMethod)
					.Select(method => module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, 0));
				Assert.Single(definitions.Where(definition => FrameworkImplementationProfile.Canonicalize(definition).Signature.Equals(member.Signature)));
				var accepted = owner == "System.Math"
					? FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding)
					: FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, caller, out binding);
				Assert.Equal(admitted, accepted);
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				if (admitted) Assert.Equal(owner == "System.Math" ? FrameworkBindingKind.PinnedManagedBody : FrameworkBindingKind.Intrinsic, binding.Kind);
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
					new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, [FrameworkTypeId.Primitive("System.Char")]), catalog, caller, out _));
			}
			var tuple = new CilType(CilTypeKind.ValueType, 0, "System.ValueTuple`2<uint,uint>",
				GenericArguments: [new CilType(CilTypeKind.UnsignedInteger, 4, "uint"), new CilType(CilTypeKind.UnsignedInteger, 4, "uint")]);
			Assert.Equal(admitted, module.IsSupportedStructType(tuple));
			if (admitted) {
				Assert.True(module.TryGetReferenceFreeStructLayout(tuple, "System.Private.CoreLib", out var layout));
				Assert.Equal(8, layout.Size); Assert.Equal(0u, layout.ReferenceBitmap); Assert.Equal(new[] { 0, 4 }, layout.FieldOffsets.Values.Order());
			}
			Assert.False(module.IsSupportedStructType(tuple with { GenericArguments = [new CilType(CilTypeKind.ManagedReference, 4, "object"), new CilType(CilTypeKind.UnsignedInteger, 4, "uint")] }));
		}
	}

	[Fact]
	public void ConstrainedIntegerFallbackRequiresTheAuditedFormattingCallSite()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
				module.Reader.GetString(type.Namespace) == "System.Text" && module.Reader.GetString(type.Name) == "StringBuilder");
			foreach (var helper in new[] { "AppendSpanFormattable", "InsertSpanFormattable" })
			{
				var body = module.GetMethod(type.GetMethods().Single(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == helper));
				var call = Assert.Single(body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Callvirt &&
					module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset).Name == "ToString"));
				var slot = module.DescribeFrameworkMethodToken((int)call.Operand!, body, call.Offset);
				foreach (var (name, kind, size, owner) in new[] {
					("sbyte", CilTypeKind.SignedInteger, 1, "System.SByte"), ("byte", CilTypeKind.UnsignedInteger, 1, "System.Byte"),
					("short", CilTypeKind.SignedInteger, 2, "System.Int16"), ("ushort", CilTypeKind.UnsignedInteger, 2, "System.UInt16"),
					("int", CilTypeKind.SignedInteger, 4, "System.Int32"), ("uint", CilTypeKind.UnsignedInteger, 4, "System.UInt32"),
					("long", CilTypeKind.SignedInteger, 8, "System.Int64"), ("ulong", CilTypeKind.UnsignedInteger, 8, "System.UInt64"),
					("float", CilTypeKind.FloatingPoint, 4, "System.Single"), ("double", CilTypeKind.FloatingPoint, 8, "System.Double") })
				{
					var caller = body with { MethodTypeArguments = [new CilType(kind, size, name)] };
					Assert.Equal(admitted, module.TryCreatePinnedIntegralToStringBinding(slot, caller, call.Offset, out var binding, out var implementation));
					if (admitted) { Assert.Equal(owner + "::ToString", implementation.DisplayName); Assert.Equal("managed:constrained-integral-tostring", binding.Target); }
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(slot, caller, -1, out _, out _));
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(slot, caller with { ModuleName = "Application" }, call.Offset, out _, out _));
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(new FrameworkMemberId(slot.DeclaringType, "Unlisted", slot.Signature), caller, call.Offset, out _, out _));
					var direct = caller with { Instructions = caller.Instructions.Select(instruction => instruction.Offset == call.Offset ? instruction with { OpCode = OpCodes.Call } : instruction).ToArray() };
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(slot, direct, call.Offset, out _, out _));
				}
				foreach (var argument in new[] { new CilType(CilTypeKind.Character, 2, "char"), new CilType(CilTypeKind.Boolean, 1, "bool"), new CilType(CilTypeKind.FloatingPoint, 4, "double") })
					Assert.False(module.TryCreatePinnedIntegralToStringBinding(slot, body with { MethodTypeArguments = [argument] }, call.Offset, out _, out _));
				Assert.False(module.TryCreatePinnedIntegralToStringBinding(slot, body, call.Offset, out _, out _));
			}
		}
	}

	[Fact]
	public void NumericMemoryLeavesRequireExactCharacterSpecializationsAndOwnedCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			foreach (var (owner, name, dependency, target, effects) in new[] {
				("Number", "UInt32ToDecChars", "WriteTwoDigits", "shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowCustomNumberFormatting::WriteTwoDigits", FrameworkEffects.WritesManagedMemory),
				("ReadOnlySpan`1", "TryCopyTo", "Memmove", "intrinsic:corelib-memmove-char", FrameworkEffects.ReadsManagedMemory | FrameworkEffects.WritesManagedMemory)
			})
			{
				var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
					module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == owner);
				var body = type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == name)
					.Select(module.GetMethod).Single(method => method.Signature.ParameterTypes.Length == (name == "UInt32ToDecChars" ? 3 : 1));
				var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(body.Handle), body, 0);
				Assert.True(StringBuilderNumericSurface.IsOwnedCaller(caller));
				var call = Assert.Single(body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call &&
					module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset).Name == dependency));
				var definition = module.DescribeFrameworkMethodToken((int)call.Operand!, body, call.Offset);
				var member = new FrameworkMemberId(definition.DeclaringType, definition.Name, definition.Signature, [FrameworkTypeId.Primitive("System.Char")]);
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, caller, out var binding));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog,
					new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog,
					new FrameworkMemberId(FrameworkTypeId.Named("Application", caller.DeclaringType.FullMetadataName!), caller.Name, caller.Signature), out _));
				foreach (var arguments in new[] { Array.Empty<FrameworkTypeId>(), new[] { FrameworkTypeId.Primitive("System.Byte") }, new[] { FrameworkTypeId.Primitive("System.Char"), FrameworkTypeId.Primitive("System.Char") } })
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
						new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature, arguments), catalog, caller, out _));
				if (admitted) { Assert.Equal(target, binding.Target); Assert.Equal(effects, binding.EffectSummary.Effects); }
			}
		}
	}

	[Fact]
	public void NumericSettingGettersAreExactReadOnlyFieldLeaves()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
				module.Reader.GetString(type.Namespace) == "System.Globalization" && module.Reader.GetString(type.Name) == "NumberFormatInfo");
			var count = 0;
			foreach (var handle in type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name).StartsWith("get_", StringComparison.Ordinal)))
			{
				var body = module.GetMethod(handle);
				var member = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), body, 0);
				if (!FrameworkImplementationProfile.IsNumberFormatSettingGetter(member)) continue;
				count++;
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out var binding));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature,
					[FrameworkTypeId.Primitive("System.Int32")]), null, catalog, null, out _));
				Assert.Equal(new[] { OpCodes.Ldarg_0, OpCodes.Ldfld, OpCodes.Ret }, body.Instructions.Select(instruction => instruction.OpCode));
				if (admitted) Assert.Equal(FrameworkEffects.ReadsManagedMemory, binding.EffectSummary.Effects);
			}
			Assert.Equal(22, count);
		}
	}

	[Fact]
	public void NumericBuffersRequireVerifiedLayoutsAndOwnerBitmaps()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				managedAssemblyPaths: [typeof(CopperSharp.Runtime.ShadowNumberBuffer).Assembly.Location], frameworkImplementationPack: catalog);
			foreach (var (name, owner, size, bitmap) in new[] {
				("System.Number/NumberBuffer", "System.Private.CoreLib", 32, 128u),
				("CopperSharp.Runtime.ShadowNumberBuffer", "CopperSharp.Runtime.Managed", 32, 128u),
				("System.Collections.Generic.ValueListBuilder`1<char>", "System.Private.CoreLib", 20, 12u),
				("CopperSharp.Runtime.ShadowValueListBuilder`1<char>", "CopperSharp.Runtime.Managed", 20, 12u)
			})
			{
				var type = new CilType(CilTypeKind.ValueType, 0, name,
					GenericArguments: name.EndsWith("<char>", StringComparison.Ordinal) ? [new CilType(CilTypeKind.Character, 2, "char")] : default);
				Assert.Equal(admitted, module.TryGetReferenceFreeStructLayout(type, owner, out var layout));
				if (admitted) { Assert.Equal(size, layout.Size); Assert.Equal(bitmap, layout.ReferenceBitmap); Assert.Equal(owner, layout.ModuleName); }
			}
		}
	}

	[Fact]
	public void IntegralAppendGraphIsCompatibleWithoutUnlistedBodies()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableIntegralEntry",
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-integral-analysis.json"),
			System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName)));
	}

	[Fact]
	public void IntegralAdaptersRequireExactReleasedScalarCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			foreach (var name in new[] { "SByte", "Byte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64" })
			{
				var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type =>
					module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == name);
				var body = type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "TryFormat")
					.Select(module.GetMethod).Single(method => method.Signature.ParameterTypes[0].DisplayName == "System.Span`1<char>");
				Assert.Equal(admitted, module.IsPinnedIntegralTryFormatMethod(body));
				Assert.True(body.IsFinal && module.IsValueTypeMethod(body));
				var caller = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(body.Handle), body, 0);
				var call = Assert.Single(body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call &&
					module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset).DeclaringType.FullMetadataName == "System.Number"));
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, body, call.Offset);
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, caller, out var binding));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog,
					new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
				if (admitted)
				{
					Assert.Equal(FrameworkBindingKind.ShadowMethod, binding.Kind);
					Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
				}
			}
		}
	}
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableIntegralEntry()
	{
		var builder = new System.Text.StringBuilder(1);
		builder.Append(sbyte.MinValue).Append('|').Append(byte.MaxValue).Append('|').Append(short.MinValue).Append('|').Append(ushort.MaxValue);
		GC.Collect();
		builder.Append('|').Append(int.MinValue).Append('|').Append(uint.MaxValue).Append('|').Append(long.MinValue).Append('|').Append(ulong.MaxValue);
		GC.Collect();
		return builder.ToString() == "-128|255|-32768|65535|-2147483648|4294967295|-9223372036854775808|18446744073709551615" ? 42 : 1;
	}
}
