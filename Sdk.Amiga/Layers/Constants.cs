/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// LONG-sized creation flags used in the D4 argument of layer creation calls.
/// Layer.Flags uses the corresponding UWORD-sized <see cref="LayerFlags"/>.
/// </summary>
[System.Flags]
public enum LayerCreationFlags : int
{
	None = 0,
	Simple = 0x0001,
	Smart = 0x0002,
	Super = 0x0004,
	Backdrop = 0x0040,
}

[System.Flags]
public enum LayerInfoFlags : ushort
{
	None = 0,
	NewLayerInfoCalled = 0x0001,
}

/// <summary>
/// Optional private ClipRect flags published by MorphOS for implementations
/// that opt into the extended ClipRect record.
/// </summary>
[System.Flags]
public enum ClipRectFlags : int
{
	None = 0,
	NeedsNoConcealedRasters = 0x0001,
	NeedsNoLayerBlitDamage = 0x0002,
}

[System.Flags]
public enum ClipRectPositionFlags : int
{
	None = 0,
	LessX = 0x0001,
	LessY = 0x0002,
	GreaterX = 0x0004,
	GreaterY = 0x0008,
}

/// <summary>MorphOS V50 Create*LayerTagList tags.</summary>
public enum LayerCreationTag : uint
{
	Dummy = 0x8000_0400u,
	BackfillHook = Dummy + 0x0001u,
	TransparentRegion = Dummy + 0x0002u,
	TransparentHook = Dummy + 0x0003u,
	WindowPointer = Dummy + 0x0004u,
	SuperBitMap = Dummy + 0x0005u,
}

/// <summary>MorphOS V52 RenderLayerInfoTagList tags.</summary>
public enum LayerRenderTag : uint
{
	Dummy = 0x8000_047Eu,
	DestinationRastPort = Dummy + 0x0001u,
	DestinationBitMap = Dummy + 0x0002u,
	DestinationBounds = Dummy + 0x0003u,
	LayerInfoBounds = Dummy + 0x0004u,
	Erase = Dummy + 0x0005u,
	RenderList = Dummy + 0x0006u,
	IgnoreList = Dummy + 0x0007u,
	ApplyOpacityMultiplier = Dummy + 0x0008u,
}

/// <summary>Published special Hook pointer values accepted by Layers.</summary>
public static class LayerBackfillHook
{
	public const uint Backfill = 0;
	public const uint NoBackfill = 1;
	public const uint NeverBackfill = 2;
}

/// <summary>Published ABI/version facts used for profile admission.</summary>
public static class LayersAbiConstants
{
	public const ushort ClassicV40 = 40;
	public const ushort MorphOsV50 = 50;
	public const ushort MorphOsV52 = 52;
	public const ushort NeverBackfillMinimumVersion = MorphOsV52;
	public const ushort NeverBackfillRevision = 22;
	public const uint NewLayerHookDataMagic = 0x4359_4252u;
	public const int ClassicVectorCount = 32;
	public const int MorphOsM68kExtensionCount = 9;
}

/// <summary>Minimum library version for each MorphOS extension vector.</summary>
public static class LayersVectorVersion
{
	public const ushort CreateUpfrontLayerTagList = LayersAbiConstants.MorphOsV50;
	public const ushort CreateBehindLayerTagList = LayersAbiConstants.MorphOsV50;
	public const ushort WhichLayerBehindLayer = LayersAbiConstants.MorphOsV52;
	public const ushort IsLayerVisible = LayersAbiConstants.MorphOsV52;
	public const ushort RenderLayerInfoTagList = LayersAbiConstants.MorphOsV52;
	public const ushort LockLayerUpdates = LayersAbiConstants.MorphOsV52;
	public const ushort UnlockLayerUpdates = LayersAbiConstants.MorphOsV52;
	public const ushort IsVisibleInLayer = LayersAbiConstants.MorphOsV52;
	public const ushort IsLayerHitable = LayersAbiConstants.MorphOsV52;
}
