/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>Named guest-memory accessors for published ExecBase fields.</summary>
public static class ExecBaseCodec
{
	public static APTR ReadThisTask<TMemory>(ref TMemory memory, APTR execBase)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(execBase, ExecLayout.ExecBase.ThisTask));

	public static ushort ReadSysFlags<TMemory>(ref TMemory memory, APTR execBase)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(execBase, ExecLayout.ExecBase.SysFlags);

	public static void WriteSysFlags<TMemory>(ref TMemory memory, APTR execBase,
		ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(execBase, ExecLayout.ExecBase.SysFlags, value);

	public static ushort ReadAttentionReschedule<TMemory>(ref TMemory memory,
		APTR execBase) where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(execBase, ExecLayout.ExecBase.AttentionReschedule);

	public static void WriteAttentionReschedule<TMemory>(ref TMemory memory,
		APTR execBase, ushort value)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(execBase, ExecLayout.ExecBase.AttentionReschedule,
			value);

	public static APTR LibraryListAddress(APTR execBase) => APTR.FromPointer(
		execBase.Raw + ExecLayout.ExecBase.LibraryList);

	public static APTR TaskReadyAddress(APTR execBase) => APTR.FromPointer(
		execBase.Raw + ExecLayout.ExecBase.TaskReady);

	public static APTR TaskWaitAddress(APTR execBase) => APTR.FromPointer(
		execBase.Raw + ExecLayout.ExecBase.TaskWait);
}

/// <summary>
/// Big-endian guest-memory access for Exec's packed public Library ABI.
/// Field writers are provided for lifecycle mutations so callers need not
/// rewrite a possibly concurrently updated library record.
/// </summary>
public static class ExecLibraryCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - Library.Size &&
		memory.IsMapped(address, Library.Size);

	public static Library Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = ExecNodeCodec.Read(ref memory, address),
		Flags = ReadFlags(ref memory, address),
		Padding = memory.ReadUInt8(address, ExecLayout.Library.Padding),
		NegativeSize = ReadNegativeSize(ref memory, address),
		PositiveSize = ReadPositiveSize(ref memory, address),
		Version = ReadVersion(ref memory, address),
		Revision = ReadRevision(ref memory, address),
		IdString = ReadIdString(ref memory, address),
		Checksum = memory.ReadUInt32(address, ExecLayout.Library.Checksum),
		OpenCount = ReadOpenCount(ref memory, address),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Library value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecNodeCodec.Write(ref memory, address, value.Node);
		WriteFlags(ref memory, address, value.Flags);
		memory.WriteUInt8(address, ExecLayout.Library.Padding, value.Padding);
		WriteNegativeSize(ref memory, address, value.NegativeSize);
		WritePositiveSize(ref memory, address, value.PositiveSize);
		WriteVersion(ref memory, address, value.Version);
		WriteRevision(ref memory, address, value.Revision);
		WriteIdString(ref memory, address, value.IdString);
		memory.WriteUInt32(address, ExecLayout.Library.Checksum, value.Checksum);
		WriteOpenCount(ref memory, address, value.OpenCount);
	}

	public static LibraryFlags ReadFlags<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory =>
		(LibraryFlags)memory.ReadUInt8(address, ExecLayout.Library.Flags);
	public static void WriteFlags<TMemory>(ref TMemory memory, APTR address,
		LibraryFlags value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, ExecLayout.Library.Flags, (byte)value);
	public static ushort ReadNegativeSize<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(address, ExecLayout.Library.NegativeSize);
	public static void WriteNegativeSize<TMemory>(ref TMemory memory,
		APTR address, ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, ExecLayout.Library.NegativeSize, value);
	public static ushort ReadPositiveSize<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(address, ExecLayout.Library.PositiveSize);
	public static void WritePositiveSize<TMemory>(ref TMemory memory,
		APTR address, ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, ExecLayout.Library.PositiveSize, value);
	public static ushort ReadVersion<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(address, ExecLayout.Library.Version);
	public static void WriteVersion<TMemory>(ref TMemory memory, APTR address,
		ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, ExecLayout.Library.Version, value);
	public static ushort ReadRevision<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(address, ExecLayout.Library.Revision);
	public static void WriteRevision<TMemory>(ref TMemory memory, APTR address,
		ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, ExecLayout.Library.Revision, value);
	public static APTR ReadIdString<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Library.IdString));
	public static void WriteIdString<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Library.IdString, value.Raw);
	public static ushort ReadOpenCount<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(address, ExecLayout.Library.OpenCount);
	public static void WriteOpenCount<TMemory>(ref TMemory memory, APTR address,
		ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, ExecLayout.Library.OpenCount, value);
}

