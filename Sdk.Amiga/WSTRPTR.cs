/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Guest pointer to a NULL-terminated/native-endian MorphOS UCS-4 string.
/// Each code point occupies one 32-bit <c>WCHAR</c> element.
/// </summary>
public readonly struct WSTRPTR
{
	private readonly uint _pointer;

	public WSTRPTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static WSTRPTR Null => new(0);

	public uint Raw => _pointer;

	public APTR Address => APTR.FromPointer(_pointer);

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static WSTRPTR FromPointer(uint pointer) => new(pointer);

	public static WSTRPTR FromAddress(APTR pointer) =>
		new(APTR.ToUInt32(pointer));

	public static uint ToUInt32(WSTRPTR pointer) => pointer._pointer;

	public static APTR ToAddress(WSTRPTR pointer) => pointer.Address;

	public static implicit operator WSTRPTR(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(WSTRPTR pointer) =>
		ToUInt32(pointer);

	public static implicit operator WSTRPTR(APTR pointer) =>
		FromAddress(pointer);

	public static implicit operator APTR(WSTRPTR pointer) =>
		ToAddress(pointer);
}
