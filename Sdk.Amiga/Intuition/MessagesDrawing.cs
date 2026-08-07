/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IntuiMessage
{
	public const uint Size = 52;

	public Message ExecMessage;
	public IDCMPFlags Class;
	public ushort Code;
	public ushort Qualifier;
	public APTR IAddress;
	public short MouseX;
	public short MouseY;
	public uint Seconds;
	public uint Micros;
	public APTR IDCMPWindow;
	public APTR SpecialLink;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtIntuiMessage
{
	public const uint Size = 56;

	public IntuiMessage IntuiMessage;
	public APTR TabletData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IntuiText
{
	public const uint Size = 20;

	public byte FrontPen;
	public byte BackPen;
	public DrawMode DrawMode;
	private byte _padding;
	public short LeftEdge;
	public short TopEdge;
	public APTR Font;
	public STRPTR Text;
	public APTR NextText;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Border
{
	public const uint Size = 16;

	public short LeftEdge;
	public short TopEdge;
	public byte FrontPen;
	public byte BackPen;
	public DrawMode DrawMode;
	public sbyte Count;
	public APTR XY;
	public APTR NextBorder;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Image
{
	public const uint Size = 20;

	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public short Depth;
	public APTR ImageData;
	public byte PlanePick;
	public byte PlaneOnOff;
	public APTR NextImage;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IBox
{
	public const uint Size = 8;

	public short Left;
	public short Top;
	public short Width;
	public short Height;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TabletData
{
	public const uint Size = 24;

	public ushort XFraction;
	public ushort YFraction;
	public uint TabletX;
	public uint TabletY;
	public uint RangeX;
	public uint RangeY;
	public APTR TagList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TabletHookData
{
	public const uint Size = 16;

	public APTR Screen;
	public uint Width;
	public uint Height;
	public int ScreenChanged;
}
