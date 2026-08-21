using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class DosLayoutTests
{
	[Theory]
	[InlineData(typeof(DateStamp), typeof(DosLayout.DateStamp), 12)]
	[InlineData(typeof(DosDateTime), typeof(DosLayout.DateTime), 26)]
	[InlineData(typeof(InfoData), typeof(DosLayout.InfoData), 36)]
	[InlineData(typeof(DosEnvec), typeof(DosLayout.DosEnvec), 80)]
	[InlineData(typeof(FileSysStartupMsg), typeof(DosLayout.FileSysStartupMsg), 16)]
	[InlineData(typeof(PosixDateStamp), typeof(DosLayout.PosixDateStamp), 12)]
	[InlineData(typeof(CSource), typeof(DosLayout.CSource), 12)]
	[InlineData(typeof(RDArgs), typeof(DosLayout.RDArgs), 32)]
	[InlineData(typeof(RecordLock), typeof(DosLayout.RecordLock), 16)]
	[InlineData(typeof(RecordLock64), typeof(DosLayout.RecordLock64), 24)]
	[InlineData(typeof(FileHandle), typeof(DosLayout.FileHandle), 44)]
	[InlineData(typeof(Segment), typeof(DosLayout.Segment), 16)]
	[InlineData(typeof(DeviceNode), typeof(DosLayout.DeviceNode), 44)]
	[InlineData(typeof(DeviceList), typeof(DosLayout.DeviceList), 44)]
	[InlineData(typeof(DosPacket), typeof(DosLayout.DosPacket), 48)]
	[InlineData(typeof(StandardPacket), typeof(DosLayout.StandardPacket), 68)]
	[InlineData(typeof(FileLock), typeof(DosLayout.FileLock), 20)]
	[InlineData(typeof(PathLock), typeof(DosLayout.PathLock), 8)]
	[InlineData(typeof(ExAllControl), typeof(DosLayout.ExAllControl), 16)]
	[InlineData(typeof(ExAllData), typeof(DosLayout.ExAllData), 40)]
	[InlineData(typeof(DosAttrBuffer), typeof(DosLayout.DosAttrBuffer), 8)]
	[InlineData(typeof(CommandLineInterface), typeof(DosLayout.CommandLineInterface), 64)]
	[InlineData(typeof(Process), typeof(DosLayout.Process), 228)]
	[InlineData(typeof(LocalVar), typeof(DosLayout.LocalVar), 24)]
	[InlineData(typeof(RootNode), typeof(DosLayout.RootNode), 56)]
	[InlineData(typeof(DosInfo), typeof(DosLayout.DosInfo), 158)]
	[InlineData(typeof(DosList), typeof(DosLayout.DosList), 44)]
	[InlineData(typeof(DosListAssignData), typeof(DosLayout.DosListAssignData), 24)]
	[InlineData(typeof(DevProc), typeof(DosLayout.DevProc), 16)]
	[InlineData(typeof(AssignList), typeof(DosLayout.AssignList), 8)]
	[InlineData(typeof(AnchorPath), typeof(DosLayout.AnchorPath), 282)]
	[InlineData(typeof(AChain), typeof(DosLayout.AChain), 274)]
	[InlineData(typeof(NotifyRequestTarget), typeof(DosLayout.NotifyRequestTarget), 8)]
	[InlineData(typeof(NotifyRequest), typeof(DosLayout.NotifyRequest), 48)]
	[InlineData(typeof(NotifyMessage), typeof(DosLayout.NotifyMessage), 38)]
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
	public void NotificationConstantsMatchDosNotifyHeader()
	{
		Assert.Equal(1u, (uint)DosNotifyFlags.SendMessage);
		Assert.Equal(2u, (uint)DosNotifyFlags.SendSignal);
		Assert.Equal(8u, (uint)DosNotifyFlags.WaitReply);
		Assert.Equal(16u, (uint)DosNotifyFlags.NotifyInitial);
		Assert.Equal(0x8000_0000u, (uint)DosNotifyFlags.HandlerMagic);
		Assert.Equal(0x4000_0000u, NotifyMessage.Class);
		Assert.Equal(0x1234, NotifyMessage.Code);
	}

	[Fact]
	public void ProcessAndSystemTagsMatchPublishedDosHeaders()
	{
		Assert.Equal(0x8000_0021u, (uint)DosSystemTag.Input);
		Assert.Equal(0x8000_0026u, (uint)DosSystemTag.FilterTags);
		Assert.Equal(0x8000_03E9u, (uint)DosNewProcessTag.SegmentList);
		Assert.Equal(0x8000_03EBu, (uint)DosNewProcessTag.Entry);
		Assert.Equal(0x8000_03FAu, (uint)DosNewProcessTag.Cli);
		Assert.Equal(0x8000_0400u, (uint)DosNewProcessTag.ExitCode);
		Assert.Equal(0x8000_0404u, (uint)DosNewProcessTag.StartupMessage);
		Assert.Equal(0x8000_0406u, (uint)DosNewProcessTag.TaskFlags);
		Assert.Equal(0x8000_044Cu, (uint)DosNewProcessTag.CodeType);
		Assert.Equal(0x8000_0455u, (uint)DosNewProcessTag.PpcStackSize);
	}

	[Fact]
	public void ChangeModeTargetsMatchDosDosHeader()
	{
		Assert.Equal(0, (int)DosChangeModeTarget.Lock);
		Assert.Equal(1, (int)DosChangeModeTarget.FileHandle);
	}

	[Fact]
	public void DevProcFlagsMatchDosDosHeader()
	{
		Assert.Equal(1u, (uint)DevProcFlags.Unlock);
		Assert.Equal(2u, (uint)DevProcFlags.Assign);
	}

	[Fact]
	public void ExAllLevelsMatchDosExAllHeader()
	{
		Assert.Equal(1, (int)DosExAllDataLevel.Name);
		Assert.Equal(2, (int)DosExAllDataLevel.Type);
		Assert.Equal(3, (int)DosExAllDataLevel.Size);
		Assert.Equal(4, (int)DosExAllDataLevel.Protection);
		Assert.Equal(5, (int)DosExAllDataLevel.Date);
		Assert.Equal(6, (int)DosExAllDataLevel.Comment);
		Assert.Equal(7, (int)DosExAllDataLevel.Owner);
	}

	[Fact]
	public void PatternTokensMatchDosAslHeader()
	{
		Assert.Equal(0x80, (byte)DosPatternToken.Any);
		Assert.Equal(0x81, (byte)DosPatternToken.Single);
		Assert.Equal(0x82, (byte)DosPatternToken.OrStart);
		Assert.Equal(0x83, (byte)DosPatternToken.OrNext);
		Assert.Equal(0x84, (byte)DosPatternToken.OrEnd);
		Assert.Equal(0x85, (byte)DosPatternToken.Not);
		Assert.Equal(0x86, (byte)DosPatternToken.NotEnd);
		Assert.Equal(0x87, (byte)DosPatternToken.NotClass);
		Assert.Equal(0x88, (byte)DosPatternToken.Class);
		Assert.Equal(0x89, (byte)DosPatternToken.RepeatBegin);
		Assert.Equal(0x8A, (byte)DosPatternToken.RepeatEnd);
		Assert.Equal(0x8B, (byte)DosPatternToken.Stop);
	}

	[Fact]
	public void AnchorAndChainFlagsMatchDosAslHeader()
	{
		Assert.Equal(2, (byte)AnchorPathFlags.IsWild);
		Assert.Equal(4, (byte)AnchorPathFlags.DoDirectory);
		Assert.Equal(8, (byte)AnchorPathFlags.DidDirectory);
		Assert.Equal(16, (byte)AnchorPathFlags.NoMemoryError);
		Assert.Equal(64, (byte)AnchorPathFlags.DirectoryChanged);
		Assert.Equal(128, (byte)AnchorPathFlags.FollowHardLinks);
		Assert.Equal(1, (byte)AChainFlags.Pattern);
		Assert.Equal(2, (byte)AChainFlags.Examined);
		Assert.Equal(8, (byte)AChainFlags.All);
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
	public void MorphOsSegmentTagsMatchOfficialDosTagsHeader()
	{
		Assert.Equal(0x8000_0BB9u, (uint)DosAddSegmentTag.Name);
		Assert.Equal(0x8000_0BBCu, (uint)DosAddSegmentTag.Type);
		Assert.Equal(0x8000_0C1Du, (uint)DosFindSegmentTag.Name);
		Assert.Equal(0x8000_0C21u, (uint)DosFindSegmentTag.MatchPattern);
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

		var localVar = new LocalVar
		{
			Node = new Node
			{
				Successor = APTR.FromPointer(0x1111_0000),
				Predecessor = APTR.FromPointer(0x2222_0000),
				Type = (byte)LocalVariableType.Alias,
				Priority = -3,
				Name = STRPTR.FromPointer(0x3333_0000),
			},
			Flags = (ushort)GlobalVariableFlags.BinaryVariable,
			Value = APTR.FromPointer(0x4444_0000),
			Length = 9,
		};
		DosLocalVarCodec.Write(ref memory, address, localVar);
		Assert.Equal(localVar, DosLocalVarCodec.Read(ref memory, address));

		var dateTime = new DosDateTime
		{
			Stamp = new DateStamp { Days = 123, Minutes = 456, Ticks = 789 },
			Format = DosDateFormat.International,
			Flags = DosDateTimeFlags.Substitute,
			Day = STRPTR.FromPointer(0x1111_0000),
			Date = STRPTR.FromPointer(0x2222_0000),
			Time = STRPTR.FromPointer(0x3333_0000),
		};
		DosDateTimeCodec.Write(ref memory, address, dateTime);
		Assert.Equal(dateTime, DosDateTimeCodec.Read(ref memory, address));

		var infoData = new InfoData
		{
			NumberOfSoftErrors = 1, UnitNumber = 2, DiskState = 3,
			NumberOfBlocks = 4, NumberOfBlocksUsed = 5, BytesPerBlock = 512,
			DiskType = unchecked((int)0x444F_5301),
			VolumeNode = BPTR.FromRaw(0x1234_5678), InUse = -1,
		};
		DosInfoDataCodec.Write(ref memory, address, infoData);
		Assert.Equal(infoData, DosInfoDataCodec.Read(ref memory, address));

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
