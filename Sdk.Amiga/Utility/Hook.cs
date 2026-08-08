/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, Pack = 2)]
public struct Hook
{
	public const uint Size = MinNode.Size + 12;

	public MinNode MinNode;
	public APTR Entry;
	public APTR SubEntry;
	public APTR Data;

	public void Initialize(APTR entry) =>
		Initialize(entry, APTR.Null, APTR.Null);

	public void Initialize(APTR entry, APTR subEntry, APTR data)
	{
		MinNode.Successor = APTR.Null;
		MinNode.Predecessor = APTR.Null;
		Entry = entry;
		SubEntry = subEntry;
		Data = data;
	}

	public static APTR AddressOf(ref Hook hook) =>
		throw new System.NotSupportedException(
			"Hook.AddressOf is lowered by CopperSharp.");
}
