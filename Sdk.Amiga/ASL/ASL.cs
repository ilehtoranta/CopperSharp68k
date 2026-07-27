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
	public static extern uint AllocFileRequest();

	[AmigaLvo(-36)]
	public static extern void FreeFileRequest(
		[M68kRegister(M68kRegister.A0)] uint fileRequest);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int RequestFile(
		[M68kRegister(M68kRegister.A0)] uint fileRequest);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AllocAslRequest(
		[M68kRegister(M68kRegister.D0)] uint reqType,
		[M68kRegister(M68kRegister.A0)] uint tags);

	[AmigaLvo(-54)]
	public static extern void FreeAslRequest(
		[M68kRegister(M68kRegister.A0)] uint requester);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AslRequest(
		[M68kRegister(M68kRegister.A0)] uint requester,
		[M68kRegister(M68kRegister.A1)] uint tags);

	// MorphOS m68k ABI call.
	[AmigaLvo(-78)]
	public static extern void AbortAslRequest(
		[M68kRegister(M68kRegister.A0)] uint requester);

	// MorphOS m68k ABI call.
	[AmigaLvo(-84)]
	public static extern void ActivateAslRequest(
		[M68kRegister(M68kRegister.A0)] uint requester);

	public static uint AllocAslRequestTags(uint reqType, uint tags) =>
		AllocAslRequest(reqType, tags);

	public static int AslRequestTags(uint requester, uint tags) =>
		AslRequest(requester, tags);
}