/// <summary>Big-endian guest-memory access for Exec's public Resident ABI.</summary>
public static class ExecResidentCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - Resident.Size &&
		memory.IsMapped(address, Resident.Size);

	public static Resident Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MatchWord = memory.ReadUInt16(address, ExecLayout.Resident.MatchWord),
		MatchTag = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Resident.MatchTag)),
		EndSkip = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Resident.EndSkip)),
		Flags = (ResidentFlags)memory.ReadUInt8(address,
			ExecLayout.Resident.Flags),
		Version = memory.ReadUInt8(address, ExecLayout.Resident.Version),
		Type = memory.ReadUInt8(address, ExecLayout.Resident.Type),
		Priority = unchecked((sbyte)memory.ReadUInt8(address,
			ExecLayout.Resident.Priority)),
		Name = STRPTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Resident.Name)),
		IdString = STRPTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Resident.IdString)),
		Init = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Resident.Init)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Resident value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, ExecLayout.Resident.MatchWord,
			value.MatchWord);
		memory.WriteUInt32(address, ExecLayout.Resident.MatchTag,
			value.MatchTag.Raw);
		memory.WriteUInt32(address, ExecLayout.Resident.EndSkip,
			value.EndSkip.Raw);
		memory.WriteUInt8(address, ExecLayout.Resident.Flags, (byte)value.Flags);
		memory.WriteUInt8(address, ExecLayout.Resident.Version, value.Version);
		memory.WriteUInt8(address, ExecLayout.Resident.Type, value.Type);
		memory.WriteUInt8(address, ExecLayout.Resident.Priority,
			unchecked((byte)value.Priority));
		memory.WriteUInt32(address, ExecLayout.Resident.Name, value.Name.Raw);
		memory.WriteUInt32(address, ExecLayout.Resident.IdString,
			value.IdString.Raw);
		memory.WriteUInt32(address, ExecLayout.Resident.Init, value.Init.Raw);
	}
}

/// <summary>
/// Big-endian guest-memory access for Exec's public AUTOINIT descriptor.
/// </summary>
public static class ExecResidentAutoInitCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - ResidentAutoInit.Size &&
		memory.IsMapped(address, ResidentAutoInit.Size);

	public static ResidentAutoInit Read<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		DataSize = memory.ReadUInt32(address,
			ExecLayout.ResidentAutoInit.DataSize),
		FunctionTable = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.ResidentAutoInit.FunctionTable)),
		StructureTable = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.ResidentAutoInit.StructureTable)),
		InitFunction = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.ResidentAutoInit.InitFunction)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		ResidentAutoInit value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.DataSize,
			value.DataSize);
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.FunctionTable,
			value.FunctionTable.Raw);
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.StructureTable,
			value.StructureTable.Raw);
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.InitFunction,
			value.InitFunction.Raw);
	}
}

/// <summary>
/// Big-endian guest-memory access for the public Exec Node ABI. Algorithms use
/// named operations here instead of repeating layout offsets.
/// </summary>
public static class ExecNodeCodec
{
	public static bool AreLinksMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - MinNode.Size &&
		memory.IsMapped(address, MinNode.Size);

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - Node.Size &&
		memory.IsMapped(address, Node.Size);

	public static APTR ReadSuccessor<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, ExecLayout.Node.Successor));

	public static APTR ReadPredecessor<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, ExecLayout.Node.Predecessor));

	public static NodeType ReadType<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		(NodeType)memory.ReadUInt8(address, ExecLayout.Node.Type);

	public static STRPTR ReadName<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		STRPTR.FromPointer(memory.ReadUInt32(address, ExecLayout.Node.Name));

	public static Node Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Successor = ReadSuccessor(ref memory, address),
		Predecessor = ReadPredecessor(ref memory, address),
		Type = (byte)ReadType(ref memory, address),
		Priority = unchecked((sbyte)memory.ReadUInt8(address,
			ExecLayout.Node.Priority)),
		Name = ReadName(ref memory, address),
	};

	public static void WriteName<TMemory>(ref TMemory memory, APTR address,
		STRPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Node.Name, value.Raw);

	public static void WriteSuccessor<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Node.Successor, value.Raw);

	public static void WritePredecessor<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Node.Predecessor, value.Raw);

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Node value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteSuccessor(ref memory, address, value.Successor);
		WritePredecessor(ref memory, address, value.Predecessor);
		memory.WriteUInt8(address, ExecLayout.Node.Type, value.Type);
		memory.WriteUInt8(address, ExecLayout.Node.Priority,
			unchecked((byte)value.Priority));
		WriteName(ref memory, address, value.Name);
	}
}

