using Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class LayersGuestCodecTests
{
	[Fact]
	public void UtilityTagItemCodecRoundTripsTypedValues()
	{
		var memory = new Memory(64);
		var address = APTR.FromPointer(16);
		var expected = new TagItem { Tag = 0x8000_1234, Data = 0xDEAD_BEEF };
		UtilityTagItemCodec.Write(ref memory, address, expected);
		Assert.True(UtilityTagItemCodec.TryRead(ref memory, address,
			out var actual));
		Assert.Equal(expected.Tag, actual.Tag);
		Assert.Equal(expected.Data, actual.Data);

		var clockAddress = APTR.FromPointer(32);
		var clock = new ClockData
		{
			Second = 59,
			Minute = 58,
			Hour = 23,
			Day = 31,
			Month = 12,
			Year = 2026,
			WeekDay = 4,
		};
		UtilityClockDataCodec.Write(ref memory, clockAddress, clock);
		Assert.True(UtilityClockDataCodec.TryRead(ref memory, clockAddress,
			out var actualClock));
		Assert.Equal(clock.Second, actualClock.Second);
		Assert.Equal(clock.Minute, actualClock.Minute);
		Assert.Equal(clock.Hour, actualClock.Hour);
		Assert.Equal(clock.Day, actualClock.Day);
		Assert.Equal(clock.Month, actualClock.Month);
		Assert.Equal(clock.Year, actualClock.Year);
		Assert.Equal(clock.WeekDay, actualClock.WeekDay);
	}

	[Fact]
	public void LayersAndGraphicsCodecsRoundTripTypedValues()
	{
		var memory = new Memory(4096);
		var rectangleAddress = APTR.FromPointer(32);
		var rectangle = LayersRectangleCodec.Create(-7, 8, 123, 234);
		AssertRectangle(new Rectangle
		{
			MinX = -7,
			MinY = 8,
			MaxX = 123,
			MaxY = 234,
		}, rectangle);
		LayersRectangleCodec.Write(ref memory, rectangleAddress, rectangle);
		AssertRectangle(rectangle, LayersRectangleCodec.Read(ref memory,
			rectangleAddress));
		Assert.True(LayersRectangleCodec.IsMapped(ref memory, rectangleAddress));
		Assert.True(LayersRectangleCodec.TryRead(ref memory, rectangleAddress,
			out var validatedRectangle));
		AssertRectangle(rectangle, validatedRectangle);
		Assert.Equal(rectangleAddress.Raw + 4,
			LayersRectangleCodec.At(rectangleAddress, 4).Raw);
		var individualRectangle = APTR.FromPointer(3800);
		LayersRectangleCodec.Clear(ref memory, individualRectangle);
		LayersRectangleCodec.WriteMinX(ref memory, individualRectangle, -11);
		LayersRectangleCodec.WriteMinY(ref memory, individualRectangle, 12);
		LayersRectangleCodec.WriteMaxX(ref memory, individualRectangle, 13);
		LayersRectangleCodec.WriteMaxY(ref memory, individualRectangle, 14);
		AssertRectangle(LayersRectangleCodec.Create(-11, 12, 13, 14),
			LayersRectangleCodec.Read(ref memory, individualRectangle));

		var region = APTR.FromPointer(64);
		var regionNode = APTR.FromPointer(96);
		LayersRegionCodec.WriteBounds(ref memory, region, rectangle);
		LayersRegionCodec.WriteFirst(ref memory, region, regionNode);
		AssertRectangle(rectangle, LayersRegionCodec.ReadBounds(ref memory, region));
		Assert.Equal(regionNode.Raw, LayersRegionCodec.ReadFirst(ref memory, region).Raw);
		Assert.Equal(region.Raw + 8, LayersRegionCodec.HeadAnchor(region).Raw);

		var next = APTR.FromPointer(128);
		LayersRegionRectangleCodec.WriteNext(ref memory, regionNode, next);
		LayersRegionRectangleCodec.WritePrevious(ref memory, regionNode,
			LayersRegionCodec.HeadAnchor(region));
		LayersRegionRectangleCodec.WriteBounds(ref memory, regionNode, rectangle);
		Assert.Equal(next.Raw,
			LayersRegionRectangleCodec.ReadNext(ref memory, regionNode).Raw);
		Assert.Equal(region.Raw + 8,
			LayersRegionRectangleCodec.ReadPrevious(ref memory, regionNode).Raw);
		AssertRectangle(rectangle,
			LayersRegionRectangleCodec.ReadBounds(ref memory, regionNode));
		Assert.Equal(regionNode.Raw + (uint)GraphicsLayout.RegionRectangle.Bounds,
			LayersRegionRectangleCodec.BoundsAddress(regionNode).Raw);

		var bitMap = APTR.FromPointer(160);
		LayersBitMapCodec.Clear(ref memory, bitMap);
		LayersBitMapCodec.WriteBytesPerRow(ref memory, bitMap, 10);
		LayersBitMapCodec.WriteRows(ref memory, bitMap, 20);
		LayersBitMapCodec.WriteFlags(ref memory, bitMap, BitMapFlags.Standard);
		LayersBitMapCodec.WriteDepth(ref memory, bitMap, 3);
		LayersBitMapCodec.WritePlane(ref memory, bitMap, 2, APTR.FromPointer(512));
		Assert.Equal((ushort)10,
			LayersBitMapCodec.ReadBytesPerRow(ref memory, bitMap));
		Assert.Equal((ushort)20, LayersBitMapCodec.ReadRows(ref memory, bitMap));
		Assert.Equal(BitMapFlags.Standard,
			LayersBitMapCodec.ReadFlags(ref memory, bitMap));
		Assert.Equal((byte)3, LayersBitMapCodec.ReadDepth(ref memory, bitMap));
		Assert.Equal(512u, LayersBitMapCodec.ReadPlane(ref memory, bitMap, 2).Raw);
		Assert.True(LayersBitMapCodec.ReadPlane(ref memory, bitMap, 8).IsNull);
		Assert.True(LayersBitMapCodec.IsMapped(ref memory, bitMap));

		var rastPort = APTR.FromPointer(224);
		LayersRastPortCodec.Clear(ref memory, rastPort);
		LayersRastPortCodec.WriteLayer(ref memory, rastPort, APTR.FromPointer(700));
		LayersRastPortCodec.WriteBitMap(ref memory, rastPort, bitMap);
		Assert.Equal(700u, LayersRastPortCodec.ReadLayer(ref memory, rastPort).Raw);
		Assert.Equal(bitMap.Raw,
			LayersRastPortCodec.ReadBitMap(ref memory, rastPort).Raw);
		Assert.Equal(rastPort.Raw + (uint)GraphicsLayout.RastPort.BitMap,
			LayersRastPortCodec.BitMapAddress(rastPort).Raw);
		Assert.Equal(rastPort.Raw + (uint)GraphicsLayout.RastPort.Layer,
			LayersRastPortCodec.LayerAddress(rastPort).Raw);
		LayersRastPortCodec.InitializeLayerDefaults(ref memory, rastPort, bitMap,
			APTR.FromPointer(704));
		Assert.Equal(704u, LayersRastPortCodec.ReadLayer(ref memory, rastPort).Raw);
		Assert.Equal(bitMap.Raw,
			LayersRastPortCodec.ReadBitMap(ref memory, rastPort).Raw);
		Assert.Equal(0xFF, memory.ReadUInt8(rastPort,
			GraphicsLayout.RastPort.Mask));
		Assert.Equal(0xCA, memory.ReadUInt8(rastPort,
			GraphicsLayout.RastPort.Minterm0 + 7));
		Assert.Equal((ushort)1, memory.ReadUInt16(rastPort,
			GraphicsLayout.RastPort.PenWidth));
		LayersRastPortCodec.WriteForegroundPen(ref memory, rastPort, 3);
		LayersRastPortCodec.WriteBackgroundPen(ref memory, rastPort, 4);
		LayersRastPortCodec.WriteOutlinePen(ref memory, rastPort, 5);
		LayersRastPortCodec.WriteDrawMode(ref memory, rastPort, 6);
		LayersRastPortCodec.WriteMask(ref memory, rastPort, 0x7F);
		LayersRastPortCodec.WriteLinePattern(ref memory, rastPort, 0xA55A);
		LayersRastPortCodec.WriteLinePatternCount(ref memory, rastPort, 7);
		LayersRastPortCodec.WriteCurrentX(ref memory, rastPort, -8);
		LayersRastPortCodec.WriteCurrentY(ref memory, rastPort, 9);
		LayersRastPortCodec.WriteTextBaseline(ref memory, rastPort, 10);
		LayersRastPortCodec.WriteTextHeight(ref memory, rastPort, 11);
		LayersRastPortCodec.WriteAreaInfo(ref memory, rastPort,
			APTR.FromPointer(720));
		LayersRastPortCodec.WriteTemporaryRaster(ref memory, rastPort,
			APTR.FromPointer(724));
		Assert.Equal((byte)3,
			LayersRastPortCodec.ReadForegroundPen(ref memory, rastPort));
		Assert.Equal((byte)4,
			LayersRastPortCodec.ReadBackgroundPen(ref memory, rastPort));
		Assert.Equal((byte)6,
			LayersRastPortCodec.ReadDrawMode(ref memory, rastPort));
		Assert.Equal((byte)0x7F,
			LayersRastPortCodec.ReadMask(ref memory, rastPort));
		Assert.Equal((ushort)0xA55A,
			LayersRastPortCodec.ReadLinePattern(ref memory, rastPort));
		Assert.Equal((byte)7,
			LayersRastPortCodec.ReadLinePatternCount(ref memory, rastPort));
		Assert.Equal((short)-8,
			LayersRastPortCodec.ReadCurrentX(ref memory, rastPort));
		Assert.Equal((short)9,
			LayersRastPortCodec.ReadCurrentY(ref memory, rastPort));
		Assert.Equal((short)10,
			LayersRastPortCodec.ReadTextBaseline(ref memory, rastPort));
		Assert.Equal((short)11,
			LayersRastPortCodec.ReadTextHeight(ref memory, rastPort));
		Assert.Equal(720u,
			LayersRastPortCodec.ReadAreaInfo(ref memory, rastPort).Raw);
		Assert.Equal((byte)5, memory.ReadUInt8(rastPort,
			GraphicsLayout.RastPort.OutlinePen));
		Assert.Equal(724u, memory.ReadUInt32(rastPort,
			GraphicsLayout.RastPort.TemporaryRaster));
		Assert.True(LayersRastPortCodec.IsMapped(ref memory, rastPort));

		var hook = APTR.FromPointer(336);
		LayersHookCodec.Clear(ref memory, hook);
		LayersHookCodec.WriteEntry(ref memory, hook, APTR.FromPointer(0x1234));
		Assert.Equal(0x1234u, LayersHookCodec.ReadEntry(ref memory, hook).Raw);
	}

	[Fact]
	public void LayersPublicEnvelopesRoundTripNamedFields()
	{
		var memory = new Memory(4096);
		var clip = APTR.FromPointer(32);
		LayersClipRectCodec.Clear(ref memory, clip);
		LayersClipRectCodec.WriteNext(ref memory, clip, APTR.FromPointer(100));
		LayersClipRectCodec.WriteReservedLink(ref memory, clip,
			APTR.FromPointer(104));
		LayersClipRectCodec.WriteObscuringLayer(ref memory, clip,
			APTR.FromPointer(108));
		LayersClipRectCodec.WriteBitMap(ref memory, clip, APTR.FromPointer(112));
		LayersClipRectCodec.WriteReservedPointer1(ref memory, clip,
			APTR.FromPointer(116));
		var bounds = new Rectangle { MinX = -2, MinY = 3, MaxX = 40, MaxY = 50 };
		LayersClipRectCodec.WriteBounds(ref memory, clip, bounds);
		Assert.True(LayersClipRectCodec.TryRead(ref memory, clip, out var clipValue));
		Assert.Equal(100u, clipValue.Next.Raw);
		Assert.Equal(104u, clipValue.Previous.Raw);
		Assert.Equal(108u, clipValue.ObscuringLayer.Raw);
		Assert.Equal(112u, clipValue.BitMap.Raw);
		Assert.Equal(116u, clipValue.ReservedPointer1.Raw);
		AssertRectangle(bounds, clipValue.Bounds);
		Assert.Equal(100u, LayersClipRectCodec.ReadNext(ref memory, clip).Raw);
		Assert.Equal(104u,
			LayersClipRectCodec.ReadReservedLink(ref memory, clip).Raw);
		Assert.Equal(108u,
			LayersClipRectCodec.ReadObscuringLayer(ref memory, clip).Raw);
		Assert.Equal(112u,
			LayersClipRectCodec.ReadBitMap(ref memory, clip).Raw);
		Assert.Equal(116u,
			LayersClipRectCodec.ReadReservedPointer1(ref memory, clip).Raw);
		AssertRectangle(bounds, LayersClipRectCodec.ReadBounds(ref memory, clip));
		Assert.Equal(memory.ReadUInt32(clip, LayersLayout.ClipRect.Bounds),
			LayersClipRectCodec.ReadPackedBoundsFirst(ref memory, clip));
		Assert.Equal(memory.ReadUInt32(clip, LayersLayout.ClipRect.Bounds + 4),
			LayersClipRectCodec.ReadPackedBoundsSecond(ref memory, clip));
		Assert.Equal(clip.Raw + (uint)LayersLayout.ClipRect.Bounds,
			LayersClipRectCodec.BoundsAddress(clip).Raw);
		Assert.True(LayersClipRectCodec.IsMapped(ref memory, clip));

		var layer = APTR.FromPointer(128);
		memory.Clear(layer, LayersLayerCodec.Size);
		LayersLayerCodec.WriteClipRect(ref memory, layer, clip);
		LayersLayerCodec.WriteBack(ref memory, layer, APTR.FromPointer(300));
		LayersLayerCodec.WriteFront(ref memory, layer, APTR.FromPointer(304));
		LayersLayerCodec.WriteLayerInfo(ref memory, layer, APTR.FromPointer(308));
		LayersLayerCodec.WriteClipRegion(ref memory, layer, APTR.FromPointer(312));
		LayersLayerCodec.WriteRastPort(ref memory, layer, APTR.FromPointer(316));
		LayersLayerCodec.WriteBackFill(ref memory, layer, APTR.FromPointer(320));
		LayersLayerCodec.WriteFlags(ref memory, layer,
			LayerFlags.Smart | LayerFlags.Refresh);
		LayersLayerCodec.WriteSuperBitMap(ref memory, layer, APTR.FromPointer(324));
		LayersLayerCodec.WriteSuperClipRect(ref memory, layer, APTR.FromPointer(328));
		LayersLayerCodec.WriteSaveClipRects(ref memory, layer, APTR.FromPointer(332));
		LayersLayerCodec.WriteDamageList(ref memory, layer, APTR.FromPointer(336));
		LayersLayerCodec.WriteReserved1(ref memory, layer, 0xA5A5_5A5A);
		LayersLayerCodec.WriteBounds(ref memory, layer, bounds);
		LayersLayerCodec.WriteScroll(ref memory, layer, -4, 5);
		LayersLayerCodec.WriteSize(ref memory, layer, 43, 48);
		LayersLayerCodec.WriteWindow(ref memory, layer, APTR.FromPointer(340));
		Assert.Equal(clip.Raw, LayersLayerCodec.ReadClipRect(ref memory, layer).Raw);
		Assert.Equal(300u, LayersLayerCodec.ReadBack(ref memory, layer).Raw);
		Assert.Equal(304u, LayersLayerCodec.ReadFront(ref memory, layer).Raw);
		Assert.Equal(308u, LayersLayerCodec.ReadLayerInfo(ref memory, layer).Raw);
		Assert.Equal(312u, LayersLayerCodec.ReadClipRegion(ref memory, layer).Raw);
		Assert.Equal(316u, LayersLayerCodec.ReadRastPort(ref memory, layer).Raw);
		Assert.Equal(320u, LayersLayerCodec.ReadBackFill(ref memory, layer).Raw);
		Assert.Equal(LayerFlags.Smart | LayerFlags.Refresh,
			LayersLayerCodec.ReadFlags(ref memory, layer));
		Assert.Equal(324u, LayersLayerCodec.ReadSuperBitMap(ref memory, layer).Raw);
		Assert.Equal(328u, LayersLayerCodec.ReadSuperClipRect(ref memory, layer).Raw);
		Assert.Equal(332u, LayersLayerCodec.ReadSaveClipRects(ref memory, layer).Raw);
		Assert.Equal(336u, LayersLayerCodec.ReadDamageList(ref memory, layer).Raw);
		Assert.Equal(0xA5A5_5A5Au, LayersLayerCodec.ReadReserved1(ref memory, layer));
		AssertRectangle(bounds, LayersLayerCodec.ReadBounds(ref memory, layer));
		Assert.Equal((short)-4, LayersLayerCodec.ReadScrollX(ref memory, layer));
		Assert.Equal((short)5, LayersLayerCodec.ReadScrollY(ref memory, layer));
		Assert.Equal((short)43, LayersLayerCodec.ReadWidth(ref memory, layer));
		Assert.Equal((short)48, LayersLayerCodec.ReadHeight(ref memory, layer));
		Assert.Equal(340u, LayersLayerCodec.ReadWindow(ref memory, layer).Raw);
		Assert.True(LayersLayerCodec.TryReadClipRect(
			ref memory, layer, out var validatedClip));
		Assert.Equal(clip.Raw, validatedClip.Raw);
		Assert.True(LayersLayerCodec.TryReadBounds(
			ref memory, layer, out var validatedBounds));
		AssertRectangle(bounds, validatedBounds);
		Assert.Equal(memory.ReadUInt32(layer, LayersLayout.Layer.Bounds),
			LayersLayerCodec.ReadPackedBoundsFirst(ref memory, layer));
		Assert.Equal(memory.ReadUInt32(layer, LayersLayout.Layer.Bounds + 4),
			LayersLayerCodec.ReadPackedBoundsSecond(ref memory, layer));
		Assert.Equal(memory.ReadUInt32(layer, LayersLayout.Layer.ScrollX),
			LayersLayerCodec.ReadPackedScroll(ref memory, layer));
		Assert.Equal(layer.Raw + (uint)LayersLayout.Layer.Lock,
			LayersLayerCodec.LockAddress(layer).Raw);

		var info = APTR.FromPointer(512);
		memory.Clear(info, LayersLayerInfoCodec.Size);
		LayersLayerInfoCodec.WriteTopLayer(ref memory, info, layer);
		LayersLayerInfoCodec.WriteExtra(ref memory, info, APTR.FromPointer(700));
		LayersLayerInfoCodec.WriteFreeClipRects(ref memory, info, clip);
		LayersLayerInfoCodec.WriteFattenCount(ref memory, info, -3);
		LayersLayerInfoCodec.WriteFlags(ref memory, info,
			LayerInfoFlags.NewLayerInfoCalled);
		LayersLayerInfoCodec.WriteBlankHook(ref memory, info, APTR.FromPointer(704));
		LayersLayerInfoCodec.WriteLockLayersCount(ref memory, info, 2);
		Assert.Equal(layer.Raw, LayersLayerInfoCodec.ReadTopLayer(ref memory, info).Raw);
		Assert.Equal(700u, LayersLayerInfoCodec.ReadExtra(ref memory, info).Raw);
		Assert.Equal(clip.Raw,
			LayersLayerInfoCodec.ReadFreeClipRects(ref memory, info).Raw);
		Assert.Equal((sbyte)-3, LayersLayerInfoCodec.ReadFattenCount(ref memory, info));
		Assert.Equal(LayerInfoFlags.NewLayerInfoCalled,
			LayersLayerInfoCodec.ReadFlags(ref memory, info));
		Assert.Equal(704u, LayersLayerInfoCodec.ReadBlankHook(ref memory, info).Raw);
		Assert.Equal((sbyte)2,
			LayersLayerInfoCodec.ReadLockLayersCount(ref memory, info));
		Assert.Equal(info.Raw + (uint)LayersLayout.LayerInfo.Lock,
			LayersLayerInfoCodec.LockAddress(info).Raw);
		Assert.Equal(info.Raw + (uint)LayersLayout.LayerInfo.GraphicsSemaphoreHead,
			LayersLayerInfoCodec.GraphicsSemaphoreHeadAddress(info).Raw);
	}

	[Fact]
	public void LayersPublicHookAndMessageCodecsRoundTripTypedValues()
	{
		var memory = new Memory(4096);
		var hookAddress = APTR.FromPointer(32);
		var hook = new NewLayerHook
		{
			MinNode = new MinNode
			{
				Successor = APTR.FromPointer(100),
				Predecessor = APTR.FromPointer(104),
			},
			Entry = APTR.FromPointer(108),
			SubEntry = APTR.FromPointer(112),
			Data = APTR.FromPointer(116),
			TransparentRegionHook = APTR.FromPointer(120),
			TransparentRegion = APTR.FromPointer(124),
		};
		Assert.True(LayersNewLayerHookCodec.TryWrite(ref memory, hookAddress,
			in hook));
		Assert.True(LayersNewLayerHookCodec.TryRead(ref memory, hookAddress,
			out var actualHook));
		Assert.Equal(hook.MinNode.Successor.Raw,
			actualHook.MinNode.Successor.Raw);
		Assert.Equal(hook.MinNode.Predecessor.Raw,
			actualHook.MinNode.Predecessor.Raw);
		Assert.Equal(hook.Entry.Raw, actualHook.Entry.Raw);
		Assert.Equal(hook.SubEntry.Raw, actualHook.SubEntry.Raw);
		Assert.Equal(hook.Data.Raw, actualHook.Data.Raw);
		Assert.Equal(hook.TransparentRegionHook.Raw,
			actualHook.TransparentRegionHook.Raw);
		Assert.Equal(hook.TransparentRegion.Raw,
			actualHook.TransparentRegion.Raw);

		var messageAddress = APTR.FromPointer(128);
		var bounds = LayersRectangleCodec.Create(-5, 6, 70, 80);
		var message = new LayerBackfillMessage
		{
			Layer = APTR.FromPointer(200),
			Bounds = bounds,
			OffsetX = -300,
			OffsetY = 400,
		};
		Assert.True(LayersHookMessageCodec.TryWrite(ref memory, messageAddress,
			in message));
		Assert.True(LayersHookMessageCodec.TryRead(ref memory, messageAddress,
			out LayerBackfillMessage actualMessage));
		Assert.Equal(message.Layer.Raw, actualMessage.Layer.Raw);
		AssertRectangle(bounds, actualMessage.Bounds);
		Assert.Equal(message.OffsetX, actualMessage.OffsetX);
		Assert.Equal(message.OffsetY, actualMessage.OffsetY);

		var infoMessage = new LayerInfoBackfillMessage
		{
			Undefined = 0xA55A_5AA5,
			Bounds = bounds,
		};
		Assert.True(LayersHookMessageCodec.TryWrite(ref memory, messageAddress,
			in infoMessage));
		Assert.True(LayersHookMessageCodec.TryRead(ref memory, messageAddress,
			out LayerInfoBackfillMessage actualInfoMessage));
		Assert.Equal(infoMessage.Undefined, actualInfoMessage.Undefined);
		AssertRectangle(bounds, actualInfoMessage.Bounds);

		var transparency = new TransparencyMessage
		{
			Layer = APTR.FromPointer(300),
			Region = APTR.FromPointer(304),
			NewBounds = APTR.FromPointer(308),
			OldBounds = APTR.FromPointer(312),
		};
		Assert.True(LayersTransparencyMessageCodec.TryWrite(ref memory,
			messageAddress, in transparency));
		Assert.True(LayersTransparencyMessageCodec.TryRead(ref memory,
			messageAddress, out var actualTransparency));
		Assert.Equal(transparency.Layer.Raw, actualTransparency.Layer.Raw);
		Assert.Equal(transparency.Region.Raw, actualTransparency.Region.Raw);
		Assert.Equal(transparency.NewBounds.Raw,
			actualTransparency.NewBounds.Raw);
		Assert.Equal(transparency.OldBounds.Raw,
			actualTransparency.OldBounds.Raw);
	}

	[Fact]
	public void ExecCodecsRoundTripTypedValuesAndLinks()
	{
		var memory = new Memory(4096);
		var list = APTR.FromPointer(32);
		LayersMinListCodec.Initialize(ref memory, list);
		Assert.Equal(list.Raw + 4, LayersMinListCodec.ReadHead(ref memory, list).Raw);
		Assert.True(LayersMinListCodec.ReadTail(ref memory, list).IsNull);
		Assert.Equal(list.Raw,
			LayersMinListCodec.ReadTailPred(ref memory, list).Raw);
		Assert.Equal(list.Raw + 4, LayersMinListCodec.TailAddress(list).Raw);

		var semaphore = APTR.FromPointer(64);
		memory.Clear(semaphore, LayersSignalSemaphoreCodec.Size);
		LayersSignalSemaphoreCodec.WriteOwner(ref memory, semaphore,
			APTR.FromPointer(900));
		LayersSignalSemaphoreCodec.WriteNestCount(ref memory, semaphore, -1);
		LayersSignalSemaphoreCodec.WriteQueueCount(ref memory, semaphore, 7);
		Assert.Equal(900u, memory.ReadUInt32(semaphore,
			ExecLayout.SignalSemaphore.Owner));
		Assert.Equal(unchecked((ushort)-1), memory.ReadUInt16(semaphore,
			ExecLayout.SignalSemaphore.NestCount));
		Assert.Equal((ushort)7, memory.ReadUInt16(semaphore,
			ExecLayout.SignalSemaphore.QueueCount));
		Assert.Equal(900u,
			LayersSignalSemaphoreCodec.ReadOwner(ref memory, semaphore).Raw);
		Assert.Equal((short)-1,
			LayersSignalSemaphoreCodec.ReadNestCount(ref memory, semaphore));
		Assert.Equal((short)7,
			LayersSignalSemaphoreCodec.ReadQueueCount(ref memory, semaphore));
		Assert.Equal(semaphore.Raw +
			(uint)ExecLayout.SignalSemaphore.WaitQueue,
			LayersSignalSemaphoreCodec.WaitQueueAddress(semaphore).Raw);

		var node = APTR.FromPointer(128);
		LayersExecNodeCodec.WriteNext(ref memory, node, APTR.FromPointer(140));
		LayersExecNodeCodec.WritePrevious(ref memory, node, APTR.FromPointer(144));
		LayersExecNodeCodec.WriteType(ref memory, node, NodeType.Library);
		LayersExecNodeCodec.WritePriority(ref memory, node, -5);
		LayersExecNodeCodec.WriteName(ref memory, node, APTR.FromPointer(148));
		Assert.Equal(140u, LayersExecNodeCodec.ReadNext(ref memory, node).Raw);
		Assert.Equal(144u, LayersExecNodeCodec.ReadPrevious(ref memory, node).Raw);
		Assert.Equal(148u, LayersExecNodeCodec.ReadName(ref memory, node).Raw);
		Assert.Equal((sbyte)-5,
			LayersExecNodeCodec.ReadPriority(ref memory, node));
		Assert.Equal((byte)NodeType.Library,
			memory.ReadUInt8(node, ExecLayout.Node.Type));
		Assert.Equal(unchecked((byte)-5),
			memory.ReadUInt8(node, ExecLayout.Node.Priority));
		Assert.Equal(148u, memory.ReadUInt32(node, ExecLayout.Node.Name));

		var execList = APTR.FromPointer(160);
		LayersExecListCodec.Initialize(ref memory, execList);
		LayersExecListCodec.WriteType(ref memory, execList, NodeType.Library);
		Assert.Equal(LayersExecListCodec.TailAddress(execList).Raw,
			LayersExecListCodec.ReadHead(ref memory, execList).Raw);
		Assert.Equal((byte)NodeType.Library,
			memory.ReadUInt8(execList, ExecLayout.List.Type));
		LayersExecListCodec.WriteHead(ref memory, execList, APTR.FromPointer(164));
		LayersExecListCodec.WriteTail(ref memory, execList, APTR.Null);
		LayersExecListCodec.WriteTailPred(ref memory, execList,
			APTR.FromPointer(168));
		Assert.Equal(164u, LayersExecListCodec.ReadHead(ref memory, execList).Raw);
		Assert.True(LayersExecListCodec.ReadTail(ref memory, execList).IsNull);
		Assert.Equal(168u,
			LayersExecListCodec.ReadTailPred(ref memory, execList).Raw);

		var library = APTR.FromPointer(192);
		memory.Clear(library, LayersLibraryCodec.Size);
		LayersLibraryCodec.WriteNegativeSize(ref memory, library, 246);
		LayersLibraryCodec.WritePositiveSize(ref memory, library, 88);
		LayersLibraryCodec.WriteFlags(ref memory, library, LibraryFlags.Changed);
		LayersLibraryCodec.WriteOpenCount(ref memory, library, 3);
		LayersLibraryCodec.WriteVersion(ref memory, library, 40);
		Assert.Equal((ushort)40,
			LayersLibraryCodec.ReadVersion(ref memory, library));
		LayersLibraryCodec.WriteRevision(ref memory, library, 1);
		LayersLibraryCodec.WriteIdString(ref memory, library, APTR.FromPointer(999));
		Assert.Equal((ushort)1,
			LayersLibraryCodec.ReadRevision(ref memory, library));
		Assert.Equal(999u,
			LayersLibraryCodec.ReadIdString(ref memory, library).Raw);
		Assert.Equal((ushort)246,
			LayersLibraryCodec.ReadNegativeSize(ref memory, library));
		Assert.Equal((ushort)88,
			LayersLibraryCodec.ReadPositiveSize(ref memory, library));
		Assert.Equal(LibraryFlags.Changed,
			LayersLibraryCodec.ReadFlags(ref memory, library));
		Assert.Equal((ushort)3, LayersLibraryCodec.ReadOpenCount(ref memory, library));

		var residentAddress = APTR.FromPointer(256);
		var resident = new Resident
		{
			MatchWord = 0x4AFC,
			MatchTag = residentAddress,
			EndSkip = APTR.FromPointer(400),
			Flags = ResidentFlags.AutoInit,
			Version = 52,
			Type = (byte)NodeType.Library,
			Priority = -2,
			Name = APTR.FromPointer(404),
			IdString = APTR.FromPointer(408),
			Init = APTR.FromPointer(412),
		};
		LayersResidentCodec.Write(ref memory, residentAddress, resident);
		var residentResult = LayersResidentCodec.Read(ref memory, residentAddress);
		Assert.Equal(resident.MatchWord, residentResult.MatchWord);
		Assert.Equal(resident.MatchTag.Raw, residentResult.MatchTag.Raw);
		Assert.Equal(resident.EndSkip.Raw, residentResult.EndSkip.Raw);
		Assert.Equal(resident.Flags, residentResult.Flags);
		Assert.Equal(resident.Version, residentResult.Version);
		Assert.Equal(resident.Type, residentResult.Type);
		Assert.Equal(resident.Priority, residentResult.Priority);
		Assert.Equal(resident.Name.Raw, residentResult.Name.Raw);
		Assert.Equal(resident.IdString.Raw, residentResult.IdString.Raw);
		Assert.Equal(resident.Init.Raw, residentResult.Init.Raw);
		Assert.Equal(resident.MatchWord,
			LayersResidentCodec.ReadMatchWord(ref memory, residentAddress));
		Assert.Equal(resident.MatchTag.Raw,
			LayersResidentCodec.ReadMatchTag(ref memory, residentAddress).Raw);
		Assert.Equal(resident.EndSkip.Raw,
			LayersResidentCodec.ReadEndSkip(ref memory, residentAddress).Raw);
		Assert.Equal(resident.Flags,
			LayersResidentCodec.ReadFlags(ref memory, residentAddress));
		Assert.Equal(resident.Version,
			LayersResidentCodec.ReadVersion(ref memory, residentAddress));
		Assert.Equal((NodeType)resident.Type,
			LayersResidentCodec.ReadType(ref memory, residentAddress));
		Assert.Equal(resident.Name.Raw,
			LayersResidentCodec.ReadName(ref memory, residentAddress).Raw);
		Assert.Equal(resident.IdString.Raw,
			LayersResidentCodec.ReadIdString(ref memory, residentAddress).Raw);
		Assert.Equal(resident.Init.Raw,
			LayersResidentCodec.ReadInit(ref memory, residentAddress).Raw);

		var residentFields = APTR.FromPointer(600);
		memory.Clear(residentFields, LayersResidentCodec.Size);
		LayersResidentCodec.WriteMatchWord(ref memory, residentFields, 0x4AFC);
		LayersResidentCodec.WriteMatchTag(ref memory, residentFields,
			residentFields);
		LayersResidentCodec.WriteEndSkip(ref memory, residentFields,
			APTR.FromPointer(700));
		LayersResidentCodec.WriteFlags(ref memory, residentFields,
			ResidentFlags.AutoInit);
		LayersResidentCodec.WriteVersion(ref memory, residentFields, 40);
		LayersResidentCodec.WriteType(ref memory, residentFields,
			NodeType.Library);
		LayersResidentCodec.WritePriority(ref memory, residentFields, -3);
		LayersResidentCodec.WriteName(ref memory, residentFields,
			APTR.FromPointer(704));
		LayersResidentCodec.WriteIdString(ref memory, residentFields,
			APTR.FromPointer(708));
		LayersResidentCodec.WriteInit(ref memory, residentFields,
			APTR.FromPointer(712));
		var individualResident = LayersResidentCodec.Read(
			ref memory, residentFields);
		Assert.Equal((ushort)0x4AFC, individualResident.MatchWord);
		Assert.Equal(residentFields.Raw, individualResident.MatchTag.Raw);
		Assert.Equal(700u, individualResident.EndSkip.Raw);
		Assert.Equal(ResidentFlags.AutoInit, individualResident.Flags);
		Assert.Equal((byte)40, individualResident.Version);
		Assert.Equal((byte)NodeType.Library, individualResident.Type);
		Assert.Equal((sbyte)-3, individualResident.Priority);
		Assert.Equal(704u, individualResident.Name.Raw);
		Assert.Equal(708u, individualResident.IdString.Raw);
		Assert.Equal(712u, individualResident.Init.Raw);

		var autoInitAddress = APTR.FromPointer(448);
		var autoInit = new ResidentAutoInit
		{
			DataSize = 1234,
			FunctionTable = APTR.FromPointer(500),
			StructureTable = APTR.FromPointer(504),
			InitFunction = APTR.FromPointer(508),
		};
		LayersResidentAutoInitCodec.Write(ref memory, autoInitAddress, autoInit);
		var autoInitResult = LayersResidentAutoInitCodec.Read(ref memory,
			autoInitAddress);
		Assert.Equal(autoInit.DataSize, autoInitResult.DataSize);
		Assert.Equal(autoInit.FunctionTable.Raw, autoInitResult.FunctionTable.Raw);
		Assert.Equal(autoInit.StructureTable.Raw, autoInitResult.StructureTable.Raw);
		Assert.Equal(autoInit.InitFunction.Raw, autoInitResult.InitFunction.Raw);
		Assert.Equal(autoInit.DataSize,
			LayersResidentAutoInitCodec.ReadDataSize(ref memory, autoInitAddress));
		Assert.Equal(autoInit.FunctionTable.Raw,
			LayersResidentAutoInitCodec.ReadFunctionTable(ref memory,
				autoInitAddress).Raw);
		Assert.Equal(autoInit.StructureTable.Raw,
			LayersResidentAutoInitCodec.ReadStructureTable(ref memory,
				autoInitAddress).Raw);
		Assert.Equal(autoInit.InitFunction.Raw,
			LayersResidentAutoInitCodec.ReadInitFunction(ref memory,
				autoInitAddress).Raw);
		var autoInitFields = APTR.FromPointer(720);
		LayersResidentAutoInitCodec.WriteDataSize(
			ref memory, autoInitFields, 4321);
		LayersResidentAutoInitCodec.WriteFunctionTable(
			ref memory, autoInitFields, APTR.FromPointer(736));
		LayersResidentAutoInitCodec.WriteStructureTable(
			ref memory, autoInitFields, APTR.FromPointer(740));
		LayersResidentAutoInitCodec.WriteInitFunction(
			ref memory, autoInitFields, APTR.FromPointer(744));
		var individualAutoInit = LayersResidentAutoInitCodec.Read(
			ref memory, autoInitFields);
		Assert.Equal(4321u, individualAutoInit.DataSize);
		Assert.Equal(736u, individualAutoInit.FunctionTable.Raw);
		Assert.Equal(740u, individualAutoInit.StructureTable.Raw);
		Assert.Equal(744u, individualAutoInit.InitFunction.Raw);

		var execBase = APTR.FromPointer(768);
		LayersExecBaseCodec.WriteCurrentTask(ref memory, execBase,
			APTR.FromPointer(0xABCD));
		Assert.Equal(0xABCDu,
			LayersExecBaseCodec.ReadCurrentTask(ref memory, execBase).Raw);
		Assert.Equal(execBase.Raw + (uint)ExecLayout.ExecBase.LibraryList,
			LayersExecBaseCodec.LibraryListAddress(execBase).Raw);
		Assert.Equal(execBase.Raw + (uint)ExecLayout.ExecBase.TaskReady,
			LayersExecBaseCodec.TaskReadyAddress(execBase).Raw);

		var task = APTR.FromPointer(1024);
		LayersExecTaskCodec.WriteState(ref memory, task, (byte)TaskState.Ready);
		LayersExecTaskCodec.WriteStackPointer(ref memory, task,
			APTR.FromPointer(2048));
		Assert.Equal((byte)TaskState.Ready,
			LayersExecTaskCodec.ReadState(ref memory, task));
		Assert.Equal(2048u,
			LayersExecTaskCodec.ReadStackPointer(ref memory, task).Raw);
	}

	private static void AssertRectangle(Rectangle expected, Rectangle actual)
	{
		Assert.Equal(expected.MinX, actual.MinX);
		Assert.Equal(expected.MinY, actual.MinY);
		Assert.Equal(expected.MaxX, actual.MaxX);
		Assert.Equal(expected.MaxY, actual.MaxY);
	}

	private struct Memory : IAmigaGuestMemory
	{
		private readonly byte[] _bytes;

		internal Memory(int size) => _bytes = new byte[size];

		public byte ReadUInt8(APTR address, int offset = 0) =>
			_bytes[Index(address, offset, 1)];

		public ushort ReadUInt16(APTR address, int offset = 0)
		{
			var index = Index(address, offset, 2);
			return (ushort)((_bytes[index] << 8) | _bytes[index + 1]);
		}

		public uint ReadUInt32(APTR address, int offset = 0)
		{
			var index = Index(address, offset, 4);
			return ((uint)_bytes[index] << 24) | ((uint)_bytes[index + 1] << 16) |
				((uint)_bytes[index + 2] << 8) | _bytes[index + 3];
		}

		public void WriteUInt8(APTR address, int offset, byte value) =>
			_bytes[Index(address, offset, 1)] = value;

		public void WriteUInt16(APTR address, int offset, ushort value)
		{
			var index = Index(address, offset, 2);
			_bytes[index] = (byte)(value >> 8);
			_bytes[index + 1] = (byte)value;
		}

		public void WriteUInt32(APTR address, int offset, uint value)
		{
			var index = Index(address, offset, 4);
			_bytes[index] = (byte)(value >> 24);
			_bytes[index + 1] = (byte)(value >> 16);
			_bytes[index + 2] = (byte)(value >> 8);
			_bytes[index + 3] = (byte)value;
		}

		public void Clear(APTR address, uint byteCount) =>
			Array.Clear(_bytes, Index(address, 0, checked((int)byteCount)),
				checked((int)byteCount));

		public void Copy(APTR source, APTR destination, uint byteCount) =>
			Array.Copy(_bytes, Index(source, 0, checked((int)byteCount)), _bytes,
				Index(destination, 0, checked((int)byteCount)), checked((int)byteCount));

		public bool IsMapped(APTR address, uint byteSize) => address.Raw != 0 &&
			address.Raw <= int.MaxValue && byteSize <= int.MaxValue &&
			address.Raw <= (uint)_bytes.Length &&
			byteSize <= (uint)_bytes.Length - address.Raw;

		private int Index(APTR address, int offset, int size)
		{
			if (offset < 0 || size < 0) throw new ArgumentOutOfRangeException();
			var value = checked(address.Raw + (uint)offset);
			if (value > int.MaxValue || size > _bytes.Length - (int)value)
				throw new ArgumentOutOfRangeException();
			return (int)value;
		}
	}
}
