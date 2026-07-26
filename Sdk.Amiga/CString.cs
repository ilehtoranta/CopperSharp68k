/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct CString
{
	private readonly uint _pointer;

	private CString(uint pointer)
	{
		_pointer = pointer;
	}

	public static CString FromLiteral(string value) =>
		throw new System.NotSupportedException("CString literals are lowered by CopperSharp.");

	public static CString FromPointer(uint pointer) =>
		throw new System.NotSupportedException("CString pointer conversion is lowered by CopperSharp.");

	public static uint ToUInt32(CString value) =>
		throw new System.NotSupportedException("CString pointer conversion is lowered by CopperSharp.");

	public static implicit operator CString(string value) =>
		FromLiteral(value);

	public static implicit operator CString(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(CString value) =>
		ToUInt32(value);
}
