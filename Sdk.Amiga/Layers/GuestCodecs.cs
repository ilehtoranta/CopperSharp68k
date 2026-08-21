namespace Amiga;

/// <summary>
/// Owner-specific big-endian codec for the public SDK <see cref="Rectangle"/>
/// value. Algorithms exchange the typed value; public field offsets remain
/// confined to this codec.
/// </summary>
public static class LayersRectangleCodec
{
	public const uint Size = Rectangle.Size;

	public static Rectangle Create(short minX, short minY, short maxX,
		short maxY) => new()
	{
		MinX = minX,
		MinY = minY,
		MaxX = maxX,
		MaxY = maxY,
	};

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out Rectangle value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address)) return false;
		value = Read(ref memory, address);
		return true;
	}

	public static Rectangle Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
		{
			MinX = unchecked((short)memory.ReadUInt16(address,
				GraphicsLayout.Rectangle.MinX)),
			MinY = unchecked((short)memory.ReadUInt16(address,
				GraphicsLayout.Rectangle.MinY)),
			MaxX = unchecked((short)memory.ReadUInt16(address,
				GraphicsLayout.Rectangle.MaxX)),
			MaxY = unchecked((short)memory.ReadUInt16(address,
				GraphicsLayout.Rectangle.MaxY)),
		};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		Rectangle value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, GraphicsLayout.Rectangle.MinX,
			unchecked((ushort)value.MinX));
		memory.WriteUInt16(address, GraphicsLayout.Rectangle.MinY,
			unchecked((ushort)value.MinY));
		memory.WriteUInt16(address, GraphicsLayout.Rectangle.MaxX,
			unchecked((ushort)value.MaxX));
		memory.WriteUInt16(address, GraphicsLayout.Rectangle.MaxY,
			unchecked((ushort)value.MaxY));
	}

	public static void WriteMinX<TMemory>(ref TMemory memory, APTR address,
		short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.Rectangle.MinX, unchecked((ushort)value));

	public static void WriteMinY<TMemory>(ref TMemory memory, APTR address,
		short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.Rectangle.MinY, unchecked((ushort)value));

	public static void WriteMaxX<TMemory>(ref TMemory memory, APTR address,
		short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.Rectangle.MaxX, unchecked((ushort)value));

	public static void WriteMaxY<TMemory>(ref TMemory memory, APTR address,
		short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.Rectangle.MaxY, unchecked((ushort)value));

	public static void Clear<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.Clear(address, Size);

	public static APTR At(APTR owner, uint offset) =>
		APTR.FromPointer(owner.Raw + offset);
}

/// <summary>Typed access boundary for the public SDK <see cref="Region"/>.</summary>
public static class LayersRegionCodec
{
	public const uint Size = Region.Size;

	public static Rectangle ReadBounds<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => LayersRectangleCodec.Read(
		ref memory, LayersRectangleCodec.At(address,
			(uint)GraphicsLayout.Region.Bounds));

	public static void WriteBounds<TMemory>(ref TMemory memory, APTR address,
		Rectangle value)
		where TMemory : struct, IAmigaGuestMemory => LayersRectangleCodec.Write(
		ref memory, LayersRectangleCodec.At(address,
			(uint)GraphicsLayout.Region.Bounds), value);

	public static APTR ReadFirst<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, GraphicsLayout.Region.RegionRectangle));

	public static void WriteFirst<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.Region.RegionRectangle, value.Raw);

	public static APTR HeadAnchor(APTR address) => LayersRectangleCodec.At(
		address, (uint)GraphicsLayout.Region.RegionRectangle);
}

/// <summary>
/// Typed access boundary for the public SDK <see cref="RegionRectangle"/>.
/// </summary>
public static class LayersRegionRectangleCodec
{
	public const uint Size = RegionRectangle.Size;

	public static APTR ReadNext<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, GraphicsLayout.RegionRectangle.Successor));

	public static void WriteNext<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.RegionRectangle.Successor, value.Raw);

	public static APTR ReadPrevious<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, GraphicsLayout.RegionRectangle.Predecessor));

	public static void WritePrevious<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.RegionRectangle.Predecessor, value.Raw);

	public static Rectangle ReadBounds<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => LayersRectangleCodec.Read(
		ref memory, LayersRectangleCodec.At(address,
			(uint)GraphicsLayout.RegionRectangle.Bounds));

	public static void WriteBounds<TMemory>(ref TMemory memory, APTR address,
		Rectangle value)
		where TMemory : struct, IAmigaGuestMemory => LayersRectangleCodec.Write(
		ref memory, LayersRectangleCodec.At(address,
			(uint)GraphicsLayout.RegionRectangle.Bounds), value);

	public static APTR BoundsAddress(APTR address) => LayersRectangleCodec.At(
		address, (uint)GraphicsLayout.RegionRectangle.Bounds);
}

