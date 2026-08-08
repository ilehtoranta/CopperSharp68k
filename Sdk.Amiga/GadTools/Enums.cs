/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum GadToolsKind : ushort
{
	Generic = 0,
	Button = 1,
	CheckBox = 2,
	Integer = 3,
	ListView = 4,
	Mx = 5,
	Number = 6,
	Cycle = 7,
	Palette = 8,
	Scroller = 9,
	Slider = 11,
	String = 12,
	Text = 13,
}

[System.Flags]
public enum NewGadgetFlags : uint
{
	PlaceTextLeft = 0x0001,
	PlaceTextRight = 0x0002,
	PlaceTextAbove = 0x0004,
	PlaceTextBelow = 0x0008,
	PlaceTextIn = 0x0010,
	HighlightLabel = 0x0020,
}

public enum NewMenuType : byte
{
	End = 0,
	Title = 1,
	Item = 2,
	SubItem = 3,
	Ignore = 64,
	MenuImage = 128,
}

[System.Flags]
public enum GadToolsMenuFlags : ushort
{
	CommandString = 0x0020,
}

public enum GadToolsJustification : uint
{
	Left = 0,
	Right = 1,
	Center = 2,
}

public enum GadToolsListViewState : uint
{
	Normal = 0,
	Selected = 1,
	NormalDisabled = 2,
	SelectedDisabled = 8,
}

public enum GadToolsCallbackResult : uint
{
	Ok = 0,
	Unknown = 1,
}
