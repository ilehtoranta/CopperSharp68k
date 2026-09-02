/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class DosNotifyRequestCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		memory.IsMapped(address, NotifyRequest.Size);

	public static NotifyRequest Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Name = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.NotifyRequest.Name)),
		FullName = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.NotifyRequest.FullName)),
		UserData = memory.ReadUInt32(address, DosLayout.NotifyRequest.UserData),
		Flags = (DosNotifyFlags)memory.ReadUInt32(address,
			DosLayout.NotifyRequest.Flags),
		Target = new NotifyRequestTarget
		{
			Task = APTR.FromPointer(memory.ReadUInt32(address,
				DosLayout.NotifyRequest.Target)),
			SignalNumber = memory.ReadUInt8(address,
				DosLayout.NotifyRequest.Target +
				DosLayout.NotifyRequestTarget.SignalNumber),
		},
		Reserved0 = memory.ReadUInt32(address, DosLayout.NotifyRequest.Reserved0),
		Reserved1 = memory.ReadUInt32(address, DosLayout.NotifyRequest.Reserved1),
		Reserved2 = memory.ReadUInt32(address, DosLayout.NotifyRequest.Reserved2),
		Reserved3 = memory.ReadUInt32(address, DosLayout.NotifyRequest.Reserved3),
		MessageCount = memory.ReadUInt32(address,
			DosLayout.NotifyRequest.MessageCount),
		Handler = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.NotifyRequest.Handler)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in NotifyRequest value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Name, value.Name.Raw);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.FullName,
			value.FullName.Raw);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.UserData, value.UserData);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Flags,
			(uint)value.Flags);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Target,
			value.Target.Task.Raw);
		memory.WriteUInt8(address, DosLayout.NotifyRequest.Target +
			DosLayout.NotifyRequestTarget.SignalNumber, value.Target.SignalNumber);
		memory.WriteUInt8(address, DosLayout.NotifyRequest.Target + 5, 0);
		memory.WriteUInt8(address, DosLayout.NotifyRequest.Target + 6, 0);
		memory.WriteUInt8(address, DosLayout.NotifyRequest.Target + 7, 0);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Reserved0,
			value.Reserved0);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Reserved1,
			value.Reserved1);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Reserved2,
			value.Reserved2);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Reserved3,
			value.Reserved3);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.MessageCount,
			value.MessageCount);
		memory.WriteUInt32(address, DosLayout.NotifyRequest.Handler,
			value.Handler.Raw);
	}
}

public static class DosNotifyMessageCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1) == 0 && memory.IsMapped(address, NotifyMessage.Size);

	public static NotifyMessage Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		ExecMessage = new Message
		{
			Node = new Node
			{
				Successor = APTR.FromPointer(memory.ReadUInt32(address, 0)),
				Predecessor = APTR.FromPointer(memory.ReadUInt32(address, 4)),
				Type = memory.ReadUInt8(address, 8),
				Priority = unchecked((sbyte)memory.ReadUInt8(address, 9)),
				Name = APTR.FromPointer(memory.ReadUInt32(address, 10)),
			},
			ReplyPort = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.Message.ReplyPort)),
			Length = memory.ReadUInt16(address, ExecLayout.Message.Length),
		},
		MessageClass = memory.ReadUInt32(address,
			DosLayout.NotifyMessage.MessageClass),
		MessageCode = memory.ReadUInt16(address,
			DosLayout.NotifyMessage.MessageCode),
		Request = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.NotifyMessage.Request)),
		Private0 = memory.ReadUInt32(address, DosLayout.NotifyMessage.Private0),
		Private1 = memory.ReadUInt32(address, DosLayout.NotifyMessage.Private1),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in NotifyMessage value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.Node.Successor,
			value.ExecMessage.Node.Successor.Raw);
		memory.WriteUInt32(address, ExecLayout.Node.Predecessor,
			value.ExecMessage.Node.Predecessor.Raw);
		memory.WriteUInt8(address, ExecLayout.Node.Type,
			value.ExecMessage.Node.Type);
		memory.WriteUInt8(address, ExecLayout.Node.Priority,
			unchecked((byte)value.ExecMessage.Node.Priority));
		memory.WriteUInt32(address, ExecLayout.Node.Name,
			value.ExecMessage.Node.Name.Raw);
		memory.WriteUInt32(address, ExecLayout.Message.ReplyPort,
			value.ExecMessage.ReplyPort.Raw);
		memory.WriteUInt16(address, ExecLayout.Message.Length,
			value.ExecMessage.Length);
		memory.WriteUInt32(address, DosLayout.NotifyMessage.MessageClass,
			value.MessageClass);
		memory.WriteUInt16(address, DosLayout.NotifyMessage.MessageCode,
			value.MessageCode);
		memory.WriteUInt32(address, DosLayout.NotifyMessage.Request,
			value.Request.Raw);
		memory.WriteUInt32(address, DosLayout.NotifyMessage.Private0,
			value.Private0);
		memory.WriteUInt32(address, DosLayout.NotifyMessage.Private1,
			value.Private1);
	}
}

public static class DosAnchorPathCodec
{
	public const uint Size = AnchorPath.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);
	public static DosAnchorPathControl Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Base = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.AnchorPath.Base)),
		Current = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.AnchorPath.Current)),
		BreakBits = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.AnchorPath.BreakBits)),
		FoundBreak = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.AnchorPath.FoundBreak)),
		Flags = (AnchorPathFlags)memory.ReadUInt8(address,
			DosLayout.AnchorPath.Flags),
		Reserved = memory.ReadUInt8(address, DosLayout.AnchorPath.Reserved),
		StringLength = unchecked((short)memory.ReadUInt16(address,
			DosLayout.AnchorPath.StringLength)),
	};
	public static void WriteControl<TMemory>(ref TMemory memory, APTR address,
		in DosAnchorPathControl value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.AnchorPath.Base, value.Base.Raw);
		memory.WriteUInt32(address, DosLayout.AnchorPath.Current, value.Current.Raw);
		memory.WriteUInt32(address, DosLayout.AnchorPath.BreakBits,
			DosDateStampCodec.Unsigned(value.BreakBits));
		memory.WriteUInt32(address, DosLayout.AnchorPath.FoundBreak,
			DosDateStampCodec.Unsigned(value.FoundBreak));
		memory.WriteUInt8(address, DosLayout.AnchorPath.Flags, (byte)value.Flags);
		memory.WriteUInt8(address, DosLayout.AnchorPath.Reserved, value.Reserved);
		memory.WriteUInt16(address, DosLayout.AnchorPath.StringLength,
			unchecked((ushort)value.StringLength));
	}
	public static APTR InfoAddress(APTR address) => APTR.FromPointer(address.Raw +
		DosLayout.AnchorPath.Info);
	public static APTR BufferAddress(APTR address) => APTR.FromPointer(address.Raw +
		DosLayout.AnchorPath.PathBuffer);
}

public static class DosAChainCodec
{
	public const uint Size = AChain.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);
	public static DosAChainControl Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Child = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.AChain.Child)),
		Parent = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.AChain.Parent)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.AChain.Lock)),
		Flags = (AChainFlags)memory.ReadUInt8(address, DosLayout.AChain.Flags),
	};
	public static void WriteControl<TMemory>(ref TMemory memory, APTR address,
		in DosAChainControl value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.AChain.Child, value.Child.Raw);
		memory.WriteUInt32(address, DosLayout.AChain.Parent, value.Parent.Raw);
		memory.WriteUInt32(address, DosLayout.AChain.Lock, value.Lock.Raw);
		memory.WriteUInt8(address, DosLayout.AChain.Flags, (byte)value.Flags);
	}
	public static AChainFlags ReadFlags<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		(AChainFlags)memory.ReadUInt8(address, DosLayout.AChain.Flags);
	public static void WriteFlags<TMemory>(ref TMemory memory, APTR address,
		AChainFlags value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, DosLayout.AChain.Flags, (byte)value);
	public static bool HasAnyFlags<TMemory>(ref TMemory memory, APTR address,
		AChainFlags value) where TMemory : struct, IAmigaGuestMemory =>
		(memory.ReadUInt8(address, DosLayout.AChain.Flags) & (byte)value) != 0;
	public static void SetFlags<TMemory>(ref TMemory memory, APTR address,
		AChainFlags value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, DosLayout.AChain.Flags, unchecked((byte)(
			memory.ReadUInt8(address, DosLayout.AChain.Flags) | (byte)value)));
	public static void ClearFlags<TMemory>(ref TMemory memory, APTR address,
		AChainFlags value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, DosLayout.AChain.Flags, unchecked((byte)(
			memory.ReadUInt8(address, DosLayout.AChain.Flags) & ~(byte)value)));
	public static APTR InfoAddress(APTR address) => APTR.FromPointer(address.Raw +
		DosLayout.AChain.Info);
	public static APTR PatternAddress(APTR address) => APTR.FromPointer(address.Raw +
		DosLayout.AChain.Pattern);
}

/// <summary>Big-endian codec for the public V36+ dos.library base.</summary>
public static class DosLibraryCodec
{
	public const uint Size = DosLibrary.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1u) == 0 && memory.IsMapped(address, Size);

	public static DosLibrary Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Library = ExecLibraryCodec.Read(ref memory, address),
		Root = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosLibrary.Root)),
		GlobalVector = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosLibrary.GlobalVector)),
		BcplA2 = unchecked((int)memory.ReadUInt32(address,
			DosLayout.DosLibrary.BcplA2)),
		BcplA5 = unchecked((int)memory.ReadUInt32(address,
			DosLayout.DosLibrary.BcplA5)),
		BcplA6 = unchecked((int)memory.ReadUInt32(address,
			DosLayout.DosLibrary.BcplA6)),
		Errors = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosLibrary.Errors)),
		TimeRequest = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosLibrary.TimeRequest)),
		UtilityBase = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosLibrary.UtilityBase)),
		IntuitionBase = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosLibrary.IntuitionBase)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosLibrary value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecLibraryCodec.Write(ref memory, address, value.Library);
		WriteRoot(ref memory, address, value.Root);
		WriteGlobalVector(ref memory, address, value.GlobalVector);
		memory.WriteUInt32(address, DosLayout.DosLibrary.BcplA2,
			unchecked((uint)value.BcplA2));
		memory.WriteUInt32(address, DosLayout.DosLibrary.BcplA5,
			unchecked((uint)value.BcplA5));
		memory.WriteUInt32(address, DosLayout.DosLibrary.BcplA6,
			unchecked((uint)value.BcplA6));
		memory.WriteUInt32(address, DosLayout.DosLibrary.Errors,
			value.Errors.Raw);
		memory.WriteUInt32(address, DosLayout.DosLibrary.TimeRequest,
			value.TimeRequest.Raw);
		memory.WriteUInt32(address, DosLayout.DosLibrary.UtilityBase,
			value.UtilityBase.Raw);
		memory.WriteUInt32(address, DosLayout.DosLibrary.IntuitionBase,
			value.IntuitionBase.Raw);
	}

	public static APTR ReadRoot<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, DosLayout.DosLibrary.Root));

	public static void WriteRoot<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosLibrary.Root, value.Raw);

	public static APTR ReadGlobalVector<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, DosLayout.DosLibrary.GlobalVector));

	public static void WriteGlobalVector<TMemory>(ref TMemory memory,
		APTR address, APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosLibrary.GlobalVector, value.Raw);
}

