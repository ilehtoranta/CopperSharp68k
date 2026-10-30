/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class HexCodecContractTests
{
	[Fact]
	public void NativeFixturesHaveHostOracles() => Assert.Equal(42, HexCodecFixtures.Entry());

	[Fact]
	public void GeneratedEncodeDecodeAndShortDestinationsMatchHost()
	{
		var random = new Random(0x731904);
		for (var size = 0; size <= 96; size++)
		{
			var data = new byte[size];
			random.NextBytes(data);
			Assert.Equal(Convert.ToHexString(data), ShadowConvertHex.ToHexString(data));
			Assert.Equal(Convert.ToHexStringLower(data), ShadowConvertHex.ToHexStringLower(data));
			Assert.Equal(Convert.ToHexString((ReadOnlySpan<byte>)data), ShadowConvertHex.ToHexString((ReadOnlySpan<byte>)data));
			Assert.Equal(Convert.ToHexStringLower((ReadOnlySpan<byte>)data), ShadowConvertHex.ToHexStringLower((ReadOnlySpan<byte>)data));
			Assert.Equal(data, ShadowConvertHex.FromHexString(Convert.ToHexString(data)));
			Assert.Equal(data, ShadowConvertHex.FromHexString(Convert.ToHexStringLower(data).AsSpan()));
			Assert.Equal(data, ShadowConvertHex.FromHexString((ReadOnlySpan<byte>)System.Text.Encoding.ASCII.GetBytes(Convert.ToHexString(data))));
			for (var offset = 0; offset <= size; offset += Math.Max(1, size / 7))
			{
				var length = size - offset;
				Assert.Equal(Convert.ToHexString(data, offset, length), ShadowConvertHex.ToHexString(data, offset, length));
				Assert.Equal(Convert.ToHexStringLower(data, offset, length), ShadowConvertHex.ToHexStringLower(data, offset, length));
			}
			for (var capacity = 0; capacity <= size * 2 + 1; capacity++)
			{
				CompareCharWrite(data, capacity, false);
				CompareCharWrite(data, capacity, true);
				CompareByteWrite(data, capacity, false);
				CompareByteWrite(data, capacity, true);
			}
		}
	}

	[Fact]
	public void MalformedAndIncrementalDecodingMatchHost()
	{
		var random = new Random(0x214857);
		for (var size = 0; size <= 64; size++)
		{
			var bytes = new byte[size];
			random.NextBytes(bytes);
			var valid = Convert.ToHexString(bytes);
			foreach (var text in new[] { valid, valid + "F", "G" + valid, valid + "z", "0X" + valid,
				valid + "\u00e9", valid + "\0", valid + " ", " " + valid, valid.ToLowerInvariant() })
			{
				CompareAllocatingDecode(text);
				var utf8 = System.Text.Encoding.UTF8.GetBytes(text);
				CompareAllocatingDecode(utf8);
				for (var capacity = 0; capacity <= Math.Min(size + 1, 17); capacity++)
				{
					CompareCharDecode(text, capacity);
					CompareByteDecode(utf8, capacity);
				}
			}
			for (var mutation = 0; mutation < 8 && mutation < valid.Length; mutation++)
			{
				var chars = valid.ToCharArray();
				chars[(mutation * 7) % chars.Length] = new[] { 'G', '/', ':', '\0', '\uff10', '\u00e9', ' ', 'x' }[mutation];
				var text = new string(chars);
				CompareAllocatingDecode(text);
				for (var capacity = 0; capacity <= Math.Min(size + 1, 12); capacity++) CompareCharDecode(text, capacity);
			}
		}
	}

	[Fact]
	public void ArgumentValidationAndNullInputsMatchHost()
	{
		var data = new byte[4];
		foreach (var offset in new[] { -1, 0, 4, 5, int.MaxValue })
		foreach (var length in new[] { -1, 0, 1, 4, int.MaxValue })
		{
			CompareException(() => Convert.ToHexString(data, offset, length), () => ShadowConvertHex.ToHexString(data, offset, length));
			CompareException(() => Convert.ToHexStringLower(data, offset, length), () => ShadowConvertHex.ToHexStringLower(data, offset, length));
		}
		CompareException(() => Convert.ToHexString((byte[])null!), () => ShadowConvertHex.ToHexString((byte[])null!));
		CompareException(() => Convert.ToHexStringLower((byte[])null!), () => ShadowConvertHex.ToHexStringLower((byte[])null!));
		CompareException(() => Convert.ToHexString((byte[])null!, -1, -1), () => ShadowConvertHex.ToHexString((byte[])null!, -1, -1));
		CompareException(() => Convert.ToHexStringLower((byte[])null!, -1, -1), () => ShadowConvertHex.ToHexStringLower((byte[])null!, -1, -1));
		CompareException(() => Convert.FromHexString((string)null!), () => ShadowConvertHex.FromHexString((string)null!));
		CompareException(() => Convert.FromHexString((string)null!, new byte[1], out _, out _),
			() => ShadowConvertHex.FromHexString((string)null!, new byte[1], out _, out _));
	}

	private static void CompareCharWrite(byte[] source, int capacity, bool lower)
	{
		var host = Enumerable.Repeat('#', capacity + 2).ToArray();
		var target = (char[])host.Clone();
		var hostOk = lower ? Convert.TryToHexStringLower(source, host.AsSpan(1, capacity), out var hw) :
			Convert.TryToHexString(source, host.AsSpan(1, capacity), out hw);
		var targetOk = lower ? ShadowConvertHex.TryToHexStringLower(source, target.AsSpan(1, capacity), out var tw) :
			ShadowConvertHex.TryToHexString(source, target.AsSpan(1, capacity), out tw);
		Assert.Equal(hostOk, targetOk);
		Assert.Equal(hw, tw);
		Assert.Equal(host, target);
	}

	private static void CompareByteWrite(byte[] source, int capacity, bool lower)
	{
		var host = Enumerable.Repeat((byte)0xcc, capacity + 2).ToArray();
		var target = (byte[])host.Clone();
		var hostOk = lower ? Convert.TryToHexStringLower(source, host.AsSpan(1, capacity), out var hw) :
			Convert.TryToHexString(source, host.AsSpan(1, capacity), out hw);
		var targetOk = lower ? ShadowConvertHex.TryToHexStringLower(source, target.AsSpan(1, capacity), out var tw) :
			ShadowConvertHex.TryToHexString(source, target.AsSpan(1, capacity), out tw);
		Assert.Equal(hostOk, targetOk);
		Assert.Equal(hw, tw);
		Assert.Equal(host, target);
	}

	private static void CompareCharDecode(string text, int capacity)
	{
		var host = Enumerable.Repeat((byte)0xcc, capacity + 2).ToArray();
		var target = (byte[])host.Clone();
		var hs = Convert.FromHexString(text.AsSpan(), host.AsSpan(1, capacity), out var hc, out var hw);
		var ts = ShadowConvertHex.FromHexString(text.AsSpan(), target.AsSpan(1, capacity), out var tc, out var tw);
		Assert.Equal((hs, hc, hw), (ts, tc, tw));
		Assert.Equal(host, target);
		var ss = ShadowConvertHex.FromHexString(text, target.AsSpan(1, capacity), out tc, out tw);
		Assert.Equal((hs, hc, hw), (ss, tc, tw));
	}

	private static void CompareByteDecode(byte[] utf8, int capacity)
	{
		var host = Enumerable.Repeat((byte)0xcc, capacity + 2).ToArray();
		var target = (byte[])host.Clone();
		var hs = Convert.FromHexString((ReadOnlySpan<byte>)utf8, host.AsSpan(1, capacity), out var hc, out var hw);
		var ts = ShadowConvertHex.FromHexString((ReadOnlySpan<byte>)utf8, target.AsSpan(1, capacity), out var tc, out var tw);
		Assert.Equal((hs, hc, hw), (ts, tc, tw));
		Assert.Equal(host, target);
	}

	private static void CompareAllocatingDecode(string text) =>
		CompareException(() => Convert.FromHexString(text), () => ShadowConvertHex.FromHexString(text), true);
	private static void CompareAllocatingDecode(byte[] bytes) =>
		CompareException(() => Convert.FromHexString((ReadOnlySpan<byte>)bytes),
			() => ShadowConvertHex.FromHexString((ReadOnlySpan<byte>)bytes), true);

	private static void CompareException<T>(Func<T> host, Func<T> target, bool bytes = false)
	{
		Exception? he = null, te = null;
		T? hv = default, tv = default;
		try { hv = host(); } catch (Exception error) { he = error; }
		try { tv = target(); } catch (Exception error) { te = error; }
		Assert.Equal(he?.GetType(), te?.GetType());
		if (he is ArgumentException hArgument && te is ArgumentException tArgument)
			Assert.Equal(hArgument.ParamName, tArgument.ParamName);
		if (he is not null) return;
		if (bytes) Assert.Equal((byte[])(object)hv!, (byte[])(object)tv!);
		else Assert.Equal(hv, tv);
	}
}