/// <summary>
/// Big-endian guest-memory access for Exec's packed public List ABI.
/// </summary>
public static class ExecListCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - List.Size &&
		memory.IsMapped(address, List.Size);

	public static APTR TailAddress(APTR address) =>
		APTR.FromPointer(address.Raw + ExecLayout.List.Tail);

	public static APTR ReadHead<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, ExecLayout.List.Head));

	public static APTR ReadTailPred<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, ExecLayout.List.TailPred));

	public static List Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Head = ReadHead(ref memory, address),
		Tail = APTR.FromPointer(memory.ReadUInt32(address, ExecLayout.List.Tail)),
		TailPred = ReadTailPred(ref memory, address),
		Type = (NodeType)memory.ReadUInt8(address, ExecLayout.List.Type),
		Padding = memory.ReadUInt8(address, ExecLayout.List.Padding),
	};

	public static void WriteHead<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.List.Head, value.Raw);

	public static void WriteTail<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.List.Tail, value.Raw);

	public static void WriteTailPred<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.List.TailPred, value.Raw);

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		List value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteHead(ref memory, address, value.Head);
		WriteTail(ref memory, address, value.Tail);
		WriteTailPred(ref memory, address, value.TailPred);
		memory.WriteUInt8(address, ExecLayout.List.Type, (byte)value.Type);
		memory.WriteUInt8(address, ExecLayout.List.Padding, value.Padding);
	}
}

/// <summary>
/// Big-endian guest-memory access for Exec's classic packed Task ABI. MorphOS
/// callers that need the alternate trap-area interpretation use only the named
/// common-field operations such as <see cref="ReadState{TMemory}"/>.
/// </summary>
public static class ExecTaskCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - Task.Size &&
		memory.IsMapped(address, Task.Size);

	public static TaskState ReadState<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		(TaskState)memory.ReadUInt8(address, ExecLayout.Task.State);

	public static APTR MemoryEntriesAddress(APTR address) => APTR.FromPointer(
		address.Raw + ExecLayout.Task.MemoryEntries);

	public static Task Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = ExecNodeCodec.Read(ref memory, address),
		Flags = (TaskFlags)memory.ReadUInt8(address, ExecLayout.Task.Flags),
		State = ReadState(ref memory, address),
		IDNestCount = unchecked((sbyte)memory.ReadUInt8(address,
			ExecLayout.Task.IDNestCount)),
		TaskDisableNestCount = unchecked((sbyte)memory.ReadUInt8(address,
			ExecLayout.Task.TaskDisableNestCount)),
		SignalAllocated = memory.ReadUInt32(address,
			ExecLayout.Task.SignalAllocated),
		SignalWait = memory.ReadUInt32(address, ExecLayout.Task.SignalWait),
		SignalReceived = memory.ReadUInt32(address,
			ExecLayout.Task.SignalReceived),
		SignalException = memory.ReadUInt32(address,
			ExecLayout.Task.SignalException),
		TrapAllocated = memory.ReadUInt16(address, ExecLayout.Task.TrapAllocated),
		TrapEnabled = memory.ReadUInt16(address, ExecLayout.Task.TrapEnabled),
		ExceptionData = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.ExceptionData)),
		ExceptionCode = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.ExceptionCode)),
		TrapData = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.TrapData)),
		TrapCode = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.TrapCode)),
		StackPointer = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.StackPointer)),
		StackLower = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.StackLower)),
		StackUpper = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.StackUpper)),
		Switch = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.Switch)),
		Launch = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.Launch)),
		MemoryEntries = ExecListCodec.Read(ref memory,
			MemoryEntriesAddress(address)),
		UserData = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Task.UserData)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Task value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecNodeCodec.Write(ref memory, address, value.Node);
		memory.WriteUInt8(address, ExecLayout.Task.Flags, (byte)value.Flags);
		memory.WriteUInt8(address, ExecLayout.Task.State, (byte)value.State);
		memory.WriteUInt8(address, ExecLayout.Task.IDNestCount,
			unchecked((byte)value.IDNestCount));
		memory.WriteUInt8(address, ExecLayout.Task.TaskDisableNestCount,
			unchecked((byte)value.TaskDisableNestCount));
		memory.WriteUInt32(address, ExecLayout.Task.SignalAllocated,
			value.SignalAllocated);
		memory.WriteUInt32(address, ExecLayout.Task.SignalWait, value.SignalWait);
		memory.WriteUInt32(address, ExecLayout.Task.SignalReceived,
			value.SignalReceived);
		memory.WriteUInt32(address, ExecLayout.Task.SignalException,
			value.SignalException);
		memory.WriteUInt16(address, ExecLayout.Task.TrapAllocated,
			value.TrapAllocated);
		memory.WriteUInt16(address, ExecLayout.Task.TrapEnabled,
			value.TrapEnabled);
		memory.WriteUInt32(address, ExecLayout.Task.ExceptionData,
			value.ExceptionData.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.ExceptionCode,
			value.ExceptionCode.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.TrapData, value.TrapData.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.TrapCode, value.TrapCode.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.StackPointer,
			value.StackPointer.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.StackLower,
			value.StackLower.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.StackUpper,
			value.StackUpper.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.Switch, value.Switch.Raw);
		memory.WriteUInt32(address, ExecLayout.Task.Launch, value.Launch.Raw);
		ExecListCodec.Write(ref memory, MemoryEntriesAddress(address),
			value.MemoryEntries);
		memory.WriteUInt32(address, ExecLayout.Task.UserData, value.UserData.Raw);
	}
}

