/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>
/// Byte registers in the A3000/A4000 motherboard-resource window. These
/// controller registers are model-specific and partly undocumented.
/// </summary>
public enum MotherboardResourceRegister : ushort
{
	FatGaryTimeout = 0x0000,
	FatGaryTimeoutEnable = 0x0001,
	FatGaryColdBoot = 0x0002,
	RamseyConfiguration = 0x0003,
	RamseyRevision = 0x0043,
}

public readonly struct MotherboardResources
{
	private const uint BaseAddress = 0x00DE0000;

	public static byte Read(MotherboardResourceRegister register) =>
		APTR.ReadUInt8(APTR.FromPointer(BaseAddress), (int)register);

	public static void Write(MotherboardResourceRegister register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(BaseAddress), (int)register, value);
}
