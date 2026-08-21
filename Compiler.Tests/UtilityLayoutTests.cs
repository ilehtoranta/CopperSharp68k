using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class UtilityLayoutTests
{
	[Fact]
	public void HookUsesPublishedUtilityOffsets()
	{
		Assert.Equal(20, Unsafe.SizeOf<Hook>());
		Assert.Equal(UtilityLayout.Hook.Size, Unsafe.SizeOf<Hook>());
		Assert.Equal(UtilityLayout.Hook.MinNode,
			Marshal.OffsetOf<Hook>(nameof(Hook.MinNode)).ToInt32());
		Assert.Equal(UtilityLayout.Hook.Entry,
			Marshal.OffsetOf<Hook>(nameof(Hook.Entry)).ToInt32());
		Assert.Equal(UtilityLayout.Hook.SubEntry,
			Marshal.OffsetOf<Hook>(nameof(Hook.SubEntry)).ToInt32());
		Assert.Equal(UtilityLayout.Hook.Data,
			Marshal.OffsetOf<Hook>(nameof(Hook.Data)).ToInt32());
	}

	[Fact]
	public void HookCodecRoundTripsEveryPackedFieldInBigEndianMemory()
	{
		var memory = new Memory(64);
		var address = APTR.FromPointer(8);
		var expected = new Hook
		{
			MinNode = new MinNode
			{
				Successor = APTR.FromPointer(0x1122_3344),
				Predecessor = APTR.FromPointer(0x5566_7788),
			},
			Entry = APTR.FromPointer(0x99AA_BBCC),
			SubEntry = APTR.FromPointer(0xDDEE_F001),
			Data = APTR.FromPointer(0x2345_6789),
		};

		UtilityHookCodec.Write(ref memory, address, expected);

		Assert.True(UtilityHookCodec.IsMapped(ref memory, address));
		var actual = UtilityHookCodec.Read(ref memory, address);
		Assert.Equal(expected.MinNode.Successor, actual.MinNode.Successor);
		Assert.Equal(expected.MinNode.Predecessor, actual.MinNode.Predecessor);
		Assert.Equal(expected.Entry, actual.Entry);
		Assert.Equal(expected.SubEntry, actual.SubEntry);
		Assert.Equal(expected.Data, actual.Data);
		Assert.Equal(0x1122_3344u, memory.ReadUInt32(address,
			UtilityLayout.Hook.MinNodeSuccessor));
	}

	private struct Memory : IAmigaGuestMemory
	{
		private readonly byte[] _bytes;
		internal Memory(int size) => _bytes = new byte[size];
		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[checked((int)address.Raw + offset)];
		public ushort ReadUInt16(APTR address, int offset = 0)
		{
			var index = checked((int)address.Raw + offset);
			return (ushort)((_bytes[index] << 8) | _bytes[index + 1]);
		}
		public uint ReadUInt32(APTR address, int offset = 0)
		{
			var index = checked((int)address.Raw + offset);
			return ((uint)_bytes[index] << 24) | ((uint)_bytes[index + 1] << 16) |
				((uint)_bytes[index + 2] << 8) | _bytes[index + 3];
		}
		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[checked((int)address.Raw + offset)] = value;
		public void WriteUInt16(APTR address, int offset, ushort value)
		{
			var index = checked((int)address.Raw + offset);
			_bytes[index] = (byte)(value >> 8);
			_bytes[index + 1] = (byte)value;
		}
		public void WriteUInt32(APTR address, int offset, uint value)
		{
			var index = checked((int)address.Raw + offset);
			_bytes[index] = (byte)(value >> 24);
			_bytes[index + 1] = (byte)(value >> 16);
			_bytes[index + 2] = (byte)(value >> 8);
			_bytes[index + 3] = (byte)value;
		}
		public void Clear(APTR address, uint byteCount) => Array.Clear(_bytes,
			checked((int)address.Raw), checked((int)byteCount));
		public void Copy(APTR source, APTR destination, uint byteCount) => Array.Copy(
			_bytes, checked((int)source.Raw), _bytes, checked((int)destination.Raw),
			checked((int)byteCount));
		public bool IsMapped(APTR address, uint byteSize) => address.Raw != 0 &&
			address.Raw <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - address.Raw;
	}
}
