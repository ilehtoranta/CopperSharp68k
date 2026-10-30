/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Tests;

public static partial class GuidCoreLibFixtures
{
	public static uint AdversarialEntry()
	{
		uint hash = 2166136261;
		var value = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
		var formats = new[] { "N", "D", "B", "P", "X" };
		// Corrupt every position, including delimiters, in each standard form.
		for (var format = 0; format < formats.Length; format++)
		{
			var canonical = value.ToString(formats[format]);
			var characters = new System.Text.StringBuilder(canonical);
			for (var index = 0; index < canonical.Length; index++)
			{
				characters[index] = index % 2 == 0 ? 'g' : '0';
				var input = characters.ToString();
				characters[index] = canonical[index];
				hash = MixParsed(hash, Guid.TryParse(input, out var parsed), parsed);
				hash = MixParsed(hash, Guid.TryParseExact(input, formats[format], out parsed), parsed);
			}
		}
		var unusual = new string?[] { null, "", " ", "garbage", "0123456789abcdef0123456789abcdef\0",
			"+1234567-+123-0x45-+123-0X4589abcdef",
			"{0x1,0x12345678,0x12345678,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x+0x,0x+,0x0x,{0x+,0x0x,0x+0x,0x0007,0x8,0x9,0xa,0xb}}",
			"{0X0000000001,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x100000000,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x1,0x2,0x3,{0x100,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x1,0x2,0x3,{0x,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0 x 1, 0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}" };
		for (var index = 0; index < unusual.Length; index++)
		{
			var input = unusual[index];
			hash = MixParsed(hash, Guid.TryParse(input, out var parsed), parsed);
			for (var format = 0; format < formats.Length; format++)
				hash = MixParsed(hash, Guid.TryParseExact(input, formats[format], out parsed), parsed);
			try { hash = MixParsed(hash, true, Guid.Parse(input!)); }
			catch (ArgumentNullException) { hash = unchecked((hash ^ 0x1234u) * 16777619); }
			catch (FormatException) { hash = unchecked((hash ^ 0x5678u) * 16777619); }
		}
		// Walk each physical bit to reach comparisons beyond the first field,
		// including signed short boundaries and every trailing byte position.
		var zero = default(Guid);
		for (var bit = 0; bit < 128; bit++)
		{
			var bytes = new byte[16];
			bytes[bit / 8] = (byte)(1 << (bit % 8));
			var current = Transport(new Guid(bytes));
			hash = unchecked((hash ^ (uint)current.CompareTo(zero)) * 16777619);
			hash = unchecked((hash ^ (uint)zero.CompareTo(current)) * 16777619);
			hash = MixParsed(hash, true, current);
		}
		return hash;
	}

	private static uint MixParsed(uint hash, bool success, Guid value)
	{
		hash = unchecked((hash ^ (success ? 1u : 0u)) * 16777619);
		var bytes = value.ToByteArray();
		for (var index = 0; index < bytes.Length; index++) hash = unchecked((hash ^ bytes[index]) * 16777619);
		return hash;
	}
}
