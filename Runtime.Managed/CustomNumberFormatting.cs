/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Runtime.CompilerServices;

namespace CopperSharp.Runtime;

/// <summary>Integral custom-format adapters for the verified CoreLib renderer.</summary>
public static class ShadowCustomNumberFormatting
{
	internal static NumberFormatInfo ResolveInfo(IFormatProvider? provider) =>
		ShadowStandardIntegerFormatting.NumberInfo(provider);

	public static unsafe void WriteTwoDigits(uint value, char* destination)
	{
		var tens = value / 10;
		destination[0] = (char)('0' + tens);
		destination[1] = (char)('0' + value - tens * 10);
	}

	public static string FormatInt32(int value, int hexMask, string? format, NumberFormatInfo info) => FormatInt64(value, format, info);
	public static string FormatUInt32(uint value, string? format, NumberFormatInfo info) => FormatUInt64(value, format, info);
	public static string FormatInt64(long value, string? format, NumberFormatInfo info) =>
		FormatMagnitude(value < 0 ? unchecked(0UL - (ulong)value) : (ulong)value, value < 0, format.AsSpan(), info);
	public static string FormatUInt64(ulong value, string? format, NumberFormatInfo info) => FormatMagnitude(value, false, format.AsSpan(), info);
	public static bool TryFormatInt32(int value, int hexMask, ReadOnlySpan<char> format, NumberFormatInfo info, Span<char> destination, out int charsWritten) =>
		TryFormatInt64(value, format, info, destination, out charsWritten);
	public static bool TryFormatUInt32(uint value, ReadOnlySpan<char> format, NumberFormatInfo info, Span<char> destination, out int charsWritten) =>
		TryFormatUInt64(value, format, info, destination, out charsWritten);
	public static bool TryFormatInt64(long value, ReadOnlySpan<char> format, NumberFormatInfo info, Span<char> destination, out int charsWritten) =>
		TryFormatMagnitude(value < 0 ? unchecked(0UL - (ulong)value) : (ulong)value, value < 0, format, info, destination, out charsWritten);
	public static bool TryFormatUInt64(ulong value, ReadOnlySpan<char> format, NumberFormatInfo info, Span<char> destination, out int charsWritten) =>
		TryFormatMagnitude(value, false, format, info, destination, out charsWritten);

	private static string FormatMagnitude(ulong value, bool negative, ReadOnlySpan<char> format, NumberFormatInfo info)
	{
		ArgumentNullException.ThrowIfNull(info);
		ValidateCustomFormat(format);
		Span<byte> digits = stackalloc byte[21];
		var number = CreateNumber(value, negative, digits);
		Span<char> scratch = stackalloc char[32];
		var builder = new ShadowValueListBuilder<char>(scratch);
		Render(ref builder, ref number, format, info);
		var result = new string(builder.AsSpan());
		builder.Dispose();
		return result;
	}

	private static bool TryFormatMagnitude(ulong value, bool negative, ReadOnlySpan<char> format, NumberFormatInfo info, Span<char> destination, out int charsWritten)
	{
		ArgumentNullException.ThrowIfNull(info);
		ValidateCustomFormat(format);
		Span<byte> digits = stackalloc byte[21];
		var number = CreateNumber(value, negative, digits);
		Span<char> scratch = stackalloc char[32];
		var builder = new ShadowValueListBuilder<char>(scratch);
		Render(ref builder, ref number, format, info);
		var text = builder.AsSpan();
		var result = text.TryCopyTo(destination);
		charsWritten = result ? text.Length : 0;
		builder.Dispose();
		return result;
	}

	private static void ValidateCustomFormat(ReadOnlySpan<char> format)
	{
		if (format.Length != 0 && format[0] != '\0')
		{
			char first = format[0];
			if (!(first >= 'A' && first <= 'Z' || first >= 'a' && first <= 'z')) return;
			int precision = 0;
			for (int index = 1; index < format.Length && format[index] != '\0'; index++)
			{
				int digit = format[index] - '0';
				if ((uint)digit > 9) return;
				if (precision > 99_999_999) throw new FormatException();
				precision = precision * 10 + digit;
			}
		}
		throw new ArgumentException("Expected a custom numeric format.", "format");
	}

