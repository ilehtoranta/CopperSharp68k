using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class CStringCodecTests
{
	[Fact]
	public void ReadsLengthWithinBound()
	{
		var memory = new TestGuestMemory(0x1000, [(byte)'A', (byte)'m',
			(byte)'i', (byte)'g', (byte)'a', 0, (byte)'!']);

		Assert.True(CStringCodec.TryReadLength(ref memory,
			APTR.FromPointer(0x1000), 6, out var length));
		Assert.Equal(5u, length);
	}

	[Fact]
	public void ReadsEmptyString()
	{
		var memory = new TestGuestMemory(0x1000, [0]);

		Assert.True(CStringCodec.TryReadLength(ref memory,
			APTR.FromPointer(0x1000), 1, out var length));
		Assert.Equal(0u, length);
	}

	[Fact]
	public void RejectsNullUnterminatedAndUnmappedStrings()
	{
		var memory = new TestGuestMemory(0x1000, [(byte)'A', (byte)'B', 0]);

		Assert.False(CStringCodec.TryReadLength(ref memory, APTR.Null, 3,
			out var nullLength));
		Assert.Equal(0u, nullLength);
		Assert.False(CStringCodec.TryReadLength(ref memory,
			APTR.FromPointer(0x1000), 2, out var unterminatedLength));
		Assert.Equal(0u, unterminatedLength);

		var truncatedMemory = new TestGuestMemory(0x2000, [(byte)'A']);
		Assert.False(CStringCodec.TryReadLength(ref truncatedMemory,
			APTR.FromPointer(0x2000), 2, out var unmappedLength));
		Assert.Equal(0u, unmappedLength);
	}

	[Fact]
	public void RejectsAddressSpaceWrap()
	{
		var memory = new TestGuestMemory(uint.MaxValue, [(byte)'A']);

		Assert.False(CStringCodec.TryReadLength(ref memory,
			APTR.FromPointer(uint.MaxValue), 2, out var length));
		Assert.Equal(0u, length);
	}

	[Fact]
	public void ComparesEqualAndDifferentStrings()
	{
		var memory = new TestGuestMemory(0x1000,
			[(byte)'A', (byte)'m', 0, (byte)'A', (byte)'m', 0,
				(byte)'A', (byte)'x', 0]);

		Assert.True(CStringCodec.TryEquals(ref memory,
			APTR.FromPointer(0x1000), APTR.FromPointer(0x1003), 3,
			out var equal));
		Assert.True(equal);
		Assert.True(CStringCodec.TryEquals(ref memory,
			APTR.FromPointer(0x1000), APTR.FromPointer(0x1006), 3,
			out equal));
		Assert.False(equal);
	}

	[Fact]
	public void CStringComparisonRejectsUnsafeInputs()
	{
		var memory = new TestGuestMemory(0x1000,
			[(byte)'A', (byte)'m', (byte)'A', (byte)'m']);

		Assert.False(CStringCodec.TryEquals(ref memory, APTR.Null,
			APTR.FromPointer(0x1000), 2, out var equal));
		Assert.False(equal);
		Assert.False(CStringCodec.TryEquals(ref memory,
			APTR.FromPointer(0x1000), APTR.FromPointer(0x1002), 2, out equal));
		Assert.False(equal);

		var wrappingMemory = new TestGuestMemory(uint.MaxValue - 1,
			[(byte)'A', (byte)'A']);
		Assert.False(CStringCodec.TryEquals(ref wrappingMemory,
			APTR.FromPointer(uint.MaxValue), APTR.FromPointer(uint.MaxValue - 1),
			2, out equal));
		Assert.False(equal);
	}

	[Fact]
	public void IdenticalCStringAddressesCompareEqualWithoutReadingMemory()
	{
		var memory = new TestGuestMemory(0x1000, []);

		Assert.True(CStringCodec.TryEquals(ref memory, APTR.Null, APTR.Null, 0,
			out var equal));
		Assert.True(equal);
	}

	private struct TestGuestMemory(uint baseAddress, byte[] bytes)
		: IAmigaGuestMemory
	{
		private readonly uint _baseAddress = baseAddress;
		private readonly byte[] _bytes = bytes;

		public readonly byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[GetIndex(address, offset)];

		public readonly ushort ReadUInt16(APTR address, int offset = 0) =>
			throw new NotSupportedException();

		public readonly uint ReadUInt32(APTR address, int offset = 0) =>
			throw new NotSupportedException();

		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[GetIndex(address, offset)] = value;

		public void WriteUInt16(APTR address, int offset, ushort value) =>
			throw new NotSupportedException();

		public void WriteUInt32(APTR address, int offset, uint value) =>
			throw new NotSupportedException();

		public void Clear(APTR address, uint byteCount) =>
			throw new NotSupportedException();

		public void Copy(APTR source, APTR destination, uint byteCount) =>
			throw new NotSupportedException();

		public readonly bool IsMapped(APTR address, uint byteSize)
		{
			if (address.Raw < _baseAddress) return false;
			var offset = address.Raw - _baseAddress;
			return offset <= _bytes.Length &&
				byteSize <= (uint)_bytes.Length - offset;
		}

		private readonly int GetIndex(APTR address, int offset) =>
			checked((int)(address.Raw - _baseAddress) + offset);
	}
}
