/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>INTENA/INTENAR and INTREQ/INTREQR bits shared by OCS and ECS.</summary>
[Flags]
public enum CustomInterruptFlags : ushort
{
	None = 0,
	TransmitBufferEmpty = 1 << 0,
	DiskBlockFinished = 1 << 1,
	Software = 1 << 2,
	Ports = 1 << 3,
	Copper = 1 << 4,
	VerticalBlank = 1 << 5,
	BlitterFinished = 1 << 6,
	Audio0 = 1 << 7,
	Audio1 = 1 << 8,
	Audio2 = 1 << 9,
	Audio3 = 1 << 10,
	ReceiveBufferFull = 1 << 11,
	DiskSync = 1 << 12,
	External = 1 << 13,
	Master = 1 << 14,
	All = 0x7FFF,
}
