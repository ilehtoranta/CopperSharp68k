/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>MOS 8520 register number. Amiga CIA registers are spaced 0x100 bytes apart.</summary>
public enum CiaRegister : byte
{
	PortA = 0x0,
	PortB = 0x1,
	DataDirectionA = 0x2,
	DataDirectionB = 0x3,
	TimerALow = 0x4,
	TimerAHigh = 0x5,
	TimerBLow = 0x6,
	TimerBHigh = 0x7,
	TimeOfDayTenths = 0x8,
	TimeOfDaySeconds = 0x9,
	TimeOfDayMinutes = 0xA,
	TimeOfDayHours = 0xB,
	SerialData = 0xC,
	InterruptControl = 0xD,
	ControlA = 0xE,
	ControlB = 0xF,
}

[Flags]
public enum CiaInterruptFlags : byte
{
	None = 0,
	TimerA = 1 << 0,
	TimerB = 1 << 1,
	Alarm = 1 << 2,
	SerialPort = 1 << 3,
	FlagPin = 1 << 4,
	Sources = TimerA | TimerB | Alarm | SerialPort | FlagPin,
	SetClear = 1 << 7,
	InterruptOccurred = 1 << 7,
}

[Flags]
public enum CiaControlAFlags : byte
{
	None = 0,
	Start = 1 << 0,
	PulseOutputOnPortB = 1 << 1,
	ToggleOutput = 1 << 2,
	OneShot = 1 << 3,
	ForceLoad = 1 << 4,
	CountCntPulses = 1 << 5,
	SerialOutput = 1 << 6,
	TimeOfDay50Hz = 1 << 7,
}

[Flags]
public enum CiaControlBFlags : byte
{
	None = 0,
	Start = 1 << 0,
	PulseOutputOnPortB = 1 << 1,
	ToggleOutput = 1 << 2,
	OneShot = 1 << 3,
	ForceLoad = 1 << 4,
	CountTimerAUnderflows = 1 << 5,
	CountTimerAUnderflowsWhileCntHigh = 3 << 5,
	AlarmWrite = 1 << 7,
}

[Flags]
public enum CiaAPortAFlags : byte
{
	Overlay = 1 << 0,
	PowerLed = 1 << 1,
	DiskChanged = 1 << 2,
	DiskWriteProtected = 1 << 3,
	DiskTrackZero = 1 << 4,
	DiskReady = 1 << 5,
	Joystick0Fire = 1 << 6,
	Joystick1Fire = 1 << 7,
}

[Flags]
public enum CiaBPortBFlags : byte
{
	DiskStep = 1 << 0,
	DiskDirection = 1 << 1,
	DiskSide = 1 << 2,
	DiskSelect0 = 1 << 3,
	DiskSelect1 = 1 << 4,
	DiskSelect2 = 1 << 5,
	DiskSelect3 = 1 << 6,
	DiskMotor = 1 << 7,
}
