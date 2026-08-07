/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum IDCMPFlags : uint
{
	None = 0,
	SizeVerify = 0x0000_0001u,
	NewSize = 0x0000_0002u,
	RefreshWindow = 0x0000_0004u,
	MouseButtons = 0x0000_0008u,
	MouseMove = 0x0000_0010u,
	GadgetDown = 0x0000_0020u,
	GadgetUp = 0x0000_0040u,
	RequesterSet = 0x0000_0080u,
	MenuPick = 0x0000_0100u,
	CloseWindow = 0x0000_0200u,
	RawKey = 0x0000_0400u,
	RequesterVerify = 0x0000_0800u,
	RequesterClear = 0x0000_1000u,
	MenuVerify = 0x0000_2000u,
	NewPrefs = 0x0000_4000u,
	DiskInserted = 0x0000_8000u,
	DiskRemoved = 0x0001_0000u,
	WorkbenchMessage = 0x0002_0000u,
	ActiveWindow = 0x0004_0000u,
	InactiveWindow = 0x0008_0000u,
	DeltaMove = 0x0010_0000u,
	VanillaKey = 0x0020_0000u,
	IntuiTicks = 0x0040_0000u,
	IDCMPUpdate = 0x0080_0000u,
	MenuHelp = 0x0100_0000u,
	ChangeWindow = 0x0200_0000u,
	GadgetHelp = 0x0400_0000u,
	LonelyMessage = 0x8000_0000u,
}

public enum IDCMPCode : ushort
{
	WindowMoveSize = 0x0000,
	WindowDepth = 0x0001,
	MenuHot = 0x0001,
	MenuCancel = 0x0002,
	MenuWaiting = 0x0003,
	VerificationAbort = 0x0004,
	WorkbenchOpen = 0x0001,
	WorkbenchClose = 0x0002,
}

[System.Flags]
public enum WindowFlags : uint
{
	None = 0,
	SizeGadget = 0x0000_0001u,
	DragBar = 0x0000_0002u,
	DepthGadget = 0x0000_0004u,
	CloseGadget = 0x0000_0008u,
	SizeBright = 0x0000_0010u,
	SizeBottom = 0x0000_0020u,
	RefreshMask = 0x0000_00C0u,
	SmartRefresh = 0x0000_0000u,
	SimpleRefresh = 0x0000_0040u,
	SuperBitmap = 0x0000_0080u,
	OtherRefresh = 0x0000_00C0u,
	Backdrop = 0x0000_0100u,
	ReportMouse = 0x0000_0200u,
	GimmeZeroZero = 0x0000_0400u,
	Borderless = 0x0000_0800u,
	Activate = 0x0000_1000u,
	WindowActive = 0x0000_2000u,
	InRequest = 0x0000_4000u,
	MenuState = 0x0000_8000u,
	RmbTrap = 0x0001_0000u,
	NoCareRefresh = 0x0002_0000u,
	NewWindowExtended = 0x0004_0000u,
	NewLookMenus = 0x0020_0000u,
	WindowRefresh = 0x0100_0000u,
	WorkbenchWindow = 0x0200_0000u,
	WindowTicked = 0x0400_0000u,
	Visitor = 0x0800_0000u,
	Zoomed = 0x1000_0000u,
	HasZoom = 0x2000_0000u,
}

[System.Flags]
public enum GadgetFlags : ushort
{
	None = 0,
	HighlightMask = 0x0003,
	HighlightComplement = 0x0000,
	HighlightBox = 0x0001,
	HighlightImage = 0x0002,
	HighlightNone = 0x0003,
	GadgetImage = 0x0004,
	RelativeBottom = 0x0008,
	RelativeRight = 0x0010,
	RelativeWidth = 0x0020,
	RelativeHeight = 0x0040,
	Selected = 0x0080,
	Disabled = 0x0100,
	TabCycle = 0x0200,
	StringExtend = 0x0400,
	ImageDisable = 0x0800,
	LabelMask = 0x3000,
	LabelIntuiText = 0x0000,
	LabelString = 0x1000,
	LabelImage = 0x2000,
	RelativeSpecial = 0x4000,
	Extended = 0x8000,
}

