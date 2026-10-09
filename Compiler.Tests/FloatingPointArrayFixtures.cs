/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibFloatingPointArrayBitsEntry()
	{
		var singles = new float[8];
		var doubles = new double[8];
		for (var index = 0; index < 8; index++)
		{
			var singleBits = FloatingArraySingleBits(index);
			var wideBits = FloatingArrayDoubleBits(index);
			StoreFloatingArraySingle(singles, index, Unsafe.BitCast<uint, float>(singleBits));
			StoreFloatingArrayDouble(doubles, index, Unsafe.BitCast<ulong, double>(wideBits));
			System.GC.Collect();
			if (ReadFloatingArraySingle(singles, index) != singleBits || ReadFloatingArrayDouble(doubles, index) != wideBits) return 100 + index;
		}
		for (var index = 0; index < 8; index++)
			if (ReadFloatingArraySingle(singles, index) != FloatingArraySingleBits(index) ||
				ReadFloatingArrayDouble(doubles, index) != FloatingArrayDoubleBits(index)) return 200 + index;
		try { ReadFloatingArraySingle(singles, -1); return 1; }
		catch (System.IndexOutOfRangeException) { }
		try { ReadFloatingArrayDouble(doubles, 8); return 2; }
		catch (System.IndexOutOfRangeException) { }
		try { StoreFloatingArraySingle(singles, 8, 0f); return 3; }
		catch (System.IndexOutOfRangeException) { }
		try { StoreFloatingArrayDouble(doubles, -1, 0d); return 4; }
		catch (System.IndexOutOfRangeException) { }
		for (var index = 0; index < 8; index++)
			if (ReadFloatingArraySingle(singles, index) != FloatingArraySingleBits(index) ||
				ReadFloatingArrayDouble(doubles, index) != FloatingArrayDoubleBits(index)) return 300 + index;
		return 42;
	}

	private static uint FloatingArraySingleBits(int index) => index switch
	{ 0 => 0u, 1 => 0x80000000u, 2 => 1u, 3 => 0x7F7FFFFFu, 4 => 0x7F800000u, 5 => 0xFF800000u, 6 => 0x7FC12345u, _ => 0xFFA12345u };
	private static ulong FloatingArrayDoubleBits(int index) => index switch
	{ 0 => 0ul, 1 => 0x8000000000000000ul, 2 => 1ul, 3 => 0x7FEFFFFFFFFFFFFFul, 4 => 0x7FF0000000000000ul,
		5 => 0xFFF0000000000000ul, 6 => 0x7FF8123456789ABCul, _ => 0xFFF0123456789ABCul };

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StoreFloatingArraySingle(float[] values, int index, float value) => values[index] = value;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StoreFloatingArrayDouble(double[] values, int index, double value) => values[index] = value;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadFloatingArraySingle(float[] values, int index) => Unsafe.BitCast<float, uint>(values[index]);
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ulong ReadFloatingArrayDouble(double[] values, int index) => Unsafe.BitCast<double, ulong>(values[index]);
}
