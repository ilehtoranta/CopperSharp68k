/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Public layers.library m68k vector offsets.</summary>
public static class LayersLvo
{
	// Classic V40 surface: 32 contiguous vectors.
	public const short InitLayers = -30;
	public const short CreateUpfrontLayer = -36;
	public const short CreateBehindLayer = -42;
	public const short UpfrontLayer = -48;
	public const short BehindLayer = -54;
	public const short MoveLayer = -60;
	public const short SizeLayer = -66;
	public const short ScrollLayer = -72;
	public const short BeginUpdate = -78;
	public const short EndUpdate = -84;
	public const short DeleteLayer = -90;
	public const short LockLayer = -96;
	public const short UnlockLayer = -102;
	public const short LockLayers = -108;
	public const short UnlockLayers = -114;
	public const short LockLayerInfo = -120;
	public const short SwapBitsRastPortClipRect = -126;
	public const short WhichLayer = -132;
	public const short UnlockLayerInfo = -138;
	public const short NewLayerInfo = -144;
	public const short DisposeLayerInfo = -150;
	public const short FattenLayerInfo = -156;
	public const short ThinLayerInfo = -162;
	public const short MoveLayerInFrontOf = -168;
	public const short InstallClipRegion = -174;
	public const short MoveSizeLayer = -180;
	public const short CreateUpfrontHookLayer = -186;
	public const short CreateBehindHookLayer = -192;
	public const short InstallLayerHook = -198;
	public const short InstallLayerInfoHook = -204;
	public const short SortLayerCR = -210;
	public const short DoHookClipRects = -216;

	// MorphOS m68k extensions. Unlisted gaps remain reserved.
	public const short CreateUpfrontLayerTagList = -234;
	public const short CreateBehindLayerTagList = -240;
	public const short WhichLayerBehindLayer = -252;
	public const short IsLayerVisible = -258;
	public const short RenderLayerInfoTagList = -282;
	public const short LockLayerUpdates = -288;
	public const short UnlockLayerUpdates = -294;
	public const short IsVisibleInLayer = -300;
	public const short IsLayerHitable = -306;
}
