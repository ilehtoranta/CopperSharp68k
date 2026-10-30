/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Invariant parsing for the canonical yyyy-MM-dd form admitted by the DateOnly profile.
/// </summary>
internal static class ShadowDateOnlyParsing
{
	public static DateOnly ParseInvariant(string? text, IFormatProvider? provider)
	{
		_ = provider;
		if (text is null) M68kRuntime.ThrowArgumentNullException();
		if (!TryParseInvariant(text, provider, out var result)) M68kRuntime.ThrowFormatException();
		return result;
	}

	public static unsafe bool TryParseInvariant(string? text, IFormatProvider? provider, out DateOnly result)
	{
		_ = provider;
		result = default;
		if (text is null || text.Length != 10 || text[4] != '-' || text[7] != '-') return false;
		if (!TryReadFourDigits(text, 0, out var year) || !TryReadTwoDigits(text, 5, out var month) ||
			!TryReadTwoDigits(text, 8, out var day) || year is < 1 or > 9999 || month is < 1 or > 12) return false;
		var maximumDay = month switch
		{
			2 => IsLeapYear(year) ? 29 : 28,
			4 or 6 or 9 or 11 => 30,
			_ => 31
		};
		if (day < 1 || day > maximumDay) return false;

		var previousYear = year - 1;
		var dayNumber = previousYear * 365 + previousYear / 4 - previousYear / 100 + previousYear / 400;
		for (var currentMonth = 1; currentMonth < month; currentMonth++)
			dayNumber += currentMonth == 2 ? IsLeapYear(year) ? 29 : 28 :
				currentMonth is 4 or 6 or 9 or 11 ? 30 : 31;
		dayNumber += day - 1;
		fixed (DateOnly* resultPointer = &result)
			*(int*)resultPointer = dayNumber;
		return true;
	}

	private static bool TryReadFourDigits(string text, int index, out int value)
	{
		value = 0;
		for (var offset = 0; offset < 4; offset++)
		{
			var digit = text[index + offset] - '0';
			if ((uint)digit > 9) return false;
			value = value * 10 + digit;
		}
		return true;
	}

	private static bool TryReadTwoDigits(string text, int index, out int value)
	{
		value = 0;
		var tens = text[index] - '0';
		var ones = text[index + 1] - '0';
		if ((uint)tens > 9 || (uint)ones > 9) return false;
		value = tens * 10 + ones;
		return true;
	}

	private static bool IsLeapYear(int year) => year % 4 == 0 && (year % 100 != 0 || year % 400 == 0);
}
