/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
// Scalar decoder control flow adapted from dotnet/runtime v10.0.9.
// Copyright (c) .NET Foundation and Contributors. Licensed under the MIT license.
 */

using System.Buffers;

namespace CopperSharp.Runtime;

/// <summary>Scalar Base64 implementation for the target's byte and char spans.</summary>
public static class ShadowBase64
{
	private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";

	public static int GetMaxEncodedToUtf8Length(int length)
	{
		if ((uint)length > 1610612733u) throw new ArgumentOutOfRangeException(nameof(length));
		return ((length + 2) / 3) * 4;
	}

	public static int GetMaxDecodedFromUtf8Length(int length)
	{
		if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
		return (length >> 2) * 3;
	}

	public static OperationStatus EncodeToUtf8(ReadOnlySpan<byte> bytes, Span<byte> utf8,
		out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
	{
		bytesConsumed = 0;
		bytesWritten = 0;
		while (bytes.Length - bytesConsumed >= 3 && utf8.Length - bytesWritten >= 4)
		{
			var value = (bytes[bytesConsumed] << 16) | (bytes[bytesConsumed + 1] << 8) | bytes[bytesConsumed + 2];
			utf8[bytesWritten] = (byte)Alphabet[(value >> 18) & 63];
			utf8[bytesWritten + 1] = (byte)Alphabet[(value >> 12) & 63];
			utf8[bytesWritten + 2] = (byte)Alphabet[(value >> 6) & 63];
			utf8[bytesWritten + 3] = (byte)Alphabet[value & 63];
			bytesConsumed += 3;
			bytesWritten += 4;
		}
		var remain = bytes.Length - bytesConsumed;
		if (remain >= 3) return OperationStatus.DestinationTooSmall;
		if (remain == 0) return OperationStatus.Done;
		if (!isFinalBlock) return utf8.Length - bytesWritten < 4 ? OperationStatus.DestinationTooSmall : OperationStatus.NeedMoreData;
		if (utf8.Length - bytesWritten < 4) return OperationStatus.DestinationTooSmall;
		var last = bytes[bytesConsumed] << 16;
		if (remain == 2) last |= bytes[bytesConsumed + 1] << 8;
		utf8[bytesWritten++] = (byte)Alphabet[(last >> 18) & 63];
		utf8[bytesWritten++] = (byte)Alphabet[(last >> 12) & 63];
		utf8[bytesWritten++] = remain == 2 ? (byte)Alphabet[(last >> 6) & 63] : (byte)'=';
		utf8[bytesWritten++] = (byte)'=';
		bytesConsumed += remain;
		return OperationStatus.Done;
	}

	public static OperationStatus EncodeToUtf8InPlace(Span<byte> buffer, int dataLength, out int bytesWritten)
	{
		if ((uint)dataLength > (uint)buffer.Length) throw new ArgumentOutOfRangeException(nameof(dataLength));
		bytesWritten = 0;
		var required = ((long)dataLength + 2) / 3 * 4;
		if (required > buffer.Length) return OperationStatus.DestinationTooSmall;
		var read = dataLength;
		var write = (int)required;
		while (read > 0)
		{
			var count = read % 3;
			if (count == 0) count = 3;
			read -= count;
			write -= 4;
			var value = buffer[read] << 16;
			if (count > 1) value |= buffer[read + 1] << 8;
			if (count > 2) value |= buffer[read + 2];
			buffer[write] = (byte)Alphabet[(value >> 18) & 63];
			buffer[write + 1] = (byte)Alphabet[(value >> 12) & 63];
			buffer[write + 2] = count > 1 ? (byte)Alphabet[(value >> 6) & 63] : (byte)'=';
			buffer[write + 3] = count > 2 ? (byte)Alphabet[value & 63] : (byte)'=';
		}
		bytesWritten = (int)required;
		return OperationStatus.Done;
	}

	public static OperationStatus DecodeFromUtf8(ReadOnlySpan<byte> utf8, Span<byte> bytes,
		out int bytesConsumed, out int bytesWritten, bool isFinalBlock = true)
		=> DecodeCore(utf8, bytes, out bytesConsumed, out bytesWritten, isFinalBlock, true);

	private static OperationStatus DecodeCore(ReadOnlySpan<byte> source, Span<byte> destination,
		out int consumed, out int written, bool final, bool ignoreWhiteSpace)
	{
		consumed = 0;
		written = 0;
		if (source.Length == 0) return OperationStatus.Done;
		var sourceLength = source.Length & ~3;
		var decodedLength = sourceLength / 4 * 3;
		var skipLast = final ? 4 : 0;
		var maxSource = destination.Length >= decodedLength ? sourceLength - skipLast : destination.Length / 3 * 4;
		while (consumed < maxSource)
		{
			var a = Decode(source[consumed]);
			var b = Decode(source[consumed + 1]);
			var c = Decode(source[consumed + 2]);
			var d = Decode(source[consumed + 3]);
			if ((a | b | c | d) < 0) return InvalidStatus(source, destination, ref consumed, ref written, final, ignoreWhiteSpace);
			destination[written++] = (byte)((a << 2) | (b >> 4));
			destination[written++] = (byte)((b << 4) | (c >> 2));
			destination[written++] = (byte)((c << 6) | d);
			consumed += 4;
		}
		if (maxSource != sourceLength - skipLast)
			return sourceLength != source.Length && final ? InvalidStatus(source, destination, ref consumed, ref written, final, ignoreWhiteSpace) : OperationStatus.DestinationTooSmall;
		if (consumed == sourceLength)
		{
			if (final) return InvalidStatus(source, destination, ref consumed, ref written, final, ignoreWhiteSpace);
			return consumed == source.Length ? OperationStatus.Done : OperationStatus.NeedMoreData;
		}
		var v0 = Decode(source[consumed]);
		var v1 = Decode(source[consumed + 1]);
		var v2 = source[consumed + 2] == '=' ? -2 : Decode(source[consumed + 2]);
		var v3 = source[consumed + 3] == '=' ? -2 : Decode(source[consumed + 3]);
		if (v0 < 0 || v1 < 0 || v2 == -1 || v3 == -1 || v2 == -2 && v3 != -2 ||
			v2 == -2 && (v1 & 15) != 0 || v3 == -2 && v2 >= 0 && (v2 & 3) != 0) return InvalidStatus(source, destination, ref consumed, ref written, final, ignoreWhiteSpace);
		var count = v2 == -2 ? 1 : v3 == -2 ? 2 : 3;
		if (destination.Length - written < count)
			return sourceLength != source.Length ? InvalidStatus(source, destination, ref consumed, ref written, final, ignoreWhiteSpace) : OperationStatus.DestinationTooSmall;
		destination[written++] = (byte)((v0 << 2) | (v1 >> 4));
		if (count > 1) destination[written++] = (byte)((v1 << 4) | (v2 >> 2));
		if (count > 2) destination[written++] = (byte)((v2 << 6) | v3);
		consumed += 4;
		return sourceLength != source.Length ? InvalidStatus(source, destination, ref consumed, ref written, final, ignoreWhiteSpace) : OperationStatus.Done;
	}

	private static OperationStatus InvalidStatus(ReadOnlySpan<byte> source, Span<byte> destination,
		ref int consumed, ref int written, bool final, bool ignoreWhiteSpace) =>
		ignoreWhiteSpace ? DecodeWhitespaceFallback(source, destination, ref consumed, ref written, final) : OperationStatus.InvalidData;

	private static OperationStatus DecodeWhitespaceFallback(ReadOnlySpan<byte> source, Span<byte> destination,
		ref int consumed, ref int written, bool final)
	{
		var remaining = source.Slice(consumed);
		var output = destination.Slice(written);
		while (true)
		{
			var white = 0;
			while (white < remaining.Length && IsWhitespace(remaining[white])) white++;
			if (white == remaining.Length) { consumed += white; return OperationStatus.Done; }
			if (white == 0) return DecodeWhitespaceBlockwise(remaining, output, ref consumed, ref written, final);
			consumed += white;
			remaining = remaining.Slice(white);
			var status = DecodeCore(remaining, output, out var localConsumed, out var localWritten, final, false);
			consumed += localConsumed;
			written += localWritten;
			if (status != OperationStatus.InvalidData) return status;
			remaining = remaining.Slice(localConsumed);
			output = output.Slice(localWritten);
			if (remaining.Length == 0) return status;
		}
	}

	private static OperationStatus DecodeWhitespaceBlockwise(ReadOnlySpan<byte> source, Span<byte> output,
		ref int consumed, ref int written, bool final)
	{
		Span<byte> quartet = stackalloc byte[4];
		while (source.Length > 0)
		{
			var read = 0;
			var count = 0;
			var skipped = 0;
			while (read < source.Length && count < 4)
			{
				if (IsWhitespace(source[read])) skipped++;
				else quartet[count++] = source[read];
				read++;
			}
			source = source.Slice(read);
			consumed += skipped;
			if (count == 0) continue;
			var another = source.Length >= 4;
			var localFinal = !another;
			if (another && (quartet[count - 1] == '=' || count > 1 && quartet[count - 2] == '='))
			{
				another = false;
				localFinal = true;
			}
			if (localFinal && !final) localFinal = false;
			var status = DecodeCore(quartet.Slice(0, count), output, out var localConsumed, out var localWritten, localFinal, false);
			consumed += localConsumed;
			written += localWritten;
			if (status != OperationStatus.Done) return status;
			if (!another)
			{
				for (var i = 0; i < source.Length; i++)
				{
					if (!IsWhitespace(source[i])) { consumed -= localConsumed; written -= localWritten; return OperationStatus.InvalidData; }
					consumed++;
				}
				return OperationStatus.Done;
			}
			output = output.Slice(localWritten);
		}
		return OperationStatus.Done;
	}

	public static OperationStatus DecodeFromUtf8InPlace(Span<byte> buffer, out int bytesWritten)
	{
		bytesWritten = 0;
		if (buffer.Length == 0) return OperationStatus.Done;
		var source = 0;
		if ((buffer.Length & 3) == 0)
		{
			while (source < buffer.Length - 4)
			{
				var a = Decode(buffer[source]);
				var b = Decode(buffer[source + 1]);
				var c = Decode(buffer[source + 2]);
				var d = Decode(buffer[source + 3]);
				if ((a | b | c | d) < 0) break;
				buffer[bytesWritten++] = (byte)((a << 2) | (b >> 4));
				buffer[bytesWritten++] = (byte)((b << 4) | (c >> 2));
				buffer[bytesWritten++] = (byte)((c << 6) | d);
				source += 4;
			}
			if (source == buffer.Length - 4)
			{
				var status = DecodeCore(buffer.Slice(source, 4), buffer.Slice(bytesWritten), out _, out var last, true, false);
				if (status == OperationStatus.Done) { bytesWritten += last; return status; }
			}
		}
		return DecodeInPlaceWhitespace(buffer, source, ref bytesWritten);
	}

	private static OperationStatus DecodeInPlaceWhitespace(Span<byte> source, int sourceIndex, ref int destinationIndex)
	{
		var blockSize = source.Length - sourceIndex;
		if (blockSize > 4) blockSize = 4;
		Span<byte> block = stackalloc byte[4];
		var lastWritten = 0;
		var paddingProcessed = false;
		while (sourceIndex < source.Length)
		{
			var count = 0;
			while (count < blockSize && sourceIndex < source.Length)
			{
				var value = source[sourceIndex++];
				if (!IsWhitespace(value)) block[count++] = value;
			}
			if (count == 0) continue;
			if (count != 4) return OperationStatus.InvalidData;
			if (paddingProcessed) { destinationIndex -= lastWritten; return OperationStatus.InvalidData; }
			var status = DecodeCore(block, block, out _, out lastWritten, true, false);
			destinationIndex += lastWritten;
			paddingProcessed = lastWritten < 3;
			if (status != OperationStatus.Done) return status;
			for (var i = 0; i < lastWritten; i++) source[destinationIndex - lastWritten + i] = block[i];
		}
		return OperationStatus.Done;
	}

	public static bool IsValid(ReadOnlySpan<byte> text) => IsValid(text, out _);
	public static bool IsValid(ReadOnlySpan<char> text) => IsValid(text, out _);

	public static bool IsValid(ReadOnlySpan<byte> text, out int decodedLength)
	{
		decodedLength = 0;
		var count = 0;
		var pad = 0;
		for (var i = 0; i < text.Length; i++)
		{
			var ch = text[i];
			if (IsWhitespace(ch)) continue;
			if (ch == '=') { if (count % 4 < 2) { decodedLength = 0; return false; } pad++; }
			else { if (Decode(ch) < 0 || pad != 0) { decodedLength = 0; return false; } }
			count++;
		}
		if (count % 4 != 0 || pad > 2) { decodedLength = 0; return false; }
		if (!ValidPaddingBits(text)) { decodedLength = 0; return false; }
		decodedLength = count / 4 * 3 - pad;
		return true;
	}

	public static bool IsValid(ReadOnlySpan<char> text, out int decodedLength)
	{
		decodedLength = 0;
		var count = 0;
		var pad = 0;
		for (var i = 0; i < text.Length; i++)
		{
			var ch = text[i];
			if (IsWhitespace(ch)) continue;
			if (ch == '=') { if (count % 4 < 2) { decodedLength = 0; return false; } pad++; }
			else { if (Decode(ch) < 0 || pad != 0) { decodedLength = 0; return false; } }
			count++;
		}
		if (count % 4 != 0 || pad > 2) { decodedLength = 0; return false; }
		if (!ValidPaddingBits(text)) { decodedLength = 0; return false; }
		decodedLength = count / 4 * 3 - pad;
		return true;
	}

	internal static bool IsWhitespace(int ch) => ch is ' ' or '\t' or '\r' or '\n';
	private static bool ValidPaddingBits(ReadOnlySpan<byte> text)
	{
		var last = 0;
		var previous = 0;
		for (var i = 0; i < text.Length; i++)
			if (!IsWhitespace(text[i])) { previous = last; last = text[i]; }
		if (last != '=') return true;
		if (previous == '=')
		{
			var seen = 0;
			for (var i = text.Length - 1; i >= 0; i--)
				if (!IsWhitespace(text[i]) && ++seen == 3) return (Decode(text[i]) & 15) == 0;
			return false;
		}
		return (Decode(previous) & 3) == 0;
	}
	private static bool ValidPaddingBits(ReadOnlySpan<char> text)
	{
		var last = 0;
		var previous = 0;
		for (var i = 0; i < text.Length; i++)
			if (!IsWhitespace(text[i])) { previous = last; last = text[i]; }
		if (last != '=') return true;
		if (previous == '=')
		{
			var seen = 0;
			for (var i = text.Length - 1; i >= 0; i--)
				if (!IsWhitespace(text[i]) && ++seen == 3) return (Decode(text[i]) & 15) == 0;
			return false;
		}
		return (Decode(previous) & 3) == 0;
	}
	internal static int Decode(int ch) => ch >= 'A' && ch <= 'Z' ? ch - 'A' :
		ch >= 'a' && ch <= 'z' ? ch - 'a' + 26 :
		ch >= '0' && ch <= '9' ? ch - '0' + 52 : ch == '+' ? 62 : ch == '/' ? 63 : -1;
	internal static char Encode(int value) => Alphabet[value & 63];
}
