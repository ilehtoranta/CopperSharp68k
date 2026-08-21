using System.Reflection;
using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Compiler;

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
	[InlineData(typeof(ResidentAutoInit), 16u)]
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
	[InlineData(typeof(TagItem), 8u)]
	[InlineData(typeof(ClockData), 14u)]
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
		AssertOffsets<ResidentAutoInit>(
			(nameof(ResidentAutoInit.DataSize), 0),
			(nameof(ResidentAutoInit.FunctionTable), 4),
			(nameof(ResidentAutoInit.StructureTable), 8),
			(nameof(ResidentAutoInit.InitFunction), 12));
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
		Assert.Equal(-1, (int)MemoryHandlerResult.AllDone);
		Assert.Equal(0, (int)MemoryHandlerResult.DidNothing);
		Assert.Equal(1, (int)MemoryHandlerResult.TryAgain);
	}

	[Fact]
	public void PublicMemoryLayoutConstantsMatchRuntimeLayout()
	{
		Assert.Equal(ExecLayout.Node.Successor, Marshal.OffsetOf<Node>(nameof(Node.Successor)).ToInt32());
		Assert.Equal(ExecLayout.Node.Name, Marshal.OffsetOf<Node>(nameof(Node.Name)).ToInt32());
		Assert.Equal(ExecLayout.MemChunk.Bytes, Marshal.OffsetOf<MemChunk>(nameof(MemChunk.Bytes)).ToInt32());
		Assert.Equal(ExecLayout.MemHeader.Attributes, Marshal.OffsetOf<MemHeader>(nameof(MemHeader.Attributes)).ToInt32());
		Assert.Equal(ExecLayout.MemHeader.First, Marshal.OffsetOf<MemHeader>(nameof(MemHeader.First)).ToInt32());
		Assert.Equal(ExecLayout.MemHeader.Free, Marshal.OffsetOf<MemHeader>(nameof(MemHeader.Free)).ToInt32());
		Assert.Equal(ExecLayout.MemList.NumberOfEntries, Marshal.OffsetOf<MemList>(nameof(MemList.NumberOfEntries)).ToInt32());
		Assert.Equal(ExecLayout.MemHandlerData.Flags, Marshal.OffsetOf<MemHandlerData>(nameof(MemHandlerData.Flags)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.ThisTask, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.ThisTask)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.ResModules, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.ResModules)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.MemList, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.MemList)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.KickMemPtr, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.KickMemPtr)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.KickTagPtr, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.KickTagPtr)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.KickCheckSum, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.KickCheckSum)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.ExReserved2, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.ExReserved2)).ToInt32());
		Assert.Equal(ExecLayout.ExecBase.ExMemHandlers, Marshal.OffsetOf<Amiga.ExecBase>(nameof(Amiga.ExecBase.ExMemHandlers)).ToInt32());
	}

	[Theory]
	[InlineData(typeof(Node), typeof(ExecLayout.Node))]
	[InlineData(typeof(Amiga.List), typeof(ExecLayout.List))]
	[InlineData(typeof(Library), typeof(ExecLayout.Library))]
	[InlineData(typeof(Device), typeof(ExecLayout.Device))]
	[InlineData(typeof(Message), typeof(ExecLayout.Message))]
	[InlineData(typeof(MsgPort), typeof(ExecLayout.MsgPort))]
	[InlineData(typeof(Unit), typeof(ExecLayout.Unit))]
	[InlineData(typeof(Interrupt), typeof(ExecLayout.Interrupt))]
	[InlineData(typeof(IntVector), typeof(ExecLayout.IntVector))]
	[InlineData(typeof(SoftIntList), typeof(ExecLayout.SoftIntList))]
	[InlineData(typeof(MemChunk), typeof(ExecLayout.MemChunk))]
	[InlineData(typeof(MemHeader), typeof(ExecLayout.MemHeader))]
	[InlineData(typeof(MemEntry), typeof(ExecLayout.MemEntry))]
	[InlineData(typeof(MemList), typeof(ExecLayout.MemList))]
	[InlineData(typeof(MemHandlerData), typeof(ExecLayout.MemHandlerData))]
	[InlineData(typeof(Amiga.Task), typeof(ExecLayout.Task))]
	[InlineData(typeof(StackSwapStruct), typeof(ExecLayout.StackSwapStruct))]
	[InlineData(typeof(IORequest), typeof(ExecLayout.IORequest))]
	[InlineData(typeof(IOStdReq), typeof(ExecLayout.IOStdReq))]
	[InlineData(typeof(Resident), typeof(ExecLayout.Resident))]
	[InlineData(typeof(ResidentAutoInit), typeof(ExecLayout.ResidentAutoInit))]
	[InlineData(typeof(MinList), typeof(ExecLayout.MinList))]
	[InlineData(typeof(SemaphoreRequest), typeof(ExecLayout.SemaphoreRequest))]
	[InlineData(typeof(SignalSemaphore), typeof(ExecLayout.SignalSemaphore))]
	[InlineData(typeof(SemaphoreMessage), typeof(ExecLayout.SemaphoreMessage))]
	[InlineData(typeof(Amiga.Semaphore), typeof(ExecLayout.Semaphore))]
	[InlineData(typeof(Amiga.ExecBase), typeof(ExecLayout.ExecBase))]
	public void PublicLayoutConstantsMatchRuntimeLayout(Type structureType, Type layoutType)
	{
		foreach (var field in layoutType.GetFields(
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static))
		{
			Assert.True(field.IsLiteral, $"{layoutType.Name}.{field.Name} must be compile-time constant");
			var expected = (int)field.GetRawConstantValue()!;
			var actual = Marshal.OffsetOf(structureType, field.Name).ToInt32();
			Assert.Equal(expected, actual);
		}
	}

	[Fact]
	public void PublicExecLvoConstantsMatchEveryExecDeclaration()
	{
		var methods = typeof(Exec).GetMethods(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Static |
			System.Reflection.BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttributes(false)
				.OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>().Any())
			.ToArray();
		Assert.Equal(155, methods.Length);

		foreach (var method in methods)
		{
			var field = typeof(ExecLvo).GetField(method.Name,
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
			Assert.NotNull(field);
			Assert.True(field!.IsLiteral, $"ExecLvo.{method.Name} must be compile-time constant");
			AssertLvo(method.Name, (short)field.GetRawConstantValue()!);
		}
	}

	[Theory]
	[InlineData(nameof(DOS.Open), DosLvo.Open)]
	[InlineData(nameof(DOS.Close), DosLvo.Close)]
	[InlineData(nameof(DOS.Read), DosLvo.Read)]
	[InlineData(nameof(DOS.Write), DosLvo.Write)]
	[InlineData(nameof(DOS.Lock), DosLvo.Lock)]
	[InlineData(nameof(DOS.Examine), DosLvo.Examine)]
	[InlineData(nameof(DOS.CurrentDir), DosLvo.CurrentDir)]
	[InlineData(nameof(DOS.IoErr), DosLvo.IoErr)]
	[InlineData(nameof(DOS.ReadArgs), DosLvo.ReadArgs)]
	[InlineData(nameof(DOS.Seek64), DosLvo.Seek64)]
	public void PublicDosLvoConstantsMatchPortableAndMorphOsDeclarations(string methodName, short expected)
	{
		var field = typeof(DosLvo).GetField(methodName,
			System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
		Assert.NotNull(field); Assert.True(field!.IsLiteral);
		Assert.Equal(expected, (short)field.GetRawConstantValue()!);
		AssertLvo(methodName, expected, typeof(DOS));
	}

	[Fact]
	public void PublicDosLvoConstantsCoverEveryClassicAndMorphOsDeclaration()
	{
		var methods = typeof(DOS).GetMethods(BindingFlags.Public | BindingFlags.Static |
			BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttribute<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>() is not null)
			.ToArray();

		Assert.Equal(194, methods.Length);
		foreach (var method in methods)
		{
			var field = typeof(DosLvo).GetField(method.Name,
				BindingFlags.Public | BindingFlags.Static);
			Assert.NotNull(field);
			Assert.True(field!.IsLiteral, $"DosLvo.{method.Name} must be a compile-time constant");
			Assert.Equal(method.GetCustomAttribute<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>()!.Offset,
				(short)field.GetRawConstantValue()!);
		}
	}

	[Fact]
	public void SetVBufUsesPublishedModesAndM68kRegisters()
	{
		Assert.Equal(0, (int)DosBufferMode.Line);
		Assert.Equal(1, (int)DosBufferMode.Full);
		Assert.Equal(2, (int)DosBufferMode.None);
		var method = typeof(DOS).GetMethod(nameof(DOS.SetVBuf),
			BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(method);
		Assert.Equal(new[] { M68kRegister.D1, M68kRegister.D2,
			M68kRegister.D3, M68kRegister.D4 }, method!.GetParameters()
			.Select(parameter => parameter.GetCustomAttribute<M68kRegisterAttribute>()!
				.Register).ToArray());
		Assert.Equal(M68kRegister.D0,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Fact]
	public void InternalSegmentCallsUsePublishedMorphOsM68kRegisters()
	{
		AssertRegisters(typeof(DOS), nameof(DOS.InternalLoadSeg),
			M68kRegister.D0, M68kRegister.A0, M68kRegister.A1, M68kRegister.A2);
		AssertRegisters(typeof(DOS), nameof(DOS.InternalUnLoadSeg),
			M68kRegister.D1, M68kRegister.A1);
		AssertRegisters(typeof(DosInternalSegmentCallbacks),
			nameof(DosInternalSegmentCallbacks.Read), M68kRegister.A3,
			M68kRegister.D1, M68kRegister.A0, M68kRegister.D0, M68kRegister.A6);
		AssertRegisters(typeof(DosInternalSegmentCallbacks),
			nameof(DosInternalSegmentCallbacks.Allocate), M68kRegister.A3,
			M68kRegister.D0, M68kRegister.D1, M68kRegister.A6);
		AssertRegisters(typeof(DosInternalSegmentCallbacks),
			nameof(DosInternalSegmentCallbacks.Free), M68kRegister.A3,
			M68kRegister.A1, M68kRegister.D0, M68kRegister.A6);

		foreach (var method in typeof(DosInternalSegmentCallbacks).GetMethods(
			BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly))
		{
			Assert.Equal(M68kRegister.A3,
				method.GetCustomAttribute<
					CopperSharp.Sdk.Amiga.AmigaIndirectCallAttribute>()?.TargetRegister);
		}
	}

	[Fact]
	public void RunCommandCallbackUsesPublishedM68kRegisters()
	{
		AssertRegisters(typeof(DosRunCommandCallbacks),
			nameof(DosRunCommandCallbacks.Execute), M68kRegister.A3,
			M68kRegister.D0, M68kRegister.A0);
		var method = typeof(DosRunCommandCallbacks).GetMethod(
			nameof(DosRunCommandCallbacks.Execute),
			BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(method);
		Assert.Equal(M68kRegister.A3,
			method!.GetCustomAttribute<
				CopperSharp.Sdk.Amiga.AmigaIndirectCallAttribute>()?.TargetRegister);
	}

	[Fact]
	public void DosProcessExitCallbackUsesPublishedM68kRegisters()
	{
		AssertRegisters(typeof(DosProcessExitCallbacks),
			nameof(DosProcessExitCallbacks.Execute), M68kRegister.A3,
			M68kRegister.D0, M68kRegister.D1);
		var method = typeof(DosProcessExitCallbacks).GetMethod(
			nameof(DosProcessExitCallbacks.Execute),
			BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(method);
		Assert.Equal(M68kRegister.A3,
			method!.GetCustomAttribute<
				CopperSharp.Sdk.Amiga.AmigaIndirectCallAttribute>()?.TargetRegister);
	}

	[Fact]
	public void ExAllHookCallbackUsesPublishedM68kRegisters()
	{
		AssertRegisters(typeof(DosExAllCallbacks),
			nameof(DosExAllCallbacks.Match), M68kRegister.A3,
			M68kRegister.A0, M68kRegister.A1, M68kRegister.A2);
		var method = typeof(DosExAllCallbacks).GetMethod(
			nameof(DosExAllCallbacks.Match),
			BindingFlags.Public | BindingFlags.Static);
		Assert.NotNull(method);
		Assert.Equal(M68kRegister.A3,
			method!.GetCustomAttribute<
				CopperSharp.Sdk.Amiga.AmigaIndirectCallAttribute>()?.TargetRegister);
		Assert.Equal(M68kRegister.D0, method.ReturnParameter
			.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Fact]
	public void PublicUtilityLvoConstantsMatchEveryUtilityDeclaration()
	{
		var methods = typeof(Utility).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttributes(false).OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>().Any());
		foreach (var method in methods)
		{
			var field = typeof(UtilityLvo).GetField(method.Name, BindingFlags.Public | BindingFlags.Static);
			Assert.NotNull(field); Assert.True(field!.IsLiteral);
			AssertLvo(method.Name, (short)field.GetRawConstantValue()!, typeof(Utility));
		}
	}

	[Theory]
	[InlineData(typeof(Expansion), typeof(ExpansionLvo), 21)]
	[InlineData(typeof(Icon), typeof(IconLvo), 21)]
	public void PublicExpansionAndIconLvoConstantsMatchDeclarations(Type library, Type constants, int expectedCount)
	{
		var methods = library.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttributes(false).OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>().Any()).ToArray();
		Assert.Equal(expectedCount, methods.Length);
		foreach (var method in methods)
		{
			var field = constants.GetField(method.Name, BindingFlags.Public | BindingFlags.Static);
			Assert.NotNull(field); Assert.True(field!.IsLiteral);
			AssertLvo(method.Name, (short)field.GetRawConstantValue()!, library);
		}
	}

	[Theory]
	[InlineData(nameof(Intuition.OpenScreen), IntuitionLvo.OpenScreen)]
	[InlineData(nameof(Intuition.OpenScreenTagList), IntuitionLvo.OpenScreenTagList)]
	[InlineData(nameof(Intuition.OpenWindow), IntuitionLvo.OpenWindow)]
	[InlineData(nameof(Intuition.ModifyIDCMP), IntuitionLvo.ModifyIDCMP)]
	[InlineData(nameof(Intuition.RethinkDisplay), IntuitionLvo.RethinkDisplay)]
	public void PublicIntuitionLvoConstantsMatchPortableDeclarations(string methodName, short expected) => AssertLvo(methodName, expected, typeof(Intuition));

	[Fact]
	public void PublicWorkbenchLvoConstantsMatchEveryDeclaration()
	{
		var methods = typeof(Workbench).GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttributes(false).OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>().Any()).ToArray();
		Assert.Equal(15, methods.Length);
		foreach (var method in methods) { var field = typeof(WorkbenchLvo).GetField(method.Name, BindingFlags.Public | BindingFlags.Static); Assert.NotNull(field); Assert.True(field!.IsLiteral); AssertLvo(method.Name, (short)field.GetRawConstantValue()!, typeof(Workbench)); }
	}

	[Fact]
	public void ExecDeclarationsHaveCompleteRegisterContracts()
	{
		var methods = typeof(Exec).GetMethods(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Static |
			System.Reflection.BindingFlags.DeclaredOnly)
			.Where(method => method.GetCustomAttributes(false)
				.OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>().Any());

		foreach (var method in methods)
		{
			var parameterRegisters = method.GetParameters()
				.Select(parameter => parameter.GetCustomAttributes(false)
					.OfType<M68kRegisterAttribute>().Single().Register)
				.ToArray();
			Assert.Equal(parameterRegisters.Length, parameterRegisters.Distinct().Count());
			Assert.DoesNotContain(M68kRegister.A6, parameterRegisters);
			if (method.ReturnType != typeof(void))
			{
				var resultRegister = method.ReturnParameter.GetCustomAttributes(false)
					.OfType<M68kRegisterAttribute>().Single().Register;
				Assert.Equal(M68kRegister.D0, resultRegister);
			}
		}
	}

	[Fact]
	public void MakeFunctionsReturnsTableSizeInD0()
	{
		var method = typeof(Exec).GetMethod(nameof(Exec.MakeFunctions))!;

		Assert.Equal(typeof(uint), method.ReturnType);
		Assert.Equal(M68kRegister.D0,
			method.ReturnParameter.GetCustomAttribute<M68kRegisterAttribute>()?.Register);
	}

	[Fact]
	public void ExecLvoCollisionsAreLimitedToIntentionalOverloads()
	{
		var duplicates = typeof(Exec).GetMethods(
			System.Reflection.BindingFlags.Public |
			System.Reflection.BindingFlags.Static |
			System.Reflection.BindingFlags.DeclaredOnly)
			.Select(method => (Method: method, Lvo: method.GetCustomAttributes(false)
				.OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>().SingleOrDefault()?.Offset))
			.Where(item => item.Lvo.HasValue)
			.GroupBy(item => item.Lvo!.Value)
			.Where(group => group.Count() > 1)
			.ToDictionary(group => group.Key,
				group => group.Select(item => item.Method.Name).Order().ToArray());

		Assert.Single(duplicates);
		Assert.Equal(new[] { nameof(Exec.OpenLibrary), nameof(Exec.OpenLibraryRaw) }, duplicates[-552]);
	}

	[Fact]
	public void MorphOsPortableTaskAndLibraryConstantsMatchPublishedHeaders()
	{
		Assert.Equal(0x01u, (uint)TaskInfoType.Name);
		Assert.Equal(0x2Bu, (uint)TaskInfoType.UserData);
		Assert.Equal(0x33u, (uint)TaskInfoType.ProcessId);
		Assert.Equal(0x100u, (uint)SystemInfoType.PageSize);
		Assert.Equal(0x8010_0002u, ExecConstants.TaskTagProgramCounter);
		Assert.Equal(0x8100_0100u, ExecConstants.LibraryTagFunctionInit);
		Assert.Equal(0x8100_010Cu, ExecConstants.LibraryTagPublic);
		Assert.Equal(0u, (uint)ExecNodeListType.Device);
		Assert.Equal(8u, (uint)ExecNodeListType.Task);
		Assert.Equal(8u, TimeVal.Size);
		Assert.Equal(8u, EClockVal.Size);
		Assert.Equal(40u, TimerRequest.Size);
		Assert.Equal(32, TimerDeviceLayout.TimerRequest.Time);
		Assert.Equal(36, TimerDeviceLayout.TimerRequest.Microseconds);
		Assert.Equal(2u, (uint)TimerUnit.EClock);
		Assert.Equal(9u, (uint)TimerCommand.AddRequest);
		Assert.Equal(-60, TimerDevice.ReadEClockLvo);
		Assert.Equal(22u, InputEvent.Size);
		Assert.Equal(14, InputEventLayout.TimeStamp);
		Assert.Equal(18, InputEventLayout.Microseconds);
		Assert.Equal(8u, GamePortTrigger.Size);
		Assert.Equal(56, Marshal.SizeOf<IOExtTD>());
		Assert.Equal(32, Marshal.SizeOf<DriveGeometry>());
		Assert.Equal(48, Marshal.OffsetOf<IOExtTD>(nameof(IOExtTD.Count)).ToInt32());
		Assert.Equal(28, Marshal.OffsetOf<DriveGeometry>(nameof(DriveGeometry.DeviceType)).ToInt32());
		Assert.Equal((ushort)22, (ushort)TrackDiskCommand.GetGeometry);
		Assert.Equal((byte)28, (byte)TrackDiskError.WriteProtected);
		Assert.Equal(68, Marshal.SizeOf<IOAudio>());
		Assert.Equal(32, Marshal.OffsetOf<IOAudio>(nameof(IOAudio.AllocationKey)).ToInt32());
		Assert.Equal(34, Marshal.OffsetOf<IOAudio>(nameof(IOAudio.Data)).ToInt32());
		Assert.Equal(48, Marshal.OffsetOf<IOAudio>(nameof(IOAudio.WriteMessage)).ToInt32());
		Assert.Equal((ushort)32, (ushort)AudioCommand.Allocate);
		Assert.Equal((sbyte)-11, (sbyte)AudioIoError.AllocationFailed);
		Assert.Equal((byte)0xE0, (byte)(AudioIoFlags.SyncCycle | AudioIoFlags.NoWait | AudioIoFlags.WriteMessage));
		Assert.Equal((ushort)9, (ushort)ConsoleCommand.AskKeyMap);
		Assert.Equal(-1, (int)ConsoleUnit.Library);
		Assert.Equal(3, (int)ConsoleUnit.SnipMap);
		Assert.Equal(-42, ConsoleDevice.CDInputHandler);
		Assert.Equal(-48, ConsoleDevice.RawKeyConvert);
		Assert.Equal(52, Marshal.SizeOf<IOClipReq>());
		Assert.Equal(48, Marshal.OffsetOf<IOClipReq>(nameof(IOClipReq.ClipId)).ToInt32());
		Assert.Equal(44, IOClipReqLayout.Offset);
		Assert.Equal(26, Marshal.SizeOf<SatisfyMessage>());
		Assert.Equal(12, Marshal.SizeOf<ClipHookMessage>());
		Assert.Equal((ushort)12, (ushort)ClipboardCommand.ChangeHook);
		Assert.Equal(9u, (uint)KeyboardCommand.ReadEvent);
		Assert.Equal(11u, (uint)InputDeviceCommand.WriteEvent);
		Assert.Equal(13u, (uint)GamePortCommand.SetTrigger);
		Assert.Equal(0x8000_03E9u, ExecConstants.ExecNodeTagType);
		Assert.Equal(0x8000_03EBu, ExecConstants.ExecNodeTagName);
	}

	private static void AssertOffsets<T>(params (string Name, int Offset)[] expected)
	{
		foreach (var (name, offset) in expected)
		{
			Assert.Equal(offset, Marshal.OffsetOf<T>(name).ToInt32());
		}
	}

	private static void AssertLvo(string methodName, int expected, Type? declaringType = null)
	{
		var method = (declaringType ?? typeof(Exec)).GetMethod(methodName)!;
		var attribute = method.GetCustomAttributes(false)
			.OfType<CopperSharp.Sdk.Amiga.AmigaLvoAttribute>()
			.Single();
		Assert.Equal(expected, attribute.Offset);
	}

	private static void AssertRegisters(Type declaringType, string methodName,
		params M68kRegister[] expected)
	{
		var method = declaringType.GetMethod(methodName,
			BindingFlags.Public | BindingFlags.Static)!;
		Assert.Equal(expected, method.GetParameters().Select(parameter =>
			parameter.GetCustomAttribute<M68kRegisterAttribute>()!.Register));
	}
}
