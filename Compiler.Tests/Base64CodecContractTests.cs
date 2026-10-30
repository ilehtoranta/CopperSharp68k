/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using System.Buffers.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class Base64CodecContractTests
{
	[Fact]
	public void GeneratedLengthsChunkingAndInPlaceMatchHost()
	{
		var random = new Random(0x6410);
		for (var size = 0; size < 140; size++)
		{
			var input = new byte[size];
			random.NextBytes(input);
			Assert.Equal(Base64.GetMaxEncodedToUtf8Length(size), ShadowBase64.GetMaxEncodedToUtf8Length(size));
			Assert.Equal(Base64.GetMaxDecodedFromUtf8Length(size), ShadowBase64.GetMaxDecodedFromUtf8Length(size));
			foreach (var final in new[] { true, false })
				for (var capacity = 0; capacity < 12; capacity++)
				{
					var host = Enumerable.Repeat((byte)0xa5, capacity).ToArray();
					var shadow = (byte[])host.Clone();
					var hs = Base64.EncodeToUtf8(input, host, out var hc, out var hw, final);
					var ss = ShadowBase64.EncodeToUtf8(input, shadow, out var sc, out var sw, final);
					Assert.Equal((hs, hc, hw), (ss, sc, sw));
					Assert.Equal(host, shadow);
				}
			var encoded = new byte[Base64.GetMaxEncodedToUtf8Length(size)];
			Base64.EncodeToUtf8(input, encoded, out _, out _);
			for (var capacity = 0; capacity < 12; capacity++)
			{
				var host = Enumerable.Repeat((byte)0xa5, capacity).ToArray();
				var shadow = (byte[])host.Clone();
				var hs = Base64.DecodeFromUtf8(encoded, host, out var hc, out var hw);
				var ss = ShadowBase64.DecodeFromUtf8(encoded, shadow, out var sc, out var sw);
				Assert.Equal((hs, hc, hw), (ss, sc, sw));
				Assert.Equal(host.AsSpan(0, hw).ToArray(), shadow.AsSpan(0, sw).ToArray());
			}
			var hostInPlace = new byte[encoded.Length];
			input.CopyTo(hostInPlace, 0);
			var shadowInPlace = (byte[])hostInPlace.Clone();
			Assert.Equal(Base64.EncodeToUtf8InPlace(hostInPlace, input.Length, out var hi),
				ShadowBase64.EncodeToUtf8InPlace(shadowInPlace, input.Length, out var si));
			Assert.Equal(hi, si);
			Assert.Equal(hostInPlace, shadowInPlace);
			Assert.Equal(Base64.DecodeFromUtf8InPlace(hostInPlace, out hi), ShadowBase64.DecodeFromUtf8InPlace(shadowInPlace, out si));
			Assert.Equal(hi, si);
			Assert.Equal(hostInPlace, shadowInPlace);
		}
	}

	[Fact]
	public void DecodeAdversarialInputsAndValidationMatchHost()
	{
		string[] corpus = ["", " ", "A", "AAAA", "AA==", "AAA=", "////", "A A A A", "AA== ", "AA==AA==",
			"====", "A===", "AAA", "AAAAA", "A?AA", "A\vAA", "A\0AA", "AA\r\n==", "AA= =", "A=AA", "AB==", "ABC=", "AA==x"];
		foreach (var sample in corpus)
		{
			var utf8 = sample.Select(c => (byte)c).ToArray();
			Assert.Equal(Base64.IsValid(utf8, out var hostLength), ShadowBase64.IsValid(utf8, out var shadowLength));
			Assert.Equal(hostLength, shadowLength);
			Assert.Equal(Base64.IsValid(sample.AsSpan(), out hostLength), ShadowBase64.IsValid(sample.AsSpan(), out shadowLength));
			Assert.Equal(hostLength, shadowLength);
			foreach (var final in new[] { true, false })
				for (var capacity = 0; capacity < 8; capacity++)
				{
					var host = Enumerable.Repeat((byte)0xa5, capacity).ToArray();
					var shadow = (byte[])host.Clone();
					var hs = Base64.DecodeFromUtf8(utf8, host, out var hc, out var hw, final);
					var ss = ShadowBase64.DecodeFromUtf8(utf8, shadow, out var sc, out var sw, final);
					Assert.Equal((hs, hc, hw), (ss, sc, sw));
					Assert.Equal(host, shadow);
				}
			var hostChars = new byte[8];
			var shadowChars = new byte[8];
			Assert.Equal(Convert.TryFromBase64Chars(sample, hostChars, out var hc2),
				ShadowConvertBase64.TryFromBase64Chars(sample, shadowChars, out var sc2));
			Assert.Equal(hc2, sc2);
			Assert.Equal(hostChars.AsSpan(0, hc2).ToArray(), shadowChars.AsSpan(0, sc2).ToArray());
		}
	}

	[Fact]
	public void RandomMalformedStreamingAndInPlaceBuffersMatchHost()
	{
		const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/= \t\r\n?";
		for (var seed = 0; seed < 1000; seed++)
		{
			var random = new Random(seed + 123);
			var input = new byte[random.Next(0, 17)];
			for (var index = 0; index < input.Length; index++) input[index] = (byte)alphabet[random.Next(alphabet.Length)];
			foreach (var final in new[] { true, false })
				for (var capacity = 0; capacity < 8; capacity++)
				{
					var host = Enumerable.Repeat((byte)0xa5, capacity).ToArray();
					var shadow = (byte[])host.Clone();
					var hostStatus = Base64.DecodeFromUtf8(input, host, out var hostConsumed, out var hostWritten, final);
					var shadowStatus = ShadowBase64.DecodeFromUtf8(input, shadow, out var shadowConsumed, out var shadowWritten, final);
					Assert.Equal((hostStatus, hostConsumed, hostWritten), (shadowStatus, shadowConsumed, shadowWritten));
					Assert.Equal(host, shadow);
				}
			var hostInPlace = (byte[])input.Clone();
			var shadowInPlace = (byte[])input.Clone();
			Assert.Equal(Base64.DecodeFromUtf8InPlace(hostInPlace, out var hostLength),
				ShadowBase64.DecodeFromUtf8InPlace(shadowInPlace, out var shadowLength));
			Assert.Equal(hostLength, shadowLength);
			Assert.Equal(hostInPlace, shadowInPlace);
		}
	}

	[Fact]
	public void ConvertFormatsRangesAndExceptionsMatchHost()
	{
		for (var size = 0; size < 170; size++)
		{
			var input = Enumerable.Range(0, size).Select(x => (byte)(x * 91 + 13)).ToArray();
			foreach (var option in new[] { Base64FormattingOptions.None, Base64FormattingOptions.InsertLineBreaks })
			{
				var expected = Convert.ToBase64String(input, option);
				Assert.Equal(expected, ShadowConvertBase64.ToBase64String(input, option));
				Assert.Equal(expected, ShadowConvertBase64.ToBase64String(input.AsSpan(), option));
				Assert.Equal(expected, ShadowConvertBase64.ToBase64String(input, 0, size, option));
				var hostChars = Enumerable.Repeat('#', expected.Length + 4).ToArray();
				var shadowChars = (char[])hostChars.Clone();
				Assert.Equal(Convert.TryToBase64Chars(input, hostChars.AsSpan(2), out var hc, option),
					ShadowConvertBase64.TryToBase64Chars(input, shadowChars.AsSpan(2), out var sc, option));
				Assert.Equal(hc, sc);
				Assert.Equal(hostChars, shadowChars);
				Assert.Equal(input, ShadowConvertBase64.FromBase64String(expected));
				Assert.Equal(input, ShadowConvertBase64.FromBase64CharArray(expected.ToCharArray(), 0, expected.Length));
			}
		}
		foreach (var length in new[] { -1, 1610612734, int.MaxValue })
			Assert.Equal(Record.Exception(() => Base64.GetMaxEncodedToUtf8Length(length))?.GetType(),
				Record.Exception(() => ShadowBase64.GetMaxEncodedToUtf8Length(length))?.GetType());
		Assert.Equal(Record.Exception(() => Base64.GetMaxDecodedFromUtf8Length(-1))?.GetType(),
			Record.Exception(() => ShadowBase64.GetMaxDecodedFromUtf8Length(-1))?.GetType());
		Assert.Equal(Record.Exception(() => Convert.ToBase64String(new byte[] { 1, 2 }, (Base64FormattingOptions)2))?.GetType(),
			Record.Exception(() => ShadowConvertBase64.ToBase64String(new byte[] { 1, 2 }, (Base64FormattingOptions)2))?.GetType());
	}
}
