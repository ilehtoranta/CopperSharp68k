/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class BoopsiAbiTests
{
	[Fact]
	public void MethodIdentifiersMatchThePublicBoopsiAbi()
	{
		Assert.Equal(0x100u, BOOPSI.OM_Dummy);
		Assert.Equal(0x101u, BOOPSI.OM_NEW);
		Assert.Equal(0x102u, BOOPSI.OM_DISPOSE);
		Assert.Equal(0x103u, BOOPSI.OM_SET);
		Assert.Equal(0x104u, BOOPSI.OM_GET);
		Assert.Equal(0x105u, BOOPSI.OM_ADDTAIL);
		Assert.Equal(0x106u, BOOPSI.OM_REMOVE);
		Assert.Equal(0x107u, BOOPSI.OM_NOTIFY);
		Assert.Equal(0x108u, BOOPSI.OM_UPDATE);
		Assert.Equal(0x109u, BOOPSI.OM_ADDMEMBER);
		Assert.Equal(0x10Au, BOOPSI.OM_REMMEMBER);
		Assert.Equal(1u, BOOPSI.OPUF_INTERIM);
	}

	[Fact]
	public void ClassAndObjectLayoutsMatchTheM68kAbi()
	{
		Assert.Equal(52, Marshal.SizeOf<IClass>());
		Assert.Equal(12, Marshal.SizeOf<_Object>());
		Assert.Equal(32, Marshal.OffsetOf<IClass>(nameof(IClass.cl_InstOffset)).ToInt32());
		Assert.Equal(34, Marshal.OffsetOf<IClass>(nameof(IClass.cl_InstSize)).ToInt32());
		Assert.Equal(40, Marshal.OffsetOf<IClass>(nameof(IClass.cl_SubclassCount)).ToInt32());
		Assert.Equal(44, Marshal.OffsetOf<IClass>(nameof(IClass.cl_ObjectCount)).ToInt32());
		Assert.Equal(8, Marshal.OffsetOf<_Object>(nameof(_Object.o_Class)).ToInt32());
		Assert.Equal(BOOPSILayout.Class.InstanceOffset,
			Marshal.OffsetOf<IClass>(nameof(IClass.cl_InstOffset)).ToInt32());
	}

	[Fact]
	public void CoreMessagesHaveExactPackedLayouts()
	{
		Assert.Equal(12, Marshal.SizeOf<opSet>());
		Assert.Equal(16, Marshal.SizeOf<opUpdate>());
		Assert.Equal(12, Marshal.SizeOf<opGet>());
		Assert.Equal(8, Marshal.SizeOf<opAddTail>());
		Assert.Equal(8, Marshal.SizeOf<opMember>());
		Assert.Equal(4, Marshal.OffsetOf<opSet>(nameof(opSet.ops_AttrList)).ToInt32());
		Assert.Equal(8, Marshal.OffsetOf<opGet>(nameof(opGet.opg_Storage)).ToInt32());
		Assert.Equal(12, Marshal.OffsetOf<opUpdate>(nameof(opUpdate.opu_Flags)).ToInt32());
	}

	[Fact]
	public void ClassCodecUsesGuardedBigEndianGuestMemory()
	{
		var memory = new TestMemory(0x1000, 128);
		var address = APTR.FromPointer(0x1010);
		var value = new IClass
		{
			cl_Dispatcher = new Hook
			{
				MinNode = new MinNode
				{
					Successor = APTR.FromPointer(0x1111_2222),
					Predecessor = APTR.FromPointer(0x3333_4444),
				},
				Entry = APTR.FromPointer(0x5555_6666),
				SubEntry = APTR.FromPointer(0x7777_8888),
				Data = APTR.FromPointer(0x9999_AAAA),
			},
			cl_Super = APTR.FromPointer(0x0102_0304),
			cl_ID = APTR.FromPointer(0x0506_0708),
			cl_InstOffset = 0x1234,
			cl_InstSize = 0x5678,
			cl_UserData = 0x90AB_CDEF,
			cl_SubclassCount = 2,
			cl_ObjectCount = 3,
			cl_Flags = 1,
		};

		BOOPSIGuestCodec.WriteClass(ref memory, address, value);
		var actual = BOOPSIGuestCodec.ReadClass(ref memory, address);

		Assert.Equal(value.cl_Dispatcher.Entry, actual.cl_Dispatcher.Entry);
		Assert.Equal(value.cl_Super, actual.cl_Super);
		Assert.Equal(value.cl_ID, actual.cl_ID);
		Assert.Equal(value.cl_InstOffset, actual.cl_InstOffset);
		Assert.Equal(value.cl_InstSize, actual.cl_InstSize);
		Assert.Equal(value.cl_UserData, actual.cl_UserData);
		Assert.Equal(value.cl_SubclassCount, actual.cl_SubclassCount);
		Assert.Equal(value.cl_ObjectCount, actual.cl_ObjectCount);
		Assert.Equal(value.cl_Flags, actual.cl_Flags);
		Assert.Equal(0x12, memory.ReadUInt8(address,
			BOOPSILayout.Class.InstanceOffset));
		Assert.Equal(0x34, memory.ReadUInt8(address,
			BOOPSILayout.Class.InstanceOffset + 1));
	}

	private struct TestMemory : IAmigaGuestMemory
	{
		private readonly uint _baseAddress;
		private readonly byte[] _bytes;

		public TestMemory(uint baseAddress, int size)
		{
			_baseAddress = baseAddress;
			_bytes = new byte[size];
		}

		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[Index(address, offset, 1)];

		public ushort ReadUInt16(APTR address, int offset = 0) =>
			BinaryPrimitives.ReadUInt16BigEndian(
				_bytes.AsSpan(Index(address, offset, 2), 2));

		public uint ReadUInt32(APTR address, int offset = 0) =>
			BinaryPrimitives.ReadUInt32BigEndian(
				_bytes.AsSpan(Index(address, offset, 4), 4));

		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[Index(address, offset, 1)] = value;

		public void WriteUInt16(APTR address, int offset, ushort value) =>
			BinaryPrimitives.WriteUInt16BigEndian(
				_bytes.AsSpan(Index(address, offset, 2), 2), value);

		public void WriteUInt32(APTR address, int offset, uint value) =>
			BinaryPrimitives.WriteUInt32BigEndian(
				_bytes.AsSpan(Index(address, offset, 4), 4), value);

		public void Clear(APTR address, uint byteCount) =>
			_bytes.AsSpan(Index(address, 0, checked((int)byteCount)),
				checked((int)byteCount)).Clear();

		public void Copy(APTR source, APTR destination, uint byteCount) =>
			_bytes.AsSpan(Index(source, 0, checked((int)byteCount)),
				checked((int)byteCount)).CopyTo(
					_bytes.AsSpan(Index(destination, 0, checked((int)byteCount)),
						checked((int)byteCount)));

		public bool IsMapped(APTR address, uint byteSize) =>
			address.Raw >= _baseAddress &&
			address.Raw - _baseAddress <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - (address.Raw - _baseAddress);

		private int Index(APTR address, int offset, int size)
		{
			var raw = checked(address.Raw + (uint)offset);
			if (raw < _baseAddress || raw - _baseAddress > (uint)_bytes.Length ||
				(uint)size > (uint)_bytes.Length - (raw - _baseAddress))
				throw new ArgumentOutOfRangeException(nameof(address));
			return checked((int)(raw - _baseAddress));
		}
	}
}
