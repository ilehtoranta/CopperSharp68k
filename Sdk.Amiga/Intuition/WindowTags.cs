/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

/// <summary>Classic V40 window and SetWindowPointer tag identifiers.</summary>
public static class IntuitionWindowTags
{
	public const uint WA_Dummy = ExecConstants.TagUser + 99u;
	public const uint WA_Left = WA_Dummy + 0x01u;
	public const uint WA_Top = WA_Dummy + 0x02u;
	public const uint WA_Width = WA_Dummy + 0x03u;
	public const uint WA_Height = WA_Dummy + 0x04u;
	public const uint WA_DetailPen = WA_Dummy + 0x05u;
	public const uint WA_BlockPen = WA_Dummy + 0x06u;
	public const uint WA_IDCMP = WA_Dummy + 0x07u;
	public const uint WA_Flags = WA_Dummy + 0x08u;
	public const uint WA_Gadgets = WA_Dummy + 0x09u;
	public const uint WA_Checkmark = WA_Dummy + 0x0Au;
	public const uint WA_Title = WA_Dummy + 0x0Bu;
	public const uint WA_ScreenTitle = WA_Dummy + 0x0Cu;
	public const uint WA_CustomScreen = WA_Dummy + 0x0Du;
	public const uint WA_SuperBitMap = WA_Dummy + 0x0Eu;
	public const uint WA_MinWidth = WA_Dummy + 0x0Fu;
	public const uint WA_MinHeight = WA_Dummy + 0x10u;
	public const uint WA_MaxWidth = WA_Dummy + 0x11u;
	public const uint WA_MaxHeight = WA_Dummy + 0x12u;
	public const uint WA_InnerWidth = WA_Dummy + 0x13u;
	public const uint WA_InnerHeight = WA_Dummy + 0x14u;
	public const uint WA_PubScreenName = WA_Dummy + 0x15u;
	public const uint WA_PubScreen = WA_Dummy + 0x16u;
	public const uint WA_PubScreenFallBack = WA_Dummy + 0x17u;
	public const uint WA_WindowName = WA_Dummy + 0x18u;
	public const uint WA_Colors = WA_Dummy + 0x19u;
	public const uint WA_Zoom = WA_Dummy + 0x1Au;
	public const uint WA_MouseQueue = WA_Dummy + 0x1Bu;
	public const uint WA_BackFill = WA_Dummy + 0x1Cu;
	public const uint WA_RptQueue = WA_Dummy + 0x1Du;
	public const uint WA_SizeGadget = WA_Dummy + 0x1Eu;
	public const uint WA_DragBar = WA_Dummy + 0x1Fu;
	public const uint WA_DepthGadget = WA_Dummy + 0x20u;
	public const uint WA_CloseGadget = WA_Dummy + 0x21u;
	public const uint WA_Backdrop = WA_Dummy + 0x22u;
	public const uint WA_ReportMouse = WA_Dummy + 0x23u;
	public const uint WA_NoCareRefresh = WA_Dummy + 0x24u;
	public const uint WA_Borderless = WA_Dummy + 0x25u;
	public const uint WA_Activate = WA_Dummy + 0x26u;
	public const uint WA_RMBTrap = WA_Dummy + 0x27u;
	public const uint WA_WBenchWindow = WA_Dummy + 0x28u;
	public const uint WA_SimpleRefresh = WA_Dummy + 0x29u;
	public const uint WA_SmartRefresh = WA_Dummy + 0x2Au;
	public const uint WA_SizeBRight = WA_Dummy + 0x2Bu;
	public const uint WA_SizeBBottom = WA_Dummy + 0x2Cu;
	public const uint WA_AutoAdjust = WA_Dummy + 0x2Du;
	public const uint WA_GimmeZeroZero = WA_Dummy + 0x2Eu;
	public const uint WA_MenuHelp = WA_Dummy + 0x2Fu;
	public const uint WA_NewLookMenus = WA_Dummy + 0x30u;
	public const uint WA_AmigaKey = WA_Dummy + 0x31u;
	public const uint WA_NotifyDepth = WA_Dummy + 0x32u;
	public const uint WA_Pointer = WA_Dummy + 0x34u;
	public const uint WA_BusyPointer = WA_Dummy + 0x35u;
	public const uint WA_PointerDelay = WA_Dummy + 0x36u;
	public const uint WA_TabletMessages = WA_Dummy + 0x37u;
	public const uint WA_HelpGroup = WA_Dummy + 0x38u;
	public const uint WA_HelpGroupWindow = WA_Dummy + 0x39u;

	public const uint HC_GADGETHELP = 1u;
}
