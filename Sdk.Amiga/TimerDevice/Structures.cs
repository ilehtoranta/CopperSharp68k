/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TimeVal
{
	public const uint Size = 8;

	public uint Seconds;
	public uint Microseconds;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct EClockVal
{
	public const uint Size = 8;

	public uint High;
	public uint Low;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TimerRequest
{
	public const uint Size = 40;

	public IORequest Request;
	public TimeVal Time;
}
