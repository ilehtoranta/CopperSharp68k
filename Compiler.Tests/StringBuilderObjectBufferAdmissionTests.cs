/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderObjectBufferAdmissionTests
{
	[Fact]
	public void ApplicationFormatterSelectsItsSealedOverride()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		var caller = module.ResolveEntryPoint("StableFixedObjectFormatter::Format");
		var call = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt);
		var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
		Assert.True(module.TryCreatePinnedApplicationToStringBinding(member, caller, call.Offset, out var binding, out var implementation));
		Assert.Equal("StableFixedObjectValue::ToString", implementation.DisplayName);
		Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayAllocate | FrameworkEffects.MayCollect));
		Assert.False(module.TryCreatePinnedApplicationToStringBinding(member, null, call.Offset, out _, out _));
		Assert.False(module.TryCreatePinnedApplicationToStringBinding(member, caller, -1, out _, out _));
		Assert.False(module.TryCreatePinnedApplicationToStringBinding(new(member.DeclaringType, "Unlisted", member.Signature), caller, call.Offset, out _, out _));
		var direct = caller with { Instructions = caller.Instructions.Select(instruction => instruction.Offset == call.Offset ? instruction with { OpCode = System.Reflection.Emit.OpCodes.Call } : instruction).ToArray() };
		Assert.False(module.TryCreatePinnedApplicationToStringBinding(member, direct, call.Offset, out _, out _));
		var ingress = caller with { Instructions = caller.Instructions.Select((instruction, index) => index == 0 ? instruction with { OpCode = System.Reflection.Emit.OpCodes.Br, Operand = call.Offset } : instruction).ToArray() };
		Assert.False(module.TryCreatePinnedApplicationToStringBinding(member, ingress, call.Offset, out _, out _));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		Assert.False(adjacent.TryCreatePinnedApplicationToStringBinding(member, caller, call.Offset, out _, out _));
	}

	[Fact]
	public void FormatterTypeTokenRequiresTheExactVerifiedFormattingBody()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
		var entry = module.ResolveEntryPoint("CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableFixedObjectFormattingEntry");
		var call = entry.Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Callvirt &&
			module.DescribeFrameworkMethodToken((int)instruction.Operand!, entry, instruction.Offset).Name == "AppendFormat");
		var caller = module.ResolveMethodToken((int)call.Operand!, entry, call.Offset).Definition!;
		for (var depth = 0; depth < 3 && !caller.Instructions.Any(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Ldtoken); depth++)
		{
			call = caller.Instructions.First(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Call &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).Name == "AppendFormat");
			caller = module.ResolveMethodToken((int)call.Operand!, caller, call.Offset).Definition!;
		}
		var token = caller.Instructions.Single(instruction => instruction.OpCode == System.Reflection.Emit.OpCodes.Ldtoken);
		var type = module.ResolveTypeToken((int)token.Operand!, caller, token.Offset);
		Assert.Equal("System.ICustomFormatter", type.DisplayName);
		Assert.True(module.IsPinnedStringBuilderFormattingTypeToken(caller, type));
		Assert.False(module.IsPinnedStringBuilderFormattingTypeToken(caller with { ModuleName = "Application" }, type));
		Assert.False(module.IsPinnedStringBuilderFormattingTypeToken(caller with { DisplayName = "System.Text.StringBuilder::Unlisted" }, type));
		Assert.False(module.IsPinnedStringBuilderFormattingTypeToken(caller, new CilType(CilTypeKind.ManagedReference, 4, "System.IFormattable")));
		pack.Replace("packVersion", "10.0.10");
		using var adjacent = new CompilationModule(typeof(CompilerFixtures).Assembly.Location,
			frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
		Assert.False(adjacent.IsPinnedStringBuilderFormattingTypeToken(caller, type));
	}

	[Fact]
	public void ReleasedFormattingResourceIdentifiersAreAudited()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		using var stream = File.OpenRead(pack.AssemblyPath);
		using var pe = new System.Reflection.PortableExecutable.PEReader(stream);
		var reader = System.Reflection.Metadata.PEReaderExtensions.GetMetadataReader(pe);
		var type = reader.TypeDefinitions.Select(reader.GetTypeDefinition).Single(type => reader.GetString(type.Namespace) == "System" && reader.GetString(type.Name) == "ExceptionResource");
		var values = type.GetFields().Select(reader.GetFieldDefinition).Where(field => !field.GetDefaultValue().IsNil)
			.ToDictionary(field => reader.GetString(field.Name), field => reader.GetBlobReader(reader.GetConstant(field.GetDefaultValue()).Value).ReadInt32());
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-formatting-resources.json"), System.Text.Json.JsonSerializer.Serialize(values));
		Assert.Equal(75, values["Format_UnexpectedClosingBrace"]);
		Assert.Equal(76, values["Format_UnclosedFormatItem"]);
		Assert.Equal(77, values["Format_ExpectedAsciiDigit"]);
		foreach (var key in new[] { "Format_UnexpectedClosingBrace", "Format_UnclosedFormatItem", "Format_ExpectedAsciiDigit" })
			Assert.Equal(key, CopperSharp.Runtime.ShadowSystemResources.GetFormattingResourceString(values[key]));
	}

	[Fact]
	public void BuffersAndHelpersRequireExactDefinitionsOwnedCallersAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var format = StringBuilderFrameworkSurface.Members.First(member => member.Name == "AppendFormat" && member.Signature.ParameterTypes.Length == 3);
		var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), format.Name, format.Signature);
		Check(true);
		pack.Replace("packVersion", "10.0.10");
		Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CompilerFixtures).Assembly.Location, frameworkImplementationPack: catalog);
			foreach (var constructor in StringBuilderObjectBufferSurface.Constructors)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructor, null, catalog, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(constructor, null, catalog, null, out _));
				Assert.False(StringBuilderObjectBufferSurface.IsConstructor(new(constructor.DeclaringType, "Unlisted", constructor.Signature)));
				Assert.False(StringBuilderObjectBufferSurface.IsConstructor(new(FrameworkTypeId.Named("Application", constructor.DeclaringType.FullMetadataName!), constructor.Name, constructor.Signature)));
				if (admitted)
				{
					Assert.True(module.TryGetReferenceFreeStructLayout(new CilType(CilTypeKind.ValueType, 4, constructor.DeclaringType.FullMetadataName!), module.AssemblyName, out var layout));
					var count = constructor.Signature.ParameterTypes.Length;
					Assert.Equal(4 * count, layout.Size);
					Assert.Equal((1u << count) - 1, layout.ReferenceBitmap);
				}
			}
			foreach (var helper in StringBuilderObjectBufferSurface.Helpers)
			{
				Assert.Equal(admitted, Admit(helper, caller));
				Assert.False(Admit(helper, null));
				Assert.False(Admit(helper, format));
				Assert.False(Admit(new(helper.DeclaringType, "Unlisted", helper.Signature, helper.MethodTypeArguments), caller));
				Assert.False(Admit(new(helper.DeclaringType, helper.Name, helper.Signature, [helper.MethodTypeArguments[0], FrameworkTypeId.Primitive("System.Int32")]), caller));
			}
			var formatError = StringBuilderExceptionSurface.Members.Single(member => member.Name == "ThrowFormatInvalidString" && member.Signature.ParameterTypes.Length == 2);
			var errorCaller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.ThrowHelper"), formatError.Name, formatError.Signature);
			var resource = new FrameworkMemberId(formatError.DeclaringType, "GetResourceString", new FrameworkMethodSignatureId(0, 0, 1,
				FrameworkTypeId.Primitive("System.String"), [FrameworkTypeId.Named("System.Runtime", "System.ExceptionResource")]));
			Assert.Equal(admitted, Admit(resource, errorCaller));
			Assert.False(Admit(resource, null));
			Assert.False(Admit(resource, caller));
			Assert.False(Admit(resource, formatError));
			Assert.False(Admit(new(resource.DeclaringType, resource.Name, resource.Signature, [FrameworkTypeId.Primitive("System.Int32")]), errorCaller));
			bool Admit(FrameworkMemberId member, FrameworkMemberId? source) => FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, source, out _);
		}
	}

	[Fact]
	public void FixedObjectFormattingMatchesHostAndHasCompatibleReleasedGraph()
	{
		Assert.Equal(42, CompilerFixtures.StringBuilderStableFixedObjectFormattingEntry());
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableFixedObjectFormattingEntry", IncludedExportNames = [],
			ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-fixed-object-formatting.json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	private sealed class StableFixedObjectValue(int index)
	{
		private readonly string _text = new System.Text.StringBuilder(1).Append((char)('A' + index), 130).Append("\0\u03a9\ud800").ToString();
		public string Render() { GC.Collect(); return _text; }
		public override string ToString() => Render();
	}
	private static object CreateStableFixedObjectValue(int index) => new StableFixedObjectValue(index);
	private sealed class StableNullDispatchValue
	{
		public override string ToString() => throw new InvalidOperationException("Null dispatch entered an override.");
	}
	private static string CallStableNullDispatch(StableNullDispatchValue value) => value.ToString();
	private sealed class StableFixedObjectFormatter : IFormatProvider, ICustomFormatter
	{
		public int Queries, Formats;
		public object GetFormat(Type? type) { GC.Collect(); Queries++; return this; }
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			GC.Collect(); Formats++;
			return ((StableFixedObjectValue)value!).ToString();
		}
	}
	public static int StringBuilderStableFixedObjectFormattingEntry()
	{
		var builder = new System.Text.StringBuilder(1);
		var provider = new StableFixedObjectFormatter();
		for (var mode = 0; mode < 6; mode++)
		{
			builder.Clear();
			if (mode == 0) builder.AppendFormat("{0}|{1}", CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1));
			else if (mode == 1) builder.AppendFormat(null, "{0}|{1}", CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1));
			else if (mode == 2) builder.AppendFormat("{0}|{1}|{2}", CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1), CreateStableFixedObjectValue(2));
			else if (mode == 3) builder.AppendFormat(null, "{0}|{1}|{2}", CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1), CreateStableFixedObjectValue(2));
			else if (mode == 4) builder.AppendFormat(provider, "{0}|{1}", CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1));
			else builder.AppendFormat(provider, "{0}|{1}|{2}", CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1), CreateStableFixedObjectValue(2));
			GC.Collect();
			var snapshot = builder.ToString();
			var count = mode < 2 || mode == 4 ? 2 : 3;
			if (snapshot.Length != 134 * count - 1) return 1;
			for (var index = 0; index < count; index++)
			{
				for (var character = 0; character < 130; character++) if (snapshot[134 * index + character] != 'A' + index) return 2;
				if (snapshot[134 * index + 130] != '\0' || snapshot[134 * index + 131] != '\u03a9' || snapshot[134 * index + 132] != '\ud800') return 3;
				if (index + 1 < count && snapshot[134 * index + 133] != '|') return 4;
			}
			builder.Clear().Append('x'); GC.Collect();
			if (snapshot[0] != 'A' || builder.ToString() != "x") return 5;
		}
		if (provider.Queries != 2 || provider.Formats != 5) return 9;
		builder.Clear().Append("seed");
		var failures = 0;
		foreach (var format in new[] { "}", "{0", "{x}", "{2}" })
		{
			try { builder.AppendFormat(format, CreateStableFixedObjectValue(0), CreateStableFixedObjectValue(1)); return 6; }
			catch (FormatException) { GC.Collect(); failures++; }
			if (builder.ToString() != "seed") return 7;
		}
		builder.Append('X'); GC.Collect();
		try { provider.Format(null, null, provider); return 10; }
		catch (NullReferenceException) { GC.Collect(); }
		try { CallStableNullDispatch(null!); return 11; }
		catch (NullReferenceException) { GC.Collect(); }
		return failures == 4 && builder.ToString() == "seedX" ? 42 : 8;
	}
}
