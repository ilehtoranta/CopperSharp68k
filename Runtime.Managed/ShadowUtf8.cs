/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;

namespace CopperSharp.Runtime;

/// <summary>Portable scalar implementation of the admitted Utf8 span operations.</summary>
public static class ShadowUtf8
{
	public static OperationStatus FromUtf16(ReadOnlySpan<char> source, Span<byte> destination,
		out int charsRead, out int bytesWritten, bool replaceInvalidSequences = true, bool isFinalBlock = true)
	{
		charsRead = 0;
		bytesWritten = 0;
		while (charsRead < source.Length)
		{
			var current = (uint)source[charsRead];
			var consumed = 1;
			if (current is >= 0xd800 and <= 0xdbff)
			{
				if (charsRead + 1 == source.Length && !isFinalBlock) return OperationStatus.NeedMoreData;
				if (charsRead + 1 < source.Length && source[charsRead + 1] is >= '\udc00' and <= '\udfff')
				{
					current = 0x10000u + ((current - 0xd800u) << 10) + (uint)(source[charsRead + 1] - 0xdc00);
					consumed = 2;
				}
				else if (!replaceInvalidSequences) return OperationStatus.InvalidData;
				else current = 0xfffd;
			}
			else if (current is >= 0xdc00 and <= 0xdfff)
			{
				if (!replaceInvalidSequences) return OperationStatus.InvalidData;
				current = 0xfffd;
			}
			var needed = current < 0x80 ? 1 : current < 0x800 ? 2 : current < 0x10000 ? 3 : 4;
			if (destination.Length - bytesWritten < needed) return OperationStatus.DestinationTooSmall;
			if (needed == 1) destination[bytesWritten] = (byte)current;
			else if (needed == 2)
			{
				destination[bytesWritten] = (byte)(0xc0u | (current >> 6));
				destination[bytesWritten + 1] = (byte)(0x80u | (current & 0x3fu));
			}
			else if (needed == 3)
			{
				destination[bytesWritten] = (byte)(0xe0u | (current >> 12));
				destination[bytesWritten + 1] = (byte)(0x80u | ((current >> 6) & 0x3fu));
				destination[bytesWritten + 2] = (byte)(0x80u | (current & 0x3fu));
			}
			else
			{
				destination[bytesWritten] = (byte)(0xf0u | (current >> 18));
				destination[bytesWritten + 1] = (byte)(0x80u | ((current >> 12) & 0x3fu));
				destination[bytesWritten + 2] = (byte)(0x80u | ((current >> 6) & 0x3fu));
				destination[bytesWritten + 3] = (byte)(0x80u | (current & 0x3fu));
			}
			charsRead += consumed;
			bytesWritten += needed;
		}
		return OperationStatus.Done;
	}

	public static OperationStatus ToUtf16(ReadOnlySpan<byte> source, Span<char> destination,
		out int bytesRead, out int charsWritten, bool replaceInvalidSequences = true, bool isFinalBlock = true)
	{
		bytesRead = 0;
		charsWritten = 0;
		while (bytesRead < source.Length)
		{
			var status = DecodeOne(source.Slice(bytesRead), isFinalBlock, out var scalar, out var consumed);
			if (status == OperationStatus.NeedMoreData) return status;
			if (status == OperationStatus.InvalidData)
			{
				if (!replaceInvalidSequences) return status;
				scalar = 0xfffd;
			}
			var required = scalar <= 0xffff ? 1 : 2;
			if (destination.Length - charsWritten < required) return OperationStatus.DestinationTooSmall;
			if (required == 1) destination[charsWritten] = (char)scalar;
			else
			{
				var supplementary = scalar - 0x10000;
				destination[charsWritten] = (char)(0xd800 | (supplementary >> 10));
				destination[charsWritten + 1] = (char)(0xdc00 | (supplementary & 0x3ff));
			}
			bytesRead += consumed;
			charsWritten += required;
		}
		return OperationStatus.Done;
	}

	public static bool IsValid(ReadOnlySpan<byte> source) => IndexOfInvalidSubsequence(source) < 0;

	public static int IndexOfInvalidSubsequence(ReadOnlySpan<byte> source)
	{
		var index = 0;
		while (index < source.Length)
		{
			if (DecodeOne(source.Slice(index), true, out _, out var consumed) != OperationStatus.Done) return index;
			index += consumed;
		}
		return -1;
	}

	private static OperationStatus DecodeOne(ReadOnlySpan<byte> source, bool isFinalBlock, out int scalar, out int consumed)
	{
		var first = source[0];
		scalar = first;
		consumed = 1;
		if (first < 0x80) return OperationStatus.Done;
		var width = first is >= 0xc2 and <= 0xdf ? 2 : first is >= 0xe0 and <= 0xef ? 3 : first is >= 0xf0 and <= 0xf4 ? 4 : 0;
		if (width == 0) return OperationStatus.InvalidData;
		var value = first & (width == 2 ? 0x1f : width == 3 ? 0x0f : 0x07);
		for (var index = 1; index < width; index++)
		{
			if (index == source.Length) return isFinalBlock ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;
			var next = source[index];
			if (next < 0x80 || next > 0xbf ||
				index == 1 && (first == 0xe0 && next < 0xa0 || first == 0xed && next > 0x9f ||
				first == 0xf0 && next < 0x90 || first == 0xf4 && next > 0x8f))
				return OperationStatus.InvalidData;
			value = (value << 6) | (next & 0x3f);
			consumed++;
		}
		scalar = value;
		return OperationStatus.Done;
	}
}
