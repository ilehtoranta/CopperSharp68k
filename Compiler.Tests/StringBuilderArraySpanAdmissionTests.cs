/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderArraySpanAdmissionTests
{
	[Fact]
	public void PublicArraySpansRequireExactCharacterSignaturesAndReleasedInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		var element = FrameworkTypeId.GenericMethodParameter(0);
		var character = FrameworkTypeId.Primitive("System.Char");
		var integer = FrameworkTypeId.Primitive("System.Int32");
		var span = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.Span`1"), [element]);
		foreach (var assembly in new[] { "System.Runtime", "System.Memory", "System.Private.CoreLib" })
		foreach (var count in new[] { 1, 2, 3 })
		{
			var owner = FrameworkTypeId.Named(assembly, "System.MemoryExtensions");
			var signature = new FrameworkMethodSignatureId(0x10, 1, count, span,
				[FrameworkTypeId.SzArray(element), .. Enumerable.Repeat(integer, count - 1)]);
			var member = new FrameworkMemberId(owner, "AsSpan", signature, [character]);
			Assert.True(Admit(member, out var binding));
			Assert.Equal(FrameworkBindingKind.Intrinsic, binding.Kind);
			Assert.Equal(count == 1 ? "intrinsic:span-from-array:char" : count == 2 ? "intrinsic:span-from-array-start:char" : "intrinsic:span-from-array-range-value:char", binding.Target);
			Assert.Equal(count == 1 ? FrameworkEffects.None : FrameworkEffects.MayThrow, binding.EffectSummary.Effects);
			Assert.True(FrameworkImplementationProfile.IsTargetRuntimeOverride(binding));
			Assert.False(Admit(new(owner, "AsMemory", signature, [character]), out _));
			Assert.False(Admit(new(FrameworkTypeId.Named("Application", "System.MemoryExtensions"), "AsSpan", signature, [character]), out _));
			Assert.False(Admit(new(owner, "AsSpan", signature, [integer]), out _));
			Assert.False(Admit(new(owner, "AsSpan", signature), out _));
			Assert.False(Admit(new(owner, "AsSpan", new FrameworkMethodSignatureId(0x30, 1, count, span, signature.ParameterTypes), [character]), out _));
			Assert.False(Admit(new(owner, "AsSpan", new FrameworkMethodSignatureId(0x10, 1, count, integer, signature.ParameterTypes), [character]), out _));
			Assert.False(Admit(new(owner, "AsSpan", new FrameworkMethodSignatureId(0x10, 1, count, span, [integer]), [character]), out _));
		}
		pack.Replace("packVersion", "10.0.10");
		catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
		Assert.False(Admit(new(FrameworkTypeId.Named("System.Memory", "System.MemoryExtensions"), "AsSpan",
			new FrameworkMethodSignatureId(0x10, 1, 1, span, [FrameworkTypeId.SzArray(element)]), [character]), out _));

		bool Admit(FrameworkMemberId member, out FrameworkBinding binding) =>
			FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out binding);
	}

	[Fact]
	public void ArraySpanFixtureMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderStableArraySpanEntry());
}

public static partial class CompilerFixtures
{
	public static int StringBuilderStableArraySpanEntry()
	{
		var full = MakeBuilderSpanArray().AsSpan();
		var tail = MakeBuilderSpanArray().AsSpan(2);
		var middle = MakeBuilderSpanArray().AsSpan(1, 2);
		GC.Collect();
		full[0] = 'Z';
		var builder = new System.Text.StringBuilder(1).Append(full).Append(tail).Append(middle);
		GC.Collect();
		var destination = new char[8].AsSpan();
		GC.Collect();
		builder.CopyTo(0, destination, 8);
		GC.Collect();
		if (builder.ToString() != "Z\0\u03a9\ud800\u03a9\ud800\0\u03a9" || destination[0] != 'Z' || destination[7] != '\u03a9') return 1;
		char[]? absent = null;
		if (absent.AsSpan().Length != 0 || absent.AsSpan(0).Length != 0 || absent.AsSpan(0, 0).Length != 0 ||
			MakeBuilderSpanArray().AsSpan(4).Length != 0 || MakeBuilderSpanArray().AsSpan(4, 0).Length != 0) return 2;
		var failures = 0;
		try { _ = absent.AsSpan(1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = absent.AsSpan(0, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsSpan(-1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsSpan(5); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsSpan(-1, 1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsSpan(0, -1); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsSpan(3, 2); } catch (ArgumentOutOfRangeException) { failures++; }
		try { _ = MakeBuilderSpanArray().AsSpan(int.MaxValue, int.MaxValue); } catch (ArgumentOutOfRangeException) { failures++; }
		GC.Collect();
		return failures == 8 && full[0] == 'Z' && full[3] == '\ud800' && destination[5] == '\ud800' ? 42 : 3;
	}

	private static char[] MakeBuilderSpanArray() => ['A', '\0', '\u03a9', '\ud800'];
}
