/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Integral number, currency and percent formats using provider grouping and placement patterns.</summary>
public static class ShadowGroupedIntegerFormatting
{
	// On the target these exact readers bind to the corresponding verified
	// CoreLib System.Number bodies. The host accessors have the same
	// allocation-free read; the public property must continue returning a copy.
	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static int[] NumberGroupSizes(NumberFormatInfo info) => GroupSizesField(info);

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_numberGroupSizes")]
	private static extern ref int[] GroupSizesField(NumberFormatInfo info);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static int[] CurrencyGroupSizes(NumberFormatInfo info) => CurrencyGroupSizesField(info);

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_currencyGroupSizes")]
	private static extern ref int[] CurrencyGroupSizesField(NumberFormatInfo info);

	[MethodImpl(MethodImplOptions.NoInlining)]
	internal static int[] PercentGroupSizes(NumberFormatInfo info) => PercentGroupSizesField(info);

	[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_percentGroupSizes")]
	private static extern ref int[] PercentGroupSizesField(NumberFormatInfo info);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void PercentSettings(bool negative, IFormatProvider? provider, ref int precision,
		out string sign, out string decimalSeparator, out string groupSeparator, out string symbol, out string pattern, out int[]? groups)
	{
		var info = ShadowStandardIntegerFormatting.NumberInfo(provider);
		if (precision < 0) precision = info!.PercentDecimalDigits;
		sign = info!.NegativeSign;
		decimalSeparator = precision == 0 ? string.Empty : info!.PercentDecimalSeparator;
		groupSeparator = info!.PercentGroupSeparator;
		symbol = info!.PercentSymbol;
		groups = PercentGroupSizes(info);
		var placement = negative ? info.PercentNegativePattern : info.PercentPositivePattern;
		pattern = negative ? placement switch
		{
			0 => "-# %", 1 => "-#%", 2 => "-%#", 3 => "%-#", 4 => "%#-", 5 => "#-%", 6 => "#%-",
			7 => "-% #", 8 => "# %-", 9 => "% #-", 10 => "% -#", _ => "#- %"
		} : placement switch { 0 => "# %", 1 => "#%", 2 => "%#", _ => "% #" };
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CurrencySettings(bool negative, IFormatProvider? provider, ref int precision,
		out string sign, out string decimalSeparator, out string groupSeparator, out string symbol, out string pattern, out int[]? groups)
	{
		var info = ShadowStandardIntegerFormatting.NumberInfo(provider);
		if (precision < 0) precision = info!.CurrencyDecimalDigits;
		sign = info!.NegativeSign;
		decimalSeparator = precision == 0 ? string.Empty : info!.CurrencyDecimalSeparator;
		groupSeparator = info!.CurrencyGroupSeparator;
		symbol = info!.CurrencySymbol;
		groups = CurrencyGroupSizes(info);
		var placement = negative ? info.CurrencyNegativePattern : info.CurrencyPositivePattern;
		pattern = negative ? placement switch
		{
			0 => "($#)", 1 => "-$#", 2 => "$-#", 3 => "$#-", 4 => "(#$)", 5 => "-#$",
			6 => "#-$", 7 => "#$-", 8 => "-# $", 9 => "-$ #", 10 => "# $-", 11 => "$ #-",
			12 => "$ -#", 13 => "#- $", 14 => "($ #)", 15 => "(# $)", _ => "$- #"
		} : placement switch { 0 => "$#", 1 => "#$", 2 => "$ #", _ => "# $" };
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Settings(bool negative, IFormatProvider? provider, ref int precision,
		out string sign, out string decimalSeparator, out string groupSeparator, out string pattern, out int[]? groups)
	{
		var info = ShadowStandardIntegerFormatting.NumberInfo(provider);
		if (precision < 0) precision = info!.NumberDecimalDigits;
		sign = info!.NegativeSign;
		decimalSeparator = precision == 0 ? string.Empty : info!.NumberDecimalSeparator;
		groupSeparator = info!.NumberGroupSeparator;
		groups = NumberGroupSizes(info);
		var negativePattern = negative ? info!.NumberNegativePattern : -1;
		pattern = negativePattern switch { 0 => "(#)", 1 => "-#", 2 => "- #", 3 => "#-", 4 => "# -", _ => "#" };
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long Layout(int digits, int precision, string sign, string decimalSeparator, string groupSeparator,
		string symbol, string pattern, int[]? groups, out uint boundaries)
	{
		boundaries = 0;
		var separators = 0;
		var groupIndex = 0;
		var size = groups is null ? 3 : groups.Length == 0 ? 0 : groups[0];
		var total = size;
		if (groupSeparator.Length != 0)
		{
			while (size != 0 && total < digits)
			{
				boundaries |= 1u << total;
				separators++;
				if (groups is not null && groupIndex < groups.Length - 1) size = groups[++groupIndex];
				total += size;
			}
		}
		long length = digits + (long)separators * groupSeparator.Length + decimalSeparator.Length + precision;
		for (var index = 0; index < pattern.Length; index++)
			if (pattern[index] != '#') length += pattern[index] == '-' ? sign.Length : pattern[index] is '$' or '%' ? symbol.Length : 1;
		return length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryFormat(uint high, uint low, bool negative, IFormatProvider? provider, int precision,
		Span<char> destination, out int charsWritten)
	{
		Settings(negative, provider, ref precision, out var sign, out var decimalSeparator, out var groupSeparator, out var pattern, out var groups);
		return TryFormatCore(high, low, negative, precision, 0, destination, out charsWritten, sign, decimalSeparator, groupSeparator, string.Empty, pattern, groups);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryFormatCurrency(uint high, uint low, bool negative, IFormatProvider? provider, int precision,
		Span<char> destination, out int charsWritten)
	{
		CurrencySettings(negative, provider, ref precision, out var sign, out var decimalSeparator, out var groupSeparator, out var symbol, out var pattern, out var groups);
		return TryFormatCore(high, low, negative, precision, 0, destination, out charsWritten, sign, decimalSeparator, groupSeparator, symbol, pattern, groups);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static bool TryFormatPercent(uint high, uint low, bool negative, IFormatProvider? provider, int precision,
		Span<char> destination, out int charsWritten)
	{
		PercentSettings(negative, provider, ref precision, out var sign, out var decimalSeparator, out var groupSeparator, out var symbol, out var pattern, out var groups);
		return TryFormatCore(high, low, negative, precision, 2, destination, out charsWritten, sign, decimalSeparator, groupSeparator, symbol, pattern, groups);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryFormatCore(uint high, uint low, bool negative, int precision, int scale,
		Span<char> destination, out int charsWritten, string sign, string decimalSeparator, string groupSeparator,
		string symbol, string pattern, int[]? groups)
	{
		var packedLength = negative
			? ShadowIntegerFormatter.PackInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4)
			: ShadowIntegerFormatter.PackUInt64(high, low, out word0, out word1, out word2, out word3, out word4);
		var skip = negative ? 1 : 0;
		var packedDigits = packedLength - skip;
		// Percent scales the decimal magnitude without multiplying a bounded
		// integer. Zero retains one digit; UInt64.MaxValue grows to 22 digits.
		var digits = packedDigits + ((high | low) != 0 ? scale : 0);
		var length = Layout(digits, precision, sign, decimalSeparator, groupSeparator, symbol, pattern, groups, out var boundaries);
		if (length > destination.Length) { charsWritten = 0; return false; }
		var offset = 0;
		for (var patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
		{
			var character = pattern[patternIndex];
			if (character == '#')
			{
				for (var index = 0; index < digits; index++)
				{
					if (index != 0 && (boundaries & (1u << (digits - index))) != 0)
						for (var separatorIndex = 0; separatorIndex < groupSeparator.Length; separatorIndex++) destination[offset++] = groupSeparator[separatorIndex];
					destination[offset++] = index < packedDigits ? ShadowStandardIntegerFormatting.PackedDigit(index + skip, word0, word1, word2, word3, word4) : '0';
				}
				for (var index = 0; index < decimalSeparator.Length; index++) destination[offset++] = decimalSeparator[index];
				for (var index = 0; index < precision; index++) destination[offset++] = '0';
			}
			else if (character == '-')
			{
				for (var index = 0; index < sign.Length; index++) destination[offset++] = sign[index];
			}
			else if (character is '$' or '%')
			{
				for (var index = 0; index < symbol.Length; index++) destination[offset++] = symbol[index];
			}
			else destination[offset++] = character;
		}
		charsWritten = offset;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string Format(uint high, uint low, bool negative, IFormatProvider? provider, int precision)
	{
		Settings(negative, provider, ref precision, out var sign, out var decimalSeparator, out var groupSeparator, out var pattern, out var groups);
		return FormatCore(high, low, negative, precision, 0, sign, decimalSeparator, groupSeparator, string.Empty, pattern, groups);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatCurrency(uint high, uint low, bool negative, IFormatProvider? provider, int precision)
	{
		CurrencySettings(negative, provider, ref precision, out var sign, out var decimalSeparator, out var groupSeparator, out var symbol, out var pattern, out var groups);
		return FormatCore(high, low, negative, precision, 0, sign, decimalSeparator, groupSeparator, symbol, pattern, groups);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string FormatPercent(uint high, uint low, bool negative, IFormatProvider? provider, int precision)
	{
		PercentSettings(negative, provider, ref precision, out var sign, out var decimalSeparator, out var groupSeparator, out var symbol, out var pattern, out var groups);
		return FormatCore(high, low, negative, precision, 2, sign, decimalSeparator, groupSeparator, symbol, pattern, groups);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string FormatCore(uint high, uint low, bool negative, int precision, int scale, string sign, string decimalSeparator,
		string groupSeparator, string symbol, string pattern, int[]? groups)
	{
		var packedLength = negative
			? ShadowIntegerFormatter.PackInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4)
			: ShadowIntegerFormatter.PackUInt64(high, low, out word0, out word1, out word2, out word3, out word4);
		var skip = negative ? 1 : 0;
		var packedDigits = packedLength - skip;
		var digits = packedDigits + ((high | low) != 0 ? scale : 0);
		var length = Layout(digits, precision, sign, decimalSeparator, groupSeparator, symbol, pattern, groups, out var boundaries);
		if (length > int.MaxValue) throw new OutOfMemoryException();
		var text = M68kRuntime.AllocateString((int)length);
		var offset = 0;
		for (var patternIndex = 0; patternIndex < pattern.Length; patternIndex++)
		{
			var character = pattern[patternIndex];
			if (character == '#')
			{
				for (var index = 0; index < digits; index++)
				{
					if (index != 0 && (boundaries & (1u << (digits - index))) != 0)
						for (var separatorIndex = 0; separatorIndex < groupSeparator.Length; separatorIndex++) M68kRuntime.SetStringChar(text, offset++, groupSeparator[separatorIndex]);
					M68kRuntime.SetStringChar(text, offset++, index < packedDigits ? ShadowStandardIntegerFormatting.PackedDigit(index + skip, word0, word1, word2, word3, word4) : '0');
				}
				for (var index = 0; index < decimalSeparator.Length; index++) M68kRuntime.SetStringChar(text, offset++, decimalSeparator[index]);
				for (var index = 0; index < precision; index++) M68kRuntime.SetStringChar(text, offset++, '0');
			}
			else if (character == '-')
			{
				for (var index = 0; index < sign.Length; index++) M68kRuntime.SetStringChar(text, offset++, sign[index]);
			}
			else if (character is '$' or '%')
			{
				for (var index = 0; index < symbol.Length; index++) M68kRuntime.SetStringChar(text, offset++, symbol[index]);
			}
			else M68kRuntime.SetStringChar(text, offset++, character);
		}
		return text;
	}
}
