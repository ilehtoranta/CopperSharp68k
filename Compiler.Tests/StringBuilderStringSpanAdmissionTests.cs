/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;
using CopperSharp.Compiler.Metadata;
using System.Reflection.Emit;
using System.Reflection.Metadata.Ecma335;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderStringSpanAdmissionTests
{
	[Fact]
	public void StringSpanProjectionsRequireExactPinnedDefinitionsAndBoundTheirDependencies()
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
				module.Reader.GetString(type.Namespace) == "System" && module.Reader.GetString(type.Name) == "MemoryExtensions");
			var constructor = StringBuilderFrameworkSurface.Members.First(member => member.Name == ".ctor" && member.Signature.ParameterTypes.Length == 0);
			var caller = new FrameworkMemberId(FrameworkTypeId.Named("System.Private.CoreLib", "System.Text.StringBuilder"), constructor.Name, constructor.Signature);
			var count = 0;
			foreach (var handle in type.GetMethods().Where(handle => module.Reader.GetString(module.Reader.GetMethodDefinition(handle).Name) == "AsSpan"))
			{
				var body = module.GetMethod(handle);
				var member = module.DescribeFrameworkMethodToken(MetadataTokens.GetToken(handle), body, 0);
				if (!FrameworkImplementationProfile.IsStringSpanProjection(member)) continue;
				count++;
				File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, $"string-span-projection-{body.Signature.ParameterTypes.Length}.txt"),
					body.Instructions.Select(instruction => $"{instruction.Offset:X4}: {instruction.OpCode} {instruction.Operand}"));
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, caller, out var binding));
				if (admitted) Assert.True(binding.EffectSummary.Effects.HasFlag(FrameworkEffects.MayThrow | FrameworkEffects.MayCollect | FrameworkEffects.ReadsManagedMemory));
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(member, null, catalog, null, out _));
				var canonical = FrameworkImplementationProfile.Canonicalize(member);
				var facade = new FrameworkMemberId(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions"), member.Name, canonical.Signature);
				Assert.Equal(admitted, FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(facade,
					new FrameworkBinding(facade, FrameworkBindingKind.Intrinsic, "intrinsic:span-from-string", FrameworkEffectSummary.None), catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(
					FrameworkTypeId.Named("Application", "System.MemoryExtensions"), member.Name, canonical.Signature), null, catalog, null, out _));
				Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(new FrameworkMemberId(member.DeclaringType, member.Name, member.Signature,
					[FrameworkTypeId.Primitive("System.Char")]), null, catalog, caller, out _));
				foreach (var instruction in body.Instructions.Where(instruction => instruction.OpCode == OpCodes.Call || instruction.OpCode == OpCodes.Newobj))
				{
					var dependency = module.DescribeFrameworkMethodToken((int)instruction.Operand!, body, instruction.Offset);
					if (dependency.Name is not ("GetRawStringData" or "Add" or ".ctor" or "ThrowArgumentOutOfRangeException")) continue;
					var accepted = FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(dependency, catalog, member, out _) ||
						FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(dependency, null, catalog, member, out _);
					Assert.Equal(admitted, accepted);
					var unlisted = new FrameworkMemberId(member.DeclaringType, "Unlisted", member.Signature);
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(dependency, catalog, unlisted, out _));
					Assert.False(FrameworkImplementationProfile.TryCreatePinnedStringBuilderBinding(dependency, null, catalog, unlisted, out _));
				}
			}
			Assert.Equal(3, count);
		}
	}

	[Fact]
	public void ApplicationStringSpansMatchCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderStableApplicationStringSpanEntry());

	[Fact]
	public void StringSeededConstructorsHaveACompatibleStableGraph()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var analysis = M68kCompiler.AnalyzeFramework(new M68kCompilationRequest {
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::StringBuilderStableStringConstructorEntry", IncludedExportNames = [],
			ExceptionMode = M68kExceptionMode.Full, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
			FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
		});
		File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "stringbuilder-stable-string-constructor-analysis.json"),
			System.Text.Json.JsonSerializer.Serialize(analysis, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		Assert.True(analysis.IsCompatible, string.Join("\n", analysis.Members.Where(member => member.Status == M68kFrameworkCompatibilityStatus.Unsupported)
			.Select(member => member.Member.DisplayName + ": " + member.Reason)));
	}
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableApplicationStringSpanEntry()
	{
		var full = MakeBuilderSpanString().AsSpan();
		var tail = MakeBuilderSpanString().AsSpan(2);
		var middle = MakeBuilderSpanString().AsSpan(1, 2);
		GC.Collect();
		var builder = new System.Text.StringBuilder(1).Append(full).Append(tail).Insert(1, middle);
		GC.Collect();
		if (builder.ToString() != "A\0\u03a9\0\u03a9\ud800\u03a9\ud800" || full[3] != '\ud800' || tail[0] != '\u03a9') return 1;
		string? absent = null;
		if (absent.AsSpan().Length != 0 || absent.AsSpan(0).Length != 0 || absent.AsSpan(0, 0).Length != 0 ||
			MakeBuilderSpanString().AsSpan(4).Length != 0 || MakeBuilderSpanString().AsSpan(4, 0).Length != 0) return 2;
		var failures = 0;
		try { _ = absent.AsSpan(1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = absent.AsSpan(0, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanString().AsSpan(-1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanString().AsSpan(5); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanString().AsSpan(-1, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanString().AsSpan(0, -1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanString().AsSpan(3, 2); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanString().AsSpan(int.MaxValue, int.MaxValue); } catch (ArgumentOutOfRangeException) { failures++; }
		GC.Collect();
		return failures == 8 && middle[0] == '\0' && full[2] == '\u03a9' && tail[1] == '\ud800' ? 42 : 3;
	}

	private static string MakeBuilderSpanString() => new System.Text.StringBuilder("A\0\u03a9\ud800").ToString();

	public static int StringBuilderStableStringConstructorEntry()
	{
		const string text = "A\0\u03a9\ud83d\ude00\ud800\uffffZ";
		var whole = new System.Text.StringBuilder(text);
		var reserved = new System.Text.StringBuilder(text, 32);
		var slice = new System.Text.StringBuilder(text, 1, 6, 1);
		GC.Collect();
		if (whole.ToString() != text || reserved.ToString() != text || reserved.Capacity != 32 || slice.ToString() != "\0\u03a9\ud83d\ude00\ud800\uffff") return 1;
		if (new System.Text.StringBuilder((string?)null).Length != 0 || new System.Text.StringBuilder(null, 0, 0, 0).Length != 0) return 2;
		var failures = 0;
		try { _ = new System.Text.StringBuilder(text, -1, 1, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = new System.Text.StringBuilder(text, 0, 9, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = new System.Text.StringBuilder(text, 0, 1, -1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = new System.Text.StringBuilder(null, 1, 0, 0); } catch (ArgumentOutOfRangeException) { failures++; }
		slice.Append('!'); whole.Clear().Append("tail"); GC.Collect();
		return failures == 4 && whole.ToString() == "tail" && slice.ToString() == "\0\u03a9\ud83d\ude00\ud800\uffff!" && reserved.ToString() == text ? 42 : 3;
	}
}
