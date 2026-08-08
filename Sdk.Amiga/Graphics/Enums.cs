/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum GraphicsDisplayFlags : ushort
{
	Ntsc = 1,
	Genlock = 2,
	Pal = 4,
	TodaSafe = 8,
	ReallyPal = 16,
	LightPenSwapFrames = 32,
}

[System.Flags]
public enum DisplayInfoAvailability : ushort
{
	NoChips = 1,
	NoMonitor = 2,
	NotWithGenlock = 4,
}

[System.Flags]
public enum DisplayInfoPropertyFlags : uint
{
	IsLace = 0x00000001,
	IsDualPlayfield = 0x00000002,
	IsPlayfieldTwoPriority = 0x00000004,
	IsHam = 0x00000008,
	IsEcs = 0x00000010,
	IsPal = 0x00000020,
	IsSprites = 0x00000040,
	IsGenlock = 0x00000080,
	IsWorkbench = 0x00000100,
	IsDraggable = 0x00000200,
	IsPanelled = 0x00000400,
	IsBeamSync = 0x00000800,
	IsExtraHalfBrite = 0x00001000,
	IsAttachedSprites = 0x00002000,
	IsVariableSpriteResolution = 0x00004000,
	IsBorderSprites = 0x00008000,
	IsAa = 0x00010000,
	IsScanDoubled = 0x00020000,
	IsVariableSpriteBase = 0x00040000,
	IsVariableSpritePriority = 0x00080000,
	IsDoubleBuffered = 0x00100000,
	IsProgrammedBeam = 0x00200000,
	IsForeign = 0x80000000,
}

[System.Flags]
public enum VSpriteFlags : ushort
{
	VSprite = 0x0001,
	SaveBack = 0x0002,
	Overlay = 0x0004,
	MustDraw = 0x0008,
	BackSaved = 0x0100,
	BobUpdate = 0x0200,
	GelGone = 0x0400,
	VSpriteOverflow = 0x0800,
}

[System.Flags]
public enum BobFlags : ushort
{
	SaveBob = 0x0001,
	BobIsComponent = 0x0002,
	Waiting = 0x0100,
	Drawn = 0x0200,
	BobsAway = 0x0400,
	BobNix = 0x0800,
	SavePreserve = 0x1000,
	OutStep = 0x2000,
}

[System.Flags]
public enum ColorMapFlags : byte
{
	Transparency = 0x01,
	ColorPlaneTransparency = 0x02,
	BorderBlanking = 0x04,
	BorderNoTransparency = 0x08,
	VideoControlBatch = 0x10,
	UserCopperClip = 0x20,
	BorderSprites = 0x40,
}

[System.Flags]
public enum CopperInstructionFlags : ushort
{
	NotLongFrame = 0x8000,
	NotShortFrame = 0x4000,
	NotSystem = 0x2000,
}

[System.Flags]
public enum ViewModes : ushort
{
	GenlockVideo = 0x0002,
	Lace = 0x0004,
	DoubleScan = 0x0008,
	SuperHires = 0x0020,
	PlayfieldB = 0x0040,
	ExtraHalfBrite = 0x0080,
	GenlockAudio = 0x0100,
	DualPlayfield = 0x0400,
	Ham = 0x0800,
	ExtendedMode = 0x1000,
	Hide = 0x2000,
	Sprites = 0x4000,
	Hires = 0x8000,
}

public enum ColorMapType : byte
{
	V12 = 0,
	V36 = 1,
	V39 = 2,
}

public enum SpriteResolution : byte
{
	Ecs = 0,
	OneHundredFortyNs = 1,
	SeventyNs = 2,
	ThirtyFiveNs = 3,
	Default = 255,
}

[System.Flags]
public enum FontStyle : byte
{
	Underlined = 0x01,
	Bold = 0x02,
	Italic = 0x04,
	Extended = 0x08,
	ColorFont = 0x40,
	Tagged = 0x80,
}

[System.Flags]
public enum FontFlags : byte
{
	RomFont = 0x01,
	DiskFont = 0x02,
	ReversePath = 0x04,
	TallDot = 0x08,
	WideDot = 0x10,
	Proportional = 0x20,
	Designed = 0x40,
	Removed = 0x80,
}

[System.Flags]
public enum GraphicsColorTextFontFlags : ushort
{
	ColorFont = 0x0001,
	GreyFont = 0x0002,
	AntiAlias = 0x0004,
}

public enum GraphicsConstants : uint
{
	InvalidModeId = 0xffffffff,
	DefaultMonitorId = 0x00000000,
	NtscMonitorId = 0x00011000,
	PalMonitorId = 0x00021000,
	LoresKey = 0x00000000,
	HiresKey = 0x00008000,
	SuperKey = 0x00008020,
	HamKey = 0x00000800,
	LoresLaceKey = 0x00000004,
	HiresLaceKey = 0x00008004,
	SuperLaceKey = 0x00008024,
	HamLaceKey = 0x00000804,
}
