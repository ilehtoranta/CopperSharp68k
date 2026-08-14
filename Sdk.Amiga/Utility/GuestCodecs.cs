namespace Amiga;

/// <summary>
/// Big-endian guest-memory codec for the public SDK <see cref="TagItem"/>
/// structure. Tag-list algorithms exchange typed values; the public field
/// positions remain owned by this SDK boundary.
/// </summary>
public static class UtilityTagItemCodec
{
	public const uint Size = TagItem.Size;

	private const int TagOffset = 0;
	private const int DataOffset = 4;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out TagItem value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address)) return false;
		value.Tag = memory.ReadUInt32(address, TagOffset);
		value.Data = memory.ReadUInt32(address, DataOffset);
		return true;
	}

	public static uint ReadTag<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt32(address, TagOffset);

	public static uint ReadData<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.ReadUInt32(address, DataOffset);

	public static void WriteTag<TMemory>(ref TMemory memory, APTR address,
		uint value)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, TagOffset, value);

	public static void WriteData<TMemory>(ref TMemory memory, APTR address,
		uint value)
		where TMemory : struct, IAmigaGuestMemory =>
		memory.WriteUInt32(address, DataOffset, value);

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		TagItem value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, TagOffset, value.Tag);
		memory.WriteUInt32(address, DataOffset, value.Data);
	}
}

/// <summary>Big-endian guest-memory codec for the public SDK ClockData.</summary>
public static class UtilityClockDataCodec
{
	public const uint Size = ClockData.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out ClockData value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (address.IsNull || (address.Raw & 1) != 0 ||
			address.Raw > uint.MaxValue - Size || !memory.IsMapped(address, Size))
			return false;
		value.Second = memory.ReadUInt16(address, 0);
		value.Minute = memory.ReadUInt16(address, 2);
		value.Hour = memory.ReadUInt16(address, 4);
		value.Day = memory.ReadUInt16(address, 6);
		value.Month = memory.ReadUInt16(address, 8);
		value.Year = memory.ReadUInt16(address, 10);
		value.WeekDay = memory.ReadUInt16(address, 12);
		return true;
	}

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		ClockData value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, value.Second);
		memory.WriteUInt16(address, 2, value.Minute);
		memory.WriteUInt16(address, 4, value.Hour);
		memory.WriteUInt16(address, 6, value.Day);
		memory.WriteUInt16(address, 8, value.Month);
		memory.WriteUInt16(address, 10, value.Year);
		memory.WriteUInt16(address, 12, value.WeekDay);
	}
}
