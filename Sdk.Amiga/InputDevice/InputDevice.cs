/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

public static class InputDevice
{
	public const string Name = "input.device";
	public const short BeginIO = -30;
}

public enum InputDeviceCommand : ushort
{
	AddHandler = 9,
	RemoveHandler = 10,
	WriteEvent = 11,
	SetThreshold = 12,
	SetPeriod = 13,
	SetMousePort = 14,
	SetMouseType = 15,
	SetMouseTrigger = 16,
	AddEvent = 24,
}

public enum InputEventClass : byte
{
	Null = 0,
	RawKey = 1,
	RawMouse = 2,
	Event = 3,
	PointerPosition = 4,
	Timer = 6,
	GadgetDown = 7,
	GadgetUp = 8,
	Requester = 9,
	MenuList = 10,
	CloseWindow = 11,
	SizeWindow = 12,
	RefreshWindow = 13,
	NewPreferences = 14,
	DiskRemoved = 15,
	DiskInserted = 16,
	ActiveWindow = 17,
	InactiveWindow = 18,
	NewPointerPosition = 19,
	MenuHelp = 20,
	ChangeWindow = 21,
	NewMouse = 22,
}

public enum InputEventSubClass : byte
{
	Compatible = 0,
	Pixel = 1,
	Tablet = 2,
	NewTablet = 3,
}

[System.Flags]
public enum InputEventQualifier : ushort
{
	None = 0,
	LeftShift = 1 << 0,
	RightShift = 1 << 1,
	CapsLock = 1 << 2,
	Control = 1 << 3,
	LeftAlt = 1 << 4,
	RightAlt = 1 << 5,
	LeftCommand = 1 << 6,
	RightCommand = 1 << 7,
	NumericPad = 1 << 8,
	Repeat = 1 << 9,
	Interrupt = 1 << 10,
	MultiBroadcast = 1 << 11,
	MiddleButton = 1 << 12,
	RightButton = 1 << 13,
	LeftButton = 1 << 14,
	RelativeMouse = 1 << 15,
}

public static class InputEventCode
{
	public const ushort UpPrefix = 0x80;
	public const ushort LeftButton = 0x68;
	public const ushort RightButton = 0x69;
	public const ushort MiddleButton = 0x6A;
	public const ushort NoButton = 0xFF;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct InputEvent
{
	public const uint Size = 22;

	public APTR NextEvent;
	public InputEventClass Class;
	public InputEventSubClass SubClass;
	public ushort Code;
	public InputEventQualifier Qualifier;
	public int Position;
	public TimeVal TimeStamp;
}

public static class InputEventLayout
{
	public const int NextEvent = 0;
	public const int Class = 4;
	public const int SubClass = 5;
	public const int Code = 6;
	public const int Qualifier = 8;
	public const int Position = 10;
	public const int X = 10;
	public const int Y = 12;
	public const int EventAddress = 10;
	public const int TimeStamp = 14;
	public const int Seconds = 14;
	public const int Microseconds = 18;
}
