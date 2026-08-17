/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct AmigaVarArg
{
	private readonly IPTR _value;

	public AmigaVarArg(uint value) =>
		_value = IPTR.FromUInt32(value);

	public AmigaVarArg(IPTR value) =>
		_value = value;

	public AmigaVarArg(SIPTR value) =>
		_value = IPTR.FromUInt32(SIPTR.ToBits(value));

	/// <summary>Current native-width value carried by this vararg.</summary>
	public IPTR Value => _value;

	/// <summary>
	/// Returns the current 32-bit 68k representation. Prefer <see cref="Value"/>
	/// for code that is intended to support a wider target.
	/// </summary>
	public uint Raw => IPTR.ToUInt32(_value);

	public static implicit operator AmigaVarArg(uint value) =>
		new(value);

	public static implicit operator AmigaVarArg(int value) =>
		new(unchecked((uint)value));

	public static implicit operator AmigaVarArg(IPTR value) =>
		new(value);

	public static implicit operator AmigaVarArg(SIPTR value) =>
		new(IPTR.FromUInt32(SIPTR.ToBits(value)));

	public static implicit operator AmigaVarArg(APTR value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(BPTR value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(STRPTR value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(CONST_STRPTR value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(CString value) =>
		new(CString.ToUInt32(value));

	public static implicit operator AmigaVarArg(IFFHandle value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(MUI.MUIObject value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(MUI.ApplicationObject value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(MUI.WindowObject value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(MUI.CustomClass value) =>
		new(value.Raw);

	public static implicit operator AmigaVarArg(string value) =>
		new(CString.ToUInt32(CString.FromLiteral(value)));
}
