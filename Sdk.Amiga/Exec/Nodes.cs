/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public struct MinNode
{
	public const uint Size = 8;

	public APTR Successor;
	public APTR Predecessor;
}

public struct Node
{
	public const uint Size = 14;

	public APTR Successor;
	public APTR Predecessor;
	public byte Type;
	public sbyte Priority;
	public STRPTR Name;
}
