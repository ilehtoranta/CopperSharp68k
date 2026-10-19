/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Standard integral formats shared by the experimental Number overrides.</summary>
public static class ShadowStandardIntegerFormatting
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static char Parse(ReadOnlySpan<char> format, out int precision)
	{
		precision = 0;
		if (format.Length == 0 || format[0] == '\0')
		{
			return 'D';
		}
		var specifier = format[0];
		if (!(specifier >= 'A' && specifier <= 'Z' || specifier >= 'a' && specifier <= 'z')) return '\0';
		for (var index = 1; index < format.Length && format[index] != '\0'; index++)
		{
			var digit = format[index] - '0';
			if (digit < 0 || digit > 9) return '\0'; // A custom numeric format.
			if (precision > 99_999_999) throw new FormatException();
			precision = precision * 10 + digit;
		}
		if (specifier is 'X' or 'x') return specifier;
		if (specifier is 'B' or 'b') return 'B';
		if (specifier is 'E' or 'e')
		{
			if (format.Length == 1) precision = 6;
			return specifier;
		}
		if (specifier is 'R' or 'r' || specifier is 'G' or 'g' && precision != 0) return specifier;
		if (specifier is 'F' or 'f' or 'N' or 'n' or 'C' or 'c' or 'P' or 'p')
		{
			// CoreLib's sole-letter path uses provider precision. A following
			// NUL enters its digit parser and denotes explicit zero precision.
			if (format.Length == 1) precision = -1;
			return specifier is 'N' or 'n' ? 'N' : specifier is 'C' or 'c' ? 'C' : specifier is 'P' or 'p' ? 'P' : 'F';
		}
		if (specifier is 'D' or 'd' || specifier is 'G' or 'g' && precision == 0)
		{
			return 'D';
		}
		throw new FormatException();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string NegativeSign(IFormatProvider? provider)
	{
		var info = NumberInfo(provider);
		return info!.NegativeSign;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static System.Globalization.NumberFormatInfo NumberInfo(IFormatProvider? provider) =>
		System.Globalization.NumberFormatInfo.GetInstance(provider);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void DecimalSettings(bool negative, char specifier, IFormatProvider? provider, ref int precision,
		out string sign, out string separator)
	{
		// F always queries the numeric provider, including positive values and
		// F0. Query once so all properties belong to the same supplied instance.
		var info = negative || specifier == 'F' ? NumberInfo(provider) : null;
		sign = negative ? info!.NegativeSign : string.Empty;
		separator = string.Empty;
		if (specifier == 'F')
		{
			if (precision < 0) precision = info!.NumberDecimalDigits;
			if (precision != 0) separator = info!.NumberDecimalSeparator;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryFormat(uint high, uint low, bool negative, IFormatProvider? provider, char specifier, int precision,
		Span<char> destination, out int charsWritten)
	{
		if (specifier == 'P')
			return ShadowGroupedIntegerFormatting.TryFormatPercent(high, low, negative, provider, precision, destination, out charsWritten);
		if (specifier == 'C')
			return ShadowGroupedIntegerFormatting.TryFormatCurrency(high, low, negative, provider, precision, destination, out charsWritten);
		if (specifier == 'N')
			return ShadowGroupedIntegerFormatting.TryFormat(high, low, negative, provider, precision, destination, out charsWritten);
		if (specifier is 'E' or 'e' or 'G' or 'g' or 'R' or 'r')
			return ShadowScientificIntegerFormatting.TryFormat(high, low, negative, provider, specifier, precision, destination, out charsWritten);
		if (specifier != 'D' && specifier != 'F')
		{
			var shift = specifier == 'B' ? 1 : 4;
			var digits = CountRadixDigits(high, low, shift);
			var length = precision > digits ? precision : digits;
			if (destination.Length < length) { charsWritten = 0; return false; }
			for (var index = 0; index < length; index++)
				destination[index] = index < length - digits ? '0' : RadixDigit(high, low, length - index - 1, shift, specifier);
			charsWritten = length;
			return true;
		}
		DecimalSettings(negative, specifier, provider, ref precision, out var negativeSign, out var separator);
		var packedLength = negative
			? ShadowIntegerFormatter.PackInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4)
			: ShadowIntegerFormatter.PackUInt64(high, low, out word0, out word1, out word2, out word3, out word4);
		var packedSign = negative ? 1 : 0;
		var digitsLength = packedLength - packedSign;
		var sign = negative ? negativeSign.Length : 0;
		var padding = specifier == 'D' && precision > digitsLength ? precision - digitsLength : 0;
		var integerLength = digitsLength + sign + padding;
		var fraction = specifier == 'F' ? precision : 0;
		var total = integerLength + separator.Length + fraction;
		if (destination.Length < total) { charsWritten = 0; return false; }
		for (var index = 0; index < sign; index++) destination[index] = negativeSign[index];
		for (var index = sign; index < integerLength; index++)
			destination[index] = index < sign + padding ? '0' : PackedDigit(index - sign - padding + packedSign, word0, word1, word2, word3, word4);
		for (var index = 0; index < separator.Length; index++) destination[integerLength + index] = separator[index];
		for (var index = integerLength + separator.Length; index < total; index++) destination[index] = '0';
		charsWritten = total;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format(uint high, uint low, bool negative, IFormatProvider? provider, char specifier, int precision)
	{
		if (specifier == 'P')
			return ShadowGroupedIntegerFormatting.FormatPercent(high, low, negative, provider, precision);
		if (specifier == 'C')
			return ShadowGroupedIntegerFormatting.FormatCurrency(high, low, negative, provider, precision);
		if (specifier == 'N')
			return ShadowGroupedIntegerFormatting.Format(high, low, negative, provider, precision);
		if (specifier is 'E' or 'e' or 'G' or 'g' or 'R' or 'r')
			return ShadowScientificIntegerFormatting.Format(high, low, negative, provider, specifier, precision);
		if (specifier != 'D' && specifier != 'F')
		{
			var shift = specifier == 'B' ? 1 : 4;
			var digits = CountRadixDigits(high, low, shift);
			var length = precision > digits ? precision : digits;
			var result = M68kRuntime.AllocateString(length);
			for (var index = 0; index < length; index++)
				M68kRuntime.SetStringChar(result, index, index < length - digits ? '0' : RadixDigit(high, low, length - index - 1, shift, specifier));
			return result;
		}
		DecimalSettings(negative, specifier, provider, ref precision, out var negativeSign, out var separator);
		var packedLength = negative
			? ShadowIntegerFormatter.PackInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4)
			: ShadowIntegerFormatter.PackUInt64(high, low, out word0, out word1, out word2, out word3, out word4);
		var packedSign = negative ? 1 : 0;
		var digitsLength = packedLength - packedSign;
		var sign = negative ? negativeSign.Length : 0;
		var padding = specifier == 'D' && precision > digitsLength ? precision - digitsLength : 0;
		var integerLength = digitsLength + sign + padding;
		var fraction = specifier == 'F' ? precision : 0;
		var total = integerLength + separator.Length + fraction;
		var text = M68kRuntime.AllocateString(total);
		for (var index = 0; index < sign; index++) M68kRuntime.SetStringChar(text, index, negativeSign[index]);
		for (var index = sign; index < integerLength; index++)
			M68kRuntime.SetStringChar(text, index, index < sign + padding ? '0' : PackedDigit(index - sign - padding + packedSign, word0, word1, word2, word3, word4));
		for (var index = 0; index < separator.Length; index++) M68kRuntime.SetStringChar(text, integerLength + index, separator[index]);
		for (var index = integerLength + separator.Length; index < total; index++) M68kRuntime.SetStringChar(text, index, '0');
		return text;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static char PackedDigit(int index, uint word0, uint word1, uint word2, uint word3, uint word4)
	{
		var word = index < 4 ? word0 : index < 8 ? word1 : index < 12 ? word2 : index < 16 ? word3 : word4;
		return (char)((word >> ((3 - (index & 3)) * 8)) & 0xffu);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CountRadixDigits(uint high, uint low, int shift)
	{
		var digits = 1;
		while (high != 0 || low >= (1u << shift))
		{
			low = (low >> shift) | (high << (32 - shift));
			high >>= shift;
			digits++;
		}
		return digits;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static char RadixDigit(uint high, uint low, int index, int shift, char specifier)
	{
		var bit = index * shift;
		var word = bit < 32 ? low : high;
		var digit = (word >> (bit & 31)) & ((1u << shift) - 1);
		return digit < 10 ? (char)('0' + digit) : (char)((specifier == 'X' ? 'A' : 'a') + digit - 10);
	}
}
