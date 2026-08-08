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

public enum KeyMapLanguage : uint
{
	Unknown = 0,
	American = 1,
	English = 2,
	German = 3,
	French = 4,
	Spanish = 5,
	Italian = 6,
	Portuguese = 7,
	Danish = 8,
	Dutch = 9,
	Norwegian = 10,
	Finnish = 11,
	Swedish = 12,
	Japanese = 13,
	Chinese = 14,
	Arabic = 15,
	Greek = 16,
	Hebrew = 17,
	Korean = 18,
}
