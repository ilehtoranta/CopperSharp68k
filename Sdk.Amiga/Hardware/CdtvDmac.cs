/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>CDTV DMAC, CD-ROM and transfer-processor register offsets.</summary>
public enum CdtvDmacRegister : byte
{
	InterruptStatus = 0x41,
	Control = 0x43,
	WordTransferCount = 0x80,
	AddressCounter = 0x84,
	DmaAddressWrap = 0x8E,
	ScsiAuxiliaryStatus = 0x91,
	ScsiData = 0x93,
	CdCommand = 0xA1,
	DmaStart = 0xE0,
	DmaStop = 0xE2,
	InterruptClear = 0xE4,
	Flush = 0xE8,
}

public readonly struct CdtvDmac
{
	/// <summary>Autoconfig assigns the board base; pass that base to each operation.</summary>
	public static byte Read(uint baseAddress, CdtvDmacRegister register) =>
		APTR.ReadUInt8(APTR.FromPointer(baseAddress), (int)register);

	public static void Write(uint baseAddress, CdtvDmacRegister register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(baseAddress), (int)register, value);
}
