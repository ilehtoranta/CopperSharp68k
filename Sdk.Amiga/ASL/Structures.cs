/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FileRequester
{
	public const uint Size = 56;

	public unsafe fixed byte Reserved0[4];
	public STRPTR File;
	public STRPTR Drawer;
	public unsafe fixed byte Reserved1[10];
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public unsafe fixed byte Reserved2[2];
	public int NumberOfArguments;
	public APTR ArgumentList;
	public APTR UserData;
	public unsafe fixed byte Reserved3[8];
	public STRPTR Pattern;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FontRequester
{
	public const uint Size = 44;

	public unsafe fixed byte Reserved0[8];
	public TextAttr Attribute;
	public byte ForegroundPen;
	public byte BackgroundPen;
	public byte DrawMode;
	public byte Reserved1;
	public APTR UserData;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public TTextAttr ExtendedAttribute;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ScreenModeRequester
{
	public const uint Size = 52;

	public uint DisplayId;
	public uint DisplayWidth;
	public uint DisplayHeight;
	public ushort DisplayDepth;
	public ushort OverscanType;
	public int AutoScroll;
	public uint BitMapWidth;
	public uint BitMapHeight;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public int InfoOpened;
	public short InfoLeftEdge;
	public short InfoTopEdge;
	public short InfoWidth;
	public short InfoHeight;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DisplayMode
{
	public const uint Size = 106;

	public Node Node;
	public DimensionInfo DimensionInfo;
	public uint PropertyFlags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AslSemaphore
{
	public const uint Size = 62;

	public SignalSemaphore Semaphore;
	public ushort Version;
	public uint SizeInBytes;
	public byte SortBy;
	public byte SortDrawers;
	public byte SortOrder;
	public byte SizePosition;
	public short RelativeLeft;
	public short RelativeTop;
	public byte RelativeWidth;
	public byte RelativeHeight;
}
