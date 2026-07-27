/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class MathIeeeDoubTrans
{
	public const string Name = "mathieeedoubtrans.library";

	public static APTR MathIeeeDoubTransLibraryBase
	{
		get => throw new System.NotSupportedException(
			"MathIeeeDoubTransLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"MathIeeeDoubTransLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPAtan([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPSin([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-42)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPCos([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPTan([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPSincos(
		[M68kRegister(M68kRegister.A0)] uint cosResult,
		[M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-60)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPSinh([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-66)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPCosh([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPTanh([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-78)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPExp([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-84)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPLog([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-90)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPPow(
		[M68kRegister(M68kRegister.D2)] ulong exponent,
		[M68kRegister(M68kRegister.D3)] ulong value);

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPSqrt([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-102)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern uint IEEEDPTieee([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-108)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPFieee([M68kRegister(M68kRegister.D0)] uint value);

	[AmigaLvo(-114)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPAsin([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-120)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPAcos([M68kRegister(M68kRegister.D0)] ulong value);

	[AmigaLvo(-126)]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern ulong IEEEDPLog10([M68kRegister(M68kRegister.D0)] ulong value);
}
