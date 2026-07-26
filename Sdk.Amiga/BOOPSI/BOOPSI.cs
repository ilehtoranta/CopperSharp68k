/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace Amiga;

public static class BOOPSI
{
	[M68kImport("amiga.boopsi.DoMethodA")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DoMethodA(
		[M68kRegister(M68kRegister.A0)] uint obj,
		[M68kRegister(M68kRegister.A1)] uint message);

	[M68kImport("amiga.boopsi.DoSuperMethodA")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint DoSuperMethodA(
		[M68kRegister(M68kRegister.A0)] uint cl,
		[M68kRegister(M68kRegister.A1)] uint obj,
		[M68kRegister(M68kRegister.A2)] uint message);

	[M68kImport("amiga.boopsi.CoerceMethodA")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint CoerceMethodA(
		[M68kRegister(M68kRegister.A0)] uint cl,
		[M68kRegister(M68kRegister.A1)] uint obj,
		[M68kRegister(M68kRegister.A2)] uint message);
}
