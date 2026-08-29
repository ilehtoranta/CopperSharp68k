/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

/// <summary>Classic V40 screen tags, error results, and related sentinels.</summary>
public static class IntuitionScreenTags
{
	public const uint SA_Dummy = ExecConstants.TagUser + 32u;
	public const uint SA_Left = SA_Dummy + 0x01u;
	public const uint SA_Top = SA_Dummy + 0x02u;
	public const uint SA_Width = SA_Dummy + 0x03u;
	public const uint SA_Height = SA_Dummy + 0x04u;
	public const uint SA_Depth = SA_Dummy + 0x05u;
	public const uint SA_DetailPen = SA_Dummy + 0x06u;
	public const uint SA_BlockPen = SA_Dummy + 0x07u;
	public const uint SA_Title = SA_Dummy + 0x08u;
	public const uint SA_Colors = SA_Dummy + 0x09u;
	public const uint SA_ErrorCode = SA_Dummy + 0x0Au;
	public const uint SA_Font = SA_Dummy + 0x0Bu;
	public const uint SA_SysFont = SA_Dummy + 0x0Cu;
	public const uint SA_Type = SA_Dummy + 0x0Du;
	public const uint SA_BitMap = SA_Dummy + 0x0Eu;
	public const uint SA_PubName = SA_Dummy + 0x0Fu;
	public const uint SA_PubSig = SA_Dummy + 0x10u;
	public const uint SA_PubTask = SA_Dummy + 0x11u;
	public const uint SA_DisplayID = SA_Dummy + 0x12u;
	public const uint SA_DClip = SA_Dummy + 0x13u;
	public const uint SA_Overscan = SA_Dummy + 0x14u;
	public const uint SA_Obsolete1 = SA_Dummy + 0x15u;
	public const uint SA_ShowTitle = SA_Dummy + 0x16u;
	public const uint SA_Behind = SA_Dummy + 0x17u;
	public const uint SA_Quiet = SA_Dummy + 0x18u;
	public const uint SA_AutoScroll = SA_Dummy + 0x19u;
	public const uint SA_Pens = SA_Dummy + 0x1Au;
	public const uint SA_FullPalette = SA_Dummy + 0x1Bu;
	public const uint SA_ColorMapEntries = SA_Dummy + 0x1Cu;
	public const uint SA_Parent = SA_Dummy + 0x1Du;
	public const uint SA_Draggable = SA_Dummy + 0x1Eu;
	public const uint SA_Exclusive = SA_Dummy + 0x1Fu;
	public const uint SA_SharePens = SA_Dummy + 0x20u;
	public const uint SA_BackFill = SA_Dummy + 0x21u;
	public const uint SA_Interleaved = SA_Dummy + 0x22u;
	public const uint SA_Colors32 = SA_Dummy + 0x23u;
	public const uint SA_VideoControl = SA_Dummy + 0x24u;
	public const uint SA_FrontChild = SA_Dummy + 0x25u;
	public const uint SA_BackChild = SA_Dummy + 0x26u;
	public const uint SA_LikeWorkbench = SA_Dummy + 0x27u;
	public const uint SA_Reserved = SA_Dummy + 0x28u;
	public const uint SA_MinimizeISG = SA_Dummy + 0x29u;

	public const uint NSTAG_EXT_VPMODE = ExecConstants.TagUser | 1u;
	public const int STDSCREENHEIGHT = -1;
	public const int STDSCREENWIDTH = -1;

	public const int OSERR_NOMONITOR = 1;
	public const int OSERR_NOCHIPS = 2;
	public const int OSERR_NOMEM = 3;
	public const int OSERR_NOCHIPMEM = 4;
	public const int OSERR_PUBNOTUNIQUE = 5;
	public const int OSERR_UNKNOWNMODE = 6;
	public const int OSERR_TOODEEP = 7;
	public const int OSERR_ATTACHFAIL = 8;
	public const int OSERR_NOTAVAILABLE = 9;
}
