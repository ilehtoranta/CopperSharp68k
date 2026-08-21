/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

/// <summary>Published m68k Hook callback used by dos.library ExAll.</summary>
public static class DosExAllCallbacks
{
	[AmigaIndirectCall(M68kRegister.A3)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Match(
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.A0)] APTR hook,
		[M68kRegister(M68kRegister.A1)] APTR exAllData,
		[M68kRegister(M68kRegister.A2)] APTR type);
}
