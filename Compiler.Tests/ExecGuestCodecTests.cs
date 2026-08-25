using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ExecGuestCodecTests
{
	[Fact]
	public void NodeAndListCodecsUsePackedBigEndianLayout()
	{
		var memory = new Memory(128);
		var list = APTR.FromPointer(8);
		var node = APTR.FromPointer(48);
		var tail = ExecListCodec.TailAddress(list);
		ExecListCodec.WriteHead(ref memory, list, node);
		ExecListCodec.WriteTail(ref memory, list, APTR.Null);
		ExecListCodec.WriteTailPred(ref memory, list, node);
		ExecNodeCodec.WriteSuccessor(ref memory, node, tail);
		ExecNodeCodec.WritePredecessor(ref memory, node, list);

		Assert.True(ExecListCodec.IsMapped(ref memory, list));
		Assert.True(ExecNodeCodec.AreLinksMapped(ref memory, node));
		Assert.True(ExecNodeCodec.IsMapped(ref memory, node));
		Assert.Equal(node, ExecListCodec.ReadHead(ref memory, list));
		Assert.Equal(node, ExecListCodec.ReadTailPred(ref memory, list));
		Assert.Equal(tail, ExecNodeCodec.ReadSuccessor(ref memory, node));
		Assert.Equal(list, ExecNodeCodec.ReadPredecessor(ref memory, node));
		Assert.Equal(node.Raw, memory.ReadUInt32(list, ExecLayout.List.Head));
		Assert.Equal(tail.Raw,
			memory.ReadUInt32(node, ExecLayout.Node.Successor));
	}

	[Fact]
	public void MessageAndMessagePortCodecsUsePackedBigEndianLayout()
	{
		var memory = new Memory(128);
		var message = APTR.FromPointer(8);
		var expectedMessage = new Message
		{
			Node = new Node
			{
				Successor = APTR.FromPointer(0x1020_3040),
				Predecessor = APTR.FromPointer(0x5060_7080),
				Type = (byte)NodeType.Message,
				Priority = -4,
				Name = STRPTR.FromPointer(0x90A0_B0C0),
			},
			ReplyPort = APTR.FromPointer(0x1234_5678),
			Length = 68,
		};
		ExecMessageCodec.Write(ref memory, message, expectedMessage);
		Assert.True(ExecMessageCodec.IsMapped(ref memory, message));
		var actualMessage = ExecMessageCodec.Read(ref memory, message);
		Assert.Equal(expectedMessage.Node.Successor,
			actualMessage.Node.Successor);
		Assert.Equal(expectedMessage.Node.Predecessor,
			actualMessage.Node.Predecessor);
		Assert.Equal(expectedMessage.Node.Type, actualMessage.Node.Type);
		Assert.Equal(expectedMessage.Node.Priority, actualMessage.Node.Priority);
		Assert.Equal(expectedMessage.Node.Name, actualMessage.Node.Name);
		Assert.Equal(expectedMessage.ReplyPort, actualMessage.ReplyPort);
		Assert.Equal(expectedMessage.Length, actualMessage.Length);
		Assert.Equal(expectedMessage.ReplyPort,
			ExecMessageCodec.ReadReplyPort(ref memory, message));

		var port = APTR.FromPointer(48);
		var list = ExecMsgPortCodec.MessageListAddress(port);
		var expected = new MsgPort
		{
			Node = new Node { Type = (byte)NodeType.MessagePort, Priority = -3 },
			Flags = PortFlags.Signal,
			SignalBit = 7,
			SignalTask = APTR.FromPointer(0x1020_3040),
			MessageList = new Amiga.List
			{
				Head = APTR.FromPointer(list.Raw + ExecLayout.List.Tail),
				TailPred = list,
				Type = NodeType.Message,
			},
		};
		ExecMsgPortCodec.Write(ref memory, port, expected);
		Assert.True(ExecMsgPortCodec.IsMapped(ref memory, port));
		var actual = ExecMsgPortCodec.Read(ref memory, port);
		Assert.Equal(expected.Node.Type, actual.Node.Type);
		Assert.Equal(expected.Node.Priority, actual.Node.Priority);
		Assert.Equal(expected.Flags, actual.Flags);
		Assert.Equal(expected.SignalBit, actual.SignalBit);
		Assert.Equal(expected.SignalTask, actual.SignalTask);
		Assert.Equal(expected.MessageList.Head, actual.MessageList.Head);
		Assert.Equal(expected.MessageList.TailPred, actual.MessageList.TailPred);
		Assert.Equal(expected.MessageList.Type, actual.MessageList.Type);
	}

	[Fact]
	public void TimerRequestCodecRoundTripsTheNamedPackedRecord()
	{
		var memory = new Memory(128);
		var address = APTR.FromPointer(16);
		var expected = new TimerRequest
		{
			Request = new IORequest
			{
				Message = new Message
				{
					Node = new Node
					{
						Successor = APTR.FromPointer(0x1020_3040),
						Predecessor = APTR.FromPointer(0x5060_7080),
						Type = (byte)NodeType.Message,
						Priority = -5,
						Name = STRPTR.FromPointer(0x1122_3344),
					},
					ReplyPort = APTR.FromPointer(0x5566_7788),
					Length = 40,
				},
				Device = APTR.FromPointer(0x89AB_CDEF),
				Unit = APTR.FromPointer(0x7654_3210),
				Command = (DeviceCommand)TimerCommand.AddRequest,
				Flags = (IOFlags)0x35,
				Error = -7,
			},
			Time = new TimeVal { Seconds = 0x0102_0304,
				Microseconds = 0x0506_0708 },
		};

		TimerRequestCodec.Write(ref memory, address, expected);
		var actual = TimerRequestCodec.Read(ref memory, address);

		Assert.True(TimerRequestCodec.IsMapped(ref memory, address));
		Assert.Equal(expected.Request.Message.Node.Successor,
			actual.Request.Message.Node.Successor);
		Assert.Equal(expected.Request.Message.Node.Predecessor,
			actual.Request.Message.Node.Predecessor);
		Assert.Equal(expected.Request.Message.Node.Type,
			actual.Request.Message.Node.Type);
		Assert.Equal(expected.Request.Message.Node.Priority,
			actual.Request.Message.Node.Priority);
		Assert.Equal(expected.Request.Message.Node.Name,
			actual.Request.Message.Node.Name);
		Assert.Equal(expected.Request.Message.ReplyPort,
			actual.Request.Message.ReplyPort);
		Assert.Equal(expected.Request.Message.Length,
			actual.Request.Message.Length);
		Assert.Equal(expected.Request.Device, actual.Request.Device);
		Assert.Equal(expected.Request.Unit, actual.Request.Unit);
		Assert.Equal(expected.Request.Command, actual.Request.Command);
		Assert.Equal(expected.Request.Flags, actual.Request.Flags);
		Assert.Equal(expected.Request.Error, actual.Request.Error);
		Assert.Equal(expected.Time.Seconds, actual.Time.Seconds);
		Assert.Equal(expected.Time.Microseconds, actual.Time.Microseconds);
		Assert.Equal(expected.Request.Device,
			TimerRequestCodec.ReadDevice(ref memory, address));
	}

	[Fact]
	public void LibraryResidentAutoInitAndTaskCodecsRoundTripNamedRecords()
	{
		var memory = new Memory(768);
		var libraryAddress = APTR.FromPointer(16);
		var expectedLibrary = new Library
		{
			Node = new Node
			{
				Type = (byte)NodeType.Library,
				Priority = -6,
				Name = STRPTR.FromPointer(0x1122_3344),
			},
			Flags = LibraryFlags.Changed | LibraryFlags.SumUsed,
			NegativeSize = 1204,
			PositiveSize = 512,
			Version = 51,
			Revision = 71,
			IdString = APTR.FromPointer(0x5566_7788),
			Checksum = 0x89AB_CDEF,
			OpenCount = 3,
		};
		ExecLibraryCodec.Write(ref memory, libraryAddress, expectedLibrary);
		var actualLibrary = ExecLibraryCodec.Read(ref memory, libraryAddress);
		Assert.True(ExecLibraryCodec.IsMapped(ref memory, libraryAddress));
		Assert.Equal(expectedLibrary.Node.Type, actualLibrary.Node.Type);
		Assert.Equal(expectedLibrary.Node.Name, actualLibrary.Node.Name);
		Assert.Equal(expectedLibrary.Flags, actualLibrary.Flags);
		Assert.Equal(expectedLibrary.NegativeSize, actualLibrary.NegativeSize);
		Assert.Equal(expectedLibrary.PositiveSize, actualLibrary.PositiveSize);
		Assert.Equal(expectedLibrary.Version, actualLibrary.Version);
		Assert.Equal(expectedLibrary.Revision, actualLibrary.Revision);
		Assert.Equal(expectedLibrary.IdString, actualLibrary.IdString);
		Assert.Equal(expectedLibrary.Checksum, actualLibrary.Checksum);
		Assert.Equal(expectedLibrary.OpenCount, actualLibrary.OpenCount);

		var residentAddress = APTR.FromPointer(80);
		var expectedResident = new Resident
		{
			MatchWord = 0x4AFC,
			MatchTag = residentAddress,
			EndSkip = APTR.FromPointer(0x200),
			Flags = ResidentFlags.AutoInit | ResidentFlags.AfterDos,
			Version = 51,
			Type = (byte)NodeType.Library,
			Priority = 4,
			Name = STRPTR.FromPointer(0x220),
			IdString = STRPTR.FromPointer(0x240),
			Init = APTR.FromPointer(0x260),
		};
		ExecResidentCodec.Write(ref memory, residentAddress, expectedResident);
		var actualResident = ExecResidentCodec.Read(ref memory, residentAddress);
		Assert.True(ExecResidentCodec.IsMapped(ref memory, residentAddress));
		Assert.Equal(expectedResident.MatchWord, actualResident.MatchWord);
		Assert.Equal(expectedResident.MatchTag, actualResident.MatchTag);
		Assert.Equal(expectedResident.EndSkip, actualResident.EndSkip);
		Assert.Equal(expectedResident.Flags, actualResident.Flags);
		Assert.Equal(expectedResident.Version, actualResident.Version);
		Assert.Equal(expectedResident.Type, actualResident.Type);
		Assert.Equal(expectedResident.Priority, actualResident.Priority);
		Assert.Equal(expectedResident.Name, actualResident.Name);
		Assert.Equal(expectedResident.IdString, actualResident.IdString);
		Assert.Equal(expectedResident.Init, actualResident.Init);

		var autoInitAddress = APTR.FromPointer(128);
		var expectedAutoInit = new ResidentAutoInit
		{
			DataSize = 512,
			FunctionTable = APTR.FromPointer(0x300),
			StructureTable = APTR.FromPointer(0x320),
			InitFunction = APTR.FromPointer(0x340),
		};
		ExecResidentAutoInitCodec.Write(ref memory, autoInitAddress,
			expectedAutoInit);
		var actualAutoInit = ExecResidentAutoInitCodec.Read(ref memory,
			autoInitAddress);
		Assert.True(ExecResidentAutoInitCodec.IsMapped(ref memory,
			autoInitAddress));
		Assert.Equal(expectedAutoInit.DataSize, actualAutoInit.DataSize);
		Assert.Equal(expectedAutoInit.FunctionTable,
			actualAutoInit.FunctionTable);
		Assert.Equal(expectedAutoInit.StructureTable,
			actualAutoInit.StructureTable);
		Assert.Equal(expectedAutoInit.InitFunction,
			actualAutoInit.InitFunction);

		var taskAddress = APTR.FromPointer(192);
		var memoryEntries = APTR.FromPointer(taskAddress.Raw +
			ExecLayout.Task.MemoryEntries);
		var expectedTask = new Amiga.Task
		{
			Node = new Node
			{
				Type = (byte)NodeType.Process,
				Priority = -2,
				Name = STRPTR.FromPointer(0x400),
			},
			Flags = TaskFlags.ProcessTime,
			State = TaskState.Ready,
			IDNestCount = -1,
			TaskDisableNestCount = -1,
			SignalAllocated = 0x0102_0304,
			SignalWait = 0x0506_0708,
			SignalReceived = 0x090A_0B0C,
			SignalException = 0x0D0E_0F10,
			TrapAllocated = 0x1122,
			TrapEnabled = 0x3344,
			StackPointer = APTR.FromPointer(0x500),
			StackLower = APTR.FromPointer(0x480),
			StackUpper = APTR.FromPointer(0x580),
			MemoryEntries = new Amiga.List
			{
				Head = ExecListCodec.TailAddress(memoryEntries),
				TailPred = memoryEntries,
				Type = NodeType.Memory,
			},
			UserData = APTR.FromPointer(0x600),
		};
		ExecTaskCodec.Write(ref memory, taskAddress, expectedTask);
		var actualTask = ExecTaskCodec.Read(ref memory, taskAddress);
		Assert.True(ExecTaskCodec.IsMapped(ref memory, taskAddress));
		Assert.Equal(expectedTask.Node.Type, actualTask.Node.Type);
		Assert.Equal(expectedTask.Flags, actualTask.Flags);
		Assert.Equal(expectedTask.State, actualTask.State);
		Assert.Equal(expectedTask.SignalAllocated, actualTask.SignalAllocated);
		Assert.Equal(expectedTask.StackPointer, actualTask.StackPointer);
		Assert.Equal(expectedTask.StackLower, actualTask.StackLower);
		Assert.Equal(expectedTask.StackUpper, actualTask.StackUpper);
		Assert.Equal(expectedTask.MemoryEntries.Head,
			actualTask.MemoryEntries.Head);
		Assert.Equal(expectedTask.MemoryEntries.TailPred,
			actualTask.MemoryEntries.TailPred);
		Assert.Equal(expectedTask.UserData, actualTask.UserData);
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
		public void Clear(APTR address, uint byteCount) =>
			Array.Clear(_bytes, checked((int)address.Raw), checked((int)byteCount));
		public void Copy(APTR source, APTR destination, uint byteCount) =>
			Array.Copy(_bytes, checked((int)source.Raw), _bytes,
				checked((int)destination.Raw), checked((int)byteCount));
		public bool IsMapped(APTR address, uint byteSize) => address.Raw != 0 &&
			address.Raw <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - address.Raw;
	}
}
