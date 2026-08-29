/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Classic V40 gadgetclass attributes, methods, and result values.</summary>
public static class IntuitionGadgetClass
{
	public const uint GA_Dummy = ExecConstants.TagUser + 0x0003_0000u;
	public const uint GA_Left = GA_Dummy + 0x0001u;
	public const uint GA_RelRight = GA_Dummy + 0x0002u;
	public const uint GA_Top = GA_Dummy + 0x0003u;
	public const uint GA_RelBottom = GA_Dummy + 0x0004u;
	public const uint GA_Width = GA_Dummy + 0x0005u;
	public const uint GA_RelWidth = GA_Dummy + 0x0006u;
	public const uint GA_Height = GA_Dummy + 0x0007u;
	public const uint GA_RelHeight = GA_Dummy + 0x0008u;
	public const uint GA_Text = GA_Dummy + 0x0009u;
	public const uint GA_Image = GA_Dummy + 0x000Au;
	public const uint GA_Border = GA_Dummy + 0x000Bu;
	public const uint GA_SelectRender = GA_Dummy + 0x000Cu;
	public const uint GA_Highlight = GA_Dummy + 0x000Du;
	public const uint GA_Disabled = GA_Dummy + 0x000Eu;
	public const uint GA_GZZGadget = GA_Dummy + 0x000Fu;
	public const uint GA_ID = GA_Dummy + 0x0010u;
	public const uint GA_UserData = GA_Dummy + 0x0011u;
	public const uint GA_SpecialInfo = GA_Dummy + 0x0012u;
	public const uint GA_Selected = GA_Dummy + 0x0013u;
	public const uint GA_EndGadget = GA_Dummy + 0x0014u;
	public const uint GA_Immediate = GA_Dummy + 0x0015u;
	public const uint GA_RelVerify = GA_Dummy + 0x0016u;
	public const uint GA_FollowMouse = GA_Dummy + 0x0017u;
	public const uint GA_RightBorder = GA_Dummy + 0x0018u;
	public const uint GA_LeftBorder = GA_Dummy + 0x0019u;
	public const uint GA_TopBorder = GA_Dummy + 0x001Au;
	public const uint GA_BottomBorder = GA_Dummy + 0x001Bu;
	public const uint GA_ToggleSelect = GA_Dummy + 0x001Cu;
	public const uint GA_SysGadget = GA_Dummy + 0x001Du;
	public const uint GA_SysGType = GA_Dummy + 0x001Eu;
	public const uint GA_Previous = GA_Dummy + 0x001Fu;
	public const uint GA_Next = GA_Dummy + 0x0020u;
	public const uint GA_DrawInfo = GA_Dummy + 0x0021u;
	public const uint GA_IntuiText = GA_Dummy + 0x0022u;
	public const uint GA_LabelImage = GA_Dummy + 0x0023u;
	public const uint GA_TabCycle = GA_Dummy + 0x0024u;
	public const uint GA_GadgetHelp = GA_Dummy + 0x0025u;
	public const uint GA_Bounds = GA_Dummy + 0x0026u;
	public const uint GA_RelSpecial = GA_Dummy + 0x0027u;

	public const uint PGA_Dummy = ExecConstants.TagUser + 0x0003_1000u;
	public const uint PGA_Freedom = PGA_Dummy + 0x0001u;
	public const uint PGA_Borderless = PGA_Dummy + 0x0002u;
	public const uint PGA_HorizPot = PGA_Dummy + 0x0003u;
	public const uint PGA_HorizBody = PGA_Dummy + 0x0004u;
	public const uint PGA_VertPot = PGA_Dummy + 0x0005u;
	public const uint PGA_VertBody = PGA_Dummy + 0x0006u;
	public const uint PGA_Total = PGA_Dummy + 0x0007u;
	public const uint PGA_Visible = PGA_Dummy + 0x0008u;
	public const uint PGA_Top = PGA_Dummy + 0x0009u;
	public const uint PGA_NewLook = PGA_Dummy + 0x000Au;

