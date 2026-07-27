/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.MUI;

public readonly struct CustomClass
{
	private const int ClassPointerOffset = 0;

	public CustomClass(uint raw)
	{
		Raw = raw;
	}

	public uint Raw { get; }

	public bool IsNull => Raw == 0;

	public unsafe uint ClassPointer => *(uint*)(Raw + ClassPointerOffset);

	public static CustomClass CreatePrivate(CString superName, int dataSize, APTR dispatcher) =>
		new(global::Amiga.MUIMaster.MUI_CreateCustomClass(0, superName, 0, dataSize, dispatcher));

	public uint NewObject(uint tags) =>
		global::Amiga.Intuition.NewObjectA(ClassPointer, 0, tags);

	public bool Delete() =>
		global::Amiga.MUIMaster.MUI_DeleteCustomClass(Raw) != 0;

	public static implicit operator uint(CustomClass value) => value.Raw;

	public static explicit operator CustomClass(uint value) => new(value);
}
