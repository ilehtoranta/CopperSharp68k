using System.Runtime.InteropServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ExecLayoutTests
{
	[Theory]
	[InlineData(typeof(Amiga.List), 14u)]
	[InlineData(typeof(Library), 34u)]
	[InlineData(typeof(Device), 34u)]
	[InlineData(typeof(MsgPort), 34u)]
	[InlineData(typeof(Unit), 38u)]
	[InlineData(typeof(Amiga.Task), 92u)]
	[InlineData(typeof(MorphOSTask), 92u)]
	[InlineData(typeof(StackSwapStruct), 12u)]
	[InlineData(typeof(Interrupt), 22u)]
	[InlineData(typeof(IntVector), 12u)]
	[InlineData(typeof(SoftIntList), 16u)]
	[InlineData(typeof(MemChunk), 8u)]
	[InlineData(typeof(MemHeader), 32u)]
	[InlineData(typeof(MemEntry), 8u)]
	[InlineData(typeof(MemList), 24u)]
	[InlineData(typeof(MemHandlerData), 12u)]
	[InlineData(typeof(FreeBlocksData), 12u)]
	[InlineData(typeof(IORequest), 32u)]
	[InlineData(typeof(IOStdReq), 48u)]
	[InlineData(typeof(Resident), 26u)]
	[InlineData(typeof(SemaphoreMessage), 24u)]
	[InlineData(typeof(Amiga.Semaphore), 36u)]
	[InlineData(typeof(TaskTrapMessage), 40u)]
	[InlineData(typeof(TaskTrapMessage68k), 48u)]
	[InlineData(typeof(TaskInitExtension), 8u)]
	[InlineData(typeof(PPCStackSwapArgs), 32u)]
	[InlineData(typeof(TaskFrame68k), 66u)]
	[InlineData(typeof(TaskStackHistoryEntry), 8u)]
	[InlineData(typeof(ExecNotifyMessage), 16u)]
	[InlineData(typeof(Amiga.ExecBase), 632u)]
	[InlineData(typeof(MorphOSExecBase), 632u)]
	public void StructuresMatchExpectedM68kSizes(Type type, uint expectedSize)
	{
		Assert.Equal(expectedSize, (uint)Marshal.SizeOf(type));
		Assert.Equal(expectedSize, (uint)type.GetField("Size")!.GetValue(null)!);
	}

	[Fact]
	public void ClassicExecFieldsMatchExpectedM68kOffsets()
	{
		AssertOffsets<Amiga.List>(
			(nameof(Amiga.List.Head), 0), (nameof(Amiga.List.Tail), 4),
			(nameof(Amiga.List.TailPred), 8), (nameof(Amiga.List.Type), 12),
			(nameof(Amiga.List.Padding), 13));
		AssertOffsets<Library>(
			(nameof(Library.Node), 0), (nameof(Library.Flags), 14),
			(nameof(Library.Padding), 15), (nameof(Library.NegativeSize), 16),
			(nameof(Library.PositiveSize), 18), (nameof(Library.Version), 20),
			(nameof(Library.Revision), 22), (nameof(Library.IdString), 24),
			(nameof(Library.Checksum), 28), (nameof(Library.OpenCount), 32));
		AssertOffsets<Device>((nameof(Device.Library), 0));
		AssertOffsets<MsgPort>(
			(nameof(MsgPort.Node), 0), (nameof(MsgPort.Flags), 14),
			(nameof(MsgPort.SignalBit), 15), (nameof(MsgPort.SignalTask), 16),
			(nameof(MsgPort.MessageList), 20));
		AssertOffsets<Unit>(
			(nameof(Unit.MessagePort), 0), (nameof(Unit.Flags), 34),
			(nameof(Unit.Padding), 35), (nameof(Unit.OpenCount), 36));
		AssertOffsets<Amiga.Task>(
			(nameof(Amiga.Task.Node), 0), (nameof(Amiga.Task.Flags), 14),
			(nameof(Amiga.Task.State), 15), (nameof(Amiga.Task.IDNestCount), 16),
			(nameof(Amiga.Task.TaskDisableNestCount), 17),
			(nameof(Amiga.Task.SignalAllocated), 18), (nameof(Amiga.Task.SignalWait), 22),
			(nameof(Amiga.Task.SignalReceived), 26), (nameof(Amiga.Task.SignalException), 30),
			(nameof(Amiga.Task.TrapAllocated), 34), (nameof(Amiga.Task.TrapEnabled), 36),
			(nameof(Amiga.Task.ExceptionData), 38), (nameof(Amiga.Task.ExceptionCode), 42),
			(nameof(Amiga.Task.TrapData), 46), (nameof(Amiga.Task.TrapCode), 50),
			(nameof(Amiga.Task.StackPointer), 54), (nameof(Amiga.Task.StackLower), 58),
			(nameof(Amiga.Task.StackUpper), 62), (nameof(Amiga.Task.Switch), 66),
			(nameof(Amiga.Task.Launch), 70), (nameof(Amiga.Task.MemoryEntries), 74),
			(nameof(Amiga.Task.UserData), 88));
		AssertOffsets<MorphOSTask>((nameof(MorphOSTask.ETask), 34),
			(nameof(MorphOSTask.ExceptionData), 38), (nameof(MorphOSTask.UserData), 88));
		AssertOffsets<StackSwapStruct>(
			(nameof(StackSwapStruct.Lower), 0), (nameof(StackSwapStruct.Upper), 4),
			(nameof(StackSwapStruct.Pointer), 8));
		AssertOffsets<Interrupt>(
			(nameof(Interrupt.Node), 0), (nameof(Interrupt.Data), 14),
			(nameof(Interrupt.Code), 18));
		AssertOffsets<IntVector>(
			(nameof(IntVector.Data), 0), (nameof(IntVector.Code), 4),
			(nameof(IntVector.Node), 8));
		AssertOffsets<SoftIntList>(
			(nameof(SoftIntList.List), 0), (nameof(SoftIntList.Padding), 14));
	}

	[Fact]
	public void MemoryIoAndResidentFieldsMatchExpectedM68kOffsets()
	{
		AssertOffsets<MemChunk>((nameof(MemChunk.Next), 0), (nameof(MemChunk.Bytes), 4));
		AssertOffsets<MemHeader>(
			(nameof(MemHeader.Node), 0), (nameof(MemHeader.Attributes), 14),
			(nameof(MemHeader.First), 16), (nameof(MemHeader.Lower), 20),
			(nameof(MemHeader.Upper), 24), (nameof(MemHeader.Free), 28));
		AssertOffsets<MemEntry>(
			(nameof(MemEntry.AddressOrRequirements), 0), (nameof(MemEntry.Length), 4));
		AssertOffsets<MemList>(
			(nameof(MemList.Node), 0), (nameof(MemList.NumberOfEntries), 14),
			(nameof(MemList.FirstEntry), 16));
		AssertOffsets<MemHandlerData>(
			(nameof(MemHandlerData.RequestSize), 0),
			(nameof(MemHandlerData.RequestFlags), 4), (nameof(MemHandlerData.Flags), 8));
		AssertOffsets<FreeBlocksData>(
			(nameof(FreeBlocksData.NumberOfBlocks), 0), (nameof(FreeBlocksData.FirstBlock), 4));
		AssertOffsets<IORequest>(
			(nameof(IORequest.Message), 0), (nameof(IORequest.Device), 20),
			(nameof(IORequest.Unit), 24), (nameof(IORequest.Command), 28),
			(nameof(IORequest.Flags), 30), (nameof(IORequest.Error), 31));
		AssertOffsets<IOStdReq>(
			(nameof(IOStdReq.Message), 0), (nameof(IOStdReq.Device), 20),
			(nameof(IOStdReq.Unit), 24), (nameof(IOStdReq.Command), 28),
			(nameof(IOStdReq.Flags), 30), (nameof(IOStdReq.Error), 31),
			(nameof(IOStdReq.Actual), 32), (nameof(IOStdReq.Length), 36),
			(nameof(IOStdReq.Data), 40), (nameof(IOStdReq.Offset), 44));
		AssertOffsets<Resident>(
			(nameof(Resident.MatchWord), 0), (nameof(Resident.MatchTag), 2),
			(nameof(Resident.EndSkip), 6), (nameof(Resident.Flags), 10),
			(nameof(Resident.Version), 11), (nameof(Resident.Type), 12),
			(nameof(Resident.Priority), 13), (nameof(Resident.Name), 14),
			(nameof(Resident.IdString), 18), (nameof(Resident.Init), 22));
	}

	[Fact]
	public void SemaphoreAndMorphOsFieldsMatchExpectedM68kOffsets()
	{
		AssertOffsets<SemaphoreMessage>(
			(nameof(SemaphoreMessage.Message), 0), (nameof(SemaphoreMessage.Semaphore), 20));
		AssertOffsets<Amiga.Semaphore>(
			(nameof(Amiga.Semaphore.MessagePort), 0), (nameof(Amiga.Semaphore.Bids), 34));
		AssertOffsets<TaskTrapMessage>(
			(nameof(TaskTrapMessage.Message), 0), (nameof(TaskTrapMessage.Task), 20),
			(nameof(TaskTrapMessage.Version), 24), (nameof(TaskTrapMessage.Type), 28),
			(nameof(TaskTrapMessage.DataAddressRegister), 32),
			(nameof(TaskTrapMessage.DataStorageInterruptStatusRegister), 36));
		AssertOffsets<TaskTrapMessage68k>(
			(nameof(TaskTrapMessage68k.Message), 0), (nameof(TaskTrapMessage68k.Task), 20),
			(nameof(TaskTrapMessage68k.Version), 24), (nameof(TaskTrapMessage68k.Type), 28),
			(nameof(TaskTrapMessage68k.StackFrameFormat), 32),
			(nameof(TaskTrapMessage68k.Address), 36), (nameof(TaskTrapMessage68k.FLSW), 40),
			(nameof(TaskTrapMessage68k.EmulHandle), 44));
		AssertOffsets<TaskInitExtension>(
			(nameof(TaskInitExtension.Trap), 0), (nameof(TaskInitExtension.Extension), 2),
			(nameof(TaskInitExtension.Tags), 4));
		AssertOffsets<PPCStackSwapArgs>((nameof(PPCStackSwapArgs.Arguments), 0));
		AssertOffsets<TaskFrame68k>(
			(nameof(TaskFrame68k.ProgramCounter), 0), (nameof(TaskFrame68k.StatusRegister), 4),
			(nameof(TaskFrame68k.Registers), 6));
		AssertOffsets<TaskStackHistoryEntry>(
			(nameof(TaskStackHistoryEntry.Type), 0), (nameof(TaskStackHistoryEntry.Address), 4));
		AssertOffsets<ExecNotifyMessage>(
			(nameof(ExecNotifyMessage.Type), 0), (nameof(ExecNotifyMessage.Flags), 4),
			(nameof(ExecNotifyMessage.Extra), 8), (nameof(ExecNotifyMessage.Extension), 12));
	}

	[Fact]
	public void ExecBaseVariantsMatchExpectedM68kOffsets()
	{
		AssertOffsets<Amiga.ExecBase>(
			(nameof(Amiga.ExecBase.LibNode), 0), (nameof(Amiga.ExecBase.SoftVer), 34),
			(nameof(Amiga.ExecBase.IntVector0), 84), (nameof(Amiga.ExecBase.IntVector15), 264),
			(nameof(Amiga.ExecBase.ThisTask), 276), (nameof(Amiga.ExecBase.MemList), 322),
			(nameof(Amiga.ExecBase.SoftInt0), 434), (nameof(Amiga.ExecBase.SoftInt4), 498),
			(nameof(Amiga.ExecBase.LastAlert), 514), (nameof(Amiga.ExecBase.SemaphoreList), 532),
			(nameof(Amiga.ExecBase.ExPad0), 558), (nameof(Amiga.ExecBase.ExReserved1), 580),
			(nameof(Amiga.ExecBase.ExMmuLock), 600), (nameof(Amiga.ExecBase.ExMemHandlers), 616),
			(nameof(Amiga.ExecBase.ExMemHandler), 628));
		AssertOffsets<MorphOSExecBase>(
			(nameof(MorphOSExecBase.LibNode), 0), (nameof(MorphOSExecBase.IntVector15), 264),
			(nameof(MorphOSExecBase.ThisTask), 276), (nameof(MorphOSExecBase.ExTaskId), 576),
			(nameof(MorphOSExecBase.ExEmulHandleSize), 580),
			(nameof(MorphOSExecBase.ExPpcTrapMsgPort), 584),
			(nameof(MorphOSExecBase.ExReserved1), 588), (nameof(MorphOSExecBase.ExMmuLock), 600),
			(nameof(MorphOSExecBase.ExDebugFlags), 612),
			(nameof(MorphOSExecBase.ExMemHandlers), 616),
			(nameof(MorphOSExecBase.ExMemHandler), 628));
	}

	[Fact]
	public void ClassicAndMorphOsExecFlagValuesRemainUnchanged()
	{
		Assert.Equal(0xFFu, (byte)NodeType.Extended);
		Assert.Equal(0x20u, (byte)LibraryFlags.QueryInfo);
		Assert.Equal(3u, (byte)PortFlags.ActionMask);
		Assert.Equal(0x80u, (byte)TaskFlags.Launch);
		Assert.Equal(0x100u, (uint)SignalFlags.Dos);
		Assert.Equal(0x80u, (byte)ResidentFlags.AutoInit);
		Assert.Equal(2u, (ushort)DeviceCommand.Read);
		Assert.Equal(1u, (uint)SemaphoreMode.Shared);
		Assert.Equal(1u << 11, (uint)Exec.MemoryFlags.Swap);
		Assert.Equal(1u << 12, (uint)Exec.MemoryFlags.ThirtyOneBit);
		Assert.Equal(1u << 20, (uint)Exec.MemoryFlags.SemaphoreProtected);
		Assert.Equal(0x2u, (uint)ExecNotifyFlags.Post);
		Assert.Equal(9u, (uint)ExecListType.RunCommand);
	}

	private static void AssertOffsets<T>(params (string Name, int Offset)[] expected)
	{
		foreach (var (name, offset) in expected)
		{
			Assert.Equal(offset, Marshal.OffsetOf<T>(name).ToInt32());
		}
	}
}