[System.Flags]
public enum GadgetActivationFlags : ushort
{
	None = 0,
	RelativeVerify = 0x0001,
	Immediate = 0x0002,
	EndGadget = 0x0004,
	FollowMouse = 0x0008,
	RightBorder = 0x0010,
	LeftBorder = 0x0020,
	TopBorder = 0x0040,
	BottomBorder = 0x0080,
	ToggleSelect = 0x0100,
	StringCenter = 0x0200,
	StringRight = 0x0400,
	LongInteger = 0x0800,
	AlternateKeyMap = 0x1000,
	StringExtend = 0x2000,
	BooleanExtend = 0x2000,
	ActiveGadget = 0x4000,
	BorderSniff = 0x8000,
}

[System.Flags]
public enum GadgetType : ushort
{
	None = 0,
	GadgetTypeMask = 0xFC00,
	ScreenGadget = 0x4000,
	GzzGadget = 0x2000,
	RequesterGadget = 0x1000,
	SystemGadget = 0x8000,
	SystemTypeMask = 0x00F0,
	Sizing = 0x0010,
	WindowDragging = 0x0020,
	ScreenDragging = 0x0030,
	WindowDepth = 0x0040,
	ScreenDepth = 0x0050,
	WindowZoom = 0x0060,
	ScreenUnused = 0x0070,
	Close = 0x0080,
	WindowToFront = WindowDepth,
	ScreenToFront = ScreenDepth,
	WindowToBack = WindowZoom,
	ScreenToBack = ScreenUnused,
	GadgetClassMask = 0x0007,
	BooleanGadget = 0x0001,
	Gadget0002 = 0x0002,
	ProportionalGadget = 0x0003,
	StringGadget = 0x0004,
	CustomGadget = 0x0005,
}

[System.Flags]
public enum GadgetMoreFlags : uint
{
	None = 0,
	Bounds = 0x0000_0001u,
	GadgetHelp = 0x0000_0002u,
	ScrollRaster = 0x0000_0004u,
}

[System.Flags]
public enum MenuFlags : ushort
{
	None = 0,
	Enabled = 0x0001,
	Drawn = 0x0100,
}

[System.Flags]
public enum MenuItemFlags : ushort
{
	None = 0,
	CheckIt = 0x0001,
	ItemText = 0x0002,
	CommandSequence = 0x0004,
	MenuToggle = 0x0008,
	Enabled = 0x0010,
	HighlightMask = 0x00C0,
	HighlightImage = 0x0000,
	HighlightComplement = 0x0040,
	HighlightBox = 0x0080,
	HighlightNone = 0x00C0,
	Checked = 0x0100,
	Drawn = 0x1000,
	Highlighted = 0x2000,
	MenuToggled = 0x4000,
}

[System.Flags]
public enum RequesterFlags : ushort
{
	None = 0,
	PointerRelative = 0x0001,
	Predrawn = 0x0002,
	Noisy = 0x0004,
	SimpleRefresh = 0x0010,
	UseRequesterImage = 0x0020,
	NoBackFill = 0x0040,
	OffWindow = 0x1000,
	Active = 0x2000,
	System = 0x4000,
	DeferRefresh = 0x8000,
}

[System.Flags]
public enum PropInfoFlags : ushort
{
	None = 0,
	AutoKnob = 0x0001,
	FreeHorizontal = 0x0002,
	FreeVertical = 0x0004,
	Borderless = 0x0008,
	NewLook = 0x0010,
	KnobHit = 0x0100,
}

[System.Flags]
public enum ScreenFlags : ushort
{
	None = 0,
	ScreenTypeMask = 0x000F,
	Workbench = 0x0001,
	Public = 0x0002,
	Custom = 0x000F,
	ShowTitle = 0x0010,
	Beeping = 0x0020,
	CustomBitmap = 0x0040,
	Behind = 0x0080,
	Quiet = 0x0100,
	HighResolution = 0x0200,
	PensShared = 0x0400,
	Extended = 0x1000,
	AutoScroll = 0x4000,
}

