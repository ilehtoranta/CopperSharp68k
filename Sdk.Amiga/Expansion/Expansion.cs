/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler;

namespace Amiga;

[AmigaLibrary(Name)]
public static class Expansion
{
	public const string Name = "expansion.library";

	public static APTR ExpansionLibraryBase
	{
		get => throw new System.NotSupportedException(
			"ExpansionLibraryBase is lowered by CopperSharp.");
		set => throw new System.NotSupportedException(
			"ExpansionLibraryBase is lowered by CopperSharp.");
	}

	[AmigaLvo(-30)]
	public static void AddConfigDev(
		[M68kRegister(M68kRegister.A0)] uint configDev)
	{
	}

	[AmigaLvo(-36)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int AddBootNode(
		[M68kRegister(M68kRegister.D0)] int bootPri,
		[M68kRegister(M68kRegister.D1)] uint flags,
		[M68kRegister(M68kRegister.A0)] uint deviceNode,
		[M68kRegister(M68kRegister.A1)] uint configDev)
	{
		return 0;
	}

	[AmigaLvo(-42)]
	public static void AllocBoardMem(
		[M68kRegister(M68kRegister.D0)] uint slotSpec)
	{
	}

	[AmigaLvo(-48)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AllocConfigDev()
	{
		return 0;
	}

	[AmigaLvo(-54)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AllocExpansionMem(
		[M68kRegister(M68kRegister.D0)] uint numSlots,
		[M68kRegister(M68kRegister.D1)] uint slotAlign)
	{
		return 0;
	}

	[AmigaLvo(-60)]
	public static void ConfigBoard(
		[M68kRegister(M68kRegister.A0)] uint board,
		[M68kRegister(M68kRegister.A1)] uint configDev)
	{
	}

	[AmigaLvo(-66)]
	public static void ConfigChain(
		[M68kRegister(M68kRegister.A0)] uint baseAddr)
	{
	}

	[AmigaLvo(-72)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindConfigDev(
		[M68kRegister(M68kRegister.A0)] uint oldConfigDev,
		[M68kRegister(M68kRegister.D0)] int manufacturer,
		[M68kRegister(M68kRegister.D1)] int product)
	{
		return 0;
	}

	[AmigaLvo(-78)]
	public static void FreeBoardMem(
		[M68kRegister(M68kRegister.D0)] uint startSlot,
		[M68kRegister(M68kRegister.D1)] uint slotSpec)
	{
	}

	[AmigaLvo(-84)]
	public static void FreeConfigDev(
		[M68kRegister(M68kRegister.A0)] uint configDev)
	{
	}

	[AmigaLvo(-90)]
	public static void FreeExpansionMem(
		[M68kRegister(M68kRegister.D0)] uint startSlot,
		[M68kRegister(M68kRegister.D1)] uint numSlots)
	{
	}

	[AmigaLvo(-96)]
	[return: M68kRegister(M68kRegister.D0)]
	public static byte ReadExpansionByte(
		[M68kRegister(M68kRegister.A0)] uint board,
		[M68kRegister(M68kRegister.D0)] uint offset)
	{
		return 0;
	}

	[AmigaLvo(-102)]
	public static void ReadExpansionRom(
		[M68kRegister(M68kRegister.A0)] uint board,
		[M68kRegister(M68kRegister.A1)] uint configDev)
	{
	}

	[AmigaLvo(-108)]
	public static void RemConfigDev(
		[M68kRegister(M68kRegister.A0)] uint configDev)
	{
	}

	[AmigaLvo(-114)]
	public static void WriteExpansionByte(
		[M68kRegister(M68kRegister.A0)] uint board,
		[M68kRegister(M68kRegister.D0)] uint offset,
		[M68kRegister(M68kRegister.D1)] uint byteValue)
	{
	}

	[AmigaLvo(-120)]
	public static void ObtainConfigBinding()
	{
	}

	[AmigaLvo(-126)]
	public static void ReleaseConfigBinding()
	{
	}

	[AmigaLvo(-132)]
	public static void SetCurrentBinding(
		[M68kRegister(M68kRegister.A0)] uint currentBinding,
		[M68kRegister(M68kRegister.D0)] uint bindingSize)
	{
	}

	[AmigaLvo(-138)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetCurrentBinding(
		[M68kRegister(M68kRegister.A0)] uint currentBinding,
		[M68kRegister(M68kRegister.D0)] uint bindingSize)
	{
		return 0;
	}

	[AmigaLvo(-144)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint MakeDosNode(
		[M68kRegister(M68kRegister.A0)] uint parameterPacket)
	{
		return 0;
	}

	[AmigaLvo(-150)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int AddDosNode(
		[M68kRegister(M68kRegister.D0)] int bootPri,
		[M68kRegister(M68kRegister.D1)] uint flags,
		[M68kRegister(M68kRegister.A0)] uint deviceNode)
	{
		return 0;
	}
}
