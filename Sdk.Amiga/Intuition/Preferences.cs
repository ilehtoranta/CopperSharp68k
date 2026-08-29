/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.InteropServices;

namespace Amiga;

/// <summary>Sizes used by the legacy Intuition Preferences ABI.</summary>
public static class IntuitionPreferencesConstants
{
	public const int FILENAME_SIZE = 30;
	public const int DEVNAME_SIZE = 16;
	public const int POINTERSIZE = 36;
	public const int TOPAZ_EIGHTY = 8;
	public const int TOPAZ_SIXTY = 9;
	public const ushort LACEWB = 1 << 0;
	public const ushort LW_RESERVED = 1;
	public const int SCREEN_DRAG = 1 << 14;
	public const int MOUSE_ACCEL = 1 << 15;
	public const byte PARALLEL_PRINTER = 0x00;
	public const byte SERIAL_PRINTER = 0x01;
	public const ushort BAUD_110 = 0x00;
	public const ushort BAUD_300 = 0x01;
	public const ushort BAUD_1200 = 0x02;
	public const ushort BAUD_2400 = 0x03;
	public const ushort BAUD_4800 = 0x04;
	public const ushort BAUD_9600 = 0x05;
	public const ushort BAUD_19200 = 0x06;
	public const ushort BAUD_MIDI = 0x07;
	public const ushort FANFOLD = 0x00;
	public const ushort SINGLE = 0x80;
	public const ushort PICA = 0x000;
	public const ushort ELITE = 0x400;
	public const ushort FINE = 0x800;
	public const ushort DRAFT = 0x000;
	public const ushort LETTER = 0x100;
	public const ushort SIX_LPI = 0x000;
	public const ushort EIGHT_LPI = 0x200;
	public const ushort IMAGE_POSITIVE = 0x00;
	public const ushort IMAGE_NEGATIVE = 0x01;
	public const ushort ASPECT_HORIZ = 0x00;
	public const ushort ASPECT_VERT = 0x01;
	public const ushort SHADE_BW = 0x00;
	public const ushort SHADE_GREYSCALE = 0x01;
	public const ushort SHADE_COLOR = 0x02;
	public const ushort US_LETTER = 0x00;
	public const ushort US_LEGAL = 0x10;
	public const ushort N_TRACTOR = 0x20;
	public const ushort W_TRACTOR = 0x30;
	public const ushort CUSTOM = 0x40;
	public const ushort EURO_A0 = 0x50;
	public const ushort EURO_A1 = 0x60;
	public const ushort EURO_A2 = 0x70;
	public const ushort EURO_A3 = 0x80;
	public const ushort EURO_A4 = 0x90;
	public const ushort EURO_A5 = 0xA0;
	public const ushort EURO_A6 = 0xB0;
	public const ushort EURO_A7 = 0xC0;
	public const ushort EURO_A8 = 0xD0;
	public const ushort CUSTOM_NAME = 0x00;
	public const ushort ALPHA_P_101 = 0x01;
	public const ushort BROTHER_15XL = 0x02;
	public const ushort CBM_MPS1000 = 0x03;
	public const ushort DIAB_630 = 0x04;
	public const ushort DIAB_ADV_D25 = 0x05;
	public const ushort DIAB_C_150 = 0x06;
	public const ushort EPSON = 0x07;
	public const ushort EPSON_JX_80 = 0x08;
	public const ushort OKIMATE_20 = 0x09;
	public const ushort QUME_LP_20 = 0x0A;
	public const ushort HP_LASERJET = 0x0B;
	public const ushort HP_LASERJET_PLUS = 0x0C;
	public const byte SBUF_512 = 0x00;
	public const byte SBUF_1024 = 0x01;
	public const byte SBUF_2048 = 0x02;
	public const byte SBUF_4096 = 0x03;
	public const byte SBUF_8000 = 0x04;
	public const byte SBUF_16000 = 0x05;
	public const byte SREAD_BITS = 0xF0;
	public const byte SWRITE_BITS = 0x0F;
	public const byte SSTOP_BITS = 0xF0;
	public const byte SBUFSIZE_BITS = 0x0F;
	public const byte SPARITY_BITS = 0xF0;
	public const byte SHSHAKE_BITS = 0x0F;
	public const byte SPARITY_NONE = 0;
	public const byte SPARITY_EVEN = 1;
	public const byte SPARITY_ODD = 2;
	public const byte SPARITY_MARK = 3;
	public const byte SPARITY_SPACE = 4;
	public const byte SHSHAKE_XON = 0;
	public const byte SHSHAKE_RTS = 1;
	public const byte SHSHAKE_NONE = 2;
	public const ushort CORRECT_RED = 0x0001;
	public const ushort CORRECT_GREEN = 0x0002;
	public const ushort CORRECT_BLUE = 0x0004;
	public const ushort CENTER_IMAGE = 0x0008;
	public const ushort IGNORE_DIMENSIONS = 0x0000;
	public const ushort BOUNDED_DIMENSIONS = 0x0010;
	public const ushort ABSOLUTE_DIMENSIONS = 0x0020;
	public const ushort PIXEL_DIMENSIONS = 0x0040;
	public const ushort MULTIPLY_DIMENSIONS = 0x0080;
	public const ushort INTEGER_SCALING = 0x0100;
	public const ushort ORDERED_DITHERING = 0x0000;
	public const ushort HALFTONE_DITHERING = 0x0200;
	public const ushort FLOYD_DITHERING = 0x0400;
	public const ushort ANTI_ALIAS = 0x0800;
	public const ushort GREY_SCALE2 = 0x1000;
	public const ushort CORRECT_RGB_MASK = CORRECT_RED | CORRECT_GREEN | CORRECT_BLUE;
	public const ushort DIMENSIONS_MASK = BOUNDED_DIMENSIONS |
		ABSOLUTE_DIMENSIONS | PIXEL_DIMENSIONS | MULTIPLY_DIMENSIONS;
	public const ushort DITHERING_MASK = HALFTONE_DITHERING | FLOYD_DITHERING;
}