public enum ScreenType : ushort
{
	Workbench = 0x0001,
	Public = 0x0002,
	Custom = 0x000F,
}

[System.Flags]
public enum ScreenViewModes : ushort
{
	None = 0,
	GenlockVideo = 0x0002,
	Interlace = 0x0004,
	DoubleScan = 0x0008,
	SuperHighResolution = 0x0020,
	PlayfieldB = 0x0040,
	ExtraHalfBrite = 0x0080,
	GenlockAudio = 0x0100,
	DualPlayfield = 0x0400,
	HoldAndModify = 0x0800,
	ExtendedMode = 0x1000,
	ViewportHidden = 0x2000,
	Sprites = 0x4000,
	HighResolution = 0x8000,
}

public enum OverscanType : ushort
{
	Text = 1,
	Standard = 2,
	Maximum = 3,
	Video = 4,
}

[System.Flags]
public enum PublicScreenModes : ushort
{
	None = 0,
	Shanghai = 0x0001,
	PopPublicScreen = 0x0002,
}

[System.Flags]
public enum PublicScreenFlags : ushort
{
	None = 0,
	Private = 0x0001,
}

public enum ScreenDepthMode : ushort
{
	ToFront = 0,
	ToBack = 1,
	InFamily = 2,
}

[System.Flags]
public enum ScreenPositionMode : ushort
{
	Relative = 0,
	Absolute = 1,
	MakeVisible = 2,
	ForcedDrag = 4,
}

[System.Flags]
public enum BoolInfoFlags : ushort
{
	None = 0,
	Mask = 0x0001,
}

[System.Flags]
public enum BitMapFlags : byte
{
	None = 0,
	Clear = 0x01,
	Displayable = 0x02,
	Interleaved = 0x04,
	Standard = 0x08,
	MinimumPlanes = 0x10,
}

[System.Flags]
public enum RastPortFlags : ushort
{
	None = 0,
	FirstDot = 0x0001,
	OneDot = 0x0002,
	DoubleBuffered = 0x0004,
	AreaOutline = 0x0008,
	NoCrossFill = 0x0020,
}

[System.Flags]
public enum LayerFlags : ushort
{
	None = 0,
	Simple = 0x0001,
	Smart = 0x0002,
	Super = 0x0004,
	Updating = 0x0010,
	Backdrop = 0x0040,
	Refresh = 0x0080,
	ClipRectsLost = 0x0100,
	InternalRefresh = 0x0200,
	InternalRefresh2 = 0x0400,
}

[System.Flags]
public enum DrawInfoFlags : uint
{
	None = 0,
	NewLook = 0x0000_0001u,
}

public enum DrawInfoPen : ushort
{
	Detail = 0,
	Block = 1,
	Text = 2,
	Shine = 3,
	Shadow = 4,
	Fill = 5,
	FillText = 6,
	Background = 7,
	HighlightText = 8,
	BarDetail = 9,
	BarBlock = 10,
	BarTrim = 11,
	NumberOfPens = 12,
}

public enum DrawMode : byte
{
	Jam1 = 0,
	Jam2 = 1,
	Complement = 2,
	InverseVideo = 4,
}

public static class IntuitionConstants
{
	public const ushort NoMenu = 0x001F;
	public const ushort NoItem = 0x003F;
	public const ushort NoSub = 0x001F;
	public const ushort MenuNull = 0xFFFF;
	public const ushort MaxBody = 0xFFFF;
	public const ushort MaxPot = 0xFFFF;
	public const ushort KnobHorizontalMinimum = 6;
	public const ushort KnobVerticalMinimum = 4;
	public const ushort DefaultMouseQueue = 5;
	public const short StandardScreenHeight = -1;
	public const short StandardScreenWidth = -1;
	public const ushort MaxPublicScreenName = 139;
}