/// <summary>Big-endian codec for the public DOS RootNode.</summary>
public static class DosRootNodeCodec
{
	public const uint Size = RootNode.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1u) == 0 && address.Raw <= uint.MaxValue - Size &&
		memory.IsMapped(address, Size);

	public static APTR CliListAddress(APTR address) => APTR.FromPointer(
		address.Raw + DosLayout.RootNode.CliList);
	public static APTR CliListTailAddress(APTR address) => APTR.FromPointer(
		CliListAddress(address).Raw + ExecLayout.MinList.Tail);

	public static BPTR ReadTaskArray<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(
		memory.ReadUInt32(address, DosLayout.RootNode.TaskArray));

	public static void WriteTaskArray<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.RootNode.TaskArray, value.Raw);

	public static MinList ReadCliList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		ReadMinList(ref memory, CliListAddress(address));

	public static void WriteCliList<TMemory>(ref TMemory memory, APTR address,
		in MinList value) where TMemory : struct, IAmigaGuestMemory =>
		WriteMinList(ref memory, CliListAddress(address), value);

	public static BPTR ReadInfo<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(
		memory.ReadUInt32(address, DosLayout.RootNode.Info));

	public static RootNode Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		TaskArray = ReadTaskArray(ref memory, address),
		ConsoleSegment = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.RootNode.ConsoleSegment)),
		Time = DosDateStampCodec.Read(ref memory, APTR.FromPointer(address.Raw +
			DosLayout.RootNode.Time)),
		RestartSegment = unchecked((int)memory.ReadUInt32(address,
			DosLayout.RootNode.RestartSegment)),
		Info = ReadInfo(ref memory, address),
		FileHandlerSegment = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.RootNode.FileHandlerSegment)),
		CliList = ReadCliList(ref memory, address),
		BootProcess = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.RootNode.BootProcess)),
		ShellSegment = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.RootNode.ShellSegment)),
		Flags = unchecked((int)memory.ReadUInt32(address,
			DosLayout.RootNode.Flags)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in RootNode value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteTaskArray(ref memory, address, value.TaskArray);
		memory.WriteUInt32(address, DosLayout.RootNode.ConsoleSegment,
			value.ConsoleSegment.Raw);
		DosDateStampCodec.Write(ref memory, APTR.FromPointer(address.Raw +
			DosLayout.RootNode.Time), value.Time);
		memory.WriteUInt32(address, DosLayout.RootNode.RestartSegment,
			unchecked((uint)value.RestartSegment));
		memory.WriteUInt32(address, DosLayout.RootNode.Info, value.Info.Raw);
		memory.WriteUInt32(address, DosLayout.RootNode.FileHandlerSegment,
			value.FileHandlerSegment.Raw);
		WriteCliList(ref memory, address, value.CliList);
		memory.WriteUInt32(address, DosLayout.RootNode.BootProcess,
			value.BootProcess.Raw);
		memory.WriteUInt32(address, DosLayout.RootNode.ShellSegment,
			value.ShellSegment.Raw);
		memory.WriteUInt32(address, DosLayout.RootNode.Flags,
			unchecked((uint)value.Flags));
	}

	/// <summary>Creates the exact empty CLI list and binds rn_Info.</summary>
	public static void Initialize<TMemory>(ref TMemory memory, APTR address,
		BPTR info) where TMemory : struct, IAmigaGuestMemory
	{
		memory.Clear(address, Size);
		memory.WriteUInt32(address, DosLayout.RootNode.Info, info.Raw);
		InitializeMinList(ref memory, CliListAddress(address));
	}

	internal static MinList ReadMinList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Head = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.MinList.Head)),
		Tail = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.MinList.Tail)),
		TailPred = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.MinList.TailPred)),
	};

	internal static void WriteMinList<TMemory>(ref TMemory memory, APTR address,
		in MinList value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.MinList.Head, value.Head.Raw);
		memory.WriteUInt32(address, ExecLayout.MinList.Tail, value.Tail.Raw);
		memory.WriteUInt32(address, ExecLayout.MinList.TailPred,
			value.TailPred.Raw);
	}

	internal static void InitializeMinList<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.MinList.Head,
			address.Raw + ExecLayout.MinList.Tail);
		memory.WriteUInt32(address, ExecLayout.MinList.Tail, 0);
		memory.WriteUInt32(address, ExecLayout.MinList.TailPred, address.Raw);
	}
}

/// <summary>Big-endian codec for the public DOS shell-table range node.</summary>
public static class DosCliProcListCodec
{
	public const uint Size = CliProcList.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1u) == 0 && address.Raw <= uint.MaxValue - Size &&
		memory.IsMapped(address, Size);

	public static CliProcList Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = new MinNode
		{
			Successor = ExecNodeCodec.ReadSuccessor(ref memory, address),
			Predecessor = ExecNodeCodec.ReadPredecessor(ref memory, address),
		},
		First = unchecked((int)memory.ReadUInt32(address,
			DosLayout.CliProcList.First)),
		Array = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.CliProcList.Array)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in CliProcList value) where TMemory : struct, IAmigaGuestMemory
	{
		ExecNodeCodec.WriteSuccessor(ref memory, address, value.Node.Successor);
		ExecNodeCodec.WritePredecessor(ref memory, address,
			value.Node.Predecessor);
		memory.WriteUInt32(address, DosLayout.CliProcList.First,
			unchecked((uint)value.First));
		memory.WriteUInt32(address, DosLayout.CliProcList.Array,
			value.Array.Raw);
	}
}

/// <summary>
/// Typed access to a public DOS shell-process array. Entry zero is the table
/// capacity; entries one through capacity contain embedded Process MsgPorts.
/// </summary>
public static class DosCliProcessArrayCodec
{
	public const uint HeaderSize = 4;
	public const uint MaximumCapacity = 4096;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (address.IsNull || (address.Raw & 3u) != 0 ||
			address.Raw > uint.MaxValue - HeaderSize ||
			!memory.IsMapped(address, HeaderSize)) return false;
		var capacity = ReadCapacity(ref memory, address);
		if (capacity == 0 || capacity > MaximumCapacity ||
			capacity > (uint.MaxValue - HeaderSize) / 4u) return false;
		var size = HeaderSize + capacity * 4u;
		return address.Raw <= uint.MaxValue - size &&
			memory.IsMapped(address, size);
	}

	public static uint ByteSize(uint capacity) => capacity == 0 ||
		capacity > MaximumCapacity ? 0 : HeaderSize + capacity * 4u;

	public static uint ReadCapacity<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt32(address, 0);

	public static void WriteCapacity<TMemory>(ref TMemory memory, APTR address,
		uint capacity) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, 0, capacity);

	public static APTR ReadProcessPort<TMemory>(ref TMemory memory, APTR address,
		uint index) where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, unchecked((int)((index + 1u) * 4u))));

	public static void WriteProcessPort<TMemory>(ref TMemory memory,
		APTR address, uint index, APTR processPort)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		unchecked((int)((index + 1u) * 4u)), processPort.Raw);
}

/// <summary>Big-endian codec for the public DOS DosInfo record.</summary>
public static class DosInfoCodec
{
	public const uint Size = DosInfo.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1u) == 0 && address.Raw <= uint.MaxValue - Size &&
		memory.IsMapped(address, Size);

	public static APTR DeviceLockAddress(APTR address) => APTR.FromPointer(
		address.Raw + DosLayout.DosInfo.DeviceLock);
	public static APTR EntryLockAddress(APTR address) => APTR.FromPointer(
		address.Raw + DosLayout.DosInfo.EntryLock);
	public static APTR DeleteLockAddress(APTR address) => APTR.FromPointer(
		address.Raw + DosLayout.DosInfo.DeleteLock);

	public static BPTR ReadDeviceInfo<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(
		memory.ReadUInt32(address, DosLayout.DosInfo.DeviceInfo));

	public static void WriteDeviceInfo<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosInfo.DeviceInfo, value.Raw);

	public static DosInfo Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		ResidentList = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DosInfo.ResidentList)),
		DeviceInfo = ReadDeviceInfo(ref memory, address),
		Devices = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DosInfo.Devices)),
		Handlers = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DosInfo.Handlers)),
		NetworkHandler = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosInfo.NetworkHandler)),
		DeviceLock = ReadSignalSemaphore(ref memory, DeviceLockAddress(address)),
		EntryLock = ReadSignalSemaphore(ref memory, EntryLockAddress(address)),
		DeleteLock = ReadSignalSemaphore(ref memory, DeleteLockAddress(address)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DosInfo.ResidentList,
			value.ResidentList.Raw);
		WriteDeviceInfo(ref memory, address, value.DeviceInfo);
		memory.WriteUInt32(address, DosLayout.DosInfo.Devices, value.Devices.Raw);
		memory.WriteUInt32(address, DosLayout.DosInfo.Handlers,
			value.Handlers.Raw);
		memory.WriteUInt32(address, DosLayout.DosInfo.NetworkHandler,
			value.NetworkHandler.Raw);
		WriteSignalSemaphore(ref memory, DeviceLockAddress(address),
			value.DeviceLock);
		WriteSignalSemaphore(ref memory, EntryLockAddress(address),
			value.EntryLock);
		WriteSignalSemaphore(ref memory, DeleteLockAddress(address),
			value.DeleteLock);
	}

	/// <summary>Initializes all three public semaphores as unlocked.</summary>
	public static void Initialize<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.Clear(address, Size);
		InitializeSignalSemaphore(ref memory, DeviceLockAddress(address));
		InitializeSignalSemaphore(ref memory, EntryLockAddress(address));
		InitializeSignalSemaphore(ref memory, DeleteLockAddress(address));
	}

	private static SignalSemaphore ReadSignalSemaphore<TMemory>(
		ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Link = ExecNodeCodec.Read(ref memory, address),
		NestCount = unchecked((short)memory.ReadUInt16(address,
			ExecLayout.SignalSemaphore.NestCount)),
		WaitQueue = DosRootNodeCodec.ReadMinList(ref memory, APTR.FromPointer(
			address.Raw + ExecLayout.SignalSemaphore.WaitQueue)),
		MultipleLink = new SemaphoreRequest
		{
			Link = new MinNode
			{
				Successor = APTR.FromPointer(memory.ReadUInt32(address,
					ExecLayout.SignalSemaphore.MultipleLink)),
				Predecessor = APTR.FromPointer(memory.ReadUInt32(address,
					ExecLayout.SignalSemaphore.MultipleLink + 4)),
			},
			Waiter = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.SignalSemaphore.MultipleLink +
				unchecked((int)MinNode.Size))),
		},
		Owner = APTR.FromPointer(memory.ReadUInt32(address,
			ExecLayout.SignalSemaphore.Owner)),
		QueueCount = unchecked((short)memory.ReadUInt16(address,
			ExecLayout.SignalSemaphore.QueueCount)),
	};

	private static void WriteSignalSemaphore<TMemory>(ref TMemory memory,
		APTR address, SignalSemaphore value)
		where TMemory : struct, IAmigaGuestMemory
	{
		ExecNodeCodec.Write(ref memory, address, value.Link);
		memory.WriteUInt16(address, ExecLayout.SignalSemaphore.NestCount,
			unchecked((ushort)value.NestCount));
		DosRootNodeCodec.WriteMinList(ref memory, APTR.FromPointer(address.Raw +
			ExecLayout.SignalSemaphore.WaitQueue), value.WaitQueue);
		memory.WriteUInt32(address, ExecLayout.SignalSemaphore.MultipleLink,
			value.MultipleLink.Link.Successor.Raw);
		memory.WriteUInt32(address, ExecLayout.SignalSemaphore.MultipleLink + 4,
			value.MultipleLink.Link.Predecessor.Raw);
		memory.WriteUInt32(address, ExecLayout.SignalSemaphore.MultipleLink +
			unchecked((int)MinNode.Size), value.MultipleLink.Waiter.Raw);
		memory.WriteUInt32(address, ExecLayout.SignalSemaphore.Owner,
			value.Owner.Raw);
		memory.WriteUInt16(address, ExecLayout.SignalSemaphore.QueueCount,
			unchecked((ushort)value.QueueCount));
	}

	private static void InitializeSignalSemaphore<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, ExecLayout.SignalSemaphore.NestCount, 0);
		DosRootNodeCodec.InitializeMinList(ref memory, APTR.FromPointer(
			address.Raw + ExecLayout.SignalSemaphore.WaitQueue));
		memory.WriteUInt32(address, ExecLayout.SignalSemaphore.Owner, 0);
		memory.WriteUInt16(address, ExecLayout.SignalSemaphore.QueueCount, 0);
	}
}

