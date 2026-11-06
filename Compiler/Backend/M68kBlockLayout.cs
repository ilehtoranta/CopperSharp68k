/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kBlockLayoutPlan(IReadOnlyList<int> BlockIds)
{
	internal IReadOnlySet<int> ColdBlockIds { get; init; } = new HashSet<int>();
	private sealed class Chain(M68kMachineBlock block)
	{
		internal List<M68kMachineBlock> Blocks { get; } = [block];
	}

	private sealed record Edge(
		M68kMachineBlock From,
		M68kMachineBlock To,
		int SuccessorIndex,
		bool TargetsEdgeBlock,
		bool TargetIsCold,
		bool StaysInLoop,
		bool IsUnconditional,
		bool CompetesWithConditionalEdge,
		int SourceOrder,
		int TargetOrder);

	internal static M68kBlockLayoutPlan Create(
		M68kMachineFunction function,
		M68kFinalDestinationPlan finalDestinations)
	{
		if (finalDestinations.EmittedBlockIds.Count == 0)
		{
			return new M68kBlockLayoutPlan(Array.Empty<int>());
		}

		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var emittedBlocks = finalDestinations.EmittedBlockIds
			.Select(blockId => blocks[blockId])
			.ToArray();
		var originalOrder = function.Blocks
			.Select((block, index) => (block.Id, index))
			.ToDictionary(static item => item.Id, static item => item.index);
		var chains = emittedBlocks.Select(static block => new Chain(block)).ToList();
		var chainByBlock = chains
			.SelectMany(static chain => chain.Blocks.Select(block => (block.Id, chain)))
			.ToDictionary(static item => item.Id, static item => item.chain);
		var conditionalTargets = emittedBlocks
			.Where(static block => block.Successors.Count > 1)
			.SelectMany(block => block.Successors.Select(finalDestinations.Resolve))
			.ToHashSet();
		var coldBlocks = FindColdBlocks(
			function.EntryBlockId,
			emittedBlocks,
			finalDestinations);

		var edges = emittedBlocks
			.Where(static block =>
				block.Instructions.LastOrDefault()?.Operation !=
					M68kMachineOperation.Switch)
			.SelectMany(block => block.Successors.Select((successorId, successorIndex) =>
			{
				var successor = blocks[finalDestinations.Resolve(successorId)];
				return new Edge(
					block,
					successor,
					successorIndex,
					IsEdgeBlock(successor, blocks),
					coldBlocks.Contains(successor.Id),
					successor.LoopDepth >= block.LoopDepth,
					block.Successors.Count == 1,
					block.Successors.Count == 1 &&
						(conditionalTargets.Contains(successor.Id) ||
						 (successor.Successors.Count > 1 &&
						  successor.Successors
							  .Select(finalDestinations.Resolve)
							  .Contains(block.Id))),
					originalOrder[block.Id],
					originalOrder[successor.Id]);
			}))
			.GroupBy(static edge => (edge.From.Id, edge.To.Id))
			.Select(static group => group
				.OrderByDescending(static edge => edge.SuccessorIndex)
				.First())
			// Do not let a split phi-copy edge take fallthrough away from
			// a loop body or normal path in favor of an exit or throw path.
			.OrderBy(static edge => edge.TargetIsCold)
			.ThenByDescending(static edge => edge.StaysInLoop)
			.ThenByDescending(static edge => edge.TargetsEdgeBlock)
			.ThenBy(static edge => edge.CompetesWithConditionalEdge)
			.ThenByDescending(static edge => edge.IsUnconditional)
			.ThenByDescending(static edge => edge.SuccessorIndex)
			.ThenByDescending(static edge => edge.From.LoopDepth)
			.ThenBy(static edge => edge.SourceOrder)
			.ThenBy(static edge => edge.TargetOrder)
			.ThenBy(static edge => edge.From.Id)
			.ThenBy(static edge => edge.To.Id)
			.ToArray();

		foreach (var edge in edges)
		{
			if (edge.To.Id == function.EntryBlockId || edge.To.IsExceptionEntry)
			{
				continue;
			}
			var sourceChain = chainByBlock[edge.From.Id];
			var targetChain = chainByBlock[edge.To.Id];
			if (sourceChain == targetChain ||
				sourceChain.Blocks[^1].Id != edge.From.Id ||
				targetChain.Blocks[0].Id != edge.To.Id)
			{
				continue;
			}

			sourceChain.Blocks.AddRange(targetChain.Blocks);
			foreach (var block in targetChain.Blocks)
			{
				chainByBlock[block.Id] = sourceChain;
			}
			chains.Remove(targetChain);
		}

		var entryChain = chainByBlock[function.EntryBlockId];
		var orderedChains = chains
			.Where(chain => chain != entryChain)
			.OrderBy(static chain => chain.Blocks[0].IsExceptionEntry)
			.ThenBy(chain => chain.Blocks.Any(block => coldBlocks.Contains(block.Id)))
			.ThenByDescending(static chain => chain.Blocks.Max(block => block.LoopDepth))
			.ThenBy(chain => chain.Blocks.Min(block => originalOrder[block.Id]))
			.ThenBy(static chain => chain.Blocks[0].Id)
			.Prepend(entryChain)
			.ToList();

		// A small side path that rejoins the start of a loop-bearing chain
		// should precede that chain, not sit beyond its entire loop body.
		// Keep every selected fallthrough intact and never move before entry.
		// Both cross-chain edges then stay near the chain head; this permits
		// short conditional branches without adding a jump to the main path.
		var chainEdges = emittedBlocks.SelectMany(block => block.Successors.Select(successor =>
			(From: block.Id, To: finalDestinations.Resolve(successor))))
			.Where(edge => chainByBlock[edge.From] != chainByBlock[edge.To]).ToArray();
		foreach (var main in orderedChains.Skip(1).ToArray())
		{
			if (!main.Blocks.Any(static block => block.LoopDepth > 0)) continue;
			var head = main.Blocks.Take(2).Select(static block => block.Id).ToHashSet();
			foreach (var side in orderedChains.Skip(1).ToArray())
			{
				if (side == main || side.Blocks.Sum(static block => block.Instructions.Count) > 16 ||
					side.Blocks.Any(block => block.LoopDepth != 0 || coldBlocks.Contains(block.Id))) continue;
				var incoming = chainEdges.Where(edge => chainByBlock[edge.To] == side).ToArray();
				var outgoing = chainEdges.Where(edge => chainByBlock[edge.From] == side).ToArray();
				if (incoming.Length == 0 || outgoing.Length == 0 ||
					!incoming.All(edge => head.Contains(edge.From)) ||
					!outgoing.All(edge => head.Contains(edge.To))) continue;
				orderedChains.Remove(side);
				orderedChains.Insert(orderedChains.IndexOf(main), side);
			}
		}

		var orderedBlocks = orderedChains
			.SelectMany(static chain => chain.Blocks)
			.Select(static block => block.Id)
			.ToArray();
		if (orderedBlocks.Length != emittedBlocks.Length ||
			orderedBlocks.Distinct().Count() != emittedBlocks.Length)
		{
			throw new InvalidOperationException(
				$"Block layout for '{function.DisplayName}' does not contain every emitted block exactly once.");
		}
		return new M68kBlockLayoutPlan(orderedBlocks) { ColdBlockIds = coldBlocks };
	}

	private static bool IsEdgeBlock(
		M68kMachineBlock block,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks) =>
		block.Predecessors.Count == 1 &&
		block.Successors.Count == 1 &&
		block.Instructions.LastOrDefault()?.Operation == M68kMachineOperation.Branch &&
		block.StartIlOffset == blocks[block.Successors[0]].StartIlOffset;

	private static bool IsIntrinsicCold(M68kMachineBlock block) =>
		block.IsExceptionEntry ||
		block.Instructions.LastOrDefault()?.Operation == M68kMachineOperation.Throw;

	private static HashSet<int> FindColdBlocks(
		int entryBlockId,
		IReadOnlyList<M68kMachineBlock> emittedBlocks,
		M68kFinalDestinationPlan finalDestinations)
	{
		var cold = emittedBlocks
			.Where(IsIntrinsicCold)
			.Select(static block => block.Id)
			.ToHashSet();
		bool changed;
		do
		{
			changed = false;
			foreach (var block in emittedBlocks)
			{
				if (block.Id == entryBlockId ||
					block.LoopDepth != 0 ||
					block.Successors.Count == 0 ||
					cold.Contains(block.Id))
				{
					continue;
				}
				if (block.Successors
					.Select(finalDestinations.Resolve)
					.All(cold.Contains))
				{
					cold.Add(block.Id);
					changed = true;
				}
			}
		}
		while (changed);
		return cold;
	}
}
