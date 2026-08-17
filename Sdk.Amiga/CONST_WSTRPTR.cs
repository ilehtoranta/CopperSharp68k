/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Read-only guest pointer to a NULL-terminated/native-endian MorphOS UCS-4 string.
/// </summary>
public readonly struct CONST_WSTRPTR
{
	private readonly uint _pointer;

	public CONST_WSTRPTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static CONST_WSTRPTR Null => new(0);

	public uint Raw => _pointer;

	public APTR Address => APTR.FromPointer(_pointer);

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static CONST_WSTRPTR FromPointer(uint pointer) => new(pointer);

	public static CONST_WSTRPTR FromAddress(APTR pointer) =>
		new(APTR.ToUInt32(pointer));

	public static uint ToUInt32(CONST_WSTRPTR pointer) => pointer._pointer;

	public static APTR ToAddress(CONST_WSTRPTR pointer) => pointer.Address;

	public static implicit operator CONST_WSTRPTR(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(CONST_WSTRPTR pointer) =>
		ToUInt32(pointer);

	public static implicit operator CONST_WSTRPTR(APTR pointer) =>
		FromAddress(pointer);

	public static implicit operator APTR(CONST_WSTRPTR pointer) =>
		ToAddress(pointer);

	public static implicit operator CONST_WSTRPTR(WSTRPTR pointer) =>
		FromPointer(WSTRPTR.ToUInt32(pointer));
}
