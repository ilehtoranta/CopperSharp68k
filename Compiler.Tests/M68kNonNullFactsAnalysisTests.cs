/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kNonNullFactsAnalysisTests
{
	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void ScratchStorageMatchesImmutableFactsAndTransferOrder(bool reverse, bool exceptionRoot)
	{
		for (var seed = 0; seed < 24; seed++)
		{
			var random = new Random(seed);
			var blocks = Enumerable.Range(0, 6 + seed % 9).Select(i => new M68kMachineBlock(13 + i * 7, i)).ToArray();
			var function = new M68kMachineFunction("non-null-storage-oracle", blocks[0].Id);
			function.Blocks.AddRange(reverse ? blocks.Reverse() : blocks);
			if (exceptionRoot) blocks[3].IsExceptionEntry = true;
			for (var i = 1; i < blocks.Length; i++) Connect(blocks[i - 1], blocks[i]);
			for (var i = 0; i < blocks.Length * 2; i++) Connect(blocks[random.Next(blocks.Length)], blocks[random.Next(blocks.Length)]);
			var values = Enumerable.Range(0, blocks.Length * 3).Select(_ => function.CreateValue(
				CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data)).ToArray();
			var generated = new Dictionary<int, int>();
			foreach (var block in blocks)
			{
				var instruction = function.CreateInstruction(M68kMachineOperation.Constant, block.StartIlOffset,
					definitions: [values[random.Next(values.Length)].Id]);
				block.Instructions.Add(instruction);
				generated.Add(instruction.Id, instruction.Definitions[0]);
			}
			// A phi at a cycle/merge must consult each predecessor's edge facts.
			var inputs = blocks[2].Predecessors.ToDictionary(p => p, _ => values[random.Next(values.Length)].Id);
			var phi = new M68kMachinePhi(values[^1].Id, inputs);
			blocks[2].Phis.Add(phi);
			var expectedTrace = new List<string>();
			var actualTrace = new List<string>();
			void Transfer(M68kMachineInstruction instruction, ISet<int> facts, List<string> trace)
			{
				trace.Add("instruction:" + instruction.Id);
				facts.Add(generated[instruction.Id]);
				if (facts.Contains(values[0].Id)) facts.Add(values[1].Id);
			}
			void Edge(M68kMachineBlock block, int successor, ISet<int> facts, List<string> trace)
			{
				trace.Add($"edge:{block.Id}:{successor}");
				facts.Add(values[(block.Id + successor) % values.Length].Id);
			}
			var expected = ImmutableOracle(function,
				(i, f) => Transfer(i, f, expectedTrace), (b, s, f) => Edge(b, s, f, expectedTrace));
			var actual = M68kNonNullFactsAnalysis.Compute(function,
				(i, f) => Transfer(i, f, actualTrace), (b, s, f) => Edge(b, s, f, actualTrace));
			Assert.Equal(expectedTrace, actualTrace);
			foreach (var block in blocks) Assert.True(expected[block.Id].SetEquals(actual[block.Id]), $"Seed {seed}, block {block.Id}.");
			Assert.Equal(blocks.Length, actual.Values.Distinct(ReferenceEqualityComparer.Instance).Count());
		}
	}

	[Fact]
	public void UnchangedTopFactsLeaveIndependentStorage()
	{
		var function = new M68kMachineFunction("all-values-nonzero", 0);
		var value = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var blocks = Enumerable.Range(0, 3).Select(i => new M68kMachineBlock(i, i)).ToArray();
		function.Blocks.AddRange(blocks);
		Connect(blocks[0], blocks[1]); Connect(blocks[1], blocks[2]);
		blocks[0].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Constant, 0, definitions: [value.Id]));
		var entries = M68kNonNullFactsAnalysis.Compute(function, (_, facts) => facts.Add(value.Id), (_, _, _) => { });
		Assert.Empty(entries[0]);
		Assert.Contains(value.Id, entries[1]); Assert.Contains(value.Id, entries[2]);
		entries[1].Clear();
		Assert.Contains(value.Id, entries[2]);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(63)]
	[InlineData(64)]
	[InlineData(65)]
	[InlineData(127)]
	[InlineData(128)]
	[InlineData(129)]
	public void SparseValueIdsAndWordBoundariesRetainIndependentTopFacts(int count)
	{
		var function = new M68kMachineFunction("sparse-non-null-top", 13);
		var all = Enumerable.Range(0, count * 3).Select(_ => function.CreateValue(
			CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data)).ToArray();
		var values = all.Where((_, index) => index % 3 == 1).ToArray();
		foreach (var value in all.Where((_, index) => index % 3 != 1)) function.Values.Remove(value.Id);
		var blocks = Enumerable.Range(0, 3).Select(i => new M68kMachineBlock(13 + i * 7, i)).ToArray();
		function.Blocks.AddRange(blocks.Reverse());
		Connect(blocks[0], blocks[1]); Connect(blocks[1], blocks[2]);
		blocks[0].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Constant, 0));
		var entries = M68kNonNullFactsAnalysis.Compute(function,
			(_, facts) => { foreach (var value in values) facts.Add(value.Id); }, (_, _, _) => { });
		Assert.Empty(entries[blocks[0].Id]);
		Assert.True(entries[blocks[1].Id].SetEquals(values.Select(v => v.Id)));
		Assert.True(entries[blocks[2].Id].SetEquals(values.Select(v => v.Id)));
		Assert.Equal(3, entries.Values.Distinct(ReferenceEqualityComparer.Instance).Count());
		entries[blocks[1].Id].Clear();
		Assert.Equal(count, entries[blocks[2].Id].Count);
	}

	private static void Connect(M68kMachineBlock from, M68kMachineBlock to)
	{
		if (!from.Successors.Contains(to.Id)) from.Successors.Add(to.Id);
		if (!to.Predecessors.Contains(from.Id)) to.Predecessors.Add(from.Id);
	}

	private static IReadOnlyDictionary<int, ImmutableHashSet<int>> ImmutableOracle(M68kMachineFunction function,
		Action<M68kMachineInstruction, ISet<int>> transfer, Action<M68kMachineBlock, int, ISet<int>> transferEdge)
	{
		var universe = function.Values.Keys.ToImmutableHashSet();
		bool IsRoot(M68kMachineBlock b) => b.Id == function.EntryBlockId || b.IsExceptionEntry || b.Predecessors.Count == 0;
		var entries = function.Blocks.ToDictionary(b => b.Id, b => IsRoot(b) ? ImmutableHashSet<int>.Empty : universe);
		var edges = function.Blocks.SelectMany(b => b.Successors.Select(s => (b.Id, s))).ToDictionary(e => e, _ => universe);
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				var input = IsRoot(block) ? ImmutableHashSet<int>.Empty : block.Predecessors.Aggregate(universe, (set, p) => set.Intersect(edges[(p, block.Id)]));
				if (!IsRoot(block)) foreach (var phi in block.Phis)
					if (block.Predecessors.All(p => phi.Inputs.TryGetValue(p, out var v) && edges[(p, block.Id)].Contains(v))) input = input.Add(phi.Definition);
				if (!input.SetEquals(entries[block.Id])) { entries[block.Id] = input; changed = true; }
				var output = input.ToHashSet();
				foreach (var instruction in block.Instructions) transfer(instruction, output);
				foreach (var successor in block.Successors)
				{
					var edge = output.ToHashSet(); transferEdge(block, successor, edge);
					var next = edge.ToImmutableHashSet();
					if (!next.SetEquals(edges[(block.Id, successor)])) { edges[(block.Id, successor)] = next; changed = true; }
				}
			}
		}
		return entries;
	}
}
