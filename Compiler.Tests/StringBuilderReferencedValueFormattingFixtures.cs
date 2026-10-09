/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private struct ReferenceFormattingPayload<T>
	{
		public T Text;
		public char[] Tail;
	}
	private static int _referenceFormattingChurn;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CollectReferenceFormattingGarbage()
	{
		System.GC.Collect();
		for (var index = 0; index < 6; index++)
		{
			var text = new string("######".AsSpan(0, 4 + index % 3));
			var array = new char[4];
			array[0] = '#';
			_referenceFormattingChurn += text.Length + array.Length;
		}
		System.GC.Collect();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static char[] CreateReferenceFormattingTail()
	{
		var result = new char[4];
		result[0] = 'R'; result[1] = '\u03A9'; result[2] = '\0'; result[3] = '\uD800';
		return result;
	}

	private static bool ReferenceFormattingTextIsOriginal(string text, char[]? tail) =>
		text == "R\u03A9\0\uD800" && (tail is null ||
			tail.Length == 4 && tail[0] == 'R' && tail[1] == '\u03A9' && tail[2] == '\0' && tail[3] == '\uD800');

	private static void RecordReferenceFormatting(string text, char[]? tail, int tag, string? format, IFormatProvider? provider, bool plain)
	{
		RecordValueFormatting(tag, -7, format, provider, plain);
		if (!ReferenceFormattingTextIsOriginal(text, tail)) _valueFormattingState!.Invalid++;
	}

	private static bool WriteReferenceFormatting(Span<char> destination, out int charsWritten, string text)
	{
		if (destination.Length < 4)
		{
			if (destination.Length != 0) destination[0] = '?';
			charsWritten = int.MinValue; return false;
		}
		text.AsSpan().CopyTo(destination); charsWritten = 4; return true;
	}

	private struct NestedPlainReferenceValue
	{
		public int Tag;
		public ReferenceFormattingPayload<string> Payload;
		public override string ToString()
		{
			CollectReferenceFormattingGarbage();
			RecordReferenceFormatting(Payload.Text, Payload.Tail, Tag, null, null, true);
			return new string(Payload.Text.AsSpan());
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static NestedPlainReferenceValue CreateNestedPlainReferenceValue() =>
		new() { Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };

	private struct NestedFormalReferenceValue : IFormattable
	{
		public int Tag;
		public ReferenceFormattingPayload<string> Payload;
		public string ToString(string? format, IFormatProvider? provider)
		{
			CollectReferenceFormattingGarbage();
			RecordReferenceFormatting(Payload.Text, Payload.Tail, Tag, format, provider, false);
			return new string(Payload.Text.AsSpan());
		}
		public override string ToString() => throw new InvalidOperationException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static NestedFormalReferenceValue CreateNestedFormalReferenceValue() =>
		new() { Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };

	private struct NestedSpanReferenceValue : ISpanFormattable
	{
		public int Tag;
		public ReferenceFormattingPayload<string> Payload;
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			CollectReferenceFormattingGarbage();
			RecordReferenceFormatting(Payload.Text, Payload.Tail, Tag, format.Length == 1 && format[0] == 'W' ? "W" : null, provider, false);
			return WriteReferenceFormatting(destination, out charsWritten, Payload.Text);
		}
		public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException();
		public override string ToString() => throw new InvalidOperationException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static NestedSpanReferenceValue CreateNestedSpanReferenceValue() =>
		new() { Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };

	private struct WordSpanReferenceValue : ISpanFormattable
	{
		public string Text;
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			CollectReferenceFormattingGarbage();
			RecordReferenceFormatting(Text, null, 31, format.Length == 1 && format[0] == 'W' ? "W" : null, provider, false);
			return WriteReferenceFormatting(destination, out charsWritten, Text);
		}
		public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException();
		public override string ToString() => throw new InvalidOperationException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WordSpanReferenceValue CreateWordSpanReferenceValue() =>
		new() { Text = new string("R\u03A9\0\uD800".AsSpan()) };

	private struct WordPlainReferenceValue
	{
		public string Text;
		public override string ToString()
		{
			CollectReferenceFormattingGarbage();
			RecordReferenceFormatting(Text, null, 31, null, null, true);
			return new string(Text.AsSpan());
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WordPlainReferenceValue CreateWordPlainReferenceValue() =>
		new() { Text = new string("R\u03A9\0\uD800".AsSpan()) };

	private struct WordFormalReferenceValue : IFormattable
	{
		public string Text;
		public string ToString(string? format, IFormatProvider? provider)
		{
			CollectReferenceFormattingGarbage();
			RecordReferenceFormatting(Text, null, 31, format, provider, false);
			return new string(Text.AsSpan());
		}
		public override string ToString() => throw new InvalidOperationException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WordFormalReferenceValue CreateWordFormalReferenceValue() =>
		new() { Text = new string("R\u03A9\0\uD800".AsSpan()) };

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object CreateReferenceFormattingBox(int kind)
	{
		if (kind == 0) { var value = CreateNestedPlainReferenceValue(); object box = value; value.Tag = 0; value.Payload = default; return box; }
		if (kind == 1) { var value = CreateNestedFormalReferenceValue(); object box = value; value.Tag = 0; value.Payload = default; return box; }
		if (kind == 2) { var value = CreateNestedSpanReferenceValue(); object box = value; value.Tag = 0; value.Payload = default; return box; }
		if (kind == 3) { var value = CreateWordSpanReferenceValue(); object box = value; value.Text = null!; return box; }
		if (kind == 4) { var value = CreateWordPlainReferenceValue(); object box = value; value.Text = null!; return box; }
		{ var value = CreateWordFormalReferenceValue(); object box = value; value.Text = null!; return box; }
	}

	private static bool ReferenceFormattingBoxHasOriginalValue(object box, int kind)
	{
		if (kind == 0) { var value = (NestedPlainReferenceValue)box; return value.Tag == 31 && ReferenceFormattingTextIsOriginal(value.Payload.Text, value.Payload.Tail); }
		if (kind == 1) { var value = (NestedFormalReferenceValue)box; return value.Tag == 31 && ReferenceFormattingTextIsOriginal(value.Payload.Text, value.Payload.Tail); }
		if (kind == 2) { var value = (NestedSpanReferenceValue)box; return value.Tag == 31 && ReferenceFormattingTextIsOriginal(value.Payload.Text, value.Payload.Tail); }
		if (kind == 3) { var value = (WordSpanReferenceValue)box; return ReferenceFormattingTextIsOriginal(value.Text, null); }
		if (kind == 4) { var value = (WordPlainReferenceValue)box; return ReferenceFormattingTextIsOriginal(value.Text, null); }
		{ var value = (WordFormalReferenceValue)box; return ReferenceFormattingTextIsOriginal(value.Text, null); }
	}

	private static void AppendReferenceFormattingValue(StringBuilder builder, IFormatProvider provider, CompositeFormat format, object box, int kind, int form, int alignment)
	{
		if (form < 2)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, provider);
			handler.AppendLiteral("[");
			if (form == 1) handler.AppendFormatted(box, alignment, "W");
			else if (kind == 0) handler.AppendFormatted((NestedPlainReferenceValue)box, alignment, "W");
			else if (kind == 1) handler.AppendFormatted((NestedFormalReferenceValue)box, alignment, "W");
			else if (kind == 2) handler.AppendFormatted((NestedSpanReferenceValue)box, alignment, "W");
			else if (kind == 3) handler.AppendFormatted((WordSpanReferenceValue)box, alignment, "W");
			else if (kind == 4) handler.AppendFormatted((WordPlainReferenceValue)box, alignment, "W");
			else handler.AppendFormatted((WordFormalReferenceValue)box, alignment, "W");
			handler.AppendLiteral("]"); builder.Append(ref handler); return;
		}
		if (form == 2)
		{
			if (kind == 0) builder.AppendFormat<NestedPlainReferenceValue>(provider, format, (NestedPlainReferenceValue)box);
			else if (kind == 1) builder.AppendFormat<NestedFormalReferenceValue>(provider, format, (NestedFormalReferenceValue)box);
			else if (kind == 2) builder.AppendFormat<NestedSpanReferenceValue>(provider, format, (NestedSpanReferenceValue)box);
			else if (kind == 3) builder.AppendFormat<WordSpanReferenceValue>(provider, format, (WordSpanReferenceValue)box);
			else if (kind == 4) builder.AppendFormat<WordPlainReferenceValue>(provider, format, (WordPlainReferenceValue)box);
			else builder.AppendFormat<WordFormalReferenceValue>(provider, format, (WordFormalReferenceValue)box);
		}
		else if (form == 3)
		{
			if (kind == 0) builder.AppendFormat<NestedPlainReferenceValue, int>(provider, format, (NestedPlainReferenceValue)box, 9);
			else if (kind == 1) builder.AppendFormat<NestedFormalReferenceValue, int>(provider, format, (NestedFormalReferenceValue)box, 9);
			else if (kind == 2) builder.AppendFormat<NestedSpanReferenceValue, int>(provider, format, (NestedSpanReferenceValue)box, 9);
			else if (kind == 3) builder.AppendFormat<WordSpanReferenceValue, int>(provider, format, (WordSpanReferenceValue)box, 9);
			else if (kind == 4) builder.AppendFormat<WordPlainReferenceValue, int>(provider, format, (WordPlainReferenceValue)box, 9);
			else builder.AppendFormat<WordFormalReferenceValue, int>(provider, format, (WordFormalReferenceValue)box, 9);
		}
		else if (form == 4)
		{
			if (kind == 0) builder.AppendFormat<NestedPlainReferenceValue, int, string>(provider, format, (NestedPlainReferenceValue)box, 9, "extra");
			else if (kind == 1) builder.AppendFormat<NestedFormalReferenceValue, int, string>(provider, format, (NestedFormalReferenceValue)box, 9, "extra");
			else if (kind == 2) builder.AppendFormat<NestedSpanReferenceValue, int, string>(provider, format, (NestedSpanReferenceValue)box, 9, "extra");
			else if (kind == 3) builder.AppendFormat<WordSpanReferenceValue, int, string>(provider, format, (WordSpanReferenceValue)box, 9, "extra");
			else if (kind == 4) builder.AppendFormat<WordPlainReferenceValue, int, string>(provider, format, (WordPlainReferenceValue)box, 9, "extra");
			else builder.AppendFormat<WordFormalReferenceValue, int, string>(provider, format, (WordFormalReferenceValue)box, 9, "extra");
		}
		else
		{
			var args = new object?[] { box, 9, "extra", box };
			if (form == 5) builder.AppendFormat(provider, format, args);
			else builder.AppendFormat(provider, format, new ReadOnlySpan<object?>(args));
		}
	}

	// The producer has returned and the original copy is cleared before collection.
	// No box or source object can keep the formatted copy's fields alive.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AppendUnboxedReferenceFormattingValue(StringBuilder builder, IFormatProvider provider, int kind)
	{
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
		if (kind == 0)
		{
			var original = CreateNestedPlainReferenceValue(); var copy = original; original = default;
			CollectReferenceFormattingGarbage(); handler.AppendFormatted(copy, "W");
			return copy.Tag == 31 && ReferenceFormattingTextIsOriginal(copy.Payload.Text, copy.Payload.Tail);
		}
		if (kind == 1)
		{
			var original = CreateNestedFormalReferenceValue(); var copy = original; original = default;
			CollectReferenceFormattingGarbage(); handler.AppendFormatted(copy, "W");
			return copy.Tag == 31 && ReferenceFormattingTextIsOriginal(copy.Payload.Text, copy.Payload.Tail);
		}
		if (kind == 2)
		{
			var original = CreateNestedSpanReferenceValue(); var copy = original; original = default;
			CollectReferenceFormattingGarbage(); handler.AppendFormatted(copy, "W");
			return copy.Tag == 31 && ReferenceFormattingTextIsOriginal(copy.Payload.Text, copy.Payload.Tail);
		}
		if (kind == 3)
		{
			var original = CreateWordSpanReferenceValue(); var copy = original; original = default;
			CollectReferenceFormattingGarbage(); handler.AppendFormatted(copy, "W");
			return ReferenceFormattingTextIsOriginal(copy.Text, null);
		}
		if (kind == 4)
		{
			var original = CreateWordPlainReferenceValue(); var copy = original; original = default;
			CollectReferenceFormattingGarbage(); handler.AppendFormatted(copy, "W");
			return ReferenceFormattingTextIsOriginal(copy.Text, null);
		}
		{
			var original = CreateWordFormalReferenceValue(); var copy = original; original = default;
			CollectReferenceFormattingGarbage(); handler.AppendFormatted(copy, "W");
			return ReferenceFormattingTextIsOriginal(copy.Text, null);
		}
	}

	public static int CoreLibStringBuilderReferencedValueFormattingEntry() => RunReferencedValueFormatting();

	private static int RunReferencedValueFormatting()
	{
		var provider = new NumberFormatInfo();
		var formats = new[] { CompositeFormat.Parse("[{0:W}]"), CompositeFormat.Parse("[{0,-7:W}]"), CompositeFormat.Parse("[{0,7:W}]") };
		for (var kind = 0; kind < 6; kind++)
		{
			var state = new ValueFormattingState { Provider = provider }; _valueFormattingState = state;
			var builder = new StringBuilder(80);
			if (!AppendUnboxedReferenceFormattingValue(builder, provider, kind)) return 4000 + kind;
			System.GC.Collect();
			if (builder.ToString() != "R\u03A9\0\uD800" || state.Calls != 1 || state.Invalid != 0) return 4100 + kind;
		}
		for (var kind = 0; kind < 6; kind++)
		for (var form = 0; form < 7; form++)
		for (var roomy = 0; roomy < 2; roomy++)
		for (var align = 0; align < 3; align++)
		{
			var box = CreateReferenceFormattingBox(kind);
			var state = new ValueFormattingState { Provider = provider }; _valueFormattingState = state;
			var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append("seed");
			System.GC.Collect();
			AppendReferenceFormattingValue(builder, provider, formats[align], box, kind, form, align == 0 ? 0 : align == 1 ? -7 : 7);
			var expected = align == 0 ? "seed[R\u03A9\0\uD800]" : align == 1 ? "seed[R\u03A9\0\uD800   ]" : "seed[   R\u03A9\0\uD800]";
			System.GC.Collect();
			if (builder.ToString() != expected) return 1000 + kind * 100 + form;
			if (state.Invalid != 0) return 2000 + kind * 100 + form;
			if (!ReferenceFormattingBoxHasOriginalValue(box, kind)) return 3000 + kind * 100 + form;
			if (state.Calls != ((kind == 2 || kind == 3) && roomy == 0 && align != 2 ? 2 : 1)) return 200 + state.Calls;
			var snapshot = builder.ToString(); builder.Clear(); state.Calls = 0;
			AppendReferenceFormattingValue(builder, provider, formats[align], box, kind, form, align == 0 ? 0 : align == 1 ? -7 : 7);
			if (snapshot != expected || builder.ToString() != expected.Substring(4) || state.Invalid != 0) return 3;
		}
		for (var kind = 0; kind < 6; kind++)
		for (var form = 0; form < 7; form++)
		{
			var box = CreateReferenceFormattingBox(kind); var error = new InvalidOperationException();
			var state = new ValueFormattingState { Provider = provider, Throw = true, Error = error }; _valueFormattingState = state;
			var builder = new StringBuilder(1).Append("seed");
			try { AppendReferenceFormattingValue(builder, provider, formats[2], box, kind, form, 7); return 4; }
			catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 5; }
			if (builder.ToString() != "seed[" || state.Calls != 1 || state.Invalid != 0 || !ReferenceFormattingBoxHasOriginalValue(box, kind)) return 6;
			state.Throw = false; builder.Clear(); AppendReferenceFormattingValue(builder, provider, formats[0], box, kind, form, 0);
			if (builder.ToString() != "[R\u03A9\0\uD800]" || state.Invalid != 0) return 7;
		}
		_valueFormattingState = null;
		return 42;
	}
}
