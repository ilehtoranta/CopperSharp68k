/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

namespace Amiga;

/// <summary>Big-endian guest-memory boundary for classic gadget-class ABI structures.</summary>
public static class IntuitionGadgetClassGuestCodec
{
	public static unsafe GadgetInfo ReadGadgetInfo<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		var value = new GadgetInfo
		{
			gi_Screen = Pointer(memory.ReadUInt32(address, 0)),
			gi_Window = Pointer(memory.ReadUInt32(address, 4)),
			gi_Requester = Pointer(memory.ReadUInt32(address, 8)),
			gi_RastPort = Pointer(memory.ReadUInt32(address, 12)),
			gi_Layer = Pointer(memory.ReadUInt32(address, 16)),
			gi_Domain = ReadIBox(ref memory, address, 20),
			gi_Pens = new GadgetInfoPens
			{
				DetailPen = memory.ReadUInt8(address, 28),
				BlockPen = memory.ReadUInt8(address, 29),
			},
			gi_DrInfo = Pointer(memory.ReadUInt32(address, 30)),
		};
		for (var index = 0; index < 6; index++)
			value.gi_Reserved[index] = memory.ReadUInt32(address, 34 + index * 4);
		return value;
	}

	public static unsafe void WriteGadgetInfo<TMemory>(ref TMemory memory,
		APTR address, GadgetInfo value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.gi_Screen.Raw);
		memory.WriteUInt32(address, 4, value.gi_Window.Raw);
		memory.WriteUInt32(address, 8, value.gi_Requester.Raw);
		memory.WriteUInt32(address, 12, value.gi_RastPort.Raw);
		memory.WriteUInt32(address, 16, value.gi_Layer.Raw);
		WriteIBox(ref memory, address, 20, value.gi_Domain);
		memory.WriteUInt8(address, 28, value.gi_Pens.DetailPen);
		memory.WriteUInt8(address, 29, value.gi_Pens.BlockPen);
		memory.WriteUInt32(address, 30, value.gi_DrInfo.Raw);
		for (var index = 0; index < 6; index++)
			memory.WriteUInt32(address, 34 + index * 4, value.gi_Reserved[index]);
	}

	public static gpHitTest ReadHitTest<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		gpht_GInfo = Pointer(memory.ReadUInt32(address, 4)),
		gpht_Mouse = ReadPoint(ref memory, address, 8),
	};

	public static void WriteHitTest<TMemory>(ref TMemory memory, APTR address,
		gpHitTest value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.gpht_GInfo.Raw);
		WritePoint(ref memory, address, 8, value.gpht_Mouse);
	}

	public static gpRender ReadRender<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		gpr_GInfo = Pointer(memory.ReadUInt32(address, 4)),
		gpr_RPort = Pointer(memory.ReadUInt32(address, 8)),
		gpr_Redraw = unchecked((int)memory.ReadUInt32(address, 12)),
	};

	public static void WriteRender<TMemory>(ref TMemory memory, APTR address,
		gpRender value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.gpr_GInfo.Raw);
		memory.WriteUInt32(address, 8, value.gpr_RPort.Raw);
		memory.WriteUInt32(address, 12, unchecked((uint)value.gpr_Redraw));
	}

	public static gpInput ReadInput<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		gpi_GInfo = Pointer(memory.ReadUInt32(address, 4)),
		gpi_IEvent = Pointer(memory.ReadUInt32(address, 8)),
		gpi_Termination = Pointer(memory.ReadUInt32(address, 12)),
		gpi_Mouse = ReadPoint(ref memory, address, 16),
		gpi_TabletData = Pointer(memory.ReadUInt32(address, 20)),
	};

	public static void WriteInput<TMemory>(ref TMemory memory, APTR address,
		gpInput value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.gpi_GInfo.Raw);
		memory.WriteUInt32(address, 8, value.gpi_IEvent.Raw);
		memory.WriteUInt32(address, 12, value.gpi_Termination.Raw);
		WritePoint(ref memory, address, 16, value.gpi_Mouse);
		memory.WriteUInt32(address, 20, value.gpi_TabletData.Raw);
	}

	public static gpGoInactive ReadGoInactive<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		gpgi_GInfo = Pointer(memory.ReadUInt32(address, 4)),
		gpgi_Abort = memory.ReadUInt32(address, 8),
	};

	public static void WriteGoInactive<TMemory>(ref TMemory memory,
		APTR address, gpGoInactive value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.gpgi_GInfo.Raw);
		memory.WriteUInt32(address, 8, value.gpgi_Abort);
	}

	public static gpLayout ReadLayout<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		gpl_GInfo = Pointer(memory.ReadUInt32(address, 4)),
		gpl_Initial = memory.ReadUInt32(address, 8),
	};

	public static void WriteLayout<TMemory>(ref TMemory memory, APTR address,
		gpLayout value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.gpl_GInfo.Raw);
		memory.WriteUInt32(address, 8, value.gpl_Initial);
	}

	private static IBox ReadIBox<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Left = Signed(memory.ReadUInt16(address, offset)),
		Top = Signed(memory.ReadUInt16(address, offset + 2)),
		Width = Signed(memory.ReadUInt16(address, offset + 4)),
		Height = Signed(memory.ReadUInt16(address, offset + 6)),
	};

	private static void WriteIBox<TMemory>(ref TMemory memory, APTR address,
		int offset, IBox value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, offset, unchecked((ushort)value.Left));
		memory.WriteUInt16(address, offset + 2, unchecked((ushort)value.Top));
		memory.WriteUInt16(address, offset + 4, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, offset + 6, unchecked((ushort)value.Height));
	}

	private static Point ReadPoint<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		X = Signed(memory.ReadUInt16(address, offset)),
		Y = Signed(memory.ReadUInt16(address, offset + 2)),
	};

	private static void WritePoint<TMemory>(ref TMemory memory, APTR address,
		int offset, Point value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, offset, unchecked((ushort)value.X));
		memory.WriteUInt16(address, offset + 2, unchecked((ushort)value.Y));
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for classic image-class messages.</summary>
public static class IntuitionImageClassGuestCodec
{
	public static impFrameBox ReadFrameBox<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		imp_ContentsBox = Pointer(memory.ReadUInt32(address, 4)),
		imp_FrameBox = Pointer(memory.ReadUInt32(address, 8)),
		imp_DrInfo = Pointer(memory.ReadUInt32(address, 12)),
		imp_FrameFlags = memory.ReadUInt32(address, 16),
	};

	public static void WriteFrameBox<TMemory>(ref TMemory memory, APTR address,
		impFrameBox value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.imp_ContentsBox.Raw);
		memory.WriteUInt32(address, 8, value.imp_FrameBox.Raw);
		memory.WriteUInt32(address, 12, value.imp_DrInfo.Raw);
		memory.WriteUInt32(address, 16, value.imp_FrameFlags);
	}

	public static impDraw ReadDraw<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		imp_RPort = Pointer(memory.ReadUInt32(address, 4)),
		imp_Offset = ReadPoint(ref memory, address, 8),
		imp_State = memory.ReadUInt32(address, 12),
		imp_DrInfo = Pointer(memory.ReadUInt32(address, 16)),
		imp_Dimensions = ReadDimensions(ref memory, address, 20),
	};

	public static void WriteDraw<TMemory>(ref TMemory memory, APTR address,
		impDraw value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.imp_RPort.Raw);
		WritePoint(ref memory, address, 8, value.imp_Offset);
		memory.WriteUInt32(address, 12, value.imp_State);
		memory.WriteUInt32(address, 16, value.imp_DrInfo.Raw);
		WriteDimensions(ref memory, address, 20, value.imp_Dimensions);
	}

	public static impErase ReadErase<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		imp_RPort = Pointer(memory.ReadUInt32(address, 4)),
		imp_Offset = ReadPoint(ref memory, address, 8),
		imp_Dimensions = ReadDimensions(ref memory, address, 12),
	};

	public static void WriteErase<TMemory>(ref TMemory memory, APTR address,
		impErase value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		memory.WriteUInt32(address, 4, value.imp_RPort.Raw);
		WritePoint(ref memory, address, 8, value.imp_Offset);
		WriteDimensions(ref memory, address, 12, value.imp_Dimensions);
	}

	public static impHitTest ReadHitTest<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		MethodID = memory.ReadUInt32(address, 0),
		imp_Point = ReadPoint(ref memory, address, 4),
		imp_Dimensions = ReadDimensions(ref memory, address, 8),
	};

	public static void WriteHitTest<TMemory>(ref TMemory memory, APTR address,
		impHitTest value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.MethodID);
		WritePoint(ref memory, address, 4, value.imp_Point);
		WriteDimensions(ref memory, address, 8, value.imp_Dimensions);
	}

	private static Point ReadPoint<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		X = Signed(memory.ReadUInt16(address, offset)),
		Y = Signed(memory.ReadUInt16(address, offset + 2)),
	};

	private static ImageDimensions ReadDimensions<TMemory>(ref TMemory memory,
		APTR address, int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Width = Signed(memory.ReadUInt16(address, offset)),
		Height = Signed(memory.ReadUInt16(address, offset + 2)),
	};

	private static void WritePoint<TMemory>(ref TMemory memory, APTR address,
		int offset, Point value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, offset, unchecked((ushort)value.X));
		memory.WriteUInt16(address, offset + 2, unchecked((ushort)value.Y));
	}

	private static void WriteDimensions<TMemory>(ref TMemory memory,
		APTR address, int offset, ImageDimensions value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, offset, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, offset + 2, unchecked((ushort)value.Height));
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for string-gadget hook state.</summary>
public static class IntuitionStringGadgetGuestCodec
{
	public static unsafe StringExtend ReadExtend<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		var value = new StringExtend
		{
			Font = Pointer(memory.ReadUInt32(address, 0)),
			InitialModes = memory.ReadUInt32(address, 8),
			EditHook = Pointer(memory.ReadUInt32(address, 12)),
			WorkBuffer = Pointer(memory.ReadUInt32(address, 16)),
		};
		for (var index = 0; index < 2; index++)
		{
			value.Pens[index] = memory.ReadUInt8(address, 4 + index);
			value.ActivePens[index] = memory.ReadUInt8(address, 6 + index);
		}
		for (var index = 0; index < 4; index++)
			value.Reserved[index] = memory.ReadUInt32(address, 20 + index * 4);
		return value;
	}

	public static unsafe void WriteExtend<TMemory>(ref TMemory memory,
		APTR address, StringExtend value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.Font.Raw);
		for (var index = 0; index < 2; index++)
		{
			memory.WriteUInt8(address, 4 + index, value.Pens[index]);
			memory.WriteUInt8(address, 6 + index, value.ActivePens[index]);
		}
		memory.WriteUInt32(address, 8, value.InitialModes);
		memory.WriteUInt32(address, 12, value.EditHook.Raw);
		memory.WriteUInt32(address, 16, value.WorkBuffer.Raw);
		for (var index = 0; index < 4; index++)
			memory.WriteUInt32(address, 20 + index * 4, value.Reserved[index]);
	}

	public static SGWork ReadWork<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Gadget = Pointer(memory.ReadUInt32(address, 0)),
		StringInfo = Pointer(memory.ReadUInt32(address, 4)),
		WorkBuffer = Pointer(memory.ReadUInt32(address, 8)),
		PrevBuffer = Pointer(memory.ReadUInt32(address, 12)),
		Modes = memory.ReadUInt32(address, 16),
		IEvent = Pointer(memory.ReadUInt32(address, 20)),
		Code = memory.ReadUInt16(address, 24),
		BufferPos = Signed(memory.ReadUInt16(address, 26)),
		NumChars = Signed(memory.ReadUInt16(address, 28)),
		Actions = memory.ReadUInt32(address, 30),
		LongInt = unchecked((int)memory.ReadUInt32(address, 34)),
		GadgetInfo = Pointer(memory.ReadUInt32(address, 38)),
		EditOp = memory.ReadUInt16(address, 42),
	};

	public static void WriteWork<TMemory>(ref TMemory memory, APTR address,
		SGWork value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.Gadget.Raw);
		memory.WriteUInt32(address, 4, value.StringInfo.Raw);
		memory.WriteUInt32(address, 8, value.WorkBuffer.Raw);
		memory.WriteUInt32(address, 12, value.PrevBuffer.Raw);
		memory.WriteUInt32(address, 16, value.Modes);
		memory.WriteUInt32(address, 20, value.IEvent.Raw);
		memory.WriteUInt16(address, 24, value.Code);
		memory.WriteUInt16(address, 26, unchecked((ushort)value.BufferPos));
		memory.WriteUInt16(address, 28, unchecked((ushort)value.NumChars));
		memory.WriteUInt32(address, 30, value.Actions);
		memory.WriteUInt32(address, 34, unchecked((uint)value.LongInt));
		memory.WriteUInt32(address, 38, value.GadgetInfo.Raw);
		memory.WriteUInt16(address, 42, value.EditOp);
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for legacy Preferences.</summary>
public static class IntuitionPreferencesGuestCodec
{
	public static unsafe Preferences Read<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		var value = new Preferences
		{
			FontHeight = unchecked((sbyte)memory.ReadUInt8(address, 0)),
			PrinterPort = memory.ReadUInt8(address, 1),
			BaudRate = memory.ReadUInt16(address, 2),
			KeyRptSpeed = ReadTime(ref memory, address, 4),
			KeyRptDelay = ReadTime(ref memory, address, 12),
			DoubleClick = ReadTime(ref memory, address, 20),
			XOffset = unchecked((sbyte)memory.ReadUInt8(address, 100)),
			YOffset = unchecked((sbyte)memory.ReadUInt8(address, 101)),
			color17 = memory.ReadUInt16(address, 102),
			color18 = memory.ReadUInt16(address, 104),
			color19 = memory.ReadUInt16(address, 106),
			PointerTicks = memory.ReadUInt16(address, 108),
			color0 = memory.ReadUInt16(address, 110),
			color1 = memory.ReadUInt16(address, 112),
			color2 = memory.ReadUInt16(address, 114),
			color3 = memory.ReadUInt16(address, 116),
			ViewXOffset = unchecked((sbyte)memory.ReadUInt8(address, 118)),
			ViewYOffset = unchecked((sbyte)memory.ReadUInt8(address, 119)),
			ViewInitX = Signed(memory.ReadUInt16(address, 120)),
			ViewInitY = Signed(memory.ReadUInt16(address, 122)),
			EnableCLI = unchecked((int)memory.ReadUInt32(address, 124)),
			PrinterType = memory.ReadUInt16(address, 128),
			PrintPitch = memory.ReadUInt16(address, 160),
			PrintQuality = memory.ReadUInt16(address, 162),
			PrintSpacing = memory.ReadUInt16(address, 164),
			PrintLeftMargin = memory.ReadUInt16(address, 166),
			PrintRightMargin = memory.ReadUInt16(address, 168),
			PrintImage = memory.ReadUInt16(address, 170),
			PrintAspect = memory.ReadUInt16(address, 172),
			PrintShade = memory.ReadUInt16(address, 174),
			PrintThreshold = Signed(memory.ReadUInt16(address, 176)),
			PaperSize = memory.ReadUInt16(address, 178),
			PaperLength = memory.ReadUInt16(address, 180),
			PaperType = memory.ReadUInt16(address, 182),
			SerRWBits = memory.ReadUInt8(address, 184),
			SerStopBuf = memory.ReadUInt8(address, 185),
			SerParShk = memory.ReadUInt8(address, 186),
			LaceWB = memory.ReadUInt8(address, 187),
			DefaultPrtUnit = memory.ReadUInt8(address, 216),
			DefaultSerUnit = memory.ReadUInt8(address, 217),
			RowSizeChange = unchecked((sbyte)memory.ReadUInt8(address, 218)),
			ColumnSizeChange = unchecked((sbyte)memory.ReadUInt8(address, 219)),
			PrintFlags = memory.ReadUInt16(address, 220),
			PrintMaxWidth = memory.ReadUInt16(address, 222),
			PrintMaxHeight = memory.ReadUInt16(address, 224),
			PrintDensity = memory.ReadUInt8(address, 226),
			PrintXOffset = memory.ReadUInt8(address, 227),
			wb_Width = memory.ReadUInt16(address, 228),
			wb_Height = memory.ReadUInt16(address, 230),
			wb_Depth = memory.ReadUInt8(address, 232),
			ext_size = memory.ReadUInt8(address, 233),
		};
		for (var index = 0; index < IntuitionPreferencesConstants.POINTERSIZE; index++)
			value.PointerMatrix[index] = memory.ReadUInt16(address, 28 + index * 2);
		for (var index = 0; index < IntuitionPreferencesConstants.FILENAME_SIZE; index++)
			value.PrinterFilename[index] = memory.ReadUInt8(address, 130 + index);
		for (var index = 0; index < 12; index++)
			value.Pad[index] = memory.ReadUInt8(address, 188 + index);
		for (var index = 0; index < IntuitionPreferencesConstants.DEVNAME_SIZE; index++)
			value.PrtDevName[index] = memory.ReadUInt8(address, 200 + index);
		return value;
	}

	public static unsafe void Write<TMemory>(ref TMemory memory, APTR address,
		Preferences value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt8(address, 0, unchecked((byte)value.FontHeight));
		memory.WriteUInt8(address, 1, value.PrinterPort);
		memory.WriteUInt16(address, 2, value.BaudRate);
		WriteTime(ref memory, address, 4, value.KeyRptSpeed);
		WriteTime(ref memory, address, 12, value.KeyRptDelay);
		WriteTime(ref memory, address, 20, value.DoubleClick);
		for (var index = 0; index < IntuitionPreferencesConstants.POINTERSIZE; index++)
			memory.WriteUInt16(address, 28 + index * 2, value.PointerMatrix[index]);
		memory.WriteUInt8(address, 100, unchecked((byte)value.XOffset));
		memory.WriteUInt8(address, 101, unchecked((byte)value.YOffset));
		memory.WriteUInt16(address, 102, value.color17);
		memory.WriteUInt16(address, 104, value.color18);
		memory.WriteUInt16(address, 106, value.color19);
		memory.WriteUInt16(address, 108, value.PointerTicks);
		memory.WriteUInt16(address, 110, value.color0);
		memory.WriteUInt16(address, 112, value.color1);
		memory.WriteUInt16(address, 114, value.color2);
		memory.WriteUInt16(address, 116, value.color3);
		memory.WriteUInt8(address, 118, unchecked((byte)value.ViewXOffset));
		memory.WriteUInt8(address, 119, unchecked((byte)value.ViewYOffset));
		memory.WriteUInt16(address, 120, unchecked((ushort)value.ViewInitX));
		memory.WriteUInt16(address, 122, unchecked((ushort)value.ViewInitY));
		memory.WriteUInt32(address, 124, unchecked((uint)value.EnableCLI));
		memory.WriteUInt16(address, 128, value.PrinterType);
		for (var index = 0; index < IntuitionPreferencesConstants.FILENAME_SIZE; index++)
			memory.WriteUInt8(address, 130 + index, value.PrinterFilename[index]);
		memory.WriteUInt16(address, 160, value.PrintPitch);
		memory.WriteUInt16(address, 162, value.PrintQuality);
		memory.WriteUInt16(address, 164, value.PrintSpacing);
		memory.WriteUInt16(address, 166, value.PrintLeftMargin);
		memory.WriteUInt16(address, 168, value.PrintRightMargin);
		memory.WriteUInt16(address, 170, value.PrintImage);
		memory.WriteUInt16(address, 172, value.PrintAspect);
		memory.WriteUInt16(address, 174, value.PrintShade);
		memory.WriteUInt16(address, 176, unchecked((ushort)value.PrintThreshold));
		memory.WriteUInt16(address, 178, value.PaperSize);
		memory.WriteUInt16(address, 180, value.PaperLength);
		memory.WriteUInt16(address, 182, value.PaperType);
		memory.WriteUInt8(address, 184, value.SerRWBits);
		memory.WriteUInt8(address, 185, value.SerStopBuf);
		memory.WriteUInt8(address, 186, value.SerParShk);
		memory.WriteUInt8(address, 187, value.LaceWB);
		for (var index = 0; index < 12; index++)
			memory.WriteUInt8(address, 188 + index, value.Pad[index]);
		for (var index = 0; index < IntuitionPreferencesConstants.DEVNAME_SIZE; index++)
			memory.WriteUInt8(address, 200 + index, value.PrtDevName[index]);
		memory.WriteUInt8(address, 216, value.DefaultPrtUnit);
		memory.WriteUInt8(address, 217, value.DefaultSerUnit);
		memory.WriteUInt8(address, 218, unchecked((byte)value.RowSizeChange));
		memory.WriteUInt8(address, 219, unchecked((byte)value.ColumnSizeChange));
		memory.WriteUInt16(address, 220, value.PrintFlags);
		memory.WriteUInt16(address, 222, value.PrintMaxWidth);
		memory.WriteUInt16(address, 224, value.PrintMaxHeight);
		memory.WriteUInt8(address, 226, value.PrintDensity);
		memory.WriteUInt8(address, 227, value.PrintXOffset);
		memory.WriteUInt16(address, 228, value.wb_Width);
		memory.WriteUInt16(address, 230, value.wb_Height);
		memory.WriteUInt8(address, 232, value.wb_Depth);
		memory.WriteUInt8(address, 233, value.ext_size);
	}

	private static TimeVal ReadTime<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Seconds = memory.ReadUInt32(address, offset),
		Microseconds = memory.ReadUInt32(address, offset + 4),
	};

	private static void WriteTime<TMemory>(ref TMemory memory, APTR address,
		int offset, TimeVal value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, offset, value.Seconds);
		memory.WriteUInt32(address, offset + 4, value.Microseconds);
	}

	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for the public IntuitionBase prefix.</summary>
