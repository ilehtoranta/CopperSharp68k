/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Point
{
	public const uint Size = 4;

	public short X;
	public short Y;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Rectangle
{
	public const uint Size = 8;

	public short MinX;
	public short MinY;
	public short MaxX;
	public short MaxY;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Rect32
{
	public const uint Size = 16;

	public int MinX;
	public int MinY;
	public int MaxX;
	public int MaxY;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AreaInfo
{
	public const uint Size = 24;

	public APTR VectorTable;
	public APTR VectorPointer;
	public APTR FlagTable;
	public APTR FlagPointer;
	public short Count;
	public short MaxCount;
	public short FirstX;
	public short FirstY;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TmpRas
{
	public const uint Size = 8;

	public APTR Raster;
	public int SizeInBytes;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct GelsInfo
{
	public const uint Size = 38;

	public byte SpriteReserved;
	public byte Flags;
	public APTR GelHead;
	public APTR GelTail;
	public APTR NextLine;
	public APTR LastColor;
	public APTR CollisionHandler;
	public short Leftmost;
	public short Rightmost;
	public short Topmost;
	public short Bottommost;
	public APTR FirstBlissObject;
	public APTR LastBlissObject;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RegionRectangle
{
	public const uint Size = 16;

	public APTR Successor;
	public APTR Predecessor;
	public Rectangle Bounds;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Region
{
	public const uint Size = 12;

	public Rectangle Bounds;
	public APTR RegionRectangle;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct RasInfo
{
	public const uint Size = 12;

	public APTR Next;
	public APTR BitMap;
	public short XOffset;
	public short YOffset;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct View
{
	public const uint Size = 18;

	public APTR ViewPort;
	public APTR LongFrameCopperList;
	public APTR ShortFrameCopperList;
	public short YOffset;
	public short XOffset;
	public ushort Modes;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtendedNode
{
	public const uint Size = 24;

	public APTR Successor;
	public APTR Predecessor;
	public byte Type;
	public sbyte Priority;
	public STRPTR Name;
	public byte Subsystem;
	public byte Subtype;
	public int Library;
	public APTR Init;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ViewExtra
{
	public const uint Size = 34;

	public ExtendedNode Node;
	public APTR View;
	public APTR Monitor;
	public ushort TopLine;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ViewPortExtra
{
	public const uint Size = 66;

	public ExtendedNode Node;
	public APTR ViewPort;
	public Rectangle DisplayClip;
	public APTR VectorTable;
	public unsafe fixed uint DriverData[2];
	public ushort Flags;
	public Point Origin0;
	public Point Origin1;
	public uint Copper1Pointer;
	public uint Copper2Pointer;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ColorMap
{
	public const uint Size = 52;

	public byte Flags;
	public byte Type;
	public ushort Count;
	public APTR ColorTable;
	public APTR ViewPortExtra;
	public APTR LowColorBits;
	public byte TransparencyPlane;
	public byte SpriteResolution;
	public byte SpriteResolutionDefault;
	public byte AuxiliaryFlags;
	public APTR ViewPort;
	public APTR NormalDisplayInfo;
	public APTR CoerceDisplayInfo;
	public APTR BatchItems;
	public uint ViewPortModeId;
	public APTR PaletteExtra;
	public ushort SpriteBaseEven;
	public ushort SpriteBaseOdd;
	public ushort BitPlane0Base;
	public ushort BitPlane1Base;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CopIns
{
	public const uint Size = 6;

	public short OpCode;
	/// <summary>
	/// The classic union containing either the next copper-list pointer or
	/// the two wait/move words. Interpret this slot according to OpCode.
	/// </summary>
	public APTR Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CprList
{
	public const uint Size = 10;

	public APTR Next;
	public APTR Start;
	public short MaxCount;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CopList
{
	public const uint Size = 38;

	public APTR Next;
	public APTR SystemCopy;
	public APTR ViewPort;
	public APTR Instructions;
	public APTR InstructionPointer;
	public APTR LongFrameStart;
	public APTR ShortFrameStart;
	public short Count;
	public short MaxCount;
	public short YOffset;
	public ushort ShortLongRepeat;
	public ushort Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct UCopList
{
	public const uint Size = 12;

	public APTR Next;
	public APTR FirstCopList;
	public APTR CopList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SimpleSprite
{
	public const uint Size = 12;

	public APTR PositionControlData;
	public ushort Height;
	public ushort X;
	public ushort Y;
	public ushort Number;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtSprite
{
	public const uint Size = 16;

	public SimpleSprite SimpleSprite;
	public ushort WordWidth;
	public ushort Flags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct VSprite
{
	public const uint Size = 60;

	public APTR NextVSprite;
	public APTR PreviousVSprite;
	public APTR DrawPath;
	public APTR ClearPath;
	public short OldY;
	public short OldX;
	public ushort Flags;
	public short Y;
	public short X;
	public short Height;
	public short Width;
	public short Depth;
	public short MeMask;
	public short HitMask;
	public APTR ImageData;
	public APTR BorderLine;
	public APTR CollisionMask;
	public APTR SpriteColors;
	public APTR Bob;
	public byte PlanePick;
	public byte PlaneOnOff;
	public short UserExtension;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Bob
{
	public const uint Size = 32;

	public ushort Flags;
	public APTR SaveBuffer;
	public APTR ImageShadow;
	public APTR Before;
	public APTR After;
	public APTR BobVSprite;
	public APTR BobComponent;
	public APTR DoubleBuffer;
	public short UserExtension;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AnimComp
{
	public const uint Size = 38;

	public ushort Flags;
	public short Timer;
	public short TimeSet;
	public APTR NextComponent;
	public APTR PreviousComponent;
	public APTR NextSequence;
	public APTR PreviousSequence;
	public APTR AnimationComponentRoutine;
	public short YTranslation;
	public short XTranslation;
	public APTR HeadObject;
	public APTR AnimationBob;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AnimOb
{
	public const uint Size = 42;

	public APTR NextObject;
	public APTR PreviousObject;
	public int Clock;
	public short OldY;
	public short OldX;
	public short Y;
	public short X;
	public short YVelocity;
	public short XVelocity;
	public short YAcceleration;
	public short XAcceleration;
	public short RingYTranslation;
	public short RingXTranslation;
	public APTR AnimationObjectRoutine;
	public APTR HeadComponent;
	public short UserExtension;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DBufPacket
{
	public const uint Size = 12;

	public short BufferY;
	public short BufferX;
	public APTR BufferPath;
	public APTR Buffer;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct QueryHeader
{
	public const uint Size = 16;

	public uint StructureId;
	public uint DisplayId;
	public uint SkipId;
	public uint Length;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DisplayInfo
{
	public const uint Size = 56;

	public QueryHeader Header;
	public ushort NotAvailable;
	public uint PropertyFlags;
	public Point Resolution;
	public ushort PixelSpeed;
	public ushort NumberOfStandardSprites;
	public ushort PaletteRange;
	public Point SpriteResolution;
	public unsafe fixed byte Padding[4];
	public byte RedBits;
	public byte GreenBits;
	public byte BlueBits;
	public unsafe fixed byte Padding2[5];
	public unsafe fixed uint Reserved[2];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct DimensionInfo
{
	public const uint Size = 88;

	public QueryHeader Header;
	public ushort MaxDepth;
	public ushort MinRasterWidth;
	public ushort MinRasterHeight;
	public ushort MaxRasterWidth;
	public ushort MaxRasterHeight;
	public Rectangle Nominal;
	public Rectangle MaxOverscan;
	public Rectangle VideoOverscan;
	public Rectangle TextOverscan;
	public Rectangle StandardOverscan;
	public unsafe fixed byte Padding[14];
	public unsafe fixed uint Reserved[2];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MonitorInfo
{
	public const uint Size = 96;

	public QueryHeader Header;
	public APTR MonitorSpec;
	public Point ViewPosition;
	public Point ViewResolution;
	public Rectangle ViewPositionRange;
	public ushort TotalRows;
	public ushort TotalColorClocks;
	public ushort MinimumRow;
	public short Compatibility;
	public unsafe fixed byte Padding[32];
	public Point MouseTicks;
	public Point DefaultViewPosition;
	public uint PreferredModeId;
	public unsafe fixed uint Reserved[2];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct NameInfo
{
	public const uint Size = 56;

	public QueryHeader Header;
	public fixed byte Name[32];
	public fixed uint Reserved[2];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct AnalogSignalInterval
{
	public const uint Size = 4;

	public ushort Start;
	public ushort Stop;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct SpecialMonitor
{
	public const uint Size = 58;

	public ExtendedNode Node;
	public ushort Flags;
	public APTR DoMonitor;
	public APTR Reserved1;
	public APTR Reserved2;
	public APTR Reserved3;
	public AnalogSignalInterval HorizontalBlank;
	public AnalogSignalInterval VerticalBlank;
	public AnalogSignalInterval HorizontalSync;
	public AnalogSignalInterval VerticalSync;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct MonitorSpec
{
	public const uint Size = 160;

	public ExtendedNode Node;
	public ushort Flags;
	public int HorizontalRatio;
	public int VerticalRatio;
	public ushort TotalRows;
	public ushort TotalColorClocks;
	public ushort DeniseMaximumDisplayColumn;
	public ushort BeamCon0;
	public ushort MinimumRow;
	public APTR SpecialMonitor;
	public ushort OpenCount;
	public APTR Transform;
	public APTR Translate;
	public APTR Scale;
	public ushort XOffset;
	public ushort YOffset;
	public Rectangle LegalView;
	public APTR MaximumOverscan;
	public APTR VideoOverscan;
	public ushort DeniseMinimumDisplayColumn;
	public uint DisplayCompatible;
	public List DisplayInfoDataBase;
	public SignalSemaphore DisplayInfoDataBaseSemaphore;
	public APTR MergeCopper;
	public APTR LoadView;
	public APTR KillView;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TTextAttr
{
	public const uint Size = 12;

	public STRPTR Name;
	public ushort YSize;
	public byte Style;
	public byte Flags;
	public APTR Tags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TextFont
{
	public const uint Size = 52;

	public Message Message;
	public ushort YSize;
	public byte Style;
	public byte Flags;
	public ushort XSize;
	public ushort Baseline;
	public ushort BoldSmear;
	public ushort Accessors;
	public byte LoChar;
	public byte HiChar;
	public APTR CharData;
	public ushort Modulo;
	public APTR CharLoc;
	public APTR CharSpace;
	public APTR CharKern;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TextFontExtension
{
	public const uint Size = 24;

	public ushort MatchWord;
	public byte Flags0;
	public byte Flags1;
	public APTR BackPointer;
	public APTR OriginalReplyPort;
	public APTR Tags;
	public APTR OriginalFontPatchS;
	public APTR OriginalFontPatchK;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ColorFontColors
{
	public const uint Size = 8;

	public ushort Reserved;
	public ushort Count;
	public APTR ColorTable;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct ColorTextFont
{
	public const uint Size = 96;

	public TextFont TextFont;
	public ushort Flags;
	public byte Depth;
	public byte ForegroundColor;
	public byte LowColor;
	public byte HighColor;
	public byte PlanePick;
	public byte PlaneOnOff;
	public APTR ColorFontColors;
	public fixed uint CharacterData[8];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TextExtent
{
	public const uint Size = 12;

	public ushort Width;
	public ushort Height;
	public Rectangle Extent;
}
