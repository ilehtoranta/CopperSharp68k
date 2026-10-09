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
	private sealed class ValueFormattingState
	{
		public IFormatProvider? Provider;
		public int Calls, Invalid;
		public bool Throw;
		public InvalidOperationException? Error;
	}
	private static ValueFormattingState? _valueFormattingState;

	private static void RecordValueFormatting(int first, int second, string? format, IFormatProvider? provider, bool plain)
	{
		var state = _valueFormattingState!;
		state.Calls++;
		if (first != 31 || second != -7 || (!plain && (format != "W" || !ReferenceEquals(provider, state.Provider)))) state.Invalid++;
		if (state.Throw) throw state.Error!;
	}

	private struct PlainFormattingValue
	{
		public int First, Second;
		public override string ToString()
		{
			System.GC.Collect(); RecordValueFormatting(First, Second, null, null, true);
			return new string("V\u03A9\0\uD800".AsSpan());
		}
	}
	private struct FormalFormattingValue : IFormattable
	{
		public int First, Second;
		public string ToString(string? format, IFormatProvider? provider)
		{
			System.GC.Collect(); RecordValueFormatting(First, Second, format, provider, false);
			return new string("V\u03A9\0\uD800".AsSpan());
		}
		public override string ToString() => throw new InvalidOperationException();
	}
	private struct SpanFormattingValue : ISpanFormattable
	{
		public int First, Second;
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			System.GC.Collect();
			RecordValueFormatting(First, Second, format.Length == 1 && format[0] == 'W' ? "W" : null, provider, false);
			if (destination.Length < 4)
			{
				if (destination.Length != 0) destination[0] = '?';
				charsWritten = int.MinValue; return false;
			}
			destination[0] = 'V'; destination[1] = '\u03A9'; destination[2] = '\0'; destination[3] = '\uD800';
			charsWritten = 4; return true;
		}
		public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException();
		public override string ToString() => throw new InvalidOperationException();
	}
	private struct WordSpanFormattingValue : ISpanFormattable
	{
		public int First;
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			System.GC.Collect();
			RecordValueFormatting(First, -7, format.Length == 1 && format[0] == 'W' ? "W" : null, provider, false);
			if (destination.Length < 4)
			{
				if (destination.Length != 0) destination[0] = '?';
				charsWritten = int.MinValue; return false;
			}
			destination[0] = 'V'; destination[1] = '\u03A9'; destination[2] = '\0'; destination[3] = '\uD800';
			charsWritten = 4; return true;
		}
		public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException();
		public override string ToString() => throw new InvalidOperationException();
	}
	private struct WordPlainFormattingValue
	{
		public int First;
		public override string ToString()
		{
			System.GC.Collect(); RecordValueFormatting(First, -7, null, null, true);
			return new string("V\u03A9\0\uD800".AsSpan());
		}
	}
	private struct WordFormalFormattingValue : IFormattable
	{
		public int First;
		public string ToString(string? format, IFormatProvider? provider)
		{
			System.GC.Collect(); RecordValueFormatting(First, -7, format, provider, false);
			return new string("V\u03A9\0\uD800".AsSpan());
		}
		public override string ToString() => throw new InvalidOperationException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object CreateFormattingBox(int kind)
	{
		if (kind == 0) { var value = new PlainFormattingValue { First = 31, Second = -7 }; object box = value; value.First = value.Second = 0; return box; }
		if (kind == 1) { var value = new FormalFormattingValue { First = 31, Second = -7 }; object box = value; value.First = value.Second = 0; return box; }
		if (kind == 2) { var value = new SpanFormattingValue { First = 31, Second = -7 }; object box = value; value.First = value.Second = 0; return box; }
		if (kind == 3) { var value = new WordSpanFormattingValue { First = 31 }; object box = value; value.First = 0; return box; }
		if (kind == 4) { var value = new WordPlainFormattingValue { First = 31 }; object box = value; value.First = 0; return box; }
		{ var value = new WordFormalFormattingValue { First = 31 }; object box = value; value.First = 0; return box; }
	}

	private static bool FormattingBoxHasOriginalValue(object box, int kind)
	{
		if (kind == 0) { var value = (PlainFormattingValue)box; return value.First == 31 && value.Second == -7; }
		if (kind == 1) { var value = (FormalFormattingValue)box; return value.First == 31 && value.Second == -7; }
		if (kind == 2) { var value = (SpanFormattingValue)box; return value.First == 31 && value.Second == -7; }
		if (kind == 3) return ((WordSpanFormattingValue)box).First == 31;
		if (kind == 4) return ((WordPlainFormattingValue)box).First == 31;
		return ((WordFormalFormattingValue)box).First == 31;
	}

	private static void AppendFormattingValue(StringBuilder builder, IFormatProvider provider, CompositeFormat format, object box, int kind, int form, int alignment)
	{
		if (form < 2)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, provider);
			handler.AppendLiteral("[");
			if (form == 1) handler.AppendFormatted(box, alignment, "W");
			else if (kind == 0) handler.AppendFormatted((PlainFormattingValue)box, alignment, "W");
			else if (kind == 1) handler.AppendFormatted((FormalFormattingValue)box, alignment, "W");
			else if (kind == 2) handler.AppendFormatted((SpanFormattingValue)box, alignment, "W");
			else if (kind == 3) handler.AppendFormatted((WordSpanFormattingValue)box, alignment, "W");
			else if (kind == 4) handler.AppendFormatted((WordPlainFormattingValue)box, alignment, "W");
			else handler.AppendFormatted((WordFormalFormattingValue)box, alignment, "W");
			handler.AppendLiteral("]"); builder.Append(ref handler); return;
		}
		if (form == 2)
		{
			if (kind == 0) builder.AppendFormat<PlainFormattingValue>(provider, format, (PlainFormattingValue)box);
			else if (kind == 1) builder.AppendFormat<FormalFormattingValue>(provider, format, (FormalFormattingValue)box);
			else if (kind == 2) builder.AppendFormat<SpanFormattingValue>(provider, format, (SpanFormattingValue)box);
			else if (kind == 3) builder.AppendFormat<WordSpanFormattingValue>(provider, format, (WordSpanFormattingValue)box);
			else if (kind == 4) builder.AppendFormat<WordPlainFormattingValue>(provider, format, (WordPlainFormattingValue)box);
			else builder.AppendFormat<WordFormalFormattingValue>(provider, format, (WordFormalFormattingValue)box);
		}
		else if (form == 3)
		{
			if (kind == 0) builder.AppendFormat<PlainFormattingValue, int>(provider, format, (PlainFormattingValue)box, 9);
			else if (kind == 1) builder.AppendFormat<FormalFormattingValue, int>(provider, format, (FormalFormattingValue)box, 9);
			else if (kind == 2) builder.AppendFormat<SpanFormattingValue, int>(provider, format, (SpanFormattingValue)box, 9);
			else if (kind == 3) builder.AppendFormat<WordSpanFormattingValue, int>(provider, format, (WordSpanFormattingValue)box, 9);
			else if (kind == 4) builder.AppendFormat<WordPlainFormattingValue, int>(provider, format, (WordPlainFormattingValue)box, 9);
			else builder.AppendFormat<WordFormalFormattingValue, int>(provider, format, (WordFormalFormattingValue)box, 9);
		}
		else if (form == 4)
		{
			if (kind == 0) builder.AppendFormat<PlainFormattingValue, int, string>(provider, format, (PlainFormattingValue)box, 9, "extra");
			else if (kind == 1) builder.AppendFormat<FormalFormattingValue, int, string>(provider, format, (FormalFormattingValue)box, 9, "extra");
			else if (kind == 2) builder.AppendFormat<SpanFormattingValue, int, string>(provider, format, (SpanFormattingValue)box, 9, "extra");
			else if (kind == 3) builder.AppendFormat<WordSpanFormattingValue, int, string>(provider, format, (WordSpanFormattingValue)box, 9, "extra");
			else if (kind == 4) builder.AppendFormat<WordPlainFormattingValue, int, string>(provider, format, (WordPlainFormattingValue)box, 9, "extra");
			else builder.AppendFormat<WordFormalFormattingValue, int, string>(provider, format, (WordFormalFormattingValue)box, 9, "extra");
		}
		else
		{
			var args = new object?[] { box, 9, "extra", box };
			if (form == 5) builder.AppendFormat(provider, format, args);
			else builder.AppendFormat(provider, format, new ReadOnlySpan<object?>(args));
		}
	}

	public static int CoreLibStringBuilderCustomValueFormattingEntry()
	{
		var provider = new NumberFormatInfo();
		var formats = new[] { CompositeFormat.Parse("[{0:W}]"), CompositeFormat.Parse("[{0,-7:W}]"), CompositeFormat.Parse("[{0,7:W}]") };
		for (var kind = 0; kind < 6; kind++)
		for (var form = 0; form < 7; form++)
		for (var roomy = 0; roomy < 2; roomy++)
		for (var align = 0; align < 3; align++)
		{
			var box = CreateFormattingBox(kind);
			var state = new ValueFormattingState { Provider = provider }; _valueFormattingState = state;
			var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append("seed");
			System.GC.Collect();
			AppendFormattingValue(builder, provider, formats[align], box, kind, form, align == 0 ? 0 : align == 1 ? -7 : 7);
			var expected = align == 0 ? "seed[V\u03A9\0\uD800]" : align == 1 ? "seed[V\u03A9\0\uD800   ]" : "seed[   V\u03A9\0\uD800]";
			System.GC.Collect();
			if (builder.ToString() != expected || state.Invalid != 0 || !FormattingBoxHasOriginalValue(box, kind)) return 100 + kind * 20 + form;
			if (state.Calls != ((kind == 2 || kind == 3) && roomy == 0 && align != 2 ? 2 : 1)) return 200 + state.Calls;
			var snapshot = builder.ToString(); builder.Clear(); state.Calls = 0;
			AppendFormattingValue(builder, provider, formats[align], box, kind, form, align == 0 ? 0 : align == 1 ? -7 : 7);
			if (snapshot != expected || builder.ToString() != expected.Substring(4) || state.Invalid != 0) return 3;
		}
		for (var kind = 0; kind < 6; kind++)
		for (var form = 0; form < 7; form++)
		{
			var box = CreateFormattingBox(kind); var error = new InvalidOperationException();
			var state = new ValueFormattingState { Provider = provider, Throw = true, Error = error }; _valueFormattingState = state;
			var builder = new StringBuilder(1).Append("seed");
			try { AppendFormattingValue(builder, provider, formats[2], box, kind, form, 7); return 4; }
			catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 5; }
			if (builder.ToString() != "seed[" || state.Calls != 1 || state.Invalid != 0 || !FormattingBoxHasOriginalValue(box, kind)) return 6;
			state.Throw = false; builder.Clear(); AppendFormattingValue(builder, provider, formats[0], box, kind, form, 0);
			if (builder.ToString() != "[V\u03A9\0\uD800]" || state.Invalid != 0) return 7;
		}
		_valueFormattingState = null;
		return 42;
	}
}
