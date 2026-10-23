/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Guid's logical fields have native target storage. Byte serialization is
/// explicitly ordered: the default form is little-endian in the first three
/// fields, while the final eight bytes are always in their displayed order.
/// </summary>
public unsafe partial struct ShadowGuid
{
	private int _a;
	private short _b;
	private short _c;
	private byte _d, _e, _f, _g, _h, _i, _j, _k;

	public ShadowGuid(int a, short b, short c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k)
	{
		_a = a; _b = b; _c = c;
		_d = d; _e = e; _f = f; _g = g; _h = h; _i = i; _j = j; _k = k;
	}

	public ShadowGuid(byte[] bytes)
	{
		if (bytes is null) M68kRuntime.ThrowArgumentNullException();
		this = new ShadowGuid((ReadOnlySpan<byte>)bytes, false);
	}

	public ShadowGuid(ReadOnlySpan<byte> bytes) : this(bytes, false) { }

	public ShadowGuid(ReadOnlySpan<byte> bytes, bool bigEndian)
	{
		if (bytes.Length != 16) M68kRuntime.ThrowArgumentException();
		_a = unchecked((int)ReadWord(bytes, 0, 4, bigEndian));
		_b = unchecked((short)ReadWord(bytes, 4, 2, bigEndian));
		_c = unchecked((short)ReadWord(bytes, 6, 2, bigEndian));
		_d = bytes[8]; _e = bytes[9]; _f = bytes[10]; _g = bytes[11];
		_h = bytes[12]; _i = bytes[13]; _j = bytes[14]; _k = bytes[15];
	}

	private static uint ReadWord(ReadOnlySpan<byte> bytes, int start, int length, bool bigEndian)
	{
		uint result = 0;
		for (var i = 0; i < length; i++)
			result |= (uint)bytes[start + i] << ((bigEndian ? length - i - 1 : i) * 8);
		return result;
	}

	public byte[] GetBytes() => GetBytes(false);

	public byte[] GetBytes(bool bigEndian)
	{
		var result = new byte[16];
		TryWriteBytes(result, bigEndian, out _);
		return result;
	}

	public bool TryWriteBytes(Span<byte> destination) => TryWriteBytes(destination, false, out _);

	public bool TryWriteBytes(Span<byte> destination, bool bigEndian, out int bytesWritten)
	{
		bytesWritten = 0;
		if (destination.Length < 16) return false;
		for (var index = 0; index < 16; index++) destination[index] = GetByte(index, bigEndian);
		bytesWritten = 16;
		return true;
	}

	private byte GetByte(int index, bool bigEndian)
	{
		if (index < 4) return (byte)((uint)_a >> ((bigEndian ? 3 - index : index) * 8));
		if (index < 6) return (byte)((ushort)_b >> ((bigEndian ? 5 - index : index - 4) * 8));
		if (index < 8) return (byte)((ushort)_c >> ((bigEndian ? 7 - index : index - 6) * 8));
		return index switch { 8 => _d, 9 => _e, 10 => _f, 11 => _g, 12 => _h, 13 => _i, 14 => _j, _ => _k };
	}

	public int CompareTo(Guid other)
	{
		var right = Read(other);
		if ((uint)_a != (uint)right._a) return (uint)_a < (uint)right._a ? -1 : 1;
		if ((ushort)_b != (ushort)right._b) return (ushort)_b < (ushort)right._b ? -1 : 1;
		if ((ushort)_c != (ushort)right._c) return (ushort)_c < (ushort)right._c ? -1 : 1;
		for (var index = 8; index < 16; index++)
		{
			var leftByte = GetByte(index, true);
			var rightByte = right.GetByte(index, true);
			if (leftByte != rightByte) return leftByte < rightByte ? -1 : 1;
		}
		return 0;
	}

	public bool Equals(Guid other) => CompareTo(other) == 0;
	public static bool AreEqual(Guid left, Guid right) => Read(left).Equals(right);
	public static bool AreNotEqual(Guid left, Guid right) => !AreEqual(left, right);

	public int GetValueHashCode() => _a ^ (ushort)_b ^ ((ushort)_c << 16) ^
		_d ^ (_e << 8) ^ (_f << 16) ^ (_g << 24) ^ _h ^ (_i << 8) ^ (_j << 16) ^ (_k << 24);

	private static ShadowGuid Read(Guid value) => *(ShadowGuid*)&value;
	private static Guid ToGuid(ShadowGuid value) => *(Guid*)&value;
}
