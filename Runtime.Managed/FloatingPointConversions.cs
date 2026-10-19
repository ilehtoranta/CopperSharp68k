/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>IEEE binary32/binary64 conversions using only integer target operations.</summary>
public static class FloatingPointConversions
{
	public static double Int32ToDouble(int value) => Int64ToDouble(value);
	public static double UInt32ToDouble(uint value) => UInt64ToDouble(value);
	public static float Int32ToSingle(int value) => Int64ToSingle(value);
	public static float UInt32ToSingle(uint value) => UInt64ToSingle(value);
	public static double Int64ToDouble(long value) => Double(IntegerBits(unchecked((ulong)(value < 0 ? -value : value)), value < 0, 52, 11));
	public static double UInt64ToDouble(ulong value) => Double(IntegerBits(value, false, 52, 11));
	public static float Int64ToSingle(long value) => M68kRuntime.UInt32BitsToSingle((uint)IntegerBits(unchecked((ulong)(value < 0 ? -value : value)), value < 0, 23, 8));
	public static float UInt64ToSingle(ulong value) => M68kRuntime.UInt32BitsToSingle((uint)IntegerBits(value, false, 23, 8));
	public static double SingleToDouble(float value) => Double(ConvertBits(M68kRuntime.SingleToUInt32Bits(value), 23, 8, 52, 11));
	public static float DoubleToSingle(double value) => M68kRuntime.UInt32BitsToSingle((uint)ConvertBits(Bits(value), 52, 11, 23, 8));
	public static double DoubleToDouble(double value) => value;
	public static float SingleToSingle(float value) => value;
	public static int DoubleToInt32(double value) => unchecked((int)IntegralBits(Bits(value), 52, 11, 32, true));
	public static uint DoubleToUInt32(double value) => (uint)IntegralBits(Bits(value), 52, 11, 32, false);
	public static long DoubleToInt64(double value) => unchecked((long)IntegralBits(Bits(value), 52, 11, 64, true));
	public static ulong DoubleToUInt64(double value) => IntegralBits(Bits(value), 52, 11, 64, false);
	public static int SingleToInt32(float value) => unchecked((int)IntegralBits(M68kRuntime.SingleToUInt32Bits(value), 23, 8, 32, true));
	public static uint SingleToUInt32(float value) => (uint)IntegralBits(M68kRuntime.SingleToUInt32Bits(value), 23, 8, 32, false);
	public static long SingleToInt64(float value) => unchecked((long)IntegralBits(M68kRuntime.SingleToUInt32Bits(value), 23, 8, 64, true));
	public static ulong SingleToUInt64(float value) => IntegralBits(M68kRuntime.SingleToUInt32Bits(value), 23, 8, 64, false);

	internal static ulong Bits(double value)
	{
		var low = M68kRuntime.SplitDouble(value, out var high);
		return ((ulong)high << 32) | low;
	}

	internal static double Double(ulong bits) => M68kRuntime.CombineDouble((uint)(bits >> 32), (uint)bits);

	internal static ulong ShiftRightJam(ulong value, int distance)
	{
		if (distance <= 0) return value;
		if (distance >= 64) return value == 0 ? 0UL : 1UL;
		return (value >> distance) | ((value << (64 - distance)) == 0 ? 0UL : 1UL);
	}