/// <summary>Typed access boundary for the public SDK <see cref="BitMap"/>.</summary>
public static class LayersBitMapCodec
{
	public const uint Size = BitMap.Size;
	private const int PlaneCount = 8;

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);

	public static void Clear<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.Clear(address, Size);

	public static ushort ReadBytesPerRow<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		GraphicsLayout.BitMap.BytesPerRow);

	public static void WriteBytesPerRow<TMemory>(ref TMemory memory,
		APTR address, ushort value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.BitMap.BytesPerRow, value);

	public static ushort ReadRows<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		GraphicsLayout.BitMap.Rows);

	public static void WriteRows<TMemory>(ref TMemory memory, APTR address,
		ushort value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.BitMap.Rows, value);

	public static BitMapFlags ReadFlags<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => (BitMapFlags)
		memory.ReadUInt8(address, GraphicsLayout.BitMap.Flags);

	public static void WriteFlags<TMemory>(ref TMemory memory, APTR address,
		BitMapFlags value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.BitMap.Flags, (byte)value);

	public static byte ReadDepth<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		GraphicsLayout.BitMap.Depth);

	public static void WriteDepth<TMemory>(ref TMemory memory, APTR address,
		byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.BitMap.Depth, value);

	public static APTR ReadPlane<TMemory>(ref TMemory memory, APTR address,
		int index)
		where TMemory : struct, IAmigaGuestMemory => index < 0 ||
		index >= PlaneCount ? APTR.Null : APTR.FromPointer(memory.ReadUInt32(address,
		GraphicsLayout.BitMap.Plane0 + index * 4));

	public static void WritePlane<TMemory>(ref TMemory memory, APTR address,
		int index, APTR value)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (index < 0 || index >= PlaneCount) return;
		memory.WriteUInt32(address, GraphicsLayout.BitMap.Plane0 + index * 4,
			value.Raw);
	}
}

/// <summary>Typed access boundary for the public SDK <see cref="RastPort"/>.</summary>
public static class LayersRastPortCodec
{
	public const uint Size = RastPort.Size;
	public const int LayerOffset = GraphicsLayout.RastPort.Layer;

	public static APTR BitMapAddress(APTR address) =>
		LayersRectangleCodec.At(address, (uint)GraphicsLayout.RastPort.BitMap);

	public static APTR LayerAddress(APTR address) =>
		LayersRectangleCodec.At(address, (uint)GraphicsLayout.RastPort.Layer);

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);

	public static void Clear<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.Clear(address, Size);

	public static APTR ReadLayer<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, GraphicsLayout.RastPort.Layer));

	public static void WriteLayer<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.RastPort.Layer, value.Raw);

	public static APTR ReadBitMap<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, GraphicsLayout.RastPort.BitMap));

	public static void WriteBitMap<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.RastPort.BitMap, value.Raw);

	public static void WriteForegroundPen<TMemory>(ref TMemory memory,
		APTR address, byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.RastPort.ForegroundPen, value);
	public static byte ReadForegroundPen<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		GraphicsLayout.RastPort.ForegroundPen);

	public static void WriteBackgroundPen<TMemory>(ref TMemory memory,
		APTR address, byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.RastPort.BackgroundPen, value);
	public static byte ReadBackgroundPen<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		GraphicsLayout.RastPort.BackgroundPen);

	public static void WriteOutlinePen<TMemory>(ref TMemory memory,
		APTR address, byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.RastPort.OutlinePen, value);

	public static void WriteDrawMode<TMemory>(ref TMemory memory, APTR address,
		byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.RastPort.DrawMode, value);
	public static byte ReadDrawMode<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		GraphicsLayout.RastPort.DrawMode);

	public static void WriteMask<TMemory>(ref TMemory memory, APTR address,
		byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.RastPort.Mask, value);
	public static byte ReadMask<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		GraphicsLayout.RastPort.Mask);

	public static void WriteLinePattern<TMemory>(ref TMemory memory,
		APTR address, ushort value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.RastPort.LinePattern, value);
	public static ushort ReadLinePattern<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		GraphicsLayout.RastPort.LinePattern);

	public static byte ReadLinePatternCount<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		GraphicsLayout.RastPort.LinePatternCount);
	public static void WriteLinePatternCount<TMemory>(ref TMemory memory,
		APTR address, byte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		GraphicsLayout.RastPort.LinePatternCount, value);

	public static short ReadCurrentX<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, GraphicsLayout.RastPort.CurrentX));
	public static void WriteCurrentX<TMemory>(ref TMemory memory, APTR address,
		short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.RastPort.CurrentX, unchecked((ushort)value));
	public static short ReadCurrentY<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, GraphicsLayout.RastPort.CurrentY));
	public static void WriteCurrentY<TMemory>(ref TMemory memory, APTR address,
		short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.RastPort.CurrentY, unchecked((ushort)value));

	public static short ReadTextBaseline<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, GraphicsLayout.RastPort.TextBaseline));
	public static void WriteTextBaseline<TMemory>(ref TMemory memory,
		APTR address, short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.RastPort.TextBaseline, unchecked((ushort)value));
	public static short ReadTextHeight<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, GraphicsLayout.RastPort.TextHeight));
	public static void WriteTextHeight<TMemory>(ref TMemory memory,
		APTR address, short value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		GraphicsLayout.RastPort.TextHeight, unchecked((ushort)value));

	public static void WriteAreaInfo<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.RastPort.AreaInfo, value.Raw);
	public static APTR ReadAreaInfo<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, GraphicsLayout.RastPort.AreaInfo));

	public static void WriteTemporaryRaster<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		GraphicsLayout.RastPort.TemporaryRaster, value.Raw);

	public static void InitializeLayerDefaults<TMemory>(ref TMemory memory,
		APTR address, APTR bitMap, APTR layer)
		where TMemory : struct, IAmigaGuestMemory
	{
		Clear(ref memory, address);
		WriteLayer(ref memory, address, layer);
		WriteBitMap(ref memory, address, bitMap);
		memory.WriteUInt8(address, GraphicsLayout.RastPort.Mask, 0xFF);
		memory.WriteUInt8(address, GraphicsLayout.RastPort.ForegroundPen, 0xFF);
		memory.WriteUInt8(address, GraphicsLayout.RastPort.BackgroundPen, 0);
		memory.WriteUInt8(address, GraphicsLayout.RastPort.OutlinePen, 0xFF);
		memory.WriteUInt8(address, GraphicsLayout.RastPort.DrawMode, 1);
		memory.WriteUInt8(address, GraphicsLayout.RastPort.LinePatternCount, 0);
		memory.WriteUInt16(address, GraphicsLayout.RastPort.LinePattern, 0xFFFF);
		for (var index = 0; index < 8; index++)
		{
			memory.WriteUInt8(address,
				GraphicsLayout.RastPort.Minterm0 + index, 0xCA);
		}
		memory.WriteUInt16(address, GraphicsLayout.RastPort.PenWidth, 1);
		memory.WriteUInt16(address, GraphicsLayout.RastPort.PenHeight, 1);
	}
}

