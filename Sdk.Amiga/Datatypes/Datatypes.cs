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
	public static uint ObtainDataTypeA(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] uint handle,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-42)]
	public static void ReleaseDataType(
		[M68kRegister(M68kRegister.A0)] uint dataType)
	{
	}

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint NewDTObjectA(
		[M68kRegister(M68kRegister.D0)] CString name,
		[M68kRegister(M68kRegister.A0)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-54)]
	public static void DisposeDTObject(
		[M68kRegister(M68kRegister.A0)] uint obj)
	{
	}

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint SetDTAttrsA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetDTAttrsA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int AddDTObject(
		[M68kRegister(M68kRegister.A0)] uint window,
		[M68kRegister(M68kRegister.A1)] uint requester,
		[M68kRegister(M68kRegister.A2)] uint obj,
		[M68kRegister(M68kRegister.D0)] int position)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	public static void RefreshDTObjectA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint tags)
	{
	}

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint DoAsyncLayout(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint gpLayout)
	{
		return 0;
	}

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint DoDTMethodA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint message)
	{
		return 0;
	}

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int RemoveDTObject(
		[M68kRegister(M68kRegister.A0)] uint window,
		[M68kRegister(M68kRegister.A1)] uint obj)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetDTMethods(
		[M68kRegister(M68kRegister.A0)] uint obj)
	{
		return 0;
	}

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetDTTriggerMethods(
		[M68kRegister(M68kRegister.A0)] uint obj)
	{
		return 0;
	}

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint PrintDTObjectA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint print)
	{
		return 0;
	}

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint ObtainDTDrawInfoA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int DrawDTObjectA(
		[M68kRegister(M68kRegister.A0)] uint rastPort,
		[M68kRegister(M68kRegister.A1)] uint obj,
		[M68kRegister(M68kRegister.D0)] int x,
		[M68kRegister(M68kRegister.D1)] int y,
		[M68kRegister(M68kRegister.D2)] int width,
		[M68kRegister(M68kRegister.D3)] int height,
		[M68kRegister(M68kRegister.D4)] int top,
		[M68kRegister(M68kRegister.D5)] int left,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	[AmigaLvo(-132)]
	public static void ReleaseDTDrawInfo(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint drawInfo)
	{
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetDTString(
		[M68kRegister(M68kRegister.D0)] uint id)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-240)]
	public static void LockDataType(
		[M68kRegister(M68kRegister.A0)] uint dataType)
	{
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-246)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindToolNodeA(
		[M68kRegister(M68kRegister.A0)] uint list,
		[M68kRegister(M68kRegister.A1)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint LaunchToolA(
		[M68kRegister(M68kRegister.A0)] uint tool,
		[M68kRegister(M68kRegister.A1)] uint project,
		[M68kRegister(M68kRegister.A2)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindMethod(
		[M68kRegister(M68kRegister.A0)] uint methods,
		[M68kRegister(M68kRegister.A1)] uint methodId)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindTriggerMethod(
		[M68kRegister(M68kRegister.A0)] uint methods,
		[M68kRegister(M68kRegister.A1)] CString command,
		[M68kRegister(M68kRegister.D0)] uint method)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-270)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint CopyDTMethods(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.A1)] uint destination,
		[M68kRegister(M68kRegister.A2)] uint include)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-276)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint CopyDTTriggerMethods(
		[M68kRegister(M68kRegister.A0)] uint source,
		[M68kRegister(M68kRegister.A1)] uint destination,
		[M68kRegister(M68kRegister.A2)] uint include)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-282)]
	public static void FreeDTMethods(
		[M68kRegister(M68kRegister.A0)] uint methods)
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
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint file,
		[M68kRegister(M68kRegister.D0)] uint mode,
		[M68kRegister(M68kRegister.D1)] int saveIcon,
		[M68kRegister(M68kRegister.A4)] uint tags)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-300)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint StartDragSelect(
		[M68kRegister(M68kRegister.A0)] uint obj)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-306)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint DoDTDomainA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint window,
		[M68kRegister(M68kRegister.A2)] uint requester,
		[M68kRegister(M68kRegister.A3)] uint rastPort,
		[M68kRegister(M68kRegister.D0)] uint which,
		[M68kRegister(M68kRegister.A4)] uint domain,
		[M68kRegister(M68kRegister.A5)] uint tags)
	{
		return 0;
	}

	public static uint ObtainDataType(uint type, uint handle, uint tags) =>
		ObtainDataTypeA(type, handle, tags);

	public static uint NewDTObject(CString name, uint tags) =>
		NewDTObjectA(name, tags);

	public static uint SetDTAttrs(uint obj, uint window, uint requester, uint tags) =>
		SetDTAttrsA(obj, window, requester, tags);

	public static uint GetDTAttrs(uint obj, uint tags) =>
		GetDTAttrsA(obj, tags);

	public static void RefreshDTObject(uint obj, uint window, uint requester, uint tags) =>
		RefreshDTObjectA(obj, window, requester, tags);

	public static void RefreshDTObjects(uint obj, uint window, uint requester, uint tags) =>
		RefreshDTObjectA(obj, window, requester, tags);

	public static uint DoDTMethod(uint obj, uint window, uint requester, uint message) =>
		DoDTMethodA(obj, window, requester, message);

	public static uint PrintDTObject(uint obj, uint window, uint requester, uint print) =>
		PrintDTObjectA(obj, window, requester, print);

	public static uint ObtainDTDrawInfo(uint obj, uint tags) =>
		ObtainDTDrawInfoA(obj, tags);

	public static int DrawDTObject(uint rastPort, uint obj, int x, int y, int width, int height, int top, int left, uint tags) =>
		DrawDTObjectA(rastPort, obj, x, y, width, height, top, left, tags);

	public static uint FindToolNode(uint list, uint tags) =>
		FindToolNodeA(list, tags);

	public static uint LaunchTool(uint tool, uint project, uint tags) =>
		LaunchToolA(tool, project, tags);

	public static uint SaveDTObject(uint obj, uint window, uint requester, uint file, uint mode, int saveIcon, uint tags) =>
		SaveDTObjectA(obj, window, requester, file, mode, saveIcon, tags);

	public static uint DoDTDomain(uint obj, uint window, uint requester, uint rastPort, uint which, uint domain, uint tags) =>
		DoDTDomainA(obj, window, requester, rastPort, which, domain, tags);
}
