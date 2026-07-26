/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Output;

internal sealed class BigEndianWriter
{
	private readonly List<byte> _bytes = new();

	public int Length => _bytes.Count;

	public void WriteByte(byte value) => _bytes.Add(value);

	public void WriteUInt32(uint value)
	{
		_bytes.Add((byte)(value >> 24));
		_bytes.Add((byte)(value >> 16));
		_bytes.Add((byte)(value >> 8));
		_bytes.Add((byte)value);
	}

	public void WriteBytes(ReadOnlySpan<byte> bytes)
	{
		foreach (var value in bytes)
		{
			_bytes.Add(value);
		}
	}

	public void PadToLong()
	{
		while ((_bytes.Count & 3) != 0)
		{
			_bytes.Add(0);
		}
	}

	public byte[] ToArray() => _bytes.ToArray();
}