/// <summary>Typed access boundary for the public Exec <see cref="MinList"/>.</summary>
public static class LayersMinListCodec
{
	public const uint Size = MinList.Size;
	public static APTR TailAddress(APTR address) => LayersRectangleCodec.At(
		address, (uint)ExecLayout.MinList.Tail);
	public static APTR ReadHead<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.MinList.Head));
	public static APTR ReadTail<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.MinList.Tail));
	public static APTR ReadTailPred<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.MinList.TailPred));
	public static void Initialize<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory
	{
		var tail = TailAddress(address);
		memory.WriteUInt32(address, ExecLayout.MinList.Head, tail.Raw);
		memory.WriteUInt32(address, ExecLayout.MinList.Tail, 0);
		memory.WriteUInt32(address, ExecLayout.MinList.TailPred, address.Raw);
	}
}

/// <summary>
/// Typed access boundary for the public Exec <see cref="SignalSemaphore"/>.
/// </summary>
public static class LayersSignalSemaphoreCodec
{
	public const uint Size = SignalSemaphore.Size;
	public static APTR WaitQueueAddress(APTR address) =>
		LayersRectangleCodec.At(address,
			(uint)ExecLayout.SignalSemaphore.WaitQueue);
	public static APTR ReadOwner<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.SignalSemaphore.Owner));
	public static short ReadNestCount<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => unchecked((short)memory.ReadUInt16(
		address, ExecLayout.SignalSemaphore.NestCount));
	public static short ReadQueueCount<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => unchecked((short)memory.ReadUInt16(
		address, ExecLayout.SignalSemaphore.QueueCount));
	public static void WriteOwner<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.SignalSemaphore.Owner, value.Raw);
	public static void WriteNestCount<T>(ref T memory, APTR address, short value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		ExecLayout.SignalSemaphore.NestCount, unchecked((ushort)value));
	public static void WriteQueueCount<T>(ref T memory, APTR address, short value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		ExecLayout.SignalSemaphore.QueueCount, unchecked((ushort)value));
}

/// <summary>Typed access boundary for public Exec <see cref="Node"/> links.</summary>
public static class LayersExecNodeCodec
{
	public const uint Size = Node.Size;
	public const uint LinksSize = MinNode.Size;
	public const uint LinkPointerSize = 4;
	public static APTR ReadNext<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Node.Successor));
	public static void WriteNext<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Node.Successor, value.Raw);
	public static APTR ReadPrevious<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Node.Predecessor));
	public static void WritePrevious<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Node.Predecessor, value.Raw);
	public static void WriteType<T>(ref T memory, APTR address, NodeType value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		ExecLayout.Node.Type, (byte)value);
	public static void WritePriority<T>(ref T memory, APTR address, sbyte value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
			ExecLayout.Node.Priority, unchecked((byte)value));

	public static sbyte ReadPriority<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => unchecked((sbyte)
			memory.ReadUInt8(address, ExecLayout.Node.Priority));
	public static void WriteName<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
			ExecLayout.Node.Name, value.Raw);

	public static APTR ReadName<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
			memory.ReadUInt32(address, ExecLayout.Node.Name));
}

/// <summary>Typed access boundary for the public Exec <see cref="List"/>.</summary>
public static class LayersExecListCodec
{
	public const uint Size = List.Size;
	public const uint LinksSize = MinList.Size;
	public static APTR ReadHead<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.List.Head));
	public static APTR ReadTailPred<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.List.TailPred));
	public static APTR ReadTail<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.List.Tail));
	public static void WriteHead<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.List.Head, value.Raw);
	public static void WriteTail<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.List.Tail, value.Raw);
	public static void WriteTailPred<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.List.TailPred, value.Raw);
	public static void WriteType<T>(ref T memory, APTR address, NodeType value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		ExecLayout.List.Type, (byte)value);
	public static void Initialize<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory
	{
		WriteHead(ref memory, address, TailAddress(address));
		WriteTail(ref memory, address, APTR.Null);
		WriteTailPred(ref memory, address, address);
	}
	public static APTR TailAddress(APTR address) => LayersRectangleCodec.At(
		address, (uint)ExecLayout.List.Tail);
}

