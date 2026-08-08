/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum LowLevelKeyFlags : uint
{
	LeftShift = 1u << 16,
	RightShift = 1u << 17,
	CapsLock = 1u << 18,
	Control = 1u << 19,
	LeftAlt = 1u << 20,
	RightAlt = 1u << 21,
	LeftAmiga = 1u << 22,
	RightAmiga = 1u << 23,
}

public enum LowLevelJoyPortType : uint
{
	NotAvailable = 0u << 28,
	GameController = 1u << 28,
	Mouse = 2u << 28,
	Joystick = 3u << 28,
	Unknown = 4u << 28,
}

[System.Flags]
public enum LowLevelJoystickButtons : uint
{
	Blue = 1u << 23,
	Red = 1u << 22,
	Yellow = 1u << 21,
	Green = 1u << 20,
	Forward = 1u << 19,
	Reverse = 1u << 18,
	Play = 1u << 17,
}

[System.Flags]
public enum LowLevelJoystickDirections : uint
{
	Up = 1u << 3,
	Down = 1u << 2,
	Left = 1u << 1,
	Right = 1u,
}

public enum LowLevelLanguage : uint
{
	Unknown = 0,
	American = 1,
	English = 2,
	German = 3,
	French = 4,
	Spanish = 5,
	Italian = 6,
	Portuguese = 7,
	Danish = 8,
	Dutch = 9,
	Norwegian = 10,
	Finnish = 11,
	Swedish = 12,
	Japanese = 13,
	Chinese = 14,
	Arabic = 15,
	Greek = 16,
	Hebrew = 17,
	Korean = 18,
}
