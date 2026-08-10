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
	public const short Open = -6;
	public const short Close = -12;
	public const short Expunge = -18;
	public const short ExtFunc = -24;
	public const short BeginIO = -30;
	public const short AbortIO = -36;
	public const short ReadEClockLvo = -60;
	public const uint UnitEClock = (uint)TimerUnit.EClock;

	[AmigaLvo(ReadEClockLvo)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadEClock(
		[M68kRegister(M68kRegister.A6)] APTR deviceBase,
		[M68kRegister(M68kRegister.A0)] APTR value);
}