/// <summary>Typed access boundary for the public Exec <see cref="Library"/>.</summary>
public static class LayersLibraryCodec
{
	public const uint Size = Library.Size;
	public static ushort ReadNegativeSize<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		ExecLayout.Library.NegativeSize);
	public static void WriteNegativeSize<T>(ref T memory, APTR address,
		ushort value) where T : struct, IAmigaGuestMemory => memory.WriteUInt16(
		address, ExecLayout.Library.NegativeSize, value);
	public static ushort ReadPositiveSize<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		ExecLayout.Library.PositiveSize);
	public static void WritePositiveSize<T>(ref T memory, APTR address,
		ushort value) where T : struct, IAmigaGuestMemory => memory.WriteUInt16(
		address, ExecLayout.Library.PositiveSize, value);
	public static LibraryFlags ReadFlags<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => (LibraryFlags)memory.ReadUInt8(
		address, ExecLayout.Library.Flags);
	public static void WriteFlags<T>(ref T memory, APTR address,
		LibraryFlags value) where T : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, ExecLayout.Library.Flags, (byte)value);
	public static ushort ReadOpenCount<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		ExecLayout.Library.OpenCount);
	public static void WriteOpenCount<T>(ref T memory, APTR address,
		ushort value) where T : struct, IAmigaGuestMemory => memory.WriteUInt16(
		address, ExecLayout.Library.OpenCount, value);
	public static void WriteVersion<T>(ref T memory, APTR address, ushort value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
			ExecLayout.Library.Version, value);

	public static ushort ReadVersion<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
			ExecLayout.Library.Version);
	public static void WriteRevision<T>(ref T memory, APTR address, ushort value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
			ExecLayout.Library.Revision, value);
	public static ushort ReadRevision<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
			ExecLayout.Library.Revision);
	public static void WriteIdString<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
			ExecLayout.Library.IdString, value.Raw);
	public static APTR ReadIdString<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
			memory.ReadUInt32(address, ExecLayout.Library.IdString));
}

/// <summary>Typed access boundary for the public Exec <see cref="Resident"/>.</summary>
public static class LayersResidentCodec
{
	public const uint Size = Resident.Size;
	public static ushort ReadMatchWord<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt16(address,
		ExecLayout.Resident.MatchWord);
	public static APTR ReadMatchTag<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Resident.MatchTag));
	public static APTR ReadEndSkip<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Resident.EndSkip));
	public static byte ReadVersion<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		ExecLayout.Resident.Version);
	public static ResidentFlags ReadFlags<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => (ResidentFlags)memory.ReadUInt8(
		address, ExecLayout.Resident.Flags);
	public static NodeType ReadType<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => (NodeType)memory.ReadUInt8(address,
		ExecLayout.Resident.Type);
	public static APTR ReadInit<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Resident.Init));
	public static APTR ReadName<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Resident.Name));
	public static APTR ReadIdString<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Resident.IdString));
	public static Resident Read<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => new()
		{
			MatchWord = ReadMatchWord(ref memory, address),
			MatchTag = ReadMatchTag(ref memory, address),
			EndSkip = APTR.FromPointer(memory.ReadUInt32(address,
				ExecLayout.Resident.EndSkip)),
			Flags = ReadFlags(ref memory, address),
			Version = ReadVersion(ref memory, address),
			Type = (byte)ReadType(ref memory, address),
			Priority = unchecked((sbyte)memory.ReadUInt8(address,
				ExecLayout.Resident.Priority)),
			Name = memory.ReadUInt32(address, ExecLayout.Resident.Name),
			IdString = memory.ReadUInt32(address, ExecLayout.Resident.IdString),
			Init = ReadInit(ref memory, address),
		};
	public static void WriteMatchWord<T>(ref T memory, APTR address,
		ushort value) where T : struct, IAmigaGuestMemory => memory.WriteUInt16(
		address, ExecLayout.Resident.MatchWord, value);
	public static void WriteMatchTag<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Resident.MatchTag, value.Raw);
	public static void WriteEndSkip<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Resident.EndSkip, value.Raw);
	public static void WriteFlags<T>(ref T memory, APTR address,
		ResidentFlags value) where T : struct, IAmigaGuestMemory =>
		memory.WriteUInt8(address, ExecLayout.Resident.Flags, (byte)value);
	public static void WriteVersion<T>(ref T memory, APTR address, byte value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		ExecLayout.Resident.Version, value);
	public static void WriteType<T>(ref T memory, APTR address, NodeType value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		ExecLayout.Resident.Type, (byte)value);
	public static void WritePriority<T>(ref T memory, APTR address, sbyte value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		ExecLayout.Resident.Priority, unchecked((byte)value));
	public static void WriteName<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Resident.Name, value.Raw);
	public static void WriteIdString<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Resident.IdString, value.Raw);
	public static void WriteInit<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Resident.Init, value.Raw);
	public static void Write<T>(ref T memory, APTR address, Resident value)
		where T : struct, IAmigaGuestMemory
	{
		WriteMatchWord(ref memory, address, value.MatchWord);
		WriteMatchTag(ref memory, address, value.MatchTag);
		WriteEndSkip(ref memory, address, value.EndSkip);
		WriteFlags(ref memory, address, value.Flags);
		WriteVersion(ref memory, address, value.Version);
		WriteType(ref memory, address, (NodeType)value.Type);
		WritePriority(ref memory, address, value.Priority);
		WriteName(ref memory, address, APTR.FromPointer(value.Name.Raw));
		WriteIdString(ref memory, address, APTR.FromPointer(value.IdString.Raw));
		WriteInit(ref memory, address, value.Init);
	}
}

