/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum DataTypeFlags : ushort
{
	Binary = 0x0000,
	Ascii = 0x0001,
	Iff = 0x0002,
	Misc = 0x0003,
	CaseSensitive = 0x0010,
	System1 = 0x1000,
}

public enum DataTypeSourceType : uint
{
	Ram = 1,
	File = 2,
	Clipboard = 3,
	HotLink = 4,
	Memory = 5,
}

public enum DataTypeToolKind : ushort
{
	Info = 1,
	Browse = 2,
	Edit = 3,
	Print = 4,
	Mail = 5,
}

public enum DataTypeToolFlags : ushort
{
	Shell = 1,
	Workbench = 2,
	Rexx = 3,
}

[System.Flags]
public enum DataTypeSpecialInfoFlags : uint
{
	Layout = 1u << 0,
	NewSize = 1u << 1,
	Dragging = 1u << 2,
	DragSelect = 1u << 3,
	Highlight = 1u << 4,
	Printing = 1u << 5,
	LayoutProcess = 1u << 6,
}

[System.Flags]
public enum FrameInfoFlags : uint
{
	Scalable = 1,
	Scrollable = 2,
	Remappable = 4,
}

public enum DataTypeWriteMode : uint
{
	Iff = 0,
	Raw = 1,
}

public enum DataTypeTriggerFunction : uint
{
	Pause = 1,
	Play = 2,
	Contents = 3,
	Index = 4,
	Retrace = 5,
	BrowsePrevious = 6,
	BrowseNext = 7,
	NextField = 8,
	PreviousField = 9,
	ActivateField = 10,
	Command = 11,
	Rewind = 12,
	FastForward = 13,
	Stop = 14,
	Resume = 15,
	Locate = 16,
}
