/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Signed pointer-sized integer used by MorphOS-style APIs.
/// </summary>
/// <remarks>
/// CopperSharp currently lowers native integers to 32-bit values for its
/// 68k target. Keep SIPTR out of fixed-width 68k guest records; use int or
/// another explicitly sized type there.
/// </remarks>
public readonly struct SIPTR
{
	private readonly nint _value;

	public SIPTR(nint value)
	{
		_value = value;
	}

	/// <summary>Size of SIPTR in the current 68000-family guest ABI.</summary>
	public const uint M68kSize = 4;

	public nint Raw => _value;

	public static SIPTR Zero => new(0);

	public static SIPTR FromIntPtr(nint value) => new(value);

	public static SIPTR FromInt32(int value) => new((nint)value);

	public static SIPTR FromBits(uint value) =>
		new(unchecked((nint)(int)value));

	public static nint ToIntPtr(SIPTR value) => value._value;

	public static int ToInt32(SIPTR value) => unchecked((int)value._value);

	public static uint ToBits(SIPTR value) =>
		unchecked((uint)(int)value._value);

	public static implicit operator SIPTR(nint value) =>
		FromIntPtr(value);

	public static implicit operator SIPTR(int value) =>
		FromInt32(value);

	public static implicit operator nint(SIPTR value) =>
		ToIntPtr(value);

	public static explicit operator int(SIPTR value) =>
		ToInt32(value);
}
