/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal static class M68kMachineLoopOptimizer
{
	public static int HoistLoopInvariants(M68kMachineFunction function)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var dominators = M68kControlFlowAnalysis.ComputeDominators(function);
		var definitions = function.Blocks
			.SelectMany(block => block.Instructions.SelectMany(instruction =>
				instruction.Definitions.Select(definition =>
					(Definition: definition, BlockId: block.Id))))
			.Concat(function.Blocks.SelectMany(block => block.Phis.Select(phi =>
				(Definition: phi.Definition, BlockId: block.Id))))
			.ToDictionary(static item => item.Definition, static item => item.BlockId);
		var hoisted = 0;
		foreach (var backEdge in function.Blocks.SelectMany(source =>
			source.Successors
				.Where(header => dominators[source.Id].Contains(header))
				.Select(header => (Source: source.Id, Header: header))).ToArray())
		{
			var loop = DiscoverLoop(blocks, backEdge.Source, backEdge.Header);
			if (loop.SelectMany(blockId => blocks[blockId].Instructions).Any(instruction =>
				instruction.Operation == M68kMachineOperation.Call ||
				instruction.IsSafepoint || instruction.MayThrow ||
				instruction.Definitions.Concat(instruction.Uses).Any(value =>
					function.Values[value].PrecoloredRegister is not null)))
			{
				continue;
			}
			var outsidePredecessors = blocks[backEdge.Header].Predecessors
				.Where(predecessor => !loop.Contains(predecessor))
				.ToArray();
			if (outsidePredecessors is not [var preheaderId])
			{
				continue;
			}
			var preheader = blocks[preheaderId];
			if (preheader.Successors.Count != 1 ||
				preheader.ActiveExceptionRegionIds.SequenceEqual(
					blocks[backEdge.Header].ActiveExceptionRegionIds) == false)
			{
				continue;
			}
			var invariantValues = definitions
				.Where(item => !loop.Contains(item.Value))
				.Select(static item => item.Key)
				.ToHashSet();
			var insertAt = preheader.Instructions.Count != 0 &&
				preheader.Instructions[^1].Operation is
					M68kMachineOperation.Branch or
					M68kMachineOperation.ConditionalBranch or
					M68kMachineOperation.Switch
					? preheader.Instructions.Count - 1
					: preheader.Instructions.Count;
			bool changed;
			do
			{
				changed = false;
				foreach (var blockId in loop.Order())
				{
					var block = blocks[blockId];
					if (!block.ActiveExceptionRegionIds.SequenceEqual(
						preheader.ActiveExceptionRegionIds))
					{
						continue;
					}
					for (var index = 0; index < block.Instructions.Count; index++)
					{
						var instruction = block.Instructions[index];
						if (!CanHoist(function, instruction) ||
							instruction.Uses.Any(use => !invariantValues.Contains(use)))
						{
							continue;
						}
						block.Instructions.RemoveAt(index--);
						preheader.Instructions.Insert(insertAt++, instruction);
						foreach (var definition in instruction.Definitions)
						{
							invariantValues.Add(definition);
						}
						hoisted++;
						changed = true;
					}
				}
			}
			while (changed);
		}
		if (hoisted != 0)
		{
			M68kMachineIrVerifier.Verify(function);
		}
		return hoisted;
	}

	private static HashSet<int> DiscoverLoop(
		IReadOnlyDictionary<int, M68kMachineBlock> blocks,
		int source,
		int header)
	{
		var loop = new HashSet<int> { source, header };
		var pending = new Stack<int>();
		if (source != header)
		{
			pending.Push(source);
		}
		while (pending.TryPop(out var block))
		{
			foreach (var predecessor in blocks[block].Predecessors)
			{
				if (loop.Add(predecessor))
				{
					pending.Push(predecessor);
				}
			}
		}
		return loop;
	}

	private static bool CanHoist(
		M68kMachineFunction function,
		M68kMachineInstruction instruction) =>
		instruction.Definitions.Length == 1 &&
		instruction.Definitions.Concat(instruction.Uses).All(value =>
			function.Values[value].PrecoloredRegister is null &&
			function.Values[value].AllowedRegisters == M68kRegisterSet.Data &&
			function.Values[value].Width == M68kMachineValueWidth.Long) &&
		instruction.MemoryEffect == M68kMachineMemoryEffect.None &&
		!instruction.MayThrow && !instruction.IsSafepoint &&
		!instruction.ProducesConditionCodes && !instruction.ConsumesConditionCodes &&
		instruction.Clobbers.IsEmpty && instruction.LogicalCall is null &&
		instruction.Operation is
			M68kMachineOperation.Constant or
			M68kMachineOperation.Add or
			M68kMachineOperation.Subtract or
			M68kMachineOperation.Multiply or
			M68kMachineOperation.And or
			M68kMachineOperation.Or or
			M68kMachineOperation.Xor or
			M68kMachineOperation.Negate or
			M68kMachineOperation.Not or
			M68kMachineOperation.Shift;
}
