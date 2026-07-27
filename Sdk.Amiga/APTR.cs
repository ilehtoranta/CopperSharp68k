/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct APTR
{
	private readonly uint _pointer;

	public APTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static APTR Null => new(0);

	public uint Raw => _pointer;

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static APTR FromPointer(uint pointer) => new(pointer);

	public static APTR ExportAddress(string exportName) =>
		throw new System.NotSupportedException(
			"APTR.ExportAddress is lowered by CopperSharp.");

	public static uint ToUInt32(APTR pointer) => pointer._pointer;

	public static implicit operator APTR(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(APTR pointer) =>
		ToUInt32(pointer);
}
