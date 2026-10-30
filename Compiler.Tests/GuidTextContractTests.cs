/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class GuidTextContractTests
{
	private static readonly string[] Formats = ["N", "D", "B", "P", "X", "n", "d", "b", "p", "x"];

	[Fact]
	public void GeneratedFormattingAndParsingVectorsMatchHostGuid()
	{
		Assert.Equal(42, GuidCoreLibFixtures.TextEntry());
		var random = new Random(0x6a1d);
		foreach (var index in Enumerable.Range(0, 66))
		{
			var bytes = new byte[16];
			if (index == 1) Array.Fill(bytes, (byte)255);
			else if (index > 1) random.NextBytes(bytes);
			var guid = new Guid(bytes);
			var shadow = new ShadowGuid(bytes);
			Assert.Equal(guid.ToString(), shadow.FormatDefault());
			Assert.Equal(guid.ToString(""), shadow.Format(""));
			foreach (var format in Formats)
			{
				var expected = guid.ToString(format);
				Assert.Equal(expected, shadow.Format(format));
				foreach (var text in new[] { expected, expected.ToUpperInvariant(), " \t" + expected + "\r\n", "\u2000" + expected + "\u3000" })
				{
					CheckParse(text);
					CheckExact(text, format);
					Assert.Equal(guid, ShadowGuid.Parse(text));
					Assert.Equal(guid, ShadowGuid.ParseExact(text, format));
				}
				var hostBuffer = new char[72];
				var targetBuffer = new char[72];
				Array.Fill(hostBuffer, '#');
				Array.Fill(targetBuffer, '#');
				Assert.Equal(guid.TryFormat(hostBuffer.AsSpan(2, expected.Length), out var hostWritten, format),
					shadow.TryFormat(targetBuffer.AsSpan(2, expected.Length), out var targetWritten, format));
				Assert.Equal(hostWritten, targetWritten);
				Assert.Equal(hostBuffer, targetBuffer);
			}
		}
	}

	[Fact]
	public void ShortFormattingAndByteDestinationsAreUntouched()
	{
		var guid = new Guid("01234567-89ab-cdef-0123-456789abcdef");
		var shadow = new ShadowGuid(guid.ToByteArray());
		foreach (var format in Formats)
		{
			for (var length = 0; length < guid.ToString(format).Length; length++)
			{
				var buffer = Enumerable.Repeat('#', 72).ToArray();
				Assert.False(shadow.TryFormat(buffer.AsSpan(1, length), out var written, format));
				Assert.Equal(0, written);
				Assert.All(buffer, c => Assert.Equal('#', c));
			}
		}
		for (var length = 0; length < 16; length++)
		{
			var bytes = Enumerable.Repeat((byte)0xa5, 20).ToArray();
			Assert.False(shadow.TryWriteBytes(bytes.AsSpan(1, length), true, out var written));
			Assert.Equal(0, written);
			Assert.All(bytes, b => Assert.Equal(0xa5, b));
		}
	}

	[Fact]
	public void LegacyPrefixesWhitespaceAndMalformedCorpusMatchHost()
	{
		string?[] corpus = [null, "", " ", "garbage", "0123456789abcdef0123456789abcdef\0",
			"+1234567-+123-0x45-+123-0X4589abcdef",
			"01234567-89ab-cdef-0x23-0x6789abcdef",
			"{0x1,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x1,0x12345678,0x12345678,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x+0x,0x+,0x0x,{0x+,0x0x,0x+0x,0x0007,0x8,0x9,0xa,0xb}}",
			"{0X0000000001,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x100000000,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x1,0x2,0x3,{0x100,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x1,0x2,0x3,{0x,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0 x 1, 0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}",
			"{0x1,0x2,0x3,{0x4,0x5,0x6,0x7,0x8,0x9,0xa,0xb}}\0"];
		foreach (var text in corpus)
		{
			CheckParse(text);
			foreach (var format in Formats.Concat(["", "DD", "Y", (string)null!]))
			{
				CheckExact(text, format);
				Assert.Equal(Record.Exception(() => Guid.ParseExact(text!, format!))?.GetType(),
					Record.Exception(() => ShadowGuid.ParseExact(text!, format!))?.GetType());
			}
			Assert.Equal(Record.Exception(() => Guid.Parse(text!))?.GetType(), Record.Exception(() => ShadowGuid.Parse(text!))?.GetType());
		}
		var seed = new Guid("01234567-89ab-cdef-0123-456789abcdef");
		foreach (var format in Formats.Take(5))
		{
			var text = seed.ToString(format);
			foreach (var replacement in new[] { 'g', '0', '+', 'x', ' ', '\0', '/', '\uff11' })
				for (var index = 0; index < text.Length; index++)
				{
					var changed = text.ToCharArray();
					changed[index] = replacement;
					var input = new string(changed);
					CheckParse(input);
					CheckExact(input, format);
				}
		}
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("Y")]
	[InlineData("DD")]
	[InlineData(" ")]
	public void InvalidFormatsAndNullsMatchHostExceptions(string? format)
	{
		const string input = "01234567-89ab-cdef-0123-456789abcdef";
		var guid = Guid.Parse(input);
		var shadow = new ShadowGuid(guid.ToByteArray());
		Assert.Equal(Record.Exception(() => Guid.ParseExact(input, format!))?.GetType(), Record.Exception(() => ShadowGuid.ParseExact(input, format!))?.GetType());
		Assert.Equal(Record.Exception(() => guid.ToString(format))?.GetType(), Record.Exception(() => shadow.Format(format))?.GetType());
		Assert.Equal(Record.Exception(() => guid.TryFormat(new char[0], out _, format))?.GetType(),
			Record.Exception(() => shadow.TryFormat(new char[0], out _, format))?.GetType());
	}

	private static void CheckParse(string? text)
	{
		var expected = Guid.TryParse(text, out var host);
		var actual = ShadowGuid.TryParse(text, out var target);
		Assert.True(expected == actual && host == target, $"TryParse mismatch: {text}");
	}

	private static void CheckExact(string? text, string? format)
	{
		var expected = Guid.TryParseExact(text, format, out var host);
		var actual = ShadowGuid.TryParseExact(text, format, out var target);
		Assert.True(expected == actual && host == target, $"TryParseExact {format} mismatch: {text}");
	}
}
