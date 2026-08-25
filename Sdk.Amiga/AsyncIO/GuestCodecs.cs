/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

/// <summary>
/// Read-only big-endian guest-memory inspector for library-owned AsyncFile
/// records. Use the existing Exec and DOS codecs for the embedded records.
/// </summary>
public static class AsyncFileCodec
{
	public const uint Size = AsyncFile.Size;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => address.IsNotNull &&
		(address.Raw & 1) == 0 && address.Raw <= uint.MaxValue - Size &&
		memory.IsMapped(address, Size);

	public static BPTR ReadFile<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => BPTR.FromRaw(memory.ReadUInt32(
			address, AsyncIOLayout.AsyncFile.File));

	public static uint ReadBlockSize<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
			AsyncIOLayout.AsyncFile.BlockSize);

	public static APTR ReadHandler<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => Pointer(memory, address,
			AsyncIOLayout.AsyncFile.Handler);

	public static APTR ReadOffset<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => Pointer(memory, address,
			AsyncIOLayout.AsyncFile.Offset);

	public static int ReadBytesLeft<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((int)memory.ReadUInt32(
			address, AsyncIOLayout.AsyncFile.BytesLeft));

	public static uint ReadBufferSize<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
			AsyncIOLayout.AsyncFile.BufferSize);

	public static APTR ReadBuffer<TMemory>(ref TMemory memory, APTR address,
		int index) where TMemory : struct, IAmigaGuestMemory => Pointer(memory,
		address, index switch
		{
			0 => AsyncIOLayout.AsyncFile.Buffer0,
			1 => AsyncIOLayout.AsyncFile.Buffer1,
			_ => throw new ArgumentOutOfRangeException(nameof(index)),
		});

	public static APTR PacketAddress(APTR address) => APTR.FromPointer(address.Raw +
		AsyncIOLayout.AsyncFile.Packet);

	public static APTR PacketPortAddress(APTR address) => APTR.FromPointer(address.Raw +
		AsyncIOLayout.AsyncFile.PacketPort);

	public static uint ReadCurrentBuffer<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
			AsyncIOLayout.AsyncFile.CurrentBuffer);

	public static uint ReadSeekOffset<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
			AsyncIOLayout.AsyncFile.SeekOffset);

	public static byte ReadPacketPending<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
			AsyncIOLayout.AsyncFile.PacketPending);

	public static byte ReadReadMode<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
			AsyncIOLayout.AsyncFile.ReadMode);

	public static byte ReadCloseFileHandle<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(
			address, AsyncIOLayout.AsyncFile.CloseFileHandle);

	public static byte ReadSeekPastEndOfFile<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(
			address, AsyncIOLayout.AsyncFile.SeekPastEndOfFile);

	public static uint ReadLastResult1<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
			AsyncIOLayout.AsyncFile.LastResult1);

	public static uint ReadLastBytesLeft<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(
			address, AsyncIOLayout.AsyncFile.LastBytesLeft);

	private static APTR Pointer<TMemory>(TMemory memory, APTR address, int offset)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(memory.ReadUInt32(
			address, offset));
}
