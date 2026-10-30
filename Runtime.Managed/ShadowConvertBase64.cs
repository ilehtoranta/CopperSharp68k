/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Runtime;

/// <summary>Deterministic Convert Base64 overloads backed by target-safe spans.</summary>
public static class ShadowConvertBase64
{
	public static string ToBase64String(byte[] inArray) => ToBase64String(inArray, System.Base64FormattingOptions.None);
	public static string ToBase64String(byte[] inArray, System.Base64FormattingOptions options)
	{
		if (inArray is null) throw new ArgumentNullException(nameof(inArray));
		return ToBase64String((ReadOnlySpan<byte>)inArray, options);
	}
	public static string ToBase64String(byte[] inArray, int offset, int length) =>
		ToBase64String(inArray, offset, length, System.Base64FormattingOptions.None);
	public static string ToBase64String(byte[] inArray, int offset, int length, System.Base64FormattingOptions options)
	{
		if (inArray is null) throw new ArgumentNullException(nameof(inArray));
		CheckRange(inArray.Length, offset, length);
		return ToBase64String(((ReadOnlySpan<byte>)inArray).Slice(offset, length), options);
	}
	public static string ToBase64String(ReadOnlySpan<byte> bytes, System.Base64FormattingOptions options = System.Base64FormattingOptions.None)
	{
		CheckOptions(options);
		if (bytes.Length == 0) return string.Empty;
		var length = Required(bytes.Length, options == System.Base64FormattingOptions.InsertLineBreaks);
		var buffer = new char[length];
		TryToBase64Chars(bytes, buffer, out _, options);
		return CreateString(buffer);
	}

	// The compiler admits the string span constructor only for this owned formatter.
	private static string CreateString(ReadOnlySpan<char> value) => new string(value);

	public static int ToBase64CharArray(byte[] inArray, int offsetIn, int length, char[] outArray, int offsetOut) =>
		ToBase64CharArray(inArray, offsetIn, length, outArray, offsetOut, System.Base64FormattingOptions.None);
	public static int ToBase64CharArray(byte[] inArray, int offsetIn, int length, char[] outArray, int offsetOut, System.Base64FormattingOptions options)
	{
		if (inArray is null) throw new ArgumentNullException(nameof(inArray));
		if (outArray is null) throw new ArgumentNullException(nameof(outArray));
		if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
		if (offsetIn < 0) throw new ArgumentOutOfRangeException(nameof(offsetIn));
		if (offsetOut < 0) throw new ArgumentOutOfRangeException(nameof(offsetOut));
		CheckOptions(options);
		if (offsetIn > inArray.Length - length) throw new ArgumentOutOfRangeException(nameof(offsetIn));
		if (length == 0) return 0;
		var required = Required(length, options == System.Base64FormattingOptions.InsertLineBreaks);
		if (offsetOut > outArray.Length - required) throw new ArgumentOutOfRangeException(nameof(offsetOut));
		TryToBase64Chars(((ReadOnlySpan<byte>)inArray).Slice(offsetIn, length), ((Span<char>)outArray).Slice(offsetOut), out var written, options);
		return written;
	}

	public static bool TryToBase64Chars(ReadOnlySpan<byte> bytes, Span<char> chars, out int charsWritten,
		System.Base64FormattingOptions options = System.Base64FormattingOptions.None)
	{
		CheckOptions(options);
		charsWritten = 0;
		if (bytes.Length == 0) return true;
		var lineBreaks = options == System.Base64FormattingOptions.InsertLineBreaks;
		var required = Required(bytes.Length, lineBreaks);
		if (chars.Length < required) return false;
		var position = 0;
		var linePosition = 0;
		for (var read = 0; read < bytes.Length; read += 3)
		{
			if (lineBreaks && linePosition == 76)
			{
				chars[position++] = '\r';
				chars[position++] = '\n';
				linePosition = 0;
			}
			var count = bytes.Length - read;
			var value = bytes[read] << 16;
			if (count > 1) value |= bytes[read + 1] << 8;
			if (count > 2) value |= bytes[read + 2];
			chars[position++] = ShadowBase64.Encode(value >> 18);
			chars[position++] = ShadowBase64.Encode(value >> 12);
			chars[position++] = count > 1 ? ShadowBase64.Encode(value >> 6) : '=';
			chars[position++] = count > 2 ? ShadowBase64.Encode(value) : '=';
			linePosition += 4;
		}
		charsWritten = position;
		return true;
	}