/// <summary>Big-endian scalar storage for MorphOS GetFileSysAttr results.</summary>
public static class DosFileSystemAttributeCodec
{
	public const uint LongSize = 4;
	public const uint QuadSize = 8;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address,
		uint size) where TMemory : struct, IAmigaGuestMemory =>
		(size == LongSize || size == QuadSize) && address.IsNotNull &&
		(address.Raw & 1u) == 0 && address.Raw <= uint.MaxValue - size &&
		memory.IsMapped(address, size);

	public static void WriteLong<TMemory>(ref TMemory memory, APTR address,
		uint value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, 0, value);

	public static void WriteQuad<TMemory>(ref TMemory memory, APTR address,
		ulong value) where TMemory : struct, IAmigaGuestMemory =>
		DosPosixDateStampCodec.WriteUInt64(ref memory, address, 0, value);
}

/// <summary>Big-endian guest-memory codec for <see cref="DateStamp"/>.</summary>
public static class DosDateStampCodec
{
	public const uint Size = DateStamp.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => Valid(ref memory, address, Size, 2);

	public static DateStamp Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Days = Signed(memory.ReadUInt32(address, DosLayout.DateStamp.Days)),
			Minutes = Signed(memory.ReadUInt32(address, DosLayout.DateStamp.Minutes)),
			Ticks = Signed(memory.ReadUInt32(address, DosLayout.DateStamp.Ticks)),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address, in DateStamp value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DateStamp.Days, Unsigned(value.Days));
		memory.WriteUInt32(address, DosLayout.DateStamp.Minutes, Unsigned(value.Minutes));
		memory.WriteUInt32(address, DosLayout.DateStamp.Ticks, Unsigned(value.Ticks));
	}

	internal static bool Valid<TMemory>(ref TMemory memory, APTR address, uint size,
		uint alignment) where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & (alignment - 1)) == 0 &&
		address.Raw <= uint.MaxValue - size && memory.IsMapped(address, size);

	internal static int Signed(uint value) => unchecked((int)value);
	internal static uint Unsigned(int value) => unchecked((uint)value);
}

/// <summary>Big-endian guest-memory codec for the packed DateTime record.</summary>
public static class DosDateTimeCodec
{
	public const uint Size = DosDateTime.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static DosDateTime Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Stamp = DosDateStampCodec.Read(ref memory, address),
		Format = (DosDateFormat)memory.ReadUInt8(address,
			DosLayout.DateTime.Format),
		Flags = (DosDateTimeFlags)memory.ReadUInt8(address,
			DosLayout.DateTime.Flags),
		Day = STRPTR.FromPointer(memory.ReadUInt32(address, DosLayout.DateTime.Day)),
		Date = STRPTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DateTime.Date)),
		Time = STRPTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DateTime.Time)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosDateTime value) where TMemory : struct, IAmigaGuestMemory
	{
		DosDateStampCodec.Write(ref memory, address, value.Stamp);
		memory.WriteUInt8(address, DosLayout.DateTime.Format, (byte)value.Format);
		memory.WriteUInt8(address, DosLayout.DateTime.Flags, (byte)value.Flags);
		memory.WriteUInt32(address, DosLayout.DateTime.Day, value.Day.Address.Raw);
		memory.WriteUInt32(address, DosLayout.DateTime.Date, value.Date.Address.Raw);
		memory.WriteUInt32(address, DosLayout.DateTime.Time, value.Time.Address.Raw);
	}
}

public static class DosEnvecCodec
{
	public const uint Size = DosEnvec.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static DosEnvec Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		TableSize = U(ref memory, address, DosLayout.DosEnvec.TableSize),
		SizeBlock = U(ref memory, address, DosLayout.DosEnvec.SizeBlock),
		SectorOrigin = U(ref memory, address, DosLayout.DosEnvec.SectorOrigin),
		Surfaces = U(ref memory, address, DosLayout.DosEnvec.Surfaces),
		SectorsPerBlock = U(ref memory, address, DosLayout.DosEnvec.SectorsPerBlock),
		BlocksPerTrack = U(ref memory, address, DosLayout.DosEnvec.BlocksPerTrack),
		Reserved = U(ref memory, address, DosLayout.DosEnvec.Reserved),
		PreAlloc = U(ref memory, address, DosLayout.DosEnvec.PreAlloc),
		Interleave = U(ref memory, address, DosLayout.DosEnvec.Interleave),
		LowCylinder = U(ref memory, address, DosLayout.DosEnvec.LowCylinder),
		HighCylinder = U(ref memory, address, DosLayout.DosEnvec.HighCylinder),
		NumberOfBuffers = U(ref memory, address,
			DosLayout.DosEnvec.NumberOfBuffers),
		BufferMemoryType = U(ref memory, address,
			DosLayout.DosEnvec.BufferMemoryType),
		MaximumTransfer = U(ref memory, address,
			DosLayout.DosEnvec.MaximumTransfer),
		Mask = U(ref memory, address, DosLayout.DosEnvec.Mask),
		BootPriority = DosDateStampCodec.Signed(U(ref memory, address,
			DosLayout.DosEnvec.BootPriority)),
		DosType = U(ref memory, address, DosLayout.DosEnvec.DosType),
		Baud = U(ref memory, address, DosLayout.DosEnvec.Baud),
		Control = U(ref memory, address, DosLayout.DosEnvec.Control),
		BootBlocks = U(ref memory, address, DosLayout.DosEnvec.BootBlocks),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosEnvec value) where TMemory : struct, IAmigaGuestMemory
	{
		W(ref memory, address, DosLayout.DosEnvec.TableSize, value.TableSize);
		W(ref memory, address, DosLayout.DosEnvec.SizeBlock, value.SizeBlock);
		W(ref memory, address, DosLayout.DosEnvec.SectorOrigin, value.SectorOrigin);
		W(ref memory, address, DosLayout.DosEnvec.Surfaces, value.Surfaces);
		W(ref memory, address, DosLayout.DosEnvec.SectorsPerBlock,
			value.SectorsPerBlock);
		W(ref memory, address, DosLayout.DosEnvec.BlocksPerTrack,
			value.BlocksPerTrack);
		W(ref memory, address, DosLayout.DosEnvec.Reserved, value.Reserved);
		W(ref memory, address, DosLayout.DosEnvec.PreAlloc, value.PreAlloc);
		W(ref memory, address, DosLayout.DosEnvec.Interleave, value.Interleave);
		W(ref memory, address, DosLayout.DosEnvec.LowCylinder, value.LowCylinder);
		W(ref memory, address, DosLayout.DosEnvec.HighCylinder, value.HighCylinder);
		W(ref memory, address, DosLayout.DosEnvec.NumberOfBuffers,
			value.NumberOfBuffers);
		W(ref memory, address, DosLayout.DosEnvec.BufferMemoryType,
			value.BufferMemoryType);
		W(ref memory, address, DosLayout.DosEnvec.MaximumTransfer,
			value.MaximumTransfer);
		W(ref memory, address, DosLayout.DosEnvec.Mask, value.Mask);
		W(ref memory, address, DosLayout.DosEnvec.BootPriority,
			DosDateStampCodec.Unsigned(value.BootPriority));
		W(ref memory, address, DosLayout.DosEnvec.DosType, value.DosType);
		W(ref memory, address, DosLayout.DosEnvec.Baud, value.Baud);
		W(ref memory, address, DosLayout.DosEnvec.Control, value.Control);
		W(ref memory, address, DosLayout.DosEnvec.BootBlocks, value.BootBlocks);
	}

	private static uint U<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt32(address, offset);
	private static void W<TMemory>(ref TMemory memory, APTR address, int offset,
		uint value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, value);
}

public static class DosFileSysStartupMsgCodec
{
	public const uint Size = FileSysStartupMsg.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static FileSysStartupMsg Read<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Unit = memory.ReadUInt32(address, DosLayout.FileSysStartupMsg.Unit),
		Device = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.FileSysStartupMsg.Device)),
		Environment = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.FileSysStartupMsg.Environment)),
		Flags = memory.ReadUInt32(address, DosLayout.FileSysStartupMsg.Flags),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in FileSysStartupMsg value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.FileSysStartupMsg.Unit, value.Unit);
		memory.WriteUInt32(address, DosLayout.FileSysStartupMsg.Device,
			value.Device.Raw);
		memory.WriteUInt32(address, DosLayout.FileSysStartupMsg.Environment,
			value.Environment.Raw);
		memory.WriteUInt32(address, DosLayout.FileSysStartupMsg.Flags, value.Flags);
	}
}

