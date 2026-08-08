/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct KeyMap
{
	public const uint Size = 32;

	public APTR LowKeyMapTypes;
	public APTR LowKeyMap;
	public APTR LowCapsable;
	public APTR LowRepeatable;
	public APTR HighKeyMapTypes;
	public APTR HighKeyMap;
	public APTR HighCapsable;
	public APTR HighRepeatable;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct KeyMapNode
{
	public const uint Size = 46;

	public Node Node;
	public KeyMap KeyMap;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ExtendedKeyMapNode
{
	public const uint Size = 60;

	public Node Node;
	public ushort NodePadding;
	public KeyMap KeyMap;
	public BPTR SegmentList;
	public APTR Resident;
	public APTR Future;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct KeyMapResource
{
	public const uint Size = 28;

	public Node Node;
	public List KeyMaps;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct UCS4ConversionTable
{
	public const uint Size = 8;

	public ushort FirstChar;
	public ushort LastChar;
	public APTR ConversionTable;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct UCS4CharsetCode
{
	public const uint Size = 8;

	public uint Ucs4;
	public uint CharsetCode;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct UCS4CharsetConversionTable
{
	public const uint Size = 12;

	public APTR Mapping;
	public UCS4ConversionTable FirstConversionTable;
}
