/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kSpillSlot(
	int Index,
	int Offset,
	int Size,
	bool IsGcRoot);

internal sealed record M68kSpillLayout(
	IReadOnlyDictionary<int, M68kSpillSlot> Slots,
	IReadOnlySet<int> RematerializedValues,
	int FrameBytes);

internal static class M68kSpillSlotAllocator
{
	public static M68kSpillLayout Allocate(
		M68kMachineFunction function,
		M68kInterferenceGraph interference,
		IReadOnlySet<int> spilledValues,
		int baseOffset = 0,
		int baseSlotIndex = 0)
	{
		var assigned = new Dictionary<int, M68kSpillSlot>();
		var rematerialized = new HashSet<int>();
		var slots = new List<(M68kSpillSlot Slot, List<int> Values)>();
		var nextOffset = baseOffset;
		foreach (var value in spilledValues
			.Select(value => function.Values[value])
			.OrderByDescending(static value => value.IsGcReference)
			.ThenByDescending(static value => (int)value.Width)
			.ThenByDescending(static value => value.SpillWeight)
			.ThenBy(static value => value.Id))
		{
			if (value.IsRematerializable)
			{
				rematerialized.Add(value.Id);
				continue;
			}

			var size = (int)value.Width;
			var reusable = slots.FirstOrDefault(candidate =>
				candidate.Slot.Size == size &&
				candidate.Slot.IsGcRoot == value.IsGcReference &&
				candidate.Values.All(other =>
					!interference.Neighbors(value.Id).Contains(other)));
			if (reusable.Slot is not null)
			{
				reusable.Values.Add(value.Id);
				assigned.Add(value.Id, reusable.Slot);
				continue;
			}

			nextOffset = Align(nextOffset, Math.Min(size, 4));
			var slot = new M68kSpillSlot(
				checked(baseSlotIndex + slots.Count),
				nextOffset,
				size,
				value.IsGcReference);
			nextOffset = checked(nextOffset + size);
			slots.Add((slot, [value.Id]));
			assigned.Add(value.Id, slot);
		}
		return new M68kSpillLayout(
			assigned,
			rematerialized,
			Align(nextOffset, 4));
	}

	public static void Verify(
		M68kMachineFunction function,
		M68kInterferenceGraph interference,
		IReadOnlySet<int> spilledValues,
		M68kSpillLayout layout)
	{
		foreach (var valueId in spilledValues)
		{
			if (!layout.Slots.ContainsKey(valueId) &&
				!layout.RematerializedValues.Contains(valueId))
			{
				throw new InvalidOperationException(
					$"Spilled value v{valueId} has no slot or rematerialization.");
			}
		}
		foreach (var (valueId, slot) in layout.Slots)
		{
			var value = function.Values[valueId];
			if (slot.Size != (int)value.Width ||
				slot.IsGcRoot != value.IsGcReference ||
				slot.Offset < 0 ||
				slot.Offset + slot.Size > layout.FrameBytes)
			{
				throw new InvalidOperationException(
					$"Spill slot {slot.Index} is invalid for v{valueId}.");
			}
			foreach (var neighbor in interference.Neighbors(valueId))
			{
				if (layout.Slots.TryGetValue(neighbor, out var other) &&
					other.Index == slot.Index)
				{
					throw new InvalidOperationException(
						$"Interfering values v{valueId} and v{neighbor} share spill slot {slot.Index}.");
				}
			}
		}
	}

	private static int Align(int value, int alignment) =>
		checked((value + alignment - 1) & -alignment);
}

