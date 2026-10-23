/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using CopperSharp.Compiler;

namespace CopperSharp.Runtime;

/// <summary>Scalar hexadecimal codecs for the admitted System.Convert surface.</summary>
public static class ShadowConvertHex
{
	public static string ToHexString(byte[] inArray) => EncodeArray(inArray, false);
	public static string ToHexString(byte[] inArray, int offset, int length) => EncodeArraySlice(inArray, offset, length, false);
	public static string ToHexString(ReadOnlySpan<byte> bytes) => Encode(bytes, false);
	public static string ToHexStringLower(byte[] inArray) => EncodeArray(inArray, true);
	public static string ToHexStringLower(byte[] inArray, int offset, int length) => EncodeArraySlice(inArray, offset, length, true);
	public static string ToHexStringLower(ReadOnlySpan<byte> bytes) => Encode(bytes, true);

	public static bool TryToHexString(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten) =>
		TryEncodeChars(source, destination, out charsWritten, false);
	public static bool TryToHexStringLower(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten) =>
		TryEncodeChars(source, destination, out charsWritten, true);
	public static bool TryToHexString(ReadOnlySpan<byte> source, Span<byte> utf8Destination, out int bytesWritten) =>
		TryEncodeUtf8(source, utf8Destination, out bytesWritten, false);
	public static bool TryToHexStringLower(ReadOnlySpan<byte> source, Span<byte> utf8Destination, out int bytesWritten) =>
		TryEncodeUtf8(source, utf8Destination, out bytesWritten, true);

	public static byte[] FromHexString(string s)
	{
		if (s is null) throw new ArgumentNullException(nameof(s));
		return FromHexString(s.AsSpan());
	}

	public static byte[] FromHexString(ReadOnlySpan<char> chars)
	{
		if ((chars.Length & 1) != 0) M68kRuntime.ThrowFormatException();
		var result = new byte[chars.Length / 2];
		if (DecodeChars(chars, result, out _) != chars.Length) M68kRuntime.ThrowFormatException();
		return result;
	}

	public static byte[] FromHexString(ReadOnlySpan<byte> utf8Source)
	{
		if ((utf8Source.Length & 1) != 0) M68kRuntime.ThrowFormatException();
		var result = new byte[utf8Source.Length / 2];
		if (DecodeUtf8(utf8Source, result, out _) != utf8Source.Length) M68kRuntime.ThrowFormatException();
		return result;
	}

	public static OperationStatus FromHexString(string source, Span<byte> destination, out int charsConsumed, out int bytesWritten)
	{
		if (source is null) throw new ArgumentNullException(nameof(source));
		return FromHexString(source.AsSpan(), destination, out charsConsumed, out bytesWritten);
	}

	public static OperationStatus FromHexString(ReadOnlySpan<char> source, Span<byte> destination, out int charsConsumed, out int bytesWritten)
	{
		var quotient = source.Length / 2;
		if (quotient == 0)
		{
			charsConsumed = bytesWritten = 0;
			return (source.Length & 1) != 0 ? OperationStatus.NeedMoreData : OperationStatus.Done;
		}
		var status = OperationStatus.Done;
		if (destination.Length < quotient)
		{
			quotient = destination.Length;
			status = OperationStatus.DestinationTooSmall;
		}
		else if ((source.Length & 1) != 0) status = OperationStatus.NeedMoreData;
		charsConsumed = DecodeChars(source.Slice(0, quotient * 2), destination, out bytesWritten);
		return charsConsumed != quotient * 2 ? OperationStatus.InvalidData : status;
	}

	public static OperationStatus FromHexString(ReadOnlySpan<byte> utf8Source, Span<byte> destination, out int bytesConsumed, out int bytesWritten)
	{
		var quotient = utf8Source.Length / 2;
		if (quotient == 0)
		{
			bytesConsumed = bytesWritten = 0;
			return (utf8Source.Length & 1) != 0 ? OperationStatus.NeedMoreData : OperationStatus.Done;
		}
		var status = OperationStatus.Done;
		if (destination.Length < quotient)
		{
			quotient = destination.Length;
			status = OperationStatus.DestinationTooSmall;
		}
		else if ((utf8Source.Length & 1) != 0) status = OperationStatus.NeedMoreData;
		bytesConsumed = DecodeUtf8(utf8Source.Slice(0, quotient * 2), destination, out bytesWritten);
		return bytesConsumed != quotient * 2 ? OperationStatus.InvalidData : status;
	}

	private static string EncodeArray(byte[] inArray, bool lower)
	{
		if (inArray is null) throw new ArgumentNullException(nameof(inArray));
		return Encode(inArray, lower);
	}

	private static string EncodeArraySlice(byte[] inArray, int offset, int length, bool lower)
	{
		if (inArray is null) throw new ArgumentNullException(nameof(inArray));
		if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
		if (offset < 0 || offset > inArray.Length - length) throw new ArgumentOutOfRangeException(nameof(offset));
		return Encode(((Span<byte>)inArray).Slice(offset, length), lower);
	}

	private static string Encode(ReadOnlySpan<byte> bytes, bool lower)
	{
		if (bytes.Length == 0) return string.Empty;
		if (bytes.Length > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(bytes));
		var buffer = new char[bytes.Length * 2];
		TryEncodeChars(bytes, buffer, out _, lower);
		return CreateString(buffer);
	}

	private static string CreateString(ReadOnlySpan<char> value) => new string(value);

	private static bool TryEncodeChars(ReadOnlySpan<byte> source, Span<char> destination, out int charsWritten, bool lower)
	{
		charsWritten = 0;
		if (source.Length > int.MaxValue / 2 || destination.Length < source.Length * 2) return false;
		for (var index = 0; index < source.Length; index++)
		{
			destination[index * 2] = Digit(source[index] >> 4, lower);
			destination[index * 2 + 1] = Digit(source[index] & 15, lower);
		}
		charsWritten = source.Length * 2;
		return true;
	}

	private static bool TryEncodeUtf8(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten, bool lower)
	{
		bytesWritten = 0;
		if (source.Length > int.MaxValue / 2 || destination.Length < source.Length * 2) return false;
		for (var index = 0; index < source.Length; index++)
		{
			destination[index * 2] = (byte)Digit(source[index] >> 4, lower);
			destination[index * 2 + 1] = (byte)Digit(source[index] & 15, lower);
		}
		bytesWritten = source.Length * 2;
		return true;
	}

	private static char Digit(int digit, bool lower) => (char)(digit < 10 ? '0' + digit : (lower ? 'a' : 'A') + digit - 10);

	private static int DecodeChars(ReadOnlySpan<char> source, Span<byte> destination, out int written)
	{
		written = 0;
		for (var index = 0; index < source.Length; index += 2)
		{
			var high = Value(source[index]);
			var low = Value(source[index + 1]);
			if (low < 0) return index + 1;
			if (high < 0) return index;
			destination[written++] = (byte)((high << 4) | low);
		}
		return source.Length;
	}

	private static int DecodeUtf8(ReadOnlySpan<byte> source, Span<byte> destination, out int written)
	{
		written = 0;
		for (var index = 0; index < source.Length; index += 2)
		{
			var high = Value(source[index]);
			var low = Value(source[index + 1]);
			if (low < 0) return index + 1;
			if (high < 0) return index;
			destination[written++] = (byte)((high << 4) | low);
		}
		return source.Length;
	}

	private static int Value(int value)
	{
		if (value >= '0' && value <= '9') return value - '0';
		if (value >= 'A' && value <= 'F') return value - 'A' + 10;
		if (value >= 'a' && value <= 'f') return value - 'a' + 10;
		return -1;
	}
}
