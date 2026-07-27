/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct CONST_STRPTR
{
	private readonly uint _pointer;

	public CONST_STRPTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static CONST_STRPTR Null => new(0);

	public uint Raw => _pointer;

	public APTR Address => APTR.FromPointer(_pointer);

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static CONST_STRPTR FromPointer(uint pointer) => new(pointer);

	public static CONST_STRPTR FromAddress(APTR pointer) =>
		new(APTR.ToUInt32(pointer));

	public static uint ToUInt32(CONST_STRPTR pointer) => pointer._pointer;

	public static APTR ToAddress(CONST_STRPTR pointer) => pointer.Address;

	public static implicit operator CONST_STRPTR(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(CONST_STRPTR pointer) =>
		ToUInt32(pointer);

	public static implicit operator CONST_STRPTR(APTR pointer) =>
		FromAddress(pointer);

	public static implicit operator APTR(CONST_STRPTR pointer) =>
		ToAddress(pointer);

	public static implicit operator CONST_STRPTR(STRPTR pointer) =>
		FromPointer(STRPTR.ToUInt32(pointer));
}