/// <summary>Legacy fixed-width Preferences structure used by V40 APIs.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct Preferences
{
	public const uint Size = 234;
	public sbyte FontHeight;
	public byte PrinterPort;
	public ushort BaudRate;
	public TimeVal KeyRptSpeed;
	public TimeVal KeyRptDelay;
	public TimeVal DoubleClick;
	public fixed ushort PointerMatrix[IntuitionPreferencesConstants.POINTERSIZE];
	public sbyte XOffset;
	public sbyte YOffset;
	public ushort color17;
	public ushort color18;
	public ushort color19;
	public ushort PointerTicks;
	public ushort color0;
	public ushort color1;
	public ushort color2;
	public ushort color3;
	public sbyte ViewXOffset;
	public sbyte ViewYOffset;
	public short ViewInitX;
	public short ViewInitY;
	public int EnableCLI;
	public ushort PrinterType;
	public fixed byte PrinterFilename[IntuitionPreferencesConstants.FILENAME_SIZE];
	public ushort PrintPitch;
	public ushort PrintQuality;
	public ushort PrintSpacing;
	public ushort PrintLeftMargin;
	public ushort PrintRightMargin;
	public ushort PrintImage;
	public ushort PrintAspect;
	public ushort PrintShade;
	public short PrintThreshold;
	public ushort PaperSize;
	public ushort PaperLength;
	public ushort PaperType;
	public byte SerRWBits;
	public byte SerStopBuf;
	public byte SerParShk;
	public byte LaceWB;
	public fixed byte Pad[12];
	public fixed byte PrtDevName[IntuitionPreferencesConstants.DEVNAME_SIZE];
	public byte DefaultPrtUnit;
	public byte DefaultSerUnit;
	public sbyte RowSizeChange;
	public sbyte ColumnSizeChange;
	public ushort PrintFlags;
	public ushort PrintMaxWidth;
	public ushort PrintMaxHeight;
	public byte PrintDensity;
	public byte PrintXOffset;
	public ushort wb_Width;
	public ushort wb_Height;
	public byte wb_Depth;
	public byte ext_size;
}
