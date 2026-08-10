/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga.Hardware;

[Flags]
public enum AmigaHardwareFeatures : uint
{
	None = 0,
	CustomChips = 1 << 0,
	CiaA = 1 << 1,
	CiaB = 1 << 2,
	Rtc2000 = 1 << 3,
	Rtc3000 = 1 << 4,
	Gayle = 1 << 5,
	Pcmcia = 1 << 6,
	IdeA600A1200 = 1 << 7,
	IdeA4000 = 1 << 8,
	A3000ScsiDmac = 1 << 9,
	A4000TScsi = 1 << 10,
	FatGary = 1 << 11,
	Ramsey = 1 << 12,
	Buster = 1 << 13,
	Amber = 1 << 14,
	CdtvDmac = 1 << 15,
	Akiko = 1 << 16,
	ZorroII = 1 << 17,
	ZorroIII = 1 << 18,
}

public static class AmigaHardware
{
	private const AmigaHardwareFeatures Common =
		AmigaHardwareFeatures.CustomChips | AmigaHardwareFeatures.CiaA |
		AmigaHardwareFeatures.CiaB;

	public static AmigaChipset GetDefaultChipset(AmigaModel model) => model switch
	{
		AmigaModel.Amiga500Plus or AmigaModel.Amiga600 or AmigaModel.Amiga3000 or
			AmigaModel.Amiga3000T or AmigaModel.CdTv => AmigaChipset.Ecs,
		AmigaModel.Amiga1200 or AmigaModel.Amiga4000 or AmigaModel.Amiga4000T or
			AmigaModel.Cd32 => AmigaChipset.Aga,
		_ => AmigaChipset.Ocs,
	};

	/// <summary>Returns built-in motherboard features, excluding optional expansion cards and chip upgrades.</summary>
	public static AmigaHardwareFeatures GetFeatures(AmigaModel model) => model switch
	{
		AmigaModel.Amiga1000 or AmigaModel.Amiga500 => Common,
		AmigaModel.Amiga2000 or AmigaModel.Amiga2500 =>
			Common | AmigaHardwareFeatures.Rtc2000 | AmigaHardwareFeatures.ZorroII,
		AmigaModel.CdTv => Common | AmigaHardwareFeatures.CdtvDmac | AmigaHardwareFeatures.Rtc2000,
		AmigaModel.Amiga500Plus => Common | AmigaHardwareFeatures.Rtc2000,
		AmigaModel.Amiga600 => Common | AmigaHardwareFeatures.Gayle |
			AmigaHardwareFeatures.Pcmcia | AmigaHardwareFeatures.IdeA600A1200,
		AmigaModel.Amiga3000 or AmigaModel.Amiga3000T => Common |
			AmigaHardwareFeatures.Rtc3000 | AmigaHardwareFeatures.A3000ScsiDmac |
			AmigaHardwareFeatures.FatGary | AmigaHardwareFeatures.Ramsey |
			AmigaHardwareFeatures.Buster | AmigaHardwareFeatures.Amber |
			AmigaHardwareFeatures.ZorroII | AmigaHardwareFeatures.ZorroIII,
		AmigaModel.Amiga1200 => Common | AmigaHardwareFeatures.Gayle |
			AmigaHardwareFeatures.Pcmcia | AmigaHardwareFeatures.IdeA600A1200,
		AmigaModel.Amiga4000 => Common | AmigaHardwareFeatures.Rtc3000 |
			AmigaHardwareFeatures.IdeA4000 | AmigaHardwareFeatures.FatGary |
			AmigaHardwareFeatures.Ramsey | AmigaHardwareFeatures.Buster |
			AmigaHardwareFeatures.ZorroII | AmigaHardwareFeatures.ZorroIII,
		AmigaModel.Amiga4000T => Common | AmigaHardwareFeatures.Rtc3000 |
			AmigaHardwareFeatures.IdeA4000 | AmigaHardwareFeatures.A4000TScsi |
			AmigaHardwareFeatures.FatGary | AmigaHardwareFeatures.Ramsey |
			AmigaHardwareFeatures.Buster | AmigaHardwareFeatures.ZorroII |
			AmigaHardwareFeatures.ZorroIII,
		AmigaModel.Cd32 => Common | AmigaHardwareFeatures.Akiko,
		_ => Common,
	};
}
