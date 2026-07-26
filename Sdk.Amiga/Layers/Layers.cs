/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Layers
{
	public const string Name = "layers.library";

	[AmigaLvo(-30)]
	public static extern void InitLayers(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateUpfrontLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.A2)] uint arg7);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateBehindLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.A2)] uint arg7);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UpfrontLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BehindLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MoveLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SizeLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-72)]
	public static extern void ScrollLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BeginUpdate(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-84)]
	public static extern void EndUpdate(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] uint arg1);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DeleteLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-96)]
	public static extern void LockLayer(
		[M68kRegister(M68kRegister.A0)] int arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-102)]
	public static extern void UnlockLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-108)]
	public static extern void LockLayers(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-114)]
	public static extern void UnlockLayers(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-120)]
	public static extern void LockLayerInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-126)]
	public static extern void SwapBitsRastPortClipRect(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WhichLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-138)]
	public static extern void UnlockLayerInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewLayerInfo();

	[AmigaLvo(-150)]
	public static extern void DisposeLayerInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-156)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FattenLayerInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-162)]
	public static extern void ThinLayerInfo(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MoveLayerInFrontOf(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-174)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint InstallClipRegion(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-180)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MoveSizeLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	[AmigaLvo(-186)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateUpfrontHookLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.A3)] uint arg7,
		[M68kRegister(M68kRegister.A2)] uint arg8);

	[AmigaLvo(-192)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateBehindHookLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.A3)] uint arg7,
		[M68kRegister(M68kRegister.A2)] uint arg8);

	[AmigaLvo(-198)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint InstallLayerHook(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-204)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint InstallLayerInfoHook(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	[AmigaLvo(-210)]
	public static extern void SortLayerCR(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	[AmigaLvo(-216)]
	public static extern void DoHookClipRects(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.A2)] uint arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(-234)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateUpfrontLayerTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.A2)] uint arg7);

	// MorphOS m68k ABI call.
	[AmigaLvo(-240)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CreateBehindLayerTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1,
		[M68kRegister(M68kRegister.D0)] int arg2,
		[M68kRegister(M68kRegister.D1)] int arg3,
		[M68kRegister(M68kRegister.D2)] int arg4,
		[M68kRegister(M68kRegister.D3)] int arg5,
		[M68kRegister(M68kRegister.D4)] int arg6,
		[M68kRegister(M68kRegister.A2)] uint arg7);

	// MorphOS m68k ABI call.
	[AmigaLvo(-252)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint WhichLayerBehindLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2);

	// MorphOS m68k ABI call.
	[AmigaLvo(-258)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsLayerVisible(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-282)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RenderLayerInfoTagList(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.A1)] uint arg1);

	// MorphOS m68k ABI call.
	[AmigaLvo(-288)]
	public static extern void LockLayerUpdates(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-294)]
	public static extern void UnlockLayerUpdates(
		[M68kRegister(M68kRegister.A0)] uint arg0);

	// MorphOS m68k ABI call.
	[AmigaLvo(-300)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsVisibleInLayer(
		[M68kRegister(M68kRegister.A0)] uint arg0,
		[M68kRegister(M68kRegister.D0)] int arg1,
		[M68kRegister(M68kRegister.D1)] int arg2,
		[M68kRegister(M68kRegister.D2)] int arg3,
		[M68kRegister(M68kRegister.D3)] int arg4);

	// MorphOS m68k ABI call.
	[AmigaLvo(-306)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsLayerHitable(
		[M68kRegister(M68kRegister.A0)] uint arg0);
}
