/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal static class M68kControlFlowAnalysis
{
	public static IReadOnlyDictionary<int, HashSet<int>> ComputeDominators(
		M68kMachineFunction function)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var roots = function.Blocks
			.Where(block =>
				block.Id == function.EntryBlockId ||
				block.IsExceptionEntry)
			.Select(static block => block.Id)
			.ToHashSet();
		var reachable = new HashSet<int>();
		var work = new Stack<int>(roots);
		while (work.TryPop(out var blockId))
		{
			if (!reachable.Add(blockId))
			{
				continue;
			}
			foreach (var successor in blocks[blockId].Successors)
			{
				work.Push(successor);
			}
		}
		if (reachable.Count != blocks.Count)
		{
			var missing = blocks.Keys.Where(id => !reachable.Contains(id));
			throw new InvalidOperationException(
				$"Invalid machine IR for '{function.DisplayName}': " +
				$"unreachable blocks {string.Join(",", missing)}.");
		}

		var dominators = new Dictionary<int, HashSet<int>>();
		foreach (var block in function.Blocks)
		{
			dominators.Add(
				block.Id,
				roots.Contains(block.Id)
					? new HashSet<int> { block.Id }
					: new HashSet<int>(reachable));
		}

		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				if (roots.Contains(block.Id))
				{
					continue;
				}
				HashSet<int>? intersection = null;
				foreach (var predecessor in block.Predecessors)
				{
					intersection = intersection is null
						? new HashSet<int>(dominators[predecessor])
						: new HashSet<int>(
							intersection.Intersect(dominators[predecessor]));
				}
				intersection ??= new HashSet<int>();
				intersection.Add(block.Id);
				if (!intersection.SetEquals(dominators[block.Id]))
				{
					dominators[block.Id] = intersection;
					changed = true;
				}
			}
		}
		return dominators;
	}

	public static void ComputeLoopDepths(M68kMachineFunction function)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var dominators = ComputeDominators(function);
		foreach (var block in function.Blocks)
		{
			block.LoopDepth = 0;
		}
		foreach (var source in function.Blocks)
		{
			foreach (var headerId in source.Successors)
			{
				if (!dominators[source.Id].Contains(headerId))
				{
					continue;
				}
				var loop = new HashSet<int> { headerId, source.Id };
				var work = new Stack<int>();
				if (source.Id != headerId)
				{
					work.Push(source.Id);
				}
				while (work.TryPop(out var blockId))
				{
					foreach (var predecessor in blocks[blockId].Predecessors)
					{
						if (loop.Add(predecessor))
						{
							work.Push(predecessor);
						}
					}
				}
				foreach (var blockId in loop)
				{
					blocks[blockId].LoopDepth++;
				}
			}
		}
	}
}

internal static class M68kMachineCostAnalysis
{
	private const long MaximumBlockWeight = 1_000_000_000;

	public static void Apply(M68kMachineFunction function)
	{
		var weights = function.Values.Keys.ToDictionary(
			static value => value,
			static _ => 0L);
		var rematerializable = new HashSet<int>();
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		foreach (var block in function.Blocks)
		{
			var blockWeight = LoopWeight(block.LoopDepth);
			foreach (var phi in block.Phis)
			{
				weights[phi.Definition] = SaturatingAdd(
					weights[phi.Definition],
					blockWeight);
				foreach (var (predecessor, input) in phi.Inputs)
				{
					weights[input] = SaturatingAdd(
						weights[input],
						LoopWeight(blocks[predecessor].LoopDepth));
				}
			}
			foreach (var instruction in block.Instructions)
			{
				foreach (var use in instruction.Uses)
				{
					weights[use] = SaturatingAdd(
						weights[use],
						checked(blockWeight * 2));
				}
				foreach (var definition in instruction.Definitions)
				{
					weights[definition] = SaturatingAdd(
						weights[definition],
						blockWeight);
					if (instruction.Operation is
						M68kMachineOperation.Constant or
						M68kMachineOperation.Address)
					{
						rematerializable.Add(definition);
					}
				}
			}
		}

		foreach (var valueId in function.Values.Keys.ToArray())
		{
			var value = function.Values[valueId];
			function.Values[valueId] = value with
			{
				IsRematerializable =
					value.IsRematerializable ||
					rematerializable.Contains(valueId),
				SpillWeight = Math.Max(1, weights[valueId])
			};
		}
	}

	private static long LoopWeight(int depth)
	{
		var result = 1L;
		for (var index = 0; index < depth; index++)
		{
			if (result >= MaximumBlockWeight / 10)
			{
				return MaximumBlockWeight;
			}
			result *= 10;
		}
		return result;
	}