/// <summary>Big-endian guest-memory access for one Exec MemEntry.</summary>
public static class ExecMemEntryCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - MemEntry.Size &&
		memory.IsMapped(address, MemEntry.Size);

	public static MemEntry Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		AddressOrRequirements = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.MemEntry.AddressOrRequirements)),
		Length = memory.ReadUInt32(address, ExecLayout.MemEntry.Length),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MemEntry value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.MemEntry.AddressOrRequirements,
			value.AddressOrRequirements.Raw);
		memory.WriteUInt32(address, ExecLayout.MemEntry.Length, value.Length);
	}
}

/// <summary>
/// Big-endian guest-memory access for Exec's public variable-length MemList ABI.
/// </summary>
public static class ExecMemListCodec
{
	public const uint HeaderSize = ExecLayout.MemList.FirstEntry;

	public static uint ByteSize(ushort numberOfEntries) =>
		HeaderSize + unchecked((uint)numberOfEntries * MemEntry.Size);

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address,
		ushort numberOfEntries) where TMemory : struct, IAmigaGuestMemory
	{
		var size = ByteSize(numberOfEntries);
		return address.IsNotNull && address.Raw <= uint.MaxValue - size &&
			memory.IsMapped(address, size);
	}

	public static ushort ReadNumberOfEntries<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt16(address, ExecLayout.MemList.NumberOfEntries);

	public static void WriteNumberOfEntries<TMemory>(ref TMemory memory,
		APTR address, ushort value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt16(address, ExecLayout.MemList.NumberOfEntries, value);

	public static APTR EntryAddress(APTR address, ushort index) =>
		APTR.FromPointer(address.Raw + HeaderSize +
			unchecked((uint)index * MemEntry.Size));

	public static MemEntry ReadEntry<TMemory>(ref TMemory memory, APTR address,
		ushort index) where TMemory : struct, IAmigaGuestMemory =>
		ExecMemEntryCodec.Read(ref memory, EntryAddress(address, index));

	public static void WriteEntry<TMemory>(ref TMemory memory, APTR address,
		ushort index, MemEntry value) where TMemory : struct, IAmigaGuestMemory =>
		ExecMemEntryCodec.Write(ref memory, EntryAddress(address, index), value);
}