public static class IntuitionBaseGuestCodec
{
	public static IntuitionBase Read<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		LibNode = new Library
		{
			Node = new Node
			{
				Successor = Pointer(memory.ReadUInt32(address, 0)),
				Predecessor = Pointer(memory.ReadUInt32(address, 4)),
				Type = memory.ReadUInt8(address, 8),
				Priority = unchecked((sbyte)memory.ReadUInt8(address, 9)),
				Name = Pointer(memory.ReadUInt32(address, 10)),
			},
			Flags = (LibraryFlags)memory.ReadUInt8(address, 14),
			Padding = memory.ReadUInt8(address, 15),
			NegativeSize = memory.ReadUInt16(address, 16),
			PositiveSize = memory.ReadUInt16(address, 18),
			Version = memory.ReadUInt16(address, 20),
			Revision = memory.ReadUInt16(address, 22),
			IdString = Pointer(memory.ReadUInt32(address, 24)),
			Checksum = memory.ReadUInt32(address, 28),
			OpenCount = memory.ReadUInt16(address, 32),
		},
		ViewLord = new View
		{
			ViewPort = Pointer(memory.ReadUInt32(address, 34)),
			LongFrameCopperList = Pointer(memory.ReadUInt32(address, 38)),
			ShortFrameCopperList = Pointer(memory.ReadUInt32(address, 42)),
			YOffset = Signed(memory.ReadUInt16(address, 46)),
			XOffset = Signed(memory.ReadUInt16(address, 48)),
			Modes = memory.ReadUInt16(address, 50),
		},
		ActiveWindow = Pointer(memory.ReadUInt32(address, 52)),
		ActiveScreen = Pointer(memory.ReadUInt32(address, 56)),
		FirstScreen = Pointer(memory.ReadUInt32(address, 60)),
		Flags = memory.ReadUInt32(address, 64),
		MouseY = Signed(memory.ReadUInt16(address, 68)),
		MouseX = Signed(memory.ReadUInt16(address, 70)),
		Seconds = memory.ReadUInt32(address, 72),
		Micros = memory.ReadUInt32(address, 76),
	};

	public static void Write<TMemory>(ref TMemory memory, APTR address,
		IntuitionBase value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.LibNode.Node.Successor.Raw);
		memory.WriteUInt32(address, 4, value.LibNode.Node.Predecessor.Raw);
		memory.WriteUInt8(address, 8, (byte)value.LibNode.Node.Type);
		memory.WriteUInt8(address, 9, unchecked((byte)value.LibNode.Node.Priority));
		memory.WriteUInt32(address, 10, value.LibNode.Node.Name.Raw);
		memory.WriteUInt8(address, 14, (byte)value.LibNode.Flags);
		memory.WriteUInt8(address, 15, value.LibNode.Padding);
		memory.WriteUInt16(address, 16, value.LibNode.NegativeSize);
		memory.WriteUInt16(address, 18, value.LibNode.PositiveSize);
		memory.WriteUInt16(address, 20, value.LibNode.Version);
		memory.WriteUInt16(address, 22, value.LibNode.Revision);
		memory.WriteUInt32(address, 24, value.LibNode.IdString.Raw);
		memory.WriteUInt32(address, 28, value.LibNode.Checksum);
		memory.WriteUInt16(address, 32, value.LibNode.OpenCount);
		memory.WriteUInt32(address, 34, value.ViewLord.ViewPort.Raw);
		memory.WriteUInt32(address, 38, value.ViewLord.LongFrameCopperList.Raw);
		memory.WriteUInt32(address, 42, value.ViewLord.ShortFrameCopperList.Raw);
		memory.WriteUInt16(address, 46, unchecked((ushort)value.ViewLord.YOffset));
		memory.WriteUInt16(address, 48, unchecked((ushort)value.ViewLord.XOffset));
		memory.WriteUInt16(address, 50, value.ViewLord.Modes);
		memory.WriteUInt32(address, 52, value.ActiveWindow.Raw);
		memory.WriteUInt32(address, 56, value.ActiveScreen.Raw);
		memory.WriteUInt32(address, 60, value.FirstScreen.Raw);
		memory.WriteUInt32(address, 64, value.Flags);
		memory.WriteUInt16(address, 68, unchecked((ushort)value.MouseY));
		memory.WriteUInt16(address, 70, unchecked((ushort)value.MouseX));
		memory.WriteUInt32(address, 72, value.Seconds);
		memory.WriteUInt32(address, 76, value.Micros);
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for classic drawing and input messages.</summary>
public static class IntuitionDrawingGuestCodec
{
	public static IntuiText ReadText<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		FrontPen = memory.ReadUInt8(address, 0),
		BackPen = memory.ReadUInt8(address, 1),
		DrawMode = (DrawMode)memory.ReadUInt8(address, 2),
		LeftEdge = Signed(memory.ReadUInt16(address, 4)),
		TopEdge = Signed(memory.ReadUInt16(address, 6)),
		Font = Pointer(memory.ReadUInt32(address, 8)),
		Text = Pointer(memory.ReadUInt32(address, 12)),
		NextText = Pointer(memory.ReadUInt32(address, 16)),
	};

	public static void WriteText<TMemory>(ref TMemory memory, APTR address,
		IntuiText value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt8(address, 0, value.FrontPen);
		memory.WriteUInt8(address, 1, value.BackPen);
		memory.WriteUInt8(address, 2, (byte)value.DrawMode);
		memory.WriteUInt8(address, 3, 0);
		memory.WriteUInt16(address, 4, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.TopEdge));
		memory.WriteUInt32(address, 8, value.Font.Raw);
		memory.WriteUInt32(address, 12, value.Text.Raw);
		memory.WriteUInt32(address, 16, value.NextText.Raw);
	}

	public static Border ReadBorder<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		LeftEdge = Signed(memory.ReadUInt16(address, 0)),
		TopEdge = Signed(memory.ReadUInt16(address, 2)),
		FrontPen = memory.ReadUInt8(address, 4),
		BackPen = memory.ReadUInt8(address, 5),
		DrawMode = (DrawMode)memory.ReadUInt8(address, 6),
		Count = unchecked((sbyte)memory.ReadUInt8(address, 7)),
		XY = Pointer(memory.ReadUInt32(address, 8)),
		NextBorder = Pointer(memory.ReadUInt32(address, 12)),
	};

	public static void WriteBorder<TMemory>(ref TMemory memory, APTR address,
		Border value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 2, unchecked((ushort)value.TopEdge));
		memory.WriteUInt8(address, 4, value.FrontPen);
		memory.WriteUInt8(address, 5, value.BackPen);
		memory.WriteUInt8(address, 6, (byte)value.DrawMode);
		memory.WriteUInt8(address, 7, unchecked((byte)value.Count));
		memory.WriteUInt32(address, 8, value.XY.Raw);
		memory.WriteUInt32(address, 12, value.NextBorder.Raw);
	}

	public static Image ReadImage<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		LeftEdge = Signed(memory.ReadUInt16(address, 0)),
		TopEdge = Signed(memory.ReadUInt16(address, 2)),
		Width = Signed(memory.ReadUInt16(address, 4)),
		Height = Signed(memory.ReadUInt16(address, 6)),
		Depth = Signed(memory.ReadUInt16(address, 8)),
		ImageData = Pointer(memory.ReadUInt32(address, 10)),
		PlanePick = memory.ReadUInt8(address, 14),
		PlaneOnOff = memory.ReadUInt8(address, 15),
		NextImage = Pointer(memory.ReadUInt32(address, 16)),
	};

	public static void WriteImage<TMemory>(ref TMemory memory, APTR address,
		Image value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 2, unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, 4, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.Height));
		memory.WriteUInt16(address, 8, unchecked((ushort)value.Depth));
		memory.WriteUInt32(address, 10, value.ImageData.Raw);
		memory.WriteUInt8(address, 14, value.PlanePick);
		memory.WriteUInt8(address, 15, value.PlaneOnOff);
		memory.WriteUInt32(address, 16, value.NextImage.Raw);
	}

	public static IBox ReadBox<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Left = Signed(memory.ReadUInt16(address, 0)),
		Top = Signed(memory.ReadUInt16(address, 2)),
		Width = Signed(memory.ReadUInt16(address, 4)),
		Height = Signed(memory.ReadUInt16(address, 6)),
	};

	public static void WriteBox<TMemory>(ref TMemory memory, APTR address,
		IBox value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)value.Left));
		memory.WriteUInt16(address, 2, unchecked((ushort)value.Top));
		memory.WriteUInt16(address, 4, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.Height));
	}

	public static TabletData ReadTabletData<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		XFraction = memory.ReadUInt16(address, 0),
		YFraction = memory.ReadUInt16(address, 2),
		TabletX = memory.ReadUInt32(address, 4),
		TabletY = memory.ReadUInt32(address, 8),
		RangeX = memory.ReadUInt32(address, 12),
		RangeY = memory.ReadUInt32(address, 16),
		TagList = Pointer(memory.ReadUInt32(address, 20)),
	};

	public static void WriteTabletData<TMemory>(ref TMemory memory, APTR address,
		TabletData value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, value.XFraction);
		memory.WriteUInt16(address, 2, value.YFraction);
		memory.WriteUInt32(address, 4, value.TabletX);
		memory.WriteUInt32(address, 8, value.TabletY);
		memory.WriteUInt32(address, 12, value.RangeX);
		memory.WriteUInt32(address, 16, value.RangeY);
		memory.WriteUInt32(address, 20, value.TagList.Raw);
	}

	public static TabletHookData ReadTabletHookData<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Screen = Pointer(memory.ReadUInt32(address, 0)),
		Width = memory.ReadUInt32(address, 4),
		Height = memory.ReadUInt32(address, 8),
		ScreenChanged = unchecked((int)memory.ReadUInt32(address, 12)),
	};

	public static void WriteTabletHookData<TMemory>(ref TMemory memory,
		APTR address, TabletHookData value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.Screen.Raw);
		memory.WriteUInt32(address, 4, value.Width);
		memory.WriteUInt32(address, 8, value.Height);
		memory.WriteUInt32(address, 12, unchecked((uint)value.ScreenChanged));
	}

	public static IntuiMessage ReadMessage<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		ExecMessage = new Message
		{
			Node = ReadNode(ref memory, address),
			ReplyPort = Pointer(memory.ReadUInt32(address, 14)),
			Length = memory.ReadUInt16(address, 18),
		},
		Class = (IDCMPFlags)memory.ReadUInt32(address, 20),
		Code = memory.ReadUInt16(address, 24),
		Qualifier = memory.ReadUInt16(address, 26),
		IAddress = Pointer(memory.ReadUInt32(address, 28)),
		MouseX = Signed(memory.ReadUInt16(address, 32)),
		MouseY = Signed(memory.ReadUInt16(address, 34)),
		Seconds = memory.ReadUInt32(address, 36),
		Micros = memory.ReadUInt32(address, 40),
		IDCMPWindow = Pointer(memory.ReadUInt32(address, 44)),
		SpecialLink = Pointer(memory.ReadUInt32(address, 48)),
	};

	public static void WriteMessage<TMemory>(ref TMemory memory, APTR address,
		IntuiMessage value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteNode(ref memory, address, value.ExecMessage.Node);
		memory.WriteUInt32(address, 14, value.ExecMessage.ReplyPort.Raw);
		memory.WriteUInt16(address, 18, value.ExecMessage.Length);
		memory.WriteUInt32(address, 20, (uint)value.Class);
		memory.WriteUInt16(address, 24, value.Code);
		memory.WriteUInt16(address, 26, value.Qualifier);
		memory.WriteUInt32(address, 28, value.IAddress.Raw);
		memory.WriteUInt16(address, 32, unchecked((ushort)value.MouseX));
		memory.WriteUInt16(address, 34, unchecked((ushort)value.MouseY));
		memory.WriteUInt32(address, 36, value.Seconds);
		memory.WriteUInt32(address, 40, value.Micros);
		memory.WriteUInt32(address, 44, value.IDCMPWindow.Raw);
		memory.WriteUInt32(address, 48, value.SpecialLink.Raw);
	}

	public static ExtIntuiMessage ReadExtendedMessage<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		IntuiMessage = ReadMessage(ref memory, address),
		TabletData = Pointer(memory.ReadUInt32(address, 52)),
	};

	public static void WriteExtendedMessage<TMemory>(ref TMemory memory,
		APTR address, ExtIntuiMessage value)
		where TMemory : struct, IAmigaGuestMemory
	{
		WriteMessage(ref memory, address, value.IntuiMessage);
		memory.WriteUInt32(address, 52, value.TabletData.Raw);
	}

	private static Node ReadNode<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Successor = Pointer(memory.ReadUInt32(address, 0)),
		Predecessor = Pointer(memory.ReadUInt32(address, 4)),
		Type = memory.ReadUInt8(address, 8),
		Priority = unchecked((sbyte)memory.ReadUInt8(address, 9)),
		Name = Pointer(memory.ReadUInt32(address, 10)),
	};

	private static void WriteNode<TMemory>(ref TMemory memory, APTR address,
		Node value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.Successor.Raw);
		memory.WriteUInt32(address, 4, value.Predecessor.Raw);
		memory.WriteUInt8(address, 8, value.Type);
		memory.WriteUInt8(address, 9, unchecked((byte)value.Priority));
		memory.WriteUInt32(address, 10, value.Name.Raw);
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for classic menus and requesters.</summary>
public static class IntuitionMenuRequesterGuestCodec
{
	public static Menu ReadMenu<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextMenu = Pointer(memory.ReadUInt32(address, 0)),
		LeftEdge = Signed(memory.ReadUInt16(address, 4)),
		TopEdge = Signed(memory.ReadUInt16(address, 6)),
		Width = Signed(memory.ReadUInt16(address, 8)),
		Height = Signed(memory.ReadUInt16(address, 10)),
		Flags = (MenuFlags)memory.ReadUInt16(address, 12),
		MenuName = Pointer(memory.ReadUInt32(address, 14)),
		FirstItem = Pointer(memory.ReadUInt32(address, 18)),
		JazzX = Signed(memory.ReadUInt16(address, 22)),
		JazzY = Signed(memory.ReadUInt16(address, 24)),
		BeatX = Signed(memory.ReadUInt16(address, 26)),
		BeatY = Signed(memory.ReadUInt16(address, 28)),
	};

	public static void WriteMenu<TMemory>(ref TMemory memory, APTR address,
		Menu value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.NextMenu.Raw);
		memory.WriteUInt16(address, 4, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, 8, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.Height));
		memory.WriteUInt16(address, 12, (ushort)value.Flags);
		memory.WriteUInt32(address, 14, value.MenuName.Raw);
		memory.WriteUInt32(address, 18, value.FirstItem.Raw);
		memory.WriteUInt16(address, 22, unchecked((ushort)value.JazzX));
		memory.WriteUInt16(address, 24, unchecked((ushort)value.JazzY));
		memory.WriteUInt16(address, 26, unchecked((ushort)value.BeatX));
		memory.WriteUInt16(address, 28, unchecked((ushort)value.BeatY));
	}

	public static MenuItem ReadMenuItem<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextItem = Pointer(memory.ReadUInt32(address, 0)),
		LeftEdge = Signed(memory.ReadUInt16(address, 4)),
		TopEdge = Signed(memory.ReadUInt16(address, 6)),
		Width = Signed(memory.ReadUInt16(address, 8)),
		Height = Signed(memory.ReadUInt16(address, 10)),
		Flags = (MenuItemFlags)memory.ReadUInt16(address, 12),
		MutualExclude = unchecked((int)memory.ReadUInt32(address, 14)),
		ItemFill = Pointer(memory.ReadUInt32(address, 18)),
		SelectFill = Pointer(memory.ReadUInt32(address, 22)),
		Command = unchecked((sbyte)memory.ReadUInt8(address, 26)),
		SubItem = Pointer(memory.ReadUInt32(address, 28)),
		NextSelect = memory.ReadUInt16(address, 32),
	};

	public static void WriteMenuItem<TMemory>(ref TMemory memory, APTR address,
		MenuItem value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.NextItem.Raw);
		memory.WriteUInt16(address, 4, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, 8, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.Height));
		memory.WriteUInt16(address, 12, (ushort)value.Flags);
		memory.WriteUInt32(address, 14, unchecked((uint)value.MutualExclude));
		memory.WriteUInt32(address, 18, value.ItemFill.Raw);
		memory.WriteUInt32(address, 22, value.SelectFill.Raw);
		memory.WriteUInt8(address, 26, unchecked((byte)value.Command));
		memory.WriteUInt8(address, 27, 0);
		memory.WriteUInt32(address, 28, value.SubItem.Raw);
		memory.WriteUInt16(address, 32, value.NextSelect);
	}

	public static unsafe Requester ReadRequester<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		var value = new Requester
		{
			OlderRequest = Pointer(memory.ReadUInt32(address, 0)),
			LeftEdge = Signed(memory.ReadUInt16(address, 4)),
			TopEdge = Signed(memory.ReadUInt16(address, 6)),
			Width = Signed(memory.ReadUInt16(address, 8)),
			Height = Signed(memory.ReadUInt16(address, 10)),
			RelativeLeft = Signed(memory.ReadUInt16(address, 12)),
			RelativeTop = Signed(memory.ReadUInt16(address, 14)),
			Gadget = Pointer(memory.ReadUInt32(address, 16)),
			Border = Pointer(memory.ReadUInt32(address, 20)),
			Text = Pointer(memory.ReadUInt32(address, 24)),
			Flags = (RequesterFlags)memory.ReadUInt16(address, 28),
			BackFill = memory.ReadUInt8(address, 30),
			Layer = Pointer(memory.ReadUInt32(address, 32)),
			ImageBitMap = Pointer(memory.ReadUInt32(address, 68)),
			Window = Pointer(memory.ReadUInt32(address, 72)),
			Image = Pointer(memory.ReadUInt32(address, 76)),
		};
		for (var index = 0; index < 32; index++)
		{
			value.RequesterPadding1[index] = memory.ReadUInt8(address, 36 + index);
			value.RequesterPadding2[index] = memory.ReadUInt8(address, 80 + index);
		}
		return value;
	}

	public static unsafe void WriteRequester<TMemory>(ref TMemory memory,
		APTR address, Requester value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.OlderRequest.Raw);
		memory.WriteUInt16(address, 4, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, 8, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.Height));
		memory.WriteUInt16(address, 12, unchecked((ushort)value.RelativeLeft));
		memory.WriteUInt16(address, 14, unchecked((ushort)value.RelativeTop));
		memory.WriteUInt32(address, 16, value.Gadget.Raw);
		memory.WriteUInt32(address, 20, value.Border.Raw);
		memory.WriteUInt32(address, 24, value.Text.Raw);
		memory.WriteUInt16(address, 28, (ushort)value.Flags);
		memory.WriteUInt8(address, 30, value.BackFill);
		memory.WriteUInt8(address, 31, 0);
		memory.WriteUInt32(address, 32, value.Layer.Raw);
		for (var index = 0; index < 32; index++)
			memory.WriteUInt8(address, 36 + index, value.RequesterPadding1[index]);
		memory.WriteUInt32(address, 68, value.ImageBitMap.Raw);
		memory.WriteUInt32(address, 72, value.Window.Raw);
		memory.WriteUInt32(address, 76, value.Image.Raw);
		for (var index = 0; index < 32; index++)
			memory.WriteUInt8(address, 80 + index, value.RequesterPadding2[index]);
	}

	public static Remember ReadRemember<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextRemember = Pointer(memory.ReadUInt32(address, 0)),
		RememberSize = memory.ReadUInt32(address, 4),
		Memory = Pointer(memory.ReadUInt32(address, 8)),
	};

	public static void WriteRemember<TMemory>(ref TMemory memory, APTR address,
		Remember value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.NextRemember.Raw);
		memory.WriteUInt32(address, 4, value.RememberSize);
		memory.WriteUInt32(address, 8, value.Memory.Raw);
	}

	public static EasyStruct ReadEasyStruct<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		StructureSize = memory.ReadUInt32(address, 0),
		Flags = memory.ReadUInt32(address, 4),
		Title = Pointer(memory.ReadUInt32(address, 8)),
		TextFormat = Pointer(memory.ReadUInt32(address, 12)),
		GadgetFormat = Pointer(memory.ReadUInt32(address, 16)),
	};

	public static void WriteEasyStruct<TMemory>(ref TMemory memory, APTR address,
		EasyStruct value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.StructureSize);
		memory.WriteUInt32(address, 4, value.Flags);
		memory.WriteUInt32(address, 8, value.Title.Raw);
		memory.WriteUInt32(address, 12, value.TextFormat.Raw);
		memory.WriteUInt32(address, 16, value.GadgetFormat.Raw);
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for classic gadget state.</summary>
public static class IntuitionGadgetGuestCodec
{
	public static Gadget ReadGadget<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextGadget = Pointer(memory.ReadUInt32(address, 0)),
		LeftEdge = Signed(memory.ReadUInt16(address, 4)),
		TopEdge = Signed(memory.ReadUInt16(address, 6)),
		Width = Signed(memory.ReadUInt16(address, 8)),
		Height = Signed(memory.ReadUInt16(address, 10)),
		Flags = (GadgetFlags)memory.ReadUInt16(address, 12),
		Activation = (GadgetActivationFlags)memory.ReadUInt16(address, 14),
		GadgetType = (GadgetType)memory.ReadUInt16(address, 16),
		GadgetRender = Pointer(memory.ReadUInt32(address, 18)),
		SelectRender = Pointer(memory.ReadUInt32(address, 22)),
		GadgetText = Pointer(memory.ReadUInt32(address, 26)),
		MutualExclude = unchecked((int)memory.ReadUInt32(address, 30)),
		SpecialInfo = Pointer(memory.ReadUInt32(address, 34)),
		GadgetID = memory.ReadUInt16(address, 38),
		UserData = Pointer(memory.ReadUInt32(address, 40)),
	};

	public static void WriteGadget<TMemory>(ref TMemory memory, APTR address,
		Gadget value) where TMemory : struct, IAmigaGuestMemory =>
		WriteGadgetPrefix(ref memory, address, value.NextGadget, value.LeftEdge,
			value.TopEdge, value.Width, value.Height, value.Flags, value.Activation,
			value.GadgetType, value.GadgetRender, value.SelectRender, value.GadgetText,
			value.MutualExclude, value.SpecialInfo, value.GadgetID, value.UserData);

	public static ExtGadget ReadExtendedGadget<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextGadget = Pointer(memory.ReadUInt32(address, 0)),
		LeftEdge = Signed(memory.ReadUInt16(address, 4)),
		TopEdge = Signed(memory.ReadUInt16(address, 6)),
		Width = Signed(memory.ReadUInt16(address, 8)),
		Height = Signed(memory.ReadUInt16(address, 10)),
		Flags = (GadgetFlags)memory.ReadUInt16(address, 12),
		Activation = (GadgetActivationFlags)memory.ReadUInt16(address, 14),
		GadgetType = (GadgetType)memory.ReadUInt16(address, 16),
		GadgetRender = Pointer(memory.ReadUInt32(address, 18)),
		SelectRender = Pointer(memory.ReadUInt32(address, 22)),
		GadgetText = Pointer(memory.ReadUInt32(address, 26)),
		MutualExclude = unchecked((int)memory.ReadUInt32(address, 30)),
		SpecialInfo = Pointer(memory.ReadUInt32(address, 34)),
		GadgetID = memory.ReadUInt16(address, 38),
		UserData = Pointer(memory.ReadUInt32(address, 40)),
		MoreFlags = (GadgetMoreFlags)memory.ReadUInt32(address, 44),
		BoundsLeftEdge = Signed(memory.ReadUInt16(address, 48)),
		BoundsTopEdge = Signed(memory.ReadUInt16(address, 50)),
		BoundsWidth = Signed(memory.ReadUInt16(address, 52)),
		BoundsHeight = Signed(memory.ReadUInt16(address, 54)),
	};

	public static void WriteExtendedGadget<TMemory>(ref TMemory memory,
		APTR address, ExtGadget value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteGadgetPrefix(ref memory, address, value.NextGadget, value.LeftEdge,
			value.TopEdge, value.Width, value.Height, value.Flags, value.Activation,
			value.GadgetType, value.GadgetRender, value.SelectRender, value.GadgetText,
			value.MutualExclude, value.SpecialInfo, value.GadgetID, value.UserData);
		memory.WriteUInt32(address, 44, (uint)value.MoreFlags);
		memory.WriteUInt16(address, 48, unchecked((ushort)value.BoundsLeftEdge));
		memory.WriteUInt16(address, 50, unchecked((ushort)value.BoundsTopEdge));
		memory.WriteUInt16(address, 52, unchecked((ushort)value.BoundsWidth));
		memory.WriteUInt16(address, 54, unchecked((ushort)value.BoundsHeight));
	}

	public static BoolInfo ReadBoolInfo<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Flags = (BoolInfoFlags)memory.ReadUInt16(address, 0),
		Mask = Pointer(memory.ReadUInt32(address, 2)),
		Reserved = unchecked((int)memory.ReadUInt32(address, 6)),
	};

	public static void WriteBoolInfo<TMemory>(ref TMemory memory, APTR address,
		BoolInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, (ushort)value.Flags);
		memory.WriteUInt32(address, 2, value.Mask.Raw);
		memory.WriteUInt32(address, 6, unchecked((uint)value.Reserved));
	}

	public static PropInfo ReadPropInfo<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		Flags = (PropInfoFlags)memory.ReadUInt16(address, 0),
		HorizPot = memory.ReadUInt16(address, 2),
		VertPot = memory.ReadUInt16(address, 4),
		HorizBody = memory.ReadUInt16(address, 6),
		VertBody = memory.ReadUInt16(address, 8),
		ContainerWidth = memory.ReadUInt16(address, 10),
		ContainerHeight = memory.ReadUInt16(address, 12),
		HorizontalPotResolution = memory.ReadUInt16(address, 14),
		VerticalPotResolution = memory.ReadUInt16(address, 16),
		LeftBorder = memory.ReadUInt16(address, 18),
		TopBorder = memory.ReadUInt16(address, 20),
	};

	public static void WritePropInfo<TMemory>(ref TMemory memory, APTR address,
		PropInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, (ushort)value.Flags);
		memory.WriteUInt16(address, 2, value.HorizPot);
		memory.WriteUInt16(address, 4, value.VertPot);
		memory.WriteUInt16(address, 6, value.HorizBody);
		memory.WriteUInt16(address, 8, value.VertBody);
		memory.WriteUInt16(address, 10, value.ContainerWidth);
		memory.WriteUInt16(address, 12, value.ContainerHeight);
		memory.WriteUInt16(address, 14, value.HorizontalPotResolution);
		memory.WriteUInt16(address, 16, value.VerticalPotResolution);
		memory.WriteUInt16(address, 18, value.LeftBorder);
		memory.WriteUInt16(address, 20, value.TopBorder);
	}

	public static StringInfo ReadStringInfo<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Buffer = Pointer(memory.ReadUInt32(address, 0)),
		UndoBuffer = Pointer(memory.ReadUInt32(address, 4)),
		BufferPosition = Signed(memory.ReadUInt16(address, 8)),
		MaxChars = Signed(memory.ReadUInt16(address, 10)),
		DisplayPosition = Signed(memory.ReadUInt16(address, 12)),
		UndoPosition = Signed(memory.ReadUInt16(address, 14)),
		NumberOfChars = Signed(memory.ReadUInt16(address, 16)),
		DisplayCount = Signed(memory.ReadUInt16(address, 18)),
		ContainerLeft = Signed(memory.ReadUInt16(address, 20)),
		ContainerTop = Signed(memory.ReadUInt16(address, 22)),
		Extension = Pointer(memory.ReadUInt32(address, 24)),
		LongInt = unchecked((int)memory.ReadUInt32(address, 28)),
		AlternateKeyMap = Pointer(memory.ReadUInt32(address, 32)),
	};

	public static void WriteStringInfo<TMemory>(ref TMemory memory, APTR address,
		StringInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.Buffer.Raw);
		memory.WriteUInt32(address, 4, value.UndoBuffer.Raw);
		memory.WriteUInt16(address, 8, unchecked((ushort)value.BufferPosition));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.MaxChars));
		memory.WriteUInt16(address, 12, unchecked((ushort)value.DisplayPosition));
		memory.WriteUInt16(address, 14, unchecked((ushort)value.UndoPosition));
		memory.WriteUInt16(address, 16, unchecked((ushort)value.NumberOfChars));
		memory.WriteUInt16(address, 18, unchecked((ushort)value.DisplayCount));
		memory.WriteUInt16(address, 20, unchecked((ushort)value.ContainerLeft));
		memory.WriteUInt16(address, 22, unchecked((ushort)value.ContainerTop));
		memory.WriteUInt32(address, 24, value.Extension.Raw);
		memory.WriteUInt32(address, 28, unchecked((uint)value.LongInt));
		memory.WriteUInt32(address, 32, value.AlternateKeyMap.Raw);
	}

	private static void WriteGadgetPrefix<TMemory>(ref TMemory memory,
		APTR address, APTR nextGadget, short leftEdge, short topEdge, short width,
		short height, GadgetFlags flags, GadgetActivationFlags activation,
		GadgetType gadgetType, APTR gadgetRender, APTR selectRender,
		APTR gadgetText, int mutualExclude, APTR specialInfo, ushort gadgetId,
		APTR userData) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, nextGadget.Raw);
		memory.WriteUInt16(address, 4, unchecked((ushort)leftEdge));
		memory.WriteUInt16(address, 6, unchecked((ushort)topEdge));
		memory.WriteUInt16(address, 8, unchecked((ushort)width));
		memory.WriteUInt16(address, 10, unchecked((ushort)height));
		memory.WriteUInt16(address, 12, (ushort)flags);
		memory.WriteUInt16(address, 14, (ushort)activation);
		memory.WriteUInt16(address, 16, (ushort)gadgetType);
		memory.WriteUInt32(address, 18, gadgetRender.Raw);
		memory.WriteUInt32(address, 22, selectRender.Raw);
		memory.WriteUInt32(address, 26, gadgetText.Raw);
		memory.WriteUInt32(address, 30, unchecked((uint)mutualExclude));
		memory.WriteUInt32(address, 34, specialInfo.Raw);
		memory.WriteUInt16(address, 38, gadgetId);
		memory.WriteUInt32(address, 40, userData.Raw);
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}

