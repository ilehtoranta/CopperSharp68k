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

	[AmigaLvo(ExpansionLvo.AddConfigDev)]
	public static void AddConfigDev(
		[M68kRegister(M68kRegister.A0)] APTR configDev)
	{
	}

	[AmigaLvo(ExpansionLvo.AddBootNode)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int AddBootNode(
		[M68kRegister(M68kRegister.D0)] int bootPri,
		[M68kRegister(M68kRegister.D1)] uint flags,
		[M68kRegister(M68kRegister.A0)] APTR deviceNode,
		[M68kRegister(M68kRegister.A1)] APTR configDev)
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.AllocBoardMem)]
	public static void AllocBoardMem(
		[M68kRegister(M68kRegister.D0)] uint slotSpec)
	{
	}

	[AmigaLvo(ExpansionLvo.AllocConfigDev)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AllocConfigDev()
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.AllocExpansionMem)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint AllocExpansionMem(
		[M68kRegister(M68kRegister.D0)] uint numSlots,
		[M68kRegister(M68kRegister.D1)] uint slotAlign)
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.ConfigBoard)]
	public static void ConfigBoard(
		[M68kRegister(M68kRegister.A0)] APTR board,
		[M68kRegister(M68kRegister.A1)] APTR configDev)
	{
	}

	[AmigaLvo(ExpansionLvo.ConfigChain)]
	public static void ConfigChain(
		[M68kRegister(M68kRegister.A0)] APTR baseAddr)
	{
	}

	[AmigaLvo(ExpansionLvo.FindConfigDev)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint FindConfigDev(
		[M68kRegister(M68kRegister.A0)] APTR oldConfigDev,
		[M68kRegister(M68kRegister.D0)] int manufacturer,
		[M68kRegister(M68kRegister.D1)] int product)
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.FreeBoardMem)]
	public static void FreeBoardMem(
		[M68kRegister(M68kRegister.D0)] uint startSlot,
		[M68kRegister(M68kRegister.D1)] uint slotSpec)
	{
	}

	[AmigaLvo(ExpansionLvo.FreeConfigDev)]
	public static void FreeConfigDev(
		[M68kRegister(M68kRegister.A0)] APTR configDev)
	{
	}

	[AmigaLvo(ExpansionLvo.FreeExpansionMem)]
	public static void FreeExpansionMem(
		[M68kRegister(M68kRegister.D0)] uint startSlot,
		[M68kRegister(M68kRegister.D1)] uint numSlots)
	{
	}

	[AmigaLvo(ExpansionLvo.ReadExpansionByte)]
	[return: M68kRegister(M68kRegister.D0)]
	public static byte ReadExpansionByte(
		[M68kRegister(M68kRegister.A0)] APTR board,
		[M68kRegister(M68kRegister.D0)] uint offset)
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.ReadExpansionRom)]
	public static void ReadExpansionRom(
		[M68kRegister(M68kRegister.A0)] APTR board,
		[M68kRegister(M68kRegister.A1)] APTR configDev)
	{
	}

	[AmigaLvo(ExpansionLvo.RemConfigDev)]
	public static void RemConfigDev(
		[M68kRegister(M68kRegister.A0)] APTR configDev)
	{
	}

	[AmigaLvo(ExpansionLvo.WriteExpansionByte)]
	public static void WriteExpansionByte(
		[M68kRegister(M68kRegister.A0)] APTR board,
		[M68kRegister(M68kRegister.D0)] uint offset,
		[M68kRegister(M68kRegister.D1)] uint byteValue)
	{
	}

	[AmigaLvo(ExpansionLvo.ObtainConfigBinding)]
	public static void ObtainConfigBinding()
	{
	}

	[AmigaLvo(ExpansionLvo.ReleaseConfigBinding)]
	public static void ReleaseConfigBinding()
	{
	}

	[AmigaLvo(ExpansionLvo.SetCurrentBinding)]
	public static void SetCurrentBinding(
		[M68kRegister(M68kRegister.A0)] APTR currentBinding,
		[M68kRegister(M68kRegister.D0)] uint bindingSize)
	{
	}

	[AmigaLvo(ExpansionLvo.GetCurrentBinding)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint GetCurrentBinding(
		[M68kRegister(M68kRegister.A0)] APTR currentBinding,
		[M68kRegister(M68kRegister.D0)] uint bindingSize)
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.MakeDosNode)]
	[return: M68kRegister(M68kRegister.D0)]
	public static uint MakeDosNode(
		[M68kRegister(M68kRegister.A0)] APTR parameterPacket)
	{
		return 0;
	}

	[AmigaLvo(ExpansionLvo.AddDosNode)]
	[return: M68kRegister(M68kRegister.D0)]
	public static int AddDosNode(
		[M68kRegister(M68kRegister.D0)] int bootPri,
		[M68kRegister(M68kRegister.D1)] uint flags,
		[M68kRegister(M68kRegister.A0)] APTR deviceNode)
	{
		return 0;
	}
}
