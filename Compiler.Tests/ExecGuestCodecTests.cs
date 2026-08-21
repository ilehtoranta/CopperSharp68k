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
		memory.WriteUInt32(message, ExecLayout.Message.ReplyPort, 0x1234_5678);
		Assert.True(ExecMessageCodec.IsMapped(ref memory, message));
		Assert.Equal(APTR.FromPointer(0x1234_5678),
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
