/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum AslRequestType : uint
{
	File = 0,
	Font = 1,
	ScreenMode = 2,
}

[System.Flags]
public enum AslFileRequesterFlags : uint
{
	FilterFunction = 1u << 7,
	IntuiFunction = 1u << 6,
	SaveMode = 1u << 5,
	PrivateIcmp = 1u << 4,
	MultiSelect = 1u << 3,
	Patterns = 1u,
}

[System.Flags]
public enum AslFileRequesterFlags2 : uint
{
	DrawersOnly = 1u,
	FilterDrawers = 2u,
	RejectIcons = 4u,
}

[System.Flags]
public enum AslFontRequesterFlags : uint
{
	FrontPen = 1u,
	BackPen = 1u << 1,
	Style = 1u << 2,
	DrawMode = 1u << 3,
	FixedWidthOnly = 1u << 4,
	PrivateIcmp = 1u << 5,
	IntuiFunction = 1u << 6,
	FilterFunction = 1u << 7,
}

public enum AslSortBy : byte
{
	Name = 0,
	Date = 1,
	Size = 2,
}

public enum AslSortDrawers : byte
{
	First = 0,
	Mix = 1,
	Last = 2,
}

public enum AslSortOrder : byte
{
	Ascending = 0,
	Descending = 1,
}

public enum AslPosition : byte
{
	Default = 0,
	CenterWindow = 1,
	CenterScreen = 2,
	WindowPosition = 3,
	ScreenPosition = 4,
	CenterMouse = 5,
}

[System.Flags]
public enum AslWindowOptions : byte
{
	RelativeSize = 1 << 4,
	Overrides = 1 << 6,
}