/// <summary>Big-endian guest-memory codec for a MorphOS POSIX timestamp.</summary>
public static class DosPosixDateStampCodec
{
	public const uint Size = PosixDateStamp.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static PosixDateStamp Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Seconds = unchecked((long)ReadUInt64(ref memory, address,
				DosLayout.PosixDateStamp.Seconds)),
			Nanoseconds = DosDateStampCodec.Signed(memory.ReadUInt32(address,
				DosLayout.PosixDateStamp.Nanoseconds)),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in PosixDateStamp value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteUInt64(ref memory, address, DosLayout.PosixDateStamp.Seconds,
			unchecked((ulong)value.Seconds));
		memory.WriteUInt32(address, DosLayout.PosixDateStamp.Nanoseconds,
			DosDateStampCodec.Unsigned(value.Nanoseconds));
	}

	internal static ulong ReadUInt64<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory
	{
		var high = memory.ReadUInt32(address, offset);
		var low = memory.ReadUInt32(address, offset + 4);
		return unchecked((ulong)CopperSharp.Compiler.M68kRuntime.CombineInt64(
			high, low));
	}

	internal static void WriteUInt64<TMemory>(ref TMemory memory, APTR address,
		int offset, ulong value) where TMemory : struct, IAmigaGuestMemory
	{
		var low = CopperSharp.Compiler.M68kRuntime.SplitUInt64(value, out var high);
		memory.WriteUInt32(address, offset, high);
		memory.WriteUInt32(address, offset + 4, low);
	}
}

public static class DosCSourceCodec
{
	public const uint Size = CSource.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static CSource Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Buffer = APTR.FromPointer(memory.ReadUInt32(address,
				DosLayout.CSource.Buffer)),
			Length = DosDateStampCodec.Signed(memory.ReadUInt32(address,
				DosLayout.CSource.Length)),
			CurrentCharacter = DosDateStampCodec.Signed(memory.ReadUInt32(address,
				DosLayout.CSource.CurrentCharacter)),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in CSource value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.CSource.Buffer, value.Buffer.Raw);
		memory.WriteUInt32(address, DosLayout.CSource.Length,
			DosDateStampCodec.Unsigned(value.Length));
		memory.WriteUInt32(address, DosLayout.CSource.CurrentCharacter,
			DosDateStampCodec.Unsigned(value.CurrentCharacter));
	}

	public static void WriteCurrentCharacter<TMemory>(ref TMemory memory,
		APTR address, int value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.CSource.CurrentCharacter,
			DosDateStampCodec.Unsigned(value));
}

public static class DosRdArgsCodec
{
	public const uint Size = RDArgs.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static RDArgs Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Source = DosCSourceCodec.Read(ref memory, address),
		AllocationList = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.RDArgs.AllocationList)),
		Buffer = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.RDArgs.Buffer)),
		BufferSize = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.RDArgs.BufferSize)),
		ExtendedHelp = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.RDArgs.ExtendedHelp)),
		Flags = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.RDArgs.Flags)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in RDArgs value) where TMemory : struct, IAmigaGuestMemory
	{
		DosCSourceCodec.Write(ref memory, address, value.Source);
		memory.WriteUInt32(address, DosLayout.RDArgs.AllocationList,
			value.AllocationList.Raw);
		memory.WriteUInt32(address, DosLayout.RDArgs.Buffer, value.Buffer.Raw);
		memory.WriteUInt32(address, DosLayout.RDArgs.BufferSize,
			DosDateStampCodec.Unsigned(value.BufferSize));
		memory.WriteUInt32(address, DosLayout.RDArgs.ExtendedHelp,
			value.ExtendedHelp.Raw);
		memory.WriteUInt32(address, DosLayout.RDArgs.Flags,
			DosDateStampCodec.Unsigned(value.Flags));
	}
}

public static class DosRecordLockCodec
{
	public const uint Size = RecordLock.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static RecordLock Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		File = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.RecordLock.File)),
		Offset = memory.ReadUInt32(address, DosLayout.RecordLock.Offset),
		Length = memory.ReadUInt32(address, DosLayout.RecordLock.Length),
		Mode = memory.ReadUInt32(address, DosLayout.RecordLock.Mode),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in RecordLock value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.RecordLock.File, value.File.Raw);
		memory.WriteUInt32(address, DosLayout.RecordLock.Offset, value.Offset);
		memory.WriteUInt32(address, DosLayout.RecordLock.Length, value.Length);
		memory.WriteUInt32(address, DosLayout.RecordLock.Mode, value.Mode);
	}
}

/// <summary>Big-endian guest-memory codec for a MorphOS 64-bit record lock.</summary>
public static class DosRecordLock64Codec
{
	public const uint Size = RecordLock64.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static RecordLock64 Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			File = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.RecordLock64.File)),
			Offset = DosPosixDateStampCodec.ReadUInt64(ref memory, address,
				DosLayout.RecordLock64.Offset),
			Length = DosPosixDateStampCodec.ReadUInt64(ref memory, address,
				DosLayout.RecordLock64.Length),
			Mode = memory.ReadUInt32(address, DosLayout.RecordLock64.Mode),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in RecordLock64 value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.RecordLock64.File, value.File.Raw);
		DosPosixDateStampCodec.WriteUInt64(ref memory, address,
			DosLayout.RecordLock64.Offset, value.Offset);
		DosPosixDateStampCodec.WriteUInt64(ref memory, address,
			DosLayout.RecordLock64.Length, value.Length);
		memory.WriteUInt32(address, DosLayout.RecordLock64.Mode, value.Mode);
	}
}

/// <summary>Big-endian guest-memory codec for a public AmigaDOS packet.</summary>
public static class DosPacketCodec
{
	public const uint Size = DosPacket.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static DosPacket Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Link = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DosPacket.Link)),
			Port = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DosPacket.Port)),
			Type = S(ref memory, address, DosLayout.DosPacket.Type),
			Result1 = S(ref memory, address, DosLayout.DosPacket.Result1),
			Result2 = S(ref memory, address, DosLayout.DosPacket.Result2),
			Argument1 = S(ref memory, address, DosLayout.DosPacket.Argument1),
			Argument2 = S(ref memory, address, DosLayout.DosPacket.Argument2),
			Argument3 = S(ref memory, address, DosLayout.DosPacket.Argument3),
			Argument4 = S(ref memory, address, DosLayout.DosPacket.Argument4),
			Argument5 = S(ref memory, address, DosLayout.DosPacket.Argument5),
			Argument6 = S(ref memory, address, DosLayout.DosPacket.Argument6),
			Argument7 = S(ref memory, address, DosLayout.DosPacket.Argument7),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address, in DosPacket value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DosPacket.Link, value.Link.Raw);
		memory.WriteUInt32(address, DosLayout.DosPacket.Port, value.Port.Raw);
		W(ref memory, address, DosLayout.DosPacket.Type, value.Type);
		W(ref memory, address, DosLayout.DosPacket.Result1, value.Result1);
		W(ref memory, address, DosLayout.DosPacket.Result2, value.Result2);
		W(ref memory, address, DosLayout.DosPacket.Argument1, value.Argument1);
		W(ref memory, address, DosLayout.DosPacket.Argument2, value.Argument2);
		W(ref memory, address, DosLayout.DosPacket.Argument3, value.Argument3);
		W(ref memory, address, DosLayout.DosPacket.Argument4, value.Argument4);
		W(ref memory, address, DosLayout.DosPacket.Argument5, value.Argument5);
		W(ref memory, address, DosLayout.DosPacket.Argument6, value.Argument6);
		W(ref memory, address, DosLayout.DosPacket.Argument7, value.Argument7);
	}

	public static void WriteResults<TMemory>(ref TMemory memory, APTR address,
		int result1, int result2) where TMemory : struct, IAmigaGuestMemory
	{
		W(ref memory, address, DosLayout.DosPacket.Result1, result1);
		W(ref memory, address, DosLayout.DosPacket.Result2, result2);
	}

	private static int S<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Signed(memory.ReadUInt32(address, offset));
	private static void W<TMemory>(ref TMemory memory, APTR address, int offset, int value)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, DosDateStampCodec.Unsigned(value));
}

public static class DosStandardPacketCodec
{
	public const uint Size = StandardPacket.Size;
	public const uint PacketOffset = DosLayout.StandardPacket.Packet;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static APTR MessageAddress(APTR address) => address;
	public static APTR PacketAddress(APTR address) =>
		APTR.FromPointer(address.Raw + DosLayout.StandardPacket.Packet);
}

/// <summary>Big-endian guest-memory codec for ExAllControl.</summary>
public static class DosExAllControlCodec
{
	public const uint Size = ExAllControl.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static ExAllControl Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Entries = memory.ReadUInt32(address, DosLayout.ExAllControl.Entries),
		LastKey = memory.ReadUInt32(address, DosLayout.ExAllControl.LastKey),
		MatchString = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.ExAllControl.MatchString)),
		MatchFunction = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.ExAllControl.MatchFunction)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in ExAllControl value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.ExAllControl.Entries, value.Entries);
		memory.WriteUInt32(address, DosLayout.ExAllControl.LastKey, value.LastKey);
		memory.WriteUInt32(address, DosLayout.ExAllControl.MatchString,
			value.MatchString.Raw);
		memory.WriteUInt32(address, DosLayout.ExAllControl.MatchFunction,
			value.MatchFunction.Raw);
	}
}

/// <summary>
/// Encoder for the progressive, variable-sized ExAllData wire record.
/// All numeric layout knowledge for this record is confined to this codec.
/// </summary>
public static class DosExAllDataCodec
{
	public static APTR InlineNameAddress(APTR record, DosExAllDataLevel level) =>
		APTR.FromPointer(record.Raw + PrefixSize(level));