internal static class M68kSpillRewriter
{
	public static void Rewrite(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		M68kInstructionLiveness? liveness = null)
	{
		var spilled = layout.Slots.Keys
			.Concat(layout.RematerializedValues)
			.ToHashSet();
		if (spilled.Count == 0)
		{
			return;
		}

		var definitions = FindInstructionDefinitions(function);
		var edgeInserter = new EdgeInserter(function);
		var phiInputClears = RewritePhiInputs(
			function,
			layout,
			spilled,
			definitions,
			edgeInserter);
		RewritePhiDefinitions(function, layout, spilled, edgeInserter);
		InsertPhiInputClears(function, layout, phiInputClears);
		RewriteInstructions(function, layout, spilled, definitions, liveness);
		foreach (var slot in layout.Slots.Values.Where(static slot => slot.IsGcRoot))
		{
			function.GcSpillSlots.Add(slot.Index);
		}

		var lingering = function.Blocks
			.SelectMany(static block =>
				block.Phis.SelectMany(static phi =>
					phi.Inputs.Values.Append(phi.Definition))
				.Concat(block.Instructions.SelectMany(static instruction =>
					instruction.Uses.Concat(instruction.Definitions))))
			.FirstOrDefault(spilled.Contains, -1);
		if (lingering >= 0)
		{
			throw new InvalidOperationException(
				$"Spill rewrite left v{lingering} in '{function.DisplayName}'.");
		}
		foreach (var value in spilled)
		{
			function.Values.Remove(value);
		}
		M68kMachineIrVerifier.Verify(function);
	}

	private static Dictionary<int, M68kMachineInstruction>
		FindInstructionDefinitions(M68kMachineFunction function)
	{
		var result = new Dictionary<int, M68kMachineInstruction>();
		foreach (var instruction in function.Blocks
			.SelectMany(static block => block.Instructions))
		{
			foreach (var definition in instruction.Definitions)
			{
				result.Add(definition, instruction);
			}
		}
		return result;
	}

	private static HashSet<(int BlockId, int Value)> RewritePhiInputs(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		IReadOnlySet<int> spilled,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		EdgeInserter edgeInserter)
	{
		var clears = new HashSet<(int BlockId, int Value)>();
		foreach (var block in function.Blocks.ToArray())
		{
			for (var phiIndex = 0; phiIndex < block.Phis.Count; phiIndex++)
			{
				var phi = block.Phis[phiIndex];
				foreach (var input in phi.Inputs.ToArray())
				{
					if (!spilled.Contains(input.Value))
					{
						continue;
					}
					var edgeBlock = edgeInserter.GetInsertionBlock(
						input.Key,
						block.Id);
					var replacement = CreateReloadOrRematerialization(
						function,
						layout,
						definitions,
						input.Value,
						block.StartIlOffset,
						edgeBlock,
						InsertBeforeTerminator(edgeBlock));
					phi = block.Phis[phiIndex];
					var inputs = phi.Inputs.ToDictionary(
						static item => item.Key,
						static item => item.Value);
					var currentPredecessor = block.Predecessors.Contains(input.Key)
						? input.Key
						: edgeBlock.Id;
					inputs[currentPredecessor] = replacement;
					phi = phi with { Inputs = inputs };
					block.Phis[phiIndex] = phi;
					if (function.Values[input.Value].IsGcReference &&
						layout.Slots.ContainsKey(input.Value))
					{
						clears.Add((edgeBlock.Id, input.Value));
					}
				}
			}
		}
		return clears;
	}

	private static void RewritePhiDefinitions(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		IReadOnlySet<int> spilled,
		EdgeInserter edgeInserter)
	{
		foreach (var block in function.Blocks.ToArray())
		{
			for (var phiIndex = block.Phis.Count - 1; phiIndex >= 0; phiIndex--)
			{
				var phi = block.Phis[phiIndex];
				if (!spilled.Contains(phi.Definition))
				{
					continue;
				}
				if (!layout.Slots.TryGetValue(phi.Definition, out var slot))
				{
					throw new InvalidOperationException(
						$"Phi v{phi.Definition} cannot be rematerialized.");
				}
				foreach (var input in phi.Inputs)
				{
					var edgeBlock = edgeInserter.GetInsertionBlock(
						input.Key,
						block.Id);
					edgeBlock.Instructions.Insert(
						InsertBeforeTerminator(edgeBlock),
						CreateStore(
							function,
							block.StartIlOffset,
							input.Value,
							slot.Index));
				}
				block.Phis.RemoveAt(phiIndex);
			}
		}
	}

