/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct STRPTR
{
	private readonly uint _pointer;

	public STRPTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static STRPTR Null => new(0);

	public uint Raw => _pointer;

	public APTR Address => APTR.FromPointer(_pointer);

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static STRPTR FromPointer(uint pointer) => new(pointer);

	public static STRPTR FromAddress(APTR pointer) =>
		new(APTR.ToUInt32(pointer));

	public static uint ToUInt32(STRPTR pointer) => pointer._pointer;

	public static APTR ToAddress(STRPTR pointer) => pointer.Address;

	public static implicit operator STRPTR(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(STRPTR pointer) =>
		ToUInt32(pointer);

	public static implicit operator STRPTR(APTR pointer) =>
		FromAddress(pointer);

	public static implicit operator APTR(STRPTR pointer) =>
		ToAddress(pointer);
}
