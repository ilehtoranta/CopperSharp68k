/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kOffsetIndexTests
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void InsertKeepsLabelAndAnchorBoundaryPoliciesIndependent(bool labels, bool anchors)
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange(Enumerable.Range(0, 12).Select(i => (byte)i));
		buffer.MarkDataStart();
		buffer.Bytes.AddRange([12, 13, 14, 15]);
		buffer.MarkWritableDataStart();
		buffer.Bytes.AddRange([16, 17, 18, 19]);
		buffer.MarkBssStart();
		buffer.Bytes.AddRange([20, 21, 22, 23]);
		int[] positions = [0, 7, 8, 8, 9, 24];
		for (var i = 0; i < positions.Length; i++)
		{
			buffer.Labels.Add("label" + i, positions[i]);
			buffer.AnalysisAnchors.Add("anchor" + i, positions[i]);
		}
		buffer.Branches.AddRange([new(7, "label0"), new(8, "label0", false), new(9, "label0")]);
		buffer.Addresses.AddRange([new(7, "external", true, -3), new(8, "label0", false, 4)]);
		buffer.PcRelative.AddRange([new(7, "label0"), new(8, "label0")]);
		buffer.InstructionEffectOverrides.Add(7, default);
		buffer.InstructionEffectOverrides.Add(8, default);
		buffer.InstructionEffectOverrides.Add(9, default);

		buffer.InsertBytes(8, 2, labels, anchors);

		Assert.Equal(positions.Select(p => p > 8 || labels && p == 8 ? p + 2 : p), buffer.Labels.Values);
		Assert.Equal(positions.Select(p => p > 8 || anchors && p == 8 ? p + 2 : p), buffer.AnalysisAnchors.Values);
		Assert.Equal(new[] { 7, 10, 11 }, buffer.Branches.Select(b => b.OpcodeOffset));
		Assert.Equal(new AddressFixup[] { new(7, "external", true, -3), new(10, "label0", false, 4) }, buffer.Addresses);
		Assert.Equal(new[] { 7, 10 }, buffer.PcRelative.Select(p => p.DisplacementOffset));
		Assert.Equal(new[] { 7, 10, 11 }, buffer.InstructionEffectOverrides.Keys.Order());
		Assert.Equal(14, buffer.DataStartOffset);
		Assert.Equal(18, buffer.WritableDataStartOffset);
		Assert.Equal(22, buffer.BssStartOffset);
		Assert.Equal(Enumerable.Range(0, 8).Concat([0, 0]).Concat(Enumerable.Range(8, 16)).Select(i => (byte)i), buffer.Bytes);
	}

	[Fact]
	public void KeptInteriorLabelsCanCrossTheShiftedSuffixWithoutBreakingTheIndex()
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange(new byte[32]);
		foreach (var (name, value) in new[] { ("inside", 14), ("end", 20), ("after", 22), ("alias", 14) })
		{
			buffer.Labels.Add(name, value);
			buffer.AnalysisAnchors.Add(name, value);
		}
		buffer.RemoveBytes(10, 10);
		Assert.Equal(new[] { 14, 10, 12, 14 }, buffer.Labels.Values);
		Assert.Equal(new[] { 10, 10, 12, 10 }, buffer.AnalysisAnchors.Values);
		Assert.True(buffer.HasLabelAt(14));
		Assert.True(buffer.HasLabelAt(10));
		Assert.False(buffer.HasLabelAt(20));
		buffer.InsertBytes(11, 2);
		Assert.Equal(new[] { 16, 10, 14, 16 }, buffer.Labels.Values);
		Assert.Equal(new[] { 10, 10, 14, 10 }, buffer.AnalysisAnchors.Values);
		Assert.True(buffer.HasLabelAt(16));
	}

	[Theory]
	[InlineData(false, 13)]
	[InlineData(false, 7621)]
	[InlineData(true, 13)]
	[InlineData(true, 7621)]
	public void MixedRebasesAndPointEditsMatchFlatDictionarySemantics(bool anchors, int seed)
	{
		var indexed = new M68kOffsetMap();
		var flat = new Dictionary<string, int>(StringComparer.Ordinal);
		var random = new Random(seed);
		for (var i = 0; i < 256; i++) Add("initial" + i, random.Next(1025));
		for (var step = 0; step < 4000; step++)
		{
			var offset = random.Next(1025);
			var count = random.Next(25);
			switch (random.Next(7))
			{
				case 0:
				case 1:
					var boundary = random.Next(2) == 0;
					indexed.InsertBytes(offset, count, boundary);
					foreach (var item in flat)
						if (item.Value > offset || boundary && item.Value == offset)
							flat[item.Key] = item.Value + count;
					break;
				case 2:
				case 3:
					indexed.RemoveBytes(offset, count, anchors);
					foreach (var item in flat)
						if (item.Value >= offset + count) flat[item.Key] = item.Value - count;
						else if (anchors && item.Value > offset) flat[item.Key] = offset;
					break;
				case 4:
					if (flat.Count != 0)
					{
						var key = flat.Keys.ElementAt(random.Next(flat.Count));
						// Also exercise equivalent strings with different identities.
						indexed[new string(key.ToCharArray())] = flat[key] = offset;
					}
					break;
				case 5: Add("added" + step, offset); break;
				case 6:
					if (flat.Count != 0)
					{
						var key = flat.Keys.ElementAt(random.Next(flat.Count));
						Assert.True(indexed.Remove(key));
						flat.Remove(key);
					}
					break;
			}
			// Leave stacked lazy shifts unread between observations as well as
			// exercising point queries after already cached enumerations.
			if (step % 17 == 0) Check();
		}
		Check();
		indexed.Clear(); flat.Clear();
		Add("after-clear", 42);
		Check();

		void Add(string key, int value) { indexed.Add(key, value); flat.Add(key, value); }
		void Check()
		{
			Assert.Equal(flat.ToArray(), indexed.ToArray());
			Assert.Equal(flat.Keys, indexed.Keys);
			Assert.Equal(flat.Values, indexed.Values);
			Assert.Equal(flat.Count, indexed.Count);
			Assert.Equal(flat, new Dictionary<string, int>(indexed, StringComparer.Ordinal));
			foreach (var item in flat)
			{
				Assert.Equal(item.Value, indexed[item.Key]);
				Assert.True(indexed.HasOffset(item.Value));
			}
			for (var probe = -1; probe <= 1050; probe += 31)
				Assert.Equal(flat.ContainsValue(probe), indexed.HasOffset(probe));
			Assert.False(indexed.TryGetValue("missing", out _));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BatchedDeletionMatchesIndependentFlatMappingAtEveryBoundary(bool anchors)
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange(new byte[64]);
		var map = anchors ? buffer.AnalysisAnchors : buffer.Labels;
		for (var offset = 0; offset <= 64; offset++) map.Add("entry" + offset, offset);
		(int Offset, int Count)[] ranges = [(40, 4), (10, 6), (16, 4), (25, 1)];
		buffer.RemoveByteRanges(ranges);
		for (var original = 0; original <= 64; original++)
		{
			var expected = original;
			foreach (var range in ranges.OrderByDescending(r => r.Offset))
			{
				if (expected >= range.Offset + range.Count) expected -= range.Count;
				else if (anchors && expected > range.Offset) expected = range.Offset;
			}
			Assert.Equal(expected, map["entry" + original]);
			Assert.True(map.HasOffset(expected));
		}
	}

	[Fact]
	public void ManySuffixEditsKeepLargeIncreasingEmissionAndAliasesStable()
	{
		var map = new M68kOffsetMap();
		for (var i = 0; i < 32768; i++) map.Add("entry" + i, i * 4);
		map.Add("alias", 0);
		for (var i = 0; i < 10000; i++)
		{
			map.InsertBytes(0, 2, shiftBoundary: false);
			map.RemoveBytes(0, 2, clampInterior: false);
		}
		for (var i = 0; i < 32768; i++) Assert.Equal(i * 4, map["entry" + i]);
		Assert.Equal(0, map["alias"]);
		Assert.Equal(32769, map.Count);
	}

	[Fact]
	public void DictionaryViewsAndExistingValueUpdatesRetainDictionarySlotOrder()
	{
		var indexed = new M68kOffsetMap();
		var flat = new Dictionary<string, int>(StringComparer.Ordinal);
		foreach (var name in new[] { "first", "second", "third", "fourth" })
		{
			indexed.Add(name, flat.Count * 4);
			flat.Add(name, flat.Count * 4);
		}
		indexed.InsertBytes(0, 2, true);
		foreach (var item in flat) flat[item.Key] += 2;
		indexed.Remove("second"); flat.Remove("second");
		indexed.Add("replacement", 100); flat.Add("replacement", 100);
		foreach (var item in indexed) indexed[item.Key] = item.Value + 6;
		foreach (var item in flat) flat[item.Key] += 6;
		var view = (IReadOnlyDictionary<string, int>)indexed;
		Assert.Equal(flat.Keys, view.Keys);
		Assert.Equal(flat.Values, view.Values);
		Assert.Equal(flat.ToArray(), indexed.ToArray());
		var copy = new KeyValuePair<string, int>[indexed.Count + 1];
		indexed.CopyTo(copy, 1);
		Assert.Equal(flat.ToArray(), copy.Skip(1));
	}

	[Fact]
	public void DuplicateAndMissingKeyOperationsLeaveTheIndexConsistent()
	{
		var map = new M68kOffsetMap();
		map.Add("key", 4);
		map.InsertBytes(0, 8, true);
		Assert.False(map.TryAdd(new string("key".ToCharArray()), 100));
		Assert.Throws<ArgumentException>(() => map.Add("key", 200));
		Assert.False(map.Remove("missing"));
		Assert.Contains(new KeyValuePair<string, int>("key", 12), map);
		Assert.False(map.Remove(new KeyValuePair<string, int>("key", 4)));
		Assert.Equal(12, map["key"]);
		Assert.True(map.HasOffset(12));
		Assert.False(map.HasOffset(100));
		Assert.Single(map);
		Assert.True(map.Remove(new KeyValuePair<string, int>("key", 12)));
		Assert.False(map.HasOffset(12));
		Assert.Empty(map);
	}

	[Fact]
	public void EnumeratorsCaptureCurrentBeforeSubsequentValueEdits()
	{
		var map = new M68kOffsetMap { ["first"] = 4, ["second"] = 8 };
		map.InsertBytes(0, 2, true);
		using var entries = map.GetEnumerator();
		using var values = map.Values.GetEnumerator();
		Assert.True(entries.MoveNext());
		Assert.True(values.MoveNext());
		map["first"] = 100;
		map["second"] = 200;
		Assert.Equal(new("first", 6), entries.Current);
		Assert.Equal(6, values.Current);
		Assert.True(entries.MoveNext());
		Assert.True(values.MoveNext());
		Assert.Equal(new("second", 200), entries.Current);
		Assert.Equal(200, values.Current);
		map.InsertBytes(0, 2, true);
		Assert.Equal(new[] { 102, 202 }, map.Values);
	}
}
