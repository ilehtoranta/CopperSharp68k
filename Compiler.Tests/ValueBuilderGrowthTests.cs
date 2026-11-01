/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed class ValueBuilderGrowthTests
{
	[Fact]
	public void GrowthPreservesEmptyUnalignedAndReferenceContainingBuffers()
	{
		Assert.Equal(42, ValueBuilderGrowthFixtures.Bytes());
		Assert.Equal(42, ValueBuilderGrowthFixtures.Characters());
		Assert.Equal(42, ValueBuilderGrowthFixtures.References());
	}
}

public static class ValueBuilderGrowthFixtures
{
	public static int Bytes()
	{
		var empty = new ShadowValueListBuilder<byte>(default);
		empty.Append(42);
		if (empty.AsSpan().Length != 1 || empty.AsSpan()[0] != 42) return 10;
		empty.Dispose();
		byte[] original = [0xCC, 0, 0, 0, 0xDD, 0xEE];
		Span<byte> initial = original;
		var builder = new ShadowValueListBuilder<byte>(initial.Slice(1, 3));
		for (var index = 0; index < 97; index++) builder.Append((byte)(index ^ 0x5A));
		var values = builder.AsSpan();
		if (values.Length != 97) return 1;
		for (var index = 0; index < values.Length; index++) if (values[index] != (byte)(index ^ 0x5A)) return 2;
		if (original[0] != 0xCC || original[1] != 0x5A || original[2] != 0x5B || original[3] != 0x58 ||
			original[4] != 0xDD || original[5] != 0xEE) return 3;
		builder.Dispose();
		return 42;
	}

	public static int Characters()
	{
		char[] original = ['#', '\0', '\0', '\0', '#'];
		Span<char> initial = original;
		var builder = new ShadowValueListBuilder<char>(initial.Slice(1, 3));
		for (var index = 0; index < 97; index++) builder.Append((char)('A' + index % 23));
		var values = builder.AsSpan();
		if (values.Length != 97) return 4;
		for (var index = 0; index < values.Length; index++) if (values[index] != (char)('A' + index % 23)) return 5;
		if (original[0] != '#' || original[1] != 'A' || original[2] != 'B' || original[3] != 'C' || original[4] != '#') return 6;
		builder.Dispose();
		return 42;
	}

	public static int References()
	{
		var original = new Pair[3];
		Span<Pair> initial = original;
		var builder = new ShadowValueListBuilder<Pair>(initial.Slice(1, 1));
		for (var index = 0; index < 39; index++)
			builder.Append(new Pair { Item = new Payload { Value = index * 3 }, Ordinal = index });
		var values = builder.AsSpan();
		if (values.Length != 39) return 7;
		for (var index = 0; index < values.Length; index++)
			if (values[index].Ordinal != index || values[index].Item is null || values[index].Item!.Value != index * 3) return 8;
		if (original[0].Item is not null || original[2].Item is not null || original[1].Item is null || original[1].Item!.Value != 0) return 9;
		builder.Dispose();
		return 42;
	}

	public struct Pair { public Payload? Item; public int Ordinal; }
	public sealed class Payload { public int Value; }
}

