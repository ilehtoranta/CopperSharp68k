/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct IFFHandle
{
	private const int StreamOffset = 0;

	public IFFHandle(uint raw)
	{
		Raw = raw;
	}

	public uint Raw { get; }

	public bool IsNull => Raw == 0;

	public bool IsNotNull => Raw != 0;

	public BPTR Stream
	{
		get => BPTR.FromRaw(APTR.ReadUInt32(APTR.FromPointer(Raw), StreamOffset));
	}

	public void SetStream(BPTR stream) =>
		APTR.WriteUInt32(APTR.FromPointer(Raw), StreamOffset, stream.Raw);

	public static implicit operator uint(IFFHandle value) => value.Raw;

	public static explicit operator IFFHandle(uint value) => new(value);
}
