/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

public enum GayleRegister : ushort
{
	CardStatus = 0x0000,
	InterruptRequest = 0x1000,
	InterruptEnable = 0x2000,
	CardConfiguration = 0x3000,
}

[Flags]
public enum GayleStatusFlags : byte
{
	PcmciaDisabled = 1 << 0,
	DigitalAudioEnabled = 1 << 1,
	CardBusyOrInterrupt = 1 << 2,
	WriteEnabled = 1 << 3,
	BatteryVoltage2OrDigitalAudio = 1 << 4,
	BatteryVoltage1OrStatusChanged = 1 << 5,
	CardDetected = 1 << 6,
	IdeInterrupt = 1 << 7,
}

[Flags]
public enum GayleInterruptFlags : byte
{
	BusErrorAfterCardChange = 1 << 0,
	ResetAfterCardChange = 1 << 1,
	CardBusyOrInterrupt = 1 << 2,
	WriteEnableChanged = 1 << 3,
	BatteryVoltage2OrDigitalAudioChanged = 1 << 4,
	BatteryVoltage1OrStatusChanged = 1 << 5,
	CardDetectChanged = 1 << 6,
	Ide = 1 << 7,
}

/// <summary>Gayle PCMCIA/IDE control registers used by the A600 and A1200.</summary>
public readonly struct Gayle
{
	private const uint BaseAddress = 0x00DA8000;
	public const uint PcmciaMemoryBase = 0x00600000;
	public const uint PcmciaAttributeBase = 0x00A00000;
	public const uint PcmciaIoBase = 0x00A20000;
	public const uint PcmciaOddIoBase = 0x00A30000;
	public const uint PcmciaResetAddress = 0x00A40000;

	public static byte Read(GayleRegister register) =>
		APTR.ReadUInt8(APTR.FromPointer(BaseAddress), (int)register);

	public static void Write(GayleRegister register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(BaseAddress), (int)register, value);
}
