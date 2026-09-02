/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal static class M68kControlFlowCleanup
{
	public static int RemoveUnreachableBlocks(M68kMachineFunction function)
	{
		ArgumentNullException.ThrowIfNull(function);
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var reachable = new HashSet<int>();
		var pending = new Stack<int>(function.Blocks
			.Where(block => block.Id == function.EntryBlockId || block.IsExceptionEntry)
			.Select(static block => block.Id));
		while (pending.TryPop(out var blockId))
		{
			if (!reachable.Add(blockId))
			{
				continue;
			}
			foreach (var successor in blocks[blockId].ControlFlowSuccessors)
			{
				pending.Push(successor);
			}
		}

		var removed = function.Blocks
			.Where(block => !reachable.Contains(block.Id))
			.Select(static block => block.Id)
			.ToHashSet();
		if (removed.Count == 0)
		{
			return 0;
		}

		foreach (var block in function.Blocks.Where(block => reachable.Contains(block.Id)))
		{
			for (var index = 0; index < block.Phis.Count; index++)
			{
				var phi = block.Phis[index];
				block.Phis[index] = phi with
				{
					Inputs = phi.Inputs
						.Where(input => !removed.Contains(input.Key))
						.ToDictionary(static input => input.Key, static input => input.Value)
				};
			}
		}

		function.RemoveBlocks(removed);
		return removed.Count;
	}
}