	private static long SaturatingAdd(long left, long right) =>
		left >= long.MaxValue - right ? long.MaxValue : left + right;
}

internal sealed class M68kLivenessInfo
{
	public Dictionary<int, HashSet<int>> Use { get; } = new();

	public Dictionary<int, HashSet<int>> Def { get; } = new();

	public Dictionary<int, HashSet<int>> LiveIn { get; } = new();

	public Dictionary<int, HashSet<int>> LiveOut { get; } = new();
}

internal sealed record M68kInstructionLiveness(
	IReadOnlyDictionary<int, HashSet<int>> LiveBefore,
	IReadOnlyDictionary<int, HashSet<int>> LiveAfter);

internal static class M68kLivenessAnalysis
{
	public static M68kLivenessInfo Analyze(M68kMachineFunction function)
	{
		M68kMachineIrVerifier.Verify(function);
		var result = new M68kLivenessInfo();
		foreach (var block in function.Blocks)
		{
			var use = new HashSet<int>();
			var def = block.Phis
				.Select(static phi => phi.Definition)
				.ToHashSet();
			foreach (var instruction in block.Instructions)
			{
				foreach (var value in instruction.Uses)
				{
					if (!def.Contains(value))
					{
						use.Add(value);
					}
				}
				def.UnionWith(instruction.Definitions);
			}

			result.Use.Add(block.Id, use);
			result.Def.Add(block.Id, def);
			result.LiveIn.Add(block.Id, new HashSet<int>());
			result.LiveOut.Add(block.Id, new HashSet<int>());
		}

		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var changed = true;
		while (changed)
		{
			changed = false;
			for (var index = function.Blocks.Count - 1; index >= 0; index--)
			{
				var block = function.Blocks[index];
				var liveOut = new HashSet<int>();
				foreach (var successorId in block.Successors)
				{
					var successor = blocks[successorId];
					var edgeLive = new HashSet<int>(result.LiveIn[successorId]);
					edgeLive.ExceptWith(successor.Phis.Select(static phi => phi.Definition));
					foreach (var phi in successor.Phis)
					{
						if (phi.Inputs.TryGetValue(block.Id, out var input))
						{
							edgeLive.Add(input);
						}
					}
					liveOut.UnionWith(edgeLive);
				}

				var liveIn = new HashSet<int>(liveOut);
				liveIn.ExceptWith(result.Def[block.Id]);
				liveIn.UnionWith(result.Use[block.Id]);
				if (!liveOut.SetEquals(result.LiveOut[block.Id]))
				{
					result.LiveOut[block.Id] = liveOut;
					changed = true;
				}
				if (!liveIn.SetEquals(result.LiveIn[block.Id]))
				{
					result.LiveIn[block.Id] = liveIn;
					changed = true;
				}
			}
		}

		return result;
	}

	public static M68kInstructionLiveness AnalyzeInstructions(
		M68kMachineFunction function,
		M68kLivenessInfo blockLiveness)
	{
		var liveBefore = new Dictionary<int, HashSet<int>>();
		var liveAfter = new Dictionary<int, HashSet<int>>();
		foreach (var block in function.Blocks)
		{
			var live = new HashSet<int>(blockLiveness.LiveOut[block.Id]);
			for (var index = block.Instructions.Count - 1; index >= 0; index--)
			{
				var instruction = block.Instructions[index];
				liveAfter.Add(instruction.Id, new HashSet<int>(live));
				live.ExceptWith(instruction.Definitions);
				live.UnionWith(instruction.Uses);
				liveBefore.Add(instruction.Id, new HashSet<int>(live));
			}
		}
		return new M68kInstructionLiveness(liveBefore, liveAfter);
	}
}

internal sealed class M68kInterferenceGraph
{
	private readonly Dictionary<int, HashSet<int>> _neighbors;
	private readonly HashSet<(int Left, int Right)> _coalescableCopies = new();

	public M68kInterferenceGraph(IEnumerable<int> values)
	{
		_neighbors = values.ToDictionary(
			static value => value,
			static _ => new HashSet<int>());
	}

	public Dictionary<int, M68kRegisterSet> ForbiddenRegisters { get; } = new();

	public HashSet<(int Left, int Right)> CopyPreferences { get; } = new();

	public IReadOnlySet<int> Neighbors(int value) => _neighbors[value];

	public void AddEdge(int left, int right)
	{
		if (left == right || _coalescableCopies.Contains(Order(left, right)))
		{
			return;
		}
		_neighbors[left].Add(right);
		_neighbors[right].Add(left);
	}

