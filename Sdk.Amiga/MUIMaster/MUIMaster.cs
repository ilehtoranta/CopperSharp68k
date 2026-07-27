/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MUIMaster
{
	public const string Name = "muimaster.library";

	public static APTR MUIMasterLibraryBase
	{
		get => throw new System.NotSupportedException(
			"MUIMasterLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"MUIMasterLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_NewObjectA(
		[M68kRegister(M68kRegister.A0)] CString className,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-36)]
	public static extern void MUI_DisposeObject(
		[M68kRegister(M68kRegister.A0)] uint obj);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_RequestA(
		[M68kRegister(M68kRegister.D0)] uint app,
		[M68kRegister(M68kRegister.D1)] uint win,
		[M68kRegister(M68kRegister.D2)] uint flags,
		[M68kRegister(M68kRegister.A0)] CString title,
		[M68kRegister(M68kRegister.A1)] CString gadgets,
		[M68kRegister(M68kRegister.A2)] CString format,
		[M68kRegister(M68kRegister.A3)] uint parameters);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_AllocAslRequest(
		[M68kRegister(M68kRegister.D0)] uint reqType,
		[M68kRegister(M68kRegister.A0)] uint tagList);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_AslRequest(
		[M68kRegister(M68kRegister.A0)] uint requester,
		[M68kRegister(M68kRegister.A1)] uint tagList);

	[AmigaLvo(-60)]
	public static extern void MUI_FreeAslRequest(
		[M68kRegister(M68kRegister.A0)] uint requester);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_Error();

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_SetError(
		[M68kRegister(M68kRegister.D0)] int num);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_GetClass(
		[M68kRegister(M68kRegister.A0)] CString className);

	[AmigaLvo(-84)]
	public static extern void MUI_FreeClass(
		[M68kRegister(M68kRegister.A0)] uint classPtr);

	[AmigaLvo(-90)]
	public static extern void MUI_RequestIDCMP(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-96)]
	public static extern void MUI_RejectIDCMP(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-102)]
	public static extern void MUI_Redraw(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_CreateCustomClass(
		[M68kRegister(M68kRegister.A0)] uint base_,
		[M68kRegister(M68kRegister.A1)] CString superName,
		[M68kRegister(M68kRegister.A2)] uint superMcc,
		[M68kRegister(M68kRegister.D0)] int dataSize,
		[M68kRegister(M68kRegister.A3)] APTR dispatcher);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_DeleteCustomClass(
		[M68kRegister(M68kRegister.A0)] uint mcc);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_MakeObjectA(
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.A0)] uint parameters);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_Layout(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.D0)] int left,
		[M68kRegister(M68kRegister.D1)] int top,
		[M68kRegister(M68kRegister.D2)] int width,
		[M68kRegister(M68kRegister.D3)] int height,
		[M68kRegister(M68kRegister.D4)] uint flags);

	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_ObtainPen(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.A1)] uint spec,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-162)]
	public static extern void MUI_ReleasePen(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.D0)] int pen);

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_AddClipping(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.D0)] short left,
		[M68kRegister(M68kRegister.D1)] short top,
		[M68kRegister(M68kRegister.D2)] short width,
		[M68kRegister(M68kRegister.D3)] short height);

	[AmigaLvo(-174)]
	public static extern void MUI_RemoveClipping(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.A1)] uint handle);

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MUI_AddClipRegion(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.A1)] uint region);

	[AmigaLvo(-186)]
	public static extern void MUI_RemoveClipRegion(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.A1)] uint handle);

	[AmigaLvo(-192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_BeginRefresh(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-198)]
	public static extern void MUI_EndRefresh(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.D0)] uint flags);

	[AmigaLvo(-690)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_GetRGBColor(
		[M68kRegister(M68kRegister.A0)] uint mri,
		[M68kRegister(M68kRegister.A1)] uint spec,
		[M68kRegister(M68kRegister.A2)] uint color);

	[AmigaLvo(-756)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MUI_RequestObjectA(
		[M68kRegister(M68kRegister.D0)] uint app,
		[M68kRegister(M68kRegister.D1)] uint win,
		[M68kRegister(M68kRegister.D2)] uint flags,
		[M68kRegister(M68kRegister.A0)] CString title,
		[M68kRegister(M68kRegister.A1)] CString gadgets,
		[M68kRegister(M68kRegister.A2)] uint obj,
		[M68kRegister(M68kRegister.A3)] CString format,
		[M68kRegister(M68kRegister.A4)] uint parameters);

	public static uint MUI_NewObjectTags(CString className, uint tags) =>
		MUI_NewObjectA(className, tags);

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint MUI_NewObject(
		[M68kRegister(M68kRegister.A0)] CString className,
		[M68kRegister(M68kRegister.A1)]
		[AmigaStackVarargs]
		params uint[] tags) =>
		throw new System.NotSupportedException(
			"MUI_NewObject stack varargs are lowered by CopperSharp.");

	public static uint MUI_MakeObjectParameters(int type, uint parameters) =>
		MUI_MakeObjectA(type, parameters);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint MUI_MakeObject(
		[M68kRegister(M68kRegister.D0)] int type,
		[M68kRegister(M68kRegister.A0)]
		[AmigaStackVarargs]
		params uint[] parameters) =>
		throw new System.NotSupportedException(
			"MUI_MakeObject stack varargs are lowered by CopperSharp.");

	public static int MUI_Request(
		uint app,
		uint win,
		uint flags,
		CString title,
		CString gadgets,
		CString format,
		uint parameters) =>
		MUI_RequestA(app, win, flags, title, gadgets, format, parameters);

	public static int MUI_RequestObject(
		uint app,
		uint win,
		uint flags,
		CString title,
		CString gadgets,
		uint obj,
		CString format,
		uint parameters) =>
		MUI_RequestObjectA(app, win, flags, title, gadgets, obj, format, parameters);

	public static uint MUI_AllocAslRequestTags(uint reqType, uint tags) =>
		MUI_AllocAslRequest(reqType, tags);

	public static int MUI_AslRequestTags(uint requester, uint tags) =>
		MUI_AslRequest(requester, tags);
}
