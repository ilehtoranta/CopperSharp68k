/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class Integer64ArithmeticTests
{
	[Fact]
	public void FullWidthDivisionAndRemainderMatchHostForBoundaryAndDeterministicInputs()
	{
		ulong[] boundaries = [0, 1, 2, 3, 10, 0x7fffffff, 0x80000000, 0xffffffff, 0x100000001,
			0x7fffffffffffffff, 0x8000000000000000, 0x8000000000000001, ulong.MaxValue];
		foreach (var numerator in boundaries)
		foreach (var denominator in boundaries.Where(value => value != 0)) Check(numerator, denominator);
		var random = new Random(64819);
		var bytes = new byte[16];
		for (var index = 0; index < 10_000; index++)
		{
			random.NextBytes(bytes);
			Check(BitConverter.ToUInt64(bytes), BitConverter.ToUInt64(bytes, 8) | 1UL);
		}
	}

	[Fact]
	public void ZeroDivisorsAndSignedDivisionOverflowRaiseCanonicalExceptions()
	{
		Assert.Throws<DivideByZeroException>(() => Integer64Arithmetic.DivideUnsigned(1, 0));
		Assert.Throws<DivideByZeroException>(() => Integer64Arithmetic.RemainderUnsigned(1, 0));
		Assert.Throws<DivideByZeroException>(() => Integer64Arithmetic.DivideSigned(long.MinValue, 0));
		Assert.Throws<DivideByZeroException>(() => Integer64Arithmetic.RemainderSigned(long.MinValue, 0));
		Assert.Throws<OverflowException>(() => Integer64Arithmetic.DivideSigned(long.MinValue, -1));
		// CIL permits the mathematically exact remainder; Intel host rem can
		// instead overflow because its instruction also computes the quotient.
		Assert.Equal(0, Integer64Arithmetic.RemainderSigned(long.MinValue, -1));
	}

	private static void Check(ulong numerator, ulong denominator)
	{
		Assert.Equal(numerator / denominator, Integer64Arithmetic.DivideUnsigned(numerator, denominator));
		Assert.Equal(numerator % denominator, Integer64Arithmetic.RemainderUnsigned(numerator, denominator));
		var left = unchecked((long)numerator);
		var right = unchecked((long)denominator);
		if (left == long.MinValue && right == -1) return;
		Assert.Equal(left / right, Integer64Arithmetic.DivideSigned(left, right));
		Assert.Equal(left % right, Integer64Arithmetic.RemainderSigned(left, right));
	}
}