	public void AddForbidden(int value, M68kRegisterSet registers)
	{
		ForbiddenRegisters.TryGetValue(value, out var prior);
		ForbiddenRegisters[value] = new M68kRegisterSet(
			(ushort)(prior.Bits | registers.Bits));
	}

	public void AddCopyPreference(int left, int right)
	{
		if (left != right)
		{
			CopyPreferences.Add(Order(left, right));
		}
	}

	public void AddCoalescableCopy(int left, int right)
	{
		if (left == right)
		{
			return;
		}
		var pair = Order(left, right);
		_coalescableCopies.Add(pair);
		CopyPreferences.Add(pair);
		_neighbors[left].Remove(right);
		_neighbors[right].Remove(left);
	}

	public void FinalizeCoalescableCopies()
	{
		var adjacency = _neighbors.Keys.ToDictionary(
			static value => value,
			static _ => new List<int>());
		foreach (var (left, right) in _coalescableCopies)
		{
			adjacency[left].Add(right);
			adjacency[right].Add(left);
		}
		var visited = new HashSet<int>();
		foreach (var start in adjacency.Keys.Order())
		{
			if (!visited.Add(start) || adjacency[start].Count == 0)
			{
				continue;
			}
			var component = new List<int>();
			var pending = new Stack<int>();
			pending.Push(start);
			while (pending.TryPop(out var value))
			{
				component.Add(value);
				foreach (var neighbor in adjacency[value])
				{
					if (visited.Add(neighbor))
					{
						pending.Push(neighbor);
					}
				}
			}
			foreach (var left in component)
			{
				foreach (var right in component)
				{
					_neighbors[left].Remove(right);
				}
			}
		}
	}

	private static (int Left, int Right) Order(int left, int right) =>
		left < right ? (left, right) : (right, left);
}

internal static class M68kInterferenceBuilder
{
	public static M68kInterferenceGraph Build(
		M68kMachineFunction function,
		M68kLivenessInfo liveness)
	{
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		if (!function.ReservedRegisters.IsEmpty)
		{
			foreach (var value in function.Values.Values)
			{
				if (value.PrecoloredRegister is null)
				{
					graph.AddForbidden(value.Id, function.ReservedRegisters);
				}
			}
		}
		foreach (var block in function.Blocks)
		{
			var live = new HashSet<int>(liveness.LiveOut[block.Id]);
			for (var index = block.Instructions.Count - 1; index >= 0; index--)
			{
				var instruction = block.Instructions[index];
				var liveAfter = new HashSet<int>(live);
				var copySource = instruction.Operation == M68kMachineOperation.Copy &&
					instruction.AllowCopyCoalescing &&
					instruction.Uses.Length == 1 &&
					instruction.Definitions.Length == 1
						? instruction.Uses[0]
						: (int?)null;
				var equivalentCopy = copySource is { } equivalentSource &&
					function.Values[equivalentSource].Kind ==
						function.Values[instruction.Definitions[0]].Kind &&
					function.Values[equivalentSource].Width ==
						function.Values[instruction.Definitions[0]].Width;
				for (var leftIndex = 0; leftIndex < instruction.Uses.Length; leftIndex++)
				{
					for (var rightIndex = leftIndex + 1;
						rightIndex < instruction.Uses.Length;
						rightIndex++)
					{
						graph.AddEdge(
							instruction.Uses[leftIndex],
							instruction.Uses[rightIndex]);
					}
				}

				foreach (var definition in instruction.Definitions)
				{
					foreach (var other in live)
					{
						if (other != copySource)
						{
							graph.AddEdge(definition, other);
						}
					}
				}
				if (copySource is { } source)
				{
					if (equivalentCopy)
					{
						graph.AddCoalescableCopy(instruction.Definitions[0], source);
					}
					else
					{
						graph.AddCopyPreference(instruction.Definitions[0], source);
					}
				}
				if (instruction.Operation == M68kMachineOperation.Subtract &&
					instruction.Uses.Length == 2 &&
					instruction.Definitions.Length == 1 &&
					instruction.Uses[0] != instruction.Uses[1])
				{
					graph.AddEdge(
						instruction.Definitions[0],
						instruction.Uses[1]);
				}
				live.ExceptWith(instruction.Definitions);
				live.UnionWith(instruction.Uses);
				if (!instruction.Clobbers.IsEmpty)
				{
					var liveAcross = new HashSet<int>(liveAfter);
					liveAcross.IntersectWith(live);
					foreach (var value in liveAcross)
					{
						graph.AddForbidden(value, instruction.Clobbers);
					}
				}
			}

			var phiDefinitions = block.Phis
				.Select(static phi => phi.Definition)
				.ToArray();
			for (var leftIndex = 0; leftIndex < phiDefinitions.Length; leftIndex++)
			{
				for (var rightIndex = leftIndex + 1;
					rightIndex < phiDefinitions.Length;
					rightIndex++)
				{
					graph.AddEdge(
						phiDefinitions[leftIndex],
						phiDefinitions[rightIndex]);
				}
				foreach (var value in liveness.LiveIn[block.Id])
				{
					if (!phiDefinitions.Contains(value))
					{
						graph.AddEdge(phiDefinitions[leftIndex], value);
					}
				}
			}
			foreach (var phi in block.Phis)
			{
				foreach (var input in phi.Inputs.Values)
				{
					graph.AddCopyPreference(phi.Definition, input);
				}
			}
		}

		graph.FinalizeCoalescableCopies();
		return graph;
	}
}

