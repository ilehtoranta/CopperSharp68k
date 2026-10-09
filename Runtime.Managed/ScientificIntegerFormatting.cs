/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Integral scientific and precision-bearing general formats.</summary>
public static class ShadowScientificIntegerFormatting
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryFormat(uint high, uint low, bool negative, IFormatProvider? provider, char specifier, int precision,
		Span<char> destination, out int charsWritten)
	{
		Span<char> digits = stackalloc char[20];
		Settings(negative, provider, out var sign, out var separator, out var exponentSign);
		var significant = specifier is 'E' or 'e' ? precision + 1 : precision < 1 ? 20 : precision;
		var count = Round(high, low, negative, significant, digits, out var scale);
		var length = Layout(specifier, precision, count, scale, sign, separator, exponentSign,
			out var integerDigits, out var fractionDigits, out var exponentDigits);
		if (destination.Length < length) { charsWritten = 0; return false; }
		var offset = 0;
		for (var index = 0; index < sign.Length; index++) destination[offset++] = sign[index];
		for (var index = 0; index < integerDigits; index++) destination[offset++] = index < count ? digits[index] : '0';
		if (fractionDigits != 0)
		{
			for (var index = 0; index < separator.Length; index++) destination[offset++] = separator[index];
			for (var index = 0; index < fractionDigits; index++) destination[offset++] = index + 1 < count ? digits[index + 1] : '0';
		}
		if (exponentDigits != 0)
		{
			destination[offset++] = specifier is 'E' or 'G' or 'R' ? 'E' : 'e';
			for (var index = 0; index < exponentSign.Length; index++) destination[offset++] = exponentSign[index];
			var exponent = scale == 0 ? 0 : scale - 1;
			if (exponentDigits == 3) destination[offset++] = '0';
			destination[offset++] = (char)('0' + exponent / 10);
			destination[offset++] = (char)('0' + exponent % 10);
		}
		charsWritten = offset;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format(uint high, uint low, bool negative, IFormatProvider? provider, char specifier, int precision)
	{
		Span<char> digits = stackalloc char[20];
		Settings(negative, provider, out var sign, out var separator, out var exponentSign);
		var significant = specifier is 'E' or 'e' ? precision + 1 : precision < 1 ? 20 : precision;
		var count = Round(high, low, negative, significant, digits, out var scale);
		var length = Layout(specifier, precision, count, scale, sign, separator, exponentSign,
			out var integerDigits, out var fractionDigits, out var exponentDigits);
		var text = M68kRuntime.AllocateString(length);
		var offset = 0;
		for (var index = 0; index < sign.Length; index++) M68kRuntime.SetStringChar(text, offset++, sign[index]);
		for (var index = 0; index < integerDigits; index++) M68kRuntime.SetStringChar(text, offset++, index < count ? digits[index] : '0');
		if (fractionDigits != 0)
		{
			for (var index = 0; index < separator.Length; index++) M68kRuntime.SetStringChar(text, offset++, separator[index]);
			for (var index = 0; index < fractionDigits; index++) M68kRuntime.SetStringChar(text, offset++, index + 1 < count ? digits[index + 1] : '0');
		}
		if (exponentDigits != 0)
		{
			M68kRuntime.SetStringChar(text, offset++, specifier is 'E' or 'G' or 'R' ? 'E' : 'e');
			for (var index = 0; index < exponentSign.Length; index++) M68kRuntime.SetStringChar(text, offset++, exponentSign[index]);
			var exponent = scale == 0 ? 0 : scale - 1;
			if (exponentDigits == 3) M68kRuntime.SetStringChar(text, offset++, '0');
			M68kRuntime.SetStringChar(text, offset++, (char)('0' + exponent / 10));
			M68kRuntime.SetStringChar(text, offset, (char)('0' + exponent % 10));
		}
		return text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Settings(bool negative, IFormatProvider? provider, out string sign, out string separator, out string exponentSign)
	{
		var info = ShadowStandardIntegerFormatting.NumberInfo(provider);
		sign = negative ? info!.NegativeSign : string.Empty;
		separator = info!.NumberDecimalSeparator;
		exponentSign = info!.PositiveSign;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Round(uint high, uint low, bool negative, int significant, Span<char> digits, out int scale)
	{
		var length = negative
			? ShadowIntegerFormatter.PackInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4)
			: ShadowIntegerFormatter.PackUInt64(high, low, out word0, out word1, out word2, out word3, out word4);
		var skip = negative ? 1 : 0;
		var count = length - skip;
		scale = count;
		for (var index = 0; index < count; index++)
			digits[index] = ShadowStandardIntegerFormatting.PackedDigit(index + skip, word0, word1, word2, word3, word4);
		if (significant < count)
		{
			var up = digits[significant] >= '5';
			count = significant;
			if (up)
			{
				var index = count - 1;
				while (index >= 0 && digits[index] == '9') { digits[index] = '0'; index--; }
				if (index >= 0) digits[index]++;
				else { digits[0] = '1'; scale++; count = 1; }
			}
		}
		while (count > 0 && digits[count - 1] == '0') count--;
		if (count == 0) scale = 0;
		return count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Layout(char specifier, int precision, int count, int scale, string sign, string separator, string exponentSign,
		out int integerDigits, out int fractionDigits, out int exponentDigits)
	{
		var scientific = specifier is 'E' or 'e';
		var generalPrecision = precision < 1 ? 20 : precision;
		exponentDigits = scientific ? 3 : scale > generalPrecision ? 2 : 0;
		integerDigits = exponentDigits != 0 ? 1 : scale == 0 ? 1 : scale;
		fractionDigits = scientific ? precision : exponentDigits != 0 && count > 1 ? count - 1 : 0;
		return sign.Length + integerDigits + (fractionDigits != 0 ? separator.Length : 0) + fractionDigits
			+ (exponentDigits != 0 ? 1 + exponentSign.Length + exponentDigits : 0);
	}
}
