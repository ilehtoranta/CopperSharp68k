/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

/// <summary>Classic timer.device vectors used by the portable clock PAL.</summary>
[AmigaLibrary(Name, AmigaLibraryBasePolicy.Manual)]
public static class TimerDevice
{
	public const string Name = "timer.device";
	public const uint UnitEClock = 2;

	public static APTR TimerDeviceLibraryBase
	{
		get => throw new System.NotSupportedException(
			"TimerDeviceLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"TimerDeviceLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadEClock(
		[M68kRegister(M68kRegister.A0)] APTR value);
}
