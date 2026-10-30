/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using System.Buffers.Text;
using System.Text.Unicode;

namespace CopperSharp.Compiler.Tests;

/// <summary>Retries a retained suffix after each streaming status, including zero-progress short writes.</summary>
public static class CoreLibCodecStreamingFixtures
{
	public static uint GeneratedEntry()
	{
		uint hash = 2166136261;
		var text = "A\u00e9\u6c34\ud83d\ude00Z";
		Span<byte> utf8 = stackalloc byte[64];
		for (var i = 0; i < utf8.Length; i++) utf8[i] = 0xa5;
		var utf8Size = 0;
		var start = 0;
		var visible = 4; // Ends on the high surrogate until the next input chunk arrives.
		var capacity = 1;
		var sawZeroProgress = false;
		var sawIncomplete = false;
		for (var iteration = 0; iteration < 12 && start < text.Length; iteration++)
		{
			var status = Utf8.FromUtf16(text.AsSpan().Slice(start, visible - start),
				utf8.Slice(1 + utf8Size, capacity), out var consumed, out var written, true, visible == text.Length);
			if (utf8[0] != 0xa5 || utf8[1 + utf8Size + capacity] != 0xa5) return 0xbad10001;
			for (var i = written; i < capacity; i++) if (utf8[1 + utf8Size + i] != 0xa5) return 0xbad10002;
			hash = Mix(Mix(Mix(hash, (uint)status), (uint)consumed), (uint)written);
			if (status == OperationStatus.DestinationTooSmall && consumed == 0 && written == 0) sawZeroProgress = true;
			if (status == OperationStatus.NeedMoreData) sawIncomplete = true;
			if (status == OperationStatus.InvalidData) return 0xbad10003;
			start += consumed; utf8Size += written;
			if (status == OperationStatus.DestinationTooSmall) capacity = 16;
			else { capacity = 1; if (visible < text.Length) { visible += 2; if (visible > text.Length) visible = text.Length; } }
		}
		if (start != text.Length || utf8Size != 11 || !sawIncomplete) return 0xbad10004;
		var expectedUtf8 = new byte[] { 0x41, 0xc3, 0xa9, 0xe6, 0xb0, 0xb4, 0xf0, 0x9f, 0x98, 0x80, 0x5a };
		for (var i = 0; i < utf8Size; i++)
		{
			if (utf8[1 + i] != expectedUtf8[i]) return 0xbad10005;
			hash = Mix(hash, utf8[1 + i]);
		}

		Span<char> restored = stackalloc char[32];
		for (var i = 0; i < restored.Length; i++) restored[i] = '#';
		start = 0; visible = 8; capacity = 1;
		var charCount = 0;
		sawIncomplete = false;
		for (var iteration = 0; iteration < 12 && start < utf8Size; iteration++)
		{
			var status = Utf8.ToUtf16(utf8.Slice(1 + start, visible - start),
				restored.Slice(1 + charCount, capacity), out var consumed, out var written, true, visible == utf8Size);
			if (restored[0] != '#' || restored[1 + charCount + capacity] != '#') return 0xbad10006;
			for (var i = written; i < capacity; i++) if (restored[1 + charCount + i] != '#') return 0xbad10007;
			hash = Mix(Mix(Mix(hash, (uint)status), (uint)consumed), (uint)written);
			if (status == OperationStatus.DestinationTooSmall && consumed == 0 && written == 0) sawZeroProgress = true;
			if (status == OperationStatus.NeedMoreData) sawIncomplete = true;
			if (status == OperationStatus.InvalidData) return 0xbad10008;
			start += consumed; charCount += written;
			if (status == OperationStatus.DestinationTooSmall) capacity = 16;
			else { capacity = 1; if (visible < utf8Size) { visible += 3; if (visible > utf8Size) visible = utf8Size; } }
		}
		if (start != utf8Size || charCount != text.Length || !sawIncomplete) return 0xbad10009;
		for (var i = 0; i < charCount; i++)
		{
			if (restored[1 + i] != text[i]) return 0xbad1000a;
			hash = Mix(hash, restored[1 + i]);
		}

		var payload = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7 };
		Span<byte> encoded = stackalloc byte[40];
		for (var i = 0; i < encoded.Length; i++) encoded[i] = 0xa5;
		start = 0; visible = 5; capacity = 2;
		var encodedSize = 0;
		sawIncomplete = false;
		for (var iteration = 0; iteration < 12 && start < payload.Length; iteration++)
		{
			var status = Base64.EncodeToUtf8(((ReadOnlySpan<byte>)payload).Slice(start, visible - start),
				encoded.Slice(1 + encodedSize, capacity), out var consumed, out var written, visible == payload.Length);
			if (encoded[0] != 0xa5 || encoded[1 + encodedSize + capacity] != 0xa5) return 0xbad1000b;
			for (var i = written; i < capacity; i++) if (encoded[1 + encodedSize + i] != 0xa5) return 0xbad1000c;
			hash = Mix(Mix(Mix(hash, (uint)status), (uint)consumed), (uint)written);
			if (status == OperationStatus.DestinationTooSmall && consumed == 0 && written == 0) sawZeroProgress = true;
			if (status == OperationStatus.NeedMoreData) sawIncomplete = true;
			if (status == OperationStatus.InvalidData) return 0xbad1000d;
			start += consumed; encodedSize += written;
			if (status == OperationStatus.DestinationTooSmall) capacity = 16;
			else { capacity = 2; if (visible < payload.Length) { visible += 3; if (visible > payload.Length) visible = payload.Length; } }
		}
		if (start != payload.Length || encodedSize != 12 || !sawIncomplete || !sawZeroProgress) return 0xbad1000e;
		var expectedBase64 = "AAECAwQFBgc=";
		for (var i = 0; i < encodedSize; i++)
		{
			if (encoded[1 + i] != expectedBase64[i]) return 0xbad1000f;
			hash = Mix(hash, encoded[1 + i]);
		}

