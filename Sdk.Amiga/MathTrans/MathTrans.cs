/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MathTrans
{
	public const string Name = "mathtrans.library";

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPAtan([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPSin([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPCos([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPTan([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPSincos(
		[M68kRegister(M68kRegister.D1)] uint cosResult,
		[M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPSinh([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPCosh([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPTanh([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPExp([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPLog([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPPow(
		[M68kRegister(M68kRegister.D1)] uint exponent,
		[M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPSqrt([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPTieee([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPFieee([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPAsin([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPAcos([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SPLog10([M68kRegister(M68kRegister.D0)] uint value);
}
