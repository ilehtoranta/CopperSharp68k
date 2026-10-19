/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class ParsedFormatProvider : IFormatProvider, ICustomFormatter
	{
		public int Queries, Calls, NumericQueries, Kind, Mode, ThrowQuery, ThrowCall;
		public bool Valid = true;
		public string? Expected;
		public InvalidOperationException? Error;
		public object? GetFormat(Type? type)
		{
			System.GC.Collect();
			if (type == typeof(NumberFormatInfo)) { NumericQueries++; return null; }
			if (type != typeof(ICustomFormatter)) { Valid = false; return null; }
			Queries++;
			if (Queries == ThrowQuery) throw Error!;
			return Mode == 2 && Queries > 1 ? null : this;
		}
		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			Calls++;
			System.GC.Collect();
			if (format != "X2" || !ReferenceEquals(provider, this) ||
				(Kind == 0 ? value is not int number || number != 31 : Kind == 1 ? !ReferenceEquals(value, Expected) : value is not null)) Valid = false;
			if (Calls == ThrowCall) throw Error!;
			return Mode == 1 ? null! : new string("C\u03A9\0\uD800".AsSpan());
		}
	}

	private static StringBuilder AppendParsedContract(StringBuilder builder, IFormatProvider provider, CompositeFormat format,
		int form, int kind, object?[] args)
	{
		if (form == 0)
		{
			if (kind == 0) return builder.AppendFormat<int>(provider, format, 31);
			if (kind == 1) return builder.AppendFormat<string?>(provider, format, (string?)args[0]);
			return builder.AppendFormat<object?>(provider, format, null);
		}
		if (form == 1)
		{
			if (kind == 0) return builder.AppendFormat<int, string?>(provider, format, 31, "extra");
			if (kind == 1) return builder.AppendFormat<string?, string?>(provider, format, (string?)args[0], "extra");
			return builder.AppendFormat<object?, string?>(provider, format, null, "extra");
		}
		if (form == 2)
		{
			if (kind == 0) return builder.AppendFormat<int, string?, int>(provider, format, 31, "extra", 9);
			if (kind == 1) return builder.AppendFormat<string?, string?, int>(provider, format, (string?)args[0], "extra", 9);
			return builder.AppendFormat<object?, string?, int>(provider, format, null, "extra", 9);
		}
		return form == 3 ? builder.AppendFormat(provider, format, args) : builder.AppendFormat(provider, format, new ReadOnlySpan<object?>(args));
	}

	public static int CoreLibParsedCompositeFormatProviderContractsEntry()
	{
		var format = CompositeFormat.Parse("[{0,7:X2}|{0,-7:X2}|{0:X2}]");
		for (var form = 0; form < 5; form++)
		for (var kind = 0; kind < 3; kind++)
		for (var mode = 0; mode < 3; mode++)
		for (var roomy = 0; roomy < 2; roomy++)
		{
			var text = new string("s\u03A9\0\uD800".AsSpan());
			var args = new object?[3]; args[0] = kind == 0 ? 31 : kind == 1 ? text : null; args[1] = "extra"; args[2] = 9;
			var provider = new ParsedFormatProvider { Kind = kind, Mode = mode, Expected = text };
			var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append("seed");
			System.GC.Collect();
			if (!ReferenceEquals(AppendParsedContract(builder, provider, format, form, kind, args), builder)) return 1;
			var expected = mode == 0 ? "seed[   C\u03A9\0\uD800|C\u03A9\0\uD800   |C\u03A9\0\uD800]"
				: mode == 1 || kind == 2 ? "seed[       |       |]"
				: kind == 0 ? "seed[     1F|       |]" : "seed[   s\u03A9\0\uD800|       |]";
			System.GC.Collect();
			if (!provider.Valid) return 200 + form * 30 + kind * 10 + mode;
			if (provider.Queries != (mode == 2 ? 4 : 5)) return 10000 + form * 1000 + kind * 100 + mode * 10 + provider.Queries;
			if (provider.Calls != (mode == 2 ? 0 : 3)) return 2000 + provider.Calls;
			if (provider.NumericQueries != 0) return 3000 + kind * 100 + mode * 10 + provider.NumericQueries;
			if (builder.ToString() != expected) return 4000 + form * 30 + kind * 10 + mode;
			var snapshot = builder.ToString();
			builder.Clear(); provider.Mode = 0; provider.Queries = provider.Calls = 0;
			AppendParsedContract(builder, provider, format, form, kind, args);
			if (builder.ToString() != "[   C\u03A9\0\uD800|C\u03A9\0\uD800   |C\u03A9\0\uD800]" || snapshot != expected || !provider.Valid) return 3;
		}
		for (var form = 0; form < 5; form++)
		for (var failure = 1; failure <= 8; failure++)
		{
			var args = new object?[] { 31, "extra", 9 };
			var error = new InvalidOperationException();
			var provider = new ParsedFormatProvider { Error = error, ThrowQuery = failure <= 5 ? failure : 0, ThrowCall = failure > 5 ? failure - 5 : 0 };
			var builder = new StringBuilder(1).Append("seed");
			try { AppendParsedContract(builder, provider, format, form, 0, args); return 4; }
			catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 5; }
			var completed = failure <= 5 ? failure <= 2 ? failure - 1 : failure - 2 : failure - 5;
			var expected = completed == 0 ? "seed" : completed == 1 ? "seed["
				: completed == 2 ? "seed[   C\u03A9\0\uD800|" : "seed[   C\u03A9\0\uD800|C\u03A9\0\uD800   |";
			if (builder.ToString() != expected || !provider.Valid || provider.Queries != (failure <= 5 ? failure : failure - 3) ||
				provider.Calls != (failure <= 5 ? Math.Max(0, failure - 3) : failure - 5)) return 6;
			provider.ThrowQuery = provider.ThrowCall = provider.Queries = provider.Calls = 0;
			builder.Clear(); AppendParsedContract(builder, provider, format, form, 0, args);
			if (builder.ToString() != "[   C\u03A9\0\uD800|C\u03A9\0\uD800   |C\u03A9\0\uD800]" || provider.Queries != 5 || provider.Calls != 3) return 7;
		}
		return 42;
	}

	public static int CoreLibParsedCompositeFormatValidationEntry()
	{
		try { CompositeFormat.Parse(null!); return 1; }
		catch (ArgumentNullException error) { if (error.ParamName != "format") return 2; }
		var invalid = new[] { "{", "}", "{{{", "}}}", "{0", "{0:", "{0,}", "{0,+2}", "{0, -}", "{-1}", "{x}", "{ 0}",
			"{0:{{}", "{0:{x}}", "{0,2x}", "{0 X}", "{0\t}", "{0,2\t}" };
		for (var index = 0; index < invalid.Length; index++)
		{
			try { CompositeFormat.Parse(invalid[index]); return 100 + index; }
			catch (FormatException) { }
		}
		// The pinned parser consumes all digits with Int32 wraparound.
		foreach (var source in new[] { "{4294967296}", "{0,4294967296}", "{0,-4294967296}" })
		{
			var format = CompositeFormat.Parse(source);
			var builder = new StringBuilder(1);
			builder.AppendFormat<int>(new NumberFormatInfo(), format, 31);
			if (format.MinimumArgumentCount != 1 || builder.ToString() != "31") return 14;
		}
		for (var form = 0; form < 5; form++)
		{
			var builder = new StringBuilder(4, 16).Append("seed");
			var provider = new ParsedFormatProvider { ThrowQuery = 1, Error = new InvalidOperationException() };
			try { AppendParsedContract(builder, provider, null!, form, 0, new object?[0]); return 4; }
			catch (ArgumentNullException error) { if (error.ParamName != "format") return 5; }
			if (builder.ToString() != "seed" || builder.Capacity != 4 || provider.Queries != 0) return 6;
		}
		var seed = new StringBuilder(4, 16).Append("seed");
		try { seed.AppendFormat(null, (CompositeFormat)null!, (object?[])null!); return 7; }
		catch (ArgumentNullException error) { if (error.ParamName != "format") return 8; }
		var empty = CompositeFormat.Parse("");
		try { seed.AppendFormat(null, empty, (object?[])null!); return 9; }
		catch (ArgumentNullException error) { if (error.ParamName != "args") return 10; }
		seed.AppendFormat(null, empty, default(ReadOnlySpan<object?>));
		if (seed.ToString() != "seed" || seed.Capacity != 4) return 15;
		var absent = new ParsedFormatProvider { ThrowQuery = 1, Error = new InvalidOperationException() };
		try { seed.AppendFormat(absent, CompositeFormat.Parse("{0}"), default(ReadOnlySpan<object?>)); return 16; }
		catch (FormatException) { if (absent.Queries != 0 || seed.ToString() != "seed") return 17; }
		var formats = new[] { "", "literal", "{{}}", "{0}", "{1}", "{2}", "{3}", "{4}", "{0}|{3}|{0}", "\0\uD800{2}\uFFFF" };
		foreach (var source in formats)
		{
			var format = CompositeFormat.Parse(source);
			var required = source == "" || source == "literal" || source == "{{}}" ? 0 : source == "{0}" ? 1 : source == "{1}" ? 2
				: source == "{2}" || source == "\0\uD800{2}\uFFFF" ? 3 : source == "{4}" ? 5 : 4;
			if (!ReferenceEquals(format.Format, source) || format.MinimumArgumentCount != required) return 11;
			for (var form = 0; form < 5; form++)
			for (var length = 0; length <= 5; length++)
			{
				if (form < 3 && length != 0) continue;
				var count = form < 3 ? form + 1 : length;
				if (count >= required) continue;
				var builder = new StringBuilder(4, 16).Append("seed");
				var provider = new ParsedFormatProvider { ThrowQuery = 1, Error = new InvalidOperationException() };
				try
				{
					AppendParsedContract(builder, provider, format, form, 0, new object?[length]); return 12;
				}
				catch (FormatException) { }
				if (builder.ToString() != "seed" || builder.Capacity != 4 || provider.Queries != 0) return 13;
			}
		}
		return 42;
	}

	public static int CoreLibParsedCompositeFormatParseAllocationFailuresEntry()
	{
		var source = new StringBuilder(1).Append('q', 513).Append("{{}}|{3}|{0:X2}|{1,-4}|{2}|{0:X2}").ToString();
		var provider = new NumberFormatInfo();
		var args = new object?[] { 31, "x", null, 6 };
		CompositeFormat.Parse(source); // Initialize the exact List/empty-array construction before arming faults.
		var failures = 0;
		for (var failAt = 1; failAt <= 64; failAt++)
		{
			CompositeFormat? parsed = null;
			var builder = new StringBuilder(1024).Append("seed");
			SetStringBuilderAllocationFailure(failAt);
			try { parsed = CompositeFormat.Parse(source); SetStringBuilderAllocationFailure(0); }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); failures++; }
			if (source.Length != 546 || source[512] != 'q' || builder.ToString() != "seed" || builder.Capacity != 1024) return 1;
			var retry = CompositeFormat.Parse(source);
			if (!ReferenceEquals(retry.Format, source) || retry.MinimumArgumentCount != 4) return 2;
			builder.Clear(); builder.AppendFormat(provider, retry, args);
			if (builder.Length != 529 || builder.ToString(513, 16) != "{}|6|1F|x   ||1F") return 3;
			if (parsed is not null)
			{
				if (failures == 0 || parsed.MinimumArgumentCount != 4) return 4;
				builder.Clear(); builder.AppendFormat(provider, parsed, args);
				return builder.Length == 529 && builder.ToString(513, 16) == "{}|6|1F|x   ||1F" ? 42 : 5;
			}
		}
		return 6;
	}

	public static int CoreLibParsedCompositeFormatAppendAllocationFailuresEntry()
	{
		var format = CompositeFormat.Parse("[{0,7:X2}|{0,-7:X2}|{0:X2}]");
		var expected = "seed[   C\u03A9\0\uD800|C\u03A9\0\uD800   |C\u03A9\0\uD800]";
		for (var form = 0; form < 5; form++)
		for (var roomy = 0; roomy < 2; roomy++)
		{
			var failures = 0;
			var succeeded = false;
			for (var failAt = 1; failAt <= 64; failAt++)
			{
				var args = new object?[] { 31, "extra", 9 };
				var provider = new ParsedFormatProvider();
				var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append("seed");
				var snapshot = builder.ToString();
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					AppendParsedContract(builder, provider, format, form, 0, args);
					SetStringBuilderAllocationFailure(0); succeeded = true;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); failures++; }
				if (snapshot != "seed" || !provider.Valid || provider.NumericQueries != 0 || format.MinimumArgumentCount != 1 ||
					(int)args[0]! != 31 || (string?)args[1] != "extra" || (int)args[2]! != 9) return 1;
				if (builder.Length < 4 || builder.Length > expected.Length) return 2;
				for (var index = 0; index < builder.Length; index++) if (builder[index] != expected[index]) return 3;
				if (succeeded && (builder.ToString() != expected || failures < 3)) return 4;
				provider.Queries = provider.Calls = 0;
				builder.Clear(); AppendParsedContract(builder, provider, format, form, 0, args);
				if (builder.ToString() != "[   C\u03A9\0\uD800|C\u03A9\0\uD800   |C\u03A9\0\uD800]" || provider.Queries != 5 || provider.Calls != 3) return 5;
				if (succeeded) break;
			}
			if (!succeeded) return 6;
		}
		return 42;
	}

	public static int CoreLibParsedCompositeFormatCapacityFailuresEntry()
	{
		var format = CompositeFormat.Parse("[{0,7:X2}|{0,-7:X2}|{0:X2}]");
		var shortFormat = CompositeFormat.Parse("{0:X2}");
		var expected = "seed[   C\u03A9\0\uD800|C\u03A9\0\uD800   |C\u03A9\0\uD800]";
		for (var form = 0; form < 5; form++)
		for (var limit = 8; limit <= 16; limit += 8)
		for (var roomy = 0; roomy < 2; roomy++)
		{
			var args = new object?[] { 31, "extra", 9 };
			var provider = new ParsedFormatProvider();
			var builder = new StringBuilder(roomy == 0 ? 1 : limit, limit).Append("seed");
			var snapshot = builder.ToString();
			try { AppendParsedContract(builder, provider, format, form, 0, args); return 1; }
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 2; }
			var prefixLength = limit == 8 ? 8 : 13;
			if (builder.Length != prefixLength || builder.Capacity != limit || builder.MaxCapacity != limit || snapshot != "seed" || !provider.Valid) return 3;
			if (provider.Queries != (limit == 8 ? 3 : 4) || provider.Calls != (limit == 8 ? 1 : 2) || provider.NumericQueries != 0) return 6;
			for (var index = 0; index < prefixLength; index++) if (builder[index] != expected[index]) return 4;
			provider.Queries = provider.Calls = 0;
			builder.Clear(); AppendParsedContract(builder, provider, shortFormat, form, 0, args);
			if (builder.ToString() != "C\u03A9\0\uD800" || provider.Queries != 2 || provider.Calls != 1) return 5;
		}
		return 42;
	}
}
