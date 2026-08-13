/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Layers
{
	public const string Name = "layers.library";

	public static APTR LayersLibraryBase
	{
		get => throw new System.NotSupportedException(
			"LayersLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"LayersLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(LayersLvo.InitLayers)]
	public static extern void InitLayers(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.CreateUpfrontLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateUpfrontLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR bitMap,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY,
		[M68kRegister(M68kRegister.D4)] LayerCreationFlags flags,
		[M68kRegister(M68kRegister.A2)] APTR superBitMap);

	[AmigaLvo(LayersLvo.CreateBehindLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateBehindLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR bitMap,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY,
		[M68kRegister(M68kRegister.D4)] LayerCreationFlags flags,
		[M68kRegister(M68kRegister.A2)] APTR superBitMap);

	[AmigaLvo(LayersLvo.UpfrontLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int UpfrontLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer);

	[AmigaLvo(LayersLvo.BehindLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BehindLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer);

	[AmigaLvo(LayersLvo.MoveLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MoveLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int deltaX,
		[M68kRegister(M68kRegister.D1)] int deltaY);

	[AmigaLvo(LayersLvo.SizeLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SizeLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int deltaWidth,
		[M68kRegister(M68kRegister.D1)] int deltaHeight);

	[AmigaLvo(LayersLvo.ScrollLayer)]
	public static extern void ScrollLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int deltaX,
		[M68kRegister(M68kRegister.D1)] int deltaY);

	[AmigaLvo(LayersLvo.BeginUpdate)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int BeginUpdate(
		[M68kRegister(M68kRegister.A0)] APTR layer);

	[AmigaLvo(LayersLvo.EndUpdate)]
	public static extern void EndUpdate(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.D0)] uint complete);

	[AmigaLvo(LayersLvo.DeleteLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DeleteLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer);

	[AmigaLvo(LayersLvo.LockLayer)]
	public static extern void LockLayer(
		[M68kRegister(M68kRegister.A0)] int dummy,
		[M68kRegister(M68kRegister.A1)] APTR layer);

	[AmigaLvo(LayersLvo.UnlockLayer)]
	public static extern void UnlockLayer(
		[M68kRegister(M68kRegister.A0)] APTR layer);

	[AmigaLvo(LayersLvo.LockLayers)]
	public static extern void LockLayers(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.UnlockLayers)]
	public static extern void UnlockLayers(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.LockLayerInfo)]
	public static extern void LockLayerInfo(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.SwapBitsRastPortClipRect)]
	public static extern void SwapBitsRastPortClipRect(
		[M68kRegister(M68kRegister.A0)] APTR rastPort,
		[M68kRegister(M68kRegister.A1)] APTR clipRect);

	[AmigaLvo(LayersLvo.WhichLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR WhichLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.D0)] int x,
		[M68kRegister(M68kRegister.D1)] int y);

	[AmigaLvo(LayersLvo.UnlockLayerInfo)]
	public static extern void UnlockLayerInfo(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.NewLayerInfo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR NewLayerInfo();

	[AmigaLvo(LayersLvo.DisposeLayerInfo)]
	public static extern void DisposeLayerInfo(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.FattenLayerInfo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int FattenLayerInfo(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.ThinLayerInfo)]
	public static extern void ThinLayerInfo(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo);

	[AmigaLvo(LayersLvo.MoveLayerInFrontOf)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MoveLayerInFrontOf(
		[M68kRegister(M68kRegister.A0)] APTR layerToMove,
		[M68kRegister(M68kRegister.A1)] APTR otherLayer);

	[AmigaLvo(LayersLvo.InstallClipRegion)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR InstallClipRegion(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.A1)] APTR region);

	[AmigaLvo(LayersLvo.MoveSizeLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MoveSizeLayer(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int deltaX,
		[M68kRegister(M68kRegister.D1)] int deltaY,
		[M68kRegister(M68kRegister.D2)] int deltaWidth,
		[M68kRegister(M68kRegister.D3)] int deltaHeight);

	[AmigaLvo(LayersLvo.CreateUpfrontHookLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateUpfrontHookLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR bitMap,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY,
		[M68kRegister(M68kRegister.D4)] LayerCreationFlags flags,
		[M68kRegister(M68kRegister.A3)] APTR hook,
		[M68kRegister(M68kRegister.A2)] APTR superBitMap);

	[AmigaLvo(LayersLvo.CreateBehindHookLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateBehindHookLayer(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR bitMap,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY,
		[M68kRegister(M68kRegister.D4)] LayerCreationFlags flags,
		[M68kRegister(M68kRegister.A3)] APTR hook,
		[M68kRegister(M68kRegister.A2)] APTR superBitMap);

	[AmigaLvo(LayersLvo.InstallLayerHook)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR InstallLayerHook(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.A1)] APTR hook);

	[AmigaLvo(LayersLvo.InstallLayerInfoHook)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR InstallLayerInfoHook(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR hook);

	[AmigaLvo(LayersLvo.SortLayerCR)]
	public static extern void SortLayerCR(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int deltaX,
		[M68kRegister(M68kRegister.D1)] int deltaY);

	[AmigaLvo(LayersLvo.DoHookClipRects)]
	public static extern void DoHookClipRects(
		[M68kRegister(M68kRegister.A0)] APTR hook,
		[M68kRegister(M68kRegister.A1)] APTR rastPort,
		[M68kRegister(M68kRegister.A2)] APTR bounds);

	// MorphOS V50 m68k ABI extensions.
	[AmigaLvo(LayersLvo.CreateUpfrontLayerTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateUpfrontLayerTagList(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR bitMap,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY,
		[M68kRegister(M68kRegister.D4)] LayerCreationFlags flags,
		[M68kRegister(M68kRegister.A2)] APTR tagList);

	[AmigaLvo(LayersLvo.CreateBehindLayerTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR CreateBehindLayerTagList(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR bitMap,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY,
		[M68kRegister(M68kRegister.D4)] LayerCreationFlags flags,
		[M68kRegister(M68kRegister.A2)] APTR tagList);

	// MorphOS V52 m68k ABI extensions.
	[AmigaLvo(LayersLvo.WhichLayerBehindLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR WhichLayerBehindLayer(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int x,
		[M68kRegister(M68kRegister.D1)] int y);

	[AmigaLvo(LayersLvo.IsLayerVisible)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsLayerVisible(
		[M68kRegister(M68kRegister.A0)] APTR layer);

	[AmigaLvo(LayersLvo.RenderLayerInfoTagList)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RenderLayerInfoTagList(
		[M68kRegister(M68kRegister.A0)] APTR layerInfo,
		[M68kRegister(M68kRegister.A1)] APTR tagList);

	[AmigaLvo(LayersLvo.LockLayerUpdates)]
	public static extern void LockLayerUpdates(
		[M68kRegister(M68kRegister.A0)] APTR layer);

	[AmigaLvo(LayersLvo.UnlockLayerUpdates)]
	public static extern void UnlockLayerUpdates(
		[M68kRegister(M68kRegister.A0)] APTR layer);

	[AmigaLvo(LayersLvo.IsVisibleInLayer)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsVisibleInLayer(
		[M68kRegister(M68kRegister.A0)] APTR layer,
		[M68kRegister(M68kRegister.D0)] int minX,
		[M68kRegister(M68kRegister.D1)] int minY,
		[M68kRegister(M68kRegister.D2)] int maxX,
		[M68kRegister(M68kRegister.D3)] int maxY);

	[AmigaLvo(LayersLvo.IsLayerHitable)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IsLayerHitable(
		[M68kRegister(M68kRegister.A0)] APTR layer);

	/// <summary>
	/// Pointer-list form of the official MorphOS RenderLayerInfoTags varargs
	/// wrapper. CopperSharp callers supply the address of a TagItem sequence.
	/// </summary>
	public static int RenderLayerInfoTags(APTR layerInfo, APTR tags) =>
		RenderLayerInfoTagList(layerInfo, tags);
}
