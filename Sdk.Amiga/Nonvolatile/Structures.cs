/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NVInfo
{
	public const uint Size = 8;

	public uint MaximumStorage;
	public uint FreeStorage;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct NVEntry
{
	public const uint Size = 20;

	public MinNode Node;
	public STRPTR Name;
	public uint SizeInBytes;
	public uint Protection;
}