/// <summary>Big-endian guest-memory access for Exec's public Message ABI.</summary>
public static class ExecMessageCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - Message.Size &&
		memory.IsMapped(address, Message.Size);

	public static APTR ReadReplyPort<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, ExecLayout.Message.ReplyPort));

	public static Message Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = ExecNodeCodec.Read(ref memory, address),
		ReplyPort = ReadReplyPort(ref memory, address),
		Length = memory.ReadUInt16(address, ExecLayout.Message.Length),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Message value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecNodeCodec.Write(ref memory, address, value.Node);
		memory.WriteUInt32(address, ExecLayout.Message.ReplyPort,
			value.ReplyPort.Raw);
		memory.WriteUInt16(address, ExecLayout.Message.Length, value.Length);
	}
}

/// <summary>Big-endian guest-memory access for Exec's public Interrupt ABI.</summary>
public static class ExecInterruptCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - Interrupt.Size &&
		memory.IsMapped(address, Interrupt.Size);

	public static Interrupt Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = ExecNodeCodec.Read(ref memory, address),
		Data = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Interrupt.Data)),
		Code = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.Interrupt.Code)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Interrupt value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecNodeCodec.Write(ref memory, address, value.Node);
		memory.WriteUInt32(address, ExecLayout.Interrupt.Data, value.Data.Raw);
		memory.WriteUInt32(address, ExecLayout.Interrupt.Code, value.Code.Raw);
	}
}

/// <summary>Big-endian guest-memory access for Exec IORequest envelopes.</summary>
public static class ExecIORequestCodec
{
	public static bool IsRequestMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - IORequest.Size &&
		memory.IsMapped(address, IORequest.Size);

	public static bool IsStandardRequestMapped<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && address.Raw <= uint.MaxValue - IOStdReq.Size &&
		memory.IsMapped(address, IOStdReq.Size);

	public static IORequest ReadRequest<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Message = ExecMessageCodec.Read(ref memory, address),
		Device = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.IORequest.Device)),
		Unit = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.IORequest.Unit)),
		Command = (DeviceCommand)memory.ReadUInt16(address,
			ExecLayout.IORequest.Command),
		Flags = (IOFlags)memory.ReadUInt8(address, ExecLayout.IORequest.Flags),
		Error = unchecked((sbyte)memory.ReadUInt8(address,
			ExecLayout.IORequest.Error)),
	};

	public static void WriteRequest<TMemory>(ref TMemory memory, APTR address,
		IORequest value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecMessageCodec.Write(ref memory, address, value.Message);
		memory.WriteUInt32(address, ExecLayout.IORequest.Device, value.Device.Raw);
		memory.WriteUInt32(address, ExecLayout.IORequest.Unit, value.Unit.Raw);
		memory.WriteUInt16(address, ExecLayout.IORequest.Command,
			(ushort)value.Command);
		memory.WriteUInt8(address, ExecLayout.IORequest.Flags, (byte)value.Flags);
		memory.WriteUInt8(address, ExecLayout.IORequest.Error,
			unchecked((byte)value.Error));
	}

	public static IOStdReq ReadStandardRequest<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Message = ExecMessageCodec.Read(ref memory, address),
		Device = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.IOStdReq.Device)),
		Unit = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.IOStdReq.Unit)),
		Command = (DeviceCommand)memory.ReadUInt16(address,
			ExecLayout.IOStdReq.Command),
		Flags = (IOFlags)memory.ReadUInt8(address, ExecLayout.IOStdReq.Flags),
		Error = unchecked((sbyte)memory.ReadUInt8(address,
			ExecLayout.IOStdReq.Error)),
		Actual = memory.ReadUInt32(address, ExecLayout.IOStdReq.Actual),
		Length = memory.ReadUInt32(address, ExecLayout.IOStdReq.Length),
		Data = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.IOStdReq.Data)),
		Offset = memory.ReadUInt32(address, ExecLayout.IOStdReq.Offset),
	};

	public static void WriteStandardRequest<TMemory>(ref TMemory memory,
		APTR address, IOStdReq value)
		where TMemory : struct, IAmigaGuestMemory
	{
		ExecMessageCodec.Write(ref memory, address, value.Message);
		memory.WriteUInt32(address, ExecLayout.IOStdReq.Device, value.Device.Raw);
		memory.WriteUInt32(address, ExecLayout.IOStdReq.Unit, value.Unit.Raw);
		memory.WriteUInt16(address, ExecLayout.IOStdReq.Command,
			(ushort)value.Command);
		memory.WriteUInt8(address, ExecLayout.IOStdReq.Flags, (byte)value.Flags);
		memory.WriteUInt8(address, ExecLayout.IOStdReq.Error,
			unchecked((byte)value.Error));
		memory.WriteUInt32(address, ExecLayout.IOStdReq.Actual, value.Actual);
		memory.WriteUInt32(address, ExecLayout.IOStdReq.Length, value.Length);
		memory.WriteUInt32(address, ExecLayout.IOStdReq.Data, value.Data.Raw);
		memory.WriteUInt32(address, ExecLayout.IOStdReq.Offset, value.Offset);
	}
}

