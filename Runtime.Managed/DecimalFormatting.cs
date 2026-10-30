/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Runtime.CompilerServices;

namespace CopperSharp.Runtime;

/// <summary>Decimal digit-buffer adapters for the verified UTF-16 CoreLib renderers.</summary>
public static class ShadowDecimalFormatting
{
	public static string FormatDecimal(decimal value, ReadOnlySpan<char> format, NumberFormatInfo info)
	{
		var specifier = ParseFormatSpecifier(format, out var precision);
		Span<byte> digits = stackalloc byte[31];
		var number = CreateNumber(value, digits);
		Span<char> scratch = stackalloc char[32];
		var builder = new ShadowValueListBuilder<char>(scratch);
		if (specifier == '\0') RenderCustom(ref builder, ref number, format, info);
		else RenderStandard(ref builder, ref number, specifier, precision, info);
		var result = new string(builder.AsSpan());
		builder.Dispose();
		return result;
	}

	public static bool TryFormatDecimal(decimal value, ReadOnlySpan<char> format, NumberFormatInfo info,
		Span<char> destination, out int charsWritten)
	{
		var specifier = ParseFormatSpecifier(format, out var precision);
		Span<byte> digits = stackalloc byte[31];
		var number = CreateNumber(value, digits);
		Span<char> scratch = stackalloc char[32];
		var builder = new ShadowValueListBuilder<char>(scratch);
		if (specifier == '\0') RenderCustom(ref builder, ref number, format, info);
		else RenderStandard(ref builder, ref number, specifier, precision, info);
		var text = builder.AsSpan();
		var success = text.TryCopyTo(destination);
		charsWritten = success ? text.Length : 0;
		builder.Dispose();
		return success;
	}

	internal static ShadowNumberBuffer CreateNumber(decimal value, Span<byte> digits)
	{
		// GetBits reads the logical words through CoreLib's numeric accessors;
		// this avoids reinterpreting its little-endian DecCalc union on the target.
		Span<int> bits = stackalloc int[4];
		decimal.GetBits(value, bits);
		uint low = (uint)bits[0], middle = (uint)bits[1], high = (uint)bits[2];
		int flags = bits[3];
		var start = 29;
		while ((low | middle | high) != 0)
		{
			uint remainder = 0;
			high = DivideWordByTen(high, ref remainder);
			middle = DivideWordByTen(middle, ref remainder);
			low = DivideWordByTen(low, ref remainder);
			digits[--start] = (byte)('0' + remainder);
		}
		int count = 29 - start;
		for (int index = 0; index < count; index++) digits[index] = digits[start + index];
		digits[count] = 0;
		return new ShadowNumberBuffer { DigitsCount = count, Scale = count - ((flags >> 16) & 255),
			IsNegative = flags < 0, Kind = ShadowNumberBufferKind.Decimal, Digits = digits };
	}

	private static uint DivideWordByTen(uint word, ref uint remainder)
	{
		uint quotient = 0;
		for (int bit = 31; bit >= 0; bit--)
		{
			remainder = remainder * 2 + ((word >> bit) & 1);
			if (remainder >= 10) { remainder -= 10; quotient |= 1u << bit; }
		}
		return quotient;
	}

	// These host bodies provide an independent CLR rendering oracle. Target
	// compilation substitutes the corresponding verified CoreLib methods.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static char ParseFormatSpecifier(ReadOnlySpan<char> format, out int precision)
	{
		precision = -1;
		if (format.Length == 0 || format[0] == '\0') return 'G';
		char specifier = format[0];
		if (!(specifier >= 'A' && specifier <= 'Z' || specifier >= 'a' && specifier <= 'z')) return '\0';
		if (format.Length == 1) return specifier;
		int value = 0;
		int index = 1;
		for (; index < format.Length && format[index] >= '0' && format[index] <= '9'; index++)
		{
			if (value >= 100_000_000) throw new FormatException();
			value = value * 10 + format[index] - '0';
		}
		if (index < format.Length && format[index] != '\0') return '\0';
		precision = value;
		return specifier;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RenderStandard(ref ShadowValueListBuilder<char> builder, ref ShadowNumberBuffer number,
		char specifier, int precision, NumberFormatInfo info)
	{
		var format = precision < 0 ? specifier.ToString() : specifier.ToString() + precision.ToString(CultureInfo.InvariantCulture);
		var result = ToDecimal(ref number).ToString(format, info);
		for (int index = 0; index < result.Length; index++) builder.Append(result[index]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void RenderCustom(ref ShadowValueListBuilder<char> builder, ref ShadowNumberBuffer number,
		ReadOnlySpan<char> format, NumberFormatInfo info)
	{
		var result = ToDecimal(ref number).ToString(format.ToString(), info);
		for (int index = 0; index < result.Length; index++) builder.Append(result[index]);
	}

	private static decimal ToDecimal(ref ShadowNumberBuffer number)
	{
		uint low = 0, middle = 0, high = 0;
		for (int index = 0; index < number.DigitsCount; index++)
		{
			ulong product = (ulong)low * 10 + (uint)(number.Digits[index] - '0');
			low = (uint)product;
			product = (ulong)middle * 10 + (product >> 32);
			middle = (uint)product;
			high = high * 10 + (uint)(product >> 32);
		}
		return new decimal((int)low, (int)middle, (int)high, number.IsNegative,
			(byte)(number.DigitsCount - number.Scale));
	}
}
