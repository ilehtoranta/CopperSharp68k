/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public readonly struct APTR
{
	private readonly uint _pointer;

	public APTR(uint pointer)
	{
		_pointer = pointer;
	}

	public static APTR Null => new(0);

	public uint Raw => _pointer;

	public bool IsNull => _pointer == 0;

	public bool IsNotNull => _pointer != 0;

	public static APTR FromPointer(uint pointer) => new(pointer);

	public static APTR ExportAddress(string exportName) =>
		throw new System.NotSupportedException(
			"APTR.ExportAddress is lowered by CopperSharp.");

	/// <summary>
	/// Atomically sets a bit in a native RAM byte and returns its previous value.
	/// The bit index wraps modulo eight. The read/modify/write is one guest BSET
	/// instruction, indivisible with respect to CPU interrupt/preemption boundaries.
	/// This is not a host interlocked operation or a guarantee against DMA writes.
	/// </summary>
	public static bool BitTestAndSet8(APTR address, int bit) =>
		throw new System.NotSupportedException(
			"APTR.BitTestAndSet8 is lowered by CopperSharp.");

	/// <summary>Atomically increments a native RAM word modulo 65536 and returns
	/// whether the result is zero. One guest ADDQ.W, indivisible at CPU interrupt
	/// boundaries; not synchronization with DMA writes. The address must be even.</summary>
	public static bool IncrementWordIsZero(APTR address, int offset) =>
		throw new System.NotSupportedException("APTR.IncrementWordIsZero is lowered by CopperSharp.");

	/// <summary>Atomically decrements a native RAM word modulo 65536 and returns
	/// whether the wrapped result is negative as a signed word. One guest SUBQ.W;
	/// the same RAM/alignment/interrupt-boundary contract as IncrementWordIsZero.</summary>
	public static bool DecrementWordIsNegative(APTR address, int offset) =>
		throw new System.NotSupportedException("APTR.DecrementWordIsNegative is lowered by CopperSharp.");

	public static byte ReadUInt8(APTR address, int offset) =>
		throw new System.NotSupportedException(
			"APTR.ReadUInt8 is lowered by CopperSharp.");

	public static ushort ReadUInt16(APTR address, int offset) =>
		throw new System.NotSupportedException(
			"APTR.ReadUInt16 is lowered by CopperSharp.");

	public static uint ReadUInt32(APTR address, int offset) =>
		throw new System.NotSupportedException(
			"APTR.ReadUInt32 is lowered by CopperSharp.");

	public static void WriteUInt8(APTR address, int offset, byte value) =>
		throw new System.NotSupportedException(
			"APTR.WriteUInt8 is lowered by CopperSharp.");

	public static void WriteUInt16(APTR address, int offset, ushort value) =>
		throw new System.NotSupportedException(
			"APTR.WriteUInt16 is lowered by CopperSharp.");

	public static void WriteUInt32(APTR address, int offset, uint value) =>
		throw new System.NotSupportedException(
			"APTR.WriteUInt32 is lowered by CopperSharp.");

	public static uint ToUInt32(APTR pointer) => pointer._pointer;

	public static implicit operator APTR(uint pointer) =>
		FromPointer(pointer);

	public static implicit operator uint(APTR pointer) =>
		ToUInt32(pointer);
}
