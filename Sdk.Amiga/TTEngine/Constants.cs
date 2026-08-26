/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum TTEngineTag : uint
{
	FontFile = 0x6EDA0000,
	FontStyle = 0x6EDA0001,
	FamilyTable = 0x6EDA0002,
	FontSize = 0x6EDA0003,
	FontWeight = 0x6EDA0004,
	ColorMap = 0x6EDA0005,
	Screen = 0x6EDA0006,
	Window = 0x6EDA0007,
	FontAscender = 0x6EDA0008,
	FontDescender = 0x6EDA0009,
	Antialias = 0x6EDA000F,
	Encoding = 0x6EDA0010,
	FontName = 0x6EDA0011,
	FamilyName = 0x6EDA0012,
	SubfamilyName = 0x6EDA0013,
	Transparency = 0x6EDA0014,
	ScaleX = 0x6EDA0015,
	SoftStyle = 0x6EDA0017,
	Foreground = 0x6EDA0018,
	Background = 0x6EDA0019,
	FontMaxTop = 0x6EDA001E,
	FontBaseline = FontMaxTop,
	FontMaxBottom = 0x6EDA001F,
	FontDesignHeight = 0x6EDA0020,
	FontRealAscender = 0x6EDA0021,
	FontRealDescender = 0x6EDA0022,
	FontAccentedAscender = 0x6EDA0023,
	CustomEncoding = 0x6EDA0024,
	Gamma = 0x6EDA0025,
	FontFixedWidth = 0x6EDA0026,
	FontHeight = 0x6EDA0027,
	FontWidth = 0x6EDA0028,
	DiskFontMetrics = 0x6EDA0029,
	ForceFixedWidth = 0x6EDA0030,
	PrintMode = 0x6EDA0033,
	DestinationAlpha = 0x6EDA0034,
}

public enum TTEngineFontStyle : uint
{
	Regular = 0,
	Italic = 1,
}

public enum TTEngineFontWeight : uint
{
	Normal = 400,
	Bold = 700,
}

public enum TTEngineAntialias : uint
{
	Auto = 0,
	Off = 1,
	On = 2,
}

[System.Flags]
public enum TTEngineSoftStyle : ushort
{
	None = 0,
	Underlined = 0x0001,
	DoubleUnderlined = 0x0002,
	Overstriked = 0x0004,
	DoubleOverstriked = 0x0008,
}

public enum TTEngineEncoding : int
{
	System = -3,
	SystemUtf8 = -1,
	Default = 0,
	Iso8859_1 = 4,
	Iso8859_2 = 5,
	Iso8859_3 = 6,
	Iso8859_4 = 7,
	Iso8859_5 = 8,
	Iso8859_6 = 9,
	Iso8859_7 = 10,
	Iso8859_8 = 11,
	Iso8859_9 = 12,
	Iso8859_10 = 13,
	Iso8859_11 = 14,
	Iso8859_13 = 109,
	Iso8859_14 = 110,
	Iso8859_15 = 111,
	Iso8859_16 = 112,
	Utf8 = 106,
	Utf16BigEndian = 1013,
	Utf16LittleEndian = 1014,
	Utf16 = 1015,
	Utf32BigEndian = 1018,
	Utf32LittleEndian = 1019,
	Utf32 = 1017,
}

public enum TTEngineDestinationAlpha : uint
{
	UseRastPort = 0,
	One = 1,
	Destination = 2,
	Source = 3,
	Mix = 4,
}

public static class TTEngineConstants
{
	public const uint TransparencyUseRastPort = 0xFFFFFF00;
	public const uint ForegroundUseRastPort = uint.MaxValue;
	public const uint BackgroundUseRastPort = uint.MaxValue;
}

public enum TTEngineRequesterTag : uint
{
	Window = 0x6EDA2000,
	PublicScreenName = 0x6EDA2001,
	Screen = 0x6EDA2002,
	SleepWindow = 0x6EDA2003,
	TitleText = 0x6EDA2004,
	PositiveText = 0x6EDA2005,
	NegativeText = 0x6EDA2006,
	InitialLeftEdge = 0x6EDA2007,
	InitialTopEdge = 0x6EDA2008,
	InitialWidth = 0x6EDA2009,
	InitialHeight = 0x6EDA200A,
	DoSizes = 0x6EDA200B,
	DoWeight = 0x6EDA200C,
	DoStyle = 0x6EDA200D,
	Activate = 0x6EDA200E,
	InitialSize = 0x6EDA200F,
	InitialName = 0x6EDA2010,
	InitialStyle = 0x6EDA2011,
	DoPreview = 0x6EDA2012,
	FixedWidthOnly = 0x6EDA2013,
}

/// <summary>Typed builders for stack-allocated TTengine tag lists.</summary>
public static class TTEngineTags
{
	public static TagItem Item(TTEngineTag tag, uint value) =>
		TagItem.Create((uint)tag, value);

	public static TagItem Item(TTEngineTag tag, APTR value) =>
		Item(tag, value.Raw);

	public static TagItem Item(TTEngineTag tag, TTEngineFontStyle value) =>
		Item(tag, (uint)value);

	public static TagItem Item(TTEngineTag tag, TTEngineFontWeight value) =>
		Item(tag, (uint)value);

	public static TagItem Item(TTEngineTag tag, TTEngineAntialias value) =>
		Item(tag, (uint)value);

	public static TagItem Item(TTEngineTag tag, TTEngineSoftStyle value) =>
		Item(tag, (uint)value);

	public static TagItem Item(TTEngineTag tag, TTEngineEncoding value) =>
		Item(tag, unchecked((uint)(int)value));

	public static TagItem Item(TTEngineTag tag, TTEngineDestinationAlpha value) =>
		Item(tag, (uint)value);

	public static TagItem RequesterItem(TTEngineRequesterTag tag, uint value) =>
		TagItem.Create((uint)tag, value);

	public static TagItem RequesterItem(TTEngineRequesterTag tag, APTR value) =>
		RequesterItem(tag, value.Raw);
}