	private static void RewriteInstructions(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		IReadOnlySet<int> spilled,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		M68kInstructionLiveness? liveness)
	{
		foreach (var block in function.Blocks.ToArray())
		{
			var rewritten = new List<M68kMachineInstruction>();
			foreach (var original in block.Instructions)
			{
				var instruction = original;
				var uses = instruction.Uses.ToArray();
				var deadGcSpills = uses
					.Where(value =>
						spilled.Contains(value) &&
						function.Values[value].IsGcReference &&
						layout.Slots.ContainsKey(value) &&
						(liveness is null ||
						 !liveness.LiveAfter[original.Id].Contains(value)))
					.Distinct()
					.ToArray();
				for (var index = 0; index < uses.Length; index++)
				{
					if (!spilled.Contains(uses[index]))
					{
						continue;
					}
					uses[index] = CreateReloadOrRematerialization(
						function,
						layout,
						definitions,
						uses[index],
						instruction.IlOffset,
						rewritten);
				}

				var stores = new List<M68kMachineInstruction>();
				var keptDefinitions = new List<int>();
				var removable = instruction.Definitions.Length != 0;
				foreach (var definition in instruction.Definitions)
				{
					if (!spilled.Contains(definition))
					{
						keptDefinitions.Add(definition);
						removable = false;
						continue;
					}
					if (layout.RematerializedValues.Contains(definition))
					{
						continue;
					}
					var temporary = CloneValue(function, definition);
					keptDefinitions.Add(temporary);
					stores.Add(CreateStore(
						function,
						instruction.IlOffset,
						temporary,
						layout.Slots[definition].Index));
					removable = false;
				}

				if (removable &&
					instruction.Operation is
						M68kMachineOperation.Constant or
						M68kMachineOperation.Address)
				{
					continue;
				}
				instruction = instruction with
				{
					Uses = uses.ToImmutableArray(),
					Definitions = keptDefinitions.ToImmutableArray()
				};
				rewritten.Add(instruction);
				rewritten.AddRange(stores);
				foreach (var value in deadGcSpills)
				{
					rewritten.Add(CreateClear(
						function,
						instruction.IlOffset,
						layout.Slots[value].Index,
						M68kMachineOperation.SpillClear));
				}
			}
			block.Instructions.Clear();
			block.Instructions.AddRange(rewritten);
		}
	}