internal readonly record struct M68kAllocatedLocation(
	M68kRegister Register,
	bool IsPair)
{
	public M68kRegisterSet OccupiedRegisters =>
		IsPair
			? M68kRegisterSet.From(Register, Register + 1)
			: M68kRegisterSet.From(Register);
}

internal sealed record M68kAllocationResult(
	IReadOnlyDictionary<int, M68kAllocatedLocation> Registers,
	IReadOnlySet<int> SpilledValues)
{
	public bool IsSpilled(int value) => SpilledValues.Contains(value);
}

internal sealed record M68kMethodAllocationStatistics(
	string Method,
	int VirtualValues,
	int RegisterValues,
	int SpilledValues,
	int Reloads,
	int RematerializedValues,
	int CoalescedCopies,
	int BankTransfers,
	int CalleeSavedRegisters,
	int SpillFrameBytes,
	int GcRootSlots,
	int AllocationIterations,
	int CodeBytes,
	int StackMemoryInstructions);

internal sealed record M68kAllocatedFunction(
	M68kMachineFunction Function,
	M68kLivenessInfo BlockLiveness,
	M68kInstructionLiveness InstructionLiveness,
	M68kInterferenceGraph Interference,
	M68kAllocationResult Allocation,
	M68kSpillLayout Spills,
	M68kSafepointPlan Safepoints,
	M68kParallelCopyPlan ParallelCopies,
	M68kAllocatedFramePlan Frame,
	M68kFinalDestinationPlan FinalDestinations,
	M68kBlockLayoutPlan BlockLayout,
	M68kMethodAllocationStatistics Statistics);

internal static class M68kRegisterAllocatorPipeline
{
	private const int MaximumAllocationIterations = 16;

