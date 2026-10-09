/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Compiler.Framework;

namespace CopperSharp.Compiler.Tests;

public sealed class StringBuilderReferenceSpanAdmissionTests
{
	[Fact]
	public void ReferenceSpanConstructorsRequireExactElementsSignaturesAndInput()
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		Check(true);
		pack.Replace("packVersion", "10.0.10"); Check(false);
		void Check(bool admitted)
		{
			var catalog = FrameworkImplementationPackLoader.Load(new M68kFrameworkImplementationPackOptions(pack.ManifestPath))!;
			foreach (var element in new[] { "System.String", "System.Object" })
			foreach (var range in new[] { false, true })
			{
				var owner = FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("System.Runtime", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive(element)]);
				var array = FrameworkTypeId.SzArray(FrameworkTypeId.GenericTypeParameter(0));
				var integer = FrameworkTypeId.Primitive("System.Int32");
				var signature = new FrameworkMethodSignatureId(0x20, 0, range ? 3 : 1, FrameworkTypeId.Primitive("System.Void"), range ? [array, integer, integer] : [array]);
				var member = new FrameworkMemberId(owner, ".ctor", signature);
				Assert.Equal(admitted, Admit(member));
				Assert.False(Admit(new(owner, "Unlisted", signature)));
				Assert.False(Admit(new(owner, ".ctor", signature, [integer])));
				Assert.False(Admit(new(FrameworkTypeId.GenericInstantiation(owner.ElementType!, [integer]), ".ctor", signature)));
				Assert.False(Admit(new(FrameworkTypeId.GenericInstantiation(FrameworkTypeId.Named("Application", "System.ReadOnlySpan`1"), [FrameworkTypeId.Primitive(element)]), ".ctor", signature)));
			}
			bool Admit(FrameworkMemberId member) => FrameworkImplementationProfile.TryCreatePinnedStringBuilderDependencyBinding(member, catalog, null, out _);
		}
	}

	[Fact]
	public void ReferenceSpanFixtureMatchesCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderStableReferenceSpanEntry());
}

public static partial class CompilerFixtures
{
	private static string[] MakeStableReferenceSpanStrings() => [new System.Text.StringBuilder(1).Append("A\0Ω\ud800").ToString(), "", "Z"];
	private static ReadOnlySpan<object> MakeStableReferenceObjectSpan()
	{
		var values = new object[2];
		values[0] = new System.Text.StringBuilder(1).Append("O\0Ω").ToString();
		values[1] = new System.Text.StringBuilder(1).Append('B');
		return new ReadOnlySpan<object>(values);
	}
	public static int StringBuilderStableReferenceSpanEntry()
	{
		var full = new ReadOnlySpan<string>(MakeStableReferenceSpanStrings());
		var tail = new ReadOnlySpan<string>(MakeStableReferenceSpanStrings(), 1, 2);
		var covariant = new ReadOnlySpan<object>(MakeStableReferenceSpanStrings(), 0, 1);
		var objects = MakeStableReferenceObjectSpan();
		GC.Collect();
		if (full.Length != 3 || tail.Length != 2 || (string)covariant[0] != "A\0Ω\ud800" || (string)objects[0] != "O\0Ω") return 1;
		if (((System.Text.StringBuilder)objects[1]).Length != 1) return 2;
		var builder = new System.Text.StringBuilder(1).AppendJoin('|', full).AppendJoin("::", tail);
		GC.Collect();
		if (builder.ToString() != "A\0Ω\ud800||Z::Z" || full[0] != "A\0Ω\ud800") return 3;
		if (new ReadOnlySpan<object>((object[]?)null).Length != 0 || new ReadOnlySpan<string>((string[]?)null, 0, 0).Length != 0) return 4;
		var failures = 0;
		try { if (new ReadOnlySpan<string>((string[]?)null, 1, 0).Length >= 0) return 7; } catch (ArgumentOutOfRangeException) { failures++; }
		try { if (new ReadOnlySpan<object>(new object[0], 0, 1).Length >= 0) return 8; } catch (ArgumentOutOfRangeException) { failures++; }
		try { if (new ReadOnlySpan<string>(new string[0], -1, 0).Length >= 0) return 9; } catch (ArgumentOutOfRangeException) { failures++; }
		object[] covariantArray = MakeStableReferenceSpanStrings();
		try { covariantArray[0] = new System.Text.StringBuilder(1); return 10; }
		catch (ArrayTypeMismatchException) { failures++; }
		GC.Collect();
		if (failures != 4) return 100 + failures;
		if (full[0] != "A\0Ω\ud800") return 6;
		return (string)objects[0] == "O\0Ω" ? 42 : 5;
	}
}
