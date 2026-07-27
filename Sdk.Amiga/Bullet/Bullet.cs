/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Bullet
{
	public const string Name = "bullet.library";

	public static APTR BulletLibraryBase
	{
		get => throw new System.NotSupportedException(
			"BulletLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"BulletLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint OpenEngine();

	[AmigaLvo(-36)]
	public static extern void CloseEngine(
		[M68kRegister(M68kRegister.A0)] uint glyphEngine);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint SetInfoA(
		[M68kRegister(M68kRegister.A0)] uint glyphEngine,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ObtainInfoA(
		[M68kRegister(M68kRegister.A0)] uint glyphEngine,
		[M68kRegister(M68kRegister.A1)] uint tags);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint ReleaseInfoA(
		[M68kRegister(M68kRegister.A0)] uint glyphEngine,
		[M68kRegister(M68kRegister.A1)] uint tags);

	public static uint SetInfo(uint glyphEngine, uint tags) =>
		SetInfoA(glyphEngine, tags);

	public static uint ObtainInfo(uint glyphEngine, uint tags) =>
		ObtainInfoA(glyphEngine, tags);

	public static uint ReleaseInfo(uint glyphEngine, uint tags) =>
		ReleaseInfoA(glyphEngine, tags);
}
