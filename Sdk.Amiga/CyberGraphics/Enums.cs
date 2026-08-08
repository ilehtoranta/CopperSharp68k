/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum CyberPixelFormat : uint
{
	Lut8 = 0,
	Rgb15 = 1,
	Rgb15X = 2,
	Rgb15Pc = 3,
	Bgr15Pc = 4,
	Rgb16 = 5,
	Bgr16 = 6,
	Rgb16Pc = 7,
	Bgr16Pc = 8,
	Rgb24 = 9,
	Bgr24 = 10,
	Argb32 = 11,
	Bgra32 = 12,
	Rgba32 = 13,
}

public enum CyberRectangleFormat : uint
{
	Rgb = 0,
	Rgba = 1,
	Argb = 2,
	Lut8 = 3,
	Grey8 = 4,
	Raw = 5,
}

public enum CyberDpmsLevel : uint
{
	On = 0,
	Standby = 1,
	Suspend = 2,
	Off = 3,
}

public enum CyberProcessPixelOperation : uint
{
	Brighten = 0,
	Darken = 1,
	SetAlpha = 2,
	Tint = 3,
	Blur = 4,
	ColorToGrey = 5,
	Negative = 6,
	NegativeFade = 7,
	TintFade = 8,
	Gradient = 9,
	ShiftRgb = 10,
}

public enum CyberDestinationAlphaValue : uint
{
	Undefined = 0,
	One = 1,
	UseSource = 2,
	UseDestination = 3,
}
