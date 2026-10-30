/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text.Unicode;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class Utf8CodecContractTests
{
	[Fact]
	public void NativeFixtureHasExpectedHostResult() => Assert.Equal(42, Utf8CodecFixtures.Entry());

	[Fact]
	public void EveryLeadByteAndBoundaryContinuationMatchesHost()
	{
		byte[] continuations = [0, 0x3f, 0x7f, 0x80, 0x8f, 0x90, 0x9f, 0xa0, 0xbf, 0xc0, 0xff];
		for (var lead = 0; lead < 256; lead++)
		foreach (var second in continuations)
		foreach (var third in continuations)
		{
			byte[] bytes = [(byte)lead, second, third, 0x80];
			for (var length = 1; length <= 4; length++)
			{
				Assert.Equal(Utf8.IsValid(bytes.AsSpan(0, length)), ShadowUtf8.IsValid(bytes.AsSpan(0, length)));
				foreach (var final in new[] { true, false })
				foreach (var replace in new[] { true, false })
				foreach (var capacity in new[] { 0, 1, 2, 4 })
					CompareToUtf16(bytes, length, capacity, replace, final);
			}
		}
	}

	[Fact]
	public void SurrogatesScalarBoundariesAndDestinationGuardsMatchHost()
	{
		char[] boundaries = ['\0', '\u007f', '\u0080', '\u07ff', '\u0800', '\ud7ff',
			'\ud800', '\udbff', '\udc00', '\udfff', '\ue000', '\uffff'];
		foreach (var first in boundaries)
		foreach (var second in boundaries)
		{
			char[] chars = [first, second, 'X'];
			for (var length = 0; length <= 3; length++)
			foreach (var final in new[] { true, false })
			foreach (var replace in new[] { true, false })
			foreach (var capacity in new[] { 0, 1, 2, 3, 4, 6, 9 })
				CompareFromUtf16(chars, length, capacity, replace, final);
		}
	}

	[Fact]
	public void GeneratedMalformedAndValidMultibyteStreamsMatchHost()
	{
		var random = new Random(0x6c23);
		for (var iteration = 0; iteration < 7000; iteration++)
		{
			var length = iteration % 9;
			var bytes = new byte[length];
			var chars = new char[length];
			random.NextBytes(bytes);
			for (var index = 0; index < length; index++) chars[index] = (char)random.Next(65536);
			if (length >= 4 && iteration % 7 == 0)
			{
				bytes[0] = 0xf0; bytes[1] = 0x9f; bytes[2] = 0x98; bytes[3] = 0x80;
				chars[0] = '\ud83d'; chars[1] = '\ude00';
			}
			Assert.Equal(Utf8.IsValid(bytes), ShadowUtf8.IsValid(bytes));
			for (var flags = 0; flags < 4; flags++)
			{
				var replace = (flags & 1) != 0;
				var final = (flags & 2) != 0;
				var capacity = (iteration * 13 + flags) % 13;
				CompareToUtf16(bytes, length, capacity, replace, final);
				CompareFromUtf16(chars, length, capacity, replace, final);
			}
		}
	}

	private static void CompareToUtf16(byte[] bytes, int length, int capacity, bool replace, bool final)
	{
		var expected = Enumerable.Repeat('#', capacity + 2).ToArray();
		var actual = (char[])expected.Clone();
		var host = Utf8.ToUtf16(bytes.AsSpan(0, length), expected.AsSpan(1, capacity), out var hostRead, out var hostWritten, replace, final);
		var target = ShadowUtf8.ToUtf16(bytes.AsSpan(0, length), actual.AsSpan(1, capacity), out var targetRead, out var targetWritten, replace, final);
		Assert.True(host == target && hostRead == targetRead && hostWritten == targetWritten && expected.SequenceEqual(actual),
			$"UTF8 {Convert.ToHexString(bytes.AsSpan(0, length))} capacity={capacity} replace={replace} final={final}; " +
			$"host={host}/{hostRead}/{hostWritten} target={target}/{targetRead}/{targetWritten}");
	}

	private static void CompareFromUtf16(char[] chars, int length, int capacity, bool replace, bool final)
	{
		var expected = Enumerable.Repeat((byte)0xa5, capacity + 2).ToArray();
		var actual = (byte[])expected.Clone();
		var host = Utf8.FromUtf16(chars.AsSpan(0, length), expected.AsSpan(1, capacity), out var hostRead, out var hostWritten, replace, final);
		var target = ShadowUtf8.FromUtf16(chars.AsSpan(0, length), actual.AsSpan(1, capacity), out var targetRead, out var targetWritten, replace, final);
		Assert.True(host == target && hostRead == targetRead && hostWritten == targetWritten && expected.SequenceEqual(actual),
			$"UTF16 {string.Join(' ', chars.AsSpan(0, length).ToArray().Select(c => ((int)c).ToString("X4")))} capacity={capacity} replace={replace} final={final}; " +
			$"host={host}/{hostRead}/{hostWritten} target={target}/{targetRead}/{targetWritten}");
	}
}
