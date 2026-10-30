/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;

namespace CopperSharp.Compiler.Tests;

public static class HexCodecFixtures
{
	public static int Entry()
	{
		var source = new byte[] { 0, 15, 16, 127, 128, 255 };
		if (Convert.ToHexString(source) != "000F107F80FF" ||
			Convert.ToHexStringLower(source) != "000f107f80ff" ||
			Convert.ToHexString(source, 1, 3) != "0F107F" ||
			Convert.ToHexStringLower(source, 1, 3) != "0f107f") return 1;
		if (Convert.ToHexString((ReadOnlySpan<byte>)source) != "000F107F80FF" ||
			Convert.ToHexStringLower((ReadOnlySpan<byte>)source) != "000f107f80ff") return 2;
		Span<char> chars = stackalloc char[16];
		for (var i = 0; i < chars.Length; i++) chars[i] = '#';
		if (Convert.TryToHexString(source, chars.Slice(2, 11), out var written) || written != 0 || chars[2] != '#') return 3;
		if (!Convert.TryToHexString(source, chars.Slice(2, 12), out written) || written != 12 ||
			chars[0] != '#' || chars[1] != '#' || chars[14] != '#' || chars[15] != '#') return 4;
		if (!Convert.TryToHexStringLower(source, chars, out written) || written != 12 || chars[3] != 'f') return 5;
		Span<byte> utf8 = stackalloc byte[16];
		for (var i = 0; i < utf8.Length; i++) utf8[i] = 0xcc;
		if (Convert.TryToHexString(source, utf8.Slice(2, 11), out written) || written != 0 || utf8[2] != 0xcc) return 6;
		if (!Convert.TryToHexString(source, utf8.Slice(2, 12), out written) || written != 12 ||
			utf8[2] != '0' || utf8[5] != 'F' || utf8[13] != 'F' || utf8[14] != 0xcc) return 7;
		if (!Convert.TryToHexStringLower(source, utf8, out written) || written != 12 || utf8[3] != 'f' || utf8[5] != '0') return 8;
		var decoded = Convert.FromHexString("000F107F80FF");
		var lower = Convert.FromHexString("000f107f80ff".AsSpan());
		var fromUtf8 = Convert.FromHexString((ReadOnlySpan<byte>)utf8.Slice(0, 12));
		for (var i = 0; i < source.Length; i++)
			if (source[i] != decoded[i] || source[i] != lower[i] || source[i] != fromUtf8[i]) return 9;
		Span<byte> output = stackalloc byte[8];
		for (var i = 0; i < output.Length; i++) output[i] = 0xee;
		if (Convert.FromHexString("0a1B2c", output.Slice(1, 2), out var consumed, out written) != OperationStatus.DestinationTooSmall ||
			consumed != 4 || written != 2 || output[0] != 0xee || output[1] != 10 || output[2] != 27 || output[3] != 0xee) return 10;
		if (Convert.FromHexString("0a1B2cZ".AsSpan(), output, out consumed, out written) != OperationStatus.NeedMoreData ||
			consumed != 6 || written != 3 || output[2] != 44) return 11;
		if (Convert.FromHexString("0a1G2c".AsSpan(), output, out consumed, out written) != OperationStatus.InvalidData ||
			consumed != 3 || written != 1 || output[0] != 10) return 12;
		var invalidUtf8 = new byte[6];
		invalidUtf8[0] = 48; invalidUtf8[1] = 97; invalidUtf8[2] = 49;
		invalidUtf8[3] = 71; invalidUtf8[4] = 50; invalidUtf8[5] = 99;
		if (Convert.FromHexString((ReadOnlySpan<byte>)invalidUtf8, output,
			out consumed, out written) != OperationStatus.InvalidData || consumed != 3 || written != 1) return 13;
		if (Convert.FromHexString("F".AsSpan(), output, out consumed, out written) != OperationStatus.NeedMoreData ||
			consumed != 0 || written != 0) return 14;
		invalidUtf8[0] = 0xc3; invalidUtf8[1] = 0xa9;
		if (Convert.FromHexString(((ReadOnlySpan<byte>)invalidUtf8).Slice(0, 2), output, out consumed, out written) !=
			OperationStatus.InvalidData || consumed != 1 || written != 0) return 18;
		var threw = false;
		try { _ = Convert.FromHexString("ABC"); } catch (FormatException) { threw = true; }
		if (!threw) return 15;
		threw = false;
		try { _ = Convert.ToHexString(source, 1, -1); }
		catch (ArgumentOutOfRangeException error) { threw = error.ParamName == "length"; }
		if (!threw) return 16;
		threw = false;
		try { _ = Convert.FromHexString((string)null!); }
		catch (ArgumentNullException error) { threw = error.ParamName == "s"; }
		if (!threw) return 17;
		threw = false;
		try { _ = Convert.ToHexString((byte[])null!); }
		catch (ArgumentNullException error) { threw = error.ParamName == "inArray"; }
		if (!threw) return 19;
		threw = false;
		try { _ = Convert.ToHexStringLower((byte[])null!, -1, -1); }
		catch (ArgumentNullException error) { threw = error.ParamName == "inArray"; }
		if (!threw) return 20;
		threw = false;
		try { _ = Convert.ToHexStringLower(source, -1, 1); }
		catch (ArgumentOutOfRangeException error) { threw = error.ParamName == "offset"; }
		if (!threw) return 21;
		threw = false;
		try { _ = Convert.ToHexString(source, 6, 1); }
		catch (ArgumentOutOfRangeException error) { threw = error.ParamName == "offset"; }
		if (!threw) return 22;
		threw = false;
		try { _ = Convert.FromHexString((string)null!, output, out _, out _); }
		catch (ArgumentNullException error) { threw = error.ParamName == "source"; }
		return threw ? 42 : 23;
	}

	public static uint GeneratedEntry()
	{
		uint hash = 2166136261;
		uint state = 0x9b725ed1;
		Span<char> chars = stackalloc char[64];
		Span<byte> utf8 = stackalloc byte[64];
		Span<byte> decoded = stackalloc byte[32];
		for (var count = 0; count < 32; count++)
		{
			var source = new byte[count];
			for (var index = 0; index < count; index++)
			{
				state = unchecked(state * 1664525 + 1013904223);
				source[index] = (byte)(state >> 24);
			}
			var upper = Convert.ToHexString(source);
			var lower = Convert.ToHexStringLower(source);
			var roundtrip = Convert.FromHexString(lower);
			if (!Convert.TryToHexString(source, chars, out var charsWritten) || charsWritten != upper.Length ||
				!Convert.TryToHexStringLower(source, utf8, out var bytesWritten) || bytesWritten != lower.Length)
				return 0xbad00001;
			for (var index = 0; index < upper.Length; index++)
			{
				if (upper[index] != chars[index] || lower[index] != utf8[index]) return 0xbad00002;
				hash = unchecked((hash ^ chars[index]) * 16777619);
				hash = unchecked((hash ^ utf8[index]) * 16777619);
			}
			var status = Convert.FromHexString((ReadOnlySpan<byte>)utf8.Slice(0, bytesWritten), decoded,
				out var consumed, out var written);
			if (status != OperationStatus.Done || consumed != bytesWritten || written != count) return 0xbad00003;
			for (var index = 0; index < count; index++)
			{
				if (roundtrip[index] != source[index] || decoded[index] != source[index]) return 0xbad00004;
				hash = unchecked((hash ^ decoded[index]) * 16777619);
			}
		}
		return hash;
	}
}
