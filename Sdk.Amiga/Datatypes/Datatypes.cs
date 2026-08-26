/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Datatypes
{
	public const string Name = "datatypes.library";

	public static APTR DatatypesLibraryBase
	{
		get => throw new System.NotSupportedException(
			"DatatypesLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"DatatypesLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR ObtainDataTypeA(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] APTR handle,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return APTR.Null;
	}

	[AmigaLvo(-42)]
	public static void ReleaseDataType(
		[M68kRegister(M68kRegister.A0)] APTR dataType)
	{
	}

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR NewDTObjectA(
		[M68kRegister(M68kRegister.D0)] APTR name,
		[M68kRegister(M68kRegister.A0)] APTR tags)
	{
		return APTR.Null;
	}

	[AmigaLvo(-54)]
	public static void DisposeDTObject(
		[M68kRegister(M68kRegister.A0)] APTR obj)
	{
	}

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint SetDTAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR requester,
		[M68kRegister(M68kRegister.A3)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetDTAttrsA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A2)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int AddDTObject(
		[M68kRegister(M68kRegister.A0)] APTR window,
		[M68kRegister(M68kRegister.A1)] APTR requester,
		[M68kRegister(M68kRegister.A2)] APTR obj,
		[M68kRegister(M68kRegister.D0)] int position)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	public static void RefreshDTObjectA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR requester,
		[M68kRegister(M68kRegister.A3)] APTR tags)
	{
	}

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint DoAsyncLayout(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR gpLayout)
	{
		return 0;
	}

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint DoDTMethodA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR requester,
		[M68kRegister(M68kRegister.A3)] APTR message)
	{
		return 0;
	}

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveDTObject(
		[M68kRegister(M68kRegister.A0)] APTR window,
		[M68kRegister(M68kRegister.A1)] APTR obj)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR GetDTMethods(
		[M68kRegister(M68kRegister.A0)] APTR obj)
	{
		return APTR.Null;
	}

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR GetDTTriggerMethods(
		[M68kRegister(M68kRegister.A0)] APTR obj)
	{
		return APTR.Null;
	}

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint PrintDTObjectA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR requester,
		[M68kRegister(M68kRegister.A3)] APTR print)
	{
		return 0;
	}

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR ObtainDTDrawInfoA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return APTR.Null;
	}

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int DrawDTObjectA(
		[M68kRegister(M68kRegister.A0)] APTR rastPort,
		[M68kRegister(M68kRegister.A1)] APTR obj,
		[M68kRegister(M68kRegister.D0)] int x,
		[M68kRegister(M68kRegister.D1)] int y,
		[M68kRegister(M68kRegister.D2)] int width,
		[M68kRegister(M68kRegister.D3)] int height,
		[M68kRegister(M68kRegister.D4)] int top,
		[M68kRegister(M68kRegister.D5)] int left,
		[M68kRegister(M68kRegister.A2)] APTR tags)
	{
		return 0;
	}

	[AmigaLvo(-132)]
	public static void ReleaseDTDrawInfo(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR drawInfo)
	{
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static STRPTR GetDTString(
		[M68kRegister(M68kRegister.D0)] uint id)
	{
		return STRPTR.Null;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-240)]
	public static void LockDataType(
		[M68kRegister(M68kRegister.A0)] APTR dataType)
	{
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-246)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR FindToolNodeA(
		[M68kRegister(M68kRegister.A0)] APTR list,
		[M68kRegister(M68kRegister.A1)] APTR tags)
	{
		return APTR.Null;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint LaunchToolA(
		[M68kRegister(M68kRegister.A0)] APTR tool,
		[M68kRegister(M68kRegister.A1)] STRPTR project,
		[M68kRegister(M68kRegister.A2)] APTR tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR FindMethod(
		[M68kRegister(M68kRegister.A0)] APTR methods,
		[M68kRegister(M68kRegister.A1)] uint methodId)
	{
		return APTR.Null;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR FindTriggerMethod(
		[M68kRegister(M68kRegister.A0)] APTR methods,
		[M68kRegister(M68kRegister.A1)] CString command,
		[M68kRegister(M68kRegister.D0)] uint method)
	{
		return APTR.Null;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-270)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR CopyDTMethods(
		[M68kRegister(M68kRegister.A0)] APTR source,
		[M68kRegister(M68kRegister.A1)] APTR destination,
		[M68kRegister(M68kRegister.A2)] APTR include)
	{
		return APTR.Null;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-276)]
	[return: M68kRegister(M68kRegister.D0)]
	public static APTR CopyDTTriggerMethods(
		[M68kRegister(M68kRegister.A0)] APTR source,
		[M68kRegister(M68kRegister.A1)] APTR destination,
		[M68kRegister(M68kRegister.A2)] APTR include)
	{
		return APTR.Null;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-282)]
	public static void FreeDTMethods(
		[M68kRegister(M68kRegister.A0)] APTR methods)
	{
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-288)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetDTTriggerMethodDataFlags(
		[M68kRegister(M68kRegister.D0)] uint method)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-294)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint SaveDTObjectA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR requester,
		[M68kRegister(M68kRegister.A3)] STRPTR file,
		[M68kRegister(M68kRegister.D0)] uint mode,
		[M68kRegister(M68kRegister.D1)] int saveIcon,
		[M68kRegister(M68kRegister.A4)] APTR tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-300)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint StartDragSelect(
		[M68kRegister(M68kRegister.A0)] APTR obj)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-306)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint DoDTDomainA(
		[M68kRegister(M68kRegister.A0)] APTR obj,
		[M68kRegister(M68kRegister.A1)] APTR window,
		[M68kRegister(M68kRegister.A2)] APTR requester,
		[M68kRegister(M68kRegister.A3)] APTR rastPort,
		[M68kRegister(M68kRegister.D0)] uint which,
		[M68kRegister(M68kRegister.A4)] APTR domain,
		[M68kRegister(M68kRegister.A5)] APTR tags)
	{
		return 0;
	}

	public static APTR ObtainDataType(uint type, APTR handle, APTR tags) =>
		ObtainDataTypeA(type, handle, tags);

	public static APTR NewDTObject(APTR name, APTR tags) =>
		NewDTObjectA(name, tags);

	public static uint SetDTAttrs(APTR obj, APTR window, APTR requester, APTR tags) =>
		SetDTAttrsA(obj, window, requester, tags);

	public static uint GetDTAttrs(APTR obj, APTR tags) =>
		GetDTAttrsA(obj, tags);

	public static void RefreshDTObject(APTR obj, APTR window, APTR requester, APTR tags) =>
		RefreshDTObjectA(obj, window, requester, tags);

	public static void RefreshDTObjects(APTR obj, APTR window, APTR requester, APTR tags) =>
		RefreshDTObjectA(obj, window, requester, tags);

	public static uint DoDTMethod(APTR obj, APTR window, APTR requester, APTR message) =>
		DoDTMethodA(obj, window, requester, message);

	public static uint PrintDTObject(APTR obj, APTR window, APTR requester, APTR print) =>
		PrintDTObjectA(obj, window, requester, print);

	public static APTR ObtainDTDrawInfo(APTR obj, APTR tags) =>
		ObtainDTDrawInfoA(obj, tags);

	public static int DrawDTObject(APTR rastPort, APTR obj, int x, int y, int width, int height, int top, int left, APTR tags) =>
		DrawDTObjectA(rastPort, obj, x, y, width, height, top, left, tags);

	public static APTR FindToolNode(APTR list, APTR tags) =>
		FindToolNodeA(list, tags);

	public static uint LaunchTool(APTR tool, STRPTR project, APTR tags) =>
		LaunchToolA(tool, project, tags);

	public static uint SaveDTObject(APTR obj, APTR window, APTR requester, STRPTR file, uint mode, int saveIcon, APTR tags) =>
		SaveDTObjectA(obj, window, requester, file, mode, saveIcon, tags);

	public static uint DoDTDomain(APTR obj, APTR window, APTR requester, APTR rastPort, uint which, APTR domain, APTR tags) =>
		DoDTDomainA(obj, window, requester, rastPort, which, domain, tags);
}
