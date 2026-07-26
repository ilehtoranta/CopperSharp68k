/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Keymap
{
	public const string Name = "keymap.library";

	[AmigaLvo(-30)]
	public static extern void SetKeyMapDefault(
		[M68kRegister(M68kRegister.A0)] uint keyMap);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint AskKeyMapDefault();

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short MapRawKey(
		[M68kRegister(M68kRegister.A0)] uint inputEvent,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A2)] uint keyMap);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MapANSI(
		[M68kRegister(M68kRegister.A0)] CString ansiString,
		[M68kRegister(M68kRegister.D0)] int count,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A2)] uint keyMap);

	// MorphOS m68k ABI call.
	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern short MapRawKeyUCS4(
		[M68kRegister(M68kRegister.A0)] uint inputEvent,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A2)] uint keyMap);

	// MorphOS m68k ABI call.
	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int MapUCS4(
		[M68kRegister(M68kRegister.A0)] uint ucs4String,
		[M68kRegister(M68kRegister.D0)] int count,
		[M68kRegister(M68kRegister.A1)] uint buffer,
		[M68kRegister(M68kRegister.D1)] int length,
		[M68kRegister(M68kRegister.A2)] uint keyMap);

	// MorphOS m68k ABI call.
	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern byte ToANSI(
		[M68kRegister(M68kRegister.A0)] uint ucs4Char,
		[M68kRegister(M68kRegister.A1)] uint keyMap);

	// MorphOS m68k ABI call.
	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ToUCS4(
		[M68kRegister(M68kRegister.A0)] byte ansiChar,
		[M68kRegister(M68kRegister.A1)] uint keyMap);

	// MorphOS m68k ABI call.
	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint GetKeyMapCodepage(
		[M68kRegister(M68kRegister.A0)] uint keyMap);
}
