/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Runtime;
using CopperSharp.Runtime.AmigaPal;

namespace CopperSharp.Compiler.Tests;

public sealed class ManagedRuntimeShadowTests
{
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
