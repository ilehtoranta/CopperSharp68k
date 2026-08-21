/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

/// <summary>
/// Classic and MorphOS m68k Process pr_ExitCode callback. The callback receives
/// the process result in D0 and pr_ExitData in D1 and may replace the result.
/// </summary>
public static class DosProcessExitCallbacks
{
	[AmigaIndirectCall(M68kRegister.A3)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Execute(
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.D0)] int returnCode,
		[M68kRegister(M68kRegister.D1)] int exitData);
}
