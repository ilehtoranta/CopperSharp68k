/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

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

	public static void Write<TMemory>(ref TMemory memory, APTR address, DateStamp value)
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
		PosixDateStamp value) where TMemory : struct, IAmigaGuestMemory
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
		CSource value) where TMemory : struct, IAmigaGuestMemory
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
		RDArgs value) where TMemory : struct, IAmigaGuestMemory
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
		RecordLock value) where TMemory : struct, IAmigaGuestMemory
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
		RecordLock64 value) where TMemory : struct, IAmigaGuestMemory
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

	public static void Write<TMemory>(ref TMemory memory, APTR address, DosPacket value)
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

	public static void Write<TMemory>(ref TMemory memory, APTR address, FileLock value)
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
		DosAttrBuffer value) where TMemory : struct, IAmigaGuestMemory
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
		Argument2 = S(ref memory, address, DosLayout.FileHandle.Argument2),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		FileHandle value) where TMemory : struct, IAmigaGuestMemory
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
		W(ref memory, address, DosLayout.FileHandle.Argument2, value.Argument2);
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
		CommandLineInterface value) where TMemory : struct, IAmigaGuestMemory
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
	public static void WriteCommandFile<TMemory>(ref TMemory memory, APTR address,
		BPTR value) where TMemory : struct, IAmigaGuestMemory =>
		WB(ref memory, address, DosLayout.CommandLineInterface.CommandFile, value);
	public static void WriteInteractive<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.Interactive, value);
	public static void WriteBackground<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.Background, value);
	public static void WriteDefaultStack<TMemory>(ref TMemory memory, APTR address,
		int value) where TMemory : struct, IAmigaGuestMemory =>
		W(ref memory, address, DosLayout.CommandLineInterface.DefaultStack, value);

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
		AssignList value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, DosLayout.AssignList.Next, value.Next.Raw);
		memory.WriteUInt32(address, DosLayout.AssignList.Lock, value.Lock.Raw);
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
		DevProc value) where TMemory : struct, IAmigaGuestMemory
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
		DosList value) where TMemory : struct, IAmigaGuestMemory
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
		APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DosList.Misc));
	public static APTR ReadAssignList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		APTR.FromPointer(memory.ReadUInt32(address, DosLayout.DosList.Misc + 4));
	public static void WriteAssignPath<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosList.Misc, value.Raw);
	public static void WriteAssignList<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DosLayout.DosList.Misc + 4, value.Raw);
}

public static class DosProcessCodec
{
	public const uint Size = Process.Size;
	public static APTR MessagePortAddress(APTR address) =>
		APTR.FromPointer(address.Raw + DosLayout.Process.MessagePort);

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
	public static APTR ReadArguments<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.Arguments);
	public static void WriteArguments<TMemory>(ref TMemory memory, APTR address,
		APTR value) where TMemory : struct, IAmigaGuestMemory => WA(ref memory,
		address, DosLayout.Process.Arguments, value);

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
