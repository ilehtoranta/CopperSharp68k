namespace Amiga;

/// <summary>
/// Guarded big-endian guest-memory boundary used by SDK structure codecs.
/// Implementations may target native addresses, an emulator bus, or a bounded
/// qualification arena without changing the public structure model.
/// </summary>
public interface IAmigaGuestMemory
{
	byte ReadUInt8(APTR address, int offset = 0);
	ushort ReadUInt16(APTR address, int offset = 0);
	uint ReadUInt32(APTR address, int offset = 0);
	void WriteUInt8(APTR address, int offset, byte value);
	void WriteUInt16(APTR address, int offset, ushort value);
	void WriteUInt32(APTR address, int offset, uint value);
	void Clear(APTR address, uint byteCount);
	void Copy(APTR source, APTR destination, uint byteCount);
	bool IsMapped(APTR address, uint byteSize);
}
