/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private readonly record struct PlatformBaseState(bool Visited, string? Identity);

	private Dictionary<int, string?> AnalyzePlatformBaseBlockEntries(
		CilMethod method,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new Dictionary<int, string?>();
		if (method.Instructions.Count == 0)
		{
			return result;
		}

		var offsetToIndex = new Dictionary<int, int>();
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			offsetToIndex.Add(method.Instructions[index].Offset, index);
		}

		var firstIndex = -1;
		for (var index = 0; index < method.Instructions.Count; index++)
		{
			if (reachableOffsets.Contains(method.Instructions[index].Offset))
			{
				firstIndex = index;
				break;
			}
		}

		if (firstIndex < 0)
		{
			return result;
		}

		var blockStarts = GetPlatformBaseBlockStarts(method.Instructions, reachableOffsets);
		var states = new Dictionary<int, PlatformBaseState>();
		var queue = new Queue<int>();
		MergePlatformBaseState(
			states,
			queue,
			method.Instructions[firstIndex].Offset,
			new PlatformBaseState(Visited: true, Identity: null));

		while (queue.Count != 0)
		{
			var offset = queue.Dequeue();
			var state = states[offset];
			var index = offsetToIndex[offset];
			var instruction = method.Instructions[index];
			var nextState = new PlatformBaseState(
				Visited: true,
				Identity: TransferPlatformBaseIdentity(method, instruction, state.Identity));

			foreach (var successor in GetPlatformBaseSuccessors(
				method.Instructions,
				index,
				reachableOffsets))
			{
				MergePlatformBaseState(states, queue, successor, nextState);
			}
		}

		foreach (var start in blockStarts)
		{
			if (states.TryGetValue(start, out var state))
			{
				result.Add(start, state.Identity);
			}
		}

		return result;
	}

	private static HashSet<int> GetPlatformBaseBlockStarts(
		IReadOnlyList<CilInstruction> instructions,
		IReadOnlySet<int> reachableOffsets)
	{
		var result = new HashSet<int>();
		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (!reachableOffsets.Contains(instruction.Offset))
			{
				continue;
			}

			if (result.Count == 0)
			{
				result.Add(instruction.Offset);
			}

			foreach (var target in GetBranchTargetOffsets(instruction))
			{
				if (reachableOffsets.Contains(target))
				{
					result.Add(target);
				}
			}

			if (InstructionCanFallThrough(instruction.OpCode) &&
				index + 1 < instructions.Count &&
				reachableOffsets.Contains(instructions[index + 1].Offset) &&
				instruction.OpCode.FlowControl is FlowControl.Cond_Branch)
			{
				result.Add(instructions[index + 1].Offset);
			}
		}

		return result;
	}

	private static IEnumerable<int> GetPlatformBaseSuccessors(
		IReadOnlyList<CilInstruction> instructions,
		int index,
		IReadOnlySet<int> reachableOffsets)
	{
		var instruction = instructions[index];
		foreach (var target in GetBranchTargetOffsets(instruction))
		{
			if (reachableOffsets.Contains(target))
			{
				yield return target;
			}
		}

		if (InstructionCanFallThrough(instruction.OpCode) &&
			index + 1 < instructions.Count &&
			reachableOffsets.Contains(instructions[index + 1].Offset))
		{
			yield return instructions[index + 1].Offset;
		}
	}

	private static IEnumerable<int> GetBranchTargetOffsets(CilInstruction instruction)
	{
		if (instruction.OpCode == OpCodes.Switch)
		{
			foreach (var target in (int[])instruction.Operand!)
			{
				yield return target;
			}
		}
		else if (instruction.OpCode.FlowControl is FlowControl.Branch or FlowControl.Cond_Branch &&
			instruction.Operand is int target)
		{
			yield return target;
		}
	}

	private static bool InstructionCanFallThrough(OpCode op) =>
		op.FlowControl is not FlowControl.Branch and not FlowControl.Return and not FlowControl.Throw;

	private string? TransferPlatformBaseIdentity(
		CilMethod method,
		CilInstruction instruction,
		string? currentIdentity)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Newarr)
		{
			return null;
		}

		if (op != OpCodes.Call &&
			op != OpCodes.Callvirt &&
			op != OpCodes.Newobj)
		{
			return currentIdentity;
		}

		var target = _module.ResolveMethodToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (target.Definition?.ExternalCall is { } externalCall)
		{
			return externalCall.Convention.Identity;
		}

		if (op == OpCodes.Newobj &&
			target.Definition is { } constructor &&
			_module.IsTransparentScalarConstructor(constructor))
		{
			return currentIdentity;
		}

		if (target.ImportName is { } importName)
		{
			return importName.StartsWith("intrinsic:amiga-library-base-set:", StringComparison.Ordinal)
				? null
				: currentIdentity;
		}

		return null;
	}

	private static void MergePlatformBaseState(
		Dictionary<int, PlatformBaseState> states,
		Queue<int> queue,
		int offset,
		PlatformBaseState incoming)
	{
		if (!states.TryGetValue(offset, out var existing) ||
			!existing.Visited)
		{
			states[offset] = incoming;
			queue.Enqueue(offset);
			return;
		}

		var mergedIdentity = existing.Identity == incoming.Identity
			? existing.Identity
			: null;
		if (mergedIdentity != existing.Identity)
		{
			states[offset] = new PlatformBaseState(Visited: true, Identity: mergedIdentity);
			queue.Enqueue(offset);
		}
	}

	private void ApplyPlatformBaseBlockEntry(string? identity)
	{
		_loadedPlatformBase =
			identity is not null &&
			_usedPlatformBases.TryGetValue(identity, out var platformBase)
				? platformBase
				: null;
	}
}
