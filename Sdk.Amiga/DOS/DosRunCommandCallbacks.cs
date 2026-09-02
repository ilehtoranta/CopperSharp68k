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

	/// <summary>
	/// Switches stacks through Exec StackSwap, calls a command with the original
	/// D0/A0 startup ABI, and restores the caller's stack after a normal RTS.
	/// </summary>
	/// <remarks>
	/// Requires Exec V37 or later and a caller-owned, initialized StackSwapStruct.
	/// Its new stack pointer must be word-aligned and have writable space for a
	/// control longword, return addresses, and the called routines' stack usage.
	/// The command may change every register except SP. The bridge preserves the
	/// caller's other registers and returns the command's D0 value. Two StackSwap
	/// calls restore the descriptor and task stack bounds. No memory is allocated,
	/// no argument stream is installed, and Exit or other nonlocal exits are not
	/// handled. The descriptor and argument storage must remain live throughout.
	/// </remarks>
	[M68kImport("intrinsic:amiga-run-command-on-stack")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ExecuteOnStack(
		[M68kRegister(M68kRegister.A0)] APTR stackSwap,
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.D0)] uint argumentLength,
		[M68kRegister(M68kRegister.A1)] APTR arguments);
}