/// <summary>Typed access boundary for the public Exec resident auto-init table.</summary>
public static class LayersResidentAutoInitCodec
{
	public const uint Size = ResidentAutoInit.Size;
	public static uint ReadDataSize<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		ExecLayout.ResidentAutoInit.DataSize);
	public static APTR ReadFunctionTable<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(memory.ReadUInt32(
		address, ExecLayout.ResidentAutoInit.FunctionTable));
	public static APTR ReadStructureTable<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(memory.ReadUInt32(
		address, ExecLayout.ResidentAutoInit.StructureTable));
	public static APTR ReadInitFunction<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(memory.ReadUInt32(
		address, ExecLayout.ResidentAutoInit.InitFunction));
	public static ResidentAutoInit Read<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => new()
		{
			DataSize = ReadDataSize(ref memory, address),
			FunctionTable = ReadFunctionTable(ref memory, address),
			StructureTable = ReadStructureTable(ref memory, address),
			InitFunction = ReadInitFunction(ref memory, address),
		};
	public static void Write<T>(ref T memory, APTR address,
		ResidentAutoInit value) where T : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.DataSize,
			value.DataSize);
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.FunctionTable,
			value.FunctionTable.Raw);
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.StructureTable,
			value.StructureTable.Raw);
		memory.WriteUInt32(address, ExecLayout.ResidentAutoInit.InitFunction,
			value.InitFunction.Raw);
	}
	public static void WriteDataSize<T>(ref T memory, APTR address, uint value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.ResidentAutoInit.DataSize, value);
	public static void WriteFunctionTable<T>(ref T memory, APTR address,
		APTR value) where T : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, ExecLayout.ResidentAutoInit.FunctionTable, value.Raw);
	public static void WriteStructureTable<T>(ref T memory, APTR address,
		APTR value) where T : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, ExecLayout.ResidentAutoInit.StructureTable, value.Raw);
	public static void WriteInitFunction<T>(ref T memory, APTR address,
		APTR value) where T : struct, IAmigaGuestMemory => memory.WriteUInt32(
		address, ExecLayout.ResidentAutoInit.InitFunction, value.Raw);
}

/// <summary>Typed access boundary for the public ExecBase fields Layers needs.</summary>
public static class LayersExecBaseCodec
{
	public const uint Size = ExecBase.Size;

	public static APTR ReadCurrentTask<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.ExecBase.ThisTask));
	public static void WriteCurrentTask<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.ExecBase.ThisTask, value.Raw);
	public static APTR LibraryListAddress(APTR address) =>
		LayersRectangleCodec.At(address, (uint)ExecLayout.ExecBase.LibraryList);
	public static APTR TaskReadyAddress(APTR address) =>
		LayersRectangleCodec.At(address, (uint)ExecLayout.ExecBase.TaskReady);
}

/// <summary>Typed access boundary for public Exec <see cref="Task"/> fields.</summary>
public static class LayersExecTaskCodec
{
	public const uint Size = Task.Size;
	public static byte ReadState<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.ReadUInt8(address,
		ExecLayout.Task.State);
	public static void WriteState<T>(ref T memory, APTR address, byte value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		ExecLayout.Task.State, value);
	public static APTR ReadStackPointer<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, ExecLayout.Task.StackPointer));
	public static void WriteStackPointer<T>(ref T memory, APTR address,
		APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		ExecLayout.Task.StackPointer, value.Raw);
}

/// <summary>Typed access boundary for the public Utility <see cref="Hook"/>.</summary>
public static class LayersHookCodec
{
	public const uint Size = Hook.Size;
	public static void Clear<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => memory.Clear(address, Size);
	public static APTR ReadEntry<T>(ref T memory, APTR address)
		where T : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, UtilityLayout.Hook.Entry));
	public static void WriteEntry<T>(ref T memory, APTR address, APTR value)
		where T : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		UtilityLayout.Hook.Entry, value.Raw);
}