	public static M68kAllocatedFunction Run(
		M68kMachineFunction function,
		bool allowUntrackedManagedByrefs = false,
		bool allowCallerBorrowedByrefs = false,
		bool rejectManagedByrefReturn = false)
	{
		M68kByrefOwnerRooting.Insert(
			function,
			allowUntrackedManagedByrefs,
			allowCallerBorrowedByrefs,
			rejectManagedByrefReturn);
		M68kCriticalEdgeSplitter.SplitPhiEdges(function);
		var originalValueCount = function.Values.Count;
		var allSlots = new Dictionary<int, M68kSpillSlot>();
		var allRematerialized = new HashSet<int>();
		var frameBytes = 0;
		var spilledValueCount = 0;
		for (var iteration = 1;
			iteration <= MaximumAllocationIterations;
			iteration++)
		{
			M68kControlFlowAnalysis.ComputeLoopDepths(function);
			M68kMachineCostAnalysis.Apply(function);
			var blockLiveness = M68kLivenessAnalysis.Analyze(function);
			var instructionLiveness = M68kLivenessAnalysis.AnalyzeInstructions(
				function,
				blockLiveness);
			var interference = M68kInterferenceBuilder.Build(
				function,
				blockLiveness);
			var allocation = M68kGraphColoringAllocator.Allocate(
				function,
				interference);
			M68kGraphColoringAllocator.VerifyAllocation(
				function,
				interference,
				allocation);
			if (allocation.SpilledValues.Count == 0)
			{
				var safepoints = M68kSafepointPlanner.Create(
					function,
					instructionLiveness,
					allSlots.Count);
				var parallelCopies = M68kParallelCopyPlanner.Create(
					function,
					blockLiveness,
					allocation);
				var spills = new M68kSpillLayout(
					allSlots,
					allRematerialized,
					frameBytes);
				var frame = M68kAllocatedFramePlanner.Create(
					function,
					allocation,
					spills,
					safepoints,
					parallelCopies);
				M68kRootSynchronizer.Insert(function, safepoints, instructionLiveness);
				var finalDestinations = M68kFinalDestinationPlan.Create(
					function,
					parallelCopies);
				var blockLayout = M68kBlockLayoutPlan.Create(
					function,
					finalDestinations);
				var statistics = M68kAllocationStatistics.Create(
					function,
					interference,
					allocation,
					spills,
					safepoints,
					iteration,
					originalValueCount,
					spilledValueCount);
				return new M68kAllocatedFunction(
					function,
					blockLiveness,
					instructionLiveness,
					interference,
					allocation,
					spills,
					safepoints,
					parallelCopies,
					frame,
					finalDestinations,
					blockLayout,
					statistics);
			}
			if (iteration == MaximumAllocationIterations)
			{
				var spilled = string.Join(
					", ",
					allocation.SpilledValues
						.Order()
						.Take(12)
						.Select(value =>
						{
							var uses = function.Blocks
								.SelectMany(static block => block.Instructions)
								.Where(instruction => instruction.Uses.Contains(value))
								.Select(static instruction => instruction.Operation)
								.Distinct();
							return $"V{value}[{string.Join("/", uses)}]";
						}));
				throw new InvalidOperationException(
					$"Register allocation for '{function.DisplayName}' did not converge " +
					$"after {MaximumAllocationIterations} iterations; " +
					$"{allocation.SpilledValues.Count} values still spill: {spilled}.");
			}

			var spillBatch = M68kSpillSlotAllocator.Allocate(
				function,
				interference,
				allocation.SpilledValues,
				frameBytes,
				allSlots.Count);
			M68kSpillSlotAllocator.Verify(
				function,
				interference,
				allocation.SpilledValues,
				spillBatch);
			foreach (var (value, slot) in spillBatch.Slots)
			{
				allSlots.Add(value, slot);
			}
			allRematerialized.UnionWith(spillBatch.RematerializedValues);
			frameBytes = spillBatch.FrameBytes;
			spilledValueCount = checked(
				spilledValueCount + allocation.SpilledValues.Count);
			M68kSpillRewriter.Rewrite(
				function,
				spillBatch,
				instructionLiveness);
		}

		throw new InvalidOperationException(
			$"Register allocation for '{function.DisplayName}' did not converge " +
			$"after {MaximumAllocationIterations} iterations.");
	}
}

internal static class M68kAllocationStatistics
{
	public static M68kMethodAllocationStatistics Create(
		M68kMachineFunction function,
		M68kInterferenceGraph graph,
		M68kAllocationResult allocation,
		M68kSpillLayout spills,
		M68kSafepointPlan safepoints,
		int allocationIterations = 1,
		int? virtualValues = null,
		int? spilledValues = null)
	{
		var coalescedCopies = 0;
		var bankTransfers = 0;
		foreach (var (left, right) in graph.CopyPreferences)
		{
			if (!allocation.Registers.TryGetValue(left, out var leftLocation) ||
				!allocation.Registers.TryGetValue(right, out var rightLocation))
			{
				continue;
			}
			if (leftLocation == rightLocation)
			{
				coalescedCopies++;
			}
			else if (IsData(leftLocation.Register) != IsData(rightLocation.Register))
			{
				bankTransfers++;
			}
		}
		var calleeSaved = allocation.Registers.Values
			.SelectMany(static location =>
				location.OccupiedRegisters.Enumerate())
			.Where(static register =>
				register is >= M68kRegister.D2 and <= M68kRegister.D7 or
					>= M68kRegister.A2 and <= M68kRegister.A6)
			.Distinct()
			.Count();
		return new M68kMethodAllocationStatistics(
			function.DisplayName,
			virtualValues ?? function.Values.Count,
			allocation.Registers.Count,
			spilledValues ?? allocation.SpilledValues.Count,
			function.Blocks
				.SelectMany(static block => block.Instructions)
				.Count(static instruction =>
					instruction.Operation == M68kMachineOperation.SpillLoad),
			spills.RematerializedValues.Count,
			coalescedCopies,
			bankTransfers,
			calleeSaved,
			spills.FrameBytes,
			safepoints.RootSlotCount,
			allocationIterations,
			0,
			0);
	}

	private static bool IsData(M68kRegister register) =>
		register <= M68kRegister.D7;
}

internal static class M68kGraphColoringAllocator
{
	private sealed class AllocationGroup
	{
		public required int Id { get; init; }

		public HashSet<int> Members { get; } = new();

		public required M68kRegisterSet AllowedRegisters { get; set; }

		public required M68kRegisterSet ForbiddenRegisters { get; set; }

		public required M68kMachineValueWidth Width { get; init; }

		public M68kRegister? PrecoloredRegister { get; set; }

		public long SpillWeight { get; set; }