	public static ExAllData Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.ExAllData.Next)),
		Name = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.ExAllData.Name)),
		Type = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.ExAllData.Type)),
		FileSize = memory.ReadUInt32(address, DosLayout.ExAllData.FileSize),
		Protection = memory.ReadUInt32(address, DosLayout.ExAllData.Protection),
		Days = memory.ReadUInt32(address, DosLayout.ExAllData.Days),
		Minutes = memory.ReadUInt32(address, DosLayout.ExAllData.Minutes),
		Ticks = memory.ReadUInt32(address, DosLayout.ExAllData.Ticks),
		Comment = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.ExAllData.Comment)),
		OwnerUid = memory.ReadUInt16(address, DosLayout.ExAllData.OwnerUid),
		OwnerGid = memory.ReadUInt16(address, DosLayout.ExAllData.OwnerGid),
	};

	public static uint PrefixSize(DosExAllDataLevel level) => level switch
	{
		DosExAllDataLevel.Name => 8,
		DosExAllDataLevel.Type => 12,
		DosExAllDataLevel.Size => 16,
		DosExAllDataLevel.Protection => 20,
		DosExAllDataLevel.Date => 32,
		DosExAllDataLevel.Comment => 36,
		DosExAllDataLevel.Owner => ExAllData.Size,
		_ => 0,
	};

	public static bool TryWriteFromFileInfo<TMemory>(ref TMemory memory,
		APTR destination, APTR publicAddress, uint capacity, APTR fileInfoBlock,
		DosExAllDataLevel level, out uint bytesWritten)
		where TMemory : struct, IAmigaGuestMemory
	{
		bytesWritten = 0;
		var prefix = PrefixSize(level);
		if (prefix == 0 || !DosFileInfoBlockCodec.IsMapped(ref memory, fileInfoBlock))
			return false;
		var nameLength = DosFileInfoBlockCodec.ReadFileNameLength(ref memory,
			fileInfoBlock);
		var commentLength = level >= DosExAllDataLevel.Comment
			? DosFileInfoBlockCodec.ReadCommentLength(ref memory, fileInfoBlock) : 0u;
		var required = prefix + nameLength + 1u;
		if (commentLength != 0) required += commentLength + 1u;
		required = (required + 1u) & ~1u;
		if (destination.IsNull || destination.Raw > uint.MaxValue - required ||
			required > capacity || !memory.IsMapped(destination, required)) return false;

		memory.Clear(destination, required);
		if (publicAddress.Raw > uint.MaxValue - required) return false;
		var name = APTR.FromPointer(destination.Raw + prefix);
		var publicName = APTR.FromPointer(publicAddress.Raw + prefix);
		memory.WriteUInt32(destination, DosLayout.ExAllData.Name, publicName.Raw);
		for (var i = 0u; i < nameLength; i++) memory.WriteUInt8(name, (int)i,
			DosFileInfoBlockCodec.ReadFileNameByte(ref memory, fileInfoBlock, i));
		if (level >= DosExAllDataLevel.Type) memory.WriteUInt32(destination,
			DosLayout.ExAllData.Type, DosDateStampCodec.Unsigned(
				DosFileInfoBlockCodec.ReadDirEntryType(ref memory, fileInfoBlock)));
		if (level >= DosExAllDataLevel.Size) memory.WriteUInt32(destination,
			DosLayout.ExAllData.FileSize, unchecked((uint)
				DosFileInfoBlockCodec.ReadSize(ref memory, fileInfoBlock)));
		if (level >= DosExAllDataLevel.Protection) memory.WriteUInt32(destination,
			DosLayout.ExAllData.Protection, unchecked((uint)
				DosFileInfoBlockCodec.ReadProtection(ref memory, fileInfoBlock)));
		if (level >= DosExAllDataLevel.Date)
		{
			var date = DosFileInfoBlockCodec.ReadDate(ref memory, fileInfoBlock);
			memory.WriteUInt32(destination, DosLayout.ExAllData.Days,
				DosDateStampCodec.Unsigned(date.Days));
			memory.WriteUInt32(destination, DosLayout.ExAllData.Minutes,
				DosDateStampCodec.Unsigned(date.Minutes));
			memory.WriteUInt32(destination, DosLayout.ExAllData.Ticks,
				DosDateStampCodec.Unsigned(date.Ticks));
		}
		if (level >= DosExAllDataLevel.Comment && commentLength != 0)
		{
			var comment = APTR.FromPointer(name.Raw + nameLength + 1u);
			var publicComment = APTR.FromPointer(publicName.Raw + nameLength + 1u);
			memory.WriteUInt32(destination, DosLayout.ExAllData.Comment,
				publicComment.Raw);
			for (var i = 0u; i < commentLength; i++) memory.WriteUInt8(comment, (int)i,
				DosFileInfoBlockCodec.ReadCommentByte(ref memory, fileInfoBlock, i));
		}
		if (level >= DosExAllDataLevel.Owner)
		{
			var owner = DosFileInfoBlockCodec.ReadOwner(ref memory, fileInfoBlock);
			memory.WriteUInt16(destination, DosLayout.ExAllData.OwnerUid,
				unchecked((ushort)(owner >> 16)));
			memory.WriteUInt16(destination, DosLayout.ExAllData.OwnerGid,
				unchecked((ushort)owner));
		}
		bytesWritten = required;
		return true;
	}

	public static void WriteNext<TMemory>(ref TMemory memory, APTR record,
		APTR next) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(record, DosLayout.ExAllData.Next, next.Raw);

	/// <summary>Relocates inline string pointers when a packed record is copied.</summary>
	public static void RelocateInlinePointers<TMemory>(ref TMemory memory,
		APTR record, APTR publicAddress, DosExAllDataLevel level)
		where TMemory : struct, IAmigaGuestMemory
	{
		var prefix = PrefixSize(level);
		if (prefix == 0) return;
		memory.WriteUInt32(record, DosLayout.ExAllData.Name,
			publicAddress.Raw + prefix);
		if (level < DosExAllDataLevel.Comment) return;
		var comment = APTR.FromPointer(memory.ReadUInt32(record,
			DosLayout.ExAllData.Comment));
		if (comment.IsNotNull)
			memory.WriteUInt32(record, DosLayout.ExAllData.Comment,
				publicAddress.Raw + (comment.Raw - record.Raw));
	}
}

public static class DosInfoDataCodec
{
	public const uint Size = InfoData.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static InfoData Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NumberOfSoftErrors = S(ref memory, address,
			DosLayout.InfoData.NumberOfSoftErrors),
		UnitNumber = S(ref memory, address, DosLayout.InfoData.UnitNumber),
		DiskState = S(ref memory, address, DosLayout.InfoData.DiskState),
		NumberOfBlocks = S(ref memory, address, DosLayout.InfoData.NumberOfBlocks),
		NumberOfBlocksUsed = S(ref memory, address,
			DosLayout.InfoData.NumberOfBlocksUsed),
		BytesPerBlock = S(ref memory, address, DosLayout.InfoData.BytesPerBlock),
		DiskType = S(ref memory, address, DosLayout.InfoData.DiskType),
		VolumeNode = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.InfoData.VolumeNode)),
		InUse = S(ref memory, address, DosLayout.InfoData.InUse),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in InfoData value) where TMemory : struct, IAmigaGuestMemory
	{
		W(ref memory, address, DosLayout.InfoData.NumberOfSoftErrors,
			value.NumberOfSoftErrors);
		W(ref memory, address, DosLayout.InfoData.UnitNumber, value.UnitNumber);
		W(ref memory, address, DosLayout.InfoData.DiskState, value.DiskState);
		W(ref memory, address, DosLayout.InfoData.NumberOfBlocks,
			value.NumberOfBlocks);
		W(ref memory, address, DosLayout.InfoData.NumberOfBlocksUsed,
			value.NumberOfBlocksUsed);
		W(ref memory, address, DosLayout.InfoData.BytesPerBlock,
			value.BytesPerBlock);
		W(ref memory, address, DosLayout.InfoData.DiskType, value.DiskType);
		memory.WriteUInt32(address, DosLayout.InfoData.VolumeNode,
			value.VolumeNode.Raw);
		W(ref memory, address, DosLayout.InfoData.InUse, value.InUse);
	}

	private static int S<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Signed(
		memory.ReadUInt32(address, offset));
	private static void W<TMemory>(ref TMemory memory, APTR address, int offset,
		int value) where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, offset, DosDateStampCodec.Unsigned(value));
}

public static class DosFileLockCodec
{
	public const uint Size = FileLock.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static FileLock Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			Link = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.FileLock.Link)),
			Key = DosDateStampCodec.Signed(memory.ReadUInt32(address, DosLayout.FileLock.Key)),
			Access = DosDateStampCodec.Signed(memory.ReadUInt32(address, DosLayout.FileLock.Access)),
			Task = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.FileLock.Task)),
			Volume = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.FileLock.Volume)),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address, in FileLock value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.FileLock.Link, value.Link.Raw);
		memory.WriteUInt32(address, DosLayout.FileLock.Key,
			DosDateStampCodec.Unsigned(value.Key));
		memory.WriteUInt32(address, DosLayout.FileLock.Access,
			DosDateStampCodec.Unsigned(value.Access));
		memory.WriteUInt32(address, DosLayout.FileLock.Task, value.Task.Raw);
		memory.WriteUInt32(address, DosLayout.FileLock.Volume, value.Volume.Raw);
	}
}

