/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>ADKCON/ADKCONR audio modulation, disk, and UART control bits.</summary>
[Flags]
public enum AudioDiskControlFlags : ushort
{
	None = 0,
	Audio0VolumeModulates1 = 1 << 0,
	Audio1VolumeModulates2 = 1 << 1,
	Audio2VolumeModulates3 = 1 << 2,
	Audio3VolumeModulates0 = 1 << 3,
	Audio0PeriodModulates1 = 1 << 4,
	Audio1PeriodModulates2 = 1 << 5,
	Audio2PeriodModulates3 = 1 << 6,
	Audio3PeriodModulates0 = 1 << 7,
	FastDisk = 1 << 8,
	MostSignificantBitSync = 1 << 9,
	WordSync = 1 << 10,
	UartBreak = 1 << 11,
	MfmPrecompensation = 1 << 12,
	Precompensation0 = 1 << 13,
	Precompensation1 = 1 << 14,
	All = 0x7FFF,
}
