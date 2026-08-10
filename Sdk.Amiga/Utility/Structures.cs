/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TagItem
{
	public const uint Size = 8;
	public uint Tag;
	public uint Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ClockData
{
	public const uint Size = 14;
	public ushort Second;
	public ushort Minute;
	public ushort Hour;
	public ushort Day;
	public ushort Month;
	public ushort Year;
	public ushort WeekDay;
}

public enum UtilityTag : uint
{
	Done = 0,
	Ignore = 1,
	More = 2,
	Skip = 3,
	User = 0x8000_0000,
}
