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
	public static int CoreLibStringBuilderInterpolatedStringsEntry()
	{
		var dynamic = new string("\u03A9\0\uD800".AsSpan());
		for (var roomy = 0; roomy < 2; roomy++)
		for (var source = 0; source < 3; source++)
		for (var form = 0; form < 4; form++)
		for (var consume = 0; consume < 4; consume++)
		{
			string? value = source == 0 ? null : source == 1 ? string.Empty : dynamic;
			var text = value ?? string.Empty;
			var builder = new StringBuilder(roomy == 0 ? 1 : 32);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			handler.AppendLiteral("[");
			if (form == 0) handler.AppendFormatted(value);
			else handler.AppendFormatted(value, form == 1 ? 0 : form == 2 ? -6 : 6, "ignored");
			handler.AppendLiteral("]");
			var otherProvider = new UnusedInterpolatedProvider();
			var result = consume switch { 0 => builder.Append(ref handler), 1 => builder.Append(otherProvider, ref handler),
				2 => builder.AppendLine(ref handler), _ => builder.AppendLine(otherProvider, ref handler) };
			System.GC.Collect();
			var spaces = new string("      ".AsSpan(0, 6 - text.Length));
			var expected = form < 2 ? "[" + text + "]" : form == 2 ? "[" + text + spaces + "]" : "[" + spaces + text + "]";
			if (consume >= 2) expected += Environment.NewLine;
			if (!ReferenceEquals(result, builder) || builder.ToString() != expected || otherProvider.Queries != 0) return 1;
		}
		return 42;
	}

	private sealed class HandlerPlainValue
	{
		public int Calls;
		public bool Throw;
		public override string ToString()
		{
			Calls++;
			System.GC.Collect();
			if (Throw) throw new InvalidOperationException();
			return new string("plain\u03A9".AsSpan());
		}
	}

	private sealed class HandlerCustomProvider : IFormatProvider, ICustomFormatter
	{
		public int Queries, Calls;
		public object? Expected;
		public string? ExpectedFormat;
		public bool Integer, ReturnNull, DropFormatter, ThrowQuery, ThrowFormat;
		public bool Valid = true;
		public InvalidOperationException? Error;
		public object? GetFormat(Type? type)
		{
			Queries++;
			System.GC.Collect();
			if (type != typeof(ICustomFormatter)) throw new InvalidOperationException();
			if (ThrowQuery && Queries > 1) throw Error!;
			return DropFormatter && Queries > 1 ? null : this;
		}
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			Calls++;
			System.GC.Collect();
			if (!ReferenceEquals(provider, this) || format != ExpectedFormat ||
				(Integer ? value is not int number || number != 42 : !ReferenceEquals(value, Expected))) Valid = false;
			if (ThrowFormat) throw Error!;
			return ReturnNull ? null! : new string("C\u03A9\0\uD800".AsSpan());
		}
	}

	public static int CoreLibStringBuilderInterpolatedCustomEntry()
	{
		for (var roomy = 0; roomy < 2; roomy++)
		for (var returnNull = 0; returnNull < 2; returnNull++)
		for (var form = 0; form < 6; form++)
		for (var alignIndex = 0; alignIndex < 3; alignIndex++)
		for (var consume = 0; consume < 4; consume++)
		{
			var plain = new HandlerPlainValue();
			var text = new string("text".AsSpan());
			var provider = new HandlerCustomProvider { ReturnNull = returnNull != 0, Integer = form == 5,
				Expected = form == 0 || form == 1 ? text : form == 2 || form == 4 ? null : plain,
				ExpectedFormat = form == 0 ? null : "W" };
			var builder = new StringBuilder(roomy == 0 ? 1 : 64);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			handler.AppendLiteral("[");
			var alignment = alignIndex == 0 ? 0 : alignIndex == 1 ? -7 : 7;
			if (form == 0) handler.AppendFormatted(text);
			else if (form == 1) handler.AppendFormatted(text, alignment, "W");
			else if (form == 2) handler.AppendFormatted((string?)null, alignment, "W");
			else if (form == 3) handler.AppendFormatted((object)plain, alignment, "W");
			else if (form == 4) handler.AppendFormatted<HandlerPlainValue>(null!, alignment, "W");
			else handler.AppendFormatted(42, alignment, "W");
			handler.AppendLiteral("]");
			var other = new UnusedInterpolatedProvider();
			var result = consume switch { 0 => builder.Append(ref handler), 1 => builder.Append(other, ref handler),
				2 => builder.AppendLine(ref handler), _ => builder.AppendLine(other, ref handler) };
			System.GC.Collect();
			var formatted = returnNull == 0 ? "C\u03A9\0\uD800" : "";
			var padding = form == 0 || alignment == 0 ? 0 : 7 - formatted.Length;
			var spaces = new string("       ".AsSpan(0, padding));
			var expected = alignment > 0 && form != 0 ? "[" + spaces + formatted + "]" : "[" + formatted + spaces + "]";
			if (consume >= 2) expected += Environment.NewLine;
			if (!provider.Valid || provider.Calls != 1 || provider.Queries != (alignment > 0 && form != 0 ? 3 : 2) ||
				plain.Calls != 0 || other.Queries != 0 || !ReferenceEquals(result, builder) || builder.ToString() != expected) return 10 + form;
		}
		for (var failure = 0; failure < 3; failure++)
		for (var roomy = 0; roomy < 2; roomy++)
		for (var alignment = -7; alignment <= 7; alignment += 7)
		{
			var error = new InvalidOperationException();
			var value = new HandlerPlainValue();
			var provider = new HandlerCustomProvider { Expected = value, ExpectedFormat = "W", Error = error,
				DropFormatter = failure == 0, ThrowQuery = failure == 1, ThrowFormat = failure == 2 };
			var builder = new StringBuilder(roomy == 0 ? 4 : 32).Append("seed");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			try { handler.AppendFormatted((object)value, alignment, "W"); if (failure != 0) return 20; }
			catch (InvalidOperationException caught) { if (failure == 0 || !ReferenceEquals(caught, error)) return 21; }
			var committed = failure != 0 || alignment == 0 ? "seed" : alignment < 0 ? "seed       " : "seed plain\u03A9";
			if (builder.ToString() != committed || value.Calls != (failure == 0 && alignment > 0 ? 1 : 0) ||
				provider.Calls != (failure == 2 ? 1 : 0) || provider.Queries != (failure == 2 && alignment > 0 ? 3 : 2)) return 22;
			provider.DropFormatter = provider.ThrowQuery = provider.ThrowFormat = false;
			handler.AppendFormatted((object)value, -7, "W");
			if (builder.ToString() != committed + "C\u03A9\0\uD800   " || !provider.Valid ||
				provider.Calls != (failure == 2 ? 2 : 1) || provider.Queries != (failure == 2 && alignment > 0 ? 4 : 3)) return 23;
		}
		return 42;
	}
	private sealed class HandlerFormalValue : IFormattable
	{
		public IFormatProvider? Provider;
		public int Calls;
		public bool Valid = true;
		public string ToString(string? format, IFormatProvider? provider)
		{
			Calls++;
			System.GC.Collect();
			if (format != "W" || !ReferenceEquals(Provider, provider)) Valid = false;
			return new string("formal".AsSpan());
		}
		public override string ToString() => throw new InvalidOperationException();
	}
	public static int CoreLibStringBuilderInterpolatedNullObjectEntry()
	{
		var builder = new StringBuilder(32).Append("seed");
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
		handler.AppendFormatted((object?)null, -8, "W");
		return builder.ToString() == "seed        " ? 42 : 1;
	}
	public static int CoreLibStringBuilderInterpolatedObjectsEntry()
	{
		for (var roomy = 0; roomy < 2; roomy++)
		for (var source = 0; source < 5; source++)
		for (var align = -8; align <= 8; align += 8)
		{
			var provider = new InterpolatedDiscoveryProvider();
			var plain = new HandlerPlainValue();
			var formal = new HandlerFormalValue { Provider = provider };
			var span = new HandlerGrowingValue { Width = 3, Provider = provider };
			object? value = source == 0 ? null : source == 1 ? new string("\u03A9\0\uD800".AsSpan()) :
				source == 2 ? plain : source == 3 ? formal : span;
			var builder = new StringBuilder(roomy == 0 ? 1 : 32).Append("[");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			handler.AppendFormatted(value, align, "W");
			handler.AppendLiteral("]");
			System.GC.Collect();
			var expected = source == 0 ? "" : source == 1 || source == 4 ? "\u03A9\0\uD800" : source == 2 ? "plain\u03A9" : "formal";
			var padding = align == 0 ? 0 : 8 - expected.Length;
			var spaces = new string("        ".AsSpan(0, padding));
			expected = align > 0 ? "[" + spaces + expected + "]" : "[" + expected + spaces + "]";
			if (builder.ToString() != expected || plain.Calls != (source == 2 ? 1 : 0) ||
				formal.Calls != (source == 3 ? 1 : 0) || !formal.Valid || span.Invalid != 0) return 1;
			if (source == 4 && span.Calls != (roomy == 0 && align <= 0 ? 2 : 1)) return 2;
			if (provider.Queries != (align > 0 || source == 4 && roomy == 0 ? 2 : 1)) return 3;
		}
		var throwing = new HandlerPlainValue { Throw = true };
		var seed = new StringBuilder(16).Append("seed");
		var retry = new StringBuilder.AppendInterpolatedStringHandler(0, 1, seed);
		try { retry.AppendFormatted((object)throwing, 8, "W"); return 4; } catch (InvalidOperationException) { }
		if (seed.ToString() != "seed" || throwing.Calls != 1) return 5;
		throwing.Throw = false;
		retry.AppendFormatted((object)throwing, -8, "W");
		return seed.ToString() == "seedplain\u03A9  " && throwing.Calls == 2 ? 42 : 6;
	}

	private sealed class HandlerFaultyValue : ISpanFormattable
	{
		public int Mode, Calls, Invalid;
		public bool FailFirst;
		public string? ExpectedFormat;
		public IFormatProvider? Provider;
		public InvalidOperationException? Error;
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			Calls++;
			System.GC.Collect();
			if (!ReferenceEquals(provider, Provider) || format.Length != (ExpectedFormat is null ? 0 : 1) ||
				(format.Length != 0 && format[0] != 'W')) Invalid++;
			if (destination.Length != 0) destination[0] = '?';
			if (FailFirst && Calls == 1) { charsWritten = int.MinValue; return false; }
			if (Mode == 4) throw Error!;
			if (Mode == 5) { charsWritten = 0; return true; }
			if (Mode == 6)
			{
				for (var i = 0; i < destination.Length; i++) destination[i] = '=';
				charsWritten = destination.Length;
				return true;
			}
			if (Mode != 0)
			{
				charsWritten = Mode == 1 ? -1 : Mode == 2 ? destination.Length + 1 : int.MaxValue;
				return true;
			}
			charsWritten = 0;
			if (destination.Length < 3) return false;
			destination[0] = '\u03A9'; destination[1] = '\0'; destination[2] = '\uD800';
			charsWritten = 3;
			return true;
		}
		public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException();
		public override string ToString() => throw new InvalidOperationException();
	}

	public static int CoreLibStringBuilderInterpolatedFaultsEntry()
	{
		for (var mode = 1; mode <= 4; mode++)
		for (var path = 0; path < 5; path++)
		for (var form = 0; form < 5; form++)
		{
			// Default/format-only overloads have no alignment parameter.
			if (form < 2 && (path == 1 || path == 3 || path == 4)) continue;
			var temporary = path >= 2;
			var provider = new InterpolatedDiscoveryProvider();
			var error = new InvalidOperationException();
			var value = new HandlerFaultyValue { Mode = mode, FailFirst = path is 2 or 3, Provider = provider,
				ExpectedFormat = form is 0 or 2 ? null : "W", Error = error };
			var capacity = path is 2 or 3 ? 4 : 32;
			var builder = new StringBuilder(capacity).Append('~', capacity);
			var chunks = builder.GetChunks(); chunks.MoveNext(); var storage = chunks.Current;
			builder.Clear().Append("seed");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			var alignment = path is 1 or 3 ? -8 : path == 4 ? 8 : 0;
			try
			{
				if (form == 0) handler.AppendFormatted(value);
				else if (form == 1) handler.AppendFormatted(value, "W");
				else if (form == 2) handler.AppendFormatted(value, alignment);
				else if (form == 3) handler.AppendFormatted(value, alignment, "W");
				else handler.AppendFormatted((object)value, alignment, "W");
				return 10 + mode;
			}
			catch (FormatException) { if (mode == 4 || temporary) return 20; }
			catch (ArgumentOutOfRangeException) { if (mode == 4 || !temporary) return 21; }
			catch (InvalidOperationException caught) { if (mode != 4 || !ReferenceEquals(caught, error)) return 22; }
			System.GC.Collect();
			if (builder.Length != 4 || builder.Capacity != capacity || builder.ToString() != "seed" || value.Invalid != 0 ||
				value.Calls != (path is 2 or 3 ? 2 : 1) || provider.Queries != (temporary ? 2 : 1)) return 23;
			var span = storage.Span;
			for (var i = 0; i < 4; i++) if (span[i] != "seed"[i]) return 24;
			for (var i = 4; i < span.Length; i++) if (span[i] != (i == 4 && !temporary ? '?' : '~')) return 25;
			value.Mode = 0; value.Calls = 0; value.FailFirst = false; value.ExpectedFormat = "W";
			handler.AppendFormatted((object)value, -8, "W");
			System.GC.Collect();
			if (builder.ToString() != "seed\u03A9\0\uD800     " || value.Invalid != 0 ||
				value.Calls != (capacity == 4 ? 2 : 1) || provider.Queries != (temporary ? capacity == 4 ? 3 : 2 : 1)) return 26;
		}
		return 42;
	}

	public static int CoreLibStringBuilderInterpolatedBoundaryCountsEntry()
	{
		for (var mode = 5; mode <= 6; mode++)
		for (var path = 0; path < 3; path++)
		for (var form = 0; form < 4; form++)
		{
			if (form < 2 && path == 2) continue;
			var provider = new InterpolatedDiscoveryProvider();
			var value = new HandlerFaultyValue { Mode = mode, Provider = provider, ExpectedFormat = form is 0 or 2 ? null : "W" };
			var builder = new StringBuilder(path == 1 ? 32 : 4).Append("seed");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			var alignment = path == 2 ? 8 : -8;
			if (form == 0) handler.AppendFormatted(value);
			else if (form == 1) handler.AppendFormatted(value, "W");
			else if (form == 2) handler.AppendFormatted(value, alignment);
			else handler.AppendFormatted((object)value, alignment, "W");
			System.GC.Collect();
			var written = mode == 5 || path == 0 ? 0 : path == 1 ? 28 : 256;
			var padding = form < 2 || written >= 8 ? 0 : 8 - written;
			var text = builder.ToString();
			if (text.Length != 4 + written + padding || value.Calls != 1 || value.Invalid != 0 ||
				provider.Queries != (path == 2 ? 2 : 1)) return 1;
			for (var i = 0; i < 4; i++) if (text[i] != "seed"[i]) return 2;
			for (var i = 4; i < text.Length; i++) if (text[i] != (written == 0 ? ' ' : '=')) return 3;
			handler.AppendLiteral("!");
			if (builder.Length != text.Length + 1 || builder[builder.Length - 1] != '!') return 4;
		}
		return 42;
	}

	private sealed class HandlerNullPlainValue
	{
		public int Calls;
		public override string ToString() { Calls++; System.GC.Collect(); return null!; }
	}
	private sealed class HandlerNullFormalValue : IFormattable
	{
		public int Calls, Invalid;
		public string? ExpectedFormat;
		public IFormatProvider? Provider;
		public string ToString(string? format, IFormatProvider? provider)
		{
			Calls++; System.GC.Collect();
			if (format != ExpectedFormat || !ReferenceEquals(provider, Provider)) Invalid++;
			return null!;
		}
		public override string ToString() => throw new InvalidOperationException();
	}
	public static int CoreLibStringBuilderInterpolatedNullFallbackEntry()
	{
		for (var roomy = 0; roomy < 2; roomy++)
		for (var kind = 0; kind < 2; kind++)
		for (var form = 0; form < 5; form++)
		for (var alignment = -8; alignment <= 8; alignment += 8)
		{
			if (form < 2 && alignment != 0) continue;
			var provider = new InterpolatedDiscoveryProvider();
			var plain = new HandlerNullPlainValue();
			var formal = new HandlerNullFormalValue { Provider = provider, ExpectedFormat = form is 0 or 2 ? null : "W" };
			var builder = new StringBuilder(roomy == 0 ? 4 : 32).Append("seed");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			if (form == 0) { if (kind == 0) handler.AppendFormatted(plain); else handler.AppendFormatted(formal); }
			else if (form == 1) { if (kind == 0) handler.AppendFormatted(plain, "W"); else handler.AppendFormatted(formal, "W"); }
			else if (form == 2) { if (kind == 0) handler.AppendFormatted(plain, alignment); else handler.AppendFormatted(formal, alignment); }
			else if (form == 3) { if (kind == 0) handler.AppendFormatted(plain, alignment, "W"); else handler.AppendFormatted(formal, alignment, "W"); }
			else handler.AppendFormatted(kind == 0 ? (object)plain : formal, alignment, "W");
			System.GC.Collect();
			if (builder.ToString() != (alignment == 0 ? "seed" : "seed        ") || formal.Invalid != 0 ||
				plain.Calls != (kind == 0 ? 1 : 0) || formal.Calls != (kind == 1 ? 1 : 0) || provider.Queries != (alignment > 0 ? 2 : 1)) return 1;
			handler.AppendLiteral("!");
			if (builder.ToString() != (alignment == 0 ? "seed!" : "seed        !")) return 2;
		}
		return 42;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool HandlerCopyStringSpan(string text, Span<char> destination)
	{
		System.GC.Collect();
		return text.TryCopyTo(destination);
	}

	public static int CoreLibHandlerStringCopyLifetimeEntry()
	{
		var text = new string("\u03A9\0\uD800".AsSpan());
		var array = new char[7];
		for (var i = 0; i < array.Length; i++) array[i] = '#';
		var destination = array.AsSpan(2, 3);
		var guards = array.AsSpan();
		array = null!;
		if (HandlerCopyStringSpan(text, destination.Slice(0, 2))) return 1;
		for (var i = 0; i < guards.Length; i++) if (guards[i] != '#') return 2;
		if (!HandlerCopyStringSpan(text, destination)) return 3;
		System.GC.Collect();
		if (destination[0] != '\u03A9' || destination[1] != '\0' || destination[2] != '\uD800') return 4;
		if (guards[0] != '#' || guards[1] != '#' || guards[5] != '#' || guards[6] != '#') return 5;
		return HandlerCopyStringSpan(string.Empty, destination.Slice(0, 0)) ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int HandlerGenericEnumBranch<T>()
	{
		if (typeof(T).IsEnum) return 17;
		return 29;
	}

	public static int CoreLibHandlerGenericEnumBranchEntry()
	{
		return HandlerGenericEnumBranch<DayOfWeek>() == 17 && HandlerGenericEnumBranch<HandlerSmallEnum>() == 17 &&
			HandlerGenericEnumBranch<int>() == 29 && HandlerGenericEnumBranch<string>() == 29 &&
			HandlerGenericEnumBranch<HandlerSignedEnum[]>() == 29 && HandlerGenericEnumBranch<object>() == 29 ? 42 : 1;
	}
	private enum HandlerSmallEnum { Value }

	private sealed class HandlerGrowingValue : ISpanFormattable
	{
		public int Width;
		public IFormatProvider? Provider;
		public int Calls;
		public readonly int[] Destinations = new int[5];
		public int Invalid;
		public bool TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			if (Calls >= Destinations.Length) throw new InvalidOperationException();
			Destinations[Calls++] = destination.Length;
			if (!ReferenceEquals(provider, Provider)) Invalid = 100 + Calls;
			System.GC.Collect();
			if (Invalid == 0 && !ReferenceEquals(provider, Provider)) Invalid = 200 + Calls;
			if (format.Length != 1 || format[0] != 'W') Invalid = 2;
			charsWritten = 0;
			if (destination.Length < Width)
			{
				if (destination.Length > 0) destination[0] = '!';
				return false;
			}
			for (var i = 0; i < Width; i++) destination[i] = i % 3 == 0 ? '\u03A9' : i % 3 == 1 ? '\0' : '\uD800';
			charsWritten = Width;
			return true;
		}
		public string ToString(string? format, IFormatProvider? provider) => throw new InvalidOperationException();
		public override string ToString() => throw new InvalidOperationException();
	}

	public static int CoreLibStringBuilderInterpolatedGrowthEntry()
	{
		for (var large = 0; large < 2; large++)
		for (var widthIndex = 0; widthIndex < 3; widthIndex++)
		for (var alignIndex = 0; alignIndex < 3; alignIndex++)
		{
			var width = widthIndex == 0 ? 3 : widthIndex == 1 ? 300 : 700;
			var provider = new InterpolatedDiscoveryProvider();
			var value = new HandlerGrowingValue { Width = width, Provider = provider };
			var builder = new StringBuilder(large == 0 ? 1 : 1024).Append("[");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			if (!ReferenceEquals(value.Provider, provider)) return 40000;
			var alignment = alignIndex == 0 ? 0 : alignIndex == 1 ? -width - 5 : width + 5;
			handler.AppendFormatted(value, alignment, "W");
			var temporary = large == 0 || alignIndex == 2;
			var firstTemporaryCall = alignIndex == 2 ? 0 : 1;
			var expectedCalls = temporary ? firstTemporaryCall + (width <= 256 ? 1 : width <= 512 ? 2 : 3) : 1;
			var caseCode = large * 1000 + widthIndex * 100 + alignIndex * 10;
			if (value.Invalid != 0) return 10000 + caseCode + value.Invalid;
			if (value.Calls != expectedCalls) return 20000 + caseCode + value.Calls;
			if (provider.Queries != (temporary ? 2 : 1)) return 30000 + caseCode + provider.Queries;
			if (alignIndex != 2 && value.Destinations[0] != (large == 0 ? 0 : 1023)) return 20;
			if (temporary)
				for (var i = firstTemporaryCall; i < expectedCalls; i++)
					if (value.Destinations[i] != 256 << (i - firstTemporaryCall)) return 21;
			handler.AppendLiteral("]");
			builder.Append(ref handler);
			System.GC.Collect();
			var text = builder.ToString();
			if (text.Length != width + 2 + (alignIndex == 0 ? 0 : 5) || text[0] != '[' || text[text.Length - 1] != ']') return 22;
			var start = alignIndex == 2 ? 6 : 1;
			for (var i = 0; i < width; i++)
				if (text[start + i] != (i % 3 == 0 ? '\u03A9' : i % 3 == 1 ? '\0' : '\uD800')) return 23;
			if (alignIndex != 0)
				for (var i = 0; i < 5; i++) if (text[(alignIndex == 2 ? 1 : width + 1) + i] != ' ') return 24;
		}
		return 42;
	}

	public static int CoreLibHandlerTemporaryBufferEntry()
	{
		var provider = new InterpolatedDiscoveryProvider();
		var value = new HandlerGrowingValue { Width = 700, Provider = provider };
		Span<char> initial = stackalloc char[4];
		var handler = new DefaultInterpolatedStringHandler(0, 1, provider, initial);
		handler.AppendLiteral("pre");
		handler.AppendFormatted(value, "W");
		if (value.Invalid != 0 || value.Calls != 4 || value.Destinations[0] != 1 || value.Destinations[1] != 253 ||
			value.Destinations[2] != 509 || value.Destinations[3] != 1021) return 1;
		var text = handler.ToStringAndClear();
		System.GC.Collect();
		if (text.Length != 703 || text[0] != 'p' || text[1] != 'r' || text[2] != 'e') return 2;
		for (var i = 0; i < 700; i++) if (text[i + 3] != (i % 3 == 0 ? '\u03A9' : i % 3 == 1 ? '\0' : '\uD800')) return 3;
		value.Width = 3; value.Calls = 0;
		handler.AppendLiteral("again");
		handler.AppendFormatted(value, "W");
		return value.Invalid == 0 && value.Calls == 1 && value.Destinations[0] == 251 && provider.Queries == 1 &&
			handler.ToStringAndClear() == "again\u03A9\0\uD800" ? 42 : 4;
	}

	public static int CoreLibStringBuilderInterpolatedGenericAllocationEntry()
	{
		var provider = new InterpolatedDiscoveryProvider();
		var value = new HandlerGrowingValue { Width = 700, Provider = provider };
		var roomy = new StringBuilder(1024).Append("seed");
		var fast = new StringBuilder.AppendInterpolatedStringHandler(0, 1, roomy, provider);
		SetStringBuilderAllocationFailure(1);
		fast.AppendFormatted(value, "W");
		SetStringBuilderAllocationFailure(0);
		if (value.Calls != 1 || value.Invalid != 0 || provider.Queries != 1 || roomy.Length != 704) return 1;
		for (var failure = 1; failure <= 4; failure++)
		{
			provider.Queries = 0; value.Calls = 0;
			var builder = new StringBuilder(failure < 3 ? 1024 : 1).Append("[");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder, provider);
			SetStringBuilderAllocationFailure(failure);
			try { handler.AppendFormatted(value, 705, "W"); SetStringBuilderAllocationFailure(0); return 2; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.Length != 1 || builder.ToString() != "[" || provider.Queries != 2 || value.Invalid != 0 ||
				value.Calls != (failure <= 2 ? failure : 3)) return 3;
			value.Calls = 0;
			handler.AppendFormatted(value, 705, "W");
			if (builder.Length != 706 || builder[0] != '[' || builder[5] != ' ' || builder[6] != '\u03A9' ||
				provider.Queries != 3 || value.Invalid != 0 || value.Calls != 3) return 4;
		}
		return 42;
	}
	private class HandlerTypeBase { }
	private sealed class HandlerTypeDerived : HandlerTypeBase { }
	private sealed class HandlerTypeGeneric<T> { }
	private struct HandlerBoxedValue { public int Value; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Type HandlerObjectType(object value) => value.GetType();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HandlerObjectIsType(object value) => value is Type;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object HandlerCreatePlainObject() => new object();

	public static int PlainObjectConstructionEntry()
	{
		var first = HandlerCreatePlainObject();
		var second = HandlerCreatePlainObject();
		M68kRuntime.Collect();
		return first != null && second != null && !ReferenceEquals(first, second) ? 42 : 1;
	}

	public static int PlainObjectAllocationFailureEntry()
	{
		SetStringBuilderAllocationFailure(1);
		try { HandlerCreatePlainObject(); SetStringBuilderAllocationFailure(0); return 1; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		return HandlerCreatePlainObject() != null ? 42 : 2;
	}

	public static int CoreLibHandlerObjectTypeIdentityEntry()
	{
		object[] values = [new object(), new HandlerTypeBase(), new HandlerTypeDerived(),
			new HandlerTypeGeneric<int>(), new HandlerTypeGeneric<string>(), "literal", new string("xxx".AsSpan()),
			new int[2], new HandlerTypeDerived[1], new object[1], 123, ulong.MaxValue,
			HandlerSignedEnum.Low, HandlerUnsignedEnum.High, DayOfWeek.Friday, new HandlerBoxedValue { Value = 7 },
			new Func<int>(CoreLibHandlerTypeClassificationEntry), typeof(int), (int?)123];
		Type[] expected = [typeof(object), typeof(HandlerTypeBase), typeof(HandlerTypeDerived),
			typeof(HandlerTypeGeneric<int>), typeof(HandlerTypeGeneric<string>), typeof(string), typeof(string),
			typeof(int[]), typeof(HandlerTypeDerived[]), typeof(object[]), typeof(int), typeof(ulong),
			typeof(HandlerSignedEnum), typeof(HandlerUnsignedEnum), typeof(DayOfWeek), typeof(HandlerBoxedValue),
			typeof(Func<int>), typeof(string).GetType(), typeof(int)];
		for (var pass = 0; pass < 3; pass++)
		{
			System.GC.Collect();
			for (var i = 0; i < values.Length; i++)
				if (HandlerObjectType(values[i]) != expected[i]) return 100 + i;
		}
		if (HandlerObjectType(typeof(int)) == typeof(Type) || HandlerObjectType(typeof(int)).IsEnum ||
			!HandlerObjectIsType(typeof(int)) || HandlerObjectIsType(values[0])) return 2;
		try { HandlerObjectType(null!); return 3; }
		catch (NullReferenceException error)
		{
			if (HandlerObjectType(error) != typeof(NullReferenceException)) return 4;
		}
		return 42;
	}

	private sealed class InterpolatedDiscoveryProvider : IFormatProvider, ICustomFormatter
	{
		public int Queries;
		public bool Custom;
		public bool Throw;
		public object? GetFormat(Type? formatType)
		{
			Queries++;
			System.GC.Collect();
			if (formatType != typeof(ICustomFormatter)) throw new InvalidOperationException();
			if (Throw) throw new InvalidOperationException();
			return Custom ? this : null;
		}
		public string Format(string? format, object? value, IFormatProvider? provider) => throw new InvalidOperationException();
	}

	public static int CoreLibStringBuilderInterpolatedProviderDiscoveryEntry()
	{
		for (var custom = 0; custom < 2; custom++)
		for (var consume = 0; consume < 4; consume++)
		{
			var provider = new InterpolatedDiscoveryProvider { Custom = custom != 0 };
			var consumingProvider = new InterpolatedDiscoveryProvider { Throw = true };
			var builder = new StringBuilder(1);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, provider);
			if (provider.Queries != 1) return 1;
			handler.AppendLiteral("[");
			handler.AppendFormatted("\u03A9\0\uD800".AsSpan(), 5);
			handler.AppendLiteral("]");
			var result = consume switch
			{
				0 => builder.Append(ref handler),
				1 => builder.Append(consumingProvider, ref handler),
				2 => builder.AppendLine(ref handler),
				_ => builder.AppendLine(consumingProvider, ref handler)
			};
			System.GC.Collect();
			var expected = consume < 2 ? "[  \u03A9\0\uD800]" : "[  \u03A9\0\uD800]" + Environment.NewLine;
			if (provider.Queries != 1 || consumingProvider.Queries != 0 || !ReferenceEquals(result, builder) ||
				builder.ToString(0, builder.Length) != expected) return 2;
		}
		var throwing = new InterpolatedDiscoveryProvider { Throw = true };
		var seed = new StringBuilder(4).Append("seed");
		try { var unused = new StringBuilder.AppendInterpolatedStringHandler(0, 0, seed, throwing); return 3; }
		catch (InvalidOperationException) { }
		if (throwing.Queries != 1 || seed.ToString(0, seed.Length) != "seed") return 4;
		var retry = new StringBuilder.AppendInterpolatedStringHandler(-1, -1, seed, null);
		retry.AppendLiteral("!");
		return seed.ToString(0, seed.Length) == "seed!" ? 42 : 5;
	}

	private enum HandlerSignedEnum : long { Low = long.MinValue, High = long.MaxValue }
	private enum HandlerUnsignedEnum : ulong { High = ulong.MaxValue }
	private struct HandlerValue { }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HandlerTypeIsEnum(Type type) => type.IsEnum;

	public static int CoreLibHandlerTypeClassificationEntry()
	{
		if (!HandlerTypeIsEnum(typeof(HandlerSignedEnum)) || !HandlerTypeIsEnum(typeof(HandlerUnsignedEnum)) ||
			!HandlerTypeIsEnum(typeof(DayOfWeek))) return 1;
		if (HandlerTypeIsEnum(typeof(int)) || HandlerTypeIsEnum(typeof(ulong)) || HandlerTypeIsEnum(typeof(string)) ||
			HandlerTypeIsEnum(typeof(HandlerValue)) || HandlerTypeIsEnum(typeof(HandlerSignedEnum[])) ||
			HandlerTypeIsEnum(typeof(HandlerSignedEnum?)) || HandlerTypeIsEnum(typeof(IFormatProvider))) return 2;
		System.GC.Collect();
		if (!HandlerTypeIsEnum(typeof(HandlerSignedEnum)) || HandlerTypeIsEnum(typeof(int))) return 3;
		try { HandlerTypeIsEnum(null!); return 4; } catch (NullReferenceException) { }
		return 42;
	}
	public static int CoreLibStringBuilderInterpolatedTextEntry()
	{
		var text = new string("\u03A9\0\uD800".AsSpan());
		var builder = new StringBuilder(1);
		var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, builder);
		handler.AppendLiteral("[");
		handler.AppendFormatted(text.AsSpan());
		handler.AppendFormatted("x".AsSpan(), -3, "ignored");
		handler.AppendLiteral("]");
		System.GC.Collect();
		if (!ReferenceEquals(builder.Append(ref handler), builder) || builder.ToString() != "[\u03A9\0\uD800x  ]") return 1;
		builder.Clear();
		handler = new StringBuilder.AppendInterpolatedStringHandler(0, 2, builder);
		handler.AppendFormatted(text.AsSpan(), 5);
		handler.AppendFormatted(ReadOnlySpan<char>.Empty, -2);
		if (!ReferenceEquals(builder.AppendLine(ref handler), builder) || builder.ToString() != "  \u03A9\0\uD800  \n") return 2;
		return 42;
	}

	private sealed class UnusedInterpolatedProvider : IFormatProvider
	{
		public int Queries;
		public object? GetFormat(Type? formatType)
		{
			Queries++;
			throw new InvalidOperationException();
		}
	}

	public static int CoreLibStringBuilderInterpolatedSpanMatrixEntry()
	{
		var provider = new UnusedInterpolatedProvider();
		Span<char> frame = stackalloc char[3];
		frame[0] = '\u03A9'; frame[1] = '\0'; frame[2] = '\uD800';
		for (var capacityIndex = 0; capacityIndex < 3; capacityIndex++)
		for (var sourceIndex = 0; sourceIndex < 4; sourceIndex++)
		for (var alignmentIndex = 0; alignmentIndex < 6; alignmentIndex++)
		for (var consume = 0; consume < 4; consume++)
		{
			var builder = new StringBuilder(capacityIndex == 0 ? 1 : capacityIndex == 1 ? 4 : 128);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder);
			var array = new char[5];
			array[0] = '!'; array[1] = '\u03A9'; array[2] = '\0'; array[3] = '\uD800'; array[4] = '?';
			ReadOnlySpan<char> span = sourceIndex switch
			{
				0 => default,
				1 => new string("!\u03A9\0\uD800?".AsSpan()).AsSpan(1, 3),
				2 => array.AsSpan(1, 3),
				_ => frame
			};
			array = null!; // The array-backed span retains its owner by itself.
			System.GC.Collect();
			handler.AppendLiteral("[");
			var alignment = alignmentIndex switch { 0 => 0, 1 => -2, 2 => 2, 3 => -7, 4 => 7, _ => -3 };
			if (alignmentIndex == 0) handler.AppendFormatted(span);
			else handler.AppendFormatted(span, alignment, "invalid-format-is-ignored");
			handler.AppendLiteral("]");
			System.GC.Collect();
			var result = consume switch
			{
				0 => builder.Append(ref handler),
				1 => builder.Append(provider, ref handler),
				2 => builder.AppendLine(ref handler),
				_ => builder.AppendLine(provider, ref handler)
			};
			if (!ReferenceEquals(result, builder) || provider.Queries != 0) return 1;
			var value = sourceIndex == 0 ? "" : "\u03A9\0\uD800";
			var padding = alignmentIndex is 3 or 4 ? sourceIndex == 0 ? "       " : "    " :
				alignmentIndex is 1 or 2 && sourceIndex == 0 ? "  " : alignmentIndex == 5 && sourceIndex == 0 ? "   " : "";
			var expected = alignment > 0 ? "[" + padding + value + "]" : "[" + value + padding + "]";
			if (consume >= 2) expected += Environment.NewLine;
			if (builder.ToString() != expected) return 2;
			builder.Clear().Append("reuse");
			if (builder.ToString() != "reuse") return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static StringBuilder.AppendInterpolatedStringHandler CreateRootedTextHandler(out ReadOnlyMemory<char> storage)
	{
		var builder = new StringBuilder(32);
		builder.Append("seed").Append('?', 20);
		var chunks = builder.GetChunks();
		chunks.MoveNext();
		storage = chunks.Current;
		builder.Length = 4;
		var handler = new StringBuilder.AppendInterpolatedStringHandler(4, 0, builder);
		return handler;
	}

	public static int CoreLibStringBuilderInterpolatedHandlerRootsEntry()
	{
		var handler = CreateRootedTextHandler(out var storage);
		for (var index = 0; index < 4; index++)
			if (storage.Span[index] != "seed"[index]) return 100 + index;
		for (var index = 0; index < 12; index++)
		{
			System.GC.Collect();
			var garbage = new char[128];
			garbage[0] = 'X';
		}
		handler.AppendFormatted("\u03A9\0\uD800".AsSpan(), -6);
		handler.AppendLiteral("tail");
		System.GC.Collect();
		var span = storage.Span;
		for (var index = 0; index < 14; index++)
			if (span[index] != "seed\u03A9\0\uD800   tail"[index]) return 200 + index;
		// The handler writes immediately to its captured receiver; the builder
		// passed when consuming it simply supplies the fluent return value.
		var other = new StringBuilder(1);
		if (!ReferenceEquals(other.Append(ref handler), other) || other.Length != 0) return 1;
		other.AppendLine(ref handler);
		return other.ToString(0, other.Length) == Environment.NewLine ? 42 : 2;
	}

	public static int CoreLibStringBuilderInterpolatedIntegerEntry()
	{
		var builder = new StringBuilder(1);
		var provider = new NumberFormatInfo();
		var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 2, builder, provider);
		handler.AppendLiteral("[");
		handler.AppendFormatted(-123, "D5");
		handler.AppendFormatted(42u, 6, "X4");
		handler.AppendLiteral("]");
		System.GC.Collect();
		if (!ReferenceEquals(builder.Append(provider, ref handler), builder) || builder.ToString() != "[-00123  002A]") return 1;
		return 42;
	}

	public static int CoreLibStringBuilderInterpolatedSpanValidationEntry()
	{
		for (var alignment = -7; alignment <= 7; alignment += 14)
		{
			var builder = new StringBuilder(4, 4).Append("seed");
			var handler = new StringBuilder.AppendInterpolatedStringHandler(-1, -1, builder);
			try { handler.AppendFormatted("abc".AsSpan(), alignment); return 1; }
			catch (ArgumentOutOfRangeException) { }
			if (builder.Length != 4 || builder.Capacity != 4 || builder.ToString(0, 4) != "seed") return 2;
			builder.Clear();
			handler.AppendFormatted(default(ReadOnlySpan<char>), int.MinValue, "ignored");
			handler.AppendLiteral("ok");
			if (builder.ToString(0, builder.Length) != "ok") return 3;
		}
		var overflow = new StringBuilder(16, 16).Append("seed");
		var overflowingHandler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, overflow);
		try { overflowingHandler.AppendFormatted("abc".AsSpan(), int.MinValue); return 4; }
		catch (ArgumentOutOfRangeException) { }
		if (overflow.ToString(0, overflow.Length) != "seedabc") return 5;
		var nullHandler = new StringBuilder.AppendInterpolatedStringHandler(0, 0, null!);
		try { nullHandler.AppendLiteral("x"); return 6; } catch (NullReferenceException) { }
		return 42;
	}

	public static int CoreLibStringBuilderInterpolatedSpanAllocationContractsEntry()
	{
		var provider = new UnusedInterpolatedProvider();
		for (var consume = 0; consume < 4; consume++)
		{
			var builder = new StringBuilder(64).Append("seed");
			SetStringBuilderAllocationFailure(1);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(int.MaxValue, int.MaxValue, builder);
			handler.AppendLiteral("[");
			handler.AppendFormatted("abc".AsSpan(), 6, "ignored");
			handler.AppendFormatted(default(ReadOnlySpan<char>));
			handler.AppendLiteral("]");
			var result = consume switch
			{
				0 => builder.Append(ref handler), 1 => builder.Append(provider, ref handler),
				2 => builder.AppendLine(ref handler), _ => builder.AppendLine(provider, ref handler)
			};
			SetStringBuilderAllocationFailure(0);
			if (!ReferenceEquals(result, builder) || provider.Queries != 0) return 1;
			var expected = consume < 2 ? "seed[   abc]" : "seed[   abc]" + Environment.NewLine;
			if (builder.ToString(0, builder.Length) != expected) return 2;
		}
		for (var operation = 0; operation < 5; operation++)
		for (var failAt = 1; failAt <= (operation is 2 or 3 ? 4 : 2); failAt++)
		{
			var builder = new StringBuilder(4).Append("seed");
			var before = builder.ToString(0, 4);
			var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder);
			SetStringBuilderAllocationFailure(failAt);
			try
			{
				if (operation == 0) handler.AppendLiteral("abc");
				else if (operation == 1) handler.AppendFormatted("abc".AsSpan());
				else if (operation == 2) handler.AppendFormatted("abc".AsSpan(), 6);
				else if (operation == 3) handler.AppendFormatted("abc".AsSpan(), -6);
				else builder.AppendLine(ref handler);
				SetStringBuilderAllocationFailure(0); return 3;
			}
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			var expected = failAt <= 2 ? "seed" : operation == 2 ? "seed   a" : "seedabc ";
			if (builder.ToString(0, builder.Length) != expected || before != "seed") return 4;
			handler.AppendLiteral("!");
			if (builder.ToString(0, builder.Length) != expected + "!") return 5;
			builder.Clear().Append("reuse");
			if (builder.ToString(0, builder.Length) != "reuse") return 6;
		}
		return 42;
	}
}
