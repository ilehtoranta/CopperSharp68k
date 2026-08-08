/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct FontContents
{
	public const uint Size = 260;

	public fixed byte FileName[256];
	public ushort YSize;
	public byte Style;
	public byte Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct TFontContents
{
	public const uint Size = 260;

	public fixed byte FileName[254];
	public ushort TagCount;
	public ushort YSize;
	public byte Style;
	public byte Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FontContentsHeader
{
	public const uint Size = 4;

	public ushort FileId;
	public ushort NumberOfEntries;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct DiskFontHeader
{
	public const uint Size = 106;

	public Node Node;
	public ushort FileId;
	public ushort Revision;
	public int Segment;
	public fixed byte Name[32];
	public TextFont TextFont;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AvailFonts
{
	public const uint Size = 10;

	public ushort Type;
	public TextAttr Attribute;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TAvailFonts
{
	public const uint Size = 14;

	public ushort Type;
	public TTextAttr Attribute;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AvailFontsHeader
{
	public const uint Size = 2;

	public ushort NumberOfEntries;
}
