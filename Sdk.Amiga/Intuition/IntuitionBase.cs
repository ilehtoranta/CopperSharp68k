/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Constants exposed alongside the public IntuitionBase prefix.</summary>
public static class IntuitionBaseConstants
{
	public const ushort DMODECOUNT = 2;
	public const ushort HIRESPICK = 0;
	public const ushort LOWRESPICK = 1;
	public const ushort EVENTMAX = 10;
	public const ushort RESCOUNT = 2;
	public const ushort HIRESGADGET = 0;
	public const ushort LOWRESGADGET = 1;
	public const ushort GADGETCOUNT = 8;
	public const ushort UPFRONTGADGET = 0;
	public const ushort DOWNBACKGADGET = 1;
	public const ushort SIZEGADGET = 2;
	public const ushort CLOSEGADGET = 3;
	public const ushort DRAGGADGET = 4;
	public const ushort SUPFRONTGADGET = 5;
	public const ushort SDOWNBACKGADGET = 6;
	public const ushort SDRAGGADGET = 7;
}

/// <summary>Strictly read-only public prefix of intuition.library base.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IntuitionBase
{
	public const uint Size = 80;
	public Library LibNode;
	public View ViewLord;
	public APTR ActiveWindow;
	public APTR ActiveScreen;
	public APTR FirstScreen;
	public uint Flags;
	public short MouseY;
	public short MouseX;
	public uint Seconds;
	public uint Micros;
}
