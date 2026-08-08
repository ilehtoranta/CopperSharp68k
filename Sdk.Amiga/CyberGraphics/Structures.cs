/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct CyberModeNode
{
	public const uint Size = 60;

	public Node Node;
	public fixed byte ModeText[32];
	public uint DisplayId;
	public ushort Width;
	public ushort Height;
	public ushort Depth;
	public APTR DisplayTagList;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CDrawMsg
{
	public const uint Size = 26;

	public APTR Memory;
	public uint OffsetX;
	public uint OffsetY;
	public uint XSize;
	public uint YSize;
	public ushort BytesPerRow;
	public ushort BytesPerPixel;
	public ushort ColorModel;
}
