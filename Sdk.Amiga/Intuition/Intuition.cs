/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Intuition
{
	public const string Name = "intuition.library";

	public static APTR IntuitionLibraryBase
	{
		get => throw new System.NotSupportedException(
			"IntuitionLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"IntuitionLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	public static extern void OpenIntuition();

	[AmigaLvo(-36)]
	public static extern void Intuition_(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort AddGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ClearDMRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-54)]
	public static extern void ClearMenuStrip(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-60)]
	public static extern void ClearPointer(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-72)]
	public static extern void CloseWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseWorkBench();

	[AmigaLvo(-84)]
	public static extern void CurrentTime(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DisplayAlert(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(-96)]
	public static extern void DisplayBeep(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoubleClick(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1,
		[M68kRegister(M68kRegister.D2)] uint arg2,
		[M68kRegister(M68kRegister.D3)] uint arg3);

	[AmigaLvo(-108)]
	public static extern void DrawBorder(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-114)]
	public static extern void DrawImage(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-120)]
	public static extern void EndRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDefPrefs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetPrefs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-138)]
	public static extern void InitRequester(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ItemAddress(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ModifyIDCMP(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-156)]
	public static extern void ModifyProp(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] uint arg3,
		[M68kRegister(M68kRegister.D1)] uint arg4,
		[M68kRegister(M68kRegister.D2)] uint arg5,
		[M68kRegister(M68kRegister.D3)] uint arg6,
		[M68kRegister(M68kRegister.D4)] uint arg7);

	[AmigaLvo(-162)]
	public static extern void MoveScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-168)]
	public static extern void MoveWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-174)]
	public static extern void OffGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-180)]
	public static extern void OffMenu(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-186)]
	public static extern void OnGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-192)]
	public static extern void OnMenu(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-198)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-210)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenWorkBench();

	[AmigaLvo(-216)]
	public static extern void PrintIText(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-222)]
	public static extern void RefreshGadgets(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-228)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort RemoveGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-234)]
	public static extern void ReportMouse(
		[M68kRegister(M68kRegister.D0)] int arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Request(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-246)]
	public static extern void ScreenToBack(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-252)]
	public static extern void ScreenToFront(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetDMRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-264)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMenuStrip(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-270)]
	public static extern void SetPointer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5);

	[AmigaLvo(-276)]
	public static extern void SetWindowTitles(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-282)]
	public static extern void ShowTitle(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-288)]
	public static extern void SizeWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-294)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ViewAddress();

	[AmigaLvo(-300)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ViewPortAddress(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-306)]
	public static extern void WindowToBack(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-312)]
	public static extern void WindowToFront(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-318)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WindowLimits(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4);

	[AmigaLvo(-324)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetPrefs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-330)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IntuiTextLength(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-336)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WBenchToBack();

	[AmigaLvo(-342)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WBenchToFront();

	[AmigaLvo(-348)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AutoRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3,
		[M68kRegister(M68kRegister.D0)] uint arg4,
		[M68kRegister(M68kRegister.D1)] uint arg5,
		[M68kRegister(M68kRegister.D2)] uint arg6,
		[M68kRegister(M68kRegister.D3)] uint arg7);

	[AmigaLvo(-354)]
	public static extern void BeginRefresh(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-360)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BuildSysRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3,
		[M68kRegister(M68kRegister.D0)] uint arg4,
		[M68kRegister(M68kRegister.D1)] uint arg5,
		[M68kRegister(M68kRegister.D2)] uint arg6);

	[AmigaLvo(-366)]
	public static extern void EndRefresh(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-372)]
	public static extern void FreeSysRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-378)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MakeScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-384)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RemakeDisplay();

	[AmigaLvo(-390)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RethinkDisplay();

	[AmigaLvo(-396)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocRemember(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(-408)]
	public static extern void FreeRemember(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(-414)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockIBase(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(-420)]
	public static extern void UnlockIBase(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-426)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetScreenData(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.A1), M68kWritesBuffer] uint arg3);

	[AmigaLvo(-432)]
	public static extern void RefreshGList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] int arg3);

	[AmigaLvo(-438)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort AddGList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.A2)] uint arg4);

	[AmigaLvo(-444)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort RemoveGList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-450)]
	public static extern void ActivateWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-456)]
	public static extern void RefreshWindowFrame(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-462)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ActivateGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-468)]
	public static extern void NewModifyProp(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] uint arg3,
		[M68kRegister(M68kRegister.D1)] uint arg4,
		[M68kRegister(M68kRegister.D2)] uint arg5,
		[M68kRegister(M68kRegister.D3)] uint arg6,
		[M68kRegister(M68kRegister.D4)] uint arg7,
		[M68kRegister(M68kRegister.D5)] int arg8);

	[AmigaLvo(-474)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int QueryOverscan(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1), M68kWritesEntireBuffer] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-480)]
	public static extern void MoveWindowInFrontOf(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-486)]
	public static extern void ChangeWindowBox(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(-492)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetEditHook(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-498)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMouseQueue(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-504)]
	public static extern void ZipWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-510)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-516)]
	public static extern void UnlockPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-522)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockPubScreenList();

	[AmigaLvo(-528)]
	public static extern void UnlockPubScreenList();

	[AmigaLvo(-534)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-540)]
	public static extern void SetDefaultPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-546)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort SetPubScreenModes(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(-552)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort PubScreenStatus(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-558)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainGIRPort(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-564)]
	public static extern void ReleaseGIRPort(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-570)]
	public static extern void GadgetMouse(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-582)]
	public static extern void GetDefaultPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-588)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int EasyRequestArgs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(-594)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BuildEasyRequestArgs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(-600)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SysReqHandler(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(-606)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenWindowTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-612)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenScreenTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-618)]
	public static extern void DrawImageState(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] uint arg4,
		[M68kRegister(M68kRegister.A2)] uint arg5);

	[AmigaLvo(-624)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PointInImage(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(-630)]
	public static extern void EraseImage(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-636)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewObjectA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(-636)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint NewObject(
		[M68kRegister(M68kRegister.A0)] uint classPtr,
		[M68kRegister(M68kRegister.A1)] uint classId,
		[M68kRegister(M68kRegister.A2)]
		[AmigaStackVarargs]
		params AmigaVarArg[] tags) =>
		throw new System.NotSupportedException(
			"Intuition.NewObject stack varargs are lowered by CopperSharp.");

	[AmigaLvo(-642)]
	public static extern void DisposeObject(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-648)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetAttrsA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-654)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetAttr(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	[AmigaLvo(-660)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetGadgetAttrsA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(-666)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextObject(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-678)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MakeClass(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] uint arg3,
		[M68kRegister(M68kRegister.D1)] uint arg4);

	[AmigaLvo(-684)]
	public static extern void AddClass(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-690)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetScreenDrawInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-696)]
	public static extern void FreeScreenDrawInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-702)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ResetMenuStrip(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-708)]
	public static extern void RemoveClass(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-714)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FreeClass(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-768)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocScreenBuffer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2);

	[AmigaLvo(-774)]
	public static extern void FreeScreenBuffer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-780)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ChangeScreenBuffer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-786)]
	public static extern void ScreenDepth(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	[AmigaLvo(-792)]
	public static extern void ScreenPosition(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5);

	[AmigaLvo(-798)]
	public static extern void ScrollWindowRaster(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5,
		[M68kRegister(M68kRegister.D5)] int arg6);

	[AmigaLvo(-804)]
	public static extern void LendMenus(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-810)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DoGadgetMethodA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(-816)]
	public static extern void SetWindowPointerA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-822)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int TimedDisplayAlert(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3);

	[AmigaLvo(-828)]
	public static extern void HelpControl(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	// MorphOS m68k ABI call.
	[AmigaLvo(-918)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetSkinInfoAttrA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(-936)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDrawInfoAttr(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(-942)]
	public static extern void WindowAction(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(-948)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int TransparencyControl(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(-954)]
	public static extern void ScrollWindowRasterNoFill(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5,
		[M68kRegister(M68kRegister.D5)] int arg6);

	// MorphOS m68k ABI call.
	[AmigaLvo(-966)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMonitorList(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-972)]
	public static extern void FreeMonitorList(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-978)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ScreenbarControlA(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-996)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMonitorModesList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1002)]
	public static extern void FreeMonitorModesList(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-1008)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMonitorMode(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.A1)] uint arg4);
}
