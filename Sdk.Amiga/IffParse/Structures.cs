/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Raw ABI record allocated by iffparse.library for an IFF stream.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IFFHandleRecord
{
	public const uint Size = 12;

	public uint Stream;
	public uint Flags;
	public int Depth;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct IFFStreamCmd
{
	public const uint Size = 12;

	public int Command;
	public APTR Buffer;
	public int NumberOfBytes;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct ContextNode
{
	public const uint Size = 24;

	public MinNode Node;
	public int Id;
	public int Type;
	public int SizeInBytes;
	public int Scanned;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LocalContextItem
{
	public const uint Size = 20;

	public MinNode Node;
	public uint Id;
	public uint Type;
	public uint Identifier;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct StoredProperty
{
	public const uint Size = 8;

	public int SizeInBytes;
	public APTR Data;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct CollectionItem
{
	public const uint Size = 12;

	public APTR Next;
	public int SizeInBytes;
	public APTR Data;
}
