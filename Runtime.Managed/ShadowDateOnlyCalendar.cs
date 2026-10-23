/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Gregorian DateOnly operations over CoreLib's zero-based day-number value.
/// The compiler binds only the exact calendar members represented here.
/// </summary>
internal static class ShadowDateOnlyCalendar
{
	public static unsafe DateOnly FromDayNumber(int dayNumber)
	{
		if (dayNumber < 0 || dayNumber > 3_652_058) M68kRuntime.ThrowArgumentOutOfRangeException();
		return *(DateOnly*)&dayNumber;
	}

	public static DateOnly AddDays(ref DateOnly value, int days) =>
		DateOnly.FromDayNumber(value.DayNumber + days);

	public static int GetYear(ref DateOnly value)
	{
		GetDateParts(value.DayNumber, out var year, out _, out _);
		return year;
	}

	public static int GetMonth(ref DateOnly value)
	{
		GetDateParts(value.DayNumber, out _, out var month, out _);
		return month;
	}

	public static int GetDay(ref DateOnly value)
	{
		GetDateParts(value.DayNumber, out _, out _, out var day);
		return day;
	}

	public static void ThrowDayNumberOutOfRange(int dayNumber) => M68kRuntime.ThrowArgumentOutOfRangeException();

	private static void GetDateParts(int dayNumber, out int year, out int month, out int day)
	{
		var remaining = dayNumber;
		year = 1;
		while (true)
		{
			var daysInYear = IsLeapYear(year) ? 366 : 365;
			if (remaining < daysInYear) break;
			remaining -= daysInYear;
			year++;
		}
		month = 1;
		while (remaining >= DaysInMonth(year, month))
		{
			remaining -= DaysInMonth(year, month);
			month++;
		}
		day = remaining + 1;
	}

	private static int DaysInMonth(int year, int month) => month switch
	{
		2 => IsLeapYear(year) ? 29 : 28,
		4 or 6 or 9 or 11 => 30,
		_ => 31
	};

	private static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
}

/// <summary>
/// Layout-compatible value-type constructor for CoreLib's one-word DateOnly.
/// The constructor body is invoked with the caller's DateOnly temporary as this.
/// </summary>
internal unsafe struct ShadowDateOnlyConstructor
{
	private int _dayNumber;

	public DateOnly AddDays(int days) => DateOnly.FromDayNumber(_dayNumber + days);
	public int CompareTo(DateOnly other) => _dayNumber < other.DayNumber ? -1 : _dayNumber > other.DayNumber ? 1 : 0;
	public bool Equals(DateOnly other) => _dayNumber == other.DayNumber;
	public static bool AreEqual(DateOnly left, DateOnly right) => left.DayNumber == right.DayNumber;
	public static bool AreNotEqual(DateOnly left, DateOnly right) => left.DayNumber != right.DayNumber;
	public static bool IsLessThan(DateOnly left, DateOnly right) => left.DayNumber < right.DayNumber;
	public static bool IsLessThanOrEqual(DateOnly left, DateOnly right) => left.DayNumber <= right.DayNumber;

	public string FormatIsoDate(string? format, IFormatProvider? provider)
	{
		_ = format;
		_ = provider;
		var dayNumber = _dayNumber;
		var value = *(DateOnly*)&dayNumber;
		Span<char> result = stackalloc char[10];
		var year = value.Year;
		result[0] = (char)('0' + year / 1000);
		result[1] = (char)('0' + year / 100 % 10);
		result[2] = (char)('0' + year / 10 % 10);
		result[3] = (char)('0' + year % 10);
		result[4] = '-';
		var month = value.Month;
		result[5] = (char)('0' + month / 10);
		result[6] = (char)('0' + month % 10);
		result[7] = '-';
		var day = value.Day;
		result[8] = (char)('0' + day / 10);
		result[9] = (char)('0' + day % 10);
		return ShadowStringData.FromCharacters(result);
	}

	public ShadowDateOnlyConstructor(int year, int month, int day)
	{
		if (year < 1 || year > 9999) M68kRuntime.ThrowArgumentOutOfRangeException();
		if (month < 1 || month > 12) M68kRuntime.ThrowArgumentOutOfRangeException();
		var maximumDay = month == 2 ? IsLeapYear(year) ? 29 : 28 :
			month is 4 or 6 or 9 or 11 ? 30 : 31;
		if (day < 1 || day > maximumDay) M68kRuntime.ThrowArgumentOutOfRangeException();

		var previousYear = year - 1;
		_dayNumber = previousYear * 365 + previousYear / 4 - previousYear / 100 + previousYear / 400;
		for (var currentMonth = 1; currentMonth < month; currentMonth++)
			_dayNumber += currentMonth == 2 ? IsLeapYear(year) ? 29 : 28 :
				currentMonth is 4 or 6 or 9 or 11 ? 30 : 31;
		_dayNumber += day - 1;
	}

	private static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
}
