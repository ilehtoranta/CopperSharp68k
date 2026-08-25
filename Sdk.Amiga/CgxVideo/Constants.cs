/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum CgxVideoTag : uint
{
	LeftIndent = 0x88000001,
	RightIndent = 0x88000002,
	TopIndent = 0x88000003,
	BottomIndent = 0x88000004,
	SourceType = 0x88000005,
	SourceWidth = 0x88000006,
	SourceHeight = 0x88000007,
	Error = 0x88000008,
	UseColorKey = 0x88000009,
	UseBackfill = 0x8800000A,
	Identifier = 0x8800000B,
	UseFilter = 0x8800000C,
	DoubleBuffer = 0x8800000D,
	Interlaced = 0x8800000E,
	CaptureMode = 0x8800000F,
	FrameIndex = 0x88000010,
	MultiBuffer = 0x88000011,
	ZoomRect = 0x88000012,
	BaseAddress = 0x88000030,
	ColorKeyPen = 0x88000031,
	ColorKey = 0x88000032,
	FrameBase0 = 0x88000033,
	FrameBase1 = 0x88000034,
	FrameType = 0x88000035,
	Width = 0x88000036,
	Height = 0x88000037,
	Modulo = 0x88000038,
	BaseOffset = 0x88000039,
	BaseOffset0 = 0x88000040,
	BaseOffset1 = 0x88000041,
	BaseOffset2 = 0x88000042,
	BaseOffset3 = 0x88000043,
	BaseOffset4 = 0x88000044,
	BaseOffset5 = 0x88000045,
	Color0SubPicture = 0x88000050,
	Color1SubPicture = 0x88000051,
	Color2SubPicture = 0x88000052,
	Color3SubPicture = 0x88000053,
	Color4SubPicture = 0x88000054,
	Color5SubPicture = 0x88000055,
	Color6SubPicture = 0x88000056,
	Color7SubPicture = 0x88000057,
	Color8SubPicture = 0x88000058,
	Color9SubPicture = 0x88000059,
	Color10SubPicture = 0x8800005A,
	Color11SubPicture = 0x8800005B,
	Color12SubPicture = 0x8800005C,
	Color13SubPicture = 0x8800005D,
	Color14SubPicture = 0x8800005E,
	Color15SubPicture = 0x8800005F,
	SubPicture = 0x88000060,
	EnableSubPicture = 0x88000061,
	StreamRectSubPicture = 0x88000062,
	ColorControlSubPicture = 0x88000063,
	HighlightRectSubPicture = 0x88000064,
	HighlightEnableSubPicture = 0x88000065,
	HighlightColorControlSubPicture = 0x88000066,
	SourceWidthSubPicture = 0x88000067,
	SourceHeightSubPicture = 0x88000068,
	ColorKeyFill = 0x88000070,
}

public enum CgxVideoQueryTag : uint
{
	Dummy = 0x800A5000,
	SupportedFeatures = 0x800A5001,
	SupportedFormats = 0x800A5002,
	MaximumWidth = 0x800A5003,
	MaximumSubPictureWidth = 0x800A5004,
}

[System.Flags]
public enum CgxVideoFeature : uint
{
	Overlay = 1u << 0,
	DoubleBuffer = 1u << 1,
	MultiBuffer = 1u << 2,
	ColorKeying = 1u << 3,
	Filtering = 1u << 4,
	CaptureMode = 1u << 5,
	Interlace = 1u << 6,
	ZoomRect = 1u << 7,
	SubPicture = 1u << 8,
}

[System.Flags]
public enum CgxVideoFormat : uint
{
	Yuyv = 1u << 0,
	R5G5B5LittleEndian = 1u << 1,
	R5G6B5LittleEndian = 1u << 2,
	Yuv420Planar = 1u << 3,
}

public enum CgxVideoSourceFormat : uint
{
	Yuv16Obsolete = 0,
	YCbCr16 = 1,
	Rgb15ByteSwapped = 2,
	R5G5B5Pc = Rgb15ByteSwapped,
	Rgb16ByteSwapped = 3,
	R5G6B5Pc = Rgb16ByteSwapped,
	YCbCr420 = 4,
}

public enum CgxVideoError : uint
{
	Ok = 0,
	InvalidScreenMode = 1,
	NoOverlayMemory = 2,
	InvalidSourceFormat = 3,
	NoMemory = 4,
}
