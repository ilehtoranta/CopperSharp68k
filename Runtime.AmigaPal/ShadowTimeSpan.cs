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
public readonly struct ShadowTimeSpan
{
#pragma warning disable CS0649 // Populated through the representation-compatible public receiver.
	private readonly uint _ticksHigh;
	private readonly uint _ticksLow;
#pragma warning restore CS0649

	[MethodImpl(MethodImplOptions.NoInlining)]
	public long GetTicks() => M68kRuntime.CombineInt64(_ticksHigh, _ticksLow);

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

	private static void Read(TimeSpan value, out uint high, out uint low)
	{
		var address = AddressOf(ref value);
		high = Amiga.APTR.ReadUInt32(address, 0);
		low = Amiga.APTR.ReadUInt32(address, 4);
	}

	private static Amiga.APTR AddressOf(ref TimeSpan value) =>
		throw new NotSupportedException(
			"ShadowTimeSpan.AddressOf is lowered by CopperSharp.");
}
