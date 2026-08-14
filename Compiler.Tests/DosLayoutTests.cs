using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class DosLayoutTests
{
	[Theory]
	[InlineData(typeof(DateStamp), typeof(DosLayout.DateStamp), 12)]
	[InlineData(typeof(PosixDateStamp), typeof(DosLayout.PosixDateStamp), 12)]
	[InlineData(typeof(CSource), typeof(DosLayout.CSource), 12)]
	[InlineData(typeof(RDArgs), typeof(DosLayout.RDArgs), 32)]
	[InlineData(typeof(RecordLock), typeof(DosLayout.RecordLock), 16)]
	[InlineData(typeof(RecordLock64), typeof(DosLayout.RecordLock64), 24)]
	[InlineData(typeof(FileHandle), typeof(DosLayout.FileHandle), 44)]
	[InlineData(typeof(DosPacket), typeof(DosLayout.DosPacket), 48)]
	[InlineData(typeof(StandardPacket), typeof(DosLayout.StandardPacket), 68)]
	[InlineData(typeof(FileLock), typeof(DosLayout.FileLock), 20)]
	[InlineData(typeof(ExAllControl), typeof(DosLayout.ExAllControl), 16)]
	[InlineData(typeof(DosAttrBuffer), typeof(DosLayout.DosAttrBuffer), 8)]
	[InlineData(typeof(CommandLineInterface), typeof(DosLayout.CommandLineInterface), 64)]
	[InlineData(typeof(Process), typeof(DosLayout.Process), 228)]
	[InlineData(typeof(RootNode), typeof(DosLayout.RootNode), 56)]
	[InlineData(typeof(DosInfo), typeof(DosLayout.DosInfo), 158)]
	[InlineData(typeof(DosList), typeof(DosLayout.DosList), 44)]
	[InlineData(typeof(DevProc), typeof(DosLayout.DevProc), 16)]
	[InlineData(typeof(AssignList), typeof(DosLayout.AssignList), 8)]
	public void LayoutConstantsCoverEveryPublishedField(Type structure, Type layout,
		int expectedSize)
	{
		Assert.Equal(expectedSize, Marshal.SizeOf(structure));
		Assert.Equal(expectedSize, (int)layout.GetField("Size")!.GetRawConstantValue()!);
		foreach (var field in structure.GetFields(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Instance))
		{
			var offset = layout.GetField(field.Name);
			Assert.NotNull(offset);
			Assert.True(offset!.IsLiteral);
			Assert.Equal(Marshal.OffsetOf(structure, field.Name).ToInt32(),
				(int)offset.GetRawConstantValue()!);
		}
	}

	[Fact]
	public void StandardPacketEmbedsMessageThenPacketWithoutHostPadding()
	{
		Assert.Equal(0, DosLayout.StandardPacket.Message);
		Assert.Equal(DosLayout.StandardPacket.Packet, Marshal.SizeOf<Message>());
		Assert.Equal(DosLayout.StandardPacket.Packet + DosLayout.DosPacket.Size,
			DosLayout.StandardPacket.Size);
	}

	[Fact]
	public void RecordLockModesMatchDosRecordHeader()
	{
		Assert.Equal(0u, (uint)DosRecordMode.Exclusive);
		Assert.Equal(1u, (uint)DosRecordMode.ExclusiveImmediate);
		Assert.Equal(2u, (uint)DosRecordMode.Shared);
		Assert.Equal(3u, (uint)DosRecordMode.SharedImmediate);
	}

	[Fact]
	public void DevProcFlagsMatchDosDosHeader()
	{
		Assert.Equal(1u, (uint)DevProcFlags.Unlock);
		Assert.Equal(2u, (uint)DevProcFlags.Assign);
	}

	[Fact]
	public void MorphOsFileInfoExtensionsOverlayTheClassicReservedTail()
	{
		Assert.Equal(260, FileInfoBlock.SizeInBytes);
		Assert.Equal(228, FileInfoBlock.Size64Offset);
		Assert.Equal(236, FileInfoBlock.NumBlocks64Offset);
		Assert.Equal(258, FileInfoBlock.ActualExtensionFlagsOffset);
		Assert.Equal(259, FileInfoBlock.RequestedExtensionFlagsOffset);
		Assert.Equal(0x8000_0E11u, (uint)DosExamine64Tag.PosixDate);
		Assert.Equal(1, (byte)FileInfoExtensionFlags.PosixDate);
	}

	[Fact]
	public void DosObjectTypesAndClassicTagsMatchDosHeaders()
	{
		Assert.Equal(0u, (uint)DosObjectType.FileHandle);
		Assert.Equal(5u, (uint)DosObjectType.RdArgs);
		Assert.Equal(9u, (uint)DosObjectType.AssignNode);
		Assert.Equal(0x8000_07D1u, (uint)DosObjectTag.FileHandleMode);
		Assert.Equal(0x8000_07D5u, (uint)DosObjectTag.PromptLength);
	}

	[Fact]
	public void PublicDosCodecsRoundTripNamedStructFieldsInBigEndianMemory()
	{
		var memory = new Memory(2048);
		var address = APTR.FromPointer(0x100);
		var recordLock = new RecordLock
		{
			File = BPTR.FromRaw(0x1020_3040), Offset = 0x5060_7080,
			Length = 0x90A0_B0C0, Mode = 3,
		};
		DosRecordLockCodec.Write(ref memory, address, recordLock);
		Assert.Equal(recordLock, DosRecordLockCodec.Read(ref memory, address));

		var rdArgs = new RDArgs
		{
			Source = new CSource
			{
				Buffer = APTR.FromPointer(0x1111_2222), Length = 31,
				CurrentCharacter = 7,
			},
			AllocationList = APTR.FromPointer(0x3333_4444),
			Buffer = APTR.FromPointer(0x5555_6666), BufferSize = 255,
			ExtendedHelp = APTR.FromPointer(0x7777_8888), Flags = -2,
		};
		DosRdArgsCodec.Write(ref memory, address, rdArgs);
		Assert.Equal(rdArgs, DosRdArgsCodec.Read(ref memory, address));

		var assign = new AssignList
		{
			Next = APTR.FromPointer(0x1234_5678),
			Lock = BPTR.FromRaw(0x1020_3040),
		};
		DosAssignListCodec.Write(ref memory, address, assign);
		Assert.Equal(assign, DosAssignListCodec.Read(ref memory, address));

		var devProc = new DevProc
		{
			Port = APTR.FromPointer(0x1111_0000), Lock = BPTR.FromRaw(0x2222_0000),
			Flags = 3, DeviceNode = APTR.FromPointer(0x3333_0000),
		};
		DosDevProcCodec.Write(ref memory, address, devProc);
		Assert.Equal(devProc, DosDevProcCodec.Read(ref memory, address));

		DosFileInfoBlockCodec.WriteDiskKey(ref memory, address, 17);
		DosFileInfoBlockCodec.WriteDirEntryType(ref memory, address, -3);
		DosFileInfoBlockCodec.WriteSize(ref memory, address, 123456);
		Assert.Equal(17, DosFileInfoBlockCodec.ReadDiskKey(ref memory, address));
		Assert.Equal(123456, DosFileInfoBlockCodec.ReadSize(ref memory, address));
	}

	private struct Memory : IAmigaGuestMemory
	{
		private readonly byte[] _bytes;
		internal Memory(int size) => _bytes = new byte[size];
		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[Index(address, offset, 1)];
		public ushort ReadUInt16(APTR address, int offset = 0)
		{
			var index = Index(address, offset, 2);
			return (ushort)((_bytes[index] << 8) | _bytes[index + 1]);
		}
		public uint ReadUInt32(APTR address, int offset = 0)
		{
			var index = Index(address, offset, 4);
			return ((uint)_bytes[index] << 24) | ((uint)_bytes[index + 1] << 16) |
				((uint)_bytes[index + 2] << 8) | _bytes[index + 3];
		}
		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[Index(address, offset, 1)] = value;
		public void WriteUInt16(APTR address, int offset, ushort value)
		{
			var index = Index(address, offset, 2);
			_bytes[index] = (byte)(value >> 8);
			_bytes[index + 1] = (byte)value;
		}
		public void WriteUInt32(APTR address, int offset, uint value)
		{
			var index = Index(address, offset, 4);
			_bytes[index] = (byte)(value >> 24);
			_bytes[index + 1] = (byte)(value >> 16);
			_bytes[index + 2] = (byte)(value >> 8);
			_bytes[index + 3] = (byte)value;
		}
		public void Clear(APTR address, uint byteCount) => Array.Clear(_bytes,
			Index(address, 0, checked((int)byteCount)), checked((int)byteCount));
		public void Copy(APTR source, APTR destination, uint byteCount) => Array.Copy(
			_bytes, Index(source, 0, checked((int)byteCount)), _bytes,
			Index(destination, 0, checked((int)byteCount)), checked((int)byteCount));
		public bool IsMapped(APTR address, uint byteSize) => address.Raw != 0 &&
			address.Raw <= (uint)_bytes.Length && byteSize <= (uint)_bytes.Length -
			address.Raw;
		private int Index(APTR address, int offset, int size) => checked(
			(int)address.Raw + offset + (size > _bytes.Length - (int)address.Raw -
			offset ? throw new ArgumentOutOfRangeException() : 0));
	}
}
