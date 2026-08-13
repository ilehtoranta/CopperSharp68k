/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>
/// MorphOS transparency-hook message from intuition/extensions.h.
/// All members point to guest-resident Layers or Graphics structures.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct TransparencyMessage
{
	public const uint Size = 16;

	public APTR Layer;
	public APTR Region;
	public APTR NewBounds;
	public APTR OldBounds;
}
