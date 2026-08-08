/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum CyberBitmapAttribute : uint
{
	BytesPerRow = 0x80000001,
	BytesPerPixel = 0x80000002,
	PixelFormat = 0x80000004,
	Width = 0x80000005,
	Height = 0x80000006,
	Depth = 0x80000007,
	IsCyberGraphics = 0x80000008,
	IsLinearMemory = 0x80000009,
	ColorMap = 0x8000000a,
}

public enum CyberIdAttribute : uint
{
	PixelFormat = 0x80000001,
	Width = 0x80000002,
	Height = 0x80000003,
	Depth = 0x80000004,
	BytesPerPixel = 0x80000005,
}

public enum CyberGradientType : uint
{
	Horizontal = 0,
	Vertical = 1,
	Rectangle = 2,
	LinearAngle = 3,
	Radial = 4,
}

public enum CyberRgbShift : uint
{
	Bgr = 1,
	Brg = 2,
	Gbr = 3,
	Grb = 4,
	Rbg = 5,
}

public enum CyberColorTableFormat : uint
{
	Xrgb8 = 0,
}

public enum CyberModeRequestTag : uint
{
	MinimumDepth = 0x80040000,
	MaximumDepth = 0x80040001,
	MinimumWidth = 0x80040002,
	MaximumWidth = 0x80040003,
	MinimumHeight = 0x80040004,
	MaximumHeight = 0x80040005,
	ColorModelArray = 0x80040006,
	WindowTitle = 0x80040014,
	OkText = 0x80040015,
	CancelText = 0x80040016,
	Screen = 0x8004001e,
}

public enum CyberBestModeIdTag : uint
{
	Depth = 0x80050000,
	NominalWidth = 0x80050001,
	NominalHeight = 0x80050002,
	MonitorId = 0x80050003,
	BoardName = 0x80050005,
}

public enum CyberVideoControlTag : uint
{
	DpmsLevel = 0x88002001,
}

public enum CyberBitmapLockTag : uint
{
	Width = 0x84001001,
	Height = 0x84001002,
	Depth = 0x84001003,
	PixelFormat = 0x84001004,
	BytesPerPixel = 0x84001005,
	BytesPerRow = 0x84001006,
	BaseAddress = 0x84001007,
}

public enum CyberBitmapUnlockTag : uint
{
	UpdateRectangles = 0x85001001,
	ReallyUnlock = 0x85001002,
}

public enum CyberProcessPixelTag : uint
{
	FadeFullScale = 0x85231020,
	FadeOffset = 0x85231021,
	GradientType = 0x85231022,
	GradientColor1 = 0x85231023,
	GradientColor2 = 0x85231024,
	RgbMask = 0x85231025,
	GradientSymmetricCenter = 0x85231026,
}

public enum CyberBitmapAlphaTag : uint
{
	MixLevel = 0x88802000,
	UseSourceAlpha = 0x88802001,
	DestinationAlphaValue = 0x88802002,
}

[System.Flags]
public enum CyberExtendedBitmapFlags : uint
{
	SpecialFormat = 1u << 7,
	RootMap = 1u << 5,
	ThreeDTarget = 1u << 8,
}

public static class CyberGraphicsConstants
{
	public const uint TagUser = 0x80000000u;
	public const uint ModeRequestTagBase = TagUser + 0x40000u;
	public const uint BestModeIdTagBase = TagUser + 0x50000u;
	public const uint GradientTypeCount = 2;

	public static uint ShiftPixelFormat(CyberPixelFormat format) => (uint)format << 24;
}
