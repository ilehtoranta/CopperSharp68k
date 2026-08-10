/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>Allocation-free handle for the CIA-A register block.</summary>
public readonly struct CiaA
{
	private const uint BaseAddress = 0x00BFE001;
	private const byte LeftMouseButton = 1 << 6;

	public static byte ReadPortA() => APTR.ReadUInt8(APTR.FromPointer(BaseAddress), 0);

	public static bool IsLeftMouseButtonPressed() => (ReadPortA() & LeftMouseButton) == 0;
}
