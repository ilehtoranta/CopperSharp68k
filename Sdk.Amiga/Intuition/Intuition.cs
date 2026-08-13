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

	[AmigaLvo(IntuitionLvo.OpenIntuition)]
	public static extern void OpenIntuition();

	[AmigaLvo(IntuitionLvo.Intuition_)]
	public static extern void Intuition_(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.AddGadget)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort AddGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2);

	[AmigaLvo(IntuitionLvo.ClearDMRequest)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ClearDMRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.ClearMenuStrip)]
	public static extern void ClearMenuStrip(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.ClearPointer)]
	public static extern void ClearPointer(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.CloseScreen)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.CloseWindow)]
	public static extern void CloseWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.CloseWorkBench)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int CloseWorkBench();

	[AmigaLvo(IntuitionLvo.CurrentTime)]
	public static extern void CurrentTime(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.DisplayAlert)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DisplayAlert(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(IntuitionLvo.DisplayBeep)]
	public static extern void DisplayBeep(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.DoubleClick)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DoubleClick(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.D1)] uint arg1,
		[M68kRegister(M68kRegister.D2)] uint arg2,
		[M68kRegister(M68kRegister.D3)] uint arg3);

	[AmigaLvo(IntuitionLvo.DrawBorder)]
	public static extern void DrawBorder(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(IntuitionLvo.DrawImage)]
	public static extern void DrawImage(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(IntuitionLvo.EndRequest)]
	public static extern void EndRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.GetDefPrefs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDefPrefs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(IntuitionLvo.GetPrefs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetPrefs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(IntuitionLvo.InitRequester)]
	public static extern void InitRequester(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.ItemAddress)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ItemAddress(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(IntuitionLvo.ModifyIDCMP)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ModifyIDCMP(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(IntuitionLvo.ModifyProp)]
	public static extern void ModifyProp(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] uint arg3,
		[M68kRegister(M68kRegister.D1)] uint arg4,
		[M68kRegister(M68kRegister.D2)] uint arg5,
		[M68kRegister(M68kRegister.D3)] uint arg6,
		[M68kRegister(M68kRegister.D4)] uint arg7);

	[AmigaLvo(IntuitionLvo.MoveScreen)]
	public static extern void MoveScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(IntuitionLvo.MoveWindow)]
	public static extern void MoveWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(IntuitionLvo.OffGadget)]
	public static extern void OffGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.OffMenu)]
	public static extern void OffMenu(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(IntuitionLvo.OnGadget)]
	public static extern void OnGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.OnMenu)]
	public static extern void OnMenu(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(IntuitionLvo.OpenScreen)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.OpenWindow)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.OpenWorkBench)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenWorkBench();

	[AmigaLvo(IntuitionLvo.PrintIText)]
	public static extern void PrintIText(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(IntuitionLvo.RefreshGadgets)]
	public static extern void RefreshGadgets(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.RemoveGadget)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort RemoveGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.ReportMouse)]
	public static extern void ReportMouse(
		[M68kRegister(M68kRegister.D0)] int arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(IntuitionLvo.Request)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Request(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.ScreenToBack)]
	public static extern void ScreenToBack(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.ScreenToFront)]
	public static extern void ScreenToFront(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.SetDMRequest)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetDMRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.SetMenuStrip)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMenuStrip(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.SetPointer)]
	public static extern void SetPointer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5);

	[AmigaLvo(IntuitionLvo.SetWindowTitles)]
	public static extern void SetWindowTitles(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.ShowTitle)]
	public static extern void ShowTitle(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(IntuitionLvo.SizeWindow)]
	public static extern void SizeWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(IntuitionLvo.ViewAddress)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ViewAddress();

	[AmigaLvo(IntuitionLvo.ViewPortAddress)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ViewPortAddress(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.WindowToBack)]
	public static extern void WindowToBack(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.WindowToFront)]
	public static extern void WindowToFront(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.WindowLimits)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WindowLimits(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] uint arg3,
		[M68kRegister(M68kRegister.D3)] uint arg4);

	[AmigaLvo(IntuitionLvo.SetPrefs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetPrefs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(IntuitionLvo.IntuiTextLength)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IntuiTextLength(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.WBenchToBack)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WBenchToBack();

	[AmigaLvo(IntuitionLvo.WBenchToFront)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int WBenchToFront();

	[AmigaLvo(IntuitionLvo.AutoRequest)]
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

	[AmigaLvo(IntuitionLvo.BeginRefresh)]
	public static extern void BeginRefresh(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	// Register map is taken from the MorphOS 3.20 intuition_lib.fd. The
	// Commodore V40 declaration remains pending NDK validation.
	[AmigaLvo(IntuitionLvo.BuildSysRequest)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BuildSysRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3,
		[M68kRegister(M68kRegister.D0)] uint arg4,
		[M68kRegister(M68kRegister.D1)] uint arg5,
		[M68kRegister(M68kRegister.D2)] uint arg6);

	[AmigaLvo(IntuitionLvo.EndRefresh)]
	public static extern void EndRefresh(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(IntuitionLvo.FreeSysRequest)]
	public static extern void FreeSysRequest(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.MakeScreen)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MakeScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.RemakeDisplay)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RemakeDisplay();

	[AmigaLvo(IntuitionLvo.RethinkDisplay)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RethinkDisplay();

	[AmigaLvo(IntuitionLvo.AllocRemember)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocRemember(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2);

	[AmigaLvo(IntuitionLvo.FreeRemember)]
	public static extern void FreeRemember(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1);

	[AmigaLvo(IntuitionLvo.LockIBase)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockIBase(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(IntuitionLvo.UnlockIBase)]
	public static extern void UnlockIBase(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.GetScreenData)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetScreenData(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.A1), M68kWritesBuffer] uint arg3);

	[AmigaLvo(IntuitionLvo.RefreshGList)]
	public static extern void RefreshGList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] int arg3);

	[AmigaLvo(IntuitionLvo.AddGList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort AddGList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.A2)] uint arg4);

	[AmigaLvo(IntuitionLvo.RemoveGList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort RemoveGList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(IntuitionLvo.ActivateWindow)]
	public static extern void ActivateWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.RefreshWindowFrame)]
	public static extern void RefreshWindowFrame(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.ActivateGadget)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ActivateGadget(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.NewModifyProp)]
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

	[AmigaLvo(IntuitionLvo.QueryOverscan)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int QueryOverscan(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1), M68kWritesEntireBuffer] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(IntuitionLvo.MoveWindowInFrontOf)]
	public static extern void MoveWindowInFrontOf(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.ChangeWindowBox)]
	public static extern void ChangeWindowBox(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(IntuitionLvo.SetEditHook)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetEditHook(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.SetMouseQueue)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetMouseQueue(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(IntuitionLvo.ZipWindow)]
	public static extern void ZipWindow(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.LockPubScreen)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.UnlockPubScreen)]
	public static extern void UnlockPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.LockPubScreenList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint LockPubScreenList();

	[AmigaLvo(IntuitionLvo.UnlockPubScreenList)]
	public static extern void UnlockPubScreenList();

	[AmigaLvo(IntuitionLvo.NextPubScreen)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.SetDefaultPubScreen)]
	public static extern void SetDefaultPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.SetPubScreenModes)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort SetPubScreenModes(
		[M68kRegister(M68kRegister.D0)] uint arg0);

	[AmigaLvo(IntuitionLvo.PubScreenStatus)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort PubScreenStatus(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(IntuitionLvo.ObtainGIRPort)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainGIRPort(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.ReleaseGIRPort)]
	public static extern void ReleaseGIRPort(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.GadgetMouse)]
	public static extern void GadgetMouse(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.GetDefaultPubScreen)]
	public static extern void GetDefaultPubScreen(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.EasyRequestArgs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int EasyRequestArgs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(IntuitionLvo.BuildEasyRequestArgs)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint BuildEasyRequestArgs(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(IntuitionLvo.SysReqHandler)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SysReqHandler(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2);

	[AmigaLvo(IntuitionLvo.OpenWindowTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenWindowTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.OpenScreenTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenScreenTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.DrawImageState)]
	public static extern void DrawImageState(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] uint arg4,
		[M68kRegister(M68kRegister.A2)] uint arg5);

	[AmigaLvo(IntuitionLvo.PointInImage)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int PointInImage(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1);

	[AmigaLvo(IntuitionLvo.EraseImage)]
	public static extern void EraseImage(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(IntuitionLvo.NewObjectA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewObjectA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	[AmigaLvo(IntuitionLvo.NewObjectA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint NewObject(
		[M68kRegister(M68kRegister.A0)] uint classPtr,
		[M68kRegister(M68kRegister.A1)] uint classId,
		[M68kRegister(M68kRegister.A2)]
		[AmigaStackVarargs]
		params AmigaVarArg[] tags) =>
		throw new System.NotSupportedException(
			"Intuition.NewObject stack varargs are lowered by CopperSharp.");

	[AmigaLvo(IntuitionLvo.DisposeObject)]
	public static extern void DisposeObject(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.SetAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetAttrsA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.GetAttr)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetAttr(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	[AmigaLvo(IntuitionLvo.SetGadgetAttrsA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetGadgetAttrsA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(IntuitionLvo.NextObject)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NextObject(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.MakeClass)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint MakeClass(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.D0)] uint arg3,
		[M68kRegister(M68kRegister.D1)] uint arg4);

	[AmigaLvo(IntuitionLvo.AddClass)]
	public static extern void AddClass(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.GetScreenDrawInfo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetScreenDrawInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.FreeScreenDrawInfo)]
	public static extern void FreeScreenDrawInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.ResetMenuStrip)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ResetMenuStrip(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.RemoveClass)]
	public static extern void RemoveClass(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.FreeClass)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FreeClass(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(IntuitionLvo.AllocScreenBuffer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocScreenBuffer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] uint arg2);

	[AmigaLvo(IntuitionLvo.FreeScreenBuffer)]
	public static extern void FreeScreenBuffer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.ChangeScreenBuffer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ChangeScreenBuffer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.ScreenDepth)]
	public static extern void ScreenDepth(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	[AmigaLvo(IntuitionLvo.ScreenPosition)]
	public static extern void ScreenPosition(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5);

	[AmigaLvo(IntuitionLvo.ScrollWindowRaster)]
	public static extern void ScrollWindowRaster(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5,
		[M68kRegister(M68kRegister.D5)] int arg6);

	[AmigaLvo(IntuitionLvo.LendMenus)]
	public static extern void LendMenus(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.DoGadgetMethodA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DoGadgetMethodA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2,
		[M68kRegister(M68kRegister.A3)] uint arg3);

	[AmigaLvo(IntuitionLvo.SetWindowPointerA)]
	public static extern void SetWindowPointerA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(IntuitionLvo.TimedDisplayAlert)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int TimedDisplayAlert(
		[M68kRegister(M68kRegister.D0)] uint arg0,
		[M68kRegister(M68kRegister.A0)] uint arg1,
		[M68kRegister(M68kRegister.D1)] uint arg2,
		[M68kRegister(M68kRegister.A1)] uint arg3);

	[AmigaLvo(IntuitionLvo.HelpControl)]
	public static extern void HelpControl(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.GetSkinInfoAttrA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetSkinInfoAttrA(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.GetDrawInfoAttr)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetDrawInfoAttr(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.WindowAction)]
	public static extern void WindowAction(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.TransparencyControl)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int TransparencyControl(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1,
		[M68kRegister(M68kRegister.A1)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.ScrollWindowRasterNoFill)]
	public static extern void ScrollWindowRasterNoFill(
		[M68kRegister(M68kRegister.A1)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4,
		[M68kRegister(M68kRegister.D4)] int arg5,
		[M68kRegister(M68kRegister.D5)] int arg6);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.GetMonitorList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMonitorList(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.FreeMonitorList)]
	public static extern void FreeMonitorList(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.ScreenbarControlA)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ScreenbarControlA(
		[M68kRegister(M68kRegister.A1)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.GetMonitorModesList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMonitorModesList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.FreeMonitorModesList)]
	public static extern void FreeMonitorModesList(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(IntuitionLvo.GetMonitorMode)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetMonitorMode(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.A1)] uint arg4);
}