	// The significand carries three guard/round/sticky bits. Exponent is unbiased.
	internal static ulong RoundPack(ulong significand, int exponent, bool negative, int fractionBits, int exponentBits)
	{
		var sign = negative ? 1UL << (fractionBits + exponentBits) : 0UL;
		if (significand == 0) return sign;
		var leading = 1UL << (fractionBits + 3);
		while (significand >= (leading << 1))
		{
			significand = ShiftRightJam(significand, 1);
			exponent++;
		}
		while (significand < leading)
		{
			significand <<= 1;
			exponent--;
		}
		var bias = (1 << (exponentBits - 1)) - 1;
		var minimum = 1 - bias;
		if (exponent < minimum)
		{
			significand = ShiftRightJam(significand, minimum - exponent);
			exponent = minimum;
		}
		var round = (uint)(significand & 7);
		var rounded = significand >> 3;
		if (round > 4 || round == 4 && (rounded & 1) != 0) rounded++;
		if (rounded >= (1UL << (fractionBits + 1)))
		{
			rounded >>= 1;
			exponent++;
		}
		if (exponent > bias) return sign | (((1UL << exponentBits) - 1) << fractionBits);
		var encodedExponent = rounded < (1UL << fractionBits) ? 0 : exponent + bias;
		return sign | ((ulong)encodedExponent << fractionBits) | (rounded & ((1UL << fractionBits) - 1));
	}

	private static ulong IntegerBits(ulong magnitude, bool negative, int fractionBits, int exponentBits)
	{
		if (magnitude == 0) return 0;
		var highest = 63;
		while ((magnitude >> highest) == 0) highest--;
		var shift = highest - fractionBits - 3;
		var significand = shift > 0 ? ShiftRightJam(magnitude, shift) : magnitude << -shift;
		return RoundPack(significand, highest, negative, fractionBits, exponentBits);
	}

	private static ulong ConvertBits(ulong bits, int sourceFraction, int sourceExponent, int targetFraction, int targetExponent)
	{
		var negative = (bits & (1UL << (sourceFraction + sourceExponent))) != 0;
		var exponentMask = (1 << sourceExponent) - 1;
		var exponent = (int)((bits >> sourceFraction) & (uint)exponentMask);
		var fraction = bits & ((1UL << sourceFraction) - 1);
		if (exponent == exponentMask)
		{
			var payload = sourceFraction > targetFraction ? fraction >> (sourceFraction - targetFraction) : fraction << (targetFraction - sourceFraction);
			if (fraction != 0) payload |= 1UL << (targetFraction - 1); // Quiet a converted NaN.
			return (negative ? 1UL << (targetFraction + targetExponent) : 0UL) |
				(((1UL << targetExponent) - 1) << targetFraction) | payload;
		}
		var significand = fraction | (exponent == 0 ? 0UL : 1UL << sourceFraction);
		var unbiased = (exponent == 0 ? 1 : exponent) - ((1 << (sourceExponent - 1)) - 1);
		var distance = sourceFraction - targetFraction - 3;
		significand = distance > 0 ? ShiftRightJam(significand, distance) : significand << -distance;
		return RoundPack(significand, unbiased, negative, targetFraction, targetExponent);
	}

	// .NET 9+ unchecked conversions truncate and saturate; NaN maps to zero.
	private static ulong IntegralBits(ulong bits, int fractionBits, int exponentBits, int integerBits, bool signed)
	{
		var negative = (bits & (1UL << (fractionBits + exponentBits))) != 0;
		var exponentMask = (1 << exponentBits) - 1;
		var encodedExponent = (int)((bits >> fractionBits) & (uint)exponentMask);
		var fraction = bits & ((1UL << fractionBits) - 1);
		if (encodedExponent == exponentMask && fraction != 0 || negative && !signed) return 0;
		var limit = signed ? 1UL << (integerBits - 1) : integerBits == 64 ? ulong.MaxValue : (1UL << integerBits) - 1;
		var maximum = signed ? negative ? limit : limit - 1 : limit;
		var exponent = encodedExponent - ((1 << (exponentBits - 1)) - 1);
		if (exponent < 0) return 0;
		ulong magnitude;
		if (exponent >= integerBits) magnitude = maximum;
		else
		{
			var significand = fraction | (1UL << fractionBits);
			magnitude = exponent >= fractionBits ? significand << (exponent - fractionBits) : significand >> (fractionBits - exponent);
			if (magnitude > maximum) magnitude = maximum;
		}
		return negative ? unchecked(0UL - magnitude) : magnitude;
	}
}
