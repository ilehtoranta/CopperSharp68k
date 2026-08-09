/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

using CopperSharp.Compiler;

/// <summary>Eight-byte high/low counter written by timer.device ReadEClock.</summary>
[M68kStackAlignment(4)]
[M68kUninitializedStorage]
public struct EClockValue
{
	public const int SizeInBytes = 8;

	public uint High;
	public uint Low;

	public static APTR AddressOf(ref EClockValue value) =>
		throw new System.NotSupportedException(
			"EClockValue.AddressOf is lowered by CopperSharp.");
}