		public bool IsRematerializable { get; set; }

		public bool PreferAddressRegisters { get; set; }

		public bool IsPair => Width == M68kMachineValueWidth.LongPair;
	}

	private static readonly M68kRegister[] CallerSavedPreference =
	[
		M68kRegister.D0,
		M68kRegister.D1,
		M68kRegister.A0,
		M68kRegister.A1,
		M68kRegister.D2,
		M68kRegister.D3,
		M68kRegister.D4,
		M68kRegister.D5,
		M68kRegister.D6,
		M68kRegister.D7,
		M68kRegister.A2,
		M68kRegister.A3,
		M68kRegister.A4,
		M68kRegister.A5,
		M68kRegister.A6
	];

	private static readonly M68kRegister[] AddressPreference =
	[
		M68kRegister.A0,
		M68kRegister.A1,
		M68kRegister.A2,
		M68kRegister.A3,
		M68kRegister.A4,
		M68kRegister.A5,
		M68kRegister.A6,
		M68kRegister.D0,
		M68kRegister.D1,
		M68kRegister.D2,
		M68kRegister.D3,
		M68kRegister.D4,
		M68kRegister.D5,
		M68kRegister.D6,
		M68kRegister.D7
	];

	public static M68kAllocationResult Allocate(
		M68kMachineFunction function,
		M68kInterferenceGraph graph)
	{
		M68kMachineIrVerifier.Verify(function);
		var groups = BuildCoalescedGroups(function, graph);
		var groupForValue = groups.Values
			.SelectMany(group => group.Members.Select(value => (value, group.Id)))
			.ToDictionary(static item => item.value, static item => item.Id);
		var groupNeighbors = groups.Keys.ToDictionary(
			static group => group,
			static _ => new HashSet<int>());
		foreach (var value in function.Values.Keys)
		{
			var leftGroup = groupForValue[value];
			foreach (var neighbor in graph.Neighbors(value))
			{
				var rightGroup = groupForValue[neighbor];
				if (leftGroup != rightGroup)
				{
					groupNeighbors[leftGroup].Add(rightGroup);
					groupNeighbors[rightGroup].Add(leftGroup);
				}
			}
		}

		var remaining = groups.Values
			.Where(static group => group.PrecoloredRegister is null)
			.Select(static group => group.Id)
			.ToHashSet();
		var stack = new Stack<int>();
		while (remaining.Count != 0)
		{
			var simplifiable = remaining
				.Select(group => groups[group])
				.Where(group =>
					EffectiveDegree(groups, groupNeighbors, remaining, group) <
					AvailableColorCount(group))
				.OrderBy(static group => group.Id)
				.FirstOrDefault();
			var selected = simplifiable?.Id ??
				remaining
					.Select(group => groups[group])
					.OrderBy(group => SpillPriority(
						group,
						EffectiveDegree(
							groups,
							groupNeighbors,
							remaining,
							group)))
					.ThenBy(static group => group.Id)
					.First()
					.Id;
			remaining.Remove(selected);
			stack.Push(selected);
		}

		var allocatedGroups = new Dictionary<int, M68kAllocatedLocation>();
		var spilledGroups = new HashSet<int>();
		foreach (var group in groups.Values
			.Where(static group => group.PrecoloredRegister is not null))
		{
			allocatedGroups.Add(
				group.Id,
				new M68kAllocatedLocation(
					group.PrecoloredRegister!.Value,
					group.IsPair));
		}

		while (stack.TryPop(out var groupId))
		{
			var group = groups[groupId];
			var forbidden = group.ForbiddenRegisters;
			foreach (var neighbor in groupNeighbors[groupId])
			{
				if (allocatedGroups.TryGetValue(neighbor, out var location))
				{
					forbidden = new M68kRegisterSet(
						(ushort)(forbidden.Bits | location.OccupiedRegisters.Bits));
				}
			}

			var preferredCopyRegisters = graph.CopyPreferences
				.Select(pair =>
				{
					var leftGroup = groupForValue[pair.Left];
					var rightGroup = groupForValue[pair.Right];
					if (leftGroup == groupId &&
						allocatedGroups.TryGetValue(rightGroup, out var rightLocation))
					{
						return (M68kRegister?)rightLocation.Register;
					}
					if (rightGroup == groupId &&
						allocatedGroups.TryGetValue(leftGroup, out var leftLocation))
					{
						return (M68kRegister?)leftLocation.Register;
					}
					return null;
				})
				.Where(static register => register is not null)
				.Select(static register => register!.Value);
			var selected = preferredCopyRegisters
				.Concat(CandidateRegisters(group))
				.Distinct()
				.FirstOrDefault(register =>
					CandidateIsAvailable(group, register, forbidden));
			if (!CandidateIsAvailable(group, selected, forbidden) ||
				!group.AllowedRegisters.Contains(selected))
			{
				spilledGroups.Add(groupId);
				continue;
			}
			allocatedGroups.Add(
				groupId,
				new M68kAllocatedLocation(selected, group.IsPair));
		}

		var allocated = new Dictionary<int, M68kAllocatedLocation>();
		var spilled = new HashSet<int>();
		foreach (var group in groups.Values)
		{
			if (spilledGroups.Contains(group.Id))
			{
				spilled.UnionWith(group.Members);
				continue;
			}
			var location = allocatedGroups[group.Id];
			foreach (var value in group.Members)
			{
				allocated.Add(value, location);
			}
		}
		return new M68kAllocationResult(allocated, spilled);
	}

