/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Floating-point target operations implemented without a hardware FPU.</summary>
public static class FloatingPointArithmetic
{
	public static float NegateSingle(float value) => M68kRuntime.UInt32BitsToSingle(M68kRuntime.SingleToUInt32Bits(value) ^ 0x80000000u);
	public static double NegateDouble(double value) => FloatingPointConversions.Double(FloatingPointConversions.Bits(value) ^ 0x8000000000000000UL);

	public static float AddSingle(float left, float right) => M68kRuntime.UInt32BitsToSingle((uint)AddBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8, false));
	public static double AddDouble(double left, double right) => FloatingPointConversions.Double(AddBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11, false));
	public static float SubtractSingle(float left, float right) => M68kRuntime.UInt32BitsToSingle((uint)AddBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8, true));
	public static double SubtractDouble(double left, double right) => FloatingPointConversions.Double(AddBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11, true));
	public static float MultiplySingle(float left, float right) => M68kRuntime.UInt32BitsToSingle((uint)MultiplyBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8));
	public static double MultiplyDouble(double left, double right) => FloatingPointConversions.Double(MultiplyBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11));
	public static float DivideSingle(float left, float right) => M68kRuntime.UInt32BitsToSingle((uint)DivideBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8));
	public static double DivideDouble(double left, double right) => FloatingPointConversions.Double(DivideBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11));
	public static float RemainderSingle(float left, float right) => M68kRuntime.UInt32BitsToSingle((uint)RemainderBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8));
	public static double RemainderDouble(double left, double right) => FloatingPointConversions.Double(RemainderBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11));

	public static bool EqualSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result == 0;
	}

	public static bool EqualDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result == 0;
	}

	public static bool NotEqualOrUnorderedSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result != 0;
	}

	public static bool NotEqualOrUnorderedDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result != 0;
	}

	public static bool GreaterSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result == 1;
	}

	public static bool GreaterDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result == 1;
	}

	public static bool LessSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result == -1;
	}

	public static bool LessDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result == -1;
	}

	public static bool GreaterOrEqualSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result == 0 || result == 1;
	}

	public static bool GreaterOrEqualDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result == 0 || result == 1;
	}

	public static bool LessOrEqualSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result <= 0;
	}

	public static bool LessOrEqualDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result <= 0;
	}

	public static bool GreaterOrUnorderedSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result > 0;
	}

	public static bool GreaterOrUnorderedDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result > 0;
	}

	public static bool LessOrUnorderedSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result == -1 || result == 2;
	}

	public static bool LessOrUnorderedDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result == -1 || result == 2;
	}

	public static bool GreaterOrEqualOrUnorderedSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result >= 0;
	}

	public static bool GreaterOrEqualOrUnorderedDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result >= 0;
	}

	public static bool LessOrEqualOrUnorderedSingle(float left, float right)
	{
		var result = CompareBits(M68kRuntime.SingleToUInt32Bits(left), M68kRuntime.SingleToUInt32Bits(right), 23, 8);
		return result != 1;
	}

	public static bool LessOrEqualOrUnorderedDouble(double left, double right)
	{
		var result = CompareBits(FloatingPointConversions.Bits(left), FloatingPointConversions.Bits(right), 52, 11);
		return result != 1;
	}

	// -1/0/1 are ordered results; 2 is unordered. Comparing integer encodings
	// directly would reverse negative values and distinguish the two zeros.
	private static int CompareBits(ulong left, ulong right, int fractionBits, int exponentBits)
	{
		var signMask = 1UL << (fractionBits + exponentBits);
		var infinity = ((1UL << exponentBits) - 1) << fractionBits;
		var leftMagnitude = left & (signMask - 1);
		var rightMagnitude = right & (signMask - 1);
		if (leftMagnitude > infinity || rightMagnitude > infinity) return 2;
		if (left == right || leftMagnitude == 0 && rightMagnitude == 0) return 0;
		var leftNegative = (left & signMask) != 0;
		var rightNegative = (right & signMask) != 0;
		if (leftNegative != rightNegative) return leftNegative ? -1 : 1;
		var result = left < right ? -1 : 1;
		return leftNegative ? -result : result;
	}

	private static ulong AddBits(ulong left, ulong right, int fractionBits, int exponentBits, bool subtract)
	{
		var signMask = 1UL << (fractionBits + exponentBits);
		var infinity = ((1UL << exponentBits) - 1) << fractionBits;
		var leftMagnitude = left & (signMask - 1);
		var rightMagnitude = right & (signMask - 1);
		if (leftMagnitude > infinity) return QuietNaN(left, fractionBits);
		if (rightMagnitude > infinity) return QuietNaN(right, fractionBits);
		var leftNegative = (left & signMask) != 0;
		var rightNegative = ((right & signMask) != 0) != subtract;
		var rightSign = rightNegative ? signMask : 0UL;
		if (leftMagnitude == infinity)
			return rightMagnitude == infinity && leftNegative != rightNegative ? InvalidNaN(fractionBits, exponentBits) : left;
		if (rightMagnitude == infinity) return rightSign | infinity;
		if (leftMagnitude == 0 && rightMagnitude == 0) return leftNegative && rightNegative ? signMask : 0UL;
		if (leftMagnitude == 0) return rightSign | rightMagnitude;
		if (rightMagnitude == 0) return left;
		var leftSignificand = Decode(leftMagnitude, fractionBits, exponentBits, out var leftExponent);
		var rightSignificand = Decode(rightMagnitude, fractionBits, exponentBits, out var rightExponent);
		if (leftExponent < rightExponent || leftExponent == rightExponent && leftSignificand < rightSignificand)
		{
			var savedSignificand = leftSignificand; leftSignificand = rightSignificand; rightSignificand = savedSignificand;
			var savedExponent = leftExponent; leftExponent = rightExponent; rightExponent = savedExponent;
			var savedSign = leftNegative; leftNegative = rightNegative; rightNegative = savedSign;
		}
		leftSignificand <<= 3;
		rightSignificand = FloatingPointConversions.ShiftRightJam(rightSignificand << 3, leftExponent - rightExponent);
		var result = leftNegative == rightNegative ? leftSignificand + rightSignificand : leftSignificand - rightSignificand;
		if (result == 0) return 0; // Exact opposite-sign cancellation rounds to positive zero.
		return FloatingPointConversions.RoundPack(result, leftExponent, leftNegative, fractionBits, exponentBits);
	}

	private static ulong MultiplyBits(ulong left, ulong right, int fractionBits, int exponentBits)
	{
		var signMask = 1UL << (fractionBits + exponentBits);
		var infinity = ((1UL << exponentBits) - 1) << fractionBits;
		var sign = (left ^ right) & signMask;
		var leftMagnitude = left & (signMask - 1);
		var rightMagnitude = right & (signMask - 1);
		if (leftMagnitude > infinity) return QuietNaN(left, fractionBits);
		if (rightMagnitude > infinity) return QuietNaN(right, fractionBits);
		if (leftMagnitude == infinity || rightMagnitude == infinity)
			return leftMagnitude == 0 || rightMagnitude == 0 ? InvalidNaN(fractionBits, exponentBits) : sign | infinity;
		if (leftMagnitude == 0 || rightMagnitude == 0) return sign;
		var leftSignificand = Decode(leftMagnitude, fractionBits, exponentBits, out var leftExponent);
		var rightSignificand = Decode(rightMagnitude, fractionBits, exponentBits, out var rightExponent);
		// Four 32-by-32 products retain the full 106-bit binary64 product.
		var low = (ulong)(uint)leftSignificand * (uint)rightSignificand;
		var cross = (leftSignificand >> 32) * (uint)rightSignificand + (low >> 32);
		var high = cross >> 32;
		cross = (ulong)(uint)leftSignificand * (rightSignificand >> 32) + (uint)cross;
		high += (leftSignificand >> 32) * (rightSignificand >> 32) + (cross >> 32);
		low = (cross << 32) | (uint)low;
		var distance = fractionBits - 3;
		var result = (high << (64 - distance)) | (low >> distance);
		if ((low << (64 - distance)) != 0) result |= 1;
		return FloatingPointConversions.RoundPack(result, leftExponent + rightExponent, sign != 0, fractionBits, exponentBits);
	}

	private static ulong DivideBits(ulong left, ulong right, int fractionBits, int exponentBits)
	{
		var signMask = 1UL << (fractionBits + exponentBits);
		var infinity = ((1UL << exponentBits) - 1) << fractionBits;
		var sign = (left ^ right) & signMask;
		var leftMagnitude = left & (signMask - 1);
		var rightMagnitude = right & (signMask - 1);
		if (leftMagnitude > infinity) return QuietNaN(left, fractionBits);
		if (rightMagnitude > infinity) return QuietNaN(right, fractionBits);
		if (leftMagnitude == infinity && rightMagnitude == infinity || leftMagnitude == 0 && rightMagnitude == 0)
			return InvalidNaN(fractionBits, exponentBits);
		if (leftMagnitude == infinity || rightMagnitude == 0) return sign | infinity;
		if (rightMagnitude == infinity || leftMagnitude == 0) return sign;
		var remainder = Decode(leftMagnitude, fractionBits, exponentBits, out var leftExponent);
		var denominator = Decode(rightMagnitude, fractionBits, exponentBits, out var rightExponent);
		var exponent = leftExponent - rightExponent;
		if (remainder < denominator) { remainder <<= 1; exponent--; }
		ulong quotient = 0;
		for (var bit = fractionBits + 3; bit >= 0; bit--)
		{
			if (remainder >= denominator) { remainder -= denominator; quotient |= 1UL << bit; }
			remainder <<= 1;
		}
		if (remainder != 0) quotient |= 1;
		return FloatingPointConversions.RoundPack(quotient, exponent, sign != 0, fractionBits, exponentBits);
	}

	private static ulong RemainderBits(ulong left, ulong right, int fractionBits, int exponentBits)
	{
		var signMask = 1UL << (fractionBits + exponentBits);
		var infinity = ((1UL << exponentBits) - 1) << fractionBits;
		var leftMagnitude = left & (signMask - 1);
		var rightMagnitude = right & (signMask - 1);
		if (leftMagnitude > infinity) return QuietNaN(left, fractionBits);
		if (rightMagnitude > infinity) return QuietNaN(right, fractionBits);
		if (leftMagnitude == infinity || rightMagnitude == 0) return InvalidNaN(fractionBits, exponentBits);
		if (rightMagnitude == infinity || leftMagnitude == 0) return left;
		var remainder = Decode(leftMagnitude, fractionBits, exponentBits, out var leftExponent);
		var denominator = Decode(rightMagnitude, fractionBits, exponentBits, out var rightExponent);
		if (leftExponent < rightExponent) return left;
		// Reduce the integer significand at the divisor's exponent. This is CIL's
		// truncating remainder, not the nearest-integer IEEE remainder operation.
		for (var distance = leftExponent - rightExponent; distance >= 0; distance--)
		{
			if (remainder >= denominator) remainder -= denominator;
			if (distance != 0) remainder <<= 1;
		}
		return FloatingPointConversions.RoundPack(remainder << 3, rightExponent, (left & signMask) != 0, fractionBits, exponentBits);
	}

	private static ulong Decode(ulong magnitude, int fractionBits, int exponentBits, out int exponent)
	{
		var encodedExponent = (int)(magnitude >> fractionBits);
		exponent = (encodedExponent == 0 ? 1 : encodedExponent) - ((1 << (exponentBits - 1)) - 1);
		var leading = 1UL << fractionBits;
		var significand = (magnitude & (leading - 1)) | (encodedExponent == 0 ? 0UL : leading);
		while (significand < leading) { significand <<= 1; exponent--; }
		return significand;
	}

	private static ulong QuietNaN(ulong bits, int fractionBits) => bits | (1UL << (fractionBits - 1));
	private static ulong InvalidNaN(int fractionBits, int exponentBits) =>
		(((1UL << exponentBits) - 1) << fractionBits) | (1UL << (fractionBits - 1));
}
