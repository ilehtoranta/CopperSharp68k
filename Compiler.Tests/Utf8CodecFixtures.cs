/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using System.Text.Unicode;

namespace CopperSharp.Compiler.Tests;

public static class Utf8CodecFixtures
{
	public static int Entry()
	{
		Span<byte> bytes = stackalloc byte[24];
		for (var index = 0; index < bytes.Length; index++) bytes[index] = 0xa5;
		var status = Utf8.FromUtf16("A\u00e9\u6c34\ud83d\ude00".AsSpan(), bytes.Slice(2, 10), out var read, out var written);
		if (status != OperationStatus.Done || read != 5 || written != 10 ||
			bytes[2] != 0x41 || bytes[3] != 0xc3 || bytes[4] != 0xa9 ||
			bytes[5] != 0xe6 || bytes[6] != 0xb0 || bytes[7] != 0xb4 ||
			bytes[8] != 0xf0 || bytes[9] != 0x9f || bytes[10] != 0x98 || bytes[11] != 0x80 ||
			bytes[1] != 0xa5 || bytes[12] != 0xa5) return 1;
		Span<char> chars = stackalloc char[8];
		for (var index = 0; index < chars.Length; index++) chars[index] = '#';
		status = Utf8.ToUtf16(bytes.Slice(2, 10), chars.Slice(1, 5), out read, out written);
		if (status != OperationStatus.Done || read != 10 || written != 5 ||
			chars[0] != '#' || chars[1] != 'A' || chars[2] != '\u00e9' || chars[3] != '\u6c34' ||
			chars[4] != '\ud83d' || chars[5] != '\ude00' || chars[6] != '#') return 2;
		status = Utf8.ToUtf16(bytes.Slice(8, 3), chars, out read, out written, false, false);
		if (status != OperationStatus.NeedMoreData || read != 0 || written != 0) return 3;
		status = Utf8.ToUtf16(bytes.Slice(8, 4), chars.Slice(0, 1), out read, out written, false, true);
		if (status != OperationStatus.DestinationTooSmall || read != 0 || written != 0) return 4;
		bytes[0] = 0xe2; bytes[1] = 0x82; bytes[2] = 0x41;
		status = Utf8.ToUtf16(bytes.Slice(0, 3), chars, out read, out written, true, true);
		if (status != OperationStatus.Done || read != 3 || written != 2 || chars[0] != '\ufffd' || chars[1] != 'A') return 5;
		status = Utf8.ToUtf16(bytes.Slice(0, 3), chars, out read, out written, false, true);
		if (status != OperationStatus.InvalidData || read != 0 || written != 0) return 6;
		if (Utf8.IsValid(bytes.Slice(0, 3)) || !Utf8.IsValid(bytes.Slice(8, 4))) return 7;
		status = Utf8.FromUtf16("\ud83d".AsSpan(), bytes, out read, out written, false, false);
		if (status != OperationStatus.NeedMoreData || read != 0 || written != 0) return 8;
		status = Utf8.FromUtf16("\ud83d".AsSpan(), bytes, out read, out written, true, true);
		if (status != OperationStatus.Done || read != 1 || written != 3 ||
			bytes[0] != 0xef || bytes[1] != 0xbf || bytes[2] != 0xbd) return 9;
		return 42;
	}

	public static uint GeneratedEntry()
	{
		uint state = 0x58a7c41du;
		uint hash = 2166136261u;
		Span<byte> bytes = stackalloc byte[32];
		Span<byte> encoded = stackalloc byte[40];
		Span<char> chars = stackalloc char[16];
		Span<char> decoded = stackalloc char[40];
		for (var iteration = 0; iteration < 128; iteration++)
		{
			for (var index = 0; index < 12; index++)
			{
				state = unchecked(state * 1664525u + 1013904223u);
				bytes[index] = (byte)(state >> 24);
				chars[index] = (char)(state >> 16);
			}
			var size = iteration % 13;
			var capacity = (iteration * 7) % 17;
			var replace = (iteration & 1) != 0;
			var final = (iteration & 2) != 0;
			for (var index = 0; index < encoded.Length; index++) encoded[index] = 0xa5;
			for (var index = 0; index < decoded.Length; index++) decoded[index] = '#';
			var from = Utf8.FromUtf16(chars.Slice(0, size), encoded.Slice(1, capacity), out var charsRead, out var bytesWritten, replace, final);
			var to = Utf8.ToUtf16(bytes.Slice(0, size), decoded.Slice(1, capacity), out var bytesRead, out var charsWritten, replace, final);
			hash = Mix(hash, (uint)from); hash = Mix(hash, (uint)charsRead); hash = Mix(hash, (uint)bytesWritten);
			hash = Mix(hash, (uint)to); hash = Mix(hash, (uint)bytesRead); hash = Mix(hash, (uint)charsWritten);
			hash = Mix(hash, Utf8.IsValid(bytes.Slice(0, size)) ? 1u : 0u);
			for (var index = 0; index < bytesWritten; index++) hash = Mix(hash, encoded[index + 1]);
			for (var index = 0; index < charsWritten; index++) hash = Mix(hash, decoded[index + 1]);
			hash = Mix(hash, encoded[0]); hash = Mix(hash, encoded[capacity + 1]);
			hash = Mix(hash, decoded[0]); hash = Mix(hash, decoded[capacity + 1]);
		}
		return hash;
	}

	private static uint Mix(uint hash, uint value) => unchecked((hash ^ value) * 16777619u);
}
