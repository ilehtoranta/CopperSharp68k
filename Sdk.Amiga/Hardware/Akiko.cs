/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>CD32 Akiko register offsets. Multi-byte registers use big-endian bus order.</summary>
public enum AkikoRegister : byte
{
	Identification = 0x00,
	CdInterruptRequest = 0x04,
	CdInterruptEnable = 0x08,
	CdInterruptEnableReadback = 0x0C,
	CdDataDmaAddress = 0x10,
	CdMiscDmaAddress = 0x14,
	CdDmaControl = 0x18,
	CdPacketBuffer = 0x20,
	CdFlags = 0x24,
	CdCommandData = 0x28,
	NvRam = 0x30,
	ChunkyToPlanar = 0x38,
}

public readonly struct Akiko
{
	private const uint BaseAddress = 0x00B80000;

	public static byte ReadByte(AkikoRegister register) =>
		APTR.ReadUInt8(APTR.FromPointer(BaseAddress), (int)register);

	public static ushort ReadUInt16(AkikoRegister register) =>
		APTR.ReadUInt16(APTR.FromPointer(BaseAddress), (int)register);

	public static uint ReadUInt32(AkikoRegister register) =>
		APTR.ReadUInt32(APTR.FromPointer(BaseAddress), (int)register);

	public static void WriteByte(AkikoRegister register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(BaseAddress), (int)register, value);

	public static void WriteUInt32(AkikoRegister register, uint value) =>
		APTR.WriteUInt32(APTR.FromPointer(BaseAddress), (int)register, value);
}
