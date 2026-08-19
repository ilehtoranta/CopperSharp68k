/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Commodities
{
	public const string Name = "commodities.library";

	public static APTR CommoditiesLibraryBase
	{
		get => throw new System.NotSupportedException(
			"CommoditiesLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"CommoditiesLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateCxObj(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] int arg1,
		[M68kRegister(M68kRegister.A1)] int arg2);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CxBroker(
		[M68kRegister(M68kRegister.A0)] APTR newBroker,
		[M68kRegister(M68kRegister.D0)] APTR error);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ActivateCxObj(
		[M68kRegister(M68kRegister.A0)] APTR cxObj,
		[M68kRegister(M68kRegister.D0)] int doIt);

	[AmigaLvo(-48)]
	public static extern void DeleteCxObj(
		[M68kRegister(M68kRegister.A0)] APTR cxObj);

	[AmigaLvo(-54)]
	public static extern void DeleteCxObjAll(
		[M68kRegister(M68kRegister.A0)] APTR cxObj);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CxObjType(
		[M68kRegister(M68kRegister.A0)] APTR cxObj);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CxObjError(
		[M68kRegister(M68kRegister.A0)] APTR cxObj);

	[AmigaLvo(-72)]
	public static extern void ClearCxObjError(
		[M68kRegister(M68kRegister.A0)] APTR cxObj);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetCxObjPri(
		[M68kRegister(M68kRegister.A0)] APTR cxObj,
		[M68kRegister(M68kRegister.D0)] int priority);

	[AmigaLvo(-84)]
	public static extern void AttachCxObj(
		[M68kRegister(M68kRegister.A0)] APTR headObj,
		[M68kRegister(M68kRegister.A1)] APTR cxObj);

	[AmigaLvo(-90)]
	public static extern void EnqueueCxObj(
		[M68kRegister(M68kRegister.A0)] APTR headObj,
		[M68kRegister(M68kRegister.A1)] APTR cxObj);

	[AmigaLvo(-96)]
	public static extern void InsertCxObj(
		[M68kRegister(M68kRegister.A0)] APTR headObj,
		[M68kRegister(M68kRegister.A1)] APTR cxObj,
		[M68kRegister(M68kRegister.A2)] APTR predecessor);

	[AmigaLvo(-102)]
	public static extern void RemoveCxObj(
		[M68kRegister(M68kRegister.A0)] APTR cxObj);

	[AmigaLvo(-114)]
	public static extern void SetTranslate(
		[M68kRegister(M68kRegister.A0)] APTR translator,
		[M68kRegister(M68kRegister.A1)] APTR events);

	[AmigaLvo(-120)]
	public static extern void SetFilter(
		[M68kRegister(M68kRegister.A0)] APTR filter,
		[M68kRegister(M68kRegister.A1)] CString text);

	[AmigaLvo(-126)]
	public static extern void SetFilterIX(
		[M68kRegister(M68kRegister.A0)] APTR filter,
		[M68kRegister(M68kRegister.A1)] APTR ix);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ParseIX(
		[M68kRegister(M68kRegister.A0)] CString description,
		[M68kRegister(M68kRegister.A1)] APTR ix);

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CxMsgType(
		[M68kRegister(M68kRegister.A0)] APTR cxMsg);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CxMsgData(
		[M68kRegister(M68kRegister.A0)] APTR cxMsg);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CxMsgID(
		[M68kRegister(M68kRegister.A0)] APTR cxMsg);

	[AmigaLvo(-156)]
	public static extern void DivertCxMsg(
		[M68kRegister(M68kRegister.A0)] APTR cxMsg,
		[M68kRegister(M68kRegister.A1)] APTR headObj,
		[M68kRegister(M68kRegister.A2)] APTR returnObj);

	[AmigaLvo(-162)]
	public static extern void RouteCxMsg(
		[M68kRegister(M68kRegister.A0)] APTR cxMsg,
		[M68kRegister(M68kRegister.A1)] APTR cxObj);

	[AmigaLvo(-168)]
	public static extern void DisposeCxMsg(
		[M68kRegister(M68kRegister.A0)] APTR cxMsg);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int InvertKeyMap(
		[M68kRegister(M68kRegister.D0)] uint ansiCode,
		[M68kRegister(M68kRegister.A0)] APTR inputEvent,
		[M68kRegister(M68kRegister.A1)] APTR keyMap);

	[AmigaLvo(-180)]
	public static extern void AddIEvents(
		[M68kRegister(M68kRegister.A0)] APTR events);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchIX(
		[M68kRegister(M68kRegister.A0)] APTR inputEvent,
		[M68kRegister(M68kRegister.A1)] APTR ix);
}
