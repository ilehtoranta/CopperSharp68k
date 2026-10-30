/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

public unsafe partial struct ShadowGuid
{
	public static Guid Parse(string input)
	{
		if (input is null) M68kRuntime.ThrowArgumentNullException();
		if (!TryParse(input, out var result)) M68kRuntime.ThrowFormatException();
		return result;
	}

	public static bool TryParse(string? input, out Guid result)
	{
		result = default;
		if (input is null || !TryParseCore(input.AsSpan(), '\0', out var parsed)) return false;
		result = ToGuid(parsed);
		return true;
	}

	public static Guid ParseExact(string input, string format)
	{
		if (input is null || format is null) M68kRuntime.ThrowArgumentNullException();
		if (!TryParseExact(input, format, out var result)) M68kRuntime.ThrowFormatException();
		return result;
	}

	public static bool TryParseExact(string? input, string? format, out Guid result)
	{
		result = default;
		if (input is null || format is null || format.Length != 1 ||
			!TryParseCore(input.AsSpan(), (char)(format[0] | 0x20), out var parsed)) return false;
		result = ToGuid(parsed);
		return true;
	}

	private static bool TryParseCore(ReadOnlySpan<char> text, char format, out ShadowGuid result)
	{
		result = default;
		var start = 0;
		var end = text.Length;
		while (start < end && IsWhiteSpace(text[start])) start++;
		while (end > start && IsWhiteSpace(text[end - 1])) end--;
		text = text.Slice(start, end - start);
		if (text.Length < 32) return false;
		if (format == '\0') format = text[0] == '(' ? 'p' : text[0] == '{' ? text[9] == '-' ? 'b' : 'x' : text[8] == '-' ? 'd' : 'n';
		if (format == 'x') return TryReadX(text, out result);
		if (format is 'b' or 'p')
		{
			if (text.Length != 38 || text[0] != (format == 'b' ? '{' : '(') || text[37] != (format == 'b' ? '}' : ')')) return false;
			text = text.Slice(1, 36);
			format = 'd';
		}
		if (format == 'd')
		{
			if (text.Length != 36 || text[8] != '-' || text[13] != '-' || text[18] != '-' || text[23] != '-') return false;
			// The released D parser also accepts legacy +/0x prefixes within
			// fixed-width components, except in its final eight hex digits.
			if (!TryReadHex(text.Slice(0, 8), true, out var a) || !TryReadHex(text.Slice(9, 4), true, out var b) ||
				!TryReadHex(text.Slice(14, 4), true, out var c) || !TryReadHex(text.Slice(19, 4), true, out var de) ||
				!TryReadHex(text.Slice(24, 4), true, out var fg) || !TryReadHex(text.Slice(28, 8), false, out var hijk)) return false;
			result = new ShadowGuid((int)a, (short)b, (short)c, (byte)(de >> 8), (byte)de, (byte)(fg >> 8), (byte)fg,
				(byte)(hijk >> 24), (byte)(hijk >> 16), (byte)(hijk >> 8), (byte)hijk);
			return true;
		}
		if (format != 'n' || text.Length != 32) return false;
		Span<byte> bytes = stackalloc byte[16];
		for (var index = 0; index < 16; index++)
		{
			var high = HexDigit(text[index * 2]);
			var low = HexDigit(text[index * 2 + 1]);
			if (high < 0 || low < 0) return false;
			bytes[index] = (byte)((high << 4) | low);
		}
		result = new ShadowGuid(bytes, true);
		return true;
	}

	private static bool TryReadHex(ReadOnlySpan<char> text, bool compatible, out uint number)
	{
		number = 0;
		if (compatible)
		{
			if (text.Length > 0 && text[0] == '+') text = text.Slice(1);
			if (text.Length >= 2 && text[0] == '0' && (text[1] | 0x20) == 'x') text = text.Slice(2);
		}
		var significantDigits = 0;
		for (var index = 0; index < text.Length; index++)
		{
			var digit = HexDigit(text[index]);
			if (digit < 0) return false;
			if (digit != 0 || significantDigits != 0) significantDigits++;
			if (significantDigits > 8) return false;
			number = (number << 4) | (uint)digit;
		}
		return true;
	}

	private static bool TryReadX(ReadOnlySpan<char> text, out ShadowGuid result)
	{
		result = default;
		var cursor = 0;
		if (ReadNonWhite(text, ref cursor) != '{') return false;
		var parsed = default(ShadowGuid);
		for (var component = 0; component < 11; component++)
		{
			if (component == 3 && ReadNonWhite(text, ref cursor) != '{') return false;
			if (!TryReadXNumber(text, ref cursor, component == 10 ? '}' : ',', out var number)) return false;
			if (component > 2 && number > 255) return false;
			switch (component)
			{
				case 0: parsed._a = (int)number; break;
				case 1: parsed._b = (short)number; break;
				case 2: parsed._c = (short)number; break;
				case 3: parsed._d = (byte)number; break;
				case 4: parsed._e = (byte)number; break;
				case 5: parsed._f = (byte)number; break;
				case 6: parsed._g = (byte)number; break;
				case 7: parsed._h = (byte)number; break;
				case 8: parsed._i = (byte)number; break;
				case 9: parsed._j = (byte)number; break;
				case 10: parsed._k = (byte)number; break;
			}
		}
		if (ReadNonWhite(text, ref cursor) != '}') return false;
		while (cursor < text.Length && IsWhiteSpace(text[cursor])) cursor++;
		if (cursor != text.Length) return false;
		result = parsed;
		return true;
	}

	private static bool TryReadXNumber(ReadOnlySpan<char> text, ref int cursor, char delimiter, out uint number)
	{
		number = 0;
		if (ReadNonWhite(text, ref cursor) != '0' || (ReadNonWhite(text, ref cursor) | 0x20) != 'x') return false;
		var character = ReadNonWhite(text, ref cursor);
		if (character == delimiter || character < 0) return false;
		if (character == '+') character = ReadNonWhite(text, ref cursor);
		if (character == '0')
		{
			var saved = cursor;
			var next = ReadNonWhite(text, ref cursor);
			if ((next | 0x20) == 'x') character = ReadNonWhite(text, ref cursor);
			else cursor = saved;
		}
		var significantDigits = 0;
		while (character != delimiter)
		{
			var digit = HexDigit(character);
			if (digit < 0) return false;
			if (digit != 0 || significantDigits != 0) significantDigits++;
			if (significantDigits > 8) return false;
			number = (number << 4) | (uint)digit;
			character = ReadNonWhite(text, ref cursor);
		}
		return true;
	}

	private static int ReadNonWhite(ReadOnlySpan<char> text, ref int cursor)
	{
		while (cursor < text.Length && IsWhiteSpace(text[cursor])) cursor++;
		return cursor < text.Length ? text[cursor++] : -1;
	}

	private static int HexDigit(int character) => character is >= '0' and <= '9' ? character - '0' :
		(character | 0x20) is >= 'a' and <= 'f' ? (character | 0x20) - 'a' + 10 : -1;

	private static bool IsWhiteSpace(char character) => character is >= '\t' and <= '\r' or ' ' or '\u0085' or '\u00a0' or
		'\u1680' or >= '\u2000' and <= '\u200a' or '\u2028' or '\u2029' or '\u202f' or '\u205f' or '\u3000';
}
