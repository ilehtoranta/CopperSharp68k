namespace Amiga;

/// <summary>Bounded guest-memory operations for null-terminated strings.</summary>
public static class CStringCodec
{
	/// <summary>
	/// Tries to find the terminating null byte of a C string without reading
	/// beyond <paramref name="maximumLength"/> bytes.
	/// </summary>
	/// <remarks>
	/// The operation fails for a null address, an address-space wrap, an
	/// unmapped byte, or a string with no terminator inside the supplied bound.
	/// </remarks>
	public static bool TryReadLength<TMemory>(ref TMemory memory, APTR value,
		uint maximumLength, out uint length)
		where TMemory : struct, IAmigaGuestMemory
	{
		length = 0;
		if (value.IsNull) return false;

		for (var index = 0u; index < maximumLength; index++)
		{
			if (value.Raw > uint.MaxValue - index) return false;
			var address = APTR.FromPointer(value.Raw + index);
			if (!memory.IsMapped(address, 1)) return false;
			if (memory.ReadUInt8(address) != 0) continue;

			length = index;
			return true;
		}

		return false;
	}

	/// <summary>
	/// Tries to compare two null-terminated strings without reading beyond
	/// <paramref name="maximumLength"/> bytes from either string.
	/// </summary>
	/// <remarks>
	/// A false return value means that a null terminator could not be reached
	/// safely within the supplied bound. Different mapped bytes are a successful
	/// comparison and produce <see langword="false"/> in
	/// <paramref name="equal"/>. Identical addresses compare equal without a
	/// memory read.
	/// </remarks>
	public static bool TryEquals<TMemory>(ref TMemory memory, APTR left,
		APTR right, uint maximumLength, out bool equal)
		where TMemory : struct, IAmigaGuestMemory
	{
		equal = left.Raw == right.Raw;
		if (equal) return true;
		if (left.IsNull || right.IsNull) return false;

		for (var index = 0u; index < maximumLength; index++)
		{
			if (left.Raw > uint.MaxValue - index ||
				right.Raw > uint.MaxValue - index)
				return false;
			var leftAddress = APTR.FromPointer(left.Raw + index);
			var rightAddress = APTR.FromPointer(right.Raw + index);
			if (!memory.IsMapped(leftAddress, 1) ||
				!memory.IsMapped(rightAddress, 1))
				return false;

			var leftByte = memory.ReadUInt8(leftAddress);
			var rightByte = memory.ReadUInt8(rightAddress);
			if (leftByte != rightByte) return true;
			if (leftByte != 0) continue;

			equal = true;
			return true;
		}

		return false;
	}
}
