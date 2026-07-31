/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kSafepoint(
	int InstructionId,
	int IlOffset,
	IReadOnlySet<int> LiveReferences,
	IReadOnlySet<int> LiveSpillRootSlots);

internal sealed record M68kSafepointPlan(
	IReadOnlyList<M68kSafepoint> Safepoints,
	IReadOnlyDictionary<int, int> RootSlotByValue,
	int FirstRootSlot,
	int RootSlotCount);

internal static class M68kSafepointPlanner
{
	public static M68kSafepointPlan Create(
		M68kMachineFunction function,
		M68kInstructionLiveness liveness,
		int firstRootSlot = 0)
	{
		var spillRoots = AnalyzeSpillRoots(function);
		var safepoints = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.IsSafepoint)
			.Select(instruction => new M68kSafepoint(
				instruction.Id,
				instruction.IlOffset,
				liveness.LiveBefore[instruction.Id]
					.Where(value => function.Values[value].IsGcReference)
					.ToHashSet(),
				spillRoots[instruction.Id]))
			.ToArray();
		var conflicts = function.Values.Values
			.Where(static value => value.IsGcReference)
			.ToDictionary(
				static value => value.Id,
				static _ => new HashSet<int>());
		foreach (var safepoint in safepoints)
		{
			var values = safepoint.LiveReferences.ToArray();
			for (var left = 0; left < values.Length; left++)
			{
				for (var right = left + 1; right < values.Length; right++)
				{
					conflicts[values[left]].Add(values[right]);
					conflicts[values[right]].Add(values[left]);
				}
			}
		}

		var slots = new Dictionary<int, int>();
		foreach (var value in safepoints
			.SelectMany(static safepoint => safepoint.LiveReferences)
			.Distinct()
			.OrderByDescending(value => conflicts[value].Count)
			.ThenBy(static value => value))
		{
			var used = conflicts[value]
				.Where(slots.ContainsKey)
				.Select(neighbor => slots[neighbor])
				.ToHashSet();
			var slot = firstRootSlot;
			while (used.Contains(slot))
			{
				slot++;
			}
			slots.Add(value, slot);
		}
		var plan = new M68kSafepointPlan(
			safepoints,
			slots,
			firstRootSlot,
			slots.Values.Distinct().Count());
		Verify(function, plan);
		return plan;
	}

	private static IReadOnlyDictionary<int, HashSet<int>> AnalyzeSpillRoots(
		M68kMachineFunction function)
	{
		var liveIn = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new HashSet<int>());
		var liveOut = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new HashSet<int>());
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				var incoming = block.Predecessors
					.SelectMany(predecessor => liveOut[predecessor])
					.ToHashSet();
				var outgoing = SimulateSpillRoots(
					function,
					block,
					incoming,
					null);
				if (!incoming.SetEquals(liveIn[block.Id]))
				{
					liveIn[block.Id] = incoming;
					changed = true;
				}
				if (!outgoing.SetEquals(liveOut[block.Id]))
				{
					liveOut[block.Id] = outgoing;
					changed = true;
				}
			}
		}

		var before = new Dictionary<int, HashSet<int>>();
		foreach (var block in function.Blocks)
		{
			SimulateSpillRoots(
				function,
				block,
				liveIn[block.Id],
				before);
		}
		return before;
	}

	private static HashSet<int> SimulateSpillRoots(
		M68kMachineFunction function,
		M68kMachineBlock block,
		IEnumerable<int> incoming,
		IDictionary<int, HashSet<int>>? before)
	{
		var live = incoming.ToHashSet();
		foreach (var instruction in block.Instructions)
		{
			before?.Add(instruction.Id, new HashSet<int>(live));
			if (instruction.SpillSlotIndex is not { } slot ||
				!function.GcSpillSlots.Contains(slot))
			{
				continue;
			}
			if (instruction.Operation == M68kMachineOperation.SpillStore)
			{
				live.Add(slot);
			}
			else if (instruction.Operation == M68kMachineOperation.SpillClear)
			{
				live.Remove(slot);
			}
		}
		return live;
	}

	public static void Verify(
		M68kMachineFunction function,
		M68kSafepointPlan plan)
	{
		foreach (var safepoint in plan.Safepoints)
		{
			var usedSlots = new HashSet<int>();
			foreach (var valueId in safepoint.LiveReferences)
			{
				if (!function.Values.TryGetValue(valueId, out var value) ||
					!value.IsGcReference)
				{
					throw new InvalidOperationException(
						$"Safepoint IL_{safepoint.IlOffset:X4} contains non-reference v{valueId}.");
				}
				if (!plan.RootSlotByValue.TryGetValue(valueId, out var slot))
				{
					throw new InvalidOperationException(
						$"Safepoint IL_{safepoint.IlOffset:X4} has no root slot for v{valueId}.");
				}
				if (!usedSlots.Add(slot))
				{
					throw new InvalidOperationException(
						$"Safepoint IL_{safepoint.IlOffset:X4} aliases live GC root slot {slot}.");
				}
			}
			foreach (var slot in safepoint.LiveSpillRootSlots)
			{
				if (!function.GcSpillSlots.Contains(slot))
				{
					throw new InvalidOperationException(
						$"Safepoint IL_{safepoint.IlOffset:X4} names non-GC spill slot {slot}.");
				}
				if (!usedSlots.Add(slot))
				{
					throw new InvalidOperationException(
						$"Safepoint IL_{safepoint.IlOffset:X4} aliases GC frame slot {slot}.");
				}
			}
		}
	}
}

internal static class M68kRootSynchronizer
{
	public static void Insert(
		M68kMachineFunction function,
		M68kSafepointPlan plan,
		M68kInstructionLiveness liveness)
	{
		var safepoints = plan.Safepoints.ToDictionary(
			static safepoint => safepoint.InstructionId);
		foreach (var block in function.Blocks)
		{
			var rewritten = new List<M68kMachineInstruction>();
			foreach (var instruction in block.Instructions)
			{
				if (safepoints.TryGetValue(instruction.Id, out var safepoint))
				{
					foreach (var value in safepoint.LiveReferences
						.OrderBy(value => plan.RootSlotByValue[value]))
					{
						rewritten.Add(function.CreateInstruction(
							M68kMachineOperation.RootStore,
							instruction.IlOffset,
							uses: [value],
							memoryEffect: M68kMachineMemoryEffect.Write,
							spillSlotIndex: plan.RootSlotByValue[value]));
					}
				}
				rewritten.Add(instruction);
				foreach (var value in instruction.Uses
					.Where(value =>
						function.Values[value].IsGcReference &&
						!liveness.LiveAfter[instruction.Id].Contains(value))
					.Distinct())
				{
					if (plan.RootSlotByValue.TryGetValue(value, out var slot))
					{
						rewritten.Add(function.CreateInstruction(
							M68kMachineOperation.RootClear,
							instruction.IlOffset,
							memoryEffect: M68kMachineMemoryEffect.Write,
							spillSlotIndex: slot));
					}
				}
			}
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
		M68kMachineIrVerifier.Verify(function);
	}
}