	private static void InsertPhiInputClears(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		IEnumerable<(int BlockId, int Value)> clears)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		foreach (var group in clears.GroupBy(static item => item.BlockId))
		{
			var block = blocks[group.Key];
			var insertionIndex = InsertBeforeTerminator(block);
			foreach (var item in group)
			{
				block.Instructions.Insert(
					insertionIndex++,
					CreateClear(
						function,
						block.StartIlOffset,
						layout.Slots[item.Value].Index,
						M68kMachineOperation.SpillClear));
			}
		}
	}

	private static int CreateReloadOrRematerialization(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		int value,
		int ilOffset,
		M68kMachineBlock block,
		int insertionIndex)
	{
		var instructions = new List<M68kMachineInstruction>();
		var replacement = CreateReloadOrRematerialization(
			function,
			layout,
			definitions,
			value,
			ilOffset,
			instructions);
		block.Instructions.InsertRange(insertionIndex, instructions);
		return replacement;
	}

	private static int CreateReloadOrRematerialization(
		M68kMachineFunction function,
		M68kSpillLayout layout,
		IReadOnlyDictionary<int, M68kMachineInstruction> definitions,
		int value,
		int ilOffset,
		List<M68kMachineInstruction> instructions)
	{
		var temporary = CloneValue(function, value);
		if (layout.Slots.TryGetValue(value, out var slot))
		{
			instructions.Add(function.CreateInstruction(
				M68kMachineOperation.SpillLoad,
				ilOffset,
				definitions: [temporary],
				memoryEffect: M68kMachineMemoryEffect.Read,
				spillSlotIndex: slot.Index));
			return temporary;
		}

		if (!definitions.TryGetValue(value, out var definition) ||
			definition.Operation is not
				M68kMachineOperation.Constant and not
				M68kMachineOperation.Address ||
			definition.Uses.Length != 0)
		{
			throw new InvalidOperationException(
				$"Cannot rematerialize v{value} in '{function.DisplayName}'.");
		}
		instructions.Add(function.CreateInstruction(
			definition.Operation,
			ilOffset,
			definitions: [temporary],
			clobbers: definition.Clobbers,
			memoryEffect: definition.MemoryEffect,
			mayThrow: definition.MayThrow,
			producesConditionCodes: definition.ProducesConditionCodes,
			sourceInstruction: definition.SourceInstruction));
		return temporary;
	}

	private static int CloneValue(
		M68kMachineFunction function,
		int valueId)
	{
		var value = function.Values[valueId];
		return function.CreateValue(
			value.Kind,
			value.Width,
			value.AllowedRegisters,
			isGcReference: value.IsGcReference,
			spillWeight: value.SpillWeight,
			isSpillTemporary: true).Id;
	}

	private static M68kMachineInstruction CreateStore(
		M68kMachineFunction function,
		int ilOffset,
		int value,
		int slot) =>
		function.CreateInstruction(
			M68kMachineOperation.SpillStore,
			ilOffset,
			uses: [value],
			memoryEffect: M68kMachineMemoryEffect.Write,
			spillSlotIndex: slot);

	private static M68kMachineInstruction CreateClear(
		M68kMachineFunction function,
		int ilOffset,
		int slot,
		M68kMachineOperation operation) =>
		function.CreateInstruction(
			operation,
			ilOffset,
			memoryEffect: M68kMachineMemoryEffect.Write,
			spillSlotIndex: slot);

	private static int InsertBeforeTerminator(M68kMachineBlock block) =>
		block.Instructions.Count != 0 &&
		block.Instructions[^1].Operation is
			M68kMachineOperation.Branch or
			M68kMachineOperation.ConditionalBranch or
			M68kMachineOperation.Switch or
			M68kMachineOperation.Return or
			M68kMachineOperation.Throw
				? block.Instructions.Count - 1
				: block.Instructions.Count;

	private sealed class EdgeInserter
	{
		private readonly M68kMachineFunction _function;
		private readonly Dictionary<(int From, int To), M68kMachineBlock> _edges =
			new();
		private int _nextBlockId;

		public EdgeInserter(M68kMachineFunction function)
		{
			_function = function;
			_nextBlockId = function.Blocks.Max(static block => block.Id) + 1;
		}

		public M68kMachineBlock GetInsertionBlock(int fromId, int toId)
		{
			if (_edges.TryGetValue((fromId, toId), out var prior))
			{
				return prior;
			}
			var blocks = _function.Blocks.ToDictionary(static block => block.Id);
			var from = blocks[fromId];
			var to = blocks[toId];
			if (from.Successors.Count == 1)
			{
				_edges.Add((fromId, toId), from);
				return from;
			}

			var split = new M68kMachineBlock(_nextBlockId++, to.StartIlOffset);
			split.Predecessors.Add(fromId);
			split.Successors.Add(toId);
			split.Instructions.Add(_function.CreateInstruction(
				M68kMachineOperation.Branch,
				to.StartIlOffset));
			from.Successors[from.Successors.IndexOf(toId)] = split.Id;
			to.Predecessors[to.Predecessors.IndexOf(fromId)] = split.Id;
			for (var index = 0; index < to.Phis.Count; index++)
			{
				var phi = to.Phis[index];
				var inputs = phi.Inputs.ToDictionary(
					static item => item.Key,
					static item => item.Value);
				var input = inputs[fromId];
				inputs.Remove(fromId);
				inputs.Add(split.Id, input);
				to.Phis[index] = phi with { Inputs = inputs };
			}
			_function.Blocks.Add(split);
			_edges.Add((fromId, toId), split);
			_edges.Add((split.Id, toId), split);
			return split;
		}
	}
}

