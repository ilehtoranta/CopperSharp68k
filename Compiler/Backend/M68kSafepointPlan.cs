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
		var immortalReferences = AnalyzeImmortalReferences(function);
		var safepoints = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.IsSafepoint)
			.Select(instruction => new M68kSafepoint(
				instruction.Id,
				instruction.IlOffset,
				liveness.LiveBefore[instruction.Id]
					.Where(value =>
						function.Values[value].IsGcReference &&
						!immortalReferences.Contains(value))
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

	private static HashSet<int> AnalyzeImmortalReferences(
		M68kMachineFunction function)
	{
		var immortal = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(instruction =>
				instruction.Operation == M68kMachineOperation.Address &&
				instruction.Definitions.Length == 1 &&
				function.Values[instruction.Definitions[0]].IsGcReference)
			.Select(static instruction => instruction.Definitions[0])
			.ToHashSet();
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var instruction in function.Blocks.SelectMany(
				static block => block.Instructions))
			{
				if (instruction is
					{
						Operation: M68kMachineOperation.Copy,
						Uses: [var source],
						Definitions: [var destination]
					} &&
					immortal.Contains(source) &&
					function.Values[destination].IsGcReference)
				{
					changed |= immortal.Add(destination);
				}
			}
			foreach (var phi in function.Blocks.SelectMany(static block => block.Phis))
			{
				if (phi.Inputs.Count != 0 &&
					phi.Inputs.Values.All(immortal.Contains) &&
					function.Values[phi.Definition].IsGcReference)
				{
					changed |= immortal.Add(phi.Definition);
				}
			}
		}
		return immortal;
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
			// Missing entries are unknown at block entry. A present null value is
			// a slot proven clear; otherwise the value id is the reference already
			// published in that compiler-owned root slot.
			var knownRootValues = new Dictionary<int, int?>();
			foreach (var instruction in block.Instructions)
			{
				if (safepoints.TryGetValue(instruction.Id, out var safepoint))
				{
					foreach (var value in safepoint.LiveReferences
						.OrderBy(value => plan.RootSlotByValue[value]))
					{
						var slot = plan.RootSlotByValue[value];
						if (knownRootValues.TryGetValue(slot, out var current) &&
							current == value)
						{
							continue;
						}
						rewritten.Add(function.CreateInstruction(
							M68kMachineOperation.RootStore,
							instruction.IlOffset,
							uses: [value],
							memoryEffect: M68kMachineMemoryEffect.Write,
							spillSlotIndex: slot));
						knownRootValues[slot] = value;
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
						if (knownRootValues.TryGetValue(slot, out var current) &&
							current is null)
						{
							continue;
						}
						rewritten.Add(function.CreateInstruction(
							M68kMachineOperation.RootClear,
							instruction.IlOffset,
							memoryEffect: M68kMachineMemoryEffect.Write,
							spillSlotIndex: slot));
						knownRootValues[slot] = null;
					}
				}
			}
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
		RemoveRedundantRootStateWrites(function, plan);
		M68kMachineIrVerifier.Verify(function);
	}

	private static void RemoveRedundantRootStateWrites(
		M68kMachineFunction function,
		M68kSafepointPlan plan)
	{
		const int EmptyRoot = -1;
		var entryStates = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new Dictionary<int, int>());
		var exitStates = new Dictionary<int, Dictionary<int, int>>();
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				var incoming = MeetPredecessorRootStates(
					function,
					block,
					exitStates,
					plan);
				if (!RootStatesEqual(entryStates[block.Id], incoming))
				{
					entryStates[block.Id] = incoming;
					changed = true;
				}

				var outgoing = new Dictionary<int, int>(incoming);
				foreach (var instruction in block.Instructions)
				{
					ApplyRootStateTransfer(
						instruction,
						outgoing,
						plan,
						EmptyRoot);
				}
				if (!exitStates.TryGetValue(block.Id, out var previous) ||
					!RootStatesEqual(previous, outgoing))
				{
					exitStates[block.Id] = outgoing;
					changed = true;
				}
			}
		}

		foreach (var block in function.Blocks)
		{
			var state = new Dictionary<int, int>(entryStates[block.Id]);
			var rewritten = new List<M68kMachineInstruction>();
			foreach (var instruction in block.Instructions)
			{
				var redundant = instruction.SpillSlotIndex is { } slot &&
					instruction.Operation switch
					{
						M68kMachineOperation.RootStore =>
							state.TryGetValue(slot, out var current) &&
							current == instruction.Uses[0],
						M68kMachineOperation.RootClear =>
							state.TryGetValue(slot, out var current) &&
							current == EmptyRoot,
						_ => false
					};
				ApplyRootStateTransfer(
					instruction,
					state,
					plan,
					EmptyRoot);
				if (!redundant)
				{
					rewritten.Add(instruction);
				}
			}
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
	}

	private static Dictionary<int, int> MeetPredecessorRootStates(
		M68kMachineFunction function,
		M68kMachineBlock block,
		IReadOnlyDictionary<int, Dictionary<int, int>> exitStates,
		M68kSafepointPlan plan)
	{
		Dictionary<int, int>? result = null;
		foreach (var predecessorId in block.Predecessors)
		{
			if (!exitStates.TryGetValue(predecessorId, out var predecessorState))
			{
				continue;
			}
			var edgeState = new Dictionary<int, int>(predecessorState);
			foreach (var phi in block.Phis)
			{
				if (!phi.Inputs.TryGetValue(predecessorId, out var input) ||
					!plan.RootSlotByValue.TryGetValue(phi.Definition, out var slot) ||
					!edgeState.TryGetValue(slot, out var current) ||
					current != input)
				{
					continue;
				}
				edgeState[slot] = phi.Definition;
			}

			if (result is null)
			{
				result = edgeState;
				continue;
			}
			foreach (var slot in result.Keys.ToArray())
			{
				if (!edgeState.TryGetValue(slot, out var value) ||
					value != result[slot])
				{
					result.Remove(slot);
				}
			}
		}
		return result ?? new Dictionary<int, int>();
	}

	private static void ApplyRootStateTransfer(
		M68kMachineInstruction instruction,
		IDictionary<int, int> state,
		M68kSafepointPlan plan,
		int emptyRoot)
	{
		if (instruction.SpillSlotIndex is { } slot)
		{
			if (instruction.Operation == M68kMachineOperation.RootStore)
			{
				state[slot] = instruction.Uses[0];
				return;
			}
			if (instruction.Operation == M68kMachineOperation.RootClear)
			{
				state[slot] = emptyRoot;
				return;
			}
		}

		if (instruction is not
			{
				Operation: M68kMachineOperation.Copy,
				Uses: [var source],
				Definitions: [var destination]
			} ||
			!plan.RootSlotByValue.TryGetValue(destination, out var destinationSlot) ||
			!state.TryGetValue(destinationSlot, out var current) ||
			current != source)
		{
			return;
		}
		state[destinationSlot] = destination;
	}

	private static bool RootStatesEqual(
		IReadOnlyDictionary<int, int> left,
		IReadOnlyDictionary<int, int> right) =>
		left.Count == right.Count &&
		left.All(pair =>
			right.TryGetValue(pair.Key, out var value) && value == pair.Value);
}