	private static ShadowNumberBuffer CreateNumber(ulong value, bool negative, Span<byte> digits)
	{
		var low = CopperSharp.Compiler.M68kRuntime.SplitInt64(unchecked((long)value), out var high);
		var count = ShadowIntegerFormatter.PackUInt64(high, low, out var word0, out var word1, out var word2, out var word3, out var word4);
		// Zero has an empty digit sequence in CoreLib's NumberBuffer.
		if (value == 0) count = 0;
		for (int index = 0; index < count; index++)
			digits[index] = (byte)ShadowStandardIntegerFormatting.PackedDigit(index, word0, word1, word2, word3, word4);
		digits[count] = 0;
		return new ShadowNumberBuffer { DigitsCount = count, Scale = count, IsNegative = negative, Kind = ShadowNumberBufferKind.Integer, Digits = digits };
	}

	// The target executes Number.NumberToStringFormat<char>. The host body gives
	// the same input to the CLR, allowing the adapters to be checked independently.
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Render(ref ShadowValueListBuilder<char> builder, ref ShadowNumberBuffer number, ReadOnlySpan<char> format, NumberFormatInfo info)
	{
		ulong magnitude = 0;
		for (int index = 0; index < number.DigitsCount; index++) magnitude = magnitude * 10 + (uint)(number.Digits[index] - '0');
		string result = number.IsNegative ? unchecked(-(long)magnitude).ToString(format.ToString(), info) : magnitude.ToString(format.ToString(), info);
		for (int index = 0; index < result.Length; index++) builder.Append(result[index]);
	}
}

/// <summary>Field-compatible view of the pinned CoreLib integral digit buffer.</summary>
public ref struct ShadowNumberBuffer
{
	public int DigitsCount;
	public int Scale;
	public bool IsNegative;
	public bool HasNonZeroTail;
	public ShadowNumberBufferKind Kind;
	public Span<byte> Digits;
}

public enum ShadowNumberBufferKind : byte { Unknown, Integer, Decimal, FloatingPoint }

/// <summary>UTF-16 numeric settings without the unused UTF-8 conversion path.</summary>
public static class ShadowNumberFormatCharacters
{
	public static ReadOnlySpan<char> NegativeSignTChar(NumberFormatInfo info) => info.NegativeSign.AsSpan();
	public static ReadOnlySpan<char> PositiveSignTChar(NumberFormatInfo info) => info.PositiveSign.AsSpan();
	public static ReadOnlySpan<char> NumberDecimalSeparatorTChar(NumberFormatInfo info) => info.NumberDecimalSeparator.AsSpan();
	public static ReadOnlySpan<char> NumberGroupSeparatorTChar(NumberFormatInfo info) => info.NumberGroupSeparator.AsSpan();
	public static ReadOnlySpan<char> PercentSymbolTChar(NumberFormatInfo info) => info.PercentSymbol.AsSpan();
	public static ReadOnlySpan<char> PerMilleSymbolTChar(NumberFormatInfo info) => info.PerMilleSymbol.AsSpan();
	public static ReadOnlySpan<char> CurrencySymbolTChar(NumberFormatInfo info) => info.CurrencySymbol.AsSpan();
	public static ReadOnlySpan<char> CurrencyDecimalSeparatorTChar(NumberFormatInfo info) => info.CurrencyDecimalSeparator.AsSpan();
	public static ReadOnlySpan<char> CurrencyGroupSeparatorTChar(NumberFormatInfo info) => info.CurrencyGroupSeparator.AsSpan();
	public static ReadOnlySpan<char> PercentDecimalSeparatorTChar(NumberFormatInfo info) => info.PercentDecimalSeparator.AsSpan();
	public static ReadOnlySpan<char> PercentGroupSeparatorTChar(NumberFormatInfo info) => info.PercentGroupSeparator.AsSpan();
}
