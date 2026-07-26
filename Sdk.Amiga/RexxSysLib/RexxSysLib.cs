/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class RexxSysLib
{
	public const string Name = "rexxsyslib.library";

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint CreateArgstring(
		[M68kRegister(M68kRegister.A0)] CString stringPtr,
		[M68kRegister(M68kRegister.D0)] uint length)
	{
		return 0;
	}

	[AmigaLvo(-132)]
	public static void DeleteArgstring(
		[M68kRegister(M68kRegister.A0)] uint argString)
	{
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint LengthArgstring(
		[M68kRegister(M68kRegister.A0)] uint argString)
	{
		return 0;
	}

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint CreateRexxMsg(
		[M68kRegister(M68kRegister.A0)] uint port,
		[M68kRegister(M68kRegister.A1)] CString extension,
		[M68kRegister(M68kRegister.D0)] uint host)
	{
		return 0;
	}

	[AmigaLvo(-150)]
	public static void DeleteRexxMsg(
		[M68kRegister(M68kRegister.A0)] uint message)
	{
	}

	[AmigaLvo(-156)]
	public static void ClearRexxMsg(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] uint count)
	{
	}

	[AmigaLvo(-162)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int FillRexxMsg(
		[M68kRegister(M68kRegister.A0)] uint message,
		[M68kRegister(M68kRegister.D0)] uint count,
		[M68kRegister(M68kRegister.D1)] uint mask)
	{
		return 0;
	}

	[AmigaLvo(-168)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int IsRexxMsg(
		[M68kRegister(M68kRegister.A0)] uint message)
	{
		return 0;
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-450)]
	public static void LockRexxBase(
		[M68kRegister(M68kRegister.D0)] uint resource)
	{
	}

	// MorphOS m68k ABI extension.
	[AmigaLvo(-456)]
	public static void UnlockRexxBase(
		[M68kRegister(M68kRegister.D0)] uint resource)
	{
	}
}
