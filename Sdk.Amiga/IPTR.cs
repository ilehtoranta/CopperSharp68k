/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Unsigned pointer-sized integer used by MorphOS-style APIs.
/// </summary>
/// <remarks>
/// CopperSharp currently lowers native integers to 32-bit values for its
/// 68k target. The native-integer representation is intentional: a future
/// target can widen IPTR without changing the API's semantic type. Do not use
/// IPTR in a fixed-width 68k guest record; use uint or APTR there instead.
/// </remarks>
public readonly struct IPTR
{
	private readonly nuint _value;

	public IPTR(nuint value)
	{
		_value = value;
	}

	/// <summary>Size of IPTR in the current 68000-family guest ABI.</summary>
	public const uint M68kSize = 4;

	public nuint Raw => _value;

	public static IPTR Zero => new(0);

	public static IPTR FromUIntPtr(nuint value) => new(value);

	public static IPTR FromUInt32(uint value) => new((nuint)value);

	public static nuint ToUIntPtr(IPTR value) => value._value;

	/// <summary>Returns the current 32-bit guest representation.</summary>
	public static uint ToUInt32(IPTR value) => unchecked((uint)value._value);

	public static implicit operator IPTR(nuint value) =>
		FromUIntPtr(value);

	public static implicit operator IPTR(uint value) =>
		FromUInt32(value);

	public static implicit operator nuint(IPTR value) =>
		ToUIntPtr(value);

	public static explicit operator uint(IPTR value) =>
		ToUInt32(value);
}
