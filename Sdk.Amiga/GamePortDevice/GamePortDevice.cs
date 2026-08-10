/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

public static class GamePortDevice
{
	public const string Name = "gameport.device";
	public const short Open = -6;
	public const short Close = -12;
	public const short Expunge = -18;
	public const short ExtFunc = -24;
	public const short BeginIO = -30;
	public const short AbortIO = -36;
}

public enum GamePortCommand : ushort
{
	ReadEvent = 9,
	AskControllerType = 10,
	SetControllerType = 11,
	AskTrigger = 12,
	SetTrigger = 13,
}

public enum GamePortControllerType : sbyte
{
	Allocated = -1,
	NoController = 0,
	Mouse = 1,
	RelativeJoystick = 2,
	AbsoluteJoystick = 3,
}

[System.Flags]
public enum GamePortTriggerFlags : ushort
{
	None = 0,
	DownKeys = 1 << 0,
	UpKeys = 1 << 1,
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct GamePortTrigger
{
	public const uint Size = 8;

	public GamePortTriggerFlags Keys;
	public ushort Timeout;
	public ushort XDelta;
	public ushort YDelta;
}

public static class GamePortTriggerLayout
{
	public const int Keys = 0;
	public const int Timeout = 2;
	public const int XDelta = 4;
	public const int YDelta = 6;
}
