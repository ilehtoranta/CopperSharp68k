using System.Buffers.Binary;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntuitionClassicCodecAuthorityTests
{
	[Fact]
	public void DrawingAndMessageStructuresRoundTripEveryAbiByte()
	{
		var memory = new TestMemory(0x6000u, 4096);
		var source = P(0x6100u);
		var destination = P(0x6300u);

		Fill(ref memory, source, IntuiText.Size);
		memory.WriteUInt8(source, 3, 0);
		IntuitionDrawingGuestCodec.WriteText(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadText(ref memory, source));
		AssertBytes(ref memory, source, destination, IntuiText.Size);

		Fill(ref memory, source, Border.Size);
		IntuitionDrawingGuestCodec.WriteBorder(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadBorder(ref memory, source));
		AssertBytes(ref memory, source, destination, Border.Size);

		Fill(ref memory, source, Image.Size);
		IntuitionDrawingGuestCodec.WriteImage(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadImage(ref memory, source));
		AssertBytes(ref memory, source, destination, Image.Size);

		Fill(ref memory, source, IBox.Size);
		IntuitionDrawingGuestCodec.WriteBox(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadBox(ref memory, source));
		AssertBytes(ref memory, source, destination, IBox.Size);

		Fill(ref memory, source, TabletData.Size);
		IntuitionDrawingGuestCodec.WriteTabletData(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadTabletData(ref memory, source));
		AssertBytes(ref memory, source, destination, TabletData.Size);

		Fill(ref memory, source, TabletHookData.Size);
		IntuitionDrawingGuestCodec.WriteTabletHookData(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadTabletHookData(ref memory, source));
		AssertBytes(ref memory, source, destination, TabletHookData.Size);

		Fill(ref memory, source, IntuiMessage.Size);
		IntuitionDrawingGuestCodec.WriteMessage(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadMessage(ref memory, source));
		AssertBytes(ref memory, source, destination, IntuiMessage.Size);

		Fill(ref memory, source, ExtIntuiMessage.Size);
		IntuitionDrawingGuestCodec.WriteExtendedMessage(ref memory, destination,
			IntuitionDrawingGuestCodec.ReadExtendedMessage(ref memory, source));
		AssertBytes(ref memory, source, destination, ExtIntuiMessage.Size);

		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionDrawingGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void MenuAndRequesterStructuresRoundTripEveryAbiByte()
	{
		var memory = new TestMemory(0x7000u, 4096);
		var source = P(0x7100u);
		var destination = P(0x7300u);

		Fill(ref memory, source, Menu.Size);
		IntuitionMenuRequesterGuestCodec.WriteMenu(ref memory, destination,
			IntuitionMenuRequesterGuestCodec.ReadMenu(ref memory, source));
		AssertBytes(ref memory, source, destination, Menu.Size);

		Fill(ref memory, source, MenuItem.Size);
		memory.WriteUInt8(source, 27, 0);
		IntuitionMenuRequesterGuestCodec.WriteMenuItem(ref memory, destination,
			IntuitionMenuRequesterGuestCodec.ReadMenuItem(ref memory, source));
		AssertBytes(ref memory, source, destination, MenuItem.Size);

		Fill(ref memory, source, Requester.Size);
		memory.WriteUInt8(source, 31, 0);
		IntuitionMenuRequesterGuestCodec.WriteRequester(ref memory, destination,
			IntuitionMenuRequesterGuestCodec.ReadRequester(ref memory, source));
		AssertBytes(ref memory, source, destination, Requester.Size);

		Fill(ref memory, source, Remember.Size);
		IntuitionMenuRequesterGuestCodec.WriteRemember(ref memory, destination,
			IntuitionMenuRequesterGuestCodec.ReadRemember(ref memory, source));
		AssertBytes(ref memory, source, destination, Remember.Size);

		Fill(ref memory, source, EasyStruct.Size);
		IntuitionMenuRequesterGuestCodec.WriteEasyStruct(ref memory, destination,
			IntuitionMenuRequesterGuestCodec.ReadEasyStruct(ref memory, source));
		AssertBytes(ref memory, source, destination, EasyStruct.Size);

		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionMenuRequesterGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void GadgetStateStructuresRoundTripEveryAbiByte()
	{
		var memory = new TestMemory(0x8000u, 4096);
		var source = P(0x8100u);
		var destination = P(0x8300u);

		Fill(ref memory, source, Gadget.Size);
		IntuitionGadgetGuestCodec.WriteGadget(ref memory, destination,
			IntuitionGadgetGuestCodec.ReadGadget(ref memory, source));
		AssertBytes(ref memory, source, destination, Gadget.Size);

		Fill(ref memory, source, ExtGadget.Size);
		IntuitionGadgetGuestCodec.WriteExtendedGadget(ref memory, destination,
			IntuitionGadgetGuestCodec.ReadExtendedGadget(ref memory, source));
		AssertBytes(ref memory, source, destination, ExtGadget.Size);

		Fill(ref memory, source, BoolInfo.Size);
		IntuitionGadgetGuestCodec.WriteBoolInfo(ref memory, destination,
			IntuitionGadgetGuestCodec.ReadBoolInfo(ref memory, source));
		AssertBytes(ref memory, source, destination, BoolInfo.Size);

		Fill(ref memory, source, PropInfo.Size);
		IntuitionGadgetGuestCodec.WritePropInfo(ref memory, destination,
			IntuitionGadgetGuestCodec.ReadPropInfo(ref memory, source));
		AssertBytes(ref memory, source, destination, PropInfo.Size);

		Fill(ref memory, source, StringInfo.Size);
		IntuitionGadgetGuestCodec.WriteStringInfo(ref memory, destination,
			IntuitionGadgetGuestCodec.ReadStringInfo(ref memory, source));
		AssertBytes(ref memory, source, destination, StringInfo.Size);

		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionGadgetGuestCodec).Assembly.GetName().Name);
	}

	[Fact]
	public void ScreenWindowDescriptorsAndSupportRecordsRoundTripEveryAbiByte()
	{
		var memory = new TestMemory(0x9000u, 4096);
		var source = P(0x9100u);
		var destination = P(0x9400u);

		Fill(ref memory, source, NewWindow.Size);
		IntuitionScreenWindowGuestCodec.WriteNewWindow(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadNewWindow(ref memory, source));
		AssertBytes(ref memory, source, destination, NewWindow.Size);

		Fill(ref memory, source, ExtNewWindow.Size);
		IntuitionScreenWindowGuestCodec.WriteExtendedNewWindow(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadExtendedNewWindow(ref memory, source));
		AssertBytes(ref memory, source, destination, ExtNewWindow.Size);

		Fill(ref memory, source, NewScreen.Size);
		IntuitionScreenWindowGuestCodec.WriteNewScreen(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadNewScreen(ref memory, source));
		AssertBytes(ref memory, source, destination, NewScreen.Size);

		Fill(ref memory, source, ExtNewScreen.Size);
		IntuitionScreenWindowGuestCodec.WriteExtendedNewScreen(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadExtendedNewScreen(ref memory, source));
		AssertBytes(ref memory, source, destination, ExtNewScreen.Size);

		Fill(ref memory, source, DrawInfo.Size);
		IntuitionScreenWindowGuestCodec.WriteDrawInfo(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadDrawInfo(ref memory, source));
		AssertBytes(ref memory, source, destination, DrawInfo.Size);

		Fill(ref memory, source, ColorSpec.Size);
		IntuitionScreenWindowGuestCodec.WriteColorSpec(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadColorSpec(ref memory, source));
		AssertBytes(ref memory, source, destination, ColorSpec.Size);

		Fill(ref memory, source, PubScreenNode.Size);
		memory.WriteUInt8(source, 29, 0);
		IntuitionScreenWindowGuestCodec.WritePubScreenNode(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadPubScreenNode(ref memory, source));
		AssertBytes(ref memory, source, destination, PubScreenNode.Size);

		Fill(ref memory, source, ScreenBuffer.Size);
		IntuitionScreenWindowGuestCodec.WriteScreenBuffer(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadScreenBuffer(ref memory, source));
		AssertBytes(ref memory, source, destination, ScreenBuffer.Size);

		Fill(ref memory, source, Window.Size);
		IntuitionScreenWindowGuestCodec.WriteWindow(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadWindow(ref memory, source));
		AssertBytes(ref memory, source, destination, Window.Size);

		Fill(ref memory, source, Screen.Size);
		memory.WriteUInt8(source, 39, 0);
		memory.WriteUInt8(source, 115, 0);
		memory.WriteUInt16(source, 190, 0);
		IntuitionScreenWindowGuestCodec.WriteScreen(ref memory, destination,
			IntuitionScreenWindowGuestCodec.ReadScreen(ref memory, source));
		AssertBytes(ref memory, source, destination, Screen.Size);

		Assert.Equal("CopperSharp.Sdk.Amiga.Support",
			typeof(IntuitionScreenWindowGuestCodec).Assembly.GetName().Name);
	}

	private static void Fill(ref TestMemory memory, APTR address, uint size)
	{
		for (var offset = 0; offset < (int)size; offset++)
			memory.WriteUInt8(address, offset, unchecked((byte)(offset * 37 + 11)));
	}

	private static void AssertBytes(ref TestMemory memory, APTR source,
		APTR destination, uint size)
	{
		for (var offset = 0; offset < (int)size; offset++)
			Assert.Equal(memory.ReadUInt8(source, offset),
				memory.ReadUInt8(destination, offset));
	}

	private static APTR P(uint value) => APTR.FromPointer(value);

	private struct TestMemory : IAmigaGuestMemory
	{
		private readonly uint _baseAddress;
		private readonly byte[] _bytes;

		public TestMemory(uint baseAddress, int size)
		{
			_baseAddress = baseAddress;
			_bytes = new byte[size];
		}

		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[Index(address, offset, 1)];
		public ushort ReadUInt16(APTR address, int offset = 0) =>
			BinaryPrimitives.ReadUInt16BigEndian(_bytes.AsSpan(Index(address, offset, 2), 2));
		public uint ReadUInt32(APTR address, int offset = 0) =>
			BinaryPrimitives.ReadUInt32BigEndian(_bytes.AsSpan(Index(address, offset, 4), 4));
		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[Index(address, offset, 1)] = value;
		public void WriteUInt16(APTR address, int offset, ushort value) =>
			BinaryPrimitives.WriteUInt16BigEndian(_bytes.AsSpan(Index(address, offset, 2), 2), value);
		public void WriteUInt32(APTR address, int offset, uint value) =>
			BinaryPrimitives.WriteUInt32BigEndian(_bytes.AsSpan(Index(address, offset, 4), 4), value);
		public void Clear(APTR address, uint byteCount) =>
			_bytes.AsSpan(Index(address, 0, checked((int)byteCount)), checked((int)byteCount)).Clear();
		public void Copy(APTR source, APTR destination, uint byteCount) =>
			_bytes.AsSpan(Index(source, 0, checked((int)byteCount)), checked((int)byteCount))
				.CopyTo(_bytes.AsSpan(Index(destination, 0, checked((int)byteCount)), checked((int)byteCount)));
		public bool IsMapped(APTR address, uint byteSize) =>
			address.Raw >= _baseAddress && address.Raw - _baseAddress <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - (address.Raw - _baseAddress);

		private int Index(APTR address, int offset, int size)
		{
			var raw = checked(address.Raw + (uint)offset);
			if (raw < _baseAddress || raw - _baseAddress > (uint)_bytes.Length ||
				(uint)size > (uint)_bytes.Length - (raw - _baseAddress))
				throw new ArgumentOutOfRangeException(nameof(address));
			return checked((int)(raw - _baseAddress));
		}
	}
}
