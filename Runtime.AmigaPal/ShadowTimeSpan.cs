/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime.AmigaPal;

/// <summary>
/// Big-endian fallback bodies for the pinned, eight-byte TimeSpan slice.
/// </summary>
public struct ShadowTimeSpan
{
#pragma warning disable CS0649 // Populated through the representation-compatible public receiver.
	private uint _ticksHigh;
	private uint _ticksLow;
#pragma warning restore CS0649

	private const long TicksPerMillisecond = 10_000;
	private const long TicksPerSecond = TicksPerMillisecond * 1_000;
	private const long TicksPerMinute = TicksPerSecond * 60;
	private const long TicksPerHour = TicksPerMinute * 60;
	private const long TicksPerDay = TicksPerHour * 24;
	private const double MaxMilliseconds = 922_337_203_685_477;
	private const double MinMilliseconds = -922_337_203_685_477;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public void Initialize(long ticks)
	{
		_ticksLow = M68kRuntime.SplitInt64(ticks, out var high);
		_ticksHigh = high;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static TimeSpan FromTicks(long ticks)
	{
		TimeSpan result = default;
		Write(ref result, ticks);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public long GetTicks() => M68kRuntime.CombineInt64(_ticksHigh, _ticksLow);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetDays() => DivideComponent(0, 0, 0x0000_00c9, 0x2a69_c000);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetHours() => DivideComponent(
		0x0000_00c9, 0x2a69_c000, 0x0000_0008, 0x61c4_6800);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetMinutes() => DivideComponent(
		0x0000_0008, 0x61c4_6800, 0, 0x23c3_4600);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetSeconds() => DivideComponent(
		0, 0x23c3_4600, 0, 0x0098_9680);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public int GetMilliseconds() => DivideComponent(
		0, 0x0098_9680, 0, 0x0000_2710);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public double GetTotalDays() => ToDouble(GetTicks()) / TicksPerDay;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public double GetTotalHours() => ToDouble(GetTicks()) / TicksPerHour;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public double GetTotalMinutes() => ToDouble(GetTicks()) / TicksPerMinute;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public double GetTotalSeconds() => ToDouble(GetTicks()) / TicksPerSecond;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public double GetTotalMilliseconds()
	{
		if (_ticksHigh == 0x7fff_ffff && _ticksLow > 0xffff_e950)
		{
			return MaxMilliseconds;
		}
		if (_ticksHigh == 0x8000_0000 && _ticksLow < 0x0000_16b0)
		{
			return MinMilliseconds;
		}
		return ToDouble(GetTicks()) / TicksPerMillisecond;
	}

	private static double ToDouble(long ticks)
	{
		var low = M68kRuntime.SplitInt64(ticks, out var high);
		var sign = high & 0x8000_0000;
		var magnitudeHigh = high;
		var magnitudeLow = low;
		if (sign != 0)
		{
			magnitudeLow = ~magnitudeLow + 1;
			magnitudeHigh = ~magnitudeHigh + (magnitudeLow == 0 ? 1u : 0u);
		}
		if (magnitudeHigh == 0 && magnitudeLow == 0)
		{
			return 0.0;
		}

		var bit = magnitudeHigh != 0 ? 63 : 31;
		var mask = 0x8000_0000u;
		var word = magnitudeHigh != 0 ? magnitudeHigh : magnitudeLow;
		while ((word & mask) == 0)
		{
			mask >>= 1;
			bit--;
		}

		var exponent = bit + 1023;
		if (bit > 52)
		{
			var shift = bit - 52;
			var remainderMask = (1u << shift) - 1;
			var remainder = magnitudeLow & remainderMask;
			var halfway = 1u << (shift - 1);
			for (var index = 0; index < shift; index++)
			{
				magnitudeLow = (magnitudeLow >> 1) | (magnitudeHigh << 31);
				magnitudeHigh >>= 1;
			}
			if (remainder > halfway || remainder == halfway && (magnitudeLow & 1) != 0)
			{
				magnitudeLow++;
				if (magnitudeLow == 0)
				{
					magnitudeHigh++;
				}
				if ((magnitudeHigh & 0x0020_0000) != 0)
				{
					magnitudeLow = (magnitudeLow >> 1) | (magnitudeHigh << 31);
					magnitudeHigh >>= 1;
					exponent++;
				}
			}
		}
		else
		{
			for (var index = bit; index < 52; index++)
			{
				magnitudeHigh = (magnitudeHigh << 1) | (magnitudeLow >> 31);
				magnitudeLow <<= 1;
			}
		}

		var resultHigh = sign | ((uint)exponent << 20) | (magnitudeHigh & 0x000f_ffff);
		return M68kRuntime.CombineDouble(resultHigh, magnitudeLow);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool Equal(TimeSpan first, TimeSpan second)
	{
		Read(first, out var firstHigh, out var firstLow);
		Read(second, out var secondHigh, out var secondLow);
		return firstHigh == secondHigh && firstLow == secondLow;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool NotEqual(TimeSpan first, TimeSpan second) => !Equal(first, second);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool LessThan(TimeSpan first, TimeSpan second) => Compare(first, second) < 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool LessThanOrEqual(TimeSpan first, TimeSpan second) => Compare(first, second) <= 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool GreaterThan(TimeSpan first, TimeSpan second) => Compare(first, second) > 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool GreaterThanOrEqual(TimeSpan first, TimeSpan second) => Compare(first, second) >= 0;

	private static int Compare(TimeSpan first, TimeSpan second)
	{
		Read(first, out var firstHigh, out var firstLow);
		Read(second, out var secondHigh, out var secondLow);
		var firstSignedHigh = (int)firstHigh;
		var secondSignedHigh = (int)secondHigh;
		if (firstSignedHigh < secondSignedHigh) return -1;
		if (firstSignedHigh > secondSignedHigh) return 1;
		if (firstLow < secondLow) return -1;
		return firstLow > secondLow ? 1 : 0;
	}

	private int DivideComponent(
		uint periodHigh,
		uint periodLow,
		uint divisorHigh,
		uint divisorLow)
	{
		var high = _ticksHigh;
		var low = _ticksLow;
		var negative = (high & 0x8000_0000) != 0;
		if (negative)
		{
			low = ~low + 1;
			high = ~high + (low == 0 ? 1u : 0u);
		}

		if (periodHigh != 0 || periodLow != 0)
		{
			RemainderUnsigned64(ref high, ref low, periodHigh, periodLow);
		}
		var quotient = DivideUnsigned64(high, low, divisorHigh, divisorLow);
		return negative ? -(int)quotient : (int)quotient;
	}

	private static uint DivideUnsigned64(
		uint high,
		uint low,
		uint divisorHigh,
		uint divisorLow)
	{
		var dividendHigh = high;
		var dividendLow = low;
		uint remainderHigh = 0;
		uint remainderLow = 0;
		uint quotient = 0;
		for (var bit = 0; bit < 64; bit++)
		{
			var next = dividendHigh >> 31;
			dividendHigh = (dividendHigh << 1) | (dividendLow >> 31);
			dividendLow <<= 1;
			remainderHigh = (remainderHigh << 1) | (remainderLow >> 31);
			remainderLow = (remainderLow << 1) | next;
			quotient <<= 1;
			if (UnsignedGreaterThanOrEqual(
					remainderHigh,
					remainderLow,
					divisorHigh,
					divisorLow))
			{
				SubtractUnsigned64(
					ref remainderHigh,
					ref remainderLow,
					divisorHigh,
					divisorLow);
				quotient |= 1;
			}
		}
		return quotient;
	}

	private static void RemainderUnsigned64(
		ref uint high,
		ref uint low,
		uint divisorHigh,
		uint divisorLow)
	{
		uint remainderHigh = 0;
		uint remainderLow = 0;
		for (var bit = 0; bit < 64; bit++)
		{
			var next = high >> 31;
			high = (high << 1) | (low >> 31);
			low <<= 1;
			remainderHigh = (remainderHigh << 1) | (remainderLow >> 31);
			remainderLow = (remainderLow << 1) | next;
			if (UnsignedGreaterThanOrEqual(
					remainderHigh,
					remainderLow,
					divisorHigh,
					divisorLow))
			{
				SubtractUnsigned64(
					ref remainderHigh,
					ref remainderLow,
					divisorHigh,
					divisorLow);
			}
		}
		high = remainderHigh;
		low = remainderLow;
	}

	private static bool UnsignedGreaterThanOrEqual(
		uint firstHigh,
		uint firstLow,
		uint secondHigh,
		uint secondLow) =>
		firstHigh > secondHigh ||
		(firstHigh == secondHigh && firstLow >= secondLow);

	private static void SubtractUnsigned64(
		ref uint high,
		ref uint low,
		uint subtrahendHigh,
		uint subtrahendLow)
	{
		var originalLow = low;
		low -= subtrahendLow;
		high -= subtrahendHigh + (originalLow < subtrahendLow ? 1u : 0u);
	}

	private static void Read(TimeSpan value, out uint high, out uint low)
	{
		var address = AddressOf(ref value);
		high = Amiga.APTR.ReadUInt32(address, 0);
		low = Amiga.APTR.ReadUInt32(address, 4);
	}

	private static void Write(ref TimeSpan value, long ticks)
	{
		var low = M68kRuntime.SplitInt64(ticks, out var high);
		var address = AddressOf(ref value);
		Amiga.APTR.WriteUInt32(address, 0, high);
		Amiga.APTR.WriteUInt32(address, 4, low);
	}

	private static Amiga.APTR AddressOf(ref TimeSpan value) =>
		throw new NotSupportedException(
			"ShadowTimeSpan.AddressOf is lowered by CopperSharp.");
}
