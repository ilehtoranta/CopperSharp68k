/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Menu
{
	public const uint Size = 30;

	public APTR NextMenu;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public MenuFlags Flags;
	public STRPTR MenuName;
	public APTR FirstItem;
	public short JazzX;
	public short JazzY;
	public short BeatX;
	public short BeatY;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MenuItem
{
	public const uint Size = 34;

	public APTR NextItem;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public MenuItemFlags Flags;
	public int MutualExclude;
	public APTR ItemFill;
	public APTR SelectFill;
	public sbyte Command;
	private byte _padding;
	public APTR SubItem;
	public ushort NextSelect;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct Requester
{
	public const uint Size = 112;

	public APTR OlderRequest;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public short RelativeLeft;
	public short RelativeTop;
	public APTR Gadget;
	public APTR Border;
	public APTR Text;
	public RequesterFlags Flags;
	public byte BackFill;
	private byte _padding;
	public APTR Layer;
	public fixed byte RequesterPadding1[32];
	public APTR ImageBitMap;
	public APTR Window;
	public APTR Image;
	public fixed byte RequesterPadding2[32];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Remember
{
	public const uint Size = 12;

	public APTR NextRemember;
	public uint RememberSize;
	public APTR Memory;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct EasyStruct
{
	public const uint Size = 20;

	public uint StructureSize;
	public uint Flags;
	public STRPTR Title;
	public STRPTR TextFormat;
	public STRPTR GadgetFormat;
}
