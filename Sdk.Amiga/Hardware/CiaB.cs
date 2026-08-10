/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>Allocation-free handle for the CIA-B register block.</summary>
public readonly struct CiaB
{
	private const uint BaseAddress = 0x00BFD000;

	public static byte Read(CiaRegister register) =>
		APTR.ReadUInt8(APTR.FromPointer(BaseAddress), (int)register << 8);

	public static void Write(CiaRegister register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(BaseAddress), (int)register << 8, value);
}
