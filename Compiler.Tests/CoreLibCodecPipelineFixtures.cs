/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using System.Buffers.Text;
using System.Text.Unicode;

namespace CopperSharp.Compiler.Tests;

public static class CoreLibCodecPipelineFixtures
{
	public static int Entry()
	{
		var texts = new[] { "", "portable protocol", "A\0\u03a9\u20ac\ud83d\ude00Z" };
		Span<byte> utf8 = stackalloc byte[128];
		Span<byte> base64 = stackalloc byte[192];
		Span<byte> decoded = stackalloc byte[128];
		Span<char> restored = stackalloc char[64];
		for (var index = 0; index < texts.Length; index++)
		{
			var original = texts[index];
			if (Utf8.FromUtf16(original.AsSpan(), utf8, out var charsRead, out var bytesWritten) != OperationStatus.Done || charsRead != original.Length) return 1;
			if (Base64.EncodeToUtf8(utf8.Slice(0, bytesWritten), base64, out var bytesRead, out var encoded) != OperationStatus.Done || bytesRead != bytesWritten) return 2;
			var hex = Convert.ToHexStringLower(base64.Slice(0, encoded));
			GC.Collect();
			var serialized = Convert.FromHexString(hex);
			if (serialized.Length != encoded || !Base64.IsValid(serialized, out var decodedLength) || decodedLength != bytesWritten) return 3;
			if (Base64.DecodeFromUtf8(serialized, decoded, out bytesRead, out var decodedBytes) != OperationStatus.Done || bytesRead != encoded || decodedBytes != bytesWritten) return 4;
			if (Utf8.ToUtf16(decoded.Slice(0, decodedBytes), restored, out bytesRead, out var charsWritten) != OperationStatus.Done || bytesRead != decodedBytes || charsWritten != original.Length) return 5;
			for (var c = 0; c < charsWritten; c++) if (restored[c] != original[c]) return 6;
			for (var b = 0; b < bytesWritten; b++) if (decoded[b] != utf8[b]) return 7;
		}
		return 42;
	}
}
