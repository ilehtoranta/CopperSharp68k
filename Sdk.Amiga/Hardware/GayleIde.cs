/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

public enum AtaRegister : byte
{
	Data,
	ErrorOrFeatures,
	SectorCount,
	SectorNumber,
	CylinderLow,
	CylinderHigh,
	DeviceHead,
	StatusOrCommand,
}

/// <summary>Gayle-compatible PATA register access for A600/A1200 and A4000-family layouts.</summary>
public readonly struct GayleIde
{
	public const uint A600A1200BaseAddress = 0x00DA0000;
	public const uint A600A1200InterruptAddress = 0x00DA9000;
	public const uint A4000BaseAddress = 0x00DD2020;
	public const uint A4000InterruptAddress = 0x00DD3020;
	private const int AlternateStatusOffset = 0x101A;

	public static ushort ReadData(uint baseAddress) =>
		APTR.ReadUInt16(APTR.FromPointer(baseAddress), 0);

	public static void WriteData(uint baseAddress, ushort value) =>
		APTR.WriteUInt16(APTR.FromPointer(baseAddress), 0, value);

	public static byte Read(uint baseAddress, AtaRegister register) =>
		register == AtaRegister.Data
			? (byte)ReadData(baseAddress)
			: APTR.ReadUInt8(APTR.FromPointer(baseAddress), 2 + ((int)register << 2));

	public static void Write(uint baseAddress, AtaRegister register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(baseAddress), 2 + ((int)register << 2), value);

	public static byte ReadAlternateStatus(uint baseAddress) =>
		APTR.ReadUInt8(APTR.FromPointer(baseAddress), AlternateStatusOffset);

	public static void WriteDeviceControl(uint baseAddress, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(baseAddress), AlternateStatusOffset, value);
}
