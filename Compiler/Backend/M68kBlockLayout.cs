/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kBlockLayoutPlan(IReadOnlyList<int> BlockIds)
{
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
			.OrderByDescending(static edge => edge.TargetsEdgeBlock)
			.ThenBy(static edge => edge.TargetIsCold)
			.ThenBy(static edge => edge.CompetesWithConditionalEdge)
			.ThenByDescending(static edge => edge.StaysInLoop)
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
			.Prepend(entryChain);

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
		return new M68kBlockLayoutPlan(orderedBlocks);
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
