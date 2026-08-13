/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>
/// Public V40/MorphOS m68k layer envelope from graphics/clip.h.
/// Applications must treat the system-owned fields as read-only.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct Layer
{
	public const uint Size = 160;

	public APTR Front;
	public APTR Back;
	public APTR ClipRect;
	public APTR RastPort;
	public Rectangle Bounds;
	public fixed byte Reserved[4];
	public ushort Priority;
	public LayerFlags Flags;
	public APTR SuperBitMap;
	public APTR SuperClipRect;
	public APTR Window;
	public short ScrollX;
	public short ScrollY;
	public APTR ClipRectWork;
	public APTR ClipRectWork2;
	public APTR NewClipRect;
	public APTR SuperSaveClipRects;
	public APTR ClipRects;
	public APTR LayerInfo;
	public SignalSemaphore Lock;
	public APTR BackFill;
	public uint Reserved1;
	public APTR ClipRegion;
	public APTR SaveClipRects;
	public short Width;
	public short Height;
	public fixed byte Reserved2[18];
	public APTR DamageList;
}

/// <summary>
/// Public ClipRect envelope. The library may allocate a larger private record;
/// callers must follow only this published prefix. Public layer ClipRect lists
/// are singly linked through <see cref="Next"/>. The offset-4 member is a
/// reserved library link; <see cref="Previous"/> is retained as the legacy SDK
/// compatibility name and must not be assumed to point to the prior Next node.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ClipRect
{
	public const uint Size = 36;

	public APTR Next;
	/// <summary>Reserved library link; not a reciprocal public-list pointer.</summary>
	public APTR Previous;
	public APTR ObscuringLayer;
	public APTR BitMap;
	public Rectangle Bounds;
	public APTR ReservedPointer1;
	public APTR ReservedPointer2;
	public int Reserved;
}

/// <summary>
/// MorphOS legacy hook extension accepted by CreateUpfrontHookLayer and
/// CreateBehindHookLayer. New code should prefer the V50 tag-list calls.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewLayerHook
{
	public const uint Size = 28;

	public MinNode MinNode;
	public APTR Entry;
	public APTR SubEntry;
	public APTR Data;
	public APTR TransparentRegionHook;
	public APTR TransparentRegion;
}

/// <summary>
/// Message supplied to layer backfill hooks and DoHookClipRects callbacks.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LayerBackfillMessage
{
	public const uint Size = 20;

	public APTR Layer;
	public Rectangle Bounds;
	public int OffsetX;
	public int OffsetY;
}

/// <summary>
/// Message supplied to LayerInfo backfill hooks. The first longword is
/// explicitly undefined and must not be interpreted as a Layer pointer.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LayerInfoBackfillMessage
{
	public const uint Size = 12;

	public uint Undefined;
	public Rectangle Bounds;
}
