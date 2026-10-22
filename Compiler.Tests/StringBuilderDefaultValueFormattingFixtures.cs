/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private struct DefaultEmptyNameValue { }
	private ref struct DefaultRefLikeNameValue(int tag) { public int Tag = tag; }
	public static System.Type DefaultRefLikeTypeToken() => typeof(DefaultRefLikeNameValue);
	private struct DefaultWordNameValue { public int Tag; }
	private struct DefaultWideNameValue { public int First, Second; }
	private struct DefaultReferenceNameValue { public string Text; }
	private struct DefaultNestedNameValue { public int Tag; public ReferenceFormattingPayload<string> Payload; }
	private struct DefaultGenericNameValue<T> { public int Tag; public T Value; }
	private class DefaultGenericNameOwner<T>
	{
		public struct Nested<U> { public T First; public U Second; }
	}

	private static void AppendDefaultNameValue(StringBuilder builder, CompositeFormat format, object box, int kind, int form, int alignment)
	{
		if (form < 2)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			if (form == 1) handler.AppendFormatted(box, alignment, "ignored");
			else if (kind == 0) handler.AppendFormatted((DefaultEmptyNameValue)box, alignment, "ignored");
			else if (kind == 1) handler.AppendFormatted((DefaultWordNameValue)box, alignment, "ignored");
			else if (kind == 2) handler.AppendFormatted((DefaultWideNameValue)box, alignment, "ignored");
			else if (kind == 3) handler.AppendFormatted((DefaultReferenceNameValue)box, alignment, "ignored");
			else if (kind == 4) handler.AppendFormatted((DefaultNestedNameValue)box, alignment, "ignored");
			else if (kind == 5) handler.AppendFormatted((DefaultGenericNameValue<int>)box, alignment, "ignored");
			else if (kind == 6) handler.AppendFormatted((DefaultGenericNameValue<string>)box, alignment, "ignored");
			else if (kind == 7) handler.AppendFormatted((DefaultGenericNameOwner<int>.Nested<string>)box, alignment, "ignored");
			else if (kind == 8) handler.AppendFormatted((DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>)box, alignment, "ignored");
			else handler.AppendFormatted((DefaultGenericNameValue<int[][]>)box, alignment, "ignored");
			handler.AppendLiteral("]"); builder.Append(ref handler); return;
		}
		if (form is >= 2 and <= 4)
		{
			if (kind == 0)
			{
				if (form == 2) builder.AppendFormat<DefaultEmptyNameValue>(null, format, (DefaultEmptyNameValue)box);
				else if (form == 3) builder.AppendFormat<DefaultEmptyNameValue, int>(null, format, (DefaultEmptyNameValue)box, 9);
				else builder.AppendFormat<DefaultEmptyNameValue, int, string>(null, format, (DefaultEmptyNameValue)box, 9, "extra");
			}
			else if (kind == 1)
			{
				if (form == 2) builder.AppendFormat<DefaultWordNameValue>(null, format, (DefaultWordNameValue)box);
				else if (form == 3) builder.AppendFormat<DefaultWordNameValue, int>(null, format, (DefaultWordNameValue)box, 9);
				else builder.AppendFormat<DefaultWordNameValue, int, string>(null, format, (DefaultWordNameValue)box, 9, "extra");
			}
			else if (kind == 2)
			{
				if (form == 2) builder.AppendFormat<DefaultWideNameValue>(null, format, (DefaultWideNameValue)box);
				else if (form == 3) builder.AppendFormat<DefaultWideNameValue, int>(null, format, (DefaultWideNameValue)box, 9);
				else builder.AppendFormat<DefaultWideNameValue, int, string>(null, format, (DefaultWideNameValue)box, 9, "extra");
			}
			else if (kind == 3)
			{
				if (form == 2) builder.AppendFormat<DefaultReferenceNameValue>(null, format, (DefaultReferenceNameValue)box);
				else if (form == 3) builder.AppendFormat<DefaultReferenceNameValue, int>(null, format, (DefaultReferenceNameValue)box, 9);
				else builder.AppendFormat<DefaultReferenceNameValue, int, string>(null, format, (DefaultReferenceNameValue)box, 9, "extra");
			}
			else if (kind == 4)
			{
				if (form == 2) builder.AppendFormat<DefaultNestedNameValue>(null, format, (DefaultNestedNameValue)box);
				else if (form == 3) builder.AppendFormat<DefaultNestedNameValue, int>(null, format, (DefaultNestedNameValue)box, 9);
				else builder.AppendFormat<DefaultNestedNameValue, int, string>(null, format, (DefaultNestedNameValue)box, 9, "extra");
			}
			else if (kind == 5)
			{
				if (form == 2) builder.AppendFormat<DefaultGenericNameValue<int>>(null, format, (DefaultGenericNameValue<int>)box);
				else if (form == 3) builder.AppendFormat<DefaultGenericNameValue<int>, int>(null, format, (DefaultGenericNameValue<int>)box, 9);
				else builder.AppendFormat<DefaultGenericNameValue<int>, int, string>(null, format, (DefaultGenericNameValue<int>)box, 9, "extra");
			}
			else if (kind == 6)
			{
				if (form == 2) builder.AppendFormat<DefaultGenericNameValue<string>>(null, format, (DefaultGenericNameValue<string>)box);
				else if (form == 3) builder.AppendFormat<DefaultGenericNameValue<string>, int>(null, format, (DefaultGenericNameValue<string>)box, 9);
				else builder.AppendFormat<DefaultGenericNameValue<string>, int, string>(null, format, (DefaultGenericNameValue<string>)box, 9, "extra");
			}
			else if (kind == 7)
			{
				if (form == 2) builder.AppendFormat<DefaultGenericNameOwner<int>.Nested<string>>(null, format, (DefaultGenericNameOwner<int>.Nested<string>)box);
				else if (form == 3) builder.AppendFormat<DefaultGenericNameOwner<int>.Nested<string>, int>(null, format, (DefaultGenericNameOwner<int>.Nested<string>)box, 9);
				else builder.AppendFormat<DefaultGenericNameOwner<int>.Nested<string>, int, string>(null, format, (DefaultGenericNameOwner<int>.Nested<string>)box, 9, "extra");
			}
			else if (kind == 8)
			{
				if (form == 2) builder.AppendFormat<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>>(null, format, (DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>)box);
				else if (form == 3) builder.AppendFormat<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>, int>(null, format, (DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>)box, 9);
				else builder.AppendFormat<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>, int, string>(null, format, (DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>)box, 9, "extra");
			}
			else
			{
				if (form == 2) builder.AppendFormat<DefaultGenericNameValue<int[][]>>(null, format, (DefaultGenericNameValue<int[][]>)box);
				else if (form == 3) builder.AppendFormat<DefaultGenericNameValue<int[][]>, int>(null, format, (DefaultGenericNameValue<int[][]>)box, 9);
				else builder.AppendFormat<DefaultGenericNameValue<int[][]>, int, string>(null, format, (DefaultGenericNameValue<int[][]>)box, 9, "extra");
			}
		}
		else
		{
			var args = new object?[] { box, 9, "extra" };
			if (form == 5) builder.AppendFormat(null, format, args);
			else builder.AppendFormat(null, format, new ReadOnlySpan<object?>(args));
		}
	}

	private static string DefaultNameThroughTypeParameter(System.Type type)
	{
		System.GC.Collect();
		return type.ToString();
	}

	public static int CoreLibDefaultRuntimeTypeNamesEntry()
	{
		var type = typeof(DefaultGenericNameValue<int[][]>);
		System.GC.Collect();
		if (DefaultNameThroughTypeParameter(type) !=
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[System.Int32[][]]") return 1;
		if (typeof(int).ToString() != "System.Int32" || typeof(string[]).ToString() != "System.String[]") return 2;
		if (typeof(DefaultGenericNameValue<>).ToString() !=
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[T]") return 3;
		return 42;
	}

	public static int CoreLibStringBuilderDefaultTypedValueEntry()
	{
		var value = new DefaultReferenceNameValue { Text = new string("R\u03A9\0\uD800".AsSpan()) };
		var builder = new StringBuilder(180).Append("seed");
		System.GC.Collect();
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
		handler.AppendFormatted(value, 0, "ignored");
		System.GC.Collect();
		if (!ReferenceFormattingTextIsOriginal(value.Text, null)) return 3;
		value = default;
		System.GC.Collect();
		if (builder.ToString() != "seedCopperSharp.Compiler.Tests.CompilerFixtures+DefaultReferenceNameValue") return 1;
		if (new DefaultEmptyNameValue().ToString() != "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultEmptyNameValue") return 2;
		return 42;
	}

	public static int CoreLibStringBuilderDefaultBoxFailureEntry()
	{
		var value = new DefaultReferenceNameValue { Text = new string("R\u03A9\0\uD800".AsSpan()) };
		var builder = new StringBuilder(180).Append("seed");
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
		SetStringBuilderAllocationFailure(1);
		try { handler.AppendFormatted(value, 0, "ignored"); SetStringBuilderAllocationFailure(0); return 1; }
		catch (System.OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (builder.ToString() != "seed" || !ReferenceFormattingTextIsOriginal(value.Text, null)) return 2;
		handler.AppendFormatted(value, 0, "ignored");
		if (builder.ToString() != "seedCopperSharp.Compiler.Tests.CompilerFixtures+DefaultReferenceNameValue") return 3;
		return 42;
	}

	public static int CoreLibStringBuilderDefaultValueNamesEntry()
	{
		var text = new string("R\u03A9\0\uD800".AsSpan());
		var tail = CreateReferenceFormattingTail();
		var boxes = new object[]
		{
			new DefaultEmptyNameValue(), new DefaultWordNameValue { Tag = 31 }, new DefaultWideNameValue { First = 31, Second = -7 },
			new DefaultReferenceNameValue { Text = text }, new DefaultNestedNameValue { Tag = 31, Payload = new() { Text = text, Tail = tail } },
			new DefaultGenericNameValue<int> { Tag = 31, Value = -7 }, new DefaultGenericNameValue<string> { Tag = 31, Value = text },
			new DefaultGenericNameOwner<int>.Nested<string> { First = 31, Second = text },
			new DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>> { Tag = 31, Value = new() { Tag = 31, Value = new() { Tag = 31, Value = new() { Tag = 31, Value = text } } } },
			new DefaultGenericNameValue<int[][]> { Tag = 31, Value = new[] { new[] { 31 } } }
		};
		text = null!; tail = null!;
		var names = new[]
		{
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultEmptyNameValue", "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultWordNameValue",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultWideNameValue", "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultReferenceNameValue",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultNestedNameValue",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[System.Int32]",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[System.String]",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameOwner`1+Nested`1[System.Int32,System.String]",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[System.String]]]]",
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericNameValue`1[System.Int32[][]]"
		};
		var spaces = new string("   ".AsSpan());
		for (var kind = 0; kind < boxes.Length; kind++)
		{
			var width = names[kind].Length + 3;
			var formats = new[] { CompositeFormat.Parse("[{0:ignored}]"), CompositeFormat.Parse("[{0,-" + width + ":ignored}]"), CompositeFormat.Parse("[{0," + width + ":ignored}]") };
			for (var form = 0; form < 7; form++)
			for (var roomy = 0; roomy < 2; roomy++)
			for (var align = 0; align < 3; align++)
			{
				var builder = new StringBuilder(roomy == 0 ? 1 : 180).Append("seed");
				System.GC.Collect();
				AppendDefaultNameValue(builder, formats[align], boxes[kind], kind, form, align == 0 ? 0 : align == 1 ? -width : width);
				var expected = align == 0 ? "seed[" + names[kind] + "]" : align == 1 ? "seed[" + names[kind] + spaces + "]" : "seed[" + spaces + names[kind] + "]";
				System.GC.Collect();
				if (builder.ToString() != expected) return 1000 + kind * 100 + form;
				var snapshot = builder.ToString(); builder.Clear();
				AppendDefaultNameValue(builder, formats[align], boxes[kind], kind, form, align == 0 ? 0 : align == 1 ? -width : width);
				if (snapshot != expected || builder.ToString() != expected.Substring(4)) return 2;
			}
			if (boxes[kind].ToString() != names[kind] || boxes[kind].GetType().ToString() != names[kind]) return 3 + kind;
		}
		if (!ReferenceFormattingTextIsOriginal(((DefaultReferenceNameValue)boxes[3]).Text, null) ||
			!ReferenceFormattingTextIsOriginal(((DefaultNestedNameValue)boxes[4]).Payload.Text, ((DefaultNestedNameValue)boxes[4]).Payload.Tail)) return 20;
		if (((DefaultGenericNameValue<int[][]>)boxes[9]).Value[0][0] != 31 ||
			!ReferenceFormattingTextIsOriginal(((DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<DefaultGenericNameValue<string>>>>)boxes[8]).Value.Value.Value.Value, null)) return 21;
		return 42;
	}
}
