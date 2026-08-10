/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

/// <summary>Chipset availability for the complete custom-register maps.</summary>
public static class CustomRegisterCatalog
{
	public static AmigaChipsetSupport GetSupport(CustomReadRegister register)
	{
		if (register == CustomReadRegister.DeniseId || register >= CustomReadRegister.HorizontalTotal)
		{
			return AmigaChipsetSupport.EcsAndAga;
		}

		return AmigaChipsetSupport.All;
	}

	public static AmigaChipsetSupport GetSupport(CustomWriteRegister register)
	{
		if (register == CustomWriteRegister.BlitterControl0Low ||
			register == CustomWriteRegister.BlitterSizeVertical ||
			register == CustomWriteRegister.BlitterSizeHorizontal ||
			register == CustomWriteRegister.BitplaneControl3 ||
			register == CustomWriteRegister.DisplayWindowHigh ||
			register >= CustomWriteRegister.HorizontalTotal &&
			register <= CustomWriteRegister.HorizontalCenter)
		{
			return AmigaChipsetSupport.EcsAndAga;
		}

		if (register == CustomWriteRegister.Bitplane7PointerHigh ||
			register == CustomWriteRegister.Bitplane7PointerLow ||
			register == CustomWriteRegister.Bitplane8PointerHigh ||
			register == CustomWriteRegister.Bitplane8PointerLow ||
			register == CustomWriteRegister.BitplaneControl4 ||
			register == CustomWriteRegister.CollisionControl2 ||
			register == CustomWriteRegister.Bitplane7Data ||
			register == CustomWriteRegister.Bitplane8Data ||
			register == CustomWriteRegister.FetchMode)
		{
			return AmigaChipsetSupport.Aga;
		}

		return AmigaChipsetSupport.All;
	}

	public static AmigaChipsetSupport GetSupport(CustomPointerRegister register) =>
		register == CustomPointerRegister.Bitplane7 || register == CustomPointerRegister.Bitplane8
			? AmigaChipsetSupport.Aga
			: AmigaChipsetSupport.All;

	public static bool IsSupported(AmigaChipsetSupport support, AmigaChipset chipset) => chipset switch
	{
		AmigaChipset.Ocs => (support & AmigaChipsetSupport.Ocs) != 0,
		AmigaChipset.Ecs => (support & AmigaChipsetSupport.Ecs) != 0,
		AmigaChipset.Aga => (support & AmigaChipsetSupport.Aga) != 0,
		_ => false,
	};
}
