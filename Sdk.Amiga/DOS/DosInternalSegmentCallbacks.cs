/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace Amiga;

/// <summary>
/// Native callback entry points used by DOS InternalLoadSeg and
/// InternalUnLoadSeg. The entry address travels in A3 and is not part of the
/// callback's published register contract.
/// </summary>
public static class DosInternalSegmentCallbacks
{
	[AmigaIndirectCall(M68kRegister.A3)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int Read(
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.D1)] BPTR readHandle,
		[M68kRegister(M68kRegister.A0)] APTR buffer,
		[M68kRegister(M68kRegister.D0)] uint length,
		[M68kRegister(M68kRegister.A6)] APTR dosBase);

	[AmigaIndirectCall(M68kRegister.A3)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR Allocate(
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.D0)] uint size,
		[M68kRegister(M68kRegister.D1)] uint flags,
		[M68kRegister(M68kRegister.A6)] APTR execBase);

	[AmigaIndirectCall(M68kRegister.A3)]
	public static extern void Free(
		[M68kRegister(M68kRegister.A3)] APTR entry,
		[M68kRegister(M68kRegister.A1)] APTR memory,
		[M68kRegister(M68kRegister.D0)] uint size,
		[M68kRegister(M68kRegister.A6)] APTR execBase);
}
