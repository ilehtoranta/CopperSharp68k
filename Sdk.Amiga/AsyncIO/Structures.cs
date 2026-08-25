/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>
/// Shared-library asyncio 39.x handle layout. The library owns this record;
/// pass its address as an <see cref="APTR"/> and do not modify its fields.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 2, Size = Size)]
public readonly struct AsyncFile
{
	public const int Size = 154;

	public readonly BPTR File;
	public readonly uint BlockSize;
	public readonly APTR Handler;
	public readonly APTR Offset;
	public readonly int BytesLeft;
	public readonly uint BufferSize;
	public readonly APTR Buffer0;
	public readonly APTR Buffer1;
	public readonly StandardPacket Packet;
	public readonly MsgPort PacketPort;
	public readonly uint CurrentBuffer;
	public readonly uint SeekOffset;
	public readonly byte PacketPending;
	public readonly byte ReadMode;
	public readonly byte CloseFileHandle;
	public readonly byte SeekPastEndOfFile;
	public readonly uint LastResult1;
	public readonly uint LastBytesLeft;
}