public static class DosFileInfoBlockCodec
{
	public const uint Size = FileInfoBlock.SizeInBytes;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);
	public static int ReadDiskKey<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Signed(
		memory.ReadUInt32(address, FileInfoBlock.DiskKeyOffset));
	public static void WriteDiskKey<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, FileInfoBlock.DiskKeyOffset, DosDateStampCodec.Unsigned(value));
	public static void WriteDirEntryType<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, FileInfoBlock.DirEntryTypeOffset, DosDateStampCodec.Unsigned(value));
	public static int ReadDirEntryType<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Signed(
		memory.ReadUInt32(address, FileInfoBlock.DirEntryTypeOffset));
	public static int ReadProtection<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Signed(
		memory.ReadUInt32(address, FileInfoBlock.ProtectionOffset));
	public static void WriteProtection<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, FileInfoBlock.ProtectionOffset, DosDateStampCodec.Unsigned(value));
	public static int ReadSize<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Signed(
		memory.ReadUInt32(address, FileInfoBlock.SizeOffset));
	public static void WriteSize<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, FileInfoBlock.SizeOffset, DosDateStampCodec.Unsigned(value));
	public static int ReadNumBlocks<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Signed(
		memory.ReadUInt32(address, 128));
	public static APTR FileNameAddress(APTR address) => APTR.FromPointer(
		address.Raw + FileInfoBlock.FileNameOffset);
	public static DateStamp ReadDate<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => DosDateStampCodec.Read(
		ref memory, APTR.FromPointer(address.Raw + FileInfoBlock.DateDaysOffset));
	public static uint ReadOwner<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		FileInfoBlock.OwnerOffset);
	public static void WriteOwner<TMemory>(ref TMemory memory, APTR address,
		uint value) where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, FileInfoBlock.OwnerOffset, value);
	public static uint ReadFileNameLength<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		FileInfoBlock.FileNameOffset);
	public static void WriteFileNameLength<TMemory>(ref TMemory memory,
		APTR address, byte length) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, FileInfoBlock.FileNameOffset, length);
	public static void WriteFileNameByte<TMemory>(ref TMemory memory, APTR address,
		uint index, byte value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, FileInfoBlock.FileNameOffset + 1 + (int)index,
			value);
	public static byte ReadFileNameByte<TMemory>(ref TMemory memory, APTR address,
		uint index) where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(
		address, FileInfoBlock.FileNameOffset + 1 + (int)index);
	public static bool WriteFileName<TMemory>(ref TMemory memory, APTR address,
		APTR source, uint length) where TMemory : struct, IAmigaGuestMemory
	{
		if (length > 107 || source.IsNull || !memory.IsMapped(source,
			length == 0 ? 1u : length)) return false;
		memory.WriteUInt8(address, FileInfoBlock.FileNameOffset,
			unchecked((byte)length));
		for (var i = 0u; i < length; i++) memory.WriteUInt8(address,
			FileInfoBlock.FileNameOffset + 1 + (int)i, memory.ReadUInt8(source,
				(int)i));
		return true;
	}
	public static uint ReadCommentLength<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		FileInfoBlock.CommentOffset);
	public static byte ReadCommentByte<TMemory>(ref TMemory memory, APTR address,
		uint index) where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(
		address, FileInfoBlock.CommentOffset + 1 + (int)index);
	public static bool WriteComment<TMemory>(ref TMemory memory, APTR address,
		APTR source, uint length) where TMemory : struct, IAmigaGuestMemory
	{
		if (length > 79 || source.IsNull || !memory.IsMapped(source,
			length == 0 ? 1u : length)) return false;
		memory.WriteUInt8(address, FileInfoBlock.CommentOffset,
			unchecked((byte)length));
		for (var i = 0u; i < length; i++) memory.WriteUInt8(address,
			FileInfoBlock.CommentOffset + 1 + (int)i, memory.ReadUInt8(source,
				(int)i));
		return true;
	}
	public static void WriteSize64<TMemory>(ref TMemory memory, APTR address,
		ulong value) where TMemory : struct, IAmigaGuestMemory =>
		DosPosixDateStampCodec.WriteUInt64(ref memory, address,
			FileInfoBlock.Size64Offset, value);
	public static void WriteNumBlocks64<TMemory>(ref TMemory memory, APTR address,
		ulong value) where TMemory : struct, IAmigaGuestMemory =>
		DosPosixDateStampCodec.WriteUInt64(ref memory, address,
			FileInfoBlock.NumBlocks64Offset, value);
	public static void WriteRequestedExtensionFlags<TMemory>(ref TMemory memory,
		APTR address, byte value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, FileInfoBlock.RequestedExtensionFlagsOffset, value);
	public static void WriteActualExtensionFlags<TMemory>(ref TMemory memory,
		APTR address, byte value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, FileInfoBlock.ActualExtensionFlagsOffset, value);
	public static void WritePosixDateOverlay<TMemory>(ref TMemory memory,
		APTR address, ulong seconds, int nanoseconds)
		where TMemory : struct, IAmigaGuestMemory
	{
		DosPosixDateStampCodec.WriteUInt64(ref memory, address,
			FileInfoBlock.DateDaysOffset, seconds);
		memory.WriteUInt32(address, FileInfoBlock.DateTickOffset,
			DosDateStampCodec.Unsigned(nanoseconds));
	}
}

public static class DosAttrBufferCodec
{
	public const uint Size = DosAttrBuffer.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static DosAttrBuffer Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Pointer = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosAttrBuffer.Pointer)),
		Length = memory.ReadUInt32(address, DosLayout.DosAttrBuffer.Length),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosAttrBuffer value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DosAttrBuffer.Pointer,
			value.Pointer.Raw);
		memory.WriteUInt32(address, DosLayout.DosAttrBuffer.Length, value.Length);
	}

	public static void WriteLength<TMemory>(ref TMemory memory, APTR address,
		uint length) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosAttrBuffer.Length, length);
}

public static class DosFileHandleCodec
{
	public const uint Size = FileHandle.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static FileHandle Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Link = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.FileHandle.Link)),
		Port = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.FileHandle.Port)),
		Type = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.FileHandle.Type)),
		Buffer = S(ref memory, address, DosLayout.FileHandle.Buffer),
		Position = S(ref memory, address, DosLayout.FileHandle.Position),
		End = S(ref memory, address, DosLayout.FileHandle.End),
		Functions = S(ref memory, address, DosLayout.FileHandle.Functions),
		Function2 = S(ref memory, address, DosLayout.FileHandle.Function2),
		Function3 = S(ref memory, address, DosLayout.FileHandle.Function3),
		Arguments = S(ref memory, address, DosLayout.FileHandle.Arguments),
		Argument1 = S(ref memory, address, DosLayout.FileHandle.Argument1),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in FileHandle value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.FileHandle.Link, value.Link.Raw);
		memory.WriteUInt32(address, DosLayout.FileHandle.Port, value.Port.Raw);
		memory.WriteUInt32(address, DosLayout.FileHandle.Type, value.Type.Raw);
		W(ref memory, address, DosLayout.FileHandle.Buffer, value.Buffer);
		W(ref memory, address, DosLayout.FileHandle.Position, value.Position);
		W(ref memory, address, DosLayout.FileHandle.End, value.End);
		W(ref memory, address, DosLayout.FileHandle.Functions, value.Functions);
		W(ref memory, address, DosLayout.FileHandle.Function2, value.Function2);
		W(ref memory, address, DosLayout.FileHandle.Function3, value.Function3);
		W(ref memory, address, DosLayout.FileHandle.Arguments, value.Arguments);
		W(ref memory, address, DosLayout.FileHandle.Argument1, value.Argument1);
	}

	public static void WritePosition<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.FileHandle.Position, value);
	public static void WriteEnd<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.FileHandle.End, value);
	public static void WriteFunction2<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.FileHandle.Function2, value);

	private static int S<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Signed(memory.ReadUInt32(address, offset));
	private static void W<TMemory>(ref TMemory memory, APTR address, int offset,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, DosDateStampCodec.Unsigned(value));
}

public static class DosDeviceNodeCodec
{
	public const uint Size = DeviceNode.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static DeviceNode Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DeviceNode.Next)),
		Type = memory.ReadUInt32(address, DosLayout.DeviceNode.Type),
		Task = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DeviceNode.Task)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DeviceNode.Lock)),
		Handler = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DeviceNode.Handler)),
		StackSize = memory.ReadUInt32(address, DosLayout.DeviceNode.StackSize),
		Priority = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.DeviceNode.Priority)),
		Startup = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DeviceNode.Startup)),
		SegmentList = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DeviceNode.SegmentList)),
		GlobalVector = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DeviceNode.GlobalVector)),
		Name = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DeviceNode.Name)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DeviceNode value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DeviceNode.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.Type, value.Type);
		memory.WriteUInt32(address, DosLayout.DeviceNode.Task, value.Task.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.Lock, value.Lock.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.Handler, value.Handler.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.StackSize, value.StackSize);
		memory.WriteUInt32(address, DosLayout.DeviceNode.Priority,
			DosDateStampCodec.Unsigned(value.Priority));
		memory.WriteUInt32(address, DosLayout.DeviceNode.Startup, value.Startup.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.SegmentList,
			value.SegmentList.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.GlobalVector,
			value.GlobalVector.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceNode.Name, value.Name.Raw);
	}
}

/// <summary>Big-endian guest-memory codec for the public 44-byte DeviceList.</summary>
public static class DosDeviceListCodec
{
	public const uint Size = DeviceList.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static DeviceList Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DeviceList.Next)),
		Type = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.DeviceList.Type)),
		Task = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DeviceList.Task)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DeviceList.Lock)),
		VolumeDate = DosDateStampCodec.Read(ref memory, APTR.FromPointer(
			address.Raw + DosLayout.DeviceList.VolumeDate)),
		LockList = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.DeviceList.LockList)),
		DiskType = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.DeviceList.DiskType)),
		Unused = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.DeviceList.Unused)),
		Name = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DeviceList.Name)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DeviceList value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DeviceList.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceList.Type,
			DosDateStampCodec.Unsigned(value.Type));
		memory.WriteUInt32(address, DosLayout.DeviceList.Task, value.Task.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceList.Lock, value.Lock.Raw);
		DosDateStampCodec.Write(ref memory, APTR.FromPointer(address.Raw +
			DosLayout.DeviceList.VolumeDate), value.VolumeDate);
		memory.WriteUInt32(address, DosLayout.DeviceList.LockList,
			value.LockList.Raw);
		memory.WriteUInt32(address, DosLayout.DeviceList.DiskType,
			DosDateStampCodec.Unsigned(value.DiskType));
		memory.WriteUInt32(address, DosLayout.DeviceList.Unused,
			DosDateStampCodec.Unsigned(value.Unused));
		memory.WriteUInt32(address, DosLayout.DeviceList.Name, value.Name.Raw);
	}

	public static APTR VolumeDateAddress(APTR address) =>
		APTR.FromPointer(address.Raw + DosLayout.DeviceList.VolumeDate);
}

public static class DosPathLockCodec
{
	public const uint Size = PathLock.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 3) == 0 &&
		memory.IsMapped(address, Size);

	public static PathLock Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.PathLock.Next)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.PathLock.Lock)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in PathLock value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.PathLock.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.PathLock.Lock, value.Lock.Raw);
	}
}

