/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct AmigaVarArg
{
	private readonly uint _value;

	public AmigaVarArg(uint value) =>
		_value = value;

	public uint Raw => _value;

	public static implicit operator AmigaVarArg(uint value) =>
		new(value);

	public static implicit operator AmigaVarArg(int value) =>
		new(unchecked((uint)value));

	public static implicit operator AmigaVarArg(APTR value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(BPTR value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(CString value) =>
		new(CString.ToUInt32(value));
}
