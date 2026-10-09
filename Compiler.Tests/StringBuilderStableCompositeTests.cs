/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderStableCompositeTests
{
	[Fact]
	public void ApplicationFormattingLeavesRequireTheirActualCallSiteAndProviderImplementation()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			foreach (var typeName in new[] { "ParsedFormatProvider", "CompositeProviderLookalike" })
			{
				var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == typeName);
				var caller = type.GetMethods().Select(module.GetMethod).Single(method => method.Name == "GetFormat");
				var calls = caller.Instructions.Where(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call &&
					module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).DeclaringType.FullMetadataName == "System.Type").ToArray();
				Assert.NotEmpty(calls);
				foreach (var name in new[] { "System.ICustomFormatter", "System.Globalization.NumberFormatInfo", "System.Globalization.CultureInfo" })
					Assert.Equal(admitted && typeName == "ParsedFormatProvider" && name != "System.Globalization.CultureInfo",
						module.IsPinnedStringBuilderFormattingTypeToken(caller, new CilType(CilTypeKind.ManagedReference, 4, name)));
				foreach (var call in calls)
				{
					var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
					Assert.Equal(admitted && typeName == "ParsedFormatProvider", module.TryCreatePinnedApplicationFormattingLeafBinding(member, caller, call.Offset, out _));
					Assert.False(module.TryCreatePinnedApplicationFormattingLeafBinding(member, null, call.Offset, out _));
					Assert.False(module.TryCreatePinnedApplicationFormattingLeafBinding(member, caller, -1, out _));
					Assert.False(module.TryCreatePinnedApplicationFormattingLeafBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), caller, call.Offset, out _));
				}
			}
			var provider = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == "ParsedFormatProvider");
			var format = provider.GetMethods().Select(module.GetMethod).Single(method => method.Name == "Format");
			var copy = format.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Newobj && module.DescribeFrameworkMethodToken((int)instruction.Operand!, format, instruction.Offset).DeclaringType.FullMetadataName == "System.String");
			var constructor = module.DescribeFrameworkMethodToken((int)copy.Operand!, format, copy.Offset);
			Assert.Equal(admitted, module.TryCreatePinnedApplicationFormattingLeafBinding(constructor, format, copy.Offset, out _));
			var direct = format with { Instructions = format.Instructions.Select(instruction => instruction.Offset == copy.Offset ? instruction with { OpCode = System.Reflection.Emit.OpCodes.Call } : instruction).ToArray() };
			Assert.False(module.TryCreatePinnedApplicationFormattingLeafBinding(constructor, direct, copy.Offset, out _));
		}
	}

	[Fact]
	public void CompositeAdmissionRequiresReleasedInputExactMembersAndOwnedPrivateCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			var parse = StringBuilderCompositeSurface.Members.Single(member => member.Name == "Parse");
			var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.CompositeFormat"), parse.Name, parse.Signature);
			foreach (var member in StringBuilderCompositeSurface.Members.Concat(StringBuilderCompositeSurface.ListMembers)
				.Concat(StringBuilderCompositeSurface.BufferMembers).Append(StringBuilderCompositeSurface.SegmentConstructor))
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding));
				if (binding is not null) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
				Assert.Equal(admitted && StringBuilderCompositeSurface.IsPublic(member), FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				Assert.Equal(admitted && StringBuilderCompositeSurface.IsPublic(member), FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog,
					new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), null, catalog, caller, out _));
			}
			var numberInfo = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Globalization.NumberFormatInfo"), ".ctor",
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.Void"), []));
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(numberInfo, null, catalog, null, out var numberBinding));
			if (numberBinding is not null) Assert.True(numberBinding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(FrameworkTypeId.Named("Application", numberInfo.DeclaringType.FullMetadataName!), numberInfo.Name, numberInfo.Signature), null, catalog, null, out _));
			var parameterName = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.ArgumentException"), "get_ParamName",
				new FrameworkMethodSignatureId(0x20, 0, 0, FrameworkTypeId.Primitive("System.String"), []));
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(parameterName, null, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(parameterName.DeclaringType, parameterName.Name, numberInfo.Signature), null, catalog, null, out _));
		}
	}

	[Fact]
	public void ParserConstrainedBufferFallbackRequiresItsActualCallInstruction()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			var caller = module.ResolveEntryPoint("System.Text.CompositeFormat::TryParseLiterals");
			var calls = caller.Instructions.Where(instruction => instruction.ConstrainedTypeToken is not null).ToArray();
			Assert.Equal(3, calls.Length);
			foreach (var call in calls)
			{
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
				Assert.Equal(admitted, module.TryCreatePinnedCompositeBufferToStringBinding(member, caller, call.Offset, out _, out var implementation));
				if (implementation is not null) Assert.Equal("ToString", implementation.Name);
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, null, call.Offset, out _, out _));
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, caller, -1, out _, out _));
				var direct = caller with { Instructions = caller.Instructions.Select(instruction => instruction.Offset == call.Offset ? instruction with { OpCode = System.Reflection.Emit.OpCodes.Call } : instruction).ToArray() };
				Assert.False(module.TryCreatePinnedCompositeBufferToStringBinding(member, direct, call.Offset, out _, out _));
			}
		}
	}

	[Fact]
	public void ParsedSegmentStorageAndCopyPreserveExactRootsAndRejectOtherTypes()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			var segment = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::RetainCompositeSegment").Signature.ReturnType;
			Assert.Equal(admitted, module.TryGetReferenceFreeStructLayout(segment, "System.Private.CoreLib", out var layout));
			if (layout is not null) { Assert.Equal(16, layout.Size); Assert.Equal(9u, layout.ReferenceBitmap); }
			Assert.Equal(admitted, module.TryGetReferenceFreeStructLayout(segment, "System.Runtime", out _));
			Assert.False(module.TryGetReferenceFreeStructLayout(segment with { GenericArguments = segment.GenericArguments.SetItem(1, new CilType(CilTypeKind.UnsignedInteger, 4, "uint")) }, "System.Private.CoreLib", out _));
			Assert.False(CompilationModule.IsCompositeFormatSegment(segment with { DisplayName = "System.ValueTuple`4<Application>" }));
			if (!admitted) return;
			var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::CoreLibStringBuilderParsedCompositeFormatEntry");
			var parseCall = entry.Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call && module.DescribeMethodToken((int)instruction.Operand!, entry, instruction.Offset)?.Name == "Parse");
			var parse = module.ResolveMethodToken((int)parseCall.Operand!, entry, parseCall.Offset).Definition!;
			var listCall = parse.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt && module.DescribeMethodToken((int)instruction.Operand!, parse, instruction.Offset)?.Name == "ToArray");
			var caller = module.ResolveMethodToken((int)listCall.Operand!, parse, listCall.Offset).Definition!;
			var copy = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call && module.DescribeMethodToken((int)instruction.Operand!, caller, instruction.Offset)?.Name == "Copy");
			var member = module.DescribeFrameworkMethodToken((int)copy.Operand!, caller, copy.Offset);
			Assert.True(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller, out _));
			Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller with { ModuleName = "Application" }, out _));
			Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(member, caller with { ConstructedDeclaringType = null }, out _));
			Assert.False(module.TryCreateExperimentalCompositeSegmentCopyBinding(new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), caller, out _));
		}
	}

	[Fact]
	public void ReleasedCompositeDefinitionsMatchTheAuditedSurface()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(pack.AssemblyPath,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == "CompositeFormat");
		var methods = type.GetMethods().Select(module.GetMethod).ToArray();
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-composite-definitions.txt"), methods.Select(method =>
			module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1).DisplayName));
		File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "stringbuilder-composite-buffer-calls.txt"), methods.SelectMany(method => method.Instructions
			.Where(instruction => instruction.ConstrainedTypeToken is not null).Select(instruction =>
				$"{method.Name} IL_{instruction.Offset:X4}: {module.ResolveTypeToken(instruction.ConstrainedTypeToken!.Value, method, instruction.Offset)} -> {module.DescribeFrameworkMethodToken((int)instruction.Operand!, method, instruction.Offset).DisplayName}; caller={StringBuilderCompositeSurface.IsOwnedCaller(module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1))}")));
		Assert.Contains(methods, method => method.Name == "Parse");
		Assert.Equal(StringBuilderCompositeSurface.Members.Count, methods.Length);
		foreach (var method in methods)
			Assert.True(StringBuilderCompositeSurface.Contains(module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(method.Handle), method, -1)));
		var resources = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == "ExceptionResource");
		var capacity = resources.GetFields().Select(module.Reader.GetFieldDefinition).Single(field => module.Reader.GetString(field.Name) == "ArgumentOutOfRange_SmallCapacity");
		Assert.Equal(15, module.Reader.GetBlobReader(module.Reader.GetConstant(capacity.GetDefaultValue()).Value).ReadInt32());
		Assert.Equal("ArgumentOutOfRange_SmallCapacity", CopperSharp.Runtime.ShadowSystemResources.GetFormattingResourceString(15));
	}

	[Fact]
	public void ReleasedCompositeFormattingMatchesHostAndClosesTheGraph()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var entry in new[] { "CoreLibStringBuilderParsedCompositeFormatEntry", "CoreLibParsedCompositeFormatProviderContractsEntry", "CoreLibParsedCompositeFormatValidationEntry" })
		{
		Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-composite-" + entry + ".json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
		}
	}
}

public static partial class CompilerFixtures
{
	private sealed class CompositeProviderLookalike
	{
		public object? GetFormat(Type type) => type == typeof(ICustomFormatter) ? this : null;
	}
}
