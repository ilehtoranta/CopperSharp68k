/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kEffectOffsetRebaseTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(6)]
	public void NumericOrderingPreservesValuesAndExactDictionaryMutationOrder(int count)
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange(new byte[2048]);
		var random = new Random(134981);
		foreach (var key in new[] { -1, int.MaxValue, 511, 64, 0, 8, 66, 62, 500, 20, 63, 65 })
		{
			buffer.InstructionEffectOverrides[key] = default(M68kInstructionEffects) with { UsesData = (ushort)key };
		}
		for (var index = 0; index < 256; index++)
		{
			buffer.InstructionEffectOverrides[random.Next(2048)] =
				default(M68kInstructionEffects) with { DefinesAddress = (ushort)index };
		}
		var expected = new Dictionary<int, M68kInstructionEffects>(buffer.InstructionEffectOverrides);
		for (var step = 0; step < 100; step++)
		{
			var offset = random.Next(buffer.Bytes.Count - count + 1);
			var end = offset + count;
			// Reference the original remove/filter/key-selector ordering, including
			// dictionary free-slot reuse. Comparing sorted pairs would miss that.
			foreach (var key in expected.Keys.Where(key => key >= offset && key < end).ToArray())
			{
				expected.Remove(key);
			}
			foreach (var key in expected.Keys.Where(key => key >= end).OrderBy(key => key).ToArray())
			{
				var effect = expected[key];
				expected.Remove(key);
				expected.Add(key - count, effect);
			}
			buffer.RemoveBytes(offset, count);
			Assert.Equal(expected.ToArray(), buffer.InstructionEffectOverrides.ToArray());
			var replacement = random.Next(buffer.Bytes.Count);
			var value = default(M68kInstructionEffects) with
			{
				UsesAddress = (ushort)step, StackDelta = step, IsBarrier = step % 2 == 0
			};
			expected[replacement] = value;
			buffer.InstructionEffectOverrides[replacement] = value;
		}
	}

	[Fact]
	public void RemovingAllEffectEntriesAllowsFreshOverridesAtTheSameOffsets()
	{
		var buffer = new M68kAssemblyBuffer(); buffer.Bytes.AddRange(new byte[12]);
		var effect = default(M68kInstructionEffects) with { IsBarrier = true, StackDelta = -4 };
		buffer.InstructionEffectOverrides.Add(8, effect);
		buffer.InstructionEffectOverrides.Add(4, effect with { StackDelta = 4 });
		buffer.RemoveBytes(4, 8);
		Assert.Empty(buffer.InstructionEffectOverrides);
		buffer.InsertBytes(4, 8);
		buffer.InstructionEffectOverrides.Add(4, effect);
		buffer.RemoveBytes(0, 2);
		Assert.Equal(new KeyValuePair<int, M68kInstructionEffects>(2, effect),
			Assert.Single(buffer.InstructionEffectOverrides));
	}
}
