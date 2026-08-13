/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Compile-time byte offsets for guest-resident Layers structures.</summary>
public static class LayersLayout
{
	public static class Layer
	{
		public const int Size = 160;
		public const int Front = 0;
		public const int Back = 4;
		public const int ClipRect = 8;
		public const int RastPort = 12;
		public const int Bounds = 16;
		public const int Reserved = 24;
		public const int Priority = 28;
		public const int Flags = 30;
		public const int SuperBitMap = 32;
		public const int SuperClipRect = 36;
		public const int Window = 40;
		public const int ScrollX = 44;
		public const int ScrollY = 46;
		public const int ClipRectWork = 48;
		public const int ClipRectWork2 = 52;
		public const int NewClipRect = 56;
		public const int SuperSaveClipRects = 60;
		public const int ClipRects = 64;
		public const int LayerInfo = 68;
		public const int Lock = 72;
		public const int BackFill = 118;
		public const int Reserved1 = 122;
		public const int ClipRegion = 126;
		public const int SaveClipRects = 130;
		public const int Width = 134;
		public const int Height = 136;
		public const int Reserved2 = 138;
		public const int DamageList = 156;
	}

	public static class ClipRect
	{
		public const int Size = 36;
		public const int Next = 0;
		/// <summary>Reserved library link; not a reciprocal public-list pointer.</summary>
		public const int Previous = 4;
		public const int ObscuringLayer = 8;
		public const int BitMap = 12;
		public const int Bounds = 16;
		public const int ReservedPointer1 = 24;
		public const int ReservedPointer2 = 28;
		public const int Reserved = 32;
	}

	/// <summary>
	/// Conditional NEWCLIPRECTS_1_1 tail published by MorphOS. It is not part
	/// of the default 36-byte public ClipRect profile.
	/// </summary>
	public static class ExtendedClipRect
	{
		public const int PublicPrefixSize = ClipRect.Size;
		public const int Flags = 36;
		public const int Size = 40;
	}

	public static class LayerInfo
	{
		public const int Size = 102;
		public const int TopLayer = 0;
		public const int CheckLayer = 4;
		public const int Obscured = 8;
		public const int FreeClipRects = 12;
		public const int PrivateReserve1 = 16;
		public const int PrivateReserve2 = 20;
		public const int Lock = 24;
		public const int GraphicsSemaphoreHead = 70;
		public const int PrivateReserve3 = 82;
		public const int PrivateReserve4 = 84;
		public const int Flags = 88;
		public const int FattenCount = 90;
		public const int LockLayersCount = 91;
		public const int PrivateReserve5 = 92;
		public const int BlankHook = 94;
		public const int Extra = 98;
	}

	public static class NewLayerHook
	{
		public const int Size = 28;
		public const int MinNode = 0;
		public const int Entry = 8;
		public const int SubEntry = 12;
		public const int Data = 16;
		public const int TransparentRegionHook = 20;
		public const int TransparentRegion = 24;
	}

	public static class LayerBackfillMessage
	{
		public const int Size = 20;
		public const int Layer = 0;
		public const int Bounds = 4;
		public const int OffsetX = 12;
		public const int OffsetY = 16;
	}

	public static class LayerInfoBackfillMessage
	{
		public const int Size = 12;
		public const int Undefined = 0;
		public const int Bounds = 4;
	}
}
