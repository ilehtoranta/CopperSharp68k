/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

[System.Flags]
public enum ConductorFlags : ushort
{
	External = 1,
	GotTick = 2,
	MetronomeSet = 4,
	Private = 8,
}

public enum ConductorState : int
{
	Metric = -1,
	Shuttle = -2,
	LocateSet = -3,
	Stopped = 0,
	Paused = 1,
	Locate = 2,
	Running = 3,
}

[System.Flags]
public enum PlayerFlags : ushort
{
	Ready = 1,
	AlarmSet = 2,
	Quiet = 4,
	Conducted = 8,
	ExternalSync = 16,
}

public enum PlayerMethod : uint
{
	Tick = 0,
	State = 1,
	Position = 2,
	Shuttle = 3,
}

public enum RealtimeConstants : uint
{
	TickFrequency = 1200,
	ConductorsLock = 0,
	NoMemory = 801,
	NoConductor = 802,
	NoTimer = 803,
	Playing = 804,
}
