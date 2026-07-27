/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct BPTR
{
	private readonly uint _pointer;

	public BPTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static BPTR Null => new(0);

	public uint Raw => _pointer;

	public APTR Address => APTR.FromPointer(_pointer << 2);

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static BPTR FromRaw(uint pointer) => new(pointer);

	public static BPTR FromAddress(APTR pointer) =>
		new(APTR.ToUInt32(pointer) >> 2);

	public static uint ToUInt32(BPTR pointer) => pointer._pointer;

	public static APTR ToAddress(BPTR pointer) => pointer.Address;

	public static implicit operator BPTR(uint pointer) =>
		FromRaw(pointer);

	public static implicit operator uint(BPTR pointer) =>
		ToUInt32(pointer);
}