public static class DosCommandLineInterfaceCodec
{
	public const uint Size = CommandLineInterface.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static CommandLineInterface Read<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Result2 = S(ref memory, address, DosLayout.CommandLineInterface.Result2),
		CurrentDirectoryName = B(ref memory, address,
			DosLayout.CommandLineInterface.CurrentDirectoryName),
		CommandDirectory = B(ref memory, address,
			DosLayout.CommandLineInterface.CommandDirectory),
		ReturnCode = S(ref memory, address,
			DosLayout.CommandLineInterface.ReturnCode),
		CommandName = B(ref memory, address, DosLayout.CommandLineInterface.CommandName),
		FailLevel = S(ref memory, address, DosLayout.CommandLineInterface.FailLevel),
		Prompt = B(ref memory, address, DosLayout.CommandLineInterface.Prompt),
		StandardInput = B(ref memory, address,
			DosLayout.CommandLineInterface.StandardInput),
		CurrentInput = B(ref memory, address,
			DosLayout.CommandLineInterface.CurrentInput),
		CommandFile = B(ref memory, address,
			DosLayout.CommandLineInterface.CommandFile),
		Interactive = S(ref memory, address,
			DosLayout.CommandLineInterface.Interactive),
		Background = S(ref memory, address,
			DosLayout.CommandLineInterface.Background),
		CurrentOutput = B(ref memory, address,
			DosLayout.CommandLineInterface.CurrentOutput),
		DefaultStack = S(ref memory, address,
			DosLayout.CommandLineInterface.DefaultStack),
		StandardOutput = B(ref memory, address,
			DosLayout.CommandLineInterface.StandardOutput),
		Module = B(ref memory, address, DosLayout.CommandLineInterface.Module),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in CommandLineInterface value) where TMemory : struct, IAmigaGuestMemory
	{
		W(ref memory, address, DosLayout.CommandLineInterface.Result2, value.Result2);
		WB(ref memory, address, DosLayout.CommandLineInterface.CurrentDirectoryName,
			value.CurrentDirectoryName);
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandDirectory,
			value.CommandDirectory);
		W(ref memory, address, DosLayout.CommandLineInterface.ReturnCode,
			value.ReturnCode);
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandName,
			value.CommandName);
		W(ref memory, address, DosLayout.CommandLineInterface.FailLevel,
			value.FailLevel);
		WB(ref memory, address, DosLayout.CommandLineInterface.Prompt, value.Prompt);
		WB(ref memory, address, DosLayout.CommandLineInterface.StandardInput,
			value.StandardInput);
		WB(ref memory, address, DosLayout.CommandLineInterface.CurrentInput,
			value.CurrentInput);
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandFile,
			value.CommandFile);
		W(ref memory, address, DosLayout.CommandLineInterface.Interactive,
			value.Interactive);
		W(ref memory, address, DosLayout.CommandLineInterface.Background,
			value.Background);
		WB(ref memory, address, DosLayout.CommandLineInterface.CurrentOutput,
			value.CurrentOutput);
		W(ref memory, address, DosLayout.CommandLineInterface.DefaultStack,
			value.DefaultStack);
		WB(ref memory, address, DosLayout.CommandLineInterface.StandardOutput,
			value.StandardOutput);
		WB(ref memory, address, DosLayout.CommandLineInterface.Module, value.Module);
	}

	public static void WriteResult2<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.Result2, value);
	public static void WriteCurrentDirectoryName<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CurrentDirectoryName,
			value);
	public static BPTR ReadCommandDirectory<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory =>
		B(ref memory, address, DosLayout.CommandLineInterface.CommandDirectory);
	public static void WriteCommandDirectory<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandDirectory,
			value);
	public static void WriteReturnCode<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.ReturnCode, value);
	public static void WriteCommandName<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandName, value);
	public static void WriteFailLevel<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.FailLevel, value);
	public static void WritePrompt<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.Prompt, value);
	public static void WriteStandardInput<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.StandardInput, value);
	public static void WriteCurrentInput<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CurrentInput, value);
	public static void WriteCommandFile<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandFile, value);
	public static void WriteInteractive<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.Interactive, value);
	public static void WriteBackground<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.Background, value);
	public static void WriteCurrentOutput<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CurrentOutput, value);
	public static void WriteDefaultStack<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.DefaultStack, value);
	public static void WriteStandardOutput<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.StandardOutput, value);

	private static int S<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Signed(memory.ReadUInt32(address, offset));
	private static BPTR B<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		BPTR.FromRaw(memory.ReadUInt32(address, offset));
	private static void W<TMemory>(ref TMemory memory, APTR address, int offset,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, DosDateStampCodec.Unsigned(value));
	private static void WB<TMemory>(ref TMemory memory, APTR address, int offset,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, value.Raw);
}

public static class DosAssignListCodec
{
	public const uint Size = AssignList.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static AssignList Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.AssignList.Next)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.AssignList.Lock)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in AssignList value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.AssignList.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.AssignList.Lock, value.Lock.Raw);
	}
}

public static class DosListAssignDataCodec
{
	public const uint Size = DosListAssignData.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static DosListAssignData Read<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		AssignName = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosListAssignData.AssignName)),
		List = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DosListAssignData.List)),
		Reserved0 = memory.ReadUInt32(address,
			DosLayout.DosListAssignData.Reserved0),
		Reserved1 = memory.ReadUInt32(address,
			DosLayout.DosListAssignData.Reserved1),
		Reserved2 = memory.ReadUInt32(address,
			DosLayout.DosListAssignData.Reserved2),
		Reserved3 = memory.ReadUInt32(address,
			DosLayout.DosListAssignData.Reserved3),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosListAssignData value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DosListAssignData.AssignName,
			value.AssignName.Raw);
		memory.WriteUInt32(address, DosLayout.DosListAssignData.List,
			value.List.Raw);
		memory.WriteUInt32(address, DosLayout.DosListAssignData.Reserved0,
			value.Reserved0);
		memory.WriteUInt32(address, DosLayout.DosListAssignData.Reserved1,
			value.Reserved1);
		memory.WriteUInt32(address, DosLayout.DosListAssignData.Reserved2,
			value.Reserved2);
		memory.WriteUInt32(address, DosLayout.DosListAssignData.Reserved3,
			value.Reserved3);
	}
}

public static class DosDevProcCodec
{
	public const uint Size = DevProc.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static DevProc Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Port = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DevProc.Port)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DevProc.Lock)),
		Flags = memory.ReadUInt32(address, DosLayout.DevProc.Flags),
		DeviceNode = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.DevProc.DeviceNode)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DevProc value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DevProc.Port, value.Port.Raw);
		memory.WriteUInt32(address, DosLayout.DevProc.Lock, value.Lock.Raw);
		memory.WriteUInt32(address, DosLayout.DevProc.Flags, value.Flags);
		memory.WriteUInt32(address, DosLayout.DevProc.DeviceNode,
			value.DeviceNode.Raw);
	}
}

public static class DosListCodec
{
	public const uint Size = DosList.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 4);

	public static void WriteNext<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosList.Next, value.Raw);
	public static void WriteTask<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosList.Task, value.Raw);
	/// <summary>Writes only the handler arm's dol_SegList field.</summary>
	public static void WriteHandlerSegmentList<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DeviceNode.SegmentList, value.Raw);

	public static DosList Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DosList.Next)),
		Type = DosDateStampCodec.Signed(memory.ReadUInt32(address,
			DosLayout.DosList.Type)),
		Task = APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DosList.Task)),
		Lock = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DosList.Lock)),
		Misc = new DosListHandler
		{
			Handler = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DosList.Misc)),
			StackSize = DosDateStampCodec.Signed(memory.ReadUInt32(address,
				DosLayout.DosList.Misc + 4)),
			Priority = DosDateStampCodec.Signed(memory.ReadUInt32(address,
				DosLayout.DosList.Misc + 8)),
			Startup = memory.ReadUInt32(address, DosLayout.DosList.Misc + 12),
			SegmentList = BPTR.FromRaw(memory.ReadUInt32(address,
				DosLayout.DosList.Misc + 16)),
			GlobalVector = BPTR.FromRaw(memory.ReadUInt32(address,
				DosLayout.DosList.Misc + 20)),
		},
		Name = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.DosList.Name)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in DosList value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.DosList.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.DosList.Type,
			DosDateStampCodec.Unsigned(value.Type));
		memory.WriteUInt32(address, DosLayout.DosList.Task, value.Task.Raw);
		memory.WriteUInt32(address, DosLayout.DosList.Lock, value.Lock.Raw);
		memory.WriteUInt32(address, DosLayout.DosList.Misc, value.Misc.Handler.Raw);
		memory.WriteUInt32(address, DosLayout.DosList.Misc + 4,
			DosDateStampCodec.Unsigned(value.Misc.StackSize));
		memory.WriteUInt32(address, DosLayout.DosList.Misc + 8,
			DosDateStampCodec.Unsigned(value.Misc.Priority));
		memory.WriteUInt32(address, DosLayout.DosList.Misc + 12,
			value.Misc.Startup);
		memory.WriteUInt32(address, DosLayout.DosList.Misc + 16,
			value.Misc.SegmentList.Raw);
		memory.WriteUInt32(address, DosLayout.DosList.Misc + 20,
			value.Misc.GlobalVector.Raw);
		memory.WriteUInt32(address, DosLayout.DosList.Name, value.Name.Raw);
	}

	public static APTR ReadAssignPath<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosListAssignDataCodec.Read(ref memory,
			AssignDataAddress(address)).AssignName;
	public static APTR ReadAssignList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosListAssignDataCodec.Read(ref memory,
			AssignDataAddress(address)).List;
	public static void WriteAssignPath<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory
	{
		var dataAddress = AssignDataAddress(address);
		var data = DosListAssignDataCodec.Read(ref memory, dataAddress);
		data.AssignName = value;
		DosListAssignDataCodec.Write(ref memory, dataAddress, data);
	}
	public static void WriteAssignList<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory
	{
		var dataAddress = AssignDataAddress(address);
		var data = DosListAssignDataCodec.Read(ref memory, dataAddress);
		data.List = value;
		DosListAssignDataCodec.Write(ref memory, dataAddress, data);
	}

	public static APTR AssignDataAddress(APTR address) =>
		APTR.FromPointer(address.Raw + DosLayout.DosList.Misc);
}

