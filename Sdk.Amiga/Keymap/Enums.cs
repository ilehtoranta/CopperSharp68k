/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum KeyMapType : byte
{
	NoQualifier = 0,
	Vanilla = 7,
	Shift = 1,
	Alt = 2,
	Control = 4,
	DownUp = 8,
	Dead = 0x20,
	String = 0x40,
	NoOp = 0x80,
}

[System.Flags]
public enum DeadPrefixFlags : byte
{
	Modifier = 1,
	Dead = 8,
}
