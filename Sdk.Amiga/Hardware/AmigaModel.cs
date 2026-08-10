/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>Commodore Amiga systems covered by the hardware register catalog.</summary>
public enum AmigaModel : byte
{
	Amiga1000,
	Amiga500,
	Amiga2000,
	Amiga2500,
	CdTv,
	Amiga3000,
	Amiga3000T,
	Amiga500Plus,
	Amiga600,
	Amiga1200,
	Amiga4000,
	Amiga4000T,
	Cd32,
}

/// <summary>Custom-chip register generation. Individual machines can contain upgraded chips.</summary>
public enum AmigaChipset : byte
{
	Ocs,
	Ecs,
	Aga,
}

[Flags]
public enum AmigaChipsetSupport : byte
{
	None = 0,
	Ocs = 1 << 0,
	Ecs = 1 << 1,
	Aga = 1 << 2,
	All = Ocs | Ecs | Aga,
	EcsAndAga = Ecs | Aga,
}