internal static class M68kCriticalEdgeSplitter
{
	public static void SplitPhiEdges(M68kMachineFunction function)
	{
		var nextBlockId = function.Blocks.Max(static block => block.Id) + 1;
		foreach (var target in function.Blocks
			.Where(static block => block.Phis.Count != 0)
			.ToArray())
		{
			foreach (var predecessorId in target.Predecessors.ToArray())
			{
				var predecessor = function.Blocks.Single(
					block => block.Id == predecessorId);
				if (predecessor.Successors.Count <= 1)
				{
					continue;
				}
				var split = new M68kMachineBlock(
					nextBlockId++,
					target.StartIlOffset);
				split.Predecessors.Add(predecessor.Id);
				split.Successors.Add(target.Id);
				split.Instructions.Add(function.CreateInstruction(
					M68kMachineOperation.Branch,
					target.StartIlOffset));
				predecessor.Successors[
					predecessor.Successors.IndexOf(target.Id)] = split.Id;
				target.Predecessors[
					target.Predecessors.IndexOf(predecessor.Id)] = split.Id;
				for (var index = 0; index < target.Phis.Count; index++)
				{
					var phi = target.Phis[index];
					var inputs = phi.Inputs.ToDictionary(
						static item => item.Key,
						static item => item.Value);
					var value = inputs[predecessor.Id];
					inputs.Remove(predecessor.Id);
					inputs.Add(split.Id, value);
					target.Phis[index] = phi with { Inputs = inputs };
				}
				function.Blocks.Add(split);
			}
		}
		M68kMachineIrVerifier.Verify(function);
	}
}

internal enum M68kStorageKind
{
	Register,
	SpillSlot,
	Temporary
}

internal readonly record struct M68kStorageLocation(
	M68kStorageKind Kind,
	int Index)
{
	public static M68kStorageLocation Register(M68kRegister register) =>
		new(M68kStorageKind.Register, (int)register);

	public static M68kStorageLocation Spill(int slot) =>
		new(M68kStorageKind.SpillSlot, slot);

	public static M68kStorageLocation Temporary(int index = 0) =>
		new(M68kStorageKind.Temporary, index);
}

internal readonly record struct M68kParallelCopy(
	M68kStorageLocation Destination,
	M68kStorageLocation Source);

internal static class M68kParallelCopyResolver
{
	public static IReadOnlyList<M68kParallelCopy> Resolve(
		IEnumerable<M68kParallelCopy> copies,
		M68kStorageLocation temporary)
	{
		var original = copies
			.Where(static copy => copy.Destination != copy.Source)
			.ToArray();
		if (original.Select(static copy => copy.Destination).Distinct().Count() !=
			original.Length)
		{
			throw new InvalidOperationException(
				"Parallel copy has more than one source for a destination.");
		}

		var remaining = original.ToList();
		var result = new List<M68kParallelCopy>();
		while (remaining.Count != 0)
		{
			var readyIndex = remaining.FindIndex(copy =>
				!remaining.Any(other => other.Source == copy.Destination));
			if (readyIndex >= 0)
			{
				result.Add(remaining[readyIndex]);
				remaining.RemoveAt(readyIndex);
				continue;
			}

			var cycle = remaining[0];
			if (cycle.Source == temporary ||
				cycle.Destination == temporary)
			{
				throw new InvalidOperationException(
					"Parallel-copy temporary overlaps a cycle location.");
			}
			result.Add(new M68kParallelCopy(temporary, cycle.Source));
			for (var index = 0; index < remaining.Count; index++)
			{
				if (remaining[index].Source == cycle.Source)
				{
					remaining[index] = remaining[index] with
					{
						Source = temporary
					};
				}
			}
		}
		Verify(original, result, temporary);
		return result;
	}

	private static void Verify(
		IReadOnlyList<M68kParallelCopy> original,
		IReadOnlyList<M68kParallelCopy> resolved,
		M68kStorageLocation temporary)
	{
		var locations = original
			.SelectMany(static copy => new[] { copy.Source, copy.Destination })
			.Append(temporary)
			.Distinct();
		var contents = locations.ToDictionary(
			static location => location,
			static location => location);
		foreach (var copy in resolved)
		{
			if (!contents.TryGetValue(copy.Source, out var value))
			{
				throw new InvalidOperationException(
					"Resolved parallel copy reads an unknown source location.");
			}
			contents[copy.Destination] = value;
		}
		foreach (var copy in original)
		{
			if (contents[copy.Destination] != copy.Source)
			{
				throw new InvalidOperationException(
					"Resolved parallel copy overwrites a source before its last use.");
			}
		}
	}
}

