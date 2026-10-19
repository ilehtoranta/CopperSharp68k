/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kLabelRetargetingTests
{
	[Theory]
	[InlineData(false, false, -10)]
	[InlineData(false, true, -10)]
	[InlineData(true, false, -10)]
	[InlineData(true, true, -10)]
	[InlineData(false, false, 14)]
	[InlineData(false, true, 14)]
	[InlineData(true, false, 14)]
	[InlineData(true, true, 14)]
	[InlineData(false, false, 100)]
	[InlineData(false, true, 100)]
	[InlineData(true, false, 100)]
	[InlineData(true, true, 100)]
	public void RangeMovePreservesAliasesBoundariesAndDictionarySlots(bool includeStart, bool includeEnd, int destination)
	{
		var map = new M68kOffsetMap();
		int[] positions = [22, 10, 8, 14, 20, 14, 18, 10, 20];
		for (var i = 0; i < positions.Length; i++) map.Add("entry" + i, positions[i] - 4);
		map.Remove("entry3");
		map.Add("replacement", 10); // Reuse a dictionary slot out of tree order.
		map.InsertBytes(0, 4, true);
		var original = map.ToArray(); // Cache positions before another deferred edit.
		map.InsertBytes(0, 2, true);
		map.MoveOffsets(12, 22, destination, includeStart, includeEnd);
		var expected = original.Select(item => new KeyValuePair<string, int>(item.Key,
			(item.Value > 10 || includeStart && item.Value == 10) &&
			(item.Value < 20 || includeEnd && item.Value == 20) ? destination : item.Value + 2)).ToArray();
		Assert.Equal(expected, map.ToArray());
		foreach (var item in expected) Assert.True(map.HasOffset(item.Value));
		map.InsertBytes(destination, 2, true);
		Assert.Equal(expected.Select(item => new KeyValuePair<string, int>(item.Key,
			item.Value >= destination ? item.Value + 2 : item.Value)), map.ToArray());
	}

	[Fact]
	public void RangeRetargetingPreservesActiveDictionaryEnumerators()
	{
		var map = new M68kOffsetMap { ["first"] = 10, ["second"] = 12, ["outside"] = 20 };
		using var entries = map.GetEnumerator();
		using var values = map.Values.GetEnumerator();
		Assert.True(entries.MoveNext());
		Assert.True(values.MoveNext());
		map.MoveOffsets(10, 20, 22);
		Assert.Equal(new("first", 10), entries.Current);
		Assert.Equal(10, values.Current);
		Assert.True(entries.MoveNext());
		Assert.True(values.MoveNext());
		Assert.Equal(new("second", 22), entries.Current);
		Assert.Equal(22, values.Current);
		Assert.Equal(new[] { 22, 22, 20 }, map.Values);
	}

	[Fact]
	public void ExactMovesAndEmptyIntervalsHandleIntegerExtremes()
	{
		var map = new M68kOffsetMap { ["min"] = int.MinValue, ["max"] = int.MaxValue, ["alias"] = int.MaxValue };
		var original = map.ToArray();
		map.MoveOffsets(10, 9, 0);
		map.MoveOffsets(int.MinValue, int.MinValue, 0);
		map.MoveOffsets(int.MaxValue, int.MaxValue, 0, includeStart: false, includeEnd: true);
		Assert.Equal(original, map.ToArray());
		map.MoveOffsetsAt(int.MaxValue, int.MinValue);
		Assert.All(map.Values, value => Assert.Equal(int.MinValue, value));
		Assert.False(map.HasOffset(int.MaxValue));
		map.MoveOffsetsAt(int.MinValue, int.MaxValue);
		Assert.All(map.Values, value => Assert.Equal(int.MaxValue, value));
		map.MoveOffsetsAt(42, 0);
		map.Clear();
		map.MoveOffsetsAt(0, 1);
		Assert.Empty(map);
	}

	[Theory]
	[InlineData(13)]
	[InlineData(7621)]
	public void RetargetingAndDeferredRebasesMatchIndependentFlatModel(int seed)
	{
		var map = new M68kOffsetMap();
		var flat = new Dictionary<string, int>(StringComparer.Ordinal);
		var random = new Random(seed);
		for (var i = 0; i < 512; i++) map.Add("entry" + i, flat["entry" + i] = random.Next(128));
		for (var step = 0; step < 4000; step++)
		{
			var start = random.Next(-16, 160);
			var end = start + random.Next(-2, 32);
			var target = random.Next(-16, 160);
			var includeStart = random.Next(2) == 0;
			var includeEnd = random.Next(2) == 0;
			switch (random.Next(5))
			{
				case 0:
					map.InsertBytes(start, 2, includeStart);
					foreach (var item in flat)
						if (item.Value > start || includeStart && item.Value == start) flat[item.Key] += 2;
					break;
				case 1:
					map.RemoveBytes(start, 2, false);
					foreach (var item in flat)
						if (item.Value >= start + 2) flat[item.Key] -= 2;
					break;
				case 2:
					map.MoveOffsetsAt(start, target);
					foreach (var item in flat) if (item.Value == start) flat[item.Key] = target;
					break;
				case 3:
					var key = "entry" + random.Next(512);
					map.Remove(key); flat.Remove(key);
					map.Add(key, target); flat.Add(key, target);
					break;
				default:
					map.MoveOffsets(start, end, target, includeStart, includeEnd);
					foreach (var item in flat)
						if ((item.Value > start || includeStart && item.Value == start) &&
							(item.Value < end || includeEnd && item.Value == end)) flat[item.Key] = target;
					break;
			}
			if (step % 17 == 0)
			{
				Assert.Equal(flat.ToArray(), map.ToArray());
				for (var probe = -16; probe < 180; probe += 7)
					Assert.Equal(flat.ContainsValue(probe), map.HasOffset(probe));
			}
		}
		Assert.Equal(flat.ToArray(), map.ToArray());
	}

	[Theory]
	[InlineData("RelocateLabels")]
	[InlineData("MoveLabelsAt")]
	public void OptimizerExactRetargetingMovesAllAliasesOnly(string helper)
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Labels.Add("before", 8);
		buffer.Labels.Add("entry", 10);
		buffer.Labels.Add("alias", 10);
		buffer.Labels.Add("after", 12);
		buffer.AnalysisAnchors.Add("anchor", 10);
		object optimizer = helper == "RelocateLabels"
			? new M68kPeepholeOptimizer(new M68kAssembler(), buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, [])
			: CreateConditionOptimizer(buffer);
		var method = optimizer.GetType().GetMethod(helper, BindingFlags.Instance | BindingFlags.NonPublic)!;
		method.CreateDelegate<Action<int, int>>(optimizer)(10, 12);
		Assert.Equal(new[] { 8, 12, 12, 12 }, buffer.Labels.Values);
		Assert.Equal(10, buffer.AnalysisAnchors["anchor"]);
	}

	private static object CreateConditionOptimizer(M68kAssemblyBuffer buffer) =>
		new M68kConditionCodeOptimizer(new M68kAssembler(), buffer);
}