/// <summary>
/// Owner-specific big-endian codec for the public SDK <see cref="ClipRect"/>
/// envelope. Portable algorithms consume the typed SDK value or these named
/// field operations; byte offsets remain confined to this codec.
/// </summary>
public static class LayersClipRectCodec
{
	public const uint Size = ClipRect.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out ClipRect value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address)) return false;

		value.Next = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.ClipRect.Next));
		value.Previous = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.ClipRect.Previous));
		value.ObscuringLayer = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.ClipRect.ObscuringLayer));
		value.BitMap = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.ClipRect.BitMap));
		value.Bounds = ReadBounds(ref memory, address);
		value.ReservedPointer1 = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.ClipRect.ReservedPointer1));
		value.ReservedPointer2 = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.ClipRect.ReservedPointer2));
		value.Reserved = unchecked((int)memory.ReadUInt32(address,
			LayersLayout.ClipRect.Reserved));
		return true;
	}

	public static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);

	public static void Clear<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.Clear(address, Size);

	public static APTR ReadNext<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.ClipRect.Next));

	public static void WriteNext<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.ClipRect.Next, value.Raw);

	public static void WriteReservedLink<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.ClipRect.Previous, value.Raw);

	public static APTR ReadReservedLink<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.ClipRect.Previous));

	public static void WriteObscuringLayer<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.ClipRect.ObscuringLayer, value.Raw);

	public static void WriteBitMap<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.ClipRect.BitMap, value.Raw);

	public static APTR ReadBitMap<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.ClipRect.BitMap));

	public static APTR ReadObscuringLayer<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.ClipRect.ObscuringLayer));

	public static APTR ReadReservedPointer1<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.ClipRect.ReservedPointer1));

	public static void WriteReservedPointer1<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.ClipRect.ReservedPointer1, value.Raw);

	public static Rectangle ReadBounds<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory
		=> LayersRectangleCodec.Read(ref memory,
			Add(address, (uint)LayersLayout.ClipRect.Bounds));

	public static void WriteBounds<TMemory>(ref TMemory memory, APTR address,
		Rectangle bounds)
		where TMemory : struct, IAmigaGuestMemory
		=> LayersRectangleCodec.Write(ref memory,
			Add(address, (uint)LayersLayout.ClipRect.Bounds), bounds);

	public static APTR BoundsAddress(APTR address) =>
		Add(address, (uint)LayersLayout.ClipRect.Bounds);

	public static uint ReadPackedBoundsFirst<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		LayersLayout.ClipRect.Bounds);

	public static uint ReadPackedBoundsSecond<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		LayersLayout.ClipRect.Bounds + 4);

	private static APTR Add(APTR address, uint offset) =>
		APTR.FromPointer(address.Raw + offset);
}

/// <summary>
/// Typed codec for the public SDK <see cref="Layer"/> envelope. Portable
/// algorithms use named field operations or typed SDK values; public byte
/// offsets remain confined to this codec.
/// </summary>
public static class LayersLayerCodec
{
	public const uint Size = Layer.Size;

	public static uint ReadPackedBoundsFirst<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		LayersLayout.Layer.Bounds);

	public static uint ReadPackedBoundsSecond<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		LayersLayout.Layer.Bounds + 4);

	public static uint ReadPackedScroll<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		LayersLayout.Layer.ScrollX);

	public static bool TryReadClipRect<TMemory>(ref TMemory memory,
		APTR address, out APTR clipRect)
		where TMemory : struct, IAmigaGuestMemory
	{
		clipRect = APTR.Null;
		if (!IsMapped(ref memory, address)) return false;
		clipRect = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.Layer.ClipRect));
		return true;
	}

	public static APTR ReadClipRect<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.ClipRect));

	public static void WriteClipRect<TMemory>(ref TMemory memory, APTR address,
		APTR clipRect)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.ClipRect, clipRect.Raw);

	public static APTR ReadBack<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.Back));

	public static void WriteBack<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.Back, value.Raw);

	public static APTR ReadFront<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.Front));

	public static void WriteFront<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.Front, value.Raw);

	public static APTR ReadLayerInfo<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.LayerInfo));

	public static void WriteLayerInfo<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.LayerInfo, value.Raw);

	public static APTR ReadClipRegion<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.ClipRegion));

	public static void WriteClipRegion<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.ClipRegion, value.Raw);

	public static APTR ReadRastPort<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.RastPort));

	public static void WriteRastPort<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.RastPort, value.Raw);

	public static APTR ReadBackFill<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.BackFill));

	public static void WriteBackFill<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.BackFill, value.Raw);

	public static LayerFlags ReadFlags<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => (LayerFlags)
		memory.ReadUInt16(address, LayersLayout.Layer.Flags);

	public static void WriteFlags<TMemory>(ref TMemory memory, APTR address,
		LayerFlags value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		LayersLayout.Layer.Flags, (ushort)value);

	public static APTR ReadSuperBitMap<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.SuperBitMap));

	public static void WriteSuperBitMap<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.SuperBitMap, value.Raw);

	public static APTR ReadSuperClipRect<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.SuperClipRect));

	public static APTR ReadSaveClipRects<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.SaveClipRects));

	public static APTR ReadDamageList<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.DamageList));

	public static void WriteDamageList<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.DamageList, value.Raw);

	public static uint ReadReserved1<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => memory.ReadUInt32(address,
		LayersLayout.Layer.Reserved1);

	public static void WriteReserved1<TMemory>(ref TMemory memory,
		APTR address, uint value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.Reserved1, value);

	public static void WriteSaveClipRects<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.SaveClipRects, value.Raw);

	public static void WriteSuperClipRect<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.SuperClipRect, value.Raw);

	public static APTR ReadWindow<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.Layer.Window));

	public static APTR LockAddress(APTR address) => APTR.FromPointer(
		address.Raw + (uint)LayersLayout.Layer.Lock);

	public static Rectangle ReadBounds<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory
		=> LayersRectangleCodec.Read(ref memory, APTR.FromPointer(address.Raw +
			(uint)LayersLayout.Layer.Bounds));

	public static void WriteBounds<TMemory>(ref TMemory memory, APTR address,
		Rectangle bounds)
		where TMemory : struct, IAmigaGuestMemory
		=> LayersRectangleCodec.Write(ref memory, APTR.FromPointer(address.Raw +
			(uint)LayersLayout.Layer.Bounds), bounds);

	public static bool TryReadBounds<TMemory>(ref TMemory memory, APTR address,
		out Rectangle bounds)
		where TMemory : struct, IAmigaGuestMemory
	{
		bounds = default;
		if (!IsMapped(ref memory, address)) return false;
		bounds = ReadBounds(ref memory, address);
		return true;
	}

	public static short ReadScrollX<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, LayersLayout.Layer.ScrollX));

	public static short ReadScrollY<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, LayersLayout.Layer.ScrollY));

	public static void WriteScroll<TMemory>(ref TMemory memory, APTR address,
		short x, short y)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, LayersLayout.Layer.ScrollX,
			unchecked((ushort)x));
		memory.WriteUInt16(address, LayersLayout.Layer.ScrollY,
			unchecked((ushort)y));
	}

	public static short ReadWidth<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, LayersLayout.Layer.Width));

	public static short ReadHeight<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((short)
		memory.ReadUInt16(address, LayersLayout.Layer.Height));

	public static void WriteSize<TMemory>(ref TMemory memory, APTR address,
		short width, short height)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, LayersLayout.Layer.Width,
			unchecked((ushort)width));
		memory.WriteUInt16(address, LayersLayout.Layer.Height,
			unchecked((ushort)height));
	}

	public static void WriteWindow<TMemory>(ref TMemory memory, APTR address,
		APTR window)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.Layer.Window, window.Raw);

	private static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);
}

