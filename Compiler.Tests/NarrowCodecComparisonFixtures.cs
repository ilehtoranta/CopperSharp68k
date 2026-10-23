/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

/// <summary>Promoted narrow CIL operands compared with signed Int32 thresholds.</summary>
public static class NarrowCodecComparisonFixtures
{
	public static int Entry()
	{
		if (ByteMaterialized(65, 128) != 11 || ByteBranched(65, 128) != 11) return 1;
		if (ByteMaterialized(128, 128) != 22 || ByteBranched(128, 128) != 22) return 2;
		if (ByteMaterialized(255, 256) != 11 || ByteBranched(255, 256) != 11) return 3;
		if (ByteMaterialized(255, -1) != 56 || ByteBranched(255, -1) != 56) return 4;
		if (CharMaterialized('\u8000', 32767) != 56 || CharBranched('\u8000', 32767) != 56) return 5;
		if (CharMaterialized('\uffff', 65536) != 11 || CharBranched('\uffff', 65536) != 11) return 6;
		if (SByteMaterialized(-1, 128) != 11 || SByteBranched(-1, 128) != 11) return 7;
		if (SByteMaterialized(127, -1) != 56 || SByteBranched(127, -1) != 56) return 8;
		return 42;
	}

	public static uint GeneratedEntry()
	{
		byte[] bytes = [0, 1, 65, 127, 128, 194, 255];
		char[] chars = ['\0', '\u0001', '\u007f', '\u0080', '\u7fff', '\u8000', '\uffff'];
		sbyte[] signed = [-128, -1, 0, 1, 127];
		int[] thresholds = [-129, -1, 0, 1, 65, 127, 128, 194, 255, 256, 32767, 32768, 65535, 65536];
		uint hash = 2166136261u;
		for (var i = 0; i < thresholds.Length; i++)
		{
			var threshold = thresholds[i];
			for (var j = 0; j < bytes.Length; j++)
			{
				hash = Mix(hash, (uint)ByteMaterialized(bytes[j], threshold));
				hash = Mix(hash, (uint)ByteBranched(bytes[j], threshold));
			}
			for (var j = 0; j < chars.Length; j++)
			{
				hash = Mix(hash, (uint)CharMaterialized(chars[j], threshold));
				hash = Mix(hash, (uint)CharBranched(chars[j], threshold));
			}
			for (var j = 0; j < signed.Length; j++)
			{
				hash = Mix(hash, (uint)SByteMaterialized(signed[j], threshold));
				hash = Mix(hash, (uint)SByteBranched(signed[j], threshold));
			}
		}
		return hash;
	}

	private static uint Mix(uint hash, uint value) => unchecked((hash ^ value) * 16777619u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ByteMaterialized(byte value, int threshold)
	{
		var lt = value < threshold;
		var le = value <= threshold;
		var eq = value == threshold;
		var ne = value != threshold;
		var ge = value >= threshold;
		var gt = value > threshold;
		return (lt ? 1 : 0) | (le ? 2 : 0) | (eq ? 4 : 0) | (ne ? 8 : 0) | (ge ? 16 : 0) | (gt ? 32 : 0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ByteBranched(byte value, int threshold)
	{
		var bits = 0;
		if (value < threshold) bits |= 1;
		if (value <= threshold) bits |= 2;
		if (value == threshold) bits |= 4;
		if (value != threshold) bits |= 8;
		if (value >= threshold) bits |= 16;
		if (value > threshold) bits |= 32;
		return bits;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CharMaterialized(char value, int threshold)
	{
		var lt = value < threshold;
		var le = value <= threshold;
		var eq = value == threshold;
		var ne = value != threshold;
		var ge = value >= threshold;
		var gt = value > threshold;
		return (lt ? 1 : 0) | (le ? 2 : 0) | (eq ? 4 : 0) | (ne ? 8 : 0) | (ge ? 16 : 0) | (gt ? 32 : 0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CharBranched(char value, int threshold)
	{
		var bits = 0;
		if (value < threshold) bits |= 1;
		if (value <= threshold) bits |= 2;
		if (value == threshold) bits |= 4;
		if (value != threshold) bits |= 8;
		if (value >= threshold) bits |= 16;
		if (value > threshold) bits |= 32;
		return bits;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SByteMaterialized(sbyte value, int threshold)
	{
		var lt = value < threshold;
		var le = value <= threshold;
		var eq = value == threshold;
		var ne = value != threshold;
		var ge = value >= threshold;
		var gt = value > threshold;
		return (lt ? 1 : 0) | (le ? 2 : 0) | (eq ? 4 : 0) | (ne ? 8 : 0) | (ge ? 16 : 0) | (gt ? 32 : 0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SByteBranched(sbyte value, int threshold)
	{
		var bits = 0;
		if (value < threshold) bits |= 1;
		if (value <= threshold) bits |= 2;
		if (value == threshold) bits |= 4;
		if (value != threshold) bits |= 8;
		if (value >= threshold) bits |= 16;
		if (value > threshold) bits |= 32;
		return bits;
	}
}
