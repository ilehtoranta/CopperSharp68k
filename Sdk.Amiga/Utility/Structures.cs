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

	/// <summary>A terminating tag item for a stack-allocated tag list.</summary>
	public static TagItem Done => default;

	/// <summary>Creates one tag item with a raw 32-bit payload.</summary>
	public static TagItem Create(uint tag, uint data) => new() { Tag = tag, Data = data };

	/// <summary>
	/// Returns the native address of the first item in a stack-allocated tag
	/// list. Keep the backing storage alive for the complete native call and end
	/// the list with <see cref="Done"/>.
	/// </summary>
	public static APTR AddressOf(ref TagItem tagItem) =>
		throw new System.NotSupportedException(
			"TagItem.AddressOf is lowered by CopperSharp.");
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
