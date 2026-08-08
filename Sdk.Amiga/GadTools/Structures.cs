/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewGadget
{
	public const uint Size = 30;

	public short LeftEdge;
	public short TopEdge;
	public short Width;
	public short Height;
	public STRPTR GadgetText;
	public APTR TextAttr;
	public ushort GadgetId;
	public uint Flags;
	public APTR VisualInfo;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NewMenu
{
	public const uint Size = 20;

	public byte Type;
	public byte Padding;
	public STRPTR Label;
	public STRPTR CommandKey;
	public ushort Flags;
	public int MutualExclude;
	public APTR UserData;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LVDrawMsg
{
	public const uint Size = 24;

	public uint MethodId;
	public APTR RastPort;
	public APTR DrawInfo;
	public Rectangle Bounds;
	public uint State;
}
