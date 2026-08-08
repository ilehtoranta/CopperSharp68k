/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum AmigaGuideFlags : uint
{
	LoadIndex = 1,
	LoadAll = 2,
	CacheNode = 4,
	CacheDatabase = 8,
	Unique = 1u << 15,
	NoActivate = 1u << 16,
	SystemGadgets = 0x80000000,
}

public enum AmigaGuideHostMethod : uint
{
	FindNode = 1,
	OpenNode = 2,
	CloseNode = 3,
	Expunge = 10,
}

[System.Flags]
public enum AmigaGuideNodeFlags : uint
{
	Keep = 1,
	Ascii = 1u << 3,
	Clean = 1u << 5,
	Done = 1u << 6,
}

public enum AmigaGuideCrossReferenceType : ushort
{
	Generic = 0,
	Function = 1,
	Command = 2,
	Include = 3,
	Macro = 4,
	Struct = 5,
	Field = 6,
	Typedef = 7,
	Define = 8,
}

public enum AmigaGuideCallback : uint
{
	Open = 0,
	Close = 1,
}

public enum AmigaGuideError : uint
{
	NotEnoughMemory = 100,
	CantOpenDatabase = 101,
	CantFindNode = 102,
	CantOpenNode = 103,
	CantOpenWindow = 104,
	InvalidCommand = 105,
	CantComplete = 106,
	PortClosed = 107,
	CantCreatePort = 108,
	KeywordNotFound = 113,
}
