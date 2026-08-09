/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

/// <summary>Classic timer.device vectors used by the portable clock PAL.</summary>
[AmigaLibrary(Name, AmigaLibraryBasePolicy.CallerProvided)]
public static class TimerDevice
{
	public const string Name = "timer.device";
	public const uint UnitEClock = 2;

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadEClock(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR value);
}
