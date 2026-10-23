/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>
/// Invariant parsing for the canonical wire forms admitted by the TimeOnly profile.
/// </summary>
internal static class ShadowTimeOnlyParsing
{
	private const long TicksPerSecond = 10_000_000;
	private const long TicksPerMinute = 600_000_000;
	private const long TicksPerHour = 36_000_000_000;

	public static TimeOnly ParseInvariant(string? text, IFormatProvider? provider)
	{
		_ = provider;
		if (text is null) M68kRuntime.ThrowArgumentNullException();
		if (!TryParseInvariant(text, provider, out var result)) M68kRuntime.ThrowFormatException();
		return result;
	}

	public static unsafe bool TryParseInvariant(string? text, IFormatProvider? provider, out TimeOnly result)
	{
		_ = provider;
		result = default;
		if (text is null) return false;
		var index = 0;
		if (!TryReadTwoDigits(text, ref index, out var hour) || !TryReadSeparator(text, ref index) ||
			!TryReadTwoDigits(text, ref index, out var minute)) return false;
		var second = 0;
		var fractionTicks = 0;
		if (index < text.Length)
		{
			if (!TryReadSeparator(text, ref index) || !TryReadTwoDigits(text, ref index, out second)) return false;
			if (index < text.Length)
			{
				if (text[index++] != '.') return false;
				var fractionDigits = 0;
				while (index < text.Length && fractionDigits < 7)
				{
					var digit = text[index] - '0';
					if ((uint)digit > 9) return false;
					fractionTicks = fractionTicks * 10 + digit;
					fractionDigits++;
					index++;
				}
				if (fractionDigits == 0 || index != text.Length) return false;
				while (fractionDigits++ < 7) fractionTicks *= 10;
			}
		}
		if (index != text.Length || hour > 23 || minute > 59 || second > 59) return false;
		var ticks = hour * TicksPerHour + minute * TicksPerMinute + second * TicksPerSecond + fractionTicks;
		fixed (TimeOnly* resultPointer = &result)
			*(long*)resultPointer = ticks;
		return true;
	}

	private static bool TryReadSeparator(string text, ref int index)
	{
		if (index >= text.Length || text[index] != ':') return false;
		index++;
		return true;
	}

	private static bool TryReadTwoDigits(string text, ref int index, out int value)
	{
		value = 0;
		if (index + 1 >= text.Length) return false;
		var tens = text[index] - '0';
		var ones = text[index + 1] - '0';
		if ((uint)tens > 9 || (uint)ones > 9) return false;
		value = tens * 10 + ones;
		index += 2;
		return true;
	}
}
