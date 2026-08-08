/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DataTypeHeader
{
	public const uint Size = 32;

	public STRPTR Name;
	public STRPTR BaseName;
	public STRPTR Pattern;
	public APTR Mask;
	public uint GroupId;
	public uint Id;
	public short MaskLength;
	public short Padding;
	public ushort Flags;
	public ushort Priority;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DataTypeHookContext
{
	public const uint Size = 40;

	public APTR SysBase;
	public APTR DosBase;
	public APTR IffParseBase;
	public APTR UtilityBase;
	public BPTR Lock;
	public APTR FileInfoBlock;
	public BPTR FileHandle;
	public APTR Iff;
	public STRPTR Buffer;
	public uint BufferLength;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DataTypeTool
{
	public const uint Size = 8;

	public ushort Which;
	public ushort Flags;
	public STRPTR Program;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DataType
{
	public const uint Size = 58;

	public Node Node1;
	public Node Node2;
	public APTR Header;
	public List ToolList;
	public STRPTR FunctionName;
	public APTR AttributeList;
	public uint Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DataTypeToolNode
{
	public const uint Size = 26;

	public Node Node;
	public DataTypeTool Tool;
	public uint Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DataTypeSpecialInfo
{
	public const uint Size = 90;

	public SignalSemaphore Lock;
	public uint Flags;
	public int TopVert;
	public int VisibleVert;
	public int TotalVert;
	public int OldTopVert;
	public int VertUnit;
	public int TopHoriz;
	public int VisibleHoriz;
	public int TotalHoriz;
	public int OldTopHoriz;
	public int HorizUnit;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTMethod
{
	public const uint Size = 12;

	public STRPTR Label;
	public STRPTR Command;
	public uint Method;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FrameDimensions
{
	public const uint Size = 12;

	public uint Width;
	public uint Height;
	public uint Depth;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct FrameInfo
{
	public const uint Size = 36;

	public uint PropertyFlags;
	public Point Resolution;
	public byte RedBits;
	public byte GreenBits;
	public byte BlueBits;
	public FrameDimensions Dimensions;
	public APTR Screen;
	public APTR ColorMap;
	public uint Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTGeneral
{
	public const uint Size = 8;

	public uint MethodId;
	public APTR GadgetInfo;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTSelect
{
	public const uint Size = 16;

	public uint MethodId;
	public APTR GadgetInfo;
	public Rectangle Select;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTFrameBox
{
	public const uint Size = 24;

	public uint MethodId;
	public APTR GadgetInfo;
	public APTR ContentsInfo;
	public APTR FrameInfo;
	public uint SizeOfFrameInfo;
	public uint FrameFlags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTGoto
{
	public const uint Size = 16;

	public uint MethodId;
	public APTR GadgetInfo;
	public STRPTR NodeName;
	public APTR AttributeList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTTrigger
{
	public const uint Size = 16;

	public uint MethodId;
	public APTR GadgetInfo;
	public uint Function;
	public APTR Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTPrint
{
	public const uint Size = 16;

	public uint MethodId;
	public APTR GadgetInfo;
	public APTR PrinterIo;
	public APTR AttributeList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTDraw
{
	public const uint Size = 36;

	public uint MethodId;
	public APTR RastPort;
	public int Left;
	public int Top;
	public int Width;
	public int Height;
	public int TopHoriz;
	public int TopVert;
	public APTR AttributeList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DTWrite
{
	public const uint Size = 20;

	public uint MethodId;
	public APTR GadgetInfo;
	public BPTR FileHandle;
	public uint Mode;
	public APTR AttributeList;
}
