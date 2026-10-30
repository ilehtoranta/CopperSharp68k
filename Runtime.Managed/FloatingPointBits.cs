/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Bit-preserving floating-point projections through the target word-pair ABI.</summary>
public static class ShadowFloatingPointBits
{
	public static ulong DoubleToUInt64(double value)
	{
		var low = M68kRuntime.SplitDouble(value, out var high);
		return ((ulong)high << 32) | low;
	}

	public static long DoubleToInt64(double value) => unchecked((long)DoubleToUInt64(value));
	public static double UInt64ToDouble(ulong value) => M68kRuntime.CombineDouble((uint)(value >> 32), (uint)value);
	public static double Int64ToDouble(long value) => UInt64ToDouble(unchecked((ulong)value));
}
