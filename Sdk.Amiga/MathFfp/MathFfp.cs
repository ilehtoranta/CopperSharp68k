/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MathFfp
{
	public const string Name = "mathffp.library";

	public static APTR MathFfpLibraryBase
	{
		get => throw new System.NotSupportedException(
			"MathFfpLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"MathFfpLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SPFix([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPFlt([M68kRegister(M68kRegister.D0)] int value);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SPCmp(
		[M68kRegister(M68kRegister.D1)] uint left,
		[M68kRegister(M68kRegister.D0)] uint right);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SPTst([M68kRegister(M68kRegister.D1)] uint value);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPAbs([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPNeg([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPAdd(
		[M68kRegister(M68kRegister.D1)] uint left,
		[M68kRegister(M68kRegister.D0)] uint right);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPSub(
		[M68kRegister(M68kRegister.D1)] uint left,
		[M68kRegister(M68kRegister.D0)] uint right);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPMul(
		[M68kRegister(M68kRegister.D1)] uint left,
		[M68kRegister(M68kRegister.D0)] uint right);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPDiv(
		[M68kRegister(M68kRegister.D1)] uint left,
		[M68kRegister(M68kRegister.D0)] uint right);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPFloor([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPCeil([M68kRegister(M68kRegister.D0)] uint value);
}
