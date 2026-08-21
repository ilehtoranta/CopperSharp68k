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

	public static APTR LibraryListAddress(APTR execBase) => APTR.FromPointer(
		execBase.Raw + ExecLayout.ExecBase.LibraryList);
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

	public static void WriteName<TMemory>(ref TMemory memory, APTR address,
		STRPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Node.Name, value.Raw);

	public static void WriteSuccessor<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Node.Successor, value.Raw);

	public static void WritePredecessor<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.Node.Predecessor, value.Raw);
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

	public static void WriteHead<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.List.Head, value.Raw);

	public static void WriteTail<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.List.Tail, value.Raw);

	public static void WriteTailPred<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, ExecLayout.List.TailPred, value.Raw);
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