internal sealed record M68kParallelCopyPlan(
	IReadOnlyDictionary<(int From, int To), IReadOnlyList<M68kParallelCopy>>
		EdgeCopies,
	bool NeedsTemporarySlot);

internal static class M68kParallelCopyPlanner
{
	public static M68kParallelCopyPlan Create(
		M68kMachineFunction function,
		M68kLivenessInfo liveness,
		M68kAllocationResult allocation)
	{
		var result = new Dictionary<
			(int From, int To),
			IReadOnlyList<M68kParallelCopy>>();
		var needsTemporary = false;
		foreach (var target in function.Blocks
			.Where(static block => block.Phis.Count != 0))
		{
			foreach (var predecessorId in target.Predecessors)
			{
				var copies = new List<M68kParallelCopy>();
				var narrow = false;
				foreach (var phi in target.Phis)
				{
					var destination = allocation.Registers[phi.Definition];
					var source = allocation.Registers[phi.Inputs[predecessorId]];
					AddLocationCopies(copies, destination, source);
					narrow |= function.Values[phi.Definition].Width is
						M68kMachineValueWidth.Byte or
						M68kMachineValueWidth.Word;
				}
				copies = copies
					.Where(static copy => copy.Destination != copy.Source)
					.Distinct()
					.ToList();
				if (copies.Count == 0)
				{
					continue;
				}

				var temporary = FindFreeTemporary(
					function,
					liveness,
					allocation,
					predecessorId,
					target,
					copies,
					narrow);
				if (temporary is null)
				{
					temporary = M68kStorageLocation.Temporary();
					needsTemporary = true;
				}
				result.Add(
					(predecessorId, target.Id),
					M68kParallelCopyResolver.Resolve(copies, temporary.Value));
			}
		}
		return new M68kParallelCopyPlan(result, needsTemporary);
	}

	private static void AddLocationCopies(
		ICollection<M68kParallelCopy> copies,
		M68kAllocatedLocation destination,
		M68kAllocatedLocation source)
	{
		copies.Add(new M68kParallelCopy(
			M68kStorageLocation.Register(destination.Register),
			M68kStorageLocation.Register(source.Register)));
		if (destination.IsPair)
		{
			if (!source.IsPair)
			{
				throw new InvalidOperationException(
					"Cannot copy a scalar physical location into a register pair.");
			}
			copies.Add(new M68kParallelCopy(
				M68kStorageLocation.Register(destination.Register + 1),
				M68kStorageLocation.Register(source.Register + 1)));
		}
		else if (source.IsPair)
		{
			throw new InvalidOperationException(
				"Cannot copy a register pair into a scalar physical location.");
		}
	}

	private static M68kStorageLocation? FindFreeTemporary(
		M68kMachineFunction function,
		M68kLivenessInfo liveness,
		M68kAllocationResult allocation,
		int predecessorId,
		M68kMachineBlock target,
		IReadOnlyList<M68kParallelCopy> copies,
		bool dataOnly)
	{
		var occupied = new HashSet<M68kRegister>();
		var edgeLive = new HashSet<int>(liveness.LiveIn[target.Id]);
		edgeLive.ExceptWith(target.Phis.Select(static phi => phi.Definition));
		foreach (var phi in target.Phis)
		{
			edgeLive.Add(phi.Inputs[predecessorId]);
		}
		foreach (var value in edgeLive)
		{
			occupied.UnionWith(
				allocation.Registers[value].OccupiedRegisters.Enumerate());
		}
		foreach (var copy in copies)
		{
			if (copy.Source.Kind == M68kStorageKind.Register)
			{
				occupied.Add((M68kRegister)copy.Source.Index);
			}
			if (copy.Destination.Kind == M68kStorageKind.Register)
			{
				occupied.Add((M68kRegister)copy.Destination.Index);
			}
		}
		var candidates = dataOnly
			? M68kRegisterSet.Data.Enumerate()
			: M68kRegisterSet.DataOrAddress.Enumerate();
		foreach (var register in candidates)
		{
			if (!occupied.Contains(register) &&
				!function.ReservedRegisters.Contains(register))
			{
				return M68kStorageLocation.Register(register);
			}
		}
		return null;
	}
}
