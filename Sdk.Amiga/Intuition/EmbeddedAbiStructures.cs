/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Message
{
	public const uint Size = 20;

	public Node Node;
	public APTR ReplyPort;
	public ushort Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MinList
{
	public const uint Size = 12;

	public APTR Head;
	public APTR Tail;
	public APTR TailPred;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SemaphoreRequest
{
	public const uint Size = 12;

	public MinNode Link;
	public APTR Waiter;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SignalSemaphore
{
	public const uint Size = 46;

	public Node Link;
	public short NestCount;
	public MinList WaitQueue;
	public SemaphoreRequest MultipleLink;
	public APTR Owner;
	public short QueueCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LayerInfo
{
	public const uint Size = 102;

	public APTR TopLayer;
	public APTR CheckLayer;
	public APTR Obscured;
	public APTR FreeClipRects;
	public int PrivateReserve1;
	public int PrivateReserve2;
	public SignalSemaphore Lock;
	public MinList GraphicsSemaphoreHead;
	public short PrivateReserve3;
	public APTR PrivateReserve4;
	public LayerInfoFlags Flags;
	public sbyte FattenCount;
	public sbyte LockLayersCount;
	public short PrivateReserve5;
	public APTR BlankHook;
	public APTR Extra;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct BitMap
{
	public const uint Size = 40;

	public ushort BytesPerRow;
	public ushort Rows;
	public BitMapFlags Flags;
	public byte Depth;
	private ushort _padding;
	public APTR Plane0;
	public APTR Plane1;
	public APTR Plane2;
	public APTR Plane3;
	public APTR Plane4;
	public APTR Plane5;
	public APTR Plane6;
	public APTR Plane7;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ViewPort
{
	public const uint Size = 40;

	public APTR Next;
	public APTR ColorMap;
	public APTR DisplayInstructions;
	public APTR SpriteInstructions;
	public APTR ColorInstructions;
	public APTR UserCopperInstructions;
	public short DisplayWidth;
	public short DisplayHeight;
	public short DisplayXOffset;
	public short DisplayYOffset;
	public ScreenViewModes Modes;
	public byte SpritePriorities;
	public byte ExtendedModes;
	public APTR RasInfo;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct RastPort
{
	public const uint Size = 100;

	public APTR Layer;
	public APTR BitMap;
	public APTR AreaPattern;
	public APTR TemporaryRaster;
	public APTR AreaInfo;
	public APTR GelsInfo;
	public byte Mask;
	public sbyte ForegroundPen;
	public sbyte BackgroundPen;
	public sbyte AreaOutlinePen;
	public DrawMode DrawMode;
	public byte AreaPatternSize;
	public byte LinePatternCount;
	private byte _padding;
	public RastPortFlags Flags;
	public ushort LinePattern;
	public short CurrentX;
	public short CurrentY;
	public fixed byte Minterms[8];
	public short PenWidth;
	public short PenHeight;
	public APTR Font;
	public byte AlgorithmicStyle;
	public byte TextFlags;
	public ushort TextHeight;
	public ushort TextWidth;
	public ushort TextBaseline;
	public short TextSpacing;
	public APTR User;
	public fixed uint LongReserved[2];
	public fixed ushort WordReserved[7];
	public fixed byte Reserved[8];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TextAttr
{
	public const uint Size = 8;

	public STRPTR Name;
	public ushort YSize;
	public byte Style;
	public byte Flags;
}
