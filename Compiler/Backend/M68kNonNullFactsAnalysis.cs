/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal static class M68kNonNullFactsAnalysis
{
	internal static IReadOnlyDictionary<int, HashSet<int>> Compute(
		M68kMachineFunction function,
		Action<M68kMachineInstruction, HashSet<int>> transfer,
		Action<M68kMachineBlock, int, HashSet<int>> transferEdge)
	{
		using var profile = M68kOptimizationProfiler.Measure("emission.non-null-facts");
		// The initial top state is read-only. A changed entry/edge gets its own
		// scratch set; a discarded top reference must never become scratch.
		var universe = function.Values.Keys.ToHashSet();
		var entries = function.Blocks.ToDictionary(static block => block.Id,
			block => IsRoot(block) ? new HashSet<int>() : universe);
		var edgeFacts = function.Blocks
			.SelectMany(block => block.Successors.Select(successor => (block.Id, successor)))
			.ToDictionary(static edge => edge, _ => universe);
		var incoming = new HashSet<int>();
		var outgoing = new HashSet<int>();
		var edge = new HashSet<int>();
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				incoming.Clear();
				if (!IsRoot(block))
				{
					// Meet from the smallest edge instead of repopulating the full
					// value universe at every visit. Keep the universe intersection
					// explicit, so the fixed-point equation is unchanged.
					var seed = universe;
					foreach (var predecessor in block.Predecessors)
						if (edgeFacts[(predecessor, block.Id)].Count < seed.Count) seed = edgeFacts[(predecessor, block.Id)];
					incoming.UnionWith(seed);
					incoming.IntersectWith(universe);
					foreach (var predecessor in block.Predecessors)
						incoming.IntersectWith(edgeFacts[(predecessor, block.Id)]);
					foreach (var phi in block.Phis)
						if (block.Predecessors.All(predecessor =>
							phi.Inputs.TryGetValue(predecessor, out var input) &&
							edgeFacts[(predecessor, block.Id)].Contains(input)))
							incoming.Add(phi.Definition);
				}

				// Capture the new state before exchanging its storage. Transferring
				// from the displaced old entry would use facts from the last round.
				var currentIncoming = incoming;
				var previous = entries[block.Id];
				if (!incoming.SetEquals(previous))
				{
					entries[block.Id] = incoming;
					incoming = ReferenceEquals(previous, universe) ? new HashSet<int>() : previous;
					changed = true;
				}
				outgoing.Clear();
				outgoing.UnionWith(currentIncoming);
				foreach (var instruction in block.Instructions) transfer(instruction, outgoing);
				foreach (var successor in block.Successors)
				{
					edge.Clear();
					edge.UnionWith(outgoing);
					transferEdge(block, successor, edge);
					var key = (block.Id, successor);
					previous = edgeFacts[key];
					if (!edge.SetEquals(previous))
					{
						edgeFacts[key] = edge;
						edge = ReferenceEquals(previous, universe) ? new HashSet<int>() : previous;
						changed = true;
					}
				}
			}
		}
		// Even an unchanged top entry must leave with independent mutable
		// storage, matching the ownership of every other returned entry.
		foreach (var block in function.Blocks)
			if (ReferenceEquals(entries[block.Id], universe)) entries[block.Id] = new(universe);
		return entries;

		bool IsRoot(M68kMachineBlock block) => block.Id == function.EntryBlockId ||
			block.IsExceptionEntry || block.Predecessors.Count == 0;
	}
}