	public static byte[] FromBase64String(string s)
	{
		if (s is null) throw new ArgumentNullException(nameof(s));
		return FromChars(s.AsSpan());
	}
	public static byte[] FromBase64CharArray(char[] inArray, int offset, int length)
	{
		if (inArray is null) throw new ArgumentNullException(nameof(inArray));
		CheckRange(inArray.Length, offset, length);
		return FromChars(((ReadOnlySpan<char>)inArray).Slice(offset, length));
	}
	public static bool TryFromBase64String(string s, Span<byte> bytes, out int bytesWritten)
	{
		if (s is null) throw new ArgumentNullException(nameof(s));
		return TryFromBase64Chars(s.AsSpan(), bytes, out bytesWritten);
	}
	public static bool TryFromBase64Chars(ReadOnlySpan<char> chars, Span<byte> bytes, out int bytesWritten)
	{
		bytesWritten = 0;
		Span<int> quartet = stackalloc int[4];
		var count = 0;
		for (var read = 0; read < chars.Length; read++)
		{
			var ch = chars[read];
			if (ShadowBase64.IsWhitespace(ch)) continue;
			quartet[count++] = ch == '=' ? -2 : ShadowBase64.Decode(ch);
			if (count != 4) continue;
			if (quartet[0] < 0 || quartet[1] < 0 || quartet[2] == -1 || quartet[3] == -1 ||
				(quartet[2] == -2 && quartet[3] != -2))
			{ bytesWritten = 0; return false; }
			var decoded = quartet[2] == -2 ? 1 : quartet[3] == -2 ? 2 : 3;
			if (bytes.Length - bytesWritten < decoded) { bytesWritten = 0; return false; }
			bytes[bytesWritten++] = (byte)((quartet[0] << 2) | (quartet[1] >> 4));
			if (decoded > 1) bytes[bytesWritten++] = (byte)((quartet[1] << 4) | (quartet[2] >> 2));
			if (decoded > 2) bytes[bytesWritten++] = (byte)((quartet[2] << 6) | quartet[3]);
			if (decoded < 3)
			{
				for (var next = read + 1; next < chars.Length; next++)
					if (!ShadowBase64.IsWhitespace(chars[next])) { bytesWritten = 0; return false; }
				return true;
			}
			count = 0;
		}
		if (count == 0) return true;
		bytesWritten = 0;
		return false;
	}

	private static byte[] FromChars(ReadOnlySpan<char> chars)
	{
		var result = new byte[(chars.Length >> 2) * 3];
		if (!TryFromBase64Chars(chars, result, out var written)) throw new FormatException();
		if (result.Length != written)
		{
			var exact = new byte[written];
			for (var i = 0; i < written; i++) exact[i] = result[i];
			return exact;
		}
		return result;
	}

	private static void CheckRange(int size, int offset, int length)
	{
		if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
		if (offset < 0 || offset > size - length) throw new ArgumentOutOfRangeException(nameof(offset));
	}
	private static void CheckOptions(System.Base64FormattingOptions options)
	{
		if ((uint)options > 1u) throw new ArgumentException("Invalid Base64 formatting option.", nameof(options));
	}
	private static int Required(int length, bool lineBreaks)
	{
		var output = ((long)length + 2) / 3 * 4;
		if (lineBreaks && output > 0) output += ((output - 1) / 76) * 2;
		if (output > int.MaxValue) throw new OutOfMemoryException();
		return (int)output;
	}
}
