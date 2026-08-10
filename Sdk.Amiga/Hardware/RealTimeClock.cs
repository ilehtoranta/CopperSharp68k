/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>Register order used by the A2000-style MSM6242B clock.</summary>
public enum Rtc2000Register : byte
{
	SecondsOnes,
	SecondsTens,
	MinutesOnes,
	MinutesTens,
	HoursOnes,
	HoursTens,
	DayOnes,
	DayTens,
	MonthOnes,
	MonthTens,
	YearOnes,
	YearTens,
	Weekday,
	Control1,
	Control2,
	Control3,
}

/// <summary>Register order used by the A3000/A4000-style clock.</summary>
public enum Rtc3000Register : byte
{
	SecondsOnes,
	SecondsTens,
	MinutesOnes,
	MinutesTens,
	HoursOnes,
	HoursTens,
	Weekday,
	DayOnes,
	DayTens,
	MonthOnes,
	MonthTens,
	YearOnes,
	YearTens,
	Control1,
	Control2,
	Control3,
}

[Flags]
public enum Rtc2000Control1Flags : byte
{
	Hold = 1 << 0,
	Busy = 1 << 1,
}

[Flags]
public enum Rtc2000Control3Flags : byte
{
	Hour24Mode = 1 << 2,
}

/// <summary>A2000-style RTC. Each four-bit register occupies the low nibble of a 32-bit slot.</summary>
public readonly struct RealTimeClock2000
{
	private const uint BaseAddress = 0x00DC0000;

	public static byte Read(Rtc2000Register register) =>
		(byte)(APTR.ReadUInt8(APTR.FromPointer(BaseAddress), ((int)register << 2) + 3) & 0x0F);

	public static void Write(Rtc2000Register register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(BaseAddress), ((int)register << 2) + 3, (byte)(value & 0x0F));
}

/// <summary>A3000/A4000-style RTC. Each four-bit register occupies the low nibble of a 32-bit slot.</summary>
public readonly struct RealTimeClock3000
{
	private const uint BaseAddress = 0x00DC0000;

	public static byte Read(Rtc3000Register register) =>
		(byte)(APTR.ReadUInt8(APTR.FromPointer(BaseAddress), ((int)register << 2) + 3) & 0x0F);

	public static void Write(Rtc3000Register register, byte value) =>
		APTR.WriteUInt8(APTR.FromPointer(BaseAddress), ((int)register << 2) + 3, (byte)(value & 0x0F));
}
