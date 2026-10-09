/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class ManagedRuntimeShadowTests
{
	[Fact]
	public void StandardIntegerFormatsMatchCoreLibAcrossWidthsAndGuardedSpans()
	{
		object[] values = [sbyte.MinValue, (sbyte)-1, (sbyte)0, sbyte.MaxValue, (byte)0, byte.MaxValue,
			short.MinValue, (short)-1, (short)0, short.MaxValue, (ushort)0, ushort.MaxValue,
			int.MinValue, -1, 0, int.MaxValue, 0u, uint.MaxValue,
			long.MinValue, -1L, 0L, long.MaxValue, 0UL, ulong.MaxValue];
		string[] formats = ["D", "d0", "D3", "d22", "D65", "X", "x0", "X3", "x20", "X65", "B", "b3", "B70", "G", "g0", "D000000000000000003", "X008\0ignored", "\0ignored"];
		foreach (var value in values)
		foreach (var format in formats)
		{
			var expected = ((IFormattable)value).ToString(format, System.Globalization.CultureInfo.InvariantCulture);
			foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
			{
				var storage = Enumerable.Repeat('#', size + 2).ToArray();
				var success = TryStandardInteger(value, format, storage.AsSpan(1, size), out var written);
				Assert.Equal(size >= expected.Length, success);
				Assert.Equal(success ? expected.Length : 0, written);
				Assert.Equal('#', storage[0]); Assert.Equal('#', storage[^1]);
				Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(storage, 1, size));
			}
		}
		Assert.False(ShadowNumberFormatting.TryFormatInt32(-1, -1, "D999999999", null, new char[1], out var count));
		Assert.Equal(0, count);
		foreach (var format in new[] { "Z", "D1000000000", "X9999999999", "B2147483648" })
		{
			Assert.Throws<FormatException>(() => 42.ToString(format));
			Assert.Throws<FormatException>(() => ShadowNumberFormatting.TryFormatInt32(42, -1, format, null, new char[1], out _));
		}
		var provider = System.Globalization.CultureInfo.InvariantCulture;
		Assert.True(ShadowNumberFormatting.TryFormatInt32(-1, 255, "X2", provider, new char[2], out count));
		Assert.Equal(2, count);
	}

	private static bool TryStandardInteger(object value, ReadOnlySpan<char> format, Span<char> destination, out int written, IFormatProvider? provider = null) => value switch
	{
		sbyte number => ShadowNumberFormatting.TryFormatInt32(number, 255, format, provider, destination, out written),
		byte number => ShadowNumberFormatting.TryFormatUInt32(number, format, provider, destination, out written),
		short number => ShadowNumberFormatting.TryFormatInt32(number, 65535, format, provider, destination, out written),
		ushort number => ShadowNumberFormatting.TryFormatUInt32(number, format, provider, destination, out written),
		int number => ShadowNumberFormatting.TryFormatInt32(number, -1, format, provider, destination, out written),
		uint number => ShadowNumberFormatting.TryFormatUInt32(number, format, provider, destination, out written),
		long number => ShadowNumberFormatting.TryFormatInt64(number, format, provider, destination, out written),
		ulong number => ShadowNumberFormatting.TryFormatUInt64(number, format, provider, destination, out written),
		_ => throw new InvalidOperationException()
	};

	[Fact]
	public void DecimalProviderSignsAndQueryBehaviorMatchCoreLib()
	{
		object[] values = [sbyte.MinValue, short.MinValue, int.MinValue, long.MinValue, (sbyte)0, (short)12, 42, 42L, byte.MaxValue, ushort.MaxValue, uint.MaxValue, ulong.MaxValue];
		foreach (var sign in new[] { "-", "", "minus", "\u2212\0\u03A9", "\uD83D\uDE00", "\uD800" })
		foreach (var value in values)
		foreach (var format in new[] { "", "D", "D24", "g0", "X", "B" })
		{
			var info = new System.Globalization.NumberFormatInfo { NegativeSign = sign };
			var expected = ((IFormattable)value).ToString(format, info);
			foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
			{
				var actual = Enumerable.Repeat('#', size + 2).ToArray();
				var success = TryStandardInteger(value, format, actual.AsSpan(1, size), out var written, info);
				Assert.Equal(size >= expected.Length, success); Assert.Equal(success ? expected.Length : 0, written);
				Assert.Equal('#', actual[0]); Assert.Equal('#', actual[^1]);
				Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(actual, 1, size));
			}
		}
		// The target's ambient default remains invariant. Keep the host oracle
		// for providers without NumberFormatInfo independent of the machine locale.
		var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			foreach (var result in new object?[] { null, "wrong", new System.Globalization.NumberFormatInfo { NegativeSign = "minus" } })
			{
				var provider = new RecordingNumberProvider { Result = result };
				var expected = (-12).ToString("D4", provider); provider.Queries = 0;
				var storage = new char[expected.Length];
				Assert.True(ShadowNumberFormatting.TryFormatInt32(-12, -1, "D4", provider, storage, out var count));
				Assert.Equal(expected, new string(storage)); Assert.Equal(expected.Length, count); Assert.Equal(1, provider.Queries);
				provider.Queries = 0; provider.Throw = true;
				Assert.True(ShadowNumberFormatting.TryFormatInt32(12, -1, "D4", provider, new char[4], out _));
				Assert.True(ShadowNumberFormatting.TryFormatUInt64(12, "D4", provider, new char[4], out _));
				Assert.True(ShadowNumberFormatting.TryFormatInt32(-1, 255, "X2", provider, new char[2], out _));
				Assert.Equal(0, provider.Queries);
				Assert.Throws<InvalidOperationException>(() => ShadowNumberFormatting.TryFormatInt32(-12, -1, default, provider, new char[4], out _));
			}
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
	}

	[Fact]
	public void FixedPointIntegerFormatsMatchCoreLibProvidersAndGuardedSpans()
	{
		object[] values = [sbyte.MinValue, (sbyte)-1, (sbyte)0, sbyte.MaxValue, (byte)0, byte.MaxValue,
			short.MinValue, (short)-1, (short)0, short.MaxValue, (ushort)0, ushort.MaxValue,
			int.MinValue, -1, 0, int.MaxValue, 0u, uint.MaxValue,
			long.MinValue, -1L, 0L, long.MaxValue, 0UL, ulong.MaxValue];
		foreach (var (sign, separator, digits) in new[] { ("-", ".", 2), ("", "::", 0), ("minus", ",", 3),
			("\u2212\0\u03A9", "\0\uD83D\uDE00", 6), ("\uD800", "\uDFFF", 99) })
		foreach (var value in values)
		foreach (var format in new[] { "F", "f", "F0", "f1", "F3", "F65", "F000000000000000003", "F\0ignored", "f3\0ignored" })
		{
			var info = new System.Globalization.NumberFormatInfo
				{ NegativeSign = sign, NumberDecimalSeparator = separator, NumberDecimalDigits = digits, NumberNegativePattern = 0 };
			var expected = ((IFormattable)value).ToString(format, info);
			foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
			{
				var storage = Enumerable.Repeat('#', size + 2).ToArray();
				var provider = new RecordingNumberProvider { Result = info };
				var success = TryStandardInteger(value, format, storage.AsSpan(1, size), out var written, provider);
				Assert.Equal(size >= expected.Length, success); Assert.Equal(success ? expected.Length : 0, written);
				Assert.Equal(1, provider.Queries);
				Assert.Equal('#', storage[0]); Assert.Equal('#', storage[^1]);
				Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(storage, 1, size));
			}
		}
		var previousCulture = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			foreach (var result in new object?[] { null, "wrong" })
			foreach (var value in new object[] { -12, 0, 12u, long.MinValue, ulong.MaxValue })
			foreach (var format in new[] { "F", "F0", "F2" })
			{
				var provider = new RecordingNumberProvider { Result = result };
				var expected = ((IFormattable)value).ToString(format, provider); provider.Queries = 0;
				var storage = new char[expected.Length];
				Assert.True(TryStandardInteger(value, format, storage, out var written, provider));
				Assert.Equal(expected, new string(storage)); Assert.Equal(expected.Length, written); Assert.Equal(1, provider.Queries);
			}
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = previousCulture; }
		var throwing = new RecordingNumberProvider { Throw = true };
		var hostWritten = 37;
		Assert.Throws<InvalidOperationException>(() => 0u.TryFormat(Span<char>.Empty, out hostWritten, "F0", throwing));
		var actualWritten = 37;
		Assert.Throws<InvalidOperationException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "F0", throwing, Span<char>.Empty, out actualWritten));
		Assert.Equal(hostWritten, actualWritten);
		throwing.Queries = 0;
		Assert.Throws<FormatException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "F1000000000", throwing, Span<char>.Empty, out _));
		Assert.Equal(0, throwing.Queries);
		var guard = new[] { '#' };
		Assert.False(ShadowNumberFormatting.TryFormatInt64(long.MinValue, "F999999999", null, guard, out var count));
		Assert.Equal(0, count); Assert.Equal('#', guard[0]);
	}
	[Fact]
	public void ScientificAndGeneralIntegerFormatsMatchCoreLibRoundingAndProviders()
	{
		object[] values = [sbyte.MinValue, (sbyte)-1, (sbyte)0, sbyte.MaxValue, (byte)0, byte.MaxValue,
			short.MinValue, (short)-1, (short)0, short.MaxValue, (ushort)0, ushort.MaxValue,
			int.MinValue, -1, 0, int.MaxValue, 0u, uint.MaxValue, long.MinValue, -1L, 0L, long.MaxValue, 0UL, ulong.MaxValue,
			25, -25, 250, -250, 1250, -1250, 1499, -1499, 950, -950, 9995, -9995, 1000, -1000,
			9999999999999999999UL, 999999999999999999UL, -999999999999999999L, 10000000000000000000UL];
		foreach (var (sign, separator, plus) in new[] { ("-", ".", "+"), ("", "::", ""), ("minus", ",", "plus"),
			("\u2212\0\u03A9", "\0\uD83D\uDE00", "\uD800\0"), ("\uDFFF", "\uD800", "\uD83D\uDE00") })
		foreach (var value in values)
		foreach (var format in new[] { "E", "e0", "E1", "E2", "e3", "E6", "E19", "e21", "E65", "E\0ignored", "E000000000000000003",
			"G1", "g2", "G3", "g4", "G6", "G19", "g20", "G21", "G65", "G999999999", "R", "r0", "R1", "r2", "R20", "r65", "R\0ignored" })
		{
			var info = new System.Globalization.NumberFormatInfo { NegativeSign = sign, NumberDecimalSeparator = separator,
				PositiveSign = plus, NumberDecimalDigits = 99, NumberNegativePattern = 0 };
			var expected = ((IFormattable)value).ToString(format, info);
			foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
			{
				var storage = Enumerable.Repeat('#', size + 2).ToArray();
				var provider = new RecordingNumberProvider { Result = info };
				var success = TryStandardInteger(value, format, storage.AsSpan(1, size), out var written, provider);
				Assert.Equal(size >= expected.Length, success); Assert.Equal(success ? expected.Length : 0, written);
				Assert.Equal(1, provider.Queries);
				Assert.Equal('#', storage[0]); Assert.Equal('#', storage[^1]);
				Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(storage, 1, size));
			}
		}
		var throwing = new RecordingNumberProvider { Throw = true }; var writtenOnThrow = 37;
		Assert.Throws<InvalidOperationException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "R", throwing, Span<char>.Empty, out writtenOnThrow));
		Assert.Equal(37, writtenOnThrow);
		foreach (var format in new[] { "E1000000000", "G1000000000", "R1000000000" })
		{
			throwing.Queries = 0;
			Assert.Throws<FormatException>(() => ShadowNumberFormatting.TryFormatUInt64(ulong.MaxValue, format, throwing, Span<char>.Empty, out _));
			Assert.Equal(0, throwing.Queries);
		}
		var guard = new[] { '#' };
		Assert.False(ShadowNumberFormatting.TryFormatInt64(long.MinValue, "E999999999", null, guard, out var count));
		Assert.Equal(0, count); Assert.Equal('#', guard[0]);
	}
	[Fact]
	public void GroupedIntegerFormatsMatchCoreLibGroupingPatternsAndGuardedSpans()
	{
		object[] values = [sbyte.MinValue, (sbyte)-1, (sbyte)0, sbyte.MaxValue, (byte)0, byte.MaxValue,
			short.MinValue, (short)-1, (short)0, short.MaxValue, (ushort)0, ushort.MaxValue,
			int.MinValue, -1, 0, int.MaxValue, 0u, uint.MaxValue, long.MinValue, -1L, 0L, long.MaxValue, 0UL, ulong.MaxValue,
			999, -999, 1000, -1000, 10000000000000000000UL];
		int[][] grouping = [[], [0], [3], [3, 2], [3, 2, 0], [1], [1, 0], [9], [9, 1, 2, 0], [2, 3, 4, 5, 6, 7, 8, 9, 0]];
		foreach (var groups in grouping)
		for (var pattern = 0; pattern < 5; pattern++)
		foreach (var (sign, separator, group, digits) in new[] { ("-", ".", ",", 2), ("", "::", "", 0),
			("minus", ",", "group", 3), ("\u2212\0\u03A9", "\uD83D\uDE00", "\0\uD800", 99) })
		foreach (var value in values)
		foreach (var format in new[] { "N", "n0", "N3", "n65", "N\0ignored", "N000000000003" })
		{
			var info = new System.Globalization.NumberFormatInfo { NumberGroupSizes = groups, NumberNegativePattern = pattern,
				NegativeSign = sign, NumberDecimalSeparator = separator, NumberGroupSeparator = group, NumberDecimalDigits = digits };
			var expected = ((IFormattable)value).ToString(format, info);
			foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
			{
				var storage = Enumerable.Repeat('#', size + 2).ToArray();
				var provider = new RecordingNumberProvider { Result = info };
				var success = TryStandardInteger(value, format, storage.AsSpan(1, size), out var written, provider);
				Assert.Equal(size >= expected.Length, success); Assert.Equal(success ? expected.Length : 0, written); Assert.Equal(1, provider.Queries);
				Assert.Equal('#', storage[0]); Assert.Equal('#', storage[^1]);
				Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(storage, 1, size));
			}
		}
		var throwing = new RecordingNumberProvider { Throw = true }; var countOnThrow = 37;
		Assert.Throws<InvalidOperationException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "N0", throwing, Span<char>.Empty, out countOnThrow));
		Assert.Equal(37, countOnThrow);
		throwing.Queries = 0;
		Assert.Throws<FormatException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "N1000000000", throwing, Span<char>.Empty, out _));
		Assert.Equal(0, throwing.Queries);
		var guard = new[] { '#' };
		Assert.False(ShadowNumberFormatting.TryFormatInt64(long.MinValue, "N999999999", null, guard, out var count));
		Assert.Equal(0, count); Assert.Equal('#', guard[0]);
		// Group-size reads must not clone the array.
		var allocationInfo = new System.Globalization.NumberFormatInfo { NumberGroupSizes = [3, 2, 0] };
		var destination = new char[128];
		TryStandardInteger(long.MinValue, "N", destination, out _, allocationInfo);
		object boxed = long.MinValue;
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var index = 0; index < 100; index++) TryStandardInteger(boxed, "N", destination, out _, allocationInfo);
		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
	}

	[Fact]
	public void CurrencyIntegerFormatsMatchCoreLibPatternsAndGuardedSpans()
	{
		object[] values = [sbyte.MinValue, sbyte.MaxValue, byte.MaxValue, short.MinValue, short.MaxValue, ushort.MaxValue,
			int.MinValue, int.MaxValue, uint.MaxValue, long.MinValue, long.MaxValue, ulong.MaxValue, 0, 0u, 0L, 0UL, -1, 999, -1000];
		int[][] grouping = [[], [0], [3], [3, 2], [3, 2, 0], [1], [1, 0], [9]];
		foreach (var groups in grouping)
		for (var pattern = 0; pattern < 17; pattern++)
		foreach (var (sign, separator, group, symbol, digits) in new[] { ("-", ".", ",", "\u00A4", 2),
			("", "::", "", "", 0), ("minus", ",", "group", "$", 3), ("\u2212\0\u03A9", "\uD83D\uDE00", "\0\uD800", "$-#\0\uD800", 99) })
		{
			var info = new System.Globalization.NumberFormatInfo { CurrencyGroupSizes = groups, CurrencyNegativePattern = pattern,
				CurrencyPositivePattern = pattern % 4, NegativeSign = sign, CurrencyDecimalSeparator = separator,
				CurrencyGroupSeparator = group, CurrencySymbol = symbol, CurrencyDecimalDigits = digits,
				NumberDecimalSeparator = "wrong", NumberGroupSeparator = "wrong", NumberDecimalDigits = 7, NumberGroupSizes = [1], NumberNegativePattern = 4 };
			foreach (var value in values)
			foreach (var format in new[] { "C", "c0", "C3", "c65", "C\0ignored", "C000000000003" })
			{
				var expected = ((IFormattable)value).ToString(format, info);
				foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
				{
					var storage = Enumerable.Repeat('#', size + 2).ToArray();
					var provider = new RecordingNumberProvider { Result = info };
					var success = TryStandardInteger(value, format, storage.AsSpan(1, size), out var written, provider);
					Assert.Equal(size >= expected.Length, success); Assert.Equal(success ? expected.Length : 0, written); Assert.Equal(1, provider.Queries);
					Assert.Equal('#', storage[0]); Assert.Equal('#', storage[^1]);
					Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(storage, 1, size));
				}
			}
		}
		foreach (var result in new object?[] { null, new object() })
		foreach (var value in new object[] { -1000, 0u, ulong.MaxValue })
		{
			var provider = new RecordingNumberProvider { Result = result };
			var storage = new char[128];
			Assert.True(TryStandardInteger(value, "C", storage, out var written, provider));
			Assert.Equal(((IFormattable)value).ToString("C", System.Globalization.CultureInfo.CurrentCulture), new string(storage, 0, written));
			Assert.Equal(1, provider.Queries);
		}
		var throwing = new RecordingNumberProvider { Throw = true }; var countOnThrow = 37;
		Assert.Throws<InvalidOperationException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "C0", throwing, Span<char>.Empty, out countOnThrow));
		Assert.Equal(37, countOnThrow);
		throwing.Queries = 0;
		Assert.Throws<FormatException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "C1000000000", throwing, Span<char>.Empty, out _));
		Assert.Equal(0, throwing.Queries);
		var guard = new[] { '#' };
		Assert.False(ShadowNumberFormatting.TryFormatInt64(long.MinValue, "C999999999", null, guard, out var count));
		Assert.Equal(0, count); Assert.Equal('#', guard[0]);
		var allocationInfo = new System.Globalization.NumberFormatInfo { CurrencyGroupSizes = [3, 2, 0] };
		var destination = new char[128]; object boxed = long.MinValue;
		TryStandardInteger(boxed, "C", destination, out _, allocationInfo);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var index = 0; index < 100; index++) TryStandardInteger(boxed, "C", destination, out _, allocationInfo);
		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
	}

	[Fact]
	public void PercentIntegerFormatsMatchCoreLibPatternsAndGuardedSpans()
	{
		object[] values = [sbyte.MinValue, sbyte.MaxValue, byte.MaxValue, short.MinValue, short.MaxValue, ushort.MaxValue,
			int.MinValue, int.MaxValue, uint.MaxValue, long.MinValue, long.MaxValue, ulong.MaxValue, 0, 0u, 0L, 0UL, -1, 999, -1000];
		int[][] grouping = [[], [0], [3], [3, 2], [3, 2, 0], [1], [1, 0], [9]];
		foreach (var groups in grouping)
		for (var pattern = 0; pattern < 12; pattern++)
		foreach (var (sign, separator, group, symbol, digits) in new[] { ("-", ".", ",", "%", 2),
			("", "::", "", "", 0), ("minus", ",", "group", "%", 3), ("\u2212\0\u03A9", "\uD83D\uDE00", "\0\uD800", "%-#\0\uD800", 99) })
		{
			var info = new System.Globalization.NumberFormatInfo { PercentGroupSizes = groups, PercentNegativePattern = pattern,
				PercentPositivePattern = pattern % 4, NegativeSign = sign, PercentDecimalSeparator = separator,
				PercentGroupSeparator = group, PercentSymbol = symbol, PercentDecimalDigits = digits,
				NumberDecimalSeparator = "wrong", NumberGroupSeparator = "wrong", NumberDecimalDigits = 7, NumberGroupSizes = [1], NumberNegativePattern = 4, CurrencySymbol = "wrong", CurrencyGroupSizes = [1], CurrencyGroupSeparator = "wrong", CurrencyDecimalSeparator = "wrong", CurrencyDecimalDigits = 9, CurrencyNegativePattern = 16 };
			foreach (var value in values)
			foreach (var format in new[] { "P", "p0", "P3", "p65", "P\0ignored", "P000000000003" })
			{
				var expected = ((IFormattable)value).ToString(format, info);
				foreach (var size in new[] { 0, expected.Length - 1, expected.Length, expected.Length + 1 })
				{
					var storage = Enumerable.Repeat('#', size + 2).ToArray();
					var provider = new RecordingNumberProvider { Result = info };
					var success = TryStandardInteger(value, format, storage.AsSpan(1, size), out var written, provider);
					Assert.Equal(size >= expected.Length, success); Assert.Equal(success ? expected.Length : 0, written); Assert.Equal(1, provider.Queries);
					Assert.Equal('#', storage[0]); Assert.Equal('#', storage[^1]);
					Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(storage, 1, size));
				}
			}
		}
		foreach (var result in new object?[] { null, new object() })
		foreach (var value in new object[] { -1000, 0u, ulong.MaxValue })
		{
			var provider = new RecordingNumberProvider { Result = result };
			var storage = new char[128];
			Assert.True(TryStandardInteger(value, "P", storage, out var written, provider));
			Assert.Equal(((IFormattable)value).ToString("P", System.Globalization.CultureInfo.CurrentCulture), new string(storage, 0, written));
			Assert.Equal(1, provider.Queries);
		}
		var throwing = new RecordingNumberProvider { Throw = true }; var countOnThrow = 37;
		Assert.Throws<InvalidOperationException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "P0", throwing, Span<char>.Empty, out countOnThrow));
		Assert.Equal(37, countOnThrow);
		throwing.Queries = 0;
		Assert.Throws<FormatException>(() => ShadowNumberFormatting.TryFormatUInt32(0, "P1000000000", throwing, Span<char>.Empty, out _));
		Assert.Equal(0, throwing.Queries);
		var guard = new[] { '#' };
		Assert.False(ShadowNumberFormatting.TryFormatInt64(long.MinValue, "P999999999", null, guard, out var count));
		Assert.Equal(0, count); Assert.Equal('#', guard[0]);
		var allocationInfo = new System.Globalization.NumberFormatInfo { PercentGroupSizes = [3, 2, 0] };
		var destination = new char[128]; object boxed = long.MinValue;
		TryStandardInteger(boxed, "P", destination, out _, allocationInfo);
		var before = GC.GetAllocatedBytesForCurrentThread();
		for (var index = 0; index < 100; index++) TryStandardInteger(boxed, "P", destination, out _, allocationInfo);
		Assert.Equal(before, GC.GetAllocatedBytesForCurrentThread());
	}

	private sealed class RecordingNumberProvider : IFormatProvider
	{
		public object? Result;
		public int Queries;
		public bool Throw;
		public object? GetFormat(Type? formatType)
		{
			Assert.Equal(typeof(System.Globalization.NumberFormatInfo), formatType);
			Queries++;
			if (Throw) throw new InvalidOperationException();
			return Result;
		}
	}

	[Fact]
	public void CoreLibTwoCharacterSearchMatchesHostSlices()
	{
		for (var length = 0; length <= 65; length++)
		for (var offset = 0; offset <= 3; offset++)
		{
			var storage = Enumerable.Repeat('#', length + offset + 2).ToArray();
			for (var index = 0; index < length; index++) storage[index + offset] = "ab\0\u03A9\uD800{}"[index % 7];
			var original = storage.ToArray();
			ReadOnlySpan<char> span = storage.AsSpan(offset, length);
			foreach (var (first, second) in new[] { ('{', '}'), ('}', '{'), ('\0', '\uD800'), ('#', '#'), ('a', 'a'), ('z', '\uFFFF') })
				Assert.Equal(span.IndexOfAny(first, second), ShadowCharacterSpans.IndexOfAny(span, first, second));
			Assert.Equal(original, storage);
		}
		Assert.Equal(-1, ShadowCharacterSpans.IndexOfAny(default, '\0', '\0'));
	}

	[Theory]
	[InlineData("pre{", "seedpre")]
	[InlineData("pre}", "seedpre")]
	[InlineData("pre{2}", "seedpre")]
	[InlineData("pre{0}mid{1", "seedpreamid")]
	[InlineData("pre{0,+2}", "seedpre")]
	[InlineData("pre{-1}", "seedpre")]
	[InlineData("pre{0:{{}}}", "seedpre")]
	public void CoreLibCompositeFormatFailurePrefixMatchesHost(string format, string expected)
	{
		var builder = new System.Text.StringBuilder(1).Append("seed");
		Assert.Throws<FormatException>(() => builder.AppendFormat(format, "a", "b"));
		Assert.Equal(expected, builder.ToString());
		builder.Clear().Append("seed");
		Assert.Equal("format", Assert.Throws<ArgumentNullException>(() => builder.AppendFormat((string)null!, "a")).ParamName);
		Assert.Equal("seed", builder.ToString());
	}

	[Fact]
	public void ExperimentalInt32CopiesAndPrimitiveClearsMatchHostArrayContracts()
	{
		for (var sourceIndex = 0; sourceIndex <= 8; sourceIndex++)
		for (var destinationIndex = 0; destinationIndex <= 8; destinationIndex++)
		for (var length = 0; length <= 8 - sourceIndex && length <= 8 - destinationIndex; length++)
		{
			var expected = Enumerable.Range(0, 8).Select(value => value * 1234567 - 3456).ToArray();
			var actual = expected.ToArray();
			Array.Copy(expected, sourceIndex, expected, destinationIndex, length);
			ShadowArray.CopyInt32(actual, sourceIndex, actual, destinationIndex, length);
			Assert.Equal(expected, actual);
		}
		for (var index = 0; index <= 8; index++)
		for (var length = 0; length <= 8 - index; length++)
		{
			Array[] values = [Enumerable.Repeat((byte)0xA5, 8).ToArray(), Enumerable.Repeat('\uFFFF', 8).ToArray(),
				Enumerable.Range(1, 8).ToArray(), Enumerable.Repeat<object>(new object(), 8).ToArray(), Enumerable.Repeat("keep", 8).ToArray()];
			foreach (var actual in values)
			{
				var expected = (Array)actual.Clone();
				Array.Clear(expected, index, length); ShadowArray.ClearPrimitive(actual, index, length);
				Assert.Equal(expected.Cast<object?>(), actual.Cast<object?>());
			}
		}
		Assert.Throws<NotSupportedException>(() => ShadowArray.CopyInt32(new int[1, 1], 0, new int[1], 0, 0));
		Assert.Throws<NotSupportedException>(() => ShadowArray.ClearPrimitive(new int[1, 1], 0, 0));
	}

	[Fact]
	public void CoreLibOrdinalCharacterEqualityMatchesSlicesWithoutChangingStorage()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uD800\uFFFF";
		foreach (var length in new[] { 0, 1, 4, 16, 32, 65, 257 })
		{
			var left = new char[length + 2];
			var right = new char[length + 4];
			Array.Fill(left, 'L'); Array.Fill(right, 'R');
			for (var index = 0; index < length; index++) left[index + 1] = right[index + 2] = pattern[index % pattern.Length];
			var leftBefore = left.ToArray(); var rightBefore = right.ToArray();
			var first = left.AsSpan(1, length); var second = right.AsSpan(2, length);
			Assert.True(ShadowCharacterSpans.EqualsOrdinal(first, second));
			Assert.True(ShadowCharacterSpans.EqualsOrdinal(first, first));
			Assert.Equal(length == 0, ShadowCharacterSpans.EqualsOrdinal(first, default));
			Assert.False(ShadowCharacterSpans.EqualsOrdinal(first, right.AsSpan(1, length + 1)));
			for (var index = 0; index < length; index++)
			{
				second[index] = 'Z';
				Assert.False(ShadowCharacterSpans.EqualsOrdinal(first, second));
				Assert.False(ShadowCharacterSpans.EqualsOrdinal(second, first));
				second[index] = first[index];
			}
			Assert.Equal(leftBefore, left); Assert.Equal(rightBefore, right);
		}
		Assert.False(ShadowCharacterSpans.EqualsOrdinal("A", "a"));
		Assert.False(ShadowCharacterSpans.EqualsOrdinal("\u00E9", "e\u0301"));
		Assert.False(ShadowCharacterSpans.EqualsOrdinal("\uD83D\uDE00", "\uDE00\uD83D"));
	}

	private static void CheckAmbientCustomSpan(object value, int capacity)
	{
		var expected = ((IFormattable)value).ToString("000.0", null);
		var storage = Enumerable.Repeat('#', capacity + 2).ToArray();
		bool success = TryStandardInteger(value, "000.0", storage.AsSpan(1, capacity), out int written, null);
		Assert.Equal(capacity >= expected.Length, success);
		Assert.Equal(success ? expected.Length : 0, written);
		for (int index = 0; index < storage.Length; index++)
			Assert.Equal(success && index > 0 && index <= written ? expected[index - 1] : '#', storage[index]);
	}

	[Fact]
	public void CoreLibDefault64BitFormattingPreservesSlicesAndSupportsAmbientCustomFormats()
	{
		var values = new HashSet<ulong> { 0, 1, uint.MaxValue, 1UL << 32, (1UL << 32) + 1, long.MaxValue, 1UL << 63, ulong.MaxValue };
		for (ulong power = 10; ; power *= 10)
		{
			values.UnionWith([power - 1, power, power + 1]);
			if (power > ulong.MaxValue / 10) break;
		}
		foreach (var value in values.Where(value => value <= long.MaxValue).ToArray())
			values.Add(unchecked((ulong)-(long)value));
		foreach (var value in values)
		foreach (var signed in new[] { false, true })
		{
			var expected = signed ? unchecked((long)value).ToString(System.Globalization.CultureInfo.InvariantCulture)
				: value.ToString(System.Globalization.CultureInfo.InvariantCulture);
			for (var size = 0; size <= expected.Length + 1; size++)
			{
				var buffer = Enumerable.Repeat('#', size + 2).ToArray();
				var destination = buffer.AsSpan(1, size);
				var success = signed
					? ShadowNumberFormatting.TryFormatInt64(unchecked((long)value), default, null, destination, out var written)
					: ShadowNumberFormatting.TryFormatUInt64(value, default, null, destination, out written);
				Assert.Equal(size >= expected.Length, success);
				Assert.Equal(success ? expected.Length : 0, written);
				Assert.Equal('#', buffer[0]);
				Assert.Equal('#', buffer[^1]);
				Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(destination));
			}
		}
		var provider = System.Globalization.CultureInfo.InvariantCulture;
		CheckAmbientCustomSpan(42L, 20);
		CheckAmbientCustomSpan(42UL, 20);
		var providerBuffer = new char[20];
		Assert.True(ShadowNumberFormatting.TryFormatInt64(42, default, provider, providerBuffer, out var providerWritten));
		Assert.Equal("42", new string(providerBuffer, 0, providerWritten));
		Assert.True(ShadowNumberFormatting.TryFormatUInt64(42, default, provider, providerBuffer, out providerWritten));
		Assert.Equal("42", new string(providerBuffer, 0, providerWritten));
		Assert.Equal(42L.ToString("000.0"), ShadowNumberFormatting.FormatInt64(42, "000.0", null));
		Assert.Equal(42UL.ToString("000.0"), ShadowNumberFormatting.FormatUInt64(42, "000.0", null));
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(9u)]
	[InlineData(10u)]
	[InlineData(99u)]
	[InlineData(100u)]
	[InlineData(999_999_999u)]
	[InlineData(1_000_000_000u)]
	[InlineData(2_147_483_647u)]
	[InlineData(2_147_483_648u)]
	[InlineData(uint.MaxValue)]
	public void CoreLibDefaultUnsignedFormattingUsesExactCapacityWithoutPartialWrites(uint value)
	{
		var expected = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
		for (var size = 0; size <= expected.Length + 1; size++)
		{
			var destination = new char[size];
			Array.Fill(destination, '#');
			var success = ShadowNumberFormatting.TryFormatUInt32(value, default, null, destination, out var written);
			Assert.Equal(size >= expected.Length, success);
			Assert.Equal(success ? expected.Length : 0, written);
			Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(destination));
		}
		CheckAmbientCustomSpan(value, 10);
		var providerBuffer = new char[10];
		Assert.True(ShadowNumberFormatting.TryFormatUInt32(value, default, System.Globalization.CultureInfo.InvariantCulture, providerBuffer, out var providerWritten));
		Assert.Equal(expected, new string(providerBuffer, 0, providerWritten));
		Assert.Equal(value.ToString("000.0"), ShadowNumberFormatting.FormatUInt32(value, "000.0", null));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(9)]
	[InlineData(10)]
	[InlineData(-1)]
	[InlineData(-100)]
	[InlineData(int.MinValue)]
	[InlineData(int.MaxValue)]
	public void CoreLibDefaultIntegerFormattingUsesExactCapacityWithoutPartialWrites(int value)
	{
		var expected = value.ToString(System.Globalization.CultureInfo.InvariantCulture);
		for (var size = 0; size <= expected.Length + 1; size++)
		{
			var destination = new char[size];
			Array.Fill(destination, '#');
			var success = ShadowNumberFormatting.TryFormatInt32(value, -1, default, null, destination, out var written);
			Assert.Equal(size >= expected.Length, success);
			Assert.Equal(success ? expected.Length : 0, written);
			Assert.Equal(success ? expected + new string('#', size - written) : new string('#', size), new string(destination));
		}
		CheckAmbientCustomSpan(value, 12);
		var providerBuffer = new char[12];
		Assert.True(ShadowNumberFormatting.TryFormatInt32(value, -1, default, System.Globalization.CultureInfo.InvariantCulture, providerBuffer, out var providerWritten));
		Assert.Equal(expected, new string(providerBuffer, 0, providerWritten));
	}

	[Theory]
	[InlineData(0L)]
	[InlineData(1L)]
	[InlineData(-1L)]
	[InlineData(864_000_000_000L)]
	[InlineData(-864_000_000_000L)]
	[InlineData(long.MaxValue)]
	[InlineData(long.MinValue)]
	public void ShadowTimeSpanTotalsMatchCoreLib(long ticks)
	{
		var shadow = default(ShadowTimeSpan);
		shadow.Initialize(ticks);
		var expected = TimeSpan.FromTicks(ticks);

		Assert.Equal(
			BitConverter.DoubleToInt64Bits(expected.TotalDays),
			BitConverter.DoubleToInt64Bits(shadow.GetTotalDays()));
		Assert.Equal(
			BitConverter.DoubleToInt64Bits(expected.TotalHours),
			BitConverter.DoubleToInt64Bits(shadow.GetTotalHours()));
		Assert.Equal(
			BitConverter.DoubleToInt64Bits(expected.TotalMinutes),
			BitConverter.DoubleToInt64Bits(shadow.GetTotalMinutes()));
		Assert.Equal(
			BitConverter.DoubleToInt64Bits(expected.TotalSeconds),
			BitConverter.DoubleToInt64Bits(shadow.GetTotalSeconds()));
		Assert.Equal(
			BitConverter.DoubleToInt64Bits(expected.TotalMilliseconds),
			BitConverter.DoubleToInt64Bits(shadow.GetTotalMilliseconds()));
	}

	[Fact]
	public void ShadowStringFormatRejectsOverflowingArgumentIndex()
	{
		Assert.Throws<FormatException>(() =>
			ShadowStringFormat.Format1("{4294967296}", 42));
		Assert.Throws<FormatException>(() =>
			ShadowStringFormat.Format1("{2147483648}", 42));
	}

	[Fact]
	public void ShadowExceptionToStringIsCompactAndDeterministic() =>
		Assert.Equal("System.Exception", new ShadowException().ToString());

	[Fact]
	public void ShadowCultureInfoOmitsDateTimeGlobalization() =>
		Assert.Null(new ShadowCultureInfo().GetDateTimeFormat());

	[Fact]
	public void ShadowInvariantCultureRetainsSharedReadOnlyNumberSettings()
	{
		var culture = ShadowCultureInfo.GetInvariantCulture();
		var info = culture.NumberFormat;
		System.GC.Collect();
		Assert.Same(culture, ShadowCultureInfo.GetInvariantCulture());
		Assert.Same(info, ShadowCultureInfo.GetInvariantCulture().NumberFormat);
		Assert.True(culture.IsReadOnly);
		Assert.True(info.IsReadOnly);
		Assert.Throws<InvalidOperationException>(() => culture.NumberFormat = new System.Globalization.NumberFormatInfo());
		Assert.Throws<InvalidOperationException>(() => info.NegativeSign = "changed");
	}

	[Fact]
	public void ShadowCultureInfoCachesMutableInvariantNumbersAndRejectsUnavailableLocaleData()
	{
		var culture = new ShadowCultureInfo();
		culture.InitializeName("");
		var info = culture.GetNumberFormat();
		Assert.Same(info, culture.GetNumberFormat());
		Assert.False(info.IsReadOnly);
		Assert.Equal("-", info.NegativeSign);
		Assert.Equal(".", info.NumberDecimalSeparator);
		var replacement = new System.Globalization.NumberFormatInfo { NegativeSign = "custom" };
		culture.SetNumberFormat(replacement);
		Assert.Same(replacement, culture.GetNumberFormat());
		Assert.Throws<ArgumentNullException>(() => culture.SetNumberFormat(null!));
		Assert.Same(replacement, culture.GetNumberFormat());
		Assert.Throws<ArgumentNullException>(() => culture.InitializeName(null!));
		Assert.Throws<NotSupportedException>(() => culture.InitializeName("en-US"));
		Assert.Throws<NotSupportedException>(() => culture.InitializeNameWithOverrides("fi-FI", false));
	}

	[Fact]
	public void ShadowSystemResourcesReturnsTheRequestedKey()
	{
		const string key = "Arg_IndexOutOfRangeException";

		Assert.Same(key, ShadowSystemResources.GetResourceString(key));
	}

	[Fact]
	public void ShadowIntegerFormatterPacksInvariantDecimalWithoutAllocation()
	{
		AssertPackedInt32(0, 1, 0x3000_0000, 0, 0);
		AssertPackedInt32(42, 2, 0x3432_0000, 0, 0);
		AssertPackedInt32(-42, 3, 0x2d34_3200, 0, 0);
		AssertPackedInt32(
			int.MinValue,
			11,
			0x2d32_3134,
			0x3734_3833,
			0x3634_3800);
		AssertPackedUInt32(
			uint.MaxValue,
			10,
			0x3432_3934,
			0x3936_3732,
			0x3935_0000);
		AssertPackedInt64(
			0x8000_0000,
			0,
			20,
			0x2d39_3232,
			0x3333_3732,
			0x3033_3638,
			0x3534_3737,
			0x3538_3038);
		AssertPackedUInt64(
			uint.MaxValue,
			uint.MaxValue,
			20,
			0x3138_3434,
			0x3637_3434,
			0x3037_3337,
			0x3039_3535,
			0x3136_3135);
		AssertPackedUInt64(
			0,
			10_000,
			5,
			0x3130_3030,
			0x3000_0000,
			0,
			0,
			0);
		AssertPackedUInt64(
			0x8ac7_2304,
			0x89e7_ffff,
			19,
			0x3939_3939,
			0x3939_3939,
			0x3939_3939,
			0x3939_3939,
			0x3939_3900);
		AssertPackedUInt64(
			0x8ac7_2304,
			0x89e8_0000,
			20,
			0x3130_3030,
			0x3030_3030,
			0x3030_3030,
			0x3030_3030,
			0x3030_3030);
	}

	private static void AssertPackedInt32(
		int value,
		int expectedLength,
		uint expectedWord0,
		uint expectedWord1,
		uint expectedWord2)
	{
		var length = ShadowIntegerFormatter.PackInt32(
			value,
			out var word0,
			out var word1,
			out var word2);
		Assert.Equal(expectedLength, length);
		Assert.Equal(expectedWord0, word0);
		Assert.Equal(expectedWord1, word1);
		Assert.Equal(expectedWord2, word2);
	}

	private static void AssertPackedUInt32(
		uint value,
		int expectedLength,
		uint expectedWord0,
		uint expectedWord1,
		uint expectedWord2)
	{
		var length = ShadowIntegerFormatter.PackUInt32(
			value,
			out var word0,
			out var word1,
			out var word2);
		Assert.Equal(expectedLength, length);
		Assert.Equal(expectedWord0, word0);
		Assert.Equal(expectedWord1, word1);
		Assert.Equal(expectedWord2, word2);
	}

	private static void AssertPackedInt64(
		uint high,
		uint low,
		int expectedLength,
		uint expectedWord0,
		uint expectedWord1,
		uint expectedWord2,
		uint expectedWord3,
		uint expectedWord4)
	{
		var length = ShadowIntegerFormatter.PackInt64(
			high,
			low,
			out var word0,
			out var word1,
			out var word2,
			out var word3,
			out var word4);
		Assert.Equal(expectedLength, length);
		Assert.Equal(expectedWord0, word0);
		Assert.Equal(expectedWord1, word1);
		Assert.Equal(expectedWord2, word2);
		Assert.Equal(expectedWord3, word3);
		Assert.Equal(expectedWord4, word4);
	}

	private static void AssertPackedUInt64(
		uint high,
		uint low,
		int expectedLength,
		uint expectedWord0,
		uint expectedWord1,
		uint expectedWord2,
		uint expectedWord3,
		uint expectedWord4)
	{
		var length = ShadowIntegerFormatter.PackUInt64(
			high,
			low,
			out var word0,
			out var word1,
			out var word2,
			out var word3,
			out var word4);
		Assert.Equal(expectedLength, length);
		Assert.Equal(expectedWord0, word0);
		Assert.Equal(expectedWord1, word1);
		Assert.Equal(expectedWord2, word2);
		Assert.Equal(expectedWord3, word3);
		Assert.Equal(expectedWord4, word4);
	}

	[Theory]
	[InlineData(0, 0)]
	[InlineData(42, 42)]
	[InlineData(-42, 42)]
	public void ShadowMathAbsMatchesNetContract(int value, int expected) =>
		Assert.Equal(expected, ShadowMath.Abs(value));

	[Fact]
	public void ShadowMathAbsThrowsForMinimumValue() =>
		Assert.Throws<OverflowException>(() => ShadowMath.Abs(int.MinValue));

	[Fact]
	public void ShadowMathIntegralLeafSurfaceMatchesNetContract()
	{
		Assert.Equal((sbyte)12, ShadowMath.Abs((sbyte)-12));
		Assert.Equal((short)1234, ShadowMath.Abs((short)-1234));
		Assert.Equal(5_000_000_000L, ShadowMath.Abs(-5_000_000_000L));
		Assert.Equal(3u, ShadowMath.Min(3u, 9u));
		Assert.Equal(9ul, ShadowMath.Max(3ul, 9ul));
		Assert.Equal((short)4, ShadowMath.Clamp((short)9, (short)-2, (short)4));
		Assert.Equal(-1, ShadowMath.Sign(long.MinValue + 1));
		Assert.Equal(1, ShadowMath.Sign(1L << 40));
		Assert.Equal(-8_000_000_000L, ShadowMath.BigMul(-100_000, 80_000));
		Assert.Equal(18_446_744_065_119_617_025UL, ShadowMath.BigMul(uint.MaxValue, uint.MaxValue));
		Assert.Throws<OverflowException>(() => ShadowMath.Abs(long.MinValue));
		Assert.Throws<ArgumentException>(() => ShadowMath.Clamp(1, 4, 2));
	}

	[Fact]
	public void ShadowMathIeeeLeafSurfacePreservesBitsAndClassification()
	{
		var negativeZero = BitConverter.Int64BitsToDouble(unchecked((long)0x8000_0000_0000_0000UL));
		var payloadNaN = BitConverter.Int64BitsToDouble(unchecked((long)0x7ff8_1234_5678_9abcUL));
		Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ShadowMath.Abs(negativeZero)));
		Assert.Equal(
			unchecked((long)0xfff8_1234_5678_9abcUL),
			BitConverter.DoubleToInt64Bits(ShadowMath.CopySign(payloadNaN, -1.0)));
		Assert.True(ShadowDouble.IsNaN(payloadNaN));
		Assert.True(ShadowDouble.IsNegative(negativeZero));
		Assert.True(ShadowDouble.IsSubnormal(double.Epsilon));
		Assert.True(ShadowDouble.IsNormal(1.0));
		Assert.True(ShadowDouble.IsPositiveInfinity(double.PositiveInfinity));
		Assert.True(ShadowSingle.IsNegativeInfinity(float.NegativeInfinity));
		Assert.True(ShadowSingle.IsFinite(42.0f));
		Assert.Equal(
			unchecked((long)0x8000_0000_0000_0000UL),
			BitConverter.DoubleToInt64Bits(ShadowMath.Min(0.0, negativeZero)));
		Assert.Equal(0L, BitConverter.DoubleToInt64Bits(ShadowMath.Max(negativeZero, 0.0)));
		Assert.Equal(-1, ShadowMath.Sign(-42.0));
		Assert.Equal(0, ShadowMath.Sign(negativeZero));
		Assert.Throws<ArithmeticException>(() => ShadowMath.Sign(payloadNaN));
	}

	[Theory]
	[InlineData(0.0)]
	[InlineData(0.25)]
	[InlineData(0.5)]
	[InlineData(1.0)]
	[InlineData(2.0)]
	[InlineData(3.0)]
	[InlineData(4.0)]
	[InlineData(12345.6789)]
	[InlineData(double.PositiveInfinity)]
	public void ShadowMathRoundingAndSquareRootMatchNetContract(double value)
	{
		Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Truncate(value)), BitConverter.DoubleToInt64Bits(ShadowMath.Truncate(value)));
		Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Floor(value)), BitConverter.DoubleToInt64Bits(ShadowMath.Floor(value)));
		Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Ceiling(value)), BitConverter.DoubleToInt64Bits(ShadowMath.Ceiling(value)));
		Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Round(value)), BitConverter.DoubleToInt64Bits(ShadowMath.Round(value)));
		Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Sqrt(value)), BitConverter.DoubleToInt64Bits(ShadowMath.Sqrt(value)));
	}

	[Fact]
	public void ShadowMathSquareRootMatchesNetAcrossDeterministicBitPatterns()
	{
		var state = 0x1234_5678_9abc_def0UL;
		for (var index = 0; index < 2_000; index++)
		{
			state = state * 6_364_136_223_846_793_005UL + 1_442_695_040_888_963_407UL;
			var bits = state & 0x7fff_ffff_ffff_ffffUL;
			var value = BitConverter.Int64BitsToDouble(unchecked((long)bits));
			Assert.Equal(
				BitConverter.DoubleToInt64Bits(Math.Sqrt(value)),
				BitConverter.DoubleToInt64Bits(ShadowMath.Sqrt(value)));
		}
	}

	[Fact]
	public void ShadowMathRoundingMatchesNetAcrossDeterministicBitPatterns()
	{
		var state = 0xfedc_ba98_7654_3210UL;
		for (var index = 0; index < 2_000; index++)
		{
			state = state * 2_862_933_555_777_941_757UL + 3_037_000_493UL;
			var value = BitConverter.Int64BitsToDouble(unchecked((long)state));
			AssertSameFloatingValue(Math.Truncate(value), ShadowMath.Truncate(value));
			AssertSameFloatingValue(Math.Floor(value), ShadowMath.Floor(value));
			AssertSameFloatingValue(Math.Ceiling(value), ShadowMath.Ceiling(value));
			foreach (var mode in Enum.GetValues<MidpointRounding>())
			{
				AssertSameFloatingValue(
					Math.Round(value, mode),
					ShadowMath.Round(value, mode),
					$"Round mismatch for {mode}, input=0x{state:X16}.");
			}
		}
	}

	private static void AssertSameFloatingValue(double expected, double actual, string? context = null)
	{
		if (double.IsNaN(expected))
		{
			Assert.True(double.IsNaN(actual), context);
			return;
		}

		Assert.True(
			BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual),
			context);
	}

	[Fact]
	public void ShadowMathFloatingLeavesMatchNetAcrossDeterministicBitPatterns()
	{
		var state = 0x0ddc_0ffe_e15e_beefUL;
		for (var index = 0; index < 2_000; index++)
		{
			state = state * 6_364_136_223_846_793_005UL + 1_442_695_040_888_963_407UL;
			var first = BitConverter.Int64BitsToDouble(unchecked((long)state));
			state = state * 6_364_136_223_846_793_005UL + 1_442_695_040_888_963_407UL;
			var second = BitConverter.Int64BitsToDouble(unchecked((long)state));
			Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Abs(first)), BitConverter.DoubleToInt64Bits(ShadowMath.Abs(first)));
			Assert.Equal(BitConverter.DoubleToInt64Bits(Math.CopySign(first, second)), BitConverter.DoubleToInt64Bits(ShadowMath.CopySign(first, second)));
			Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Min(first, second)), BitConverter.DoubleToInt64Bits(ShadowMath.Min(first, second)));
			Assert.Equal(BitConverter.DoubleToInt64Bits(Math.Max(first, second)), BitConverter.DoubleToInt64Bits(ShadowMath.Max(first, second)));

			var firstSingle = BitConverter.Int32BitsToSingle(unchecked((int)state));
			var secondSingle = BitConverter.Int32BitsToSingle(unchecked((int)(state >> 32)));
			Assert.Equal(BitConverter.SingleToInt32Bits(Math.Abs(firstSingle)), BitConverter.SingleToInt32Bits(ShadowMath.Abs(firstSingle)));
			Assert.Equal(BitConverter.SingleToInt32Bits(Math.Min(firstSingle, secondSingle)), BitConverter.SingleToInt32Bits(ShadowMath.Min(firstSingle, secondSingle)));
			Assert.Equal(BitConverter.SingleToInt32Bits(Math.Max(firstSingle, secondSingle)), BitConverter.SingleToInt32Bits(ShadowMath.Max(firstSingle, secondSingle)));
		}
	}

	[Fact]
	public void ShadowBitConverterUsesTargetBigEndianByteOrder() =>
		Assert.Equal(
			new byte[] { 0x01, 0x02, 0x03, 0x04 },
			ShadowBitConverter.GetBytes(0x01020304));

	[Fact]
	public void CompilerOnlyPrimitiveFailsClearlyOnHost()
	{
		var exception = Assert.Throws<PlatformNotSupportedException>(
			() => M68kRuntime.AllocateString(4));
		Assert.Contains("compiler primitive", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void ShadowListCoreOperationsMatchNetListContract()
	{
		var values = new ShadowList<int>();
		for (var value = 0; value < 9; value++)
		{
			values.Add(value * 3);
		}

		Assert.Equal(9, values.Count);
		Assert.Equal(24, values[8]);
		values[4] = 42;
		Assert.Equal(42, values[4]);
		Assert.Throws<ArgumentOutOfRangeException>(() => _ = values[-1]);
		Assert.Throws<ArgumentOutOfRangeException>(() => values[values.Count] = 0);
	}

	[Fact]
	public void ShadowListCapacityAndMutationMatchNetListContract()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new ShadowList<string>(-1));
		var values = new ShadowList<string>(2);
		Assert.Equal(2, values.Capacity);
		values.Add("zero");
		values.Add("one");
		values.Add("two");
		Assert.True(values.Capacity >= 3);
		Assert.Throws<ArgumentOutOfRangeException>(() => values.Capacity = 2);

		values.RemoveAt(1);
		Assert.Equal(2, values.Count);
		Assert.Equal("two", values[1]);
		Assert.Throws<ArgumentOutOfRangeException>(() => values.RemoveAt(2));
		var copy = values.ToArray();
		Assert.Equal(new[] { "zero", "two" }, copy);
		copy[0] = "changed";
		Assert.Equal("zero", values[0]);

		values.Clear();
		Assert.Equal(0, values.Count);
		Assert.True(values.Capacity >= 3);
		Assert.Empty(values.ToArray());
		values.Capacity = 0;
		Assert.Equal(0, values.Capacity);
	}

	[Fact]
	public void ShadowListIntegralEqualityMatchesNetListContract()
	{
		var values = new ShadowList<long>(3);
		values.Add(0x0000_0001_0000_0002L);
		values.Add(0x0000_0003_0000_0002L);
		values.Add(0x0000_0001_0000_0004L);
		Assert.True(values.Contains(0x0000_0003_0000_0002L));
		Assert.False(values.Contains(0x0000_0003_0000_0004L));
		Assert.Equal(2, values.IndexOf(0x0000_0001_0000_0004L));

		var stable = values.GetEnumerator();
		Assert.True(stable.MoveNext());
		Assert.False(values.Remove(42));
		Assert.True(stable.MoveNext());

		var invalidated = values.GetEnumerator();
		Assert.True(invalidated.MoveNext());
		Assert.True(values.Remove(0x0000_0001_0000_0002L));
		Assert.Throws<InvalidOperationException>(() => invalidated.MoveNext());
		Assert.Equal(2, values.Count);
		Assert.Equal(0x0000_0003_0000_0002L, values[0]);
	}

	[Fact]
	public void ShadowListStringEqualityMatchesNetListContract()
	{
		var equalContent = "xAmigax".Substring(1, 5);
		var values = new ShadowList<string?>(4);
		values.Add("Amiga");
		values.Add(null);
		values.Add("Amiga");
		Assert.True(values.Contains(equalContent));
		Assert.Equal(0, values.IndexOf(equalContent));
		Assert.True(values.Contains(null));
		Assert.False(values.Contains("amiga"));
		Assert.True(values.Remove(equalContent));
		Assert.Equal(1, values.IndexOf("Amiga"));
	}

	[Fact]
	public void ShadowListEnumerationMatchesNetListContract()
	{
		var values = new ShadowList<string>(2);
		values.Add("first");
		values.Add("second");
		var enumerator = values.GetEnumerator();
		values.Capacity = 8;
		Assert.True(enumerator.MoveNext());
		Assert.Equal("first", enumerator.Current);
		Assert.True(enumerator.MoveNext());
		Assert.Equal("second", enumerator.Current);
		Assert.False(enumerator.MoveNext());
		Assert.Null(enumerator.Current);
		Assert.False(enumerator.MoveNext());
		enumerator.Dispose();

		var invalidated = values.GetEnumerator();
		Assert.True(invalidated.MoveNext());
		values[0] = "changed";
		Assert.Throws<InvalidOperationException>(() => invalidated.MoveNext());

		var cleared = values.GetEnumerator();
		values.Clear();
		Assert.Throws<InvalidOperationException>(() => cleared.MoveNext());

		var empty = new ShadowList<int>().GetEnumerator();
		Assert.False(empty.MoveNext());
		Assert.Equal(0, empty.Current);
	}

	[Fact]
	public void ShadowListInterfaceEnumerationMatchesCoreLib()
	{
		Check(new List<string>(), new ShadowList<string>());
		var actual = new ShadowList<string>(); actual.Add("first"); actual.Add("second");
		Check(new List<string> { "first", "second" }, actual);
		void Check(List<string> expected, ShadowList<string> shadow)
		{
			var expectedIterator = ((IEnumerable<string>)expected).GetEnumerator();
			var actualIterator = ((IEnumerable<string>)shadow).GetEnumerator();
			Assert.Equal(Observe(() => expectedIterator.Current), Observe(() => actualIterator.Current));
			Assert.Equal(Observe(() => ((System.Collections.IEnumerator)expectedIterator).Current), Observe(() => ((System.Collections.IEnumerator)actualIterator).Current));
			while (expectedIterator.MoveNext()) { Assert.True(actualIterator.MoveNext()); Assert.Equal(expectedIterator.Current, actualIterator.Current); }
			Assert.False(actualIterator.MoveNext());
			Assert.Equal(Observe(() => expectedIterator.Current), Observe(() => actualIterator.Current));
			Assert.Equal(Observe(() => ((System.Collections.IEnumerator)expectedIterator).Current), Observe(() => ((System.Collections.IEnumerator)actualIterator).Current));
			((System.Collections.IEnumerator)expectedIterator).Reset(); ((System.Collections.IEnumerator)actualIterator).Reset();
			Assert.Equal(expectedIterator.MoveNext(), actualIterator.MoveNext());
			expected.Add("mutation"); shadow.Add("mutation");
			Assert.Equal(Observe(() => expectedIterator.MoveNext()), Observe(() => actualIterator.MoveNext()));
			Assert.Equal(Observe(() => { ((System.Collections.IEnumerator)expectedIterator).Reset(); return null; }), Observe(() => { ((System.Collections.IEnumerator)actualIterator).Reset(); return null; }));
			expectedIterator.Dispose(); actualIterator.Dispose();
		}
		static object? Observe(Func<object?> operation)
		{
			try { return operation(); } catch (InvalidOperationException) { return typeof(InvalidOperationException); }
		}
		IEnumerable<string> empty = new ShadowList<string>();
		Assert.Same(empty.GetEnumerator(), empty.GetEnumerator());
		Assert.Same(empty.GetEnumerator(), ((System.Collections.IEnumerable)empty).GetEnumerator());
	}

	[Fact]
	public void ShadowDictionaryCoreMatchesNetDictionaryContract()
	{
		var values = new ShadowDictionary<int, string>();
		values.Add(1, "one");
		values.Add(5, "five");
		values.Add(9, "nine");
		values.Add(13, "thirteen");
		values.Add(17, "seventeen");
		Assert.Equal(5, values.Count);
		Assert.Equal("nine", values[9]);
		Assert.True(values.TryGetValue(13, out var thirteen));
		Assert.Equal("thirteen", thirteen);
		Assert.False(values.TryGetValue(2, out var missing));
		Assert.Null(missing);
		values[9] = "changed";
		Assert.Equal("changed", values[9]);
		values[21] = "twenty-one";
		Assert.Equal(6, values.Count);
		Assert.Throws<ArgumentException>(() => values.Add(5, "duplicate"));
		Assert.Throws<KeyNotFoundException>(() => _ = values[2]);
	}

	[Fact]
	public void ShadowDictionaryStringKeysEnforceNullAndOrdinalRules()
	{
		var values = new ShadowDictionary<string, int>();
		values.Add("Amiga", 42);
		var equalContent = "xAmigax".Substring(1, 5);
		Assert.Equal(42, values[equalContent]);
		Assert.False(values.TryGetValue("amiga", out var missing));
		Assert.Equal(0, missing);
		Assert.Throws<ArgumentNullException>(() => values.Add(null!, 1));
		Assert.Throws<ArgumentNullException>(() => values.TryGetValue(null!, out _));
	}

	[Fact]
	public void ShadowEnumerableRangeMatchesSelectedNetContract()
	{
		var sequence = ShadowEnumerable.Range(-2, 5);
		Assert.Equal([-2, -1, 0, 1, 2], ShadowEnumerable.ToArray(sequence));
		Assert.Equal([-2, -1, 0, 1, 2], ShadowEnumerable.ToArray(sequence));
		Assert.Empty(ShadowEnumerable.ToArray(ShadowEnumerable.Range(42, 0)));
		Assert.Throws<ArgumentOutOfRangeException>(() => ShadowEnumerable.Range(0, -1));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			ShadowEnumerable.Range(int.MaxValue, 2));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.ToArray((IEnumerable<int>)null!));
	}

	[Fact]
	public void ShadowEnumerableRepeatPreservesValuesAndEnumerationState()
	{
		var value = new object();
		var sequence = ShadowEnumerable.Repeat(value, 3);
		var first = ShadowEnumerable.ToArray(sequence);
		var second = ShadowEnumerable.ToArray(sequence);
		Assert.NotSame(first, second);
		Assert.Equal(3, first.Length);
		Assert.All(first, item => Assert.Same(value, item));
		Assert.All(second, item => Assert.Same(value, item));
		Assert.Equal(first, sequence.ToArray());
		Assert.Empty(ShadowEnumerable.ToArray(ShadowEnumerable.Repeat(42, 0)));
		Assert.Throws<ArgumentOutOfRangeException>(() => ShadowEnumerable.Repeat(42, -1));
	}

	[Fact]
	public void ShadowEnumerableSelectIsDeferredOrderedAndRepeatable()
	{
		var calls = 0;
		var source = ShadowEnumerable.Range(1, 3);
		var selected = ShadowEnumerable.SelectInt32(source, value =>
		{
			calls++;
			return value * 2;
		});

		Assert.Equal(0, calls);
		Assert.Equal([2, 4, 6], ShadowEnumerable.SelectInt32ToArray(selected));
		Assert.Equal(3, calls);
		Assert.Equal([2, 4, 6], ShadowEnumerable.SelectInt32ToArray(selected));
		Assert.Equal(6, calls);
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.SelectInt32(null!, static value => value));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.SelectInt32(source, null!));
	}

	[Fact]
	public void ShadowEnumerableWhereIsDeferredOrderedAndEvaluatesOncePerElement()
	{
		var calls = 0;
		var source = ShadowEnumerable.Range(1, 4);
		var filtered = ShadowEnumerable.RangeWhereInt32(source, value =>
		{
			calls++;
			return (value & 1) == 0;
		});

		Assert.Equal(0, calls);
		Assert.Equal([2, 4], ShadowEnumerable.RangeWhereInt32ToArray(filtered));
		Assert.Equal(4, calls);
		Assert.Equal([2, 4], filtered.ToArray());
		Assert.Equal(8, calls);
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.RangeWhereInt32(null!, static value => true));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.RangeWhereInt32(source, null!));
	}

	[Fact]
	public void ShadowEnumerableAnyShortCircuitsExactPrivateSources()
	{
		Assert.False(ShadowEnumerable.RangeAny(ShadowEnumerable.Range(0, 0)));
		Assert.True(ShadowEnumerable.RangeAny(ShadowEnumerable.Range(0, 1)));

		var selectCalls = 0;
		var selected = ShadowEnumerable.SelectInt32(
			ShadowEnumerable.Range(1, 2),
			value =>
			{
				selectCalls++;
				return value * 2;
			});
		Assert.True(ShadowEnumerable.SelectInt32Any(selected));
		Assert.Equal(0, selectCalls);

		var whereCalls = 0;
		var filtered = ShadowEnumerable.RangeWhereInt32(
			ShadowEnumerable.Range(1, 4),
			value =>
			{
				whereCalls++;
				return (value & 1) == 0;
			});
		var terminalCalls = 0;
		Assert.True(ShadowEnumerable.RangeWhereInt32AnyPredicate(
			filtered,
			value =>
			{
				terminalCalls++;
				return value > 2;
			}));
		Assert.Equal(4, whereCalls);
		Assert.Equal(2, terminalCalls);

		var repeatCalls = 0;
		Assert.True(ShadowEnumerable.RepeatInt32AnyPredicate(
			ShadowEnumerable.Repeat(1, 4),
			value => ++repeatCalls == 3));
		Assert.Equal(3, repeatCalls);
		Assert.Throws<ArgumentNullException>(() => ShadowEnumerable.RangeAny(null!));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.RangeAnyPredicate(ShadowEnumerable.Range(0, 1), null!));
	}

	[Fact]
	public void ShadowEnumerableTakeNarrowsEveryExactPrivateSourceLazily()
	{
		Assert.Equal(
			[3, 4],
			ShadowEnumerable.RangeToArray(
				ShadowEnumerable.RangeTakeInt32(ShadowEnumerable.Range(3, 5), 2)));
		Assert.Empty(
			ShadowEnumerable.RangeToArray(
				ShadowEnumerable.RangeTakeInt32(ShadowEnumerable.Range(3, 5), -1)));
		Assert.Equal(
			[7, 7, 7],
			ShadowEnumerable.RepeatToArray(
				ShadowEnumerable.RepeatInt32TakeInt32(ShadowEnumerable.Repeat(7, 3), 8)));

		var selectCalls = 0;
		var selected = ShadowEnumerable.SelectInt32(
			ShadowEnumerable.Range(1, 5),
			value =>
			{
				selectCalls++;
				return value * 10;
			});
		var selectedTake = ShadowEnumerable.SelectInt32TakeInt32(selected, 2);
		Assert.Equal(0, selectCalls);
		Assert.Equal([10, 20], ShadowEnumerable.SelectInt32ToArray(selectedTake));
		Assert.Equal(2, selectCalls);

		var whereCalls = 0;
		var filtered = ShadowEnumerable.RangeWhereInt32(
			ShadowEnumerable.Range(1, 8),
			value =>
			{
				whereCalls++;
				return (value & 1) == 0;
			});
		var filteredTake = ShadowEnumerable.RangeWhereInt32TakeInt32(filtered, 2);
		Assert.Equal([2, 4], ShadowEnumerable.RangeWhereInt32ToArray(filteredTake));
		Assert.Equal(4, whereCalls);
		Assert.Equal([2], ShadowEnumerable.RangeWhereInt32TakeInt32(filteredTake, 1).ToArray());

		var selectWhereCalls = 0;
		var selectWhere = ShadowEnumerable.SelectWhereInt32(
			ShadowEnumerable.SelectInt32(
				ShadowEnumerable.Range(1, 6),
				value => value * 3),
			value =>
			{
				selectWhereCalls++;
				return (value & 1) == 0;
			});
		Assert.Equal(
			[6, 12],
			ShadowEnumerable.SelectWhereInt32ToArray(
				ShadowEnumerable.SelectWhereInt32TakeInt32(selectWhere, 2)));
		Assert.Equal(4, selectWhereCalls);

		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.RangeTakeInt32(null!, 1));
	}

	[Fact]
	public void ShadowEnumerableSumAggregatesEveryExactPrivateSourceWithCheckedArithmetic()
	{
		Assert.Equal(0, ShadowEnumerable.RangeSum(ShadowEnumerable.Range(1, 0)));
		Assert.Equal(10, ShadowEnumerable.RangeSum(ShadowEnumerable.Range(1, 4)));
		Assert.Equal(
			20,
			ShadowEnumerable.RangeSumSelector(
				ShadowEnumerable.Range(1, 4),
				static value => value * 2));
		Assert.Equal(12, ShadowEnumerable.RepeatInt32Sum(ShadowEnumerable.Repeat(3, 4)));

		var selected = ShadowEnumerable.SelectInt32(
			ShadowEnumerable.Range(1, 3),
			static value => value * 3);
		Assert.Equal(18, ShadowEnumerable.SelectInt32Sum(selected));
		Assert.Equal(
			21,
			ShadowEnumerable.SelectInt32SumSelector(selected, static value => value + 1));

		var filtered = ShadowEnumerable.RangeWhereInt32(
			ShadowEnumerable.Range(1, 6),
			static value => (value & 1) == 0);
		Assert.Equal(12, ShadowEnumerable.RangeWhereInt32Sum(filtered));
		var filteredTake = ShadowEnumerable.RangeWhereInt32TakeInt32(filtered, 2);
		Assert.Equal(6, ShadowEnumerable.RangeWhereInt32TakeSum(filteredTake));
		Assert.Equal(
			12,
			ShadowEnumerable.RangeWhereInt32TakeSumSelector(
				filteredTake,
				static value => value * 2));

		var selectWhere = ShadowEnumerable.SelectWhereInt32(
			ShadowEnumerable.SelectInt32(
				ShadowEnumerable.Range(1, 5),
				static value => value * 2),
			static value => value > 4);
		Assert.Equal(24, ShadowEnumerable.SelectWhereInt32Sum(selectWhere));
		var selectWhereTake = ShadowEnumerable.SelectWhereInt32TakeInt32(selectWhere, 2);
		Assert.Equal(14, ShadowEnumerable.SelectWhereInt32TakeSum(selectWhereTake));

		var arrayCalls = 0;
		Assert.Equal(
			42,
			ShadowEnumerable.ArraySumSelector(
				new[] { new HostSumBlock(19), new HostSumBlock(23) },
				value =>
				{
					arrayCalls++;
					return value.Value;
				}));
		Assert.Equal(2, arrayCalls);
		Assert.Equal(
			0,
			ShadowEnumerable.ArraySumSelector(
				Array.Empty<HostSumBlock>(),
				static value => value.Value));

		Assert.Throws<OverflowException>(() =>
			ShadowEnumerable.RangeSum(ShadowEnumerable.Range(int.MaxValue - 1, 2)));
		Assert.Throws<OverflowException>(() =>
			ShadowEnumerable.ArraySumSelector(
				new[] { new HostSumBlock(int.MaxValue), new HostSumBlock(1) },
				static value => value.Value));
		Assert.Throws<ArgumentNullException>(() => ShadowEnumerable.RangeSum(null!));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.RangeSumSelector(ShadowEnumerable.Range(1, 1), null!));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.ArraySumSelector<HostSumBlock>(
				null!,
				static value => value.Value));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.ArraySumSelector(
				Array.Empty<HostSumBlock>(),
				null!));
	}

	[Fact]
	public void ShadowEnumerableDictionaryOrderingIsDeferredStableAndRepeatable()
	{
		var source = new ShadowDictionary<uint, HostOrderBlock>();
		source.Add(10, new HostOrderBlock(0, 2, 1));
		source.Add(11, new HostOrderBlock(1, 1, 2));
		source.Add(12, new HostOrderBlock(2, 1, 1));
		source.Add(13, new HostOrderBlock(3, 1, 1));
		source.Add(14, new HostOrderBlock(4, 2, 0));
		var primaryCalls = 0;
		var secondaryCalls = 0;
		var primary = ShadowEnumerable.DictionaryUInt32ValuesOrderBy<HostOrderBlock, int>(
			source.Values,
			value =>
			{
				primaryCalls++;
				return value.Primary;
			});
		var ordered = ShadowEnumerable.DictionaryUInt32ValuesThenBy<HostOrderBlock, int>(
			primary,
			value =>
			{
				secondaryCalls++;
				return value.Secondary;
			});

		Assert.Equal(0, primaryCalls);
		Assert.Equal(0, secondaryCalls);
		source.Add(15, new HostOrderBlock(5, 0, 9));
		Assert.Equal([5, 2, 3, 1, 4, 0], ordered.Select(static value => value.Id));
		Assert.Equal(6, primaryCalls);
		Assert.Equal(6, secondaryCalls);
		Assert.Equal([5, 2, 3, 1, 4, 0], ordered.Select(static value => value.Id));
		Assert.Equal(12, primaryCalls);
		Assert.Equal(12, secondaryCalls);

		var throwingPrimary =
			ShadowEnumerable.DictionaryUInt32ValuesOrderBy<HostOrderBlock, int>(
				source.Values,
				value => value.Id == 2
					? throw new InvalidOperationException()
					: value.Primary);
		var throwsWhenEnumerated =
			ShadowEnumerable.DictionaryUInt32ValuesThenBy<HostOrderBlock, int>(
				throwingPrimary,
				static value => value.Secondary);
		Assert.Throws<InvalidOperationException>(() => throwsWhenEnumerated.ToArray());
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.DictionaryUInt32ValuesOrderBy<HostOrderBlock, int>(
				null!,
				static value => value.Primary));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.DictionaryUInt32ValuesOrderBy<HostOrderBlock, int>(
				source.Values,
				null!));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.DictionaryUInt32ValuesThenBy<HostOrderBlock, int>(
				null!,
				static value => value.Secondary));
		Assert.Throws<ArgumentNullException>(() =>
			ShadowEnumerable.DictionaryUInt32ValuesThenBy<HostOrderBlock, int>(
				primary,
				null!));
	}

	private readonly record struct HostSumBlock(int Value);

	private readonly record struct HostOrderBlock(int Id, int Primary, int Secondary);
}
