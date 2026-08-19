/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class ASL
{
	public const string Name = "asl.library";

	public static APTR ASLLibraryBase
	{
		get => throw new System.NotSupportedException(
			"ASLLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"ASLLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocFileRequest();

	[AmigaLvo(-36)]
	public static extern void FreeFileRequest(
		[M68kRegister(M68kRegister.A0)] APTR fileRequest);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RequestFile(
		[M68kRegister(M68kRegister.A0)] APTR fileRequest);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern APTR AllocAslRequest(
		[M68kRegister(M68kRegister.D0)] uint reqType,
		[M68kRegister(M68kRegister.A0)] APTR tags);

	[AmigaLvo(-54)]
	public static extern void FreeAslRequest(
		[M68kRegister(M68kRegister.A0)] APTR requester);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AslRequest(
		[M68kRegister(M68kRegister.A0)] APTR requester,
		[M68kRegister(M68kRegister.A1)] APTR tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-78)]
	public static extern void AbortAslRequest(
		[M68kRegister(M68kRegister.A0)] APTR requester);

	// MorphOS m68k ABI call.
	[AmigaLvo(-84)]
	public static extern void ActivateAslRequest(
		[M68kRegister(M68kRegister.A0)] APTR requester);

	public static APTR AllocAslRequestTags(uint reqType, APTR tags) =>
		AllocAslRequest(reqType, tags);

	public static int AslRequestTags(APTR requester, APTR tags) =>
		AslRequest(requester, tags);
}
