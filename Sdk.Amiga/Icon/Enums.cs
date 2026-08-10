/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum IconAspectRatio : byte
{
	Unknown = 0,
}

public enum IconDefaultType : int
{
	Disk = 1,
	Drawer = 2,
	Tool = 3,
	Project = 4,
	Garbage = 5,
	Device = 6,
	Kick = 7,
}

[System.Flags]
public enum IconIdentifyOptions : uint
{
	None = 0,
}
