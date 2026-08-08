/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum ExpansionRomType : byte
{
	NewBoard = 0xc0,
	ZorroII = 0xc0,
	ZorroIII = 0x80,
	MemoryList = 1 << 5,
	DiagnosticValid = 1 << 4,
	ChainedConfig = 1 << 3,
}

[System.Flags]
public enum ExpansionRomFlags : byte
{
	MemorySpace = 1 << 7,
	NoShutUp = 1 << 6,
	Extended = 1 << 5,
	ZorroIII = 1 << 4,
}

[System.Flags]
public enum ExpansionControlInterrupt : byte
{
	Enable = 1 << 1,
	Reset = 1 << 3,
	Int2Pending = 1 << 4,
	Int6Pending = 1 << 5,
	Int7Pending = 1 << 6,
	Interrupting = 1 << 7,
}

public enum ExpansionConstants : uint
{
	ExpansionBase = 0x00e80000,
	ZorroIIIExpansionBase = 0xff000000,
	ExpansionSize = 0x00080000,
	ExpansionSlots = 8,
	MemoryBase = 0x00200000,
	MemorySize = 0x00800000,
	MemorySlots = 128,
	ZorroIIIConfigArea = 0x40000000,
	ZorroIIIConfigAreaEnd = 0x7fffffff,
	ZorroIIISizeGranularity = 0x00080000,
}

public enum DiagnosticBusWidth : byte
{
	NibbleWide = 0x00,
	ByteWide = 0x40,
	WordWide = 0x80,
}

public enum DiagnosticBootTime : byte
{
	Never = 0x00,
	ConfigTime = 0x10,
	BindTime = 0x20,
}
