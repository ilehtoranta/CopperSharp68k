/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class GuidCoreLibFixtures
{
	private sealed class Holder { public int Before; public Guid Value; public int After; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Guid Transport(Guid value) => value;

	public static int BinaryEntry()
	{
		var original = new Guid(0x11223344, 0x5566, 0x7788, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff, 0x10);
		var values = new Guid[3];
		values[1] = original;
		var holder = new Holder { Before = 0x12345678, Value = Transport(values[1]), After = 0x76543210 };
		var returned = Transport(holder.Value);
		if (holder.Before != 0x12345678 || holder.After != 0x76543210) return 18;
		if (!returned.Equals(original) || returned != original || returned == default(Guid)) return 1;
		if (values[0] != default(Guid) || values[2] != default(Guid)) return 2;
		var little = original.ToByteArray();
		var big = original.ToByteArray(true);
		if (little[0] != 0x44 || little[1] != 0x33 || little[2] != 0x22 || little[3] != 0x11 ||
			little[4] != 0x66 || little[5] != 0x55 || little[6] != 0x88 || little[7] != 0x77) return 3;
		if (big[0] != 0x11 || big[1] != 0x22 || big[2] != 0x33 || big[3] != 0x44 ||
			big[4] != 0x55 || big[5] != 0x66 || big[6] != 0x77 || big[7] != 0x88) return 4;
		for (var i = 8; i < 16; i++) if (little[i] != big[i]) return 5;
		if (new Guid(little) != original || new Guid((ReadOnlySpan<byte>)little) != original ||
			new Guid((ReadOnlySpan<byte>)big, true) != original) return 6;
		Span<byte> destination = stackalloc byte[20];
		for (var i = 0; i < 20; i++) destination[i] = 0xcc;
		if (original.TryWriteBytes(destination.Slice(2, 15), true, out var written) || written != 0) return 7;
		for (var i = 0; i < 20; i++) if (destination[i] != 0xcc) return 8;
		if (!original.TryWriteBytes(destination.Slice(2, 16), true, out written) || written != 16) return 9;
		for (var i = 0; i < 16; i++) if (destination[i + 2] != big[i]) return 10;
		if (destination[0] != 0xcc || destination[1] != 0xcc || destination[18] != 0xcc || destination[19] != 0xcc) return 11;
		if (!original.TryWriteBytes(destination)) return 12;
		for (var i = 0; i < 16; i++) if (destination[i] != little[i]) return 13;
		if (original.GetHashCode() != returned.GetHashCode()) return 14;
		var unsigned = new Guid(unchecked((int)0x80000000), 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
		if (unsigned.CompareTo(original) <= 0 || original.CompareTo(unsigned) >= 0 || original.CompareTo(returned) != 0) return 15;
		var invalidThrew = false;
		try { _ = new Guid(new byte[15]); }
		catch (ArgumentException) { invalidThrew = true; }
		if (!invalidThrew) return 16;
		var nullThrew = false;
		try { _ = new Guid((byte[])null!); }
		catch (ArgumentNullException) { nullThrew = true; }
		return nullThrew ? 42 : 17;
	}

	public static int RandomnessRejectionEntry() => Guid.NewGuid().GetHashCode();
	public static int Version7RejectionEntry() => Guid.CreateVersion7().GetHashCode();
	public static string ProviderRejectionEntry() => default(Guid).ToString("D", null);
	public static bool Utf8RejectionEntry()
	{
		Span<byte> bytes = stackalloc byte[36];
		return default(Guid).TryFormat(bytes, out _, default);
	}
	public static Guid SpanParseRejectionEntry() => Guid.Parse("11223344-5566-7788-99aa-bbccddeeff10".AsSpan());

	public static int IncomingScalarAbiEntry()
	{
		if (IncomingBytes(1, 2, 128, 254, 3, 4, 5, 6, 7, 8, 9, 10) != 541) return 1;
		if (IncomingWords(1, 2, -32768, -1, 3, 4, 5, 6, 7, 8, 9, 10) != -32610) return 2;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IncomingBytes(byte a, byte b, byte c, byte d, byte e, byte f, byte g, byte h, byte i, byte j, byte k, byte l)
	{
		// Dynamic stack allocation exercises the A5-anchored prologue. The first
		// four ABI arguments survive the loop, making their homes cheap to spill.
		Span<byte> scratch = stackalloc byte[4];
		var total = 0;
		for (var n = 0; n < 3; n++)
		{
			scratch[n] = e;
			total += scratch[n] + f + g + h + i + j + k + l;
		}
		return total + a + b + c + d;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IncomingWords(short a, short b, short c, short d, short e, short f, short g, short h, short i, short j, short k, short l)
	{
		var total = 0;
		for (var n = 0; n < 3; n++) total += e + f + g + h + i + j + k + l;
		return total + a + b + c + d;
	}
}
