/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

/// <summary>Absolute m68k entry used by dos.library RunCommand.</summary>
public static class DosRunCommandCallbacks
{
	[AmigaIndirectCall(M68kRegister.A3)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Execute(
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.D0)] uint argumentLength,
		[M68kRegister(M68kRegister.A0)] APTR arguments);
}