	public const uint STRINGA_Dummy = ExecConstants.TagUser + 0x0003_2000u;
	public const uint STRINGA_MaxChars = STRINGA_Dummy + 0x0001u;
	public const uint STRINGA_Buffer = STRINGA_Dummy + 0x0002u;
	public const uint STRINGA_UndoBuffer = STRINGA_Dummy + 0x0003u;
	public const uint STRINGA_WorkBuffer = STRINGA_Dummy + 0x0004u;
	public const uint STRINGA_BufferPos = STRINGA_Dummy + 0x0005u;
	public const uint STRINGA_DispPos = STRINGA_Dummy + 0x0006u;
	public const uint STRINGA_AltKeyMap = STRINGA_Dummy + 0x0007u;
	public const uint STRINGA_Font = STRINGA_Dummy + 0x0008u;
	public const uint STRINGA_Pens = STRINGA_Dummy + 0x0009u;
	public const uint STRINGA_ActivePens = STRINGA_Dummy + 0x000Au;
	public const uint STRINGA_EditHook = STRINGA_Dummy + 0x000Bu;
	public const uint STRINGA_EditModes = STRINGA_Dummy + 0x000Cu;
	public const uint STRINGA_ReplaceMode = STRINGA_Dummy + 0x000Du;
	public const uint STRINGA_FixedFieldMode = STRINGA_Dummy + 0x000Eu;
	public const uint STRINGA_NoFilterMode = STRINGA_Dummy + 0x000Fu;
	public const uint STRINGA_Justification = STRINGA_Dummy + 0x0010u;
	public const uint STRINGA_LongVal = STRINGA_Dummy + 0x0011u;
	public const uint STRINGA_TextVal = STRINGA_Dummy + 0x0012u;
	public const uint STRINGA_ExitHelp = STRINGA_Dummy + 0x0013u;
	public const uint SG_DEFAULTMAXCHARS = 128u;

	public const uint LAYOUTA_Dummy = ExecConstants.TagUser + 0x0003_8000u;
	public const uint LAYOUTA_LayoutObj = LAYOUTA_Dummy + 0x0001u;
	public const uint LAYOUTA_Spacing = LAYOUTA_Dummy + 0x0002u;
	public const uint LAYOUTA_Orientation = LAYOUTA_Dummy + 0x0003u;
	public const uint LORIENT_NONE = 0u;
	public const uint LORIENT_HORIZ = 1u;
	public const uint LORIENT_VERT = 2u;

	public const uint GM_Dummy = 0xFFFF_FFFFu;
	public const uint GM_HITTEST = 0u;
	public const uint GM_RENDER = 1u;
	public const uint GM_GOACTIVE = 2u;
	public const uint GM_HANDLEINPUT = 3u;
	public const uint GM_GOINACTIVE = 4u;
	public const uint GM_HELPTEST = 5u;
	public const uint GM_LAYOUT = 6u;

	public const uint GMR_GADGETHIT = 0x0000_0004u;
	public const uint GMR_NOHELPHIT = 0u;
	public const uint GMR_HELPHIT = 0xFFFF_FFFFu;
	public const uint GMR_HELPCODE = 0x0001_0000u;
	public const uint GMR_MEACTIVE = 0u;
	public const uint GMR_NOREUSE = 1u << 1;
	public const uint GMR_REUSE = 1u << 2;
	public const uint GMR_VERIFY = 1u << 3;
	public const uint GMR_NEXTACTIVE = 1u << 4;
	public const uint GMR_PREVACTIVE = 1u << 5;

	public const int GREDRAW_UPDATE = 2;
	public const int GREDRAW_REDRAW = 1;
	public const int GREDRAW_TOGGLE = 0;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct GadgetInfoPens
{
	public const uint Size = 2;

	public byte DetailPen;
	public byte BlockPen;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct GadgetInfo
{
	public const uint Size = 58;

	public APTR gi_Screen;
	public APTR gi_Window;
	public APTR gi_Requester;
	public APTR gi_RastPort;
	public APTR gi_Layer;
	public IBox gi_Domain;
	public GadgetInfoPens gi_Pens;
	public APTR gi_DrInfo;
	public fixed uint gi_Reserved[6];
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct gpHitTest
{
	public const uint Size = 12;

	public uint MethodID;
	public APTR gpht_GInfo;
	public Point gpht_Mouse;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct gpRender
{
	public const uint Size = 16;

	public uint MethodID;
	public APTR gpr_GInfo;
	public APTR gpr_RPort;
	public int gpr_Redraw;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct gpInput
{
	public const uint Size = 24;

	public uint MethodID;
	public APTR gpi_GInfo;
	public APTR gpi_IEvent;
	public APTR gpi_Termination;
	public Point gpi_Mouse;
	public APTR gpi_TabletData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct gpGoInactive
{
	public const uint Size = 12;

	public uint MethodID;
	public APTR gpgi_GInfo;
	public uint gpgi_Abort;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct gpLayout
{
	public const uint Size = 12;

	public uint MethodID;
	public APTR gpl_GInfo;
	public uint gpl_Initial;
}
