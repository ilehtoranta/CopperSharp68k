/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Nonvolatile
{
	public const string Name = "nonvolatile.library";

	public static APTR NonvolatileLibraryBase
	{
		get => throw new System.NotSupportedException(
			"NonvolatileLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"NonvolatileLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetCopyNV(
		[M68kRegister(M68kRegister.A0)] CString appName,
		[M68kRegister(M68kRegister.A1)] CString itemName,
		[M68kRegister(M68kRegister.D1)] int killRequesters);

	[AmigaLvo(-36)]
	public static extern void FreeNVData(
		[M68kRegister(M68kRegister.A0)] uint data);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ushort StoreNV(
		[M68kRegister(M68kRegister.A0)] CString appName,
		[M68kRegister(M68kRegister.A1)] CString itemName,
		[M68kRegister(M68kRegister.A2)] uint data,
		[M68kRegister(M68kRegister.D0)] uint length,
		[M68kRegister(M68kRegister.D1)] int killRequesters);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int DeleteNV(
		[M68kRegister(M68kRegister.A0)] CString appName,
		[M68kRegister(M68kRegister.A1)] CString itemName,
		[M68kRegister(M68kRegister.D1)] int killRequesters);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetNVInfo(
		[M68kRegister(M68kRegister.D1)] int killRequesters);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetNVList(
		[M68kRegister(M68kRegister.A0)] CString appName,
		[M68kRegister(M68kRegister.D1)] int killRequesters);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int SetNVProtection(
		[M68kRegister(M68kRegister.A0)] CString appName,
		[M68kRegister(M68kRegister.A1)] CString itemName,
		[M68kRegister(M68kRegister.D2)] int mask,
		[M68kRegister(M68kRegister.D1)] int killRequesters);
}
