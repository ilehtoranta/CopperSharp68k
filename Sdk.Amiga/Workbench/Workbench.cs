/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Workbench
{
	public const string Name = "workbench.library";

	public static APTR WorkbenchLibraryBase
	{
		get => throw new System.NotSupportedException(
			"WorkbenchLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"WorkbenchLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(WorkbenchLvo.UpdateWorkbench)]
	public static void UpdateWorkbench(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint parentLock,
		[M68kRegister(M68kRegister.D0)] int action)
	{
	}

	[AmigaLvo(WorkbenchLvo.AddAppWindowA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AddAppWindowA(
		[M68kRegister(M68kRegister.D0)] uint id,
		[M68kRegister(M68kRegister.D1)] uint userData,
		[M68kRegister(M68kRegister.A0)] uint window,
		[M68kRegister(M68kRegister.A1)] uint messagePort,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(WorkbenchLvo.RemoveAppWindow)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveAppWindow(
		[M68kRegister(M68kRegister.A0)] uint appWindow)
	{
		return 0;
	}

	[AmigaLvo(WorkbenchLvo.AddAppIconA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AddAppIconA(
		[M68kRegister(M68kRegister.D0)] uint id,
		[M68kRegister(M68kRegister.D1)] uint userData,
		[M68kRegister(M68kRegister.A0)] CString text,
		[M68kRegister(M68kRegister.A1)] uint messagePort,
		[M68kRegister(M68kRegister.A2)] uint lockPtr,
		[M68kRegister(M68kRegister.A3)] uint diskObject,
		[M68kRegister(M68kRegister.A4)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(WorkbenchLvo.RemoveAppIcon)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveAppIcon(
		[M68kRegister(M68kRegister.A0)] uint appIcon)
	{
		return 0;
	}

	[AmigaLvo(WorkbenchLvo.AddAppMenuItemA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AddAppMenuItemA(
		[M68kRegister(M68kRegister.D0)] uint id,
		[M68kRegister(M68kRegister.D1)] uint userData,
		[M68kRegister(M68kRegister.A0)] CString text,
		[M68kRegister(M68kRegister.A1)] uint messagePort,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(WorkbenchLvo.RemoveAppMenuItem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveAppMenuItem(
		[M68kRegister(M68kRegister.A0)] uint appMenuItem)
	{
		return 0;
	}

	[AmigaLvo(WorkbenchLvo.WBInfo)]
	public static void WBInfo(
		[M68kRegister(M68kRegister.A0)] uint lockPtr,
		[M68kRegister(M68kRegister.A1)] CString name,
		[M68kRegister(M68kRegister.A2)] uint screen)
	{
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.OpenWorkbenchObjectA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int OpenWorkbenchObjectA(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.CloseWorkbenchObjectA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int CloseWorkbenchObjectA(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.WorkbenchControlA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int WorkbenchControlA(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.AddAppWindowDropZoneA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AddAppWindowDropZoneA(
		[M68kRegister(M68kRegister.A0)] uint appWindow,
		[M68kRegister(M68kRegister.D0)] uint id,
		[M68kRegister(M68kRegister.D1)] uint userData,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.RemoveAppWindowDropZone)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveAppWindowDropZone(
		[M68kRegister(M68kRegister.A0)] uint appWindow,
		[M68kRegister(M68kRegister.A1)] uint dropZone)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.ChangeWorkbenchSelectionA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int ChangeWorkbenchSelectionA(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint hook,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(WorkbenchLvo.MakeWorkbenchObjectVisibleA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int MakeWorkbenchObjectVisibleA(
		[M68kRegister(M68kRegister.A0)] CString name,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	// MorphOS also exposes AppWindowObtain(), AppWindowRelease(),
	// ManageDesktopObjectA(), CreateDrawerA(), and CreateIconA() as direct
	// function-pointer calls without ppcinline register metadata.
	// QuoteWorkbench() and StartWorkbench() are present in older LVO tables
	// but are not documented in current public Workbench autodocs or the
	// MorphOS ppcinline ABI page.

	public static uint AddAppWindow(uint id, uint userData, uint window, uint messagePort, uint tags) =>
		AddAppWindowA(id, userData, window, messagePort, tags);

	public static uint AddAppIcon(uint id, uint userData, CString text, uint messagePort, uint lockPtr, uint diskObject, uint tags) =>
		AddAppIconA(id, userData, text, messagePort, lockPtr, diskObject, tags);

	public static uint AddAppMenuItem(uint id, uint userData, CString text, uint messagePort, uint tags) =>
		AddAppMenuItemA(id, userData, text, messagePort, tags);

	public static int OpenWorkbenchObject(CString name, uint tags) =>
		OpenWorkbenchObjectA(name, tags);

	public static int CloseWorkbenchObject(CString name, uint tags) =>
		CloseWorkbenchObjectA(name, tags);

	public static int WorkbenchControl(CString name, uint tags) =>
		WorkbenchControlA(name, tags);

	public static uint AddAppWindowDropZone(uint appWindow, uint id, uint userData, uint tags) =>
		AddAppWindowDropZoneA(appWindow, id, userData, tags);

	public static int ChangeWorkbenchSelection(CString name, uint hook, uint tags) =>
		ChangeWorkbenchSelectionA(name, hook, tags);

	public static int MakeWorkbenchObjectVisible(CString name, uint tags) =>
		MakeWorkbenchObjectVisibleA(name, tags);
}