/// <summary>Big-endian guest-memory boundary for screen/window descriptors and support records.</summary>
public static class IntuitionScreenWindowGuestCodec
{
	public static NewWindow ReadNewWindow<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		LeftEdge = Signed(memory.ReadUInt16(address, 0)),
		TopEdge = Signed(memory.ReadUInt16(address, 2)),
		Width = Signed(memory.ReadUInt16(address, 4)),
		Height = Signed(memory.ReadUInt16(address, 6)),
		DetailPen = memory.ReadUInt8(address, 8),
		BlockPen = memory.ReadUInt8(address, 9),
		IDCMPFlags = (IDCMPFlags)memory.ReadUInt32(address, 10),
		Flags = (WindowFlags)memory.ReadUInt32(address, 14),
		FirstGadget = Pointer(memory.ReadUInt32(address, 18)),
		CheckMark = Pointer(memory.ReadUInt32(address, 22)),
		Title = Pointer(memory.ReadUInt32(address, 26)),
		Screen = Pointer(memory.ReadUInt32(address, 30)),
		BitMap = Pointer(memory.ReadUInt32(address, 34)),
		MinWidth = Signed(memory.ReadUInt16(address, 38)),
		MinHeight = Signed(memory.ReadUInt16(address, 40)),
		MaxWidth = memory.ReadUInt16(address, 42),
		MaxHeight = memory.ReadUInt16(address, 44),
		Type = (ScreenType)memory.ReadUInt16(address, 46),
	};

	public static void WriteNewWindow<TMemory>(ref TMemory memory, APTR address,
		NewWindow value) where TMemory : struct, IAmigaGuestMemory =>
		WriteNewWindowFields(ref memory, address, value.LeftEdge, value.TopEdge,
			value.Width, value.Height, value.DetailPen, value.BlockPen,
			value.IDCMPFlags, value.Flags, value.FirstGadget, value.CheckMark,
			value.Title, value.Screen, value.BitMap, value.MinWidth, value.MinHeight,
			value.MaxWidth, value.MaxHeight, value.Type);

	public static ExtNewWindow ReadExtendedNewWindow<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		LeftEdge = Signed(memory.ReadUInt16(address, 0)),
		TopEdge = Signed(memory.ReadUInt16(address, 2)),
		Width = Signed(memory.ReadUInt16(address, 4)),
		Height = Signed(memory.ReadUInt16(address, 6)),
		DetailPen = memory.ReadUInt8(address, 8),
		BlockPen = memory.ReadUInt8(address, 9),
		IDCMPFlags = (IDCMPFlags)memory.ReadUInt32(address, 10),
		Flags = (WindowFlags)memory.ReadUInt32(address, 14),
		FirstGadget = Pointer(memory.ReadUInt32(address, 18)),
		CheckMark = Pointer(memory.ReadUInt32(address, 22)),
		Title = Pointer(memory.ReadUInt32(address, 26)),
		Screen = Pointer(memory.ReadUInt32(address, 30)),
		BitMap = Pointer(memory.ReadUInt32(address, 34)),
		MinWidth = Signed(memory.ReadUInt16(address, 38)),
		MinHeight = Signed(memory.ReadUInt16(address, 40)),
		MaxWidth = memory.ReadUInt16(address, 42),
		MaxHeight = memory.ReadUInt16(address, 44),
		Type = (ScreenType)memory.ReadUInt16(address, 46),
		Extension = Pointer(memory.ReadUInt32(address, 48)),
	};

	public static void WriteExtendedNewWindow<TMemory>(ref TMemory memory,
		APTR address, ExtNewWindow value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteNewWindowFields(ref memory, address, value.LeftEdge, value.TopEdge,
			value.Width, value.Height, value.DetailPen, value.BlockPen,
			value.IDCMPFlags, value.Flags, value.FirstGadget, value.CheckMark,
			value.Title, value.Screen, value.BitMap, value.MinWidth, value.MinHeight,
			value.MaxWidth, value.MaxHeight, value.Type);
		memory.WriteUInt32(address, 48, value.Extension.Raw);
	}

	public static NewScreen ReadNewScreen<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		LeftEdge = Signed(memory.ReadUInt16(address, 0)),
		TopEdge = Signed(memory.ReadUInt16(address, 2)),
		Width = Signed(memory.ReadUInt16(address, 4)),
		Height = Signed(memory.ReadUInt16(address, 6)),
		Depth = Signed(memory.ReadUInt16(address, 8)),
		DetailPen = memory.ReadUInt8(address, 10),
		BlockPen = memory.ReadUInt8(address, 11),
		ViewModes = (ScreenViewModes)memory.ReadUInt16(address, 12),
		Type = (ScreenType)memory.ReadUInt16(address, 14),
		Font = Pointer(memory.ReadUInt32(address, 16)),
		DefaultTitle = Pointer(memory.ReadUInt32(address, 20)),
		Gadgets = Pointer(memory.ReadUInt32(address, 24)),
		CustomBitMap = Pointer(memory.ReadUInt32(address, 28)),
	};

	public static void WriteNewScreen<TMemory>(ref TMemory memory, APTR address,
		NewScreen value) where TMemory : struct, IAmigaGuestMemory =>
		WriteNewScreenFields(ref memory, address, value.LeftEdge, value.TopEdge,
			value.Width, value.Height, value.Depth, value.DetailPen, value.BlockPen,
			value.ViewModes, value.Type, value.Font, value.DefaultTitle,
			value.Gadgets, value.CustomBitMap);

	public static ExtNewScreen ReadExtendedNewScreen<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		LeftEdge = Signed(memory.ReadUInt16(address, 0)),
		TopEdge = Signed(memory.ReadUInt16(address, 2)),
		Width = Signed(memory.ReadUInt16(address, 4)),
		Height = Signed(memory.ReadUInt16(address, 6)),
		Depth = Signed(memory.ReadUInt16(address, 8)),
		DetailPen = memory.ReadUInt8(address, 10),
		BlockPen = memory.ReadUInt8(address, 11),
		ViewModes = (ScreenViewModes)memory.ReadUInt16(address, 12),
		Type = (ScreenType)memory.ReadUInt16(address, 14),
		Font = Pointer(memory.ReadUInt32(address, 16)),
		DefaultTitle = Pointer(memory.ReadUInt32(address, 20)),
		Gadgets = Pointer(memory.ReadUInt32(address, 24)),
		CustomBitMap = Pointer(memory.ReadUInt32(address, 28)),
		Extension = Pointer(memory.ReadUInt32(address, 32)),
	};

	public static void WriteExtendedNewScreen<TMemory>(ref TMemory memory,
		APTR address, ExtNewScreen value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteNewScreenFields(ref memory, address, value.LeftEdge, value.TopEdge,
			value.Width, value.Height, value.Depth, value.DetailPen, value.BlockPen,
			value.ViewModes, value.Type, value.Font, value.DefaultTitle,
			value.Gadgets, value.CustomBitMap);
		memory.WriteUInt32(address, 32, value.Extension.Raw);
	}

	public static unsafe DrawInfo ReadDrawInfo<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory
	{
		var value = new DrawInfo
		{
			Version = memory.ReadUInt16(address, 0),
			NumberOfPens = memory.ReadUInt16(address, 2),
			Pens = Pointer(memory.ReadUInt32(address, 4)),
			Font = Pointer(memory.ReadUInt32(address, 8)),
			Depth = memory.ReadUInt16(address, 12),
			ResolutionX = memory.ReadUInt16(address, 14),
			ResolutionY = memory.ReadUInt16(address, 16),
			Flags = (DrawInfoFlags)memory.ReadUInt32(address, 18),
			CheckMark = Pointer(memory.ReadUInt32(address, 22)),
			AmigaKey = Pointer(memory.ReadUInt32(address, 26)),
		};
		for (var index = 0; index < 5; index++)
			value.Reserved[index] = memory.ReadUInt32(address, 30 + index * 4);
		return value;
	}

	public static unsafe void WriteDrawInfo<TMemory>(ref TMemory memory,
		APTR address, DrawInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, value.Version);
		memory.WriteUInt16(address, 2, value.NumberOfPens);
		memory.WriteUInt32(address, 4, value.Pens.Raw);
		memory.WriteUInt32(address, 8, value.Font.Raw);
		memory.WriteUInt16(address, 12, value.Depth);
		memory.WriteUInt16(address, 14, value.ResolutionX);
		memory.WriteUInt16(address, 16, value.ResolutionY);
		memory.WriteUInt32(address, 18, (uint)value.Flags);
		memory.WriteUInt32(address, 22, value.CheckMark.Raw);
		memory.WriteUInt32(address, 26, value.AmigaKey.Raw);
		for (var index = 0; index < 5; index++)
			memory.WriteUInt32(address, 30 + index * 4, value.Reserved[index]);
	}

	public static ColorSpec ReadColorSpec<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		ColorIndex = Signed(memory.ReadUInt16(address, 0)),
		Red = memory.ReadUInt16(address, 2),
		Green = memory.ReadUInt16(address, 4),
		Blue = memory.ReadUInt16(address, 6),
	};

	public static void WriteColorSpec<TMemory>(ref TMemory memory, APTR address,
		ColorSpec value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)value.ColorIndex));
		memory.WriteUInt16(address, 2, value.Red);
		memory.WriteUInt16(address, 4, value.Green);
		memory.WriteUInt16(address, 6, value.Blue);
	}

	public static PubScreenNode ReadPubScreenNode<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Node = new Node
		{
			Successor = Pointer(memory.ReadUInt32(address, 0)),
			Predecessor = Pointer(memory.ReadUInt32(address, 4)),
			Type = memory.ReadUInt8(address, 8),
			Priority = unchecked((sbyte)memory.ReadUInt8(address, 9)),
			Name = Pointer(memory.ReadUInt32(address, 10)),
		},
		Screen = Pointer(memory.ReadUInt32(address, 14)),
		Flags = (PublicScreenFlags)memory.ReadUInt16(address, 18),
		SizeInBytes = Signed(memory.ReadUInt16(address, 20)),
		VisitorCount = Signed(memory.ReadUInt16(address, 22)),
		SignalTask = Pointer(memory.ReadUInt32(address, 24)),
		SignalBit = memory.ReadUInt8(address, 28),
	};

	public static void WritePubScreenNode<TMemory>(ref TMemory memory,
		APTR address, PubScreenNode value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.Node.Successor.Raw);
		memory.WriteUInt32(address, 4, value.Node.Predecessor.Raw);
		memory.WriteUInt8(address, 8, value.Node.Type);
		memory.WriteUInt8(address, 9, unchecked((byte)value.Node.Priority));
		memory.WriteUInt32(address, 10, value.Node.Name.Raw);
		memory.WriteUInt32(address, 14, value.Screen.Raw);
		memory.WriteUInt16(address, 18, (ushort)value.Flags);
		memory.WriteUInt16(address, 20, unchecked((ushort)value.SizeInBytes));
		memory.WriteUInt16(address, 22, unchecked((ushort)value.VisitorCount));
		memory.WriteUInt32(address, 24, value.SignalTask.Raw);
		memory.WriteUInt8(address, 28, value.SignalBit);
		memory.WriteUInt8(address, 29, 0);
	}

	public static ScreenBuffer ReadScreenBuffer<TMemory>(ref TMemory memory,
		APTR address) where TMemory : struct, IAmigaGuestMemory => new()
	{
		BitMap = Pointer(memory.ReadUInt32(address, 0)),
		DoubleBufferInfo = Pointer(memory.ReadUInt32(address, 4)),
	};

	public static void WriteScreenBuffer<TMemory>(ref TMemory memory,
		APTR address, ScreenBuffer value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.BitMap.Raw);
		memory.WriteUInt32(address, 4, value.DoubleBufferInfo.Raw);
	}

	public static Window ReadWindow<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextWindow = Pointer(memory.ReadUInt32(address, 0)),
		LeftEdge = Signed(memory.ReadUInt16(address, 4)),
		TopEdge = Signed(memory.ReadUInt16(address, 6)),
		Width = Signed(memory.ReadUInt16(address, 8)),
		Height = Signed(memory.ReadUInt16(address, 10)),
		MouseY = Signed(memory.ReadUInt16(address, 12)),
		MouseX = Signed(memory.ReadUInt16(address, 14)),
		MinWidth = Signed(memory.ReadUInt16(address, 16)),
		MinHeight = Signed(memory.ReadUInt16(address, 18)),
		MaxWidth = memory.ReadUInt16(address, 20),
		MaxHeight = memory.ReadUInt16(address, 22),
		Flags = (WindowFlags)memory.ReadUInt32(address, 24),
		MenuStrip = Pointer(memory.ReadUInt32(address, 28)),
		Title = Pointer(memory.ReadUInt32(address, 32)),
		FirstRequest = Pointer(memory.ReadUInt32(address, 36)),
		DMRequest = Pointer(memory.ReadUInt32(address, 40)),
		RequesterCount = Signed(memory.ReadUInt16(address, 44)),
		Screen = Pointer(memory.ReadUInt32(address, 46)),
		RastPort = Pointer(memory.ReadUInt32(address, 50)),
		BorderLeft = unchecked((sbyte)memory.ReadUInt8(address, 54)),
		BorderTop = unchecked((sbyte)memory.ReadUInt8(address, 55)),
		BorderRight = unchecked((sbyte)memory.ReadUInt8(address, 56)),
		BorderBottom = unchecked((sbyte)memory.ReadUInt8(address, 57)),
		BorderRastPort = Pointer(memory.ReadUInt32(address, 58)),
		FirstGadget = Pointer(memory.ReadUInt32(address, 62)),
		Parent = Pointer(memory.ReadUInt32(address, 66)),
		Descendant = Pointer(memory.ReadUInt32(address, 70)),
		Pointer = Pointer(memory.ReadUInt32(address, 74)),
		PointerHeight = unchecked((sbyte)memory.ReadUInt8(address, 78)),
		PointerWidth = unchecked((sbyte)memory.ReadUInt8(address, 79)),
		XOffset = unchecked((sbyte)memory.ReadUInt8(address, 80)),
		YOffset = unchecked((sbyte)memory.ReadUInt8(address, 81)),
		IDCMPFlags = (IDCMPFlags)memory.ReadUInt32(address, 82),
		UserPort = Pointer(memory.ReadUInt32(address, 86)),
		WindowPort = Pointer(memory.ReadUInt32(address, 90)),
		MessageKey = Pointer(memory.ReadUInt32(address, 94)),
		DetailPen = memory.ReadUInt8(address, 98),
		BlockPen = memory.ReadUInt8(address, 99),
		CheckMark = Pointer(memory.ReadUInt32(address, 100)),
		ScreenTitle = Pointer(memory.ReadUInt32(address, 104)),
		GzzMouseX = Signed(memory.ReadUInt16(address, 108)),
		GzzMouseY = Signed(memory.ReadUInt16(address, 110)),
		GzzWidth = Signed(memory.ReadUInt16(address, 112)),
		GzzHeight = Signed(memory.ReadUInt16(address, 114)),
		ExtData = Pointer(memory.ReadUInt32(address, 116)),
		UserData = Pointer(memory.ReadUInt32(address, 120)),
		Layer = Pointer(memory.ReadUInt32(address, 124)),
		Font = Pointer(memory.ReadUInt32(address, 128)),
		MoreFlags = memory.ReadUInt32(address, 132),
	};

	public static void WriteWindow<TMemory>(ref TMemory memory, APTR address,
		Window value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.NextWindow.Raw);
		memory.WriteUInt16(address, 4, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 6, unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, 8, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.Height));
		memory.WriteUInt16(address, 12, unchecked((ushort)value.MouseY));
		memory.WriteUInt16(address, 14, unchecked((ushort)value.MouseX));
		memory.WriteUInt16(address, 16, unchecked((ushort)value.MinWidth));
		memory.WriteUInt16(address, 18, unchecked((ushort)value.MinHeight));
		memory.WriteUInt16(address, 20, value.MaxWidth);
		memory.WriteUInt16(address, 22, value.MaxHeight);
		memory.WriteUInt32(address, 24, (uint)value.Flags);
		memory.WriteUInt32(address, 28, value.MenuStrip.Raw);
		memory.WriteUInt32(address, 32, value.Title.Raw);
		memory.WriteUInt32(address, 36, value.FirstRequest.Raw);
		memory.WriteUInt32(address, 40, value.DMRequest.Raw);
		memory.WriteUInt16(address, 44, unchecked((ushort)value.RequesterCount));
		memory.WriteUInt32(address, 46, value.Screen.Raw);
		memory.WriteUInt32(address, 50, value.RastPort.Raw);
		memory.WriteUInt8(address, 54, unchecked((byte)value.BorderLeft));
		memory.WriteUInt8(address, 55, unchecked((byte)value.BorderTop));
		memory.WriteUInt8(address, 56, unchecked((byte)value.BorderRight));
		memory.WriteUInt8(address, 57, unchecked((byte)value.BorderBottom));
		memory.WriteUInt32(address, 58, value.BorderRastPort.Raw);
		memory.WriteUInt32(address, 62, value.FirstGadget.Raw);
		memory.WriteUInt32(address, 66, value.Parent.Raw);
		memory.WriteUInt32(address, 70, value.Descendant.Raw);
		memory.WriteUInt32(address, 74, value.Pointer.Raw);
		memory.WriteUInt8(address, 78, unchecked((byte)value.PointerHeight));
		memory.WriteUInt8(address, 79, unchecked((byte)value.PointerWidth));
		memory.WriteUInt8(address, 80, unchecked((byte)value.XOffset));
		memory.WriteUInt8(address, 81, unchecked((byte)value.YOffset));
		memory.WriteUInt32(address, 82, (uint)value.IDCMPFlags);
		memory.WriteUInt32(address, 86, value.UserPort.Raw);
		memory.WriteUInt32(address, 90, value.WindowPort.Raw);
		memory.WriteUInt32(address, 94, value.MessageKey.Raw);
		memory.WriteUInt8(address, 98, value.DetailPen);
		memory.WriteUInt8(address, 99, value.BlockPen);
		memory.WriteUInt32(address, 100, value.CheckMark.Raw);
		memory.WriteUInt32(address, 104, value.ScreenTitle.Raw);
		memory.WriteUInt16(address, 108, unchecked((ushort)value.GzzMouseX));
		memory.WriteUInt16(address, 110, unchecked((ushort)value.GzzMouseY));
		memory.WriteUInt16(address, 112, unchecked((ushort)value.GzzWidth));
		memory.WriteUInt16(address, 114, unchecked((ushort)value.GzzHeight));
		memory.WriteUInt32(address, 116, value.ExtData.Raw);
		memory.WriteUInt32(address, 120, value.UserData.Raw);
		memory.WriteUInt32(address, 124, value.Layer.Raw);
		memory.WriteUInt32(address, 128, value.Font.Raw);
		memory.WriteUInt32(address, 132, value.MoreFlags);
	}

	public static Screen ReadScreen<TMemory>(ref TMemory memory, APTR address)
		where TMemory : struct, IAmigaGuestMemory => new()
	{
		NextScreen = Pointer(memory.ReadUInt32(address, 0)),
		FirstWindow = Pointer(memory.ReadUInt32(address, 4)),
		LeftEdge = Signed(memory.ReadUInt16(address, 8)),
		TopEdge = Signed(memory.ReadUInt16(address, 10)),
		Width = Signed(memory.ReadUInt16(address, 12)),
		Height = Signed(memory.ReadUInt16(address, 14)),
		MouseY = Signed(memory.ReadUInt16(address, 16)),
		MouseX = Signed(memory.ReadUInt16(address, 18)),
		Flags = (ScreenFlags)memory.ReadUInt16(address, 20),
		Title = Pointer(memory.ReadUInt32(address, 22)),
		DefaultTitle = Pointer(memory.ReadUInt32(address, 26)),
		BarHeight = unchecked((sbyte)memory.ReadUInt8(address, 30)),
		BarVBorder = unchecked((sbyte)memory.ReadUInt8(address, 31)),
		BarHBorder = unchecked((sbyte)memory.ReadUInt8(address, 32)),
		MenuVBorder = unchecked((sbyte)memory.ReadUInt8(address, 33)),
		MenuHBorder = unchecked((sbyte)memory.ReadUInt8(address, 34)),
		WindowBorderTop = unchecked((sbyte)memory.ReadUInt8(address, 35)),
		WindowBorderLeft = unchecked((sbyte)memory.ReadUInt8(address, 36)),
		WindowBorderRight = unchecked((sbyte)memory.ReadUInt8(address, 37)),
		WindowBorderBottom = unchecked((sbyte)memory.ReadUInt8(address, 38)),
		Font = Pointer(memory.ReadUInt32(address, 40)),
		ViewPort = ReadViewPort(ref memory, address, 44),
		RastPort = ReadRastPort(ref memory, address, 84),
		BitMap = ReadBitMap(ref memory, address, 184),
		LayerInfo = ReadLayerInfo(ref memory, address, 224),
		FirstGadget = Pointer(memory.ReadUInt32(address, 326)),
		DetailPen = memory.ReadUInt8(address, 330),
		BlockPen = memory.ReadUInt8(address, 331),
		SaveColor0 = memory.ReadUInt16(address, 332),
		BarLayer = Pointer(memory.ReadUInt32(address, 334)),
		ExtData = Pointer(memory.ReadUInt32(address, 338)),
		UserData = Pointer(memory.ReadUInt32(address, 342)),
	};

	public static void WriteScreen<TMemory>(ref TMemory memory, APTR address,
		Screen value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, 0, value.NextScreen.Raw);
		memory.WriteUInt32(address, 4, value.FirstWindow.Raw);
		memory.WriteUInt16(address, 8, unchecked((ushort)value.LeftEdge));
		memory.WriteUInt16(address, 10, unchecked((ushort)value.TopEdge));
		memory.WriteUInt16(address, 12, unchecked((ushort)value.Width));
		memory.WriteUInt16(address, 14, unchecked((ushort)value.Height));
		memory.WriteUInt16(address, 16, unchecked((ushort)value.MouseY));
		memory.WriteUInt16(address, 18, unchecked((ushort)value.MouseX));
		memory.WriteUInt16(address, 20, (ushort)value.Flags);
		memory.WriteUInt32(address, 22, value.Title.Raw);
		memory.WriteUInt32(address, 26, value.DefaultTitle.Raw);
		memory.WriteUInt8(address, 30, unchecked((byte)value.BarHeight));
		memory.WriteUInt8(address, 31, unchecked((byte)value.BarVBorder));
		memory.WriteUInt8(address, 32, unchecked((byte)value.BarHBorder));
		memory.WriteUInt8(address, 33, unchecked((byte)value.MenuVBorder));
		memory.WriteUInt8(address, 34, unchecked((byte)value.MenuHBorder));
		memory.WriteUInt8(address, 35, unchecked((byte)value.WindowBorderTop));
		memory.WriteUInt8(address, 36, unchecked((byte)value.WindowBorderLeft));
		memory.WriteUInt8(address, 37, unchecked((byte)value.WindowBorderRight));
		memory.WriteUInt8(address, 38, unchecked((byte)value.WindowBorderBottom));
		memory.WriteUInt8(address, 39, 0);
		memory.WriteUInt32(address, 40, value.Font.Raw);
		WriteViewPort(ref memory, address, 44, value.ViewPort);
		WriteRastPort(ref memory, address, 84, value.RastPort);
		WriteBitMap(ref memory, address, 184, value.BitMap);
		WriteLayerInfo(ref memory, address, 224, value.LayerInfo);
		memory.WriteUInt32(address, 326, value.FirstGadget.Raw);
		memory.WriteUInt8(address, 330, value.DetailPen);
		memory.WriteUInt8(address, 331, value.BlockPen);
		memory.WriteUInt16(address, 332, value.SaveColor0);
		memory.WriteUInt32(address, 334, value.BarLayer.Raw);
		memory.WriteUInt32(address, 338, value.ExtData.Raw);
		memory.WriteUInt32(address, 342, value.UserData.Raw);
	}

	private static ViewPort ReadViewPort<TMemory>(ref TMemory memory,
		APTR address, int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Next = Pointer(memory.ReadUInt32(address, offset)),
		ColorMap = Pointer(memory.ReadUInt32(address, offset + 4)),
		DisplayInstructions = Pointer(memory.ReadUInt32(address, offset + 8)),
		SpriteInstructions = Pointer(memory.ReadUInt32(address, offset + 12)),
		ColorInstructions = Pointer(memory.ReadUInt32(address, offset + 16)),
		UserCopperInstructions = Pointer(memory.ReadUInt32(address, offset + 20)),
		DisplayWidth = Signed(memory.ReadUInt16(address, offset + 24)),
		DisplayHeight = Signed(memory.ReadUInt16(address, offset + 26)),
		DisplayXOffset = Signed(memory.ReadUInt16(address, offset + 28)),
		DisplayYOffset = Signed(memory.ReadUInt16(address, offset + 30)),
		Modes = (ScreenViewModes)memory.ReadUInt16(address, offset + 32),
		SpritePriorities = memory.ReadUInt8(address, offset + 34),
		ExtendedModes = memory.ReadUInt8(address, offset + 35),
		RasInfo = Pointer(memory.ReadUInt32(address, offset + 36)),
	};

	private static void WriteViewPort<TMemory>(ref TMemory memory, APTR address,
		int offset, ViewPort value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, offset, value.Next.Raw);
		memory.WriteUInt32(address, offset + 4, value.ColorMap.Raw);
		memory.WriteUInt32(address, offset + 8, value.DisplayInstructions.Raw);
		memory.WriteUInt32(address, offset + 12, value.SpriteInstructions.Raw);
		memory.WriteUInt32(address, offset + 16, value.ColorInstructions.Raw);
		memory.WriteUInt32(address, offset + 20, value.UserCopperInstructions.Raw);
		memory.WriteUInt16(address, offset + 24, unchecked((ushort)value.DisplayWidth));
		memory.WriteUInt16(address, offset + 26, unchecked((ushort)value.DisplayHeight));
		memory.WriteUInt16(address, offset + 28, unchecked((ushort)value.DisplayXOffset));
		memory.WriteUInt16(address, offset + 30, unchecked((ushort)value.DisplayYOffset));
		memory.WriteUInt16(address, offset + 32, (ushort)value.Modes);
		memory.WriteUInt8(address, offset + 34, value.SpritePriorities);
		memory.WriteUInt8(address, offset + 35, value.ExtendedModes);
		memory.WriteUInt32(address, offset + 36, value.RasInfo.Raw);
	}

	private static unsafe RastPort ReadRastPort<TMemory>(ref TMemory memory,
		APTR address, int offset) where TMemory : struct, IAmigaGuestMemory
	{
		var value = new RastPort
		{
			Layer = Pointer(memory.ReadUInt32(address, offset)),
			BitMap = Pointer(memory.ReadUInt32(address, offset + 4)),
			AreaPattern = Pointer(memory.ReadUInt32(address, offset + 8)),
			TemporaryRaster = Pointer(memory.ReadUInt32(address, offset + 12)),
			AreaInfo = Pointer(memory.ReadUInt32(address, offset + 16)),
			GelsInfo = Pointer(memory.ReadUInt32(address, offset + 20)),
			Mask = memory.ReadUInt8(address, offset + 24),
			ForegroundPen = unchecked((sbyte)memory.ReadUInt8(address, offset + 25)),
			BackgroundPen = unchecked((sbyte)memory.ReadUInt8(address, offset + 26)),
			AreaOutlinePen = unchecked((sbyte)memory.ReadUInt8(address, offset + 27)),
			DrawMode = (DrawMode)memory.ReadUInt8(address, offset + 28),
			AreaPatternSize = memory.ReadUInt8(address, offset + 29),
			LinePatternCount = memory.ReadUInt8(address, offset + 30),
			Flags = (RastPortFlags)memory.ReadUInt16(address, offset + 32),
			LinePattern = memory.ReadUInt16(address, offset + 34),
			CurrentX = Signed(memory.ReadUInt16(address, offset + 36)),
			CurrentY = Signed(memory.ReadUInt16(address, offset + 38)),
			PenWidth = Signed(memory.ReadUInt16(address, offset + 48)),
			PenHeight = Signed(memory.ReadUInt16(address, offset + 50)),
			Font = Pointer(memory.ReadUInt32(address, offset + 52)),
			AlgorithmicStyle = memory.ReadUInt8(address, offset + 56),
			TextFlags = memory.ReadUInt8(address, offset + 57),
			TextHeight = memory.ReadUInt16(address, offset + 58),
			TextWidth = memory.ReadUInt16(address, offset + 60),
			TextBaseline = memory.ReadUInt16(address, offset + 62),
			TextSpacing = Signed(memory.ReadUInt16(address, offset + 64)),
			User = Pointer(memory.ReadUInt32(address, offset + 66)),
		};
		for (var index = 0; index < 8; index++)
			value.Minterms[index] = memory.ReadUInt8(address, offset + 40 + index);
		for (var index = 0; index < 2; index++)
			value.LongReserved[index] = memory.ReadUInt32(address, offset + 70 + index * 4);
		for (var index = 0; index < 7; index++)
			value.WordReserved[index] = memory.ReadUInt16(address, offset + 78 + index * 2);
		for (var index = 0; index < 8; index++)
			value.Reserved[index] = memory.ReadUInt8(address, offset + 92 + index);
		return value;
	}

	private static unsafe void WriteRastPort<TMemory>(ref TMemory memory,
		APTR address, int offset, RastPort value)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, offset, value.Layer.Raw);
		memory.WriteUInt32(address, offset + 4, value.BitMap.Raw);
		memory.WriteUInt32(address, offset + 8, value.AreaPattern.Raw);
		memory.WriteUInt32(address, offset + 12, value.TemporaryRaster.Raw);
		memory.WriteUInt32(address, offset + 16, value.AreaInfo.Raw);
		memory.WriteUInt32(address, offset + 20, value.GelsInfo.Raw);
		memory.WriteUInt8(address, offset + 24, value.Mask);
		memory.WriteUInt8(address, offset + 25, unchecked((byte)value.ForegroundPen));
		memory.WriteUInt8(address, offset + 26, unchecked((byte)value.BackgroundPen));
		memory.WriteUInt8(address, offset + 27, unchecked((byte)value.AreaOutlinePen));
		memory.WriteUInt8(address, offset + 28, (byte)value.DrawMode);
		memory.WriteUInt8(address, offset + 29, value.AreaPatternSize);
		memory.WriteUInt8(address, offset + 30, value.LinePatternCount);
		memory.WriteUInt8(address, offset + 31, 0);
		memory.WriteUInt16(address, offset + 32, (ushort)value.Flags);
		memory.WriteUInt16(address, offset + 34, value.LinePattern);
		memory.WriteUInt16(address, offset + 36, unchecked((ushort)value.CurrentX));
		memory.WriteUInt16(address, offset + 38, unchecked((ushort)value.CurrentY));
		for (var index = 0; index < 8; index++)
			memory.WriteUInt8(address, offset + 40 + index, value.Minterms[index]);
		memory.WriteUInt16(address, offset + 48, unchecked((ushort)value.PenWidth));
		memory.WriteUInt16(address, offset + 50, unchecked((ushort)value.PenHeight));
		memory.WriteUInt32(address, offset + 52, value.Font.Raw);
		memory.WriteUInt8(address, offset + 56, value.AlgorithmicStyle);
		memory.WriteUInt8(address, offset + 57, value.TextFlags);
		memory.WriteUInt16(address, offset + 58, value.TextHeight);
		memory.WriteUInt16(address, offset + 60, value.TextWidth);
		memory.WriteUInt16(address, offset + 62, value.TextBaseline);
		memory.WriteUInt16(address, offset + 64, unchecked((ushort)value.TextSpacing));
		memory.WriteUInt32(address, offset + 66, value.User.Raw);
		for (var index = 0; index < 2; index++)
			memory.WriteUInt32(address, offset + 70 + index * 4, value.LongReserved[index]);
		for (var index = 0; index < 7; index++)
			memory.WriteUInt16(address, offset + 78 + index * 2, value.WordReserved[index]);
		for (var index = 0; index < 8; index++)
			memory.WriteUInt8(address, offset + 92 + index, value.Reserved[index]);
	}

	private static BitMap ReadBitMap<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		BytesPerRow = memory.ReadUInt16(address, offset),
		Rows = memory.ReadUInt16(address, offset + 2),
		Flags = (BitMapFlags)memory.ReadUInt8(address, offset + 4),
		Depth = memory.ReadUInt8(address, offset + 5),
		Plane0 = Pointer(memory.ReadUInt32(address, offset + 8)),
		Plane1 = Pointer(memory.ReadUInt32(address, offset + 12)),
		Plane2 = Pointer(memory.ReadUInt32(address, offset + 16)),
		Plane3 = Pointer(memory.ReadUInt32(address, offset + 20)),
		Plane4 = Pointer(memory.ReadUInt32(address, offset + 24)),
		Plane5 = Pointer(memory.ReadUInt32(address, offset + 28)),
		Plane6 = Pointer(memory.ReadUInt32(address, offset + 32)),
		Plane7 = Pointer(memory.ReadUInt32(address, offset + 36)),
	};

	private static void WriteBitMap<TMemory>(ref TMemory memory, APTR address,
		int offset, BitMap value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, offset, value.BytesPerRow);
		memory.WriteUInt16(address, offset + 2, value.Rows);
		memory.WriteUInt8(address, offset + 4, (byte)value.Flags);
		memory.WriteUInt8(address, offset + 5, value.Depth);
		memory.WriteUInt16(address, offset + 6, 0);
		memory.WriteUInt32(address, offset + 8, value.Plane0.Raw);
		memory.WriteUInt32(address, offset + 12, value.Plane1.Raw);
		memory.WriteUInt32(address, offset + 16, value.Plane2.Raw);
		memory.WriteUInt32(address, offset + 20, value.Plane3.Raw);
		memory.WriteUInt32(address, offset + 24, value.Plane4.Raw);
		memory.WriteUInt32(address, offset + 28, value.Plane5.Raw);
		memory.WriteUInt32(address, offset + 32, value.Plane6.Raw);
		memory.WriteUInt32(address, offset + 36, value.Plane7.Raw);
	}

	private static LayerInfo ReadLayerInfo<TMemory>(ref TMemory memory,
		APTR address, int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		TopLayer = Pointer(memory.ReadUInt32(address, offset)),
		CheckLayer = Pointer(memory.ReadUInt32(address, offset + 4)),
		Obscured = Pointer(memory.ReadUInt32(address, offset + 8)),
		FreeClipRects = Pointer(memory.ReadUInt32(address, offset + 12)),
		PrivateReserve1 = unchecked((int)memory.ReadUInt32(address, offset + 16)),
		PrivateReserve2 = unchecked((int)memory.ReadUInt32(address, offset + 20)),
		Lock = ReadSemaphore(ref memory, address, offset + 24),
		GraphicsSemaphoreHead = ReadMinList(ref memory, address, offset + 70),
		PrivateReserve3 = Signed(memory.ReadUInt16(address, offset + 82)),
		PrivateReserve4 = Pointer(memory.ReadUInt32(address, offset + 84)),
		Flags = (LayerInfoFlags)memory.ReadUInt16(address, offset + 88),
		FattenCount = unchecked((sbyte)memory.ReadUInt8(address, offset + 90)),
		LockLayersCount = unchecked((sbyte)memory.ReadUInt8(address, offset + 91)),
		PrivateReserve5 = Signed(memory.ReadUInt16(address, offset + 92)),
		BlankHook = Pointer(memory.ReadUInt32(address, offset + 94)),
		Extra = Pointer(memory.ReadUInt32(address, offset + 98)),
	};

	private static void WriteLayerInfo<TMemory>(ref TMemory memory, APTR address,
		int offset, LayerInfo value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, offset, value.TopLayer.Raw);
		memory.WriteUInt32(address, offset + 4, value.CheckLayer.Raw);
		memory.WriteUInt32(address, offset + 8, value.Obscured.Raw);
		memory.WriteUInt32(address, offset + 12, value.FreeClipRects.Raw);
		memory.WriteUInt32(address, offset + 16, unchecked((uint)value.PrivateReserve1));
		memory.WriteUInt32(address, offset + 20, unchecked((uint)value.PrivateReserve2));
		WriteSemaphore(ref memory, address, offset + 24, value.Lock);
		WriteMinList(ref memory, address, offset + 70, value.GraphicsSemaphoreHead);
		memory.WriteUInt16(address, offset + 82, unchecked((ushort)value.PrivateReserve3));
		memory.WriteUInt32(address, offset + 84, value.PrivateReserve4.Raw);
		memory.WriteUInt16(address, offset + 88, (ushort)value.Flags);
		memory.WriteUInt8(address, offset + 90, unchecked((byte)value.FattenCount));
		memory.WriteUInt8(address, offset + 91, unchecked((byte)value.LockLayersCount));
		memory.WriteUInt16(address, offset + 92, unchecked((ushort)value.PrivateReserve5));
		memory.WriteUInt32(address, offset + 94, value.BlankHook.Raw);
		memory.WriteUInt32(address, offset + 98, value.Extra.Raw);
	}

	private static SignalSemaphore ReadSemaphore<TMemory>(ref TMemory memory,
		APTR address, int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Link = ReadNode(ref memory, address, offset),
		NestCount = Signed(memory.ReadUInt16(address, offset + 14)),
		WaitQueue = ReadMinList(ref memory, address, offset + 16),
		MultipleLink = new SemaphoreRequest
		{
			Link = new MinNode
			{
				Successor = Pointer(memory.ReadUInt32(address, offset + 28)),
				Predecessor = Pointer(memory.ReadUInt32(address, offset + 32)),
			},
			Waiter = Pointer(memory.ReadUInt32(address, offset + 36)),
		},
		Owner = Pointer(memory.ReadUInt32(address, offset + 40)),
		QueueCount = Signed(memory.ReadUInt16(address, offset + 44)),
	};

	private static void WriteSemaphore<TMemory>(ref TMemory memory, APTR address,
		int offset, SignalSemaphore value) where TMemory : struct, IAmigaGuestMemory
	{
		WriteNode(ref memory, address, offset, value.Link);
		memory.WriteUInt16(address, offset + 14, unchecked((ushort)value.NestCount));
		WriteMinList(ref memory, address, offset + 16, value.WaitQueue);
		memory.WriteUInt32(address, offset + 28, value.MultipleLink.Link.Successor.Raw);
		memory.WriteUInt32(address, offset + 32, value.MultipleLink.Link.Predecessor.Raw);
		memory.WriteUInt32(address, offset + 36, value.MultipleLink.Waiter.Raw);
		memory.WriteUInt32(address, offset + 40, value.Owner.Raw);
		memory.WriteUInt16(address, offset + 44, unchecked((ushort)value.QueueCount));
	}

	private static Node ReadNode<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Successor = Pointer(memory.ReadUInt32(address, offset)),
		Predecessor = Pointer(memory.ReadUInt32(address, offset + 4)),
		Type = memory.ReadUInt8(address, offset + 8),
		Priority = unchecked((sbyte)memory.ReadUInt8(address, offset + 9)),
		Name = Pointer(memory.ReadUInt32(address, offset + 10)),
	};

	private static void WriteNode<TMemory>(ref TMemory memory, APTR address,
		int offset, Node value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, offset, value.Successor.Raw);
		memory.WriteUInt32(address, offset + 4, value.Predecessor.Raw);
		memory.WriteUInt8(address, offset + 8, value.Type);
		memory.WriteUInt8(address, offset + 9, unchecked((byte)value.Priority));
		memory.WriteUInt32(address, offset + 10, value.Name.Raw);
	}

	private static MinList ReadMinList<TMemory>(ref TMemory memory, APTR address,
		int offset) where TMemory : struct, IAmigaGuestMemory => new()
	{
		Head = Pointer(memory.ReadUInt32(address, offset)),
		Tail = Pointer(memory.ReadUInt32(address, offset + 4)),
		TailPred = Pointer(memory.ReadUInt32(address, offset + 8)),
	};

	private static void WriteMinList<TMemory>(ref TMemory memory, APTR address,
		int offset, MinList value) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt32(address, offset, value.Head.Raw);
		memory.WriteUInt32(address, offset + 4, value.Tail.Raw);
		memory.WriteUInt32(address, offset + 8, value.TailPred.Raw);
	}

	private static void WriteNewWindowFields<TMemory>(ref TMemory memory,
		APTR address, short left, short top, short width, short height,
		byte detailPen, byte blockPen, IDCMPFlags idcmpFlags, WindowFlags flags,
		APTR firstGadget, APTR checkMark, APTR title, APTR screen, APTR bitMap,
		short minWidth, short minHeight, ushort maxWidth, ushort maxHeight,
		ScreenType type) where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)left));
		memory.WriteUInt16(address, 2, unchecked((ushort)top));
		memory.WriteUInt16(address, 4, unchecked((ushort)width));
		memory.WriteUInt16(address, 6, unchecked((ushort)height));
		memory.WriteUInt8(address, 8, detailPen);
		memory.WriteUInt8(address, 9, blockPen);
		memory.WriteUInt32(address, 10, (uint)idcmpFlags);
		memory.WriteUInt32(address, 14, (uint)flags);
		memory.WriteUInt32(address, 18, firstGadget.Raw);
		memory.WriteUInt32(address, 22, checkMark.Raw);
		memory.WriteUInt32(address, 26, title.Raw);
		memory.WriteUInt32(address, 30, screen.Raw);
		memory.WriteUInt32(address, 34, bitMap.Raw);
		memory.WriteUInt16(address, 38, unchecked((ushort)minWidth));
		memory.WriteUInt16(address, 40, unchecked((ushort)minHeight));
		memory.WriteUInt16(address, 42, maxWidth);
		memory.WriteUInt16(address, 44, maxHeight);
		memory.WriteUInt16(address, 46, (ushort)type);
	}

	private static void WriteNewScreenFields<TMemory>(ref TMemory memory,
		APTR address, short left, short top, short width, short height, short depth,
		byte detailPen, byte blockPen, ScreenViewModes viewModes, ScreenType type,
		APTR font, APTR defaultTitle, APTR gadgets, APTR customBitMap)
		where TMemory : struct, IAmigaGuestMemory
	{
		memory.WriteUInt16(address, 0, unchecked((ushort)left));
		memory.WriteUInt16(address, 2, unchecked((ushort)top));
		memory.WriteUInt16(address, 4, unchecked((ushort)width));
		memory.WriteUInt16(address, 6, unchecked((ushort)height));
		memory.WriteUInt16(address, 8, unchecked((ushort)depth));
		memory.WriteUInt8(address, 10, detailPen);
		memory.WriteUInt8(address, 11, blockPen);
		memory.WriteUInt16(address, 12, (ushort)viewModes);
		memory.WriteUInt16(address, 14, (ushort)type);
		memory.WriteUInt32(address, 16, font.Raw);
		memory.WriteUInt32(address, 20, defaultTitle.Raw);
		memory.WriteUInt32(address, 24, gadgets.Raw);
		memory.WriteUInt32(address, 28, customBitMap.Raw);
	}

	private static APTR Pointer(uint value) => APTR.FromPointer(value);
	private static short Signed(ushort value) => unchecked((short)value);
}
