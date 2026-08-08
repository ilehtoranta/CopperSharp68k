/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum NonvolatileEntryFlags : uint
{
	Delete = 1,
	ApplicationName = 1u << 31,
}

public enum NonvolatileError : int
{
	BadName = 1,
	WriteProtected = 2,
	Failure = 3,
	Fatal = 4,
}
