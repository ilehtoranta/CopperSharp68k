/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace Amiga;

public enum LocaleMeasuringSystem : byte
{
	Iso = 0,
	American = 1,
	Imperial = 2,
	British = 3,
}

public enum LocaleCalendarType : byte
{
	Sunday = 0,
	Monday = 1,
	Tuesday = 2,
	Wednesday = 3,
	Thursday = 4,
	Friday = 5,
	Saturday = 6,
}

public enum LocaleComparisonType : int
{
	Ascii = 0,
	Collate1 = 1,
	Collate2 = 2,
}

public enum LocaleSpaceSeparator : byte
{
	NoSpace = 0,
	Space = 1,
}

public enum LocaleSignPosition : byte
{
	Parentheses = 0,
	PrecedeAll = 1,
	SucceedAll = 2,
	PrecedeCurrency = 3,
	SucceedCurrency = 4,
}

public enum LocaleCurrencyPosition : byte
{
	Precedes = 0,
	Succeeds = 1,
}

public enum LocaleStringId : int
{
	Day1 = 1,
	Day7 = 7,
	AbbreviatedDay1 = 8,
	Month1 = 15,
	Month12 = 26,
	Yes = 39,
	No = 40,
	Am = 41,
	Pm = 42,
	Max = 51,
}