/// <summary>Typed access boundary for the public SDK <see cref="LayerInfo"/>.</summary>
public static class LayersLayerInfoCodec
{
	public const uint Size = LayerInfo.Size;

	public static APTR ReadTopLayer<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.LayerInfo.TopLayer));

	public static void WriteTopLayer<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.LayerInfo.TopLayer, value.Raw);

	public static APTR LockAddress(APTR address) => APTR.FromPointer(
		address.Raw + (uint)LayersLayout.LayerInfo.Lock);

	public static APTR GraphicsSemaphoreHeadAddress(APTR address) =>
		APTR.FromPointer(address.Raw +
			(uint)LayersLayout.LayerInfo.GraphicsSemaphoreHead);

	public static APTR ReadExtra<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.LayerInfo.Extra));

	public static void WriteExtra<TMemory>(ref TMemory memory, APTR address,
		APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.LayerInfo.Extra, value.Raw);

	public static APTR ReadFreeClipRects<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.LayerInfo.FreeClipRects));

	public static void WriteFreeClipRects<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.LayerInfo.FreeClipRects, value.Raw);

	public static sbyte ReadFattenCount<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((sbyte)
		memory.ReadUInt8(address, LayersLayout.LayerInfo.FattenCount));

	public static void WriteFattenCount<TMemory>(ref TMemory memory,
		APTR address, sbyte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		LayersLayout.LayerInfo.FattenCount, unchecked((byte)value));

	public static LayerInfoFlags ReadFlags<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => (LayerInfoFlags)
		memory.ReadUInt16(address, LayersLayout.LayerInfo.Flags);

	public static void WriteFlags<TMemory>(ref TMemory memory, APTR address,
		LayerInfoFlags value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt16(address,
		LayersLayout.LayerInfo.Flags, (ushort)value);

	public static APTR ReadBlankHook<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => APTR.FromPointer(
		memory.ReadUInt32(address, LayersLayout.LayerInfo.BlankHook));

	public static void WriteBlankHook<TMemory>(ref TMemory memory,
		APTR address, APTR value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt32(address,
		LayersLayout.LayerInfo.BlankHook, value.Raw);

	public static sbyte ReadLockLayersCount<TMemory>(ref TMemory memory,
		APTR address)
		where TMemory : struct, IAmigaGuestMemory => unchecked((sbyte)
		memory.ReadUInt8(address, LayersLayout.LayerInfo.LockLayersCount));

	public static void WriteLockLayersCount<TMemory>(ref TMemory memory,
		APTR address, sbyte value)
		where TMemory : struct, IAmigaGuestMemory => memory.WriteUInt8(address,
		LayersLayout.LayerInfo.LockLayersCount, unchecked((byte)value));
}

/// <summary>
/// Typed big-endian codec for the public MorphOS <see cref="NewLayerHook"/>.
/// </summary>
public static class LayersNewLayerHookCodec
{
	public const uint Size = NewLayerHook.Size;

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out NewLayerHook value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address)) return false;
		value.MinNode.Successor = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.MinNode));
		value.MinNode.Predecessor = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.MinNode + 4));
		value.Entry = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.Entry));
		value.SubEntry = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.SubEntry));
		value.Data = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.Data));
		value.TransparentRegionHook = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.TransparentRegionHook));
		value.TransparentRegion = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.NewLayerHook.TransparentRegion));
		return true;
	}

	public static bool TryWrite<TMemory>(ref TMemory memory, APTR address,
		in NewLayerHook value)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (!IsMapped(ref memory, address)) return false;
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.MinNode,
			value.MinNode.Successor.Raw);
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.MinNode + 4,
			value.MinNode.Predecessor.Raw);
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.Entry,
			value.Entry.Raw);
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.SubEntry,
			value.SubEntry.Raw);
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.Data,
			value.Data.Raw);
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.TransparentRegionHook,
			value.TransparentRegionHook.Raw);
		memory.WriteUInt32(address, LayersLayout.NewLayerHook.TransparentRegion,
			value.TransparentRegion.Raw);
		return true;
	}

	private static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);
}

