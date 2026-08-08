/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.InteropServices;

namespace Amiga;

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct LocaleBase
{
	public const uint Size = 38;

	public Library Library;
	public int SystemPatches;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public unsafe struct LocaleInfo
{
	public const uint Size = 168;

	public STRPTR LocaleName;
	public STRPTR LanguageName;
	public fixed uint PreferredLanguages[10];
	public uint Flags;
	public uint CodeSet;
	public uint CountryCode;
	public uint TelephoneCode;
	public int GmtOffset;
	public byte MeasuringSystem;
	public byte CalendarType;
	public fixed byte Reserved0[2];
	public STRPTR DateTimeFormat;
	public STRPTR DateFormat;
	public STRPTR TimeFormat;
	public STRPTR ShortDateTimeFormat;
	public STRPTR ShortDateFormat;
	public STRPTR ShortTimeFormat;
	public STRPTR DecimalPoint;
	public STRPTR GroupSeparator;
	public STRPTR FractionGroupSeparator;
	public APTR Grouping;
	public APTR FractionGrouping;
	public STRPTR MonetaryDecimalPoint;
	public STRPTR MonetaryGroupSeparator;
	public STRPTR MonetaryFractionGroupSeparator;
	public APTR MonetaryGrouping;
	public APTR MonetaryFractionGrouping;
	public byte MonetaryFractionDigits;
	public byte MonetaryInternationalFractionDigits;
	public fixed byte Reserved1[2];
	public STRPTR MonetaryCurrencySymbol;
	public STRPTR MonetarySmallCurrencySymbol;
	public STRPTR MonetaryInternationalCurrencySymbol;
	public STRPTR MonetaryPositiveSign;
	public byte MonetaryPositiveSpaceSeparator;
	public byte MonetaryPositiveSignPosition;
	public byte MonetaryPositiveCurrencyPosition;
	public byte Reserved2;
	public STRPTR MonetaryNegativeSign;
	public byte MonetaryNegativeSpaceSeparator;
	public byte MonetaryNegativeSignPosition;
	public byte MonetaryNegativeCurrencyPosition;
	public byte Reserved3;
}

[StructLayout(LayoutKind.Sequential, Pack = 2)]
public struct Catalog
{
	public const uint Size = 28;

	public Node Link;
	public ushort Padding;
	public STRPTR Language;
	public uint CodeSet;
	public ushort Version;
	public ushort Revision;
}
