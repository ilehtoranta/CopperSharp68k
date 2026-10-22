/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class FloatingPointArithmeticTests
{
	[Theory]
	[InlineData("Add")]
	[InlineData("Subtract")]
	[InlineData("Multiply")]
	[InlineData("Divide")]
	[InlineData("Remainder")]
	public void IntegerKernelsMatchHostIeeeArithmeticAtBothPrecisions(string operation)
	{
		foreach (var (leftBits, rightBits) in Cases())
		{
			var left = BitConverter.UInt64BitsToDouble(leftBits);
			var right = BitConverter.UInt64BitsToDouble(rightBits);
			var expected = Evaluate(operation, left, right);
			var actual = operation switch {
				"Add" => FloatingPointArithmetic.AddDouble(left, right),
				"Subtract" => FloatingPointArithmetic.SubtractDouble(left, right),
				"Multiply" => FloatingPointArithmetic.MultiplyDouble(left, right),
				"Divide" => FloatingPointArithmetic.DivideDouble(left, right),
				_ => FloatingPointArithmetic.RemainderDouble(left, right)
			};
			Check(BitConverter.DoubleToUInt64Bits(expected), BitConverter.DoubleToUInt64Bits(actual), 0x7ff8000000000000, leftBits, rightBits);
			var leftSingle = BitConverter.UInt32BitsToSingle((uint)leftBits);
			var rightSingle = BitConverter.UInt32BitsToSingle((uint)rightBits);
			var expectedSingle = Evaluate(operation, leftSingle, rightSingle);
			var actualSingle = operation switch {
				"Add" => FloatingPointArithmetic.AddSingle(leftSingle, rightSingle),
				"Subtract" => FloatingPointArithmetic.SubtractSingle(leftSingle, rightSingle),
				"Multiply" => FloatingPointArithmetic.MultiplySingle(leftSingle, rightSingle),
				"Divide" => FloatingPointArithmetic.DivideSingle(leftSingle, rightSingle),
				_ => FloatingPointArithmetic.RemainderSingle(leftSingle, rightSingle)
			};
			Check(BitConverter.SingleToUInt32Bits(expectedSingle), BitConverter.SingleToUInt32Bits(actualSingle), 0x7fc00000, (uint)leftBits, (uint)rightBits);
		}
		void Check(ulong expectedBits, ulong actualBits, ulong nanMask, ulong leftBits, ulong rightBits)
		{
			var quietBit = nanMask & unchecked(0UL - nanMask);
			var exponentMask = nanMask ^ quietBit;
			var isNaN = (expectedBits & exponentMask) == exponentMask && (expectedBits & ((quietBit << 1) - 1)) != 0;
			if (isNaN) Assert.Equal(nanMask, actualBits & nanMask);
			else Assert.True(expectedBits == actualBits, $"{operation} {leftBits:X16}, {rightBits:X16}: expected {expectedBits:X16}, actual {actualBits:X16}.");
		}
	}

	[Theory]
	[InlineData("Equal")]
	[InlineData("NotEqualOrUnordered")]
	[InlineData("Greater")]
	[InlineData("Less")]
	[InlineData("GreaterOrEqual")]
	[InlineData("LessOrEqual")]
	[InlineData("GreaterOrUnordered")]
	[InlineData("LessOrUnordered")]
	[InlineData("GreaterOrEqualOrUnordered")]
	[InlineData("LessOrEqualOrUnordered")]
	public void IntegerComparisonKernelsMatchHostRelationsAtBothPrecisions(string predicate)
	{
		var doubleComparison = typeof(FloatingPointArithmetic).GetMethod(predicate + "Double")!.CreateDelegate<Func<double, double, bool>>();
		var singleComparison = typeof(FloatingPointArithmetic).GetMethod(predicate + "Single")!.CreateDelegate<Func<float, float, bool>>();
		foreach (var (leftBits, rightBits) in Cases().Concat(FloatingPointComparisonFixtureBuilder.Pairs(false)))
		{
			var left = BitConverter.UInt64BitsToDouble(leftBits);
			var right = BitConverter.UInt64BitsToDouble(rightBits);
			Assert.Equal(Expected(left, right), doubleComparison(left, right));
		}
		foreach (var (leftBits, rightBits) in Cases().Concat(FloatingPointComparisonFixtureBuilder.Pairs(true)))
		{
			var left = BitConverter.UInt32BitsToSingle((uint)leftBits);
			var right = BitConverter.UInt32BitsToSingle((uint)rightBits);
			Assert.Equal(Expected(left, right), singleComparison(left, right));
		}
		bool Expected(double left, double right)
		{
			var unordered = double.IsNaN(left) || double.IsNaN(right);
			return predicate switch {
				"Equal" => left == right,
				"NotEqualOrUnordered" => left != right,
				"Greater" => left > right,
				"Less" => left < right,
				"GreaterOrEqual" => left >= right,
				"LessOrEqual" => left <= right,
				"GreaterOrUnordered" => unordered || left > right,
				"LessOrUnordered" => unordered || left < right,
				"GreaterOrEqualOrUnordered" => unordered || left >= right,
				_ => unordered || left <= right
			};
		}
	}

	internal static double Evaluate(string operation, double left, double right) => operation switch {
		"Add" => left + right, "Subtract" => left - right, "Multiply" => left * right,
		"Divide" => left / right, _ => left % right };
	internal static float Evaluate(string operation, float left, float right) => operation switch {
		"Add" => left + right, "Subtract" => left - right, "Multiply" => left * right,
		"Divide" => left / right, _ => left % right };

	internal static IEnumerable<(ulong Left, ulong Right)> NativeCases()
	{
		var edges = new ulong[] { 0, 0x8000000000000000, 1, 0x000fffffffffffff, 0x0010000000000000,
			0x3fefffffffffffff, 0x3ff0000000000000, 0x3ff0000000000001, 0x4000000000000000,
			0xbff0000000000000, 0x7fefffffffffffff, 0x7ff0000000000000, 0xfff0000000000000,
			0x7ff8000012345678, 0xfff0000012345678,
			0x80000000, 1, 0x007fffff, 0x00800000, 0x3f7fffff, 0x3f800000, 0x3f800001,
			0x40000000, 0xbf800000, 0x7f7fffff, 0x7f800000, 0xff800000, 0x7fc12345, 0xffa12345 };
		for (var index = 0; index < edges.Length; index++)
		{
			yield return (edges[index], edges[(index + 7) % edges.Length]);
			yield return (edges[index], edges[index]);
			yield return (edges[index], edges[index] ^ 0x8000000080000000);
		}
		// Underflow halfway, subnormal carry, overflow, guard/sticky rounding,
		// cancellation and the estimate constants used by CoreLib's Dragon4.
		foreach (var pair in new (ulong, ulong)[] {
			(1, 0x3fe0000000000000), (3, 0x3fe0000000000000), (0x001fffffffffffff, 0x3fe0000000000000),
			(0x7fefffffffffffff, 0x3ff0000000000001), (0x3ff0000000000001, 0x3ff0000000000001),
			(0x3ff0000000000000, 0x3ca0000000000000), (0x3ff0000000000000, 0xbca0000000000000),
			(0x3ff0000000000001, 0xbff0000000000000), (0x4008000000000000, 0x4000000000000000),
			(0x408ffc0000000000, 0x3fd34413509f79ff), (0x000fffffffffffff, 0x0010000000000000),
			(0x7fefffffffffffff, 1), (0x3ff0000000000000, 0x000fffffffffffff),
			(1, 0x3f000000), (3, 0x3f000000), (0x00ffffff, 0x3f000000),
			(0x7f7fffff, 0x3f800001), (0x3f800001, 0x3f800001), (0x3f800000, 0x33800000),
			(0x3f800001, 0xbf800000), (0x40400000, 0x40000000), (0x7f7fffff, 1) }) yield return pair;
	}

	private static IEnumerable<(ulong, ulong)> Cases()
	{
		foreach (var pair in NativeCases()) yield return pair;
		var bits = 0xFEDCBA9876543210UL;
		for (var index = 0; index < 50_000; index++)
		{
			bits ^= bits << 13; bits ^= bits >> 7; bits ^= bits << 17;
			var left = bits;
			bits ^= bits << 13; bits ^= bits >> 7; bits ^= bits << 17;
			yield return (left, bits);
			if (index % 32 == 0) { yield return (left, left ^ 0x8000000080000000); yield return (left, unchecked(left + 1)); }
		}
	}
}
