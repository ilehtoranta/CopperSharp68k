/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MathIeeeSingTrans
{
	public const string Name = "mathieeesingtrans.library";

	public static APTR MathIeeeSingTransLibraryBase
	{
		get => throw new System.NotSupportedException(
			"MathIeeeSingTransLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"MathIeeeSingTransLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPAtan([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPSin([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPCos([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPTan([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPSincos(
		[M68kRegister(M68kRegister.A0)] uint cosResult,
		[M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPSinh([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPCosh([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPTanh([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPExp([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPLog([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPPow(
		[M68kRegister(M68kRegister.D1)] uint exponent,
		[M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPSqrt([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPTieee([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPFieee([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPAsin([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPAcos([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPLog10([M68kRegister(M68kRegister.D0)] uint value);
}
