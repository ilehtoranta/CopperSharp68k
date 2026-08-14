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
}
