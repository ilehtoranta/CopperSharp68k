/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

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
		DosDateTime value) where TMemory : struct, IAmigaGuestMemory
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
		DosEnvec value) where TMemory : struct, IAmigaGuestMemory
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
		FileSysStartupMsg value) where TMemory : struct, IAmigaGuestMemory
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
		InfoData value) where TMemory : struct, IAmigaGuestMemory
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
		DeviceNode value) where TMemory : struct, IAmigaGuestMemory
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
		DeviceList value) where TMemory : struct, IAmigaGuestMemory
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
		AssignList value) where TMemory : struct, IAmigaGuestMemory
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
		DosListAssignData value) where TMemory : struct, IAmigaGuestMemory
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
	public static APTR ReadGlobalVector<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => A(ref memory, address,
			DosLayout.Process.GlobalVector);

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
		LocalVar value) where TMemory : struct, IAmigaGuestMemory
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
		DosHunkSegmentHeader value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(header, DosLayout.HunkSegmentHeader.AllocationSize,
			value.AllocationSize);
		memory.WriteUInt32(header, DosLayout.HunkSegmentHeader.Next, value.Next.Raw);
	}
}
