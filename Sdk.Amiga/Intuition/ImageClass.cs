/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Classic V40 imageclass attributes, methods, states, and identities.</summary>
public static class IntuitionImageClass
{
	public const short CUSTOMIMAGEDEPTH = -1;

	public const uint IA_Dummy = ExecConstants.TagUser + 0x0002_0000u;
	public const uint IA_Left = IA_Dummy + 0x01u;
	public const uint IA_Top = IA_Dummy + 0x02u;
	public const uint IA_Width = IA_Dummy + 0x03u;
	public const uint IA_Height = IA_Dummy + 0x04u;
	public const uint IA_FGPen = IA_Dummy + 0x05u;
	public const uint IA_BGPen = IA_Dummy + 0x06u;
	public const uint IA_Data = IA_Dummy + 0x07u;
	public const uint IA_LineWidth = IA_Dummy + 0x08u;
	public const uint IA_ShadowPen = IA_Dummy + 0x09u;
	public const uint IA_HighlightPen = IA_Dummy + 0x0Au;
	public const uint SYSIA_Size = IA_Dummy + 0x0Bu;
	public const uint SYSIA_Depth = IA_Dummy + 0x0Cu;
	public const uint SYSIA_Which = IA_Dummy + 0x0Du;
	public const uint IA_Pens = IA_Dummy + 0x0Eu;
	public const uint SYSIA_Pens = IA_Pens;
	public const uint IA_Resolution = IA_Dummy + 0x0Fu;
	public const uint IA_APattern = IA_Dummy + 0x10u;
	public const uint IA_APatSize = IA_Dummy + 0x11u;
	public const uint IA_Mode = IA_Dummy + 0x12u;
	public const uint IA_Font = IA_Dummy + 0x13u;
	public const uint IA_Outline = IA_Dummy + 0x14u;
	public const uint IA_Recessed = IA_Dummy + 0x15u;
	public const uint IA_DoubleEmboss = IA_Dummy + 0x16u;
	public const uint IA_EdgesOnly = IA_Dummy + 0x17u;
	public const uint SYSIA_DrawInfo = IA_Dummy + 0x18u;
	public const uint SYSIA_ReferenceFont = IA_Dummy + 0x19u;
	public const uint IA_SupportsDisable = IA_Dummy + 0x1Au;
	public const uint IA_FrameType = IA_Dummy + 0x1Bu;

	public const uint SYSISIZE_MEDRES = 0u;
	public const uint SYSISIZE_LOWRES = 1u;
	public const uint SYSISIZE_HIRES = 2u;

	public const uint DEPTHIMAGE = 0x00u;
	public const uint ZOOMIMAGE = 0x01u;
	public const uint SIZEIMAGE = 0x02u;
	public const uint CLOSEIMAGE = 0x03u;
	public const uint SDEPTHIMAGE = 0x05u;
	public const uint LEFTIMAGE = 0x0Au;
	public const uint UPIMAGE = 0x0Bu;
	public const uint RIGHTIMAGE = 0x0Cu;
	public const uint DOWNIMAGE = 0x0Du;
	public const uint CHECKIMAGE = 0x0Eu;
	public const uint MXIMAGE = 0x0Fu;
	public const uint MENUCHECK = 0x10u;
	public const uint AMIGAKEY = 0x11u;

	public const uint FRAME_DEFAULT = 0u;
	public const uint FRAME_BUTTON = 1u;
	public const uint FRAME_RIDGE = 2u;
	public const uint FRAME_ICONDROPBOX = 3u;

	public const uint IM_DRAW = 0x202u;
	public const uint IM_HITTEST = 0x203u;
	public const uint IM_ERASE = 0x204u;
	public const uint IM_MOVE = 0x205u;
	public const uint IM_DRAWFRAME = 0x206u;
	public const uint IM_FRAMEBOX = 0x207u;
	public const uint IM_HITFRAME = 0x208u;
	public const uint IM_ERASEFRAME = 0x209u;

	public const uint IDS_NORMAL = 0u;
	public const uint IDS_SELECTED = 1u;
	public const uint IDS_DISABLED = 2u;
	public const uint IDS_BUSY = 3u;
	public const uint IDS_INDETERMINATE = 4u;
	public const uint IDS_INDETERMINANT = IDS_INDETERMINATE;
	public const uint IDS_INACTIVENORMAL = 5u;
	public const uint IDS_INACTIVESELECTED = 6u;
	public const uint IDS_INACTIVEDISABLED = 7u;
	public const uint IDS_SELECTEDDISABLED = 8u;
	public const uint FRAMEF_SPECIFY = 1u;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ImageDimensions
{
	public const uint Size = 4;
	public short Width;
	public short Height;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct impFrameBox
{
	public const uint Size = 20;
	public uint MethodID;
	public APTR imp_ContentsBox;
	public APTR imp_FrameBox;
	public APTR imp_DrInfo;
	public uint imp_FrameFlags;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct impDraw
{
	public const uint Size = 24;
	public uint MethodID;
	public APTR imp_RPort;
	public Point imp_Offset;
	public uint imp_State;
	public APTR imp_DrInfo;
	public ImageDimensions imp_Dimensions;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct impErase
{
	public const uint Size = 16;
	public uint MethodID;
	public APTR imp_RPort;
	public Point imp_Offset;
	public ImageDimensions imp_Dimensions;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct impHitTest
{
	public const uint Size = 12;
	public uint MethodID;
	public Point imp_Point;
	public ImageDimensions imp_Dimensions;
}
