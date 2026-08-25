using System.Runtime.CompilerServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class GuestMemoryWrapperFoldingFixtures
{
	public struct GuestMemoryAdapter
	{
		private uint _slot;

		public GuestMemoryAdapter(uint slot) => _slot = slot;

		public byte ReadUInt8(APTR address, int offset)
		{
			_ = _slot;
			return APTR.ReadUInt8(address, offset);
		}

		public ushort ReadUInt16(APTR address, int offset) =>
			APTR.ReadUInt16(address, offset);

		public uint ReadUInt32(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset);

		public void WriteUInt8(APTR address, int offset, byte value) =>
			APTR.WriteUInt8(address, offset, value);

		public void WriteUInt16(APTR address, int offset, ushort value) =>
			APTR.WriteUInt16(address, offset, value);

		public void WriteUInt32(APTR address, int offset, uint value) =>
			APTR.WriteUInt32(address, offset, value);

		public uint ReadAdjusted(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset + 4);

		public uint ReadTwice(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset) ^ APTR.ReadUInt32(address, offset + 4);

		public uint ReadAndTouchReceiver(APTR address, int offset)
		{
			_slot++;
			return APTR.ReadUInt32(address, offset);
		}

		public uint ReadAfterMerge(APTR address, int offset, bool advance)
		{
			var actualOffset = offset;
			if (advance)
			{
				actualOffset += 4;
			}
			return APTR.ReadUInt32(address, actualOffset);
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public uint NoInlineRead(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset);
	}

	private interface IConstrainedGuestMemory
	{
		uint ReadUInt32(APTR address, int offset);
		void WriteUInt32(APTR address, int offset, uint value);
	}

	private struct ConstrainedGuestMemoryAdapter : IConstrainedGuestMemory
	{
		public uint Slot;

		public uint ReadUInt32(APTR address, int offset)
		{
			_ = Slot;
			return APTR.ReadUInt32(address, offset);
		}

		public void WriteUInt32(APTR address, int offset, uint value) =>
			APTR.WriteUInt32(address, offset, value);
	}

	public sealed class ReferenceGuestMemoryAdapter
	{
		public uint ReadUInt32(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint EligibleEntry()
	{
		var memory = new GuestMemoryAdapter(0);
		var address = APTR.FromPointer(0x0000_3000);
		memory.WriteUInt8(address, 0, 0xA5);
		memory.WriteUInt16(address, 2, 0x1234);
		memory.WriteUInt32(address, 4, 0x89AB_CDEF);
		return ((uint)memory.ReadUInt8(address, 0) << 24) ^
			((uint)memory.ReadUInt16(address, 2) << 8) ^
			memory.ReadUInt32(address, 4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint DirectEntry()
	{
		var address = APTR.FromPointer(0x0000_3000);
		APTR.WriteUInt8(address, 0, 0xA5);
		APTR.WriteUInt16(address, 2, 0x1234);
		APTR.WriteUInt32(address, 4, 0x89AB_CDEF);
		return ((uint)APTR.ReadUInt8(address, 0) << 24) ^
			((uint)APTR.ReadUInt16(address, 2) << 8) ^
			APTR.ReadUInt32(address, 4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReadUInt32Caller(
		ref GuestMemoryAdapter memory,
		APTR address,
		int offset) => memory.ReadUInt32(address, offset);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static byte ReadUInt8Caller(
		ref GuestMemoryAdapter memory,
		APTR address,
		int offset) => memory.ReadUInt8(address, offset);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static void WriteUInt8Caller(
		ref GuestMemoryAdapter memory,
		APTR address,
		int offset,
		byte value) => memory.WriteUInt8(address, offset, value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReferenceCaller(
		ReferenceGuestMemoryAdapter memory,
		APTR address,
		int offset) => memory.ReadUInt32(address, offset);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConstrainedEntry()
	{
		var memory = new ConstrainedGuestMemoryAdapter { Slot = 0 };
		return ConstrainedPassThrough(
			ref memory,
			APTR.FromPointer(0x0000_3000),
			12,
			0x1357_9BDF);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ConstrainedPassThrough<T>(
		ref T memory,
		APTR address,
		int offset,
		uint value)
		where T : struct, IConstrainedGuestMemory
	{
		memory.WriteUInt32(address, offset, value);
		return memory.ReadUInt32(address, offset);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NegativeEntry()
	{
		var memory = new GuestMemoryAdapter(0);
		var address = APTR.FromPointer(0x0000_3000);
		APTR.WriteUInt32(address, 0, 0x1122_3344);
		APTR.WriteUInt32(address, 4, 0x5566_7788);
		return memory.ReadAdjusted(address, 0) ^
			memory.ReadTwice(address, 0) ^
			memory.ReadAndTouchReceiver(address, 0) ^
			memory.ReadAfterMerge(address, 0, true) ^
			memory.NoInlineRead(address, 0);
	}
}
