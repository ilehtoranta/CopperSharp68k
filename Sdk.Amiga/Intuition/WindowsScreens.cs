/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewWindow
{
	public const uint Size = 48;

	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public byte DetailPen;
	public byte BlockPen;
	public IDCMPFlags IDCMPFlags;
	public WindowFlags Flags;
	public APTR FirstGadget;
	public APTR CheckMark;
	public STRPTR Title;
	public APTR Screen;
	public APTR BitMap;
	public short MinWidth;
	public short MinHeight;
	public ushort MaxWidth;
	public ushort MaxHeight;
	public ScreenType Type;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtNewWindow
{
	public const uint Size = 52;

	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public byte DetailPen;
	public byte BlockPen;
	public IDCMPFlags IDCMPFlags;
	public WindowFlags Flags;
	public APTR FirstGadget;
	public APTR CheckMark;
	public STRPTR Title;
	public APTR Screen;
	public APTR BitMap;
	public short MinWidth;
	public short MinHeight;
	public ushort MaxWidth;
	public ushort MaxHeight;
	public ScreenType Type;
	public APTR Extension;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Window
{
	public const uint Size = 136;

	public APTR NextWindow;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public short MouseY;
	public short MouseX;
	public short MinWidth;
	public short MinHeight;
	public ushort MaxWidth;
	public ushort MaxHeight;
	public WindowFlags Flags;
	public APTR MenuStrip;
	public STRPTR Title;
	public APTR FirstRequest;
	public APTR DMRequest;
	public short RequesterCount;
	public APTR Screen;
	public APTR RastPort;
	public sbyte BorderLeft;
	public sbyte BorderTop;
	public sbyte BorderRight;
	public sbyte BorderBottom;
	public APTR BorderRastPort;
	public APTR FirstGadget;
	public APTR Parent;
	public APTR Descendant;
	public APTR Pointer;
	public sbyte PointerHeight;
	public sbyte PointerWidth;
	public sbyte XOffset;
	public sbyte YOffset;
	public IDCMPFlags IDCMPFlags;
	public APTR UserPort;
	public APTR WindowPort;
	public APTR MessageKey;
	public byte DetailPen;
	public byte BlockPen;
	public APTR CheckMark;
	public STRPTR ScreenTitle;
	public short GzzMouseX;
	public short GzzMouseY;
	public short GzzWidth;
	public short GzzHeight;
	public APTR ExtData;
	public APTR UserData;
	public APTR Layer;
	public APTR Font;
	public uint MoreFlags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewScreen
{
	public const uint Size = 32;

	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public short Depth;
	public byte DetailPen;
	public byte BlockPen;
	public ScreenViewModes ViewModes;
	public ScreenType Type;
	public APTR Font;
	public STRPTR DefaultTitle;
	public APTR Gadgets;
	public APTR CustomBitMap;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtNewScreen
{
	public const uint Size = 36;

	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public short Depth;
	public byte DetailPen;
	public byte BlockPen;
	public ScreenViewModes ViewModes;
	public ScreenType Type;
	public APTR Font;
	public STRPTR DefaultTitle;
	public APTR Gadgets;
	public APTR CustomBitMap;
	public APTR Extension;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Screen
{
	public const uint Size = 346;

	public APTR NextScreen;
	public APTR FirstWindow;
	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public short MouseY;
	public short MouseX;
	public ScreenFlags Flags;
	public STRPTR Title;
	public STRPTR DefaultTitle;
	public sbyte BarHeight;
	public sbyte BarVBorder;
	public sbyte BarHBorder;
	public sbyte MenuVBorder;
	public sbyte MenuHBorder;
	public sbyte WindowBorderTop;
	public sbyte WindowBorderLeft;
	public sbyte WindowBorderRight;
	public sbyte WindowBorderBottom;
	public APTR Font;
	public ViewPort ViewPort;
	public RastPort RastPort;
	public BitMap BitMap;
	public LayerInfo LayerInfo;
	public APTR FirstGadget;
	public byte DetailPen;
	public byte BlockPen;
	public ushort SaveColor0;
	public APTR BarLayer;
	public APTR ExtData;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct DrawInfo
{
	public const uint Size = 50;

	public ushort Version;
	public ushort NumberOfPens;
	public APTR Pens;
	public APTR Font;
	public ushort Depth;
	public ushort ResolutionX;
	public ushort ResolutionY;
	public DrawInfoFlags Flags;
	public APTR CheckMark;
	public APTR AmigaKey;
	public fixed uint Reserved[5];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ColorSpec
{
	public const uint Size = 8;

	public short ColorIndex;
	public ushort Red;
	public ushort Green;
	public ushort Blue;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct PubScreenNode
{
	public const uint Size = 30;

	public Node Node;
	public APTR Screen;
	public PublicScreenFlags Flags;
	public short SizeInBytes;
	public short VisitorCount;
	public APTR SignalTask;
	public byte SignalBit;
	private byte _padding;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ScreenBuffer
{
	public const uint Size = 8;

	public APTR BitMap;
	public APTR DoubleBufferInfo;
}
