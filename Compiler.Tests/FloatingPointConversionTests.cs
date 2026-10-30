/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class FloatingPointConversionTests
{
	[Fact]
	public void IntegerConversionsRoundOnceAtBothIeeePrecisions()
	{
		foreach (var bits in IntegerCases())
		{
			var signed = unchecked((long)bits);
			Assert.Equal(BitConverter.DoubleToUInt64Bits((double)bits), BitConverter.DoubleToUInt64Bits(FloatingPointConversions.UInt64ToDouble(bits)));
			Assert.Equal(BitConverter.DoubleToUInt64Bits((double)signed), BitConverter.DoubleToUInt64Bits(FloatingPointConversions.Int64ToDouble(signed)));
			Assert.Equal(BitConverter.SingleToUInt32Bits((float)bits), BitConverter.SingleToUInt32Bits(FloatingPointConversions.UInt64ToSingle(bits)));
			Assert.Equal(BitConverter.SingleToUInt32Bits((float)signed), BitConverter.SingleToUInt32Bits(FloatingPointConversions.Int64ToSingle(signed)));
			Assert.Equal(BitConverter.DoubleToUInt64Bits((double)(uint)bits), BitConverter.DoubleToUInt64Bits(FloatingPointConversions.UInt32ToDouble((uint)bits)));
			Assert.Equal(BitConverter.DoubleToUInt64Bits((double)unchecked((int)bits)), BitConverter.DoubleToUInt64Bits(FloatingPointConversions.Int32ToDouble(unchecked((int)bits))));
			Assert.Equal(BitConverter.SingleToUInt32Bits((float)(uint)bits), BitConverter.SingleToUInt32Bits(FloatingPointConversions.UInt32ToSingle((uint)bits)));
			Assert.Equal(BitConverter.SingleToUInt32Bits((float)unchecked((int)bits)), BitConverter.SingleToUInt32Bits(FloatingPointConversions.Int32ToSingle(unchecked((int)bits))));
		}
	}

	[Fact]
	public void SingleAndDoubleConversionsPreserveZerosAndRoundSubnormalsAndFiniteValues()
	{
		foreach (var bits in FloatingCases())
		{
			var wide = BitConverter.UInt64BitsToDouble(bits);
			var single = BitConverter.UInt32BitsToSingle((uint)bits);
			Assert.Equal(BitConverter.SingleToUInt32Bits((float)wide), BitConverter.SingleToUInt32Bits(FloatingPointConversions.DoubleToSingle(wide)));
			Assert.Equal(BitConverter.DoubleToUInt64Bits((double)single), BitConverter.DoubleToUInt64Bits(FloatingPointConversions.SingleToDouble(single)));
		}
	}

	[Fact]
	public void FloatingToIntegerConversionsMatchNet10TruncationSaturationAndNaN()
	{
		foreach (var bits in FloatingCases())
		{
			var wide = BitConverter.UInt64BitsToDouble(bits);
			var single = BitConverter.UInt32BitsToSingle((uint)bits);
			Assert.Equal(unchecked((int)wide), FloatingPointConversions.DoubleToInt32(wide));
			Assert.Equal(unchecked((uint)wide), FloatingPointConversions.DoubleToUInt32(wide));
			Assert.Equal(unchecked((long)wide), FloatingPointConversions.DoubleToInt64(wide));
			Assert.Equal(unchecked((ulong)wide), FloatingPointConversions.DoubleToUInt64(wide));
			Assert.Equal(unchecked((int)single), FloatingPointConversions.SingleToInt32(single));
			Assert.Equal(unchecked((uint)single), FloatingPointConversions.SingleToUInt32(single));
			Assert.Equal(unchecked((long)single), FloatingPointConversions.SingleToInt64(single));
			Assert.Equal(unchecked((ulong)single), FloatingPointConversions.SingleToUInt64(single));
		}
	}

	private static IEnumerable<ulong> IntegerCases()
	{
		yield return 0; yield return ulong.MaxValue;
		foreach (var bit in new[] { 23, 24, 31, 32, 52, 53, 54, 62, 63 })
		foreach (var delta in new long[] { -3, -2, -1, 0, 1, 2, 3 })
		{
			var value = unchecked((1UL << bit) + (ulong)delta);
			yield return value; yield return unchecked(0UL - value);
		}
		// Values immediately around a binary32 halfway point that is representable
		// in binary64: conversion through Double would introduce double rounding.
		yield return (1UL << 63) + (1UL << 39) - 1;
		yield return (1UL << 63) + (1UL << 39) + 1;
		foreach (var bits in RandomBits()) yield return bits;
	}

	private static IEnumerable<ulong> FloatingCases()
	{
		foreach (var bits in new ulong[] { 0, 0x8000000000000000, 1, 0x000FFFFFFFFFFFFF, 0x0010000000000000,
			0x7FEFFFFFFFFFFFFF, 0x7FF0000000000000, 0xFFF0000000000000, 0x7FF8000012345678, 0xFFF0000012345678 }) yield return bits;
		foreach (var single in new uint[] { 0, 0x80000000, 1, 0x007FFFFF, 0x00800000, 0x7F7FFFFF,
			0x7F800000, 0xFF800000, 0x7FC12345, 0xFFA12345 }) yield return single;
		foreach (var exponent in new[] { -150, -149, -148, -127, -126, -125, 23, 24, 30, 31, 32, 52, 53, 62, 63, 64, 127, 128 })
		{
			var encoded = (ulong)(exponent + 1023) << 52;
			for (var delta = -2; delta <= 2; delta++)
			{
				var bits = unchecked(encoded + (ulong)delta);
				yield return bits; yield return bits | 0x8000000000000000;
			}
		}
		foreach (var bits in RandomBits()) yield return bits;
	}

	private static IEnumerable<ulong> RandomBits()
	{
		var bits = 0xFEDCBA9876543210UL;
		for (var index = 0; index < 50_000; index++)
		{
			bits ^= bits << 13; bits ^= bits >> 7; bits ^= bits << 17;
			yield return bits;
		}
	}
}
