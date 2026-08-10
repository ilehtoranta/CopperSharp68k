/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum TimerUnit : uint
{
	MicroHz = 0,
	VBlank = 1,
	EClock = 2,
	WaitUntil = 3,
	WaitEClock = 4,
}

public enum TimerCommand : ushort
{
	AddRequest = 9,
	GetSystemTime = 10,
	SetSystemTime = 11,
	AddTime = 12,
	SubtractTime = 13,
}