	private static Dictionary<int, AllocationGroup> BuildCoalescedGroups(
		M68kMachineFunction function,
		M68kInterferenceGraph graph)
	{
		var groups = new Dictionary<int, AllocationGroup>();
		var groupForValue = new Dictionary<int, int>();
		foreach (var value in function.Values.Values)
		{
			var forbidden = graph.ForbiddenRegisters.TryGetValue(
				value.Id,
				out var registers)
					? registers
					: M68kRegisterSet.None;
			var group = new AllocationGroup
			{
				Id = value.Id,
				AllowedRegisters = value.AllowedRegisters,
				ForbiddenRegisters = forbidden,
				Width = value.Width,
				PrecoloredRegister = value.PrecoloredRegister,
				SpillWeight = value.SpillWeight,
				IsRematerializable = value.IsRematerializable,
				PreferAddressRegisters = value.Kind is
					CilStackValueKind.Reference or
					CilStackValueKind.ManagedPointer
			};
			group.Members.Add(value.Id);
			groups.Add(group.Id, group);
			groupForValue.Add(value.Id, group.Id);
		}

		foreach (var (left, right) in graph.CopyPreferences
			.OrderByDescending(pair =>
				function.Values[pair.Left].AllowedRegisters == M68kRegisterSet.Address ||
				function.Values[pair.Right].AllowedRegisters == M68kRegisterSet.Address)
			.ThenBy(static pair => pair.Left)
			.ThenBy(static pair => pair.Right))
		{
			var leftId = groupForValue[left];
			var rightId = groupForValue[right];
			if (leftId == rightId)
			{
				continue;
			}
			var leftGroup = groups[leftId];
			var rightGroup = groups[rightId];
			if (!CanCoalesce(
					function,
					graph,
					groups,
					groupForValue,
					leftGroup,
					rightGroup))
			{
				continue;
			}

			var keep = leftGroup.PrecoloredRegister is not null
				? leftGroup
				: rightGroup.PrecoloredRegister is not null
					? rightGroup
					: leftGroup.Id < rightGroup.Id
						? leftGroup
						: rightGroup;
			var remove = ReferenceEquals(keep, leftGroup)
				? rightGroup
				: leftGroup;
			keep.AllowedRegisters = keep.AllowedRegisters.Intersect(
				remove.AllowedRegisters);
			keep.ForbiddenRegisters = new M68kRegisterSet(
				(ushort)(keep.ForbiddenRegisters.Bits |
					remove.ForbiddenRegisters.Bits));
			keep.PrecoloredRegister ??= remove.PrecoloredRegister;
			keep.SpillWeight = keep.SpillWeight >= long.MaxValue - remove.SpillWeight
				? long.MaxValue
				: keep.SpillWeight + remove.SpillWeight;
			keep.IsRematerializable &=
				remove.IsRematerializable;
			keep.PreferAddressRegisters |= remove.PreferAddressRegisters;
			foreach (var member in remove.Members)
			{
				keep.Members.Add(member);
				groupForValue[member] = keep.Id;
			}
			groups.Remove(remove.Id);
		}
		return groups;
	}

