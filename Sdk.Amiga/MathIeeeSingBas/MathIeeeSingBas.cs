/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MathIeeeSingBas
{
	public const string Name = "mathieeesingbas.library";

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IEEESPFix([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPFlt([M68kRegister(M68kRegister.D0)] int value);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IEEESPCmp(
		[M68kRegister(M68kRegister.D0)] uint left,
		[M68kRegister(M68kRegister.D1)] uint right);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IEEESPTst([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPAbs([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPNeg([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPAdd(
		[M68kRegister(M68kRegister.D0)] uint left,
		[M68kRegister(M68kRegister.D1)] uint right);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPSub(
		[M68kRegister(M68kRegister.D0)] uint left,
		[M68kRegister(M68kRegister.D1)] uint right);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPMul(
		[M68kRegister(M68kRegister.D0)] uint left,
		[M68kRegister(M68kRegister.D1)] uint right);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPDiv(
		[M68kRegister(M68kRegister.D0)] uint left,
		[M68kRegister(M68kRegister.D1)] uint right);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPFloor([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEESPCeil([M68kRegister(M68kRegister.D0)] uint value);
}
