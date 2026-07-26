/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MathIeeeDoubBas
{
	public const string Name = "mathieeedoubbas.library";

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IEEEDPFix([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPFlt([M68kRegister(M68kRegister.D0)] int value);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IEEEDPCmp(
		[M68kRegister(M68kRegister.D0)] ulong left,
		[M68kRegister(M68kRegister.D1)] ulong right);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int IEEEDPTst([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPAbs([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPNeg([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPAdd(
		[M68kRegister(M68kRegister.D0)] ulong left,
		[M68kRegister(M68kRegister.D1)] ulong right);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPSub(
		[M68kRegister(M68kRegister.D0)] ulong left,
		[M68kRegister(M68kRegister.D1)] ulong right);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPMul(
		[M68kRegister(M68kRegister.D0)] ulong left,
		[M68kRegister(M68kRegister.D1)] ulong right);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPDiv(
		[M68kRegister(M68kRegister.D0)] ulong left,
		[M68kRegister(M68kRegister.D1)] ulong right);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPFloor([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPCeil([M68kRegister(M68kRegister.D0)] ulong value);
}
