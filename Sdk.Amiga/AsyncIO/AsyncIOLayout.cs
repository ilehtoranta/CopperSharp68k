/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public static class AsyncIOLayout
{
	public static class AsyncFile
	{
		public const int File = 0;
		public const int BlockSize = 4;
		public const int Handler = 8;
		public const int Offset = 12;
		public const int BytesLeft = 16;
		public const int BufferSize = 20;
		public const int Buffer0 = 24;
		public const int Buffer1 = 28;
		public const int Packet = 32;
		public const int PacketPort = Packet + (int)StandardPacket.Size;
		public const int CurrentBuffer = PacketPort + (int)MsgPort.Size;
		public const int SeekOffset = CurrentBuffer + 4;
		public const int PacketPending = SeekOffset + 4;
		public const int ReadMode = PacketPending + 1;
		public const int CloseFileHandle = ReadMode + 1;
		public const int SeekPastEndOfFile = CloseFileHandle + 1;
		public const int LastResult1 = SeekPastEndOfFile + 1;
		public const int LastBytesLeft = LastResult1 + 4;
	}
}