public static class DosProcessCodec
{
	public const uint Size = Process.Size;
	public static APTR MessagePortAddress(APTR address) =>
		APTR.FromPointer(address.Raw + DosLayout.Process.MessagePort);
	public static APTR ProcessAddressFromMessagePort(APTR address) =>
		address.Raw < DosLayout.Process.MessagePort ? APTR.Null :
		APTR.FromPointer(address.Raw - DosLayout.Process.MessagePort);
	public static APTR LocalVariablesAddress(APTR address) =>
		APTR.FromPointer(address.Raw + DosLayout.Process.LocalVariables);
	public static sbyte ReadPriority<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((sbyte)
		memory.ReadUInt8(address, ExecLayout.Node.Priority));
	public static int ReadTaskNumber<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => S(ref memory, address,
			DosLayout.Process.TaskNumber);
	public static void WriteTaskNumber<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => W(ref memory,
		address, DosLayout.Process.TaskNumber, value);
	public static int ReadStackSize<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => S(ref memory, address,
			DosLayout.Process.StackSize);
	public static void WriteStackSize<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => W(ref memory,
		address, DosLayout.Process.StackSize, value);
	public static APTR ReadGlobalVector<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.GlobalVector);
	public static void WriteGlobalVector<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.Process.GlobalVector, value.Raw);

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static int ReadResult2<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => S(ref memory, address,
			DosLayout.Process.Result2);
	public static void WriteResult2<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => W(ref memory,
		address, DosLayout.Process.Result2, value);
	public static BPTR ReadCurrentDirectory<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => B(ref memory,
		address, DosLayout.Process.CurrentDirectory);
	public static void WriteCurrentDirectory<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.Process.CurrentDirectory, value);
	public static BPTR ReadCurrentInput<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => B(ref memory, address,
			DosLayout.Process.CurrentInput);
	public static void WriteCurrentInput<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory => WB(ref memory,
		address, DosLayout.Process.CurrentInput, value);
	public static BPTR ReadCurrentOutput<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => B(ref memory, address,
		DosLayout.Process.CurrentOutput);
	public static void WriteCurrentOutput<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory => WB(ref memory,
		address, DosLayout.Process.CurrentOutput, value);
	public static BPTR ReadSegmentList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => B(ref memory, address,
		DosLayout.Process.SegmentList);
	public static void WriteSegmentList<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory => WB(ref memory,
		address, DosLayout.Process.SegmentList, value);
	public static APTR ReadConsoleTask<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.ConsoleTask);
	public static void WriteConsoleTask<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory => WA(ref memory,
		address, DosLayout.Process.ConsoleTask, value);
	public static APTR ReadFileSystemTask<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.FileSystemTask);
	public static void WriteFileSystemTask<TMemory>(ref TMemory memory,
		APTR address, APTR value) where TMemory : struct, IAmigaGuestMemory =>
		WA(ref memory, address, DosLayout.Process.FileSystemTask, value);
	public static BPTR ReadCommandLineInterface<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => B(ref memory,
		address, DosLayout.Process.CommandLineInterface);
	public static void WriteCommandLineInterface<TMemory>(ref TMemory memory,
		APTR address, BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.Process.CommandLineInterface, value);
	public static APTR ReadWindowPointer<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.WindowPointer);
	public static void WriteWindowPointer<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory => WA(ref memory,
		address, DosLayout.Process.WindowPointer, value);
	public static BPTR ReadHomeDirectory<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => B(ref memory, address,
			DosLayout.Process.HomeDirectory);
	public static void WriteHomeDirectory<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory => WB(ref memory,
		address, DosLayout.Process.HomeDirectory, value);
	public static int ReadFlags<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => S(ref memory, address,
			DosLayout.Process.Flags);
	public static void WriteFlags<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => W(ref memory,
		address, DosLayout.Process.Flags, value);
	public static APTR ReadExitCode<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
		DosLayout.Process.ExitCode);
	public static void WriteExitCode<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory => WA(ref memory,
		address, DosLayout.Process.ExitCode, value);
	public static int ReadExitData<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => S(ref memory, address,
		DosLayout.Process.ExitData);
	public static void WriteExitData<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory => W(ref memory,
		address, DosLayout.Process.ExitData, value);
	public static APTR ReadArguments<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.Arguments);
	public static void WriteArguments<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory => WA(ref memory,
		address, DosLayout.Process.Arguments, value);
	public static uint ReadShellPrivate<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
			DosLayout.Process.ShellPrivate);
	public static void WriteShellPrivate<TMemory>(ref TMemory memory,
		APTR address, uint value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.Process.ShellPrivate, value);
	public static BPTR ReadCurrentError<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => B(ref memory, address,
		DosLayout.Process.CurrentError);
	public static void WriteCurrentError<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory => WB(ref memory,
		address, DosLayout.Process.CurrentError, value);

	private static int S<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Signed(memory.ReadUInt32(address, offset));
	private static APTR A<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, offset));
	private static BPTR B<TMemory>(ref TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory =>
		BPTR.FromRaw(memory.ReadUInt32(address, offset));
	private static void W<TMemory>(ref TMemory memory, APTR address, int offset,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, DosDateStampCodec.Unsigned(value));
	private static void WA<TMemory>(ref TMemory memory, APTR address, int offset,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, value.Raw);
	private static void WB<TMemory>(ref TMemory memory, APTR address, int offset,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, offset, value.Raw);
}

public static class DosLocalVarCodec
{
	public const uint Size = LocalVar.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		DosDateStampCodec.Valid(ref memory, address, Size, 2);

	public static LocalVar Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = new Node
		{
			Successor = APTR.FromPointer(memory.ReadUInt32(address,
				DosLayout.LocalVar.Node + ExecLayout.Node.Successor)),
			Predecessor = APTR.FromPointer(memory.ReadUInt32(address,
				DosLayout.LocalVar.Node + ExecLayout.Node.Predecessor)),
			Type = memory.ReadUInt8(address,
				DosLayout.LocalVar.Node + ExecLayout.Node.Type),
			Priority = unchecked((sbyte)memory.ReadUInt8(address,
				DosLayout.LocalVar.Node + ExecLayout.Node.Priority)),
			Name = STRPTR.FromPointer(memory.ReadUInt32(address,
				DosLayout.LocalVar.Node + ExecLayout.Node.Name)),
		},
		Flags = memory.ReadUInt16(address, DosLayout.LocalVar.Flags),
		Value = APTR.FromPointer(memory.ReadUInt32(address,
			DosLayout.LocalVar.Value)),
		Length = memory.ReadUInt32(address, DosLayout.LocalVar.Length),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in LocalVar value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address,
			DosLayout.LocalVar.Node + ExecLayout.Node.Successor,
			value.Node.Successor.Raw);
		memory.WriteUInt32(address,
			DosLayout.LocalVar.Node + ExecLayout.Node.Predecessor,
			value.Node.Predecessor.Raw);
		memory.WriteUInt8(address, DosLayout.LocalVar.Node + ExecLayout.Node.Type,
			value.Node.Type);
		memory.WriteUInt8(address,
			DosLayout.LocalVar.Node + ExecLayout.Node.Priority,
			unchecked((byte)value.Node.Priority));
		memory.WriteUInt32(address, DosLayout.LocalVar.Node + ExecLayout.Node.Name,
			value.Node.Name.Raw);
		memory.WriteUInt16(address, DosLayout.LocalVar.Flags, value.Flags);
		memory.WriteUInt32(address, DosLayout.LocalVar.Value, value.Value.Raw);
		memory.WriteUInt32(address, DosLayout.LocalVar.Length, value.Length);
	}
}

/// <summary>Big-endian codec for MorphOS V51.51 variable CLI snapshots.</summary>
public static class DosCLIDataCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address,
		uint count) where TMemory : struct, IAmigaGuestMemory
	{
		if (address.IsNull || (address.Raw & 1) != 0 || count > 4096) return false;
		var size = 4u + count * 4u;
		return size >= 4 && memory.IsMapped(address, size);
	}
	public static uint ReadCount<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt32(address, DosLayout.CLIData.NumberOfCLIs);
	public static void WriteCount<TMemory>(ref TMemory memory, APTR address,
		uint value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.CLIData.NumberOfCLIs, value);
	public static APTR ReadItem<TMemory>(ref TMemory memory, APTR address,
		uint index) where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, DosLayout.CLIData.CLIs + unchecked((int)index * 4)));
	public static void WriteItem<TMemory>(ref TMemory memory, APTR address,
		uint index, APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.CLIData.CLIs + unchecked((int)index * 4),
			value.Raw);
}

/// <summary>Big-endian codec for a MorphOS V51.51 CLI snapshot item.</summary>
public static class DosCLIDataItemCodec
{
	public static int ReadCLINumber<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((int)
		memory.ReadUInt32(address, DosLayout.CLIDataItem.CLINumber));
	public static void Write<TMemory>(ref TMemory memory, APTR address,
		int cliNumber, int defaultStack, int globalVector, sbyte priority,
		byte flags) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.CLIDataItem.CLINumber,
			unchecked((uint)cliNumber));
		memory.WriteUInt32(address, DosLayout.CLIDataItem.DefaultStack,
			unchecked((uint)defaultStack));
		memory.WriteUInt32(address, DosLayout.CLIDataItem.GlobalVector,
			unchecked((uint)globalVector));
		memory.WriteUInt32(address, DosLayout.CLIDataItem.Future, 0);
		memory.WriteUInt8(address, DosLayout.CLIDataItem.Priority,
			unchecked((byte)priority));
		memory.WriteUInt8(address, DosLayout.CLIDataItem.Flags, flags);
	}
	public static APTR CommandAddress(APTR address) => APTR.FromPointer(
		address.Raw + DosLayout.CLIDataItem.Command);
}

/// <summary>Big-endian codec for the public variable-length DOS Segment.</summary>
public static class DosSegmentCodec
{
	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1u) == 0 && memory.IsMapped(address, Segment.Size);

	public static Segment Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = BPTR.FromRaw(memory.ReadUInt32(address, DosLayout.Segment.Next)),
		UseCount = unchecked((int)memory.ReadUInt32(address,
			DosLayout.Segment.UseCount)),
		SegmentList = BPTR.FromRaw(memory.ReadUInt32(address,
			DosLayout.Segment.SegmentList)),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		in Segment value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.Segment.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.Segment.UseCount,
			unchecked((uint)value.UseCount));
		memory.WriteUInt32(address, DosLayout.Segment.SegmentList,
			value.SegmentList.Raw);
	}

	public static BPTR ReadNext<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(
		memory.ReadUInt32(address, DosLayout.Segment.Next));
	public static void WriteNext<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.Segment.Next, value.Raw);
	public static int ReadUseCount<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((int)
		memory.ReadUInt32(address, DosLayout.Segment.UseCount));
	public static void WriteUseCount<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.Segment.UseCount,
			unchecked((uint)value));
	public static BPTR ReadSegmentList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(
		memory.ReadUInt32(address, DosLayout.Segment.SegmentList));
	public static APTR NameAddress(APTR address) => APTR.FromPointer(
		address.Raw + DosLayout.Segment.Name);
}

/// <summary>Big-endian view of a classic Amiga HUNK segment link/header.</summary>
public static class DosHunkSegmentHeaderCodec
{
	public static APTR HeaderAddress(BPTR segment) => APTR.FromPointer(
		segment.Address.Raw - 4u);
	public static APTR LinkAddress(BPTR segment) => segment.Address;
	public static APTR DataAddress(BPTR segment) => APTR.FromPointer(
		segment.Address.Raw + 4u);
	public static bool IsMapped<TMemory>(ref TMemory memory, BPTR segment)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (segment.IsNull || segment.Raw > 0x3FFF_FFFFu ||
			segment.Address.Raw < 4u) return false;
		var header = HeaderAddress(segment);
		if ((header.Raw & 1u) != 0 || !memory.IsMapped(header,
			DosHunkSegmentHeader.Size)) return false;
		var size = ReadAllocationSize(ref memory, segment);
		return size >= DosHunkSegmentHeader.Size && memory.IsMapped(header, size);
	}
	public static uint ReadAllocationSize<TMemory>(ref TMemory memory, BPTR segment)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(
		HeaderAddress(segment), DosLayout.HunkSegmentHeader.AllocationSize);
	public static BPTR ReadNext<TMemory>(ref TMemory memory, BPTR segment)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(memory.ReadUInt32(
		HeaderAddress(segment), DosLayout.HunkSegmentHeader.Next));
	public static void Write<TMemory>(ref TMemory memory, APTR header,
		in DosHunkSegmentHeader value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(header, DosLayout.HunkSegmentHeader.AllocationSize,
			value.AllocationSize);
		memory.WriteUInt32(header, DosLayout.HunkSegmentHeader.Next, value.Next.Raw);
	}
}
