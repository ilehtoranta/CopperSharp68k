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

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateCxObj(
		[M68kRegister(M68kRegister.D0)] uint type,
		[M68kRegister(M68kRegister.A0)] int arg1,
		[M68kRegister(M68kRegister.A1)] int arg2);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CxBroker(
		[M68kRegister(M68kRegister.A0)] uint newBroker,
		[M68kRegister(M68kRegister.D0)] uint error);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ActivateCxObj(
		[M68kRegister(M68kRegister.A0)] uint cxObj,
		[M68kRegister(M68kRegister.D0)] int doIt);

	[AmigaLvo(-48)]
	public static extern void DeleteCxObj(
		[M68kRegister(M68kRegister.A0)] uint cxObj);

	[AmigaLvo(-54)]
	public static extern void DeleteCxObjAll(
		[M68kRegister(M68kRegister.A0)] uint cxObj);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CxObjType(
		[M68kRegister(M68kRegister.A0)] uint cxObj);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CxObjError(
		[M68kRegister(M68kRegister.A0)] uint cxObj);

	[AmigaLvo(-72)]
	public static extern void ClearCxObjError(
		[M68kRegister(M68kRegister.A0)] uint cxObj);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetCxObjPri(
		[M68kRegister(M68kRegister.A0)] uint cxObj,
		[M68kRegister(M68kRegister.D0)] int priority);

	[AmigaLvo(-84)]
	public static extern void AttachCxObj(
		[M68kRegister(M68kRegister.A0)] uint headObj,
		[M68kRegister(M68kRegister.A1)] uint cxObj);

	[AmigaLvo(-90)]
	public static extern void EnqueueCxObj(
		[M68kRegister(M68kRegister.A0)] uint headObj,
		[M68kRegister(M68kRegister.A1)] uint cxObj);

	[AmigaLvo(-96)]
	public static extern void InsertCxObj(
		[M68kRegister(M68kRegister.A0)] uint headObj,
		[M68kRegister(M68kRegister.A1)] uint cxObj,
		[M68kRegister(M68kRegister.A2)] uint predecessor);

	[AmigaLvo(-102)]
	public static extern void RemoveCxObj(
		[M68kRegister(M68kRegister.A0)] uint cxObj);

	[AmigaLvo(-114)]
	public static extern void SetTranslate(
		[M68kRegister(M68kRegister.A0)] uint translator,
		[M68kRegister(M68kRegister.A1)] uint events);

	[AmigaLvo(-120)]
	public static extern void SetFilter(
		[M68kRegister(M68kRegister.A0)] uint filter,
		[M68kRegister(M68kRegister.A1)] CString text);

	[AmigaLvo(-126)]
	public static extern void SetFilterIX(
		[M68kRegister(M68kRegister.A0)] uint filter,
		[M68kRegister(M68kRegister.A1)] uint ix);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ParseIX(
		[M68kRegister(M68kRegister.A0)] uint description,
		[M68kRegister(M68kRegister.A1)] uint ix);

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CxMsgType(
		[M68kRegister(M68kRegister.A0)] uint cxMsg);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CxMsgData(
		[M68kRegister(M68kRegister.A0)] uint cxMsg);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CxMsgID(
		[M68kRegister(M68kRegister.A0)] uint cxMsg);

	[AmigaLvo(-156)]
	public static extern void DivertCxMsg(
		[M68kRegister(M68kRegister.A0)] uint cxMsg,
		[M68kRegister(M68kRegister.A1)] uint headObj,
		[M68kRegister(M68kRegister.A2)] uint returnObj);

	[AmigaLvo(-162)]
	public static extern void RouteCxMsg(
		[M68kRegister(M68kRegister.A0)] uint cxMsg,
		[M68kRegister(M68kRegister.A1)] uint cxObj);

	[AmigaLvo(-168)]
	public static extern void DisposeCxMsg(
		[M68kRegister(M68kRegister.A0)] uint cxMsg);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int InvertKeyMap(
		[M68kRegister(M68kRegister.D0)] uint ansiCode,
		[M68kRegister(M68kRegister.A0)] uint inputEvent,
		[M68kRegister(M68kRegister.A1)] uint keyMap);

	[AmigaLvo(-180)]
	public static extern void AddIEvents(
		[M68kRegister(M68kRegister.A0)] uint events);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MatchIX(
		[M68kRegister(M68kRegister.A0)] uint inputEvent,
		[M68kRegister(M68kRegister.A1)] uint ix);
}
