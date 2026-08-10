/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>
/// Width-explicit access for model-specific or expansion hardware that does
/// not have a stable shared register layout. No probing or ownership is done.
/// </summary>
public readonly struct HardwareBus
{
	public static byte ReadUInt8(uint address) => APTR.ReadUInt8(APTR.FromPointer(address), 0);
	public static ushort ReadUInt16(uint address) => APTR.ReadUInt16(APTR.FromPointer(address), 0);
	public static uint ReadUInt32(uint address) => APTR.ReadUInt32(APTR.FromPointer(address), 0);
	public static void WriteUInt8(uint address, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(address), 0, value);
	public static void WriteUInt16(uint address, ushort value) =>
		APTR.WriteUInt16(APTR.FromPointer(address), 0, value);
	public static void WriteUInt32(uint address, uint value) =>
		APTR.WriteUInt32(APTR.FromPointer(address), 0, value);
}