/// <summary>
/// Typed big-endian codec for public Layers backfill callback messages.
/// </summary>
public static class LayersHookMessageCodec
{
	public static bool TryWrite<TMemory>(ref TMemory memory, APTR address,
		in LayerBackfillMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (!IsMapped(ref memory, address, LayerBackfillMessage.Size)) return false;
		memory.WriteUInt32(address, LayersLayout.LayerBackfillMessage.Layer,
			value.Layer.Raw);
		LayersRectangleCodec.Write(ref memory, LayersRectangleCodec.At(address,
			(uint)LayersLayout.LayerBackfillMessage.Bounds), value.Bounds);
		memory.WriteUInt32(address, LayersLayout.LayerBackfillMessage.OffsetX,
			unchecked((uint)value.OffsetX));
		memory.WriteUInt32(address, LayersLayout.LayerBackfillMessage.OffsetY,
			unchecked((uint)value.OffsetY));
		return true;
	}

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out LayerBackfillMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address, LayerBackfillMessage.Size)) return false;
		value.Layer = APTR.FromPointer(memory.ReadUInt32(address,
			LayersLayout.LayerBackfillMessage.Layer));
		value.Bounds = LayersRectangleCodec.Read(ref memory,
			LayersRectangleCodec.At(address,
				(uint)LayersLayout.LayerBackfillMessage.Bounds));
		value.OffsetX = unchecked((int)memory.ReadUInt32(address,
			LayersLayout.LayerBackfillMessage.OffsetX));
		value.OffsetY = unchecked((int)memory.ReadUInt32(address,
			LayersLayout.LayerBackfillMessage.OffsetY));
		return true;
	}

	public static bool TryWrite<TMemory>(ref TMemory memory, APTR address,
		in LayerInfoBackfillMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (!IsMapped(ref memory, address, LayerInfoBackfillMessage.Size))
			return false;
		memory.WriteUInt32(address, LayersLayout.LayerInfoBackfillMessage.Undefined,
			value.Undefined);
		LayersRectangleCodec.Write(ref memory, LayersRectangleCodec.At(address,
			(uint)LayersLayout.LayerInfoBackfillMessage.Bounds), value.Bounds);
		return true;
	}

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out LayerInfoBackfillMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address, LayerInfoBackfillMessage.Size))
			return false;
		value.Undefined = memory.ReadUInt32(address,
			LayersLayout.LayerInfoBackfillMessage.Undefined);
		value.Bounds = LayersRectangleCodec.Read(ref memory,
			LayersRectangleCodec.At(address,
				(uint)LayersLayout.LayerInfoBackfillMessage.Bounds));
		return true;
	}

	private static bool IsMapped<TMemory>(ref TMemory memory, APTR address,
		uint size)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - size && memory.IsMapped(address, size);
}

/// <summary>
/// Typed big-endian codec for the MorphOS public
/// <see cref="TransparencyMessage"/>.
/// </summary>
public static class LayersTransparencyMessageCodec
{
	public const uint Size = TransparencyMessage.Size;
	private const int LayerOffset = 0;
	private const int RegionOffset = 4;
	private const int NewBoundsOffset = 8;
	private const int OldBoundsOffset = 12;

	public static bool TryWrite<TMemory>(ref TMemory memory, APTR address,
		in TransparencyMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		if (!IsMapped(ref memory, address)) return false;
		memory.WriteUInt32(address, LayerOffset, value.Layer.Raw);
		memory.WriteUInt32(address, RegionOffset, value.Region.Raw);
		memory.WriteUInt32(address, NewBoundsOffset, value.NewBounds.Raw);
		memory.WriteUInt32(address, OldBoundsOffset, value.OldBounds.Raw);
		return true;
	}

	public static bool TryRead<TMemory>(ref TMemory memory, APTR address,
		out TransparencyMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		value = default;
		if (!IsMapped(ref memory, address)) return false;
		value.Layer = APTR.FromPointer(memory.ReadUInt32(address, LayerOffset));
		value.Region = APTR.FromPointer(memory.ReadUInt32(address, RegionOffset));
		value.NewBounds = APTR.FromPointer(memory.ReadUInt32(address,
			NewBoundsOffset));
		value.OldBounds = APTR.FromPointer(memory.ReadUInt32(address,
			OldBoundsOffset));
		return true;
	}

	private static bool IsMapped<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory =>
		address.IsNotNull && (address.Raw & 1) == 0 &&
		address.Raw <= uint.MaxValue - Size && memory.IsMapped(address, Size);
}
