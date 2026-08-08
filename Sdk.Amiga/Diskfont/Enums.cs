/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum AvailableFontType : ushort
{
	Memory = 0,
	Disk = 1,
	Scaled = 2,
	Bitmap = 3,
	Tagged = 16,
}

public enum DiskFontIdentifier : ushort
{
	FontContents = 0x0f00,
	TaggedFontContents = 0x0f02,
	OutlineFontContents = 0x0f03,
	DiskFont = 0x0f80,
}
