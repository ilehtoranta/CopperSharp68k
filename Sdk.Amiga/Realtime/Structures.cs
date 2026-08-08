/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Conductor
{
	public const uint Size = 54;

	public Node Link;
	public ushort Reserved0;
	public MinList Players;
	public uint ClockTime;
	public uint StartTime;
	public uint ExternalTime;
	public uint MaximumExternalTime;
	public uint Metronome;
	public ushort Reserved1;
	public ushort Flags;
	public byte State;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Player
{
	public const uint Size = 44;

	public Node Link;
	public byte Reserved0;
	public byte Reserved1;
	public APTR Hook;
	public APTR Source;
	public APTR Task;
	public int MetricTime;
	public int AlarmTime;
	public APTR UserData;
	public ushort PlayerId;
	public ushort Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct PlayerTimeMessage
{
	public const uint Size = 8;

	public uint Method;
	public uint Time;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct PlayerStateMessage
{
	public const uint Size = 8;

	public uint Method;
	public uint OldState;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RealTimeBase
{
	public const uint Size = 48;

	public Library Library;
	public unsafe fixed byte Reserved0[2];
	public uint Time;
	public uint TimeFraction;
	public ushort Reserved1;
	public short TickError;
}
