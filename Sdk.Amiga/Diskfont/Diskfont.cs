/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Diskfont
{
	public const string Name = "diskfont.library";

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenDiskFont(
		[M68kRegister(M68kRegister.A0)] uint textAttr);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int AvailFonts(
		[M68kRegister(M68kRegister.A0)] uint buffer,
		[M68kRegister(M68kRegister.D0)] int bufferBytes,
		[M68kRegister(M68kRegister.D1)] int flags);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewFontContents(
		[M68kRegister(M68kRegister.A0)] uint fontsLock,
		[M68kRegister(M68kRegister.A1)] CString fontName);

	[AmigaLvo(-48)]
	public static extern void DisposeFontContents(
		[M68kRegister(M68kRegister.A1)] uint fontContentsHeader);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint NewScaledDiskFont(
		[M68kRegister(M68kRegister.A0)] uint sourceFont,
		[M68kRegister(M68kRegister.A1)] uint destTextAttr);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int GetDiskFontCtrl(
		[M68kRegister(M68kRegister.D0)] int tagId);

	[AmigaLvo(-66)]
	public static extern void SetDiskFontCtrlA(
		[M68kRegister(M68kRegister.A0)] uint tags);

	public static void SetDiskFontCtrl(uint tags) =>
		SetDiskFontCtrlA(tags);
}
