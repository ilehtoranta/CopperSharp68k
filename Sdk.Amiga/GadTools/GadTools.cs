/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class GadTools
{
	public const string Name = "gadtools.library";

	public static APTR GadToolsLibraryBase
	{
		get => throw new System.NotSupportedException(
			"GadToolsLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"GadToolsLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateGadgetA(
		[M68kRegister(M68kRegister.D0)] uint kind,
		[M68kRegister(M68kRegister.A0)] uint previous,
		[M68kRegister(M68kRegister.A1)] uint newGadget,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-36)]
	public static extern void FreeGadgets(
		[M68kRegister(M68kRegister.A0)] uint gadget);

	[AmigaLvo(-42)]
	public static extern void GT_SetGadgetAttrsA(
		[M68kRegister(M68kRegister.A0)] uint gadget,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint tags);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateMenusA(
		[M68kRegister(M68kRegister.A0)] uint newMenu,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-54)]
	public static extern void FreeMenus(
		[M68kRegister(M68kRegister.A0)] uint menu);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LayoutMenuItemsA(
		[M68kRegister(M68kRegister.A0)] uint menuItem,
		[M68kRegister(M68kRegister.A1)] uint visualInfo,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int LayoutMenusA(
		[M68kRegister(M68kRegister.A0)] uint firstMenu,
		[M68kRegister(M68kRegister.A1)] uint visualInfo,
		[M68kRegister(M68kRegister.A2)] uint tags);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GT_GetIMsg(
		[M68kRegister(M68kRegister.A0)] uint userPort);

	[AmigaLvo(-78)]
	public static extern void GT_ReplyIMsg(
		[M68kRegister(M68kRegister.A1)] uint intuiMessage);

	[AmigaLvo(-84)]
	public static extern void GT_RefreshWindow(
		[M68kRegister(M68kRegister.A0)] uint window,
		[M68kRegister(M68kRegister.A1)] uint requester);

	[AmigaLvo(-90)]
	public static extern void GT_BeginRefresh(
		[M68kRegister(M68kRegister.A0)] uint window);

	[AmigaLvo(-96)]
	public static extern void GT_EndRefresh(
		[M68kRegister(M68kRegister.A0)] uint window,
		[M68kRegister(M68kRegister.D0)] int complete);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GT_FilterIMsg(
		[M68kRegister(M68kRegister.A1)] uint intuiMessage);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GT_PostFilterIMsg(
		[M68kRegister(M68kRegister.A1)] uint intuiMessage);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateContext(
		[M68kRegister(M68kRegister.A0)] uint gadgetPointer);

	[AmigaLvo(-120)]
	public static extern void DrawBevelBoxA(
		[M68kRegister(M68kRegister.A0)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] int left,
		[M68kRegister(M68kRegister.D1)] int top,
		[M68kRegister(M68kRegister.D2)] int width,
		[M68kRegister(M68kRegister.D3)] int height,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetVisualInfoA(
		[M68kRegister(M68kRegister.A0)] uint screen,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-132)]
	public static extern void FreeVisualInfo(
		[M68kRegister(M68kRegister.A0)] uint visualInfo);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GT_GetGadgetAttrsA(
		[M68kRegister(M68kRegister.A0)] uint gadget,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3), M68kWritesBuffer] uint tags);

	public static uint CreateGadget(uint kind, uint previous, uint newGadget, uint tags) =>
		CreateGadgetA(kind, previous, newGadget, tags);

	public static void GT_SetGadgetAttrs(uint gadget, uint window, uint requester, uint tags) =>
		GT_SetGadgetAttrsA(gadget, window, requester, tags);

	public static uint CreateMenus(uint newMenu, uint tags) =>
		CreateMenusA(newMenu, tags);

	public static int LayoutMenuItems(uint menuItem, uint visualInfo, uint tags) =>
		LayoutMenuItemsA(menuItem, visualInfo, tags);

	public static int LayoutMenus(uint firstMenu, uint visualInfo, uint tags) =>
		LayoutMenusA(firstMenu, visualInfo, tags);

	public static void DrawBevelBox(uint rastPort, int left, int top, int width, int height, uint tags) =>
		DrawBevelBoxA(rastPort, left, top, width, height, tags);

	public static uint GetVisualInfo(uint screen, uint tags) =>
		GetVisualInfoA(screen, tags);

	public static int GT_GetGadgetAttrs(uint gadget, uint window, uint requester, uint tags) =>
		GT_GetGadgetAttrsA(gadget, window, requester, tags);
}