		Span<byte> decoded = stackalloc byte[40];
		for (var i = 0; i < decoded.Length; i++) decoded[i] = 0xa5;
		start = 0; visible = 5; capacity = 2;
		var decodedSize = 0;
		sawIncomplete = false;
		for (var iteration = 0; iteration < 12 && start < encodedSize; iteration++)
		{
			var status = Base64.DecodeFromUtf8(encoded.Slice(1 + start, visible - start),
				decoded.Slice(1 + decodedSize, capacity), out var consumed, out var written, visible == encodedSize);
			if (decoded[0] != 0xa5 || decoded[1 + decodedSize + capacity] != 0xa5) return 0xbad10010;
			for (var i = written; i < capacity; i++) if (decoded[1 + decodedSize + i] != 0xa5) return 0xbad10011;
			hash = Mix(Mix(Mix(hash, (uint)status), (uint)consumed), (uint)written);
			if (status == OperationStatus.DestinationTooSmall && consumed == 0 && written == 0) sawZeroProgress = true;
			if (status == OperationStatus.NeedMoreData) sawIncomplete = true;
			if (status == OperationStatus.InvalidData) return 0xbad10012;
			start += consumed; decodedSize += written;
			if (status == OperationStatus.DestinationTooSmall) capacity = 16;
			else { capacity = 2; if (visible < encodedSize) { visible += 3; if (visible > encodedSize) visible = encodedSize; } }
		}
		if (start != encodedSize || decodedSize != payload.Length || !sawIncomplete) return 0xbad10013;
		for (var i = 0; i < payload.Length; i++)
		{
			if (decoded[1 + i] != payload[i]) return 0xbad10014;
			hash = Mix(hash, decoded[1 + i]);
		}

		var hex = "001122AAbbCC";
		var utf8Hex = new byte[hex.Length];
		for (var i = 0; i < utf8Hex.Length; i++) utf8Hex[i] = (byte)hex[i];
		Span<byte> hexDecoded = stackalloc byte[40];
		for (var mode = 0; mode < 2; mode++)
		{
			for (var i = 0; i < hexDecoded.Length; i++) hexDecoded[i] = 0xa5;
			start = 0; visible = 5; capacity = 1; decodedSize = 0;
			sawIncomplete = false;
			for (var iteration = 0; iteration < 20 && start < hex.Length; iteration++)
			{
				var destination = hexDecoded.Slice(1 + decodedSize, capacity);
				var status = mode == 0 ?
					Convert.FromHexString(hex.AsSpan().Slice(start, visible - start), destination, out var consumed, out var written) :
					Convert.FromHexString(((ReadOnlySpan<byte>)utf8Hex).Slice(start, visible - start), destination, out consumed, out written);
				if (hexDecoded[0] != 0xa5 || hexDecoded[1 + decodedSize + capacity] != 0xa5) return 0xbad10015;
				for (var i = written; i < capacity; i++) if (hexDecoded[1 + decodedSize + i] != 0xa5) return 0xbad10016;
				hash = Mix(Mix(Mix(hash, (uint)status), (uint)consumed), (uint)written);
				if (status == OperationStatus.DestinationTooSmall && consumed == 0 && written == 0) sawZeroProgress = true;
				if (status == OperationStatus.NeedMoreData) sawIncomplete = true;
				if (status == OperationStatus.InvalidData) return 0xbad10017;
				start += consumed; decodedSize += written;
				if (status == OperationStatus.DestinationTooSmall) capacity = 16;
				else { capacity = 1; if (visible < hex.Length) { visible += 3; if (visible > hex.Length) visible = hex.Length; } }
			}
			if (start != hex.Length || decodedSize != 6 || !sawIncomplete) return 0xbad10018;
			if (hexDecoded[1] != 0 || hexDecoded[2] != 0x11 || hexDecoded[3] != 0x22 ||
				hexDecoded[4] != 0xaa || hexDecoded[5] != 0xbb || hexDecoded[6] != 0xcc) return 0xbad10019;
			for (var i = 0; i < decodedSize; i++) hash = Mix(hash, hexDecoded[1 + i]);
		}
		return hash;
	}

	private static uint Mix(uint hash, uint value) => unchecked((hash ^ value) * 16777619);
}