/// <summary>Big-endian guest-memory access for Exec's public MsgPort ABI.</summary>
public static class ExecMsgPortCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		address.Raw <= uint.MaxValue - MsgPort.Size &&
		memory.IsMapped(address, MsgPort.Size);

	public static APTR MessageListAddress(APTR address) =>
		APTR.FromPointer(address.Raw + ExecLayout.MsgPort.MessageList);

	public static MsgPort Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = new Node
		{
			Successor = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.Node.Successor)),
			Predecessor = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.Node.Predecessor)),
			Type = memory.ReadUInt8(address, ExecLayout.Node.Type),
			Priority = unchecked((sbyte)memory.ReadUInt8(address,
				ExecLayout.Node.Priority)),
			Name = STRPTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.Node.Name)),
		},
		Flags = (PortFlags)memory.ReadUInt8(address, ExecLayout.MsgPort.Flags),
		SignalBit = memory.ReadUInt8(address, ExecLayout.MsgPort.SignalBit),
		SignalTask = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.MsgPort.SignalTask)),
		MessageList = new List
		{
			Head = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.MsgPort.MessageList + ExecLayout.List.Head)),
			Tail = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.MsgPort.MessageList + ExecLayout.List.Tail)),
			TailPred = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.MsgPort.MessageList + ExecLayout.List.TailPred)),
			Type = (NodeType)memory.ReadUInt8(address,
				ExecLayout.MsgPort.MessageList + ExecLayout.List.Type),
			Padding = memory.ReadUInt8(address,
				ExecLayout.MsgPort.MessageList + ExecLayout.List.Padding),
		},
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		MsgPort value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.Node.Successor,
			value.Node.Successor.Raw);
		memory.WriteUInt32(address, ExecLayout.Node.Predecessor,
			value.Node.Predecessor.Raw);
		memory.WriteUInt8(address, ExecLayout.Node.Type, value.Node.Type);
		memory.WriteUInt8(address, ExecLayout.Node.Priority,
			unchecked((byte)value.Node.Priority));
		memory.WriteUInt32(address, ExecLayout.Node.Name, value.Node.Name.Raw);
		memory.WriteUInt8(address, ExecLayout.MsgPort.Flags, (byte)value.Flags);
		memory.WriteUInt8(address, ExecLayout.MsgPort.SignalBit, value.SignalBit);
		memory.WriteUInt32(address, ExecLayout.MsgPort.SignalTask,
			value.SignalTask.Raw);
		memory.WriteUInt32(address, ExecLayout.MsgPort.MessageList +
			ExecLayout.List.Head, value.MessageList.Head.Raw);
		memory.WriteUInt32(address, ExecLayout.MsgPort.MessageList +
			ExecLayout.List.Tail, value.MessageList.Tail.Raw);
		memory.WriteUInt32(address, ExecLayout.MsgPort.MessageList +
			ExecLayout.List.TailPred, value.MessageList.TailPred.Raw);
		memory.WriteUInt8(address, ExecLayout.MsgPort.MessageList +
			ExecLayout.List.Type, (byte)value.MessageList.Type);
		memory.WriteUInt8(address, ExecLayout.MsgPort.MessageList +
			ExecLayout.List.Padding, value.MessageList.Padding);
	}
}

/// <summary>Big-endian guest-memory access for Exec's StackSwapStruct ABI.</summary>
public static class ExecStackSwapCodec
{
	public static StackSwapStruct Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Lower = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.StackSwapStruct.Lower)),
		Upper = memory.ReadUInt32(address, ExecLayout.StackSwapStruct.Upper),
		Pointer = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.StackSwapStruct.Pointer)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		StackSwapStruct value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.StackSwapStruct.Lower,
			value.Lower.Raw);
		memory.WriteUInt32(address, ExecLayout.StackSwapStruct.Upper,
			value.Upper);
		memory.WriteUInt32(address, ExecLayout.StackSwapStruct.Pointer,
			value.Pointer.Raw);
	}
}