	private static bool CanCoalesce(
		M68kMachineFunction function,
		M68kInterferenceGraph graph,
		IReadOnlyDictionary<int, AllocationGroup> groups,
		IReadOnlyDictionary<int, int> groupForValue,
		AllocationGroup left,
		AllocationGroup right)
	{
		if (left.Width != right.Width ||
			(left.PrecoloredRegister is { } leftRegister &&
			 right.PrecoloredRegister is { } rightRegister &&
			 leftRegister != rightRegister) ||
			left.PreferAddressRegisters != right.PreferAddressRegisters &&
				(left.PreferAddressRegisters
					? !right.AllowedRegisters.Overlaps(M68kRegisterSet.Address)
					: !left.AllowedRegisters.Overlaps(M68kRegisterSet.Address)))
		{
			return false;
		}
		var allowed = left.AllowedRegisters
			.Intersect(right.AllowedRegisters)
			.Except(new M68kRegisterSet(
				(ushort)(left.ForbiddenRegisters.Bits |
					right.ForbiddenRegisters.Bits)));
		if (allowed.IsEmpty)
		{
			return false;
		}
		var fixedRegister = left.PrecoloredRegister ?? right.PrecoloredRegister;
		if (fixedRegister is { } required && !allowed.Contains(required))
		{
			return false;
		}
		foreach (var leftMember in left.Members)
		{
			if (right.Members.Any(graph.Neighbors(leftMember).Contains))
			{
				return false;
			}
		}
		if (left.AllowedRegisters == M68kRegisterSet.Address ||
			right.AllowedRegisters == M68kRegisterSet.Address)
		{
			// Address constraints are short-lived machine requirements, not
			// independent values. Coalescing them with an already-addressable
			// source removes the otherwise unavoidable An-to-A0 base copy.
			return true;
		}

		var neighborGroups = left.Members
			.Concat(right.Members)
			.SelectMany(graph.Neighbors)
			.Select(value => groupForValue[value])
			.Where(group => group != left.Id && group != right.Id)
			.Distinct()
			.ToArray();
		var highDegree = neighborGroups.Count(groupId =>
		{
			var neighbor = groups[groupId];
			if (!allowed.Overlaps(neighbor.AllowedRegisters))
			{
				return false;
			}
			var degree = neighbor.Members
				.SelectMany(graph.Neighbors)
				.Select(value => groupForValue[value])
				.Distinct()
				.Count();
			return degree >= allowed.Count;
		});
		return highDegree < allowed.Count;
	}

	public static void VerifyAllocation(
		M68kMachineFunction function,
		M68kInterferenceGraph graph,
		M68kAllocationResult allocation)
	{
		foreach (var (valueId, location) in allocation.Registers)
		{
			var value = function.Values[valueId];
			if (!value.AllowedRegisters.Contains(location.Register) ||
				location.IsPair != value.IsRegisterPair)
			{
				throw new InvalidOperationException(
					$"Allocator assigned illegal register {location.Register} to v{valueId}.");
			}
			if (graph.ForbiddenRegisters.TryGetValue(valueId, out var forbidden) &&
				location.OccupiedRegisters.Overlaps(forbidden))
			{
				throw new InvalidOperationException(
					$"Allocator assigned clobbered register {location.Register} to v{valueId}.");
			}
			foreach (var neighbor in graph.Neighbors(valueId))
			{
				if (allocation.Registers.TryGetValue(neighbor, out var other) &&
					location.OccupiedRegisters.Overlaps(other.OccupiedRegisters))
				{
					throw new InvalidOperationException(
						$"Interfering values v{valueId} and v{neighbor} share a register.");
				}
			}
		}
	}

	private static int EffectiveDegree(
		IReadOnlyDictionary<int, AllocationGroup> groups,
		IReadOnlyDictionary<int, HashSet<int>> neighbors,
		IReadOnlySet<int> remaining,
		AllocationGroup group) =>
		neighbors[group.Id].Count(neighbor =>
			(remaining.Contains(neighbor) ||
			 groups[neighbor].PrecoloredRegister is not null) &&
			group.AllowedRegisters.Overlaps(
				groups[neighbor].AllowedRegisters));

	private static int AvailableColorCount(
		AllocationGroup group) =>
		group.AllowedRegisters
			.Enumerate()
			.Count(register => CandidateIsAvailable(
				group,
				register,
				group.ForbiddenRegisters));

	private static double SpillPriority(AllocationGroup group, int degree) =>
		((double)group.SpillWeight /
			(group.IsRematerializable ? 4 : 1)) /
		Math.Max(1, degree);

	private static IEnumerable<M68kRegister> CandidateRegisters(
		AllocationGroup group)
	{
		var preference = group.PreferAddressRegisters
			? AddressPreference
			: CallerSavedPreference;
		foreach (var register in preference)
		{
			if (group.AllowedRegisters.Contains(register))
			{
				yield return register;
			}
		}
	}

	private static bool CandidateIsAvailable(
		AllocationGroup group,
		M68kRegister register,
		M68kRegisterSet forbidden)
	{
		if (!group.AllowedRegisters.Contains(register) ||
			forbidden.Contains(register))
		{
			return false;
		}
		return !group.IsPair ||
			(register < M68kRegister.D7 &&
			 !forbidden.Contains(register + 1));
	}
}
