/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class LowLevel
{
	public const string Name = "lowlevel.library";

	public static APTR LowLevelLibraryBase
	{
		get => throw new System.NotSupportedException(
			"LowLevelLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"LowLevelLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReadJoyPort(
		[M68kRegister(M68kRegister.D0)] uint port);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte GetLanguageSelection();

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetKey();

	[AmigaLvo(-54)]
	public static extern void QueryKeys(
		[M68kRegister(M68kRegister.A0), M68kWritesBuffer] uint queryArray,
		[M68kRegister(M68kRegister.D1)] uint arraySize);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddKBInt(
		[M68kRegister(M68kRegister.A0)] uint intRoutine,
		[M68kRegister(M68kRegister.A1)] uint intData);

	[AmigaLvo(-66)]
	public static extern void RemKBInt(
		[M68kRegister(M68kRegister.A1)] uint intHandle);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SystemControlA(
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddTimerInt(
		[M68kRegister(M68kRegister.A0)] uint intRoutine,
		[M68kRegister(M68kRegister.A1)] uint intData);

	[AmigaLvo(-84)]
	public static extern void RemTimerInt(
		[M68kRegister(M68kRegister.A1)] uint intHandle);

	[AmigaLvo(-90)]
	public static extern void StopTimerInt(
		[M68kRegister(M68kRegister.A1)] uint intHandle);

	[AmigaLvo(-96)]
	public static extern void StartTimerInt(
		[M68kRegister(M68kRegister.A1)] uint intHandle,
		[M68kRegister(M68kRegister.D0)] uint interval,
		[M68kRegister(M68kRegister.D1)] int continuous);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ElapsedTime(
		[M68kRegister(M68kRegister.A0)] uint context);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AddVBlankInt(
		[M68kRegister(M68kRegister.A0)] uint intRoutine,
		[M68kRegister(M68kRegister.A1)] uint intData);

	[AmigaLvo(-114)]
	public static extern void RemVBlankInt(
		[M68kRegister(M68kRegister.A1)] uint intHandle);

	// MorphOS m68k ABI call.
	[AmigaLvo(-132)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetJoyPortAttrsA(
		[M68kRegister(M68kRegister.D0)] uint port,
		[M68kRegister(M68kRegister.A1)] uint tags);

	public static uint SystemControl(uint tags) =>
		SystemControlA(tags);

	public static int SetJoyPortAttrs(uint port, uint tags) =>
		SetJoyPortAttrsA(port, tags);
}
