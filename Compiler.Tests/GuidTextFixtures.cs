/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Tests;

public static partial class GuidCoreLibFixtures
{
	public static uint GeneratedEntry()
	{
		var formats = new[] { "N", "D", "B", "P", "X" };
		uint state = 0x1234abcd;
		uint hash = 2166136261;
		var previous = default(Guid);
		Span<char> buffer = stackalloc char[72];
		for (var vector = 0; vector < 24; vector++)
		{
			var bytes = new byte[16];
			for (var index = 0; index < 16; index++)
			{
				state = unchecked(state * 1664525 + 1013904223);
				bytes[index] = (byte)(state >> 24);
			}
			var value = Transport(new Guid(bytes));
			hash = unchecked((hash ^ (uint)value.GetHashCode()) * 16777619);
			hash = unchecked((hash ^ (uint)value.CompareTo(previous)) * 16777619);
			previous = value;
			var networkBytes = value.ToByteArray(true);
			for (var index = 0; index < 16; index++) hash = unchecked((hash ^ networkBytes[index]) * 16777619);
			if (new Guid((ReadOnlySpan<byte>)networkBytes, true) != value) return 0xbad00001;
			for (var formatIndex = 0; formatIndex < formats.Length; formatIndex++)
			{
				var text = value.ToString(formats[formatIndex]);
				if (Guid.Parse(text) != value || Guid.ParseExact(text, formats[formatIndex]) != value) return 0xbad00002;
				if (!Guid.TryParseExact(text, formats[formatIndex], out var parsed) || parsed != value) return 0xbad00003;
				if (!value.TryFormat(buffer, out var written, formats[formatIndex].AsSpan()) || written != text.Length) return 0xbad00004;
				for (var index = 0; index < written; index++)
				{
					if (buffer[index] != text[index]) return 0xbad00005;
					hash = unchecked((hash ^ buffer[index]) * 16777619);
				}
			}
		}
		return hash;
	}

	public static int TextEntry()
	{
		var value = new Guid(0x11223344, 0x5566, 0x7788, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x10);
		var formats = new[] { "N", "D", "B", "P", "X", "n", "d", "b", "p", "x" };
		var texts = new[] { "112233445566778899aabbccddeeff10", "11223344-5566-7788-99aa-bbccddeeff10",
			"{11223344-5566-7788-99aa-bbccddeeff10}", "(11223344-5566-7788-99aa-bbccddeeff10)",
			"{0x11223344,0x5566,0x7788,{0x99,0xaa,0xbb,0xcc,0xdd,0xee,0xff,0x10}}" };
		if (value.ToString() != texts[1] || value.ToString("") != texts[1]) return 1;
		Span<char> buffer = stackalloc char[72];
		for (var index = 0; index < formats.Length; index++)
		{
			var expected = texts[index % 5];
			var format = formats[index];
			if (value.ToString(format) != expected) return 10 + index;
			if (Guid.Parse(expected) != value || Guid.ParseExact(expected, format) != value) return 20 + index;
			if (!Guid.TryParse(expected, out var parsed) || parsed != value) return 30 + index;
			if (!Guid.TryParseExact(expected, format, out parsed) || parsed != value) return 50 + index;
			for (var c = 0; c < buffer.Length; c++) buffer[c] = '#';
			if (value.TryFormat(buffer.Slice(1, expected.Length - 1), out var written, format.AsSpan()) || written != 0) return 60 + index;
			for (var c = 0; c < buffer.Length; c++) if (buffer[c] != '#') return 70 + index;
			if (!value.TryFormat(buffer.Slice(1, expected.Length), out written, format.AsSpan()) || written != expected.Length) return 80 + index;
			for (var c = 0; c < expected.Length; c++) if (buffer[c + 1] != expected[c]) return 90 + index;
			if (buffer[0] != '#' || buffer[expected.Length + 1] != '#') return 100 + index;
		}
		if (!Guid.TryParse("\u2000(11223344-5566-7788-99AA-BBCCDDEEFF10)\u3000", out var white) || white != value) return 110;
		if (Guid.Parse("+1234567-+123-0x45-+123-0X4589abcdef").ToString() != "01234567-0123-0045-0123-004589abcdef") return 111;
		if (Guid.ParseExact("{ 0 x 1, 0x+0x2, 0X3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}", "X").ToString() != "00000001-0002-0003-0405-060708090a0b") return 112;
		if (Guid.TryParse("not-a-guid", out var invalid) || invalid != default(Guid)) return 113;
		if (Guid.TryParse((string?)null, out invalid) || invalid != default(Guid)) return 114;
		if (Guid.TryParseExact(texts[0], "D", out invalid) || invalid != default(Guid)) return 115;
		if (Guid.TryParseExact(texts[0], null, out invalid) || invalid != default(Guid)) return 116;
		var formatThrew = false;
		try { _ = Guid.Parse("invalid"); }
		catch (FormatException) { formatThrew = true; }
		if (!formatThrew) return 117;
		var nullThrew = false;
		try { _ = Guid.Parse((string)null!); }
		catch (ArgumentNullException) { nullThrew = true; }
		if (!nullThrew) return 118;
		var invalidFormatThrew = false;
		try { _ = value.ToString("Y"); }
		catch (FormatException) { invalidFormatThrew = true; }
		if (!invalidFormatThrew) return 119;
		var exactNullThrew = false;
		try { _ = Guid.ParseExact(texts[0], null!); }
		catch (ArgumentNullException) { exactNullThrew = true; }
		return exactNullThrew ? 42 : 120;
	}
}
