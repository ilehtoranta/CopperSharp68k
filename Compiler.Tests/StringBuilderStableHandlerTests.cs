/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;
using System.Reflection.Emit;
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderStableHandlerTests
{
	[Fact]
	public void CustomIntegerStringCopyRequiresTheExactRuntimeCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(typeof(CopperSharp.Runtime.ShadowCustomNumberFormatting).Assembly.Location,
				frameworkImplementationPack: catalog);
			var caller = module.ResolveEntryPoint("CopperSharp.Runtime.ShadowCustomNumberFormatting::FormatMagnitude");
			var call = Assert.Single(caller.Instructions.Where(instruction => instruction.OpCode == OpCodes.Newobj &&
				module.DescribeFrameworkMethodToken((int)instruction.Operand!, caller, instruction.Offset).DeclaringType.FullMetadataName == "System.String"));
			var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
			var owner = module.DescribeFrameworkMethodToken(System.Reflection.Metadata.Ecma335.MetadataTokens.GetToken(caller.Handle), caller, -1);
			Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, owner, out var binding));
			if (admitted)
			{
				Assert.Equal("shadow:CopperSharp.Runtime.Managed:CopperSharp.Runtime.ShadowStringData::FromCharacters", binding.Target);
				Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayCollect));
			}
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog,
				new FrameworkMemberId(owner.DeclaringType, "Unlisted", owner.Signature), out _));
			Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(
				new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature), catalog, owner, out _));
		}
	}

	[Fact]
	public void BoxedIntegralDispatchRequiresReleasedInputAndExactPrimitiveStorage()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			foreach (var (name, kind, size) in new[] {
				("sbyte", CilTypeKind.SignedInteger, 1), ("byte", CilTypeKind.UnsignedInteger, 1),
				("short", CilTypeKind.SignedInteger, 2), ("ushort", CilTypeKind.UnsignedInteger, 2),
				("int", CilTypeKind.SignedInteger, 4), ("uint", CilTypeKind.UnsignedInteger, 4),
				("long", CilTypeKind.SignedInteger, 8), ("ulong", CilTypeKind.UnsignedInteger, 8) })
			{
				var type = new CilType(kind, size, name);
				Assert.Equal(admitted, module.RegisterBoxedDispatchLayout(type, "System.Private.CoreLib") is not null);
				Assert.Null(module.RegisterBoxedDispatchLayout(type with { Size = size == 8 ? 4 : 8 }, "System.Private.CoreLib"));
				Assert.Null(module.RegisterBoxedDispatchLayout(type with { Kind = CilTypeKind.FloatingPoint }, "System.Private.CoreLib"));
				Assert.Null(module.RegisterBoxedDispatchLayout(type with { DisplayName = "Application.Integer" }, "System.Private.CoreLib"));
			}
		}
	}

	[Fact]
	public void HandlerReflectionTokensRequireTheirExactMetadataCaller()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			var owner = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition)
				.Single(type => module.Reader.GetString(type.Name) == "DefaultInterpolatedStringHandler");
			foreach (var name in new[] { "HasCustomFormatter", "AppendCustomFormatter" })
			{
				var caller = owner.GetMethods().Select(module.GetMethod).Single(method => method.Name == name);
				Assert.Equal(admitted, module.IsPinnedStringBuilderFormattingTypeToken(caller, new CilType(CilTypeKind.ManagedReference, 4, "System.ICustomFormatter")));
				Assert.Equal(admitted && name == "HasCustomFormatter", module.IsPinnedStringBuilderFormattingTypeToken(caller, new CilType(CilTypeKind.ManagedReference, 4, "System.Globalization.CultureInfo")));
				Assert.False(module.IsPinnedStringBuilderFormattingTypeToken(caller, new CilType(CilTypeKind.ManagedReference, 4, "System.String")));
			}
			var other = owner.GetMethods().Select(module.GetMethod).Single(method => method.Name == "Clear");
			Assert.False(module.IsPinnedStringBuilderFormattingTypeToken(other, new CilType(CilTypeKind.ManagedReference, 4, "System.ICustomFormatter")));
		}
	}

	[Fact]
	public void HandlerStorageMembersRequireReleasedInputAndOwnedCallers()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			using var module = new CompilationModule(pack.AssemblyPath, frameworkImplementationPack: catalog);
			var definition = StringBuilderFrameworkSurface.Helpers.First(member => member.Name == "AppendSpanFormattable");
			var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), definition.Name, definition.Signature);
			foreach (var member in StringBuilderInterpolatedHandlerSurface.Members)
			{
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog,
					new FrameworkMemberId(caller.DeclaringType, "Unlisted", caller.Signature), out _));
			}
		}
	}

	[Fact]
	public void HandlerFallbackRequiresAnExactConstrainedReferenceCall()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true); pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			using var module = new CompilationModule(pack.AssemblyPath,
				frameworkImplementationPack: FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!);
			foreach (var owner in new[] { "AppendInterpolatedStringHandler", "DefaultInterpolatedStringHandler" })
			{
				var type = module.Reader.TypeDefinitions.Select(module.Reader.GetTypeDefinition).Single(type => module.Reader.GetString(type.Name) == owner);
				var body = type.GetMethods().Select(module.GetMethod).Single(method => method.Name == "AppendFormatted" &&
					method.Signature.GenericParameterCount == 1 && method.Signature.ParameterTypes is [_, { DisplayName: "string" }]);
				var caller = body with { MethodTypeArguments = [new CilType(CilTypeKind.ManagedReference, 4, "object")] };
				var call = Assert.Single(body.Instructions.Where(instruction => instruction.ConstrainedTypeToken is not null &&
					module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset).Name == "ToString"));
				var member = module.DescribeFrameworkMethodToken((int)call.Operand!, caller, call.Offset);
				Assert.Equal(admitted, module.TryCreatePinnedHandlerToStringBinding(member, caller, call.Offset, out _));
				Assert.False(module.TryCreatePinnedHandlerToStringBinding(member, null, call.Offset, out _));
				Assert.False(module.TryCreatePinnedHandlerToStringBinding(member, caller, -1, out _));
				Assert.False(module.TryCreatePinnedHandlerToStringBinding(member, caller with { MethodTypeArguments = [new CilType(CilTypeKind.SignedInteger, 4, "int")] }, call.Offset, out _));
			}
		}
	}

	[Fact]
	public void StableHandlersMatchHostAndCloseTheFormattingGraph()
	{
		Assert.Equal(42, CompilerFixtures.StringBuilderStableHandlersEntry());
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var result = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableHandlersEntry",
			IncludedExportNames = [], ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-handlers-analysis.json"), System.Text.Json.JsonSerializer.Serialize(result));
		Assert.True(result.IsCompatible, string.Join("\n", result.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported).Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	private sealed class StableHandlerObject
	{
		public readonly string Text = new StringBuilder(1).Append('x', 300).Append("\0\u03a9\ud800").ToString();
		public bool Throw;
		public override string ToString() { GC.Collect(); if (Throw) throw new InvalidOperationException("handler"); return Text; }
	}
	private sealed class StableHandlerProvider : IFormatProvider, ICustomFormatter
	{
		public int Queries, Calls;
		public bool Throw, ReturnNull;
		public object? GetFormat(Type? type) { Queries++; GC.Collect(); return this; }
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			Calls++; GC.Collect();
			if (Throw) throw new InvalidOperationException("handler");
			return ReturnNull ? null! : "C\0\u03a9\ud800";
		}
	}
	public static int StringBuilderStableHandlersEntry()
	{
		for (var capacity = 0; capacity < 2; capacity++)
		{
			var builder = new StringBuilder(capacity == 0 ? 1 : 512);
			var value = new StableHandlerObject();
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
			handler.AppendFormatted((object)value, 310, null);
			var snapshot = builder.ToString(); GC.Collect(); builder.Clear();
			if (snapshot.Length != 310) return 1;
			for (var i = 0; i < 310; i++) if (snapshot[i] != (i < 7 ? ' ' : value.Text[i - 7])) return 2;
			handler.AppendFormatted(42, 6, "D4");
			if (builder.Length != 6) return 100 + builder.Length;
			for (var i = 0; i < 6; i++) if (builder[i] != "  0042"[i]) return 1000 + i * 256 + builder[i];
			builder.Clear(); object boxed = 42; GC.Collect(); handler.AppendFormatted(boxed, 6, "D4");
			if (builder.ToString() != "  0042") return 13;
			builder.Clear(); handler.AppendFormatted<object>(null!, 3, null);
			if (builder.ToString() != "   ") return 4;
			builder.Clear().Append("seed"); value.Throw = true;
			try { handler.AppendFormatted((object)value, 310, null); return 5; }
			catch (InvalidOperationException) { GC.Collect(); }
			if (builder.ToString() != "seed") return 6;
			value.Throw = false; builder.Clear(); handler.AppendFormatted((object)value, -310, null);
			if (builder.Length != 310) return 7;
			for (var i = 0; i < 310; i++) if (builder[i] != (i < 303 ? value.Text[i] : ' ')) return 14;
			var provider = new StableHandlerProvider(); builder.Clear();
			handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			handler.AppendFormatted((object)value, 6, "W"); GC.Collect();
			if (builder.ToString() != "  C\0\u03a9\ud800" || provider.Calls != 1 || provider.Queries != 3) return 8;
			provider.ReturnNull = true; builder.Clear(); handler.AppendFormatted((object)value, 6, "W");
			if (builder.ToString() != "      " || provider.Calls != 2) return 9;
			provider.Throw = true; builder.Clear().Append("seed");
			try { handler.AppendFormatted((object)value, 6, "W"); return 10; }
			catch (InvalidOperationException) { GC.Collect(); }
			if (builder.ToString() != "seed") return 11;
			provider.Throw = provider.ReturnNull = false; builder.Clear(); handler.AppendFormatted((object)value, 0, "W");
			if (builder.ToString() != "C\0\u03a9\ud800") return 12;
		}
		return 42;
	}
}
