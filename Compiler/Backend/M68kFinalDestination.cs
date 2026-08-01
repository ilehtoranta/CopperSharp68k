/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kFinalDestinationPlan(
	IReadOnlyDictionary<int, int> FinalDestinationByBlock,
	IReadOnlyList<int> EmittedBlockIds,
	IReadOnlyDictionary<int, IReadOnlyList<int>> AliasesByDestination)
{
	internal int Resolve(int blockId) => FinalDestinationByBlock[blockId];

	internal bool IsOmitted(int blockId) => Resolve(blockId) != blockId;

	internal static M68kFinalDestinationPlan Create(
		M68kMachineFunction function,
		M68kParallelCopyPlan parallelCopies)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var copiedBlocks = parallelCopies.EdgeCopies.Keys
			.SelectMany(static edge => new[] { edge.From, edge.To })
			.ToHashSet();
		var candidates = function.Blocks
			.Where(block => IsTransparent(
				function,
				block,
				copiedBlocks))
			.Select(static block => block.Id)
			.ToHashSet();
		var destinations = function.Blocks
			.Where(block => !candidates.Contains(block.Id))
			.ToDictionary(static block => block.Id, static block => block.Id);

		foreach (var candidate in function.Blocks
			.Where(block => candidates.Contains(block.Id))
			.Select(static block => block.Id))
		{
			ResolveCandidate(candidate);
		}

		var emitted = function.Blocks
			.Where(block => destinations[block.Id] == block.Id)
			.Select(static block => block.Id)
			.ToArray();
		var aliases = function.Blocks
			.Where(block => destinations[block.Id] != block.Id)
			.GroupBy(block => destinations[block.Id])
			.ToDictionary(
				static group => group.Key,
				static group => (IReadOnlyList<int>)group
					.Select(static block => block.Id)
					.ToArray());
		return new M68kFinalDestinationPlan(destinations, emitted, aliases);

		int ResolveCandidate(int start)
		{
			if (destinations.TryGetValue(start, out var known))
			{
				return known;
			}

			var path = new List<int>();
			var pathIndexes = new Dictionary<int, int>();
			var current = start;
			while (candidates.Contains(current) &&
				!destinations.ContainsKey(current))
			{
				if (pathIndexes.TryGetValue(current, out var cycleStart))
				{
					for (var index = cycleStart; index < path.Count; index++)
					{
						destinations[path[index]] = path[index];
					}
					for (var index = cycleStart - 1; index >= 0; index--)
					{
						var successor = blocks[path[index]].Successors[0];
						destinations[path[index]] = destinations[successor];
					}
					return destinations[start];
				}
				pathIndexes.Add(current, path.Count);
				path.Add(current);
				current = blocks[current].Successors[0];
			}

			var destination = destinations.TryGetValue(current, out known)
				? known
				: current;
			for (var index = path.Count - 1; index >= 0; index--)
			{
				destinations[path[index]] = destination;
			}
			return destination;
		}
	}

	private static bool IsTransparent(
		M68kMachineFunction function,
		M68kMachineBlock block,
		IReadOnlySet<int> copiedBlocks)
	{
		if (block.Id == function.EntryBlockId ||
			block.IsExceptionEntry ||
			block.Phis.Count != 0 ||
			block.Successors.Count != 1 ||
			block.Instructions.Count != 1 ||
			copiedBlocks.Contains(block.Id))
		{
			return false;
		}

		var instruction = block.Instructions[0];
		if (instruction.Operation != M68kMachineOperation.Branch ||
			instruction.Uses.Length != 0 ||
			instruction.Definitions.Length != 0 ||
			!instruction.Clobbers.IsEmpty ||
			instruction.MemoryEffect != M68kMachineMemoryEffect.None ||
			instruction.IsSafepoint ||
			instruction.MayThrow ||
			instruction.ProducesConditionCodes ||
			instruction.ConsumesConditionCodes ||
			instruction.SpillSlotIndex is not null ||
			instruction.ArgumentIndex is not null ||
			instruction.StackVarargsRegister is not null ||
			instruction.Immediate is not null ||
			instruction.BranchCondition is not null)
		{
			return false;
		}

		return instruction.SourceInstruction is not { } source ||
			source.OpCode == OpCodes.Br ||
			source.OpCode == OpCodes.Br_S;
	}
}
