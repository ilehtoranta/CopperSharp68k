/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Full-width integer division using target-independent integer operations.</summary>
public static class Integer64Arithmetic
{
	public static ulong DivideUnsigned(ulong numerator, ulong denominator) => DivRemUnsigned(numerator, denominator, out _);
	public static ulong RemainderUnsigned(ulong numerator, ulong denominator)
	{
		DivRemUnsigned(numerator, denominator, out var remainder);
		return remainder;
	}

	public static long DivideSigned(long numerator, long denominator)
	{
		if (numerator == long.MinValue && denominator == -1) M68kRuntime.ThrowOverflowException();
		var quotient = DivRemUnsigned(Magnitude(numerator), Magnitude(denominator), out _);
		return unchecked((long)((numerator < 0) != (denominator < 0) ? 0UL - quotient : quotient));
	}

	public static long RemainderSigned(long numerator, long denominator)
	{
		DivRemUnsigned(Magnitude(numerator), Magnitude(denominator), out var remainder);
		return unchecked((long)(numerator < 0 ? 0UL - remainder : remainder));
	}

	private static ulong Magnitude(long value) => unchecked((ulong)(value < 0 ? -value : value));

	private static ulong DivRemUnsigned(ulong numerator, ulong denominator, out ulong remainder)
	{
		if (denominator == 0) M68kRuntime.ThrowDivideByZeroException();
		// Runtime divisors such as filesystem block sizes are often powers of
		// two. Keep this in the C# runtime so every generated target benefits;
		// do not run the full 64-step restoring division for a shift and mask.
		if ((denominator & (denominator - 1)) == 0)
		{
			remainder = numerator & (denominator - 1);
			var word = (uint)denominator;
			var shift = 0;
			if (word == 0) { word = (uint)(denominator >> 32); shift = 32; }
			// The word contains exactly one bit. Five bounded tests replace a
			// variable-length shift loop, including divisors in the high lane.
			if (word >= 0x10000) { word >>= 16; shift += 16; }
			if (word >= 0x100) { word >>= 8; shift += 8; }
			if (word >= 0x10) { word >>= 4; shift += 4; }
			if (word >= 4) { word >>= 2; shift += 2; }
			if (word >= 2) shift++;
			return numerator >> shift;
		}
		ulong quotient = 0;
		remainder = 0;
		for (var bit = 63; bit >= 0; bit--)
		{
			// Preserve the 65th remainder bit before shifting. Subtraction then
			// wraps back to the full-width remainder even for high-bit divisors.
			var carry = (remainder >> 63) != 0;
			remainder = (remainder << 1) | ((numerator >> bit) & 1UL);
			if (carry || remainder >= denominator)
			{
				remainder = unchecked(remainder - denominator);
				quotient |= 1UL << bit;
			}
		}
		return quotient;
	}
}
