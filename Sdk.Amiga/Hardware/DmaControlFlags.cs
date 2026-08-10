/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>DMACON/DMACONR channel and status bits shared by OCS and ECS.</summary>
[Flags]
public enum DmaControlFlags : ushort
{
	None = 0,
	Audio0 = 1 << 0,
	Audio1 = 1 << 1,
	Audio2 = 1 << 2,
	Audio3 = 1 << 3,
	Disk = 1 << 4,
	Sprite = 1 << 5,
	Blitter = 1 << 6,
	Copper = 1 << 7,
	Raster = 1 << 8,
	Master = 1 << 9,
	BlitterPriority = 1 << 10,
	BlitterZero = 1 << 13,
	BlitterBusy = 1 << 14,
	WritableMask = Audio0 | Audio1 | Audio2 | Audio3 | Disk | Sprite |
		Blitter | Copper | Raster | Master | BlitterPriority,
}
