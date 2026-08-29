/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kMemoryPromotionContext(
	CilMethod Method,
	CompilationModule Module,
	IReadOnlyDictionary<CilMethodIdentity, M68kMethodMemorySummary> MethodSummaries,
	IReadOnlySet<M68kMemoryObject> LocallyOwnedGlobalObjects,
	IReadOnlyDictionary<int, M68kHeapOwnerFacts> HeapOwners,
	M68kMachineFunction Function,
	bool FrameAndArgumentOnly = false);

internal sealed record M68kMemoryPromotionStatistics(
	int Candidates,
	int PromotedLocations,
	int LoadsForwarded,
	int StoresRemoved,
	int PhisInserted,
	int NormalizationsInserted,
	int KeepAlivesInserted)
{
	public static M68kMemoryPromotionStatistics Empty { get; } =
		new(0, 0, 0, 0, 0, 0, 0);

	public bool Changed =>
		LoadsForwarded != 0 || StoresRemoved != 0 ||
		PhisInserted != 0 || NormalizationsInserted != 0 ||
		KeepAlivesInserted != 0;
}

/// <summary>
/// Promotes one exact memory location at a time into the existing machine SSA.
/// Stores become memory-version definitions, loads become copies of the current
/// version, and iterated dominance frontiers provide merge values for joins and
/// loop headers.
/// </summary>
internal static class M68kMemoryPromotionPass
{
	private sealed record Candidate(
		M68kMemoryObject Object,
		M68kMachineValue Prototype,
		int? SeedValue,
		int? ZeroValue,
		bool RemoveStores,
		bool NeedsGcKeepAlive);

	private sealed class MutableStatistics
	{
		public int Candidates;

		public int PromotedLocations;

		public int LoadsForwarded;

		public int StoresRemoved;

		public int PhisInserted;

		public int NormalizationsInserted;

		public int KeepAlivesInserted;

		public M68kMemoryPromotionStatistics Freeze() => new(
			Candidates,
			PromotedLocations,
			LoadsForwarded,
			StoresRemoved,
			PhisInserted,
			NormalizationsInserted,
			KeepAlivesInserted);
	}

	public static M68kMemoryPromotionStatistics Run(
		M68kMachineFunction function,
		M68kMemoryPromotionContext context)
	{
		ArgumentNullException.ThrowIfNull(function);
		ArgumentNullException.ThrowIfNull(context);
		if (function.HasExceptionHandlers ||
			function.Blocks.SelectMany(static block => block.SuccessorEdges)
				.Any(static edge => edge.Kind != M68kMachineEdgeKind.Normal))
		{
			// Machine phis intentionally describe normal predecessors only. Keep EH
			// memory materialized until an exact exceptional memory-state carrier is
			// available; this also preserves handler/finally observation points.
			return M68kMemoryPromotionStatistics.Empty;
		}

		function.SynchronizeNormalEdges();
		var statistics = new MutableStatistics();
		var objects = function.Blocks
			.SelectMany(static block => block.Instructions)
			.SelectMany(static instruction => instruction.ExactMemoryAccesses)
			.Select(static access => access.Object)
			.Distinct()
			.OrderBy(static item => item.Kind)
			.ThenBy(static item => item.Identity, StringComparer.Ordinal)
			.ThenBy(static item => item.OwnerValueId)
			.ThenBy(static item => item.Offset)
			.ToArray();
		statistics.Candidates = objects.Length;
		foreach (var memoryObject in objects)
		{
			if (!TryCreateCandidate(
					function,
					context,
					objects,
					memoryObject,
					out var candidate))
			{
				continue;
			}
			if (PromoteCandidate(function, context, candidate, statistics))
			{
				statistics.PromotedLocations++;
			}
		}
		if (statistics.Freeze().Changed)
		{
			M68kMachineIrVerifier.Verify(function);
		}
		return statistics.Freeze();
	}

	private static bool TryCreateCandidate(
		M68kMachineFunction function,
		M68kMemoryPromotionContext context,
		IReadOnlyList<M68kMemoryObject> allObjects,
		M68kMemoryObject memoryObject,
		out Candidate candidate)
	{
		candidate = null!;
		if (Environment.GetEnvironmentVariable(
				"COPPERSHARP_DIAGNOSTIC_DISABLE_HEAP_MEMORY_PROMOTION") == "1" &&
			memoryObject.Kind is not (
				M68kMemoryObjectKind.FrameSlot or
				M68kMemoryObjectKind.ArgumentHome))
		{
			return false;
		}
		if (Environment.GetEnvironmentVariable(
				"COPPERSHARP_DIAGNOSTIC_DISABLE_FRAME_MEMORY_PROMOTION") == "1" &&
			memoryObject.Kind is
				M68kMemoryObjectKind.FrameSlot or
				M68kMemoryObjectKind.ArgumentHome)
		{
			return false;
		}
		if (context.FrameAndArgumentOnly &&
			memoryObject.Kind is not (
				M68kMemoryObjectKind.FrameSlot or
				M68kMemoryObjectKind.ArgumentHome))
		{
			return false;
		}
		if (memoryObject.Size <= 0 || memoryObject.Offset < 0 ||
			allObjects.Any(other => other != memoryObject &&
				memoryObject.Overlaps(other)))
		{
			return false;
		}
		if (memoryObject.IsHeapObject &&
			(memoryObject.OwnerValueId is not { } owner ||
			 !context.HeapOwners.TryGetValue(owner, out var ownerFacts) ||
			 !ownerFacts.IsPromotable))
		{
			return false;
		}

		var instructions = AccessingInstructions(function, memoryObject).ToArray();
		if (instructions.Length == 0 ||
			instructions.Any(static item =>
				(item.Instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0 ||
				item.Access.Kind is M68kExactMemoryAccessKind.Address or
					M68kExactMemoryAccessKind.Escape) ||
			instructions.GroupBy(static item => item.Instruction.Id)
				.Any(static group => group.Count() != 1))
		{
			return false;
		}

		var observedValues = new List<M68kMachineValue>();
		foreach (var (_, instruction, access) in instructions)
		{
			var valueId = access.Kind switch
			{
				M68kExactMemoryAccessKind.Read =>
					access.ValueId ?? instruction.Definitions.SingleOrDefault(),
				M68kExactMemoryAccessKind.Write =>
					access.ValueId ?? (instruction.Uses.Length == 0
						? -1
						: instruction.Uses[^1]),
				_ => -1
			};
			if (valueId < 0)
			{
				if (access.Kind == M68kExactMemoryAccessKind.Write &&
					instruction.Immediate == 0)
				{
					continue;
				}
				return false;
			}
			if (!function.Values.TryGetValue(valueId, out var value))
			{
				return false;
			}
			observedValues.Add(value);
		}
		var prototype = CanonicalPrototype(memoryObject, observedValues);
		if (prototype is null)
		{
			return false;
		}

		var seed = FindSeed(function, memoryObject, prototype, context);
		int? zeroValue = instructions.Any(static item =>
			item.Access.Kind == M68kExactMemoryAccessKind.Write &&
			item.Access.ValueId is null &&
			item.Instruction.Immediate == 0)
				? AddZero(function, prototype)
				: null;
		var removeStores = memoryObject.Kind switch
		{
			M68kMemoryObjectKind.FrameSlot or
				M68kMemoryObjectKind.ArgumentHome => true,
			M68kMemoryObjectKind.ObjectField or
				M68kMemoryObjectKind.ArrayElement or
				M68kMemoryObjectKind.AggregateLane => true,
			M68kMemoryObjectKind.StaticField or
				M68kMemoryObjectKind.LibraryBase or
				M68kMemoryObjectKind.RuntimeSlot =>
					!memoryObject.IsManagedRoot &&
					context.LocallyOwnedGlobalObjects.Contains(memoryObject),
			_ => false
		};
		if (removeStores && function.Blocks
			.SelectMany(static block => block.Instructions)
			.Any(instruction => IsBarrier(instruction, memoryObject, context)))
		{
			// A visible call/unknown access needs the concrete memory state. Keep
			// stores materialized instead of introducing writeback/reload traffic.
			removeStores = false;
		}
		candidate = new Candidate(
			memoryObject,
			prototype,
			seed,
			zeroValue,
			removeStores,
			removeStores && memoryObject.IsManagedRoot);
		return true;
	}

	private static int? FindSeed(
		M68kMachineFunction function,
		M68kMemoryObject memoryObject,
		M68kMachineValue prototype,
		M68kMemoryPromotionContext context)
	{
		if (memoryObject.Kind == M68kMemoryObjectKind.ArgumentHome &&
			int.TryParse(
				memoryObject.Identity,
				System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture,
				out var argumentIndex))
		{
			return function.Blocks
				.SelectMany(static block => block.Instructions)
				.Where(instruction =>
					instruction.Operation == M68kMachineOperation.Argument &&
					instruction.ArgumentIndex == argumentIndex &&
					instruction.Definitions.Length == 1)
				.Select(static instruction => instruction.Definitions[0])
				.FirstOrDefault(value => SameRepresentation(
					prototype,
					function.Values[value]),
					-1) is var argument && argument >= 0
					? argument
					: null;
		}
		if (memoryObject.Kind == M68kMemoryObjectKind.FrameSlot &&
			int.TryParse(
				memoryObject.Identity,
				System.Globalization.NumberStyles.None,
				System.Globalization.CultureInfo.InvariantCulture,
				out var localIndex) &&
			function.LocalHomes.GetValueOrDefault(localIndex)?.Initialize == true)
		{
			return AddZero(function, prototype);
		}
		if (memoryObject.OwnerValueId is { } owner &&
			context.HeapOwners.TryGetValue(owner, out var facts) &&
			facts.IsPromotable &&
			(facts.IsArray || !facts.ConstructorMayWrite))
		{
			return AddZero(function, prototype);
		}
		return null;
	}

	private static int AddZero(
		M68kMachineFunction function,
		M68kMachineValue prototype)
	{
		var entry = function.Blocks.Single(block =>
			block.Id == function.EntryBlockId);
		var value = function.CreateValue(
			prototype.Kind,
			prototype.Width,
			DefaultRegisters(prototype),
			isGcReference: prototype.IsGcReference,
			isRematerializable: true,
			spillWeight: prototype.SpillWeight);
		var constant = prototype.IsGcReference
			? M68kMachineConstant.Null
			: prototype.Kind switch
			{
				CilStackValueKind.BooleanByte =>
					M68kMachineConstant.Boolean(false),
				CilStackValueKind.Int64 => M68kMachineConstant.Int64(0),
				CilStackValueKind.Float32 => new M68kMachineConstant(
					M68kMachineConstantKind.Float32Bits,
					0),
				CilStackValueKind.Float64 => new M68kMachineConstant(
					M68kMachineConstantKind.Float64Bits,
					0),
				_ => M68kMachineConstant.Int32(0)
			};
		entry.Instructions.Insert(0, function.CreateInstruction(
			M68kMachineOperation.Constant,
			Math.Max(0, entry.StartIlOffset),
			definitions: [value.Id],
			constantValue: constant));
		return value.Id;
	}

	private static bool PromoteCandidate(
		M68kMachineFunction function,
		M68kMemoryPromotionContext context,
		Candidate candidate,
		MutableStatistics statistics)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var availability = ComputeAvailability(function, context, candidate);
		var definitionBlocks = new HashSet<int>();
		if (candidate.SeedValue is not null)
		{
			definitionBlocks.Add(function.EntryBlockId);
		}
		foreach (var (block, _, access) in AccessingInstructions(
			function,
			candidate.Object))
		{
			if (access.Kind is M68kExactMemoryAccessKind.Read or
				M68kExactMemoryAccessKind.Write)
			{
				definitionBlocks.Add(block.Id);
			}
		}

		var dominators = ComputeNormalDominators(function);
		var immediateDominators = ComputeImmediateDominators(
			function,
			dominators);
		var frontiers = ComputeDominanceFrontiers(
			function,
			immediateDominators);
		var phiBlocks = new HashSet<int>();
		var work = new Queue<int>(definitionBlocks);
		while (work.TryDequeue(out var definitionBlock))
		{
			foreach (var frontier in frontiers[definitionBlock])
			{
				if (!availability.In[frontier] || !phiBlocks.Add(frontier))
				{
					continue;
				}
				if (!definitionBlocks.Contains(frontier))
				{
					work.Enqueue(frontier);
				}
			}
		}

		var phiInputs = new Dictionary<int, Dictionary<int, int>>();
		var phiDefinitions = new Dictionary<int, int>();
		foreach (var blockId in phiBlocks.Order())
		{
			var value = function.CreateValue(
				candidate.Prototype.Kind,
				candidate.Prototype.Width,
				DefaultRegisters(candidate.Prototype),
				isGcReference: candidate.Prototype.IsGcReference,
				spillWeight: candidate.Prototype.SpillWeight);
			var inputs = new Dictionary<int, int>();
			blocks[blockId].Phis.Add(new M68kMachinePhi(value.Id, inputs));
			phiDefinitions[blockId] = value.Id;
			phiInputs[blockId] = inputs;
			statistics.PhisInserted++;
		}

		var children = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new List<int>());
		foreach (var (blockId, parent) in immediateDominators)
		{
			if (parent is { } parentId)
			{
				children[parentId].Add(blockId);
			}
		}
		var stack = new Stack<int>();
		if (candidate.SeedValue is { } seed)
		{
			stack.Push(seed);
		}
		var changed = false;

		void Rename(int blockId)
		{
			var depth = stack.Count;
			var block = blocks[blockId];
			if (phiDefinitions.TryGetValue(blockId, out var phiDefinition))
			{
				stack.Push(phiDefinition);
			}
			for (var index = 0; index < block.Instructions.Count;)
			{
				var instruction = block.Instructions[index];
				var access = instruction.ExactMemoryAccesses.FirstOrDefault(item =>
					item.Object == candidate.Object);
				var hasAccess = instruction.ExactMemoryAccesses.Any(item =>
					item.Object == candidate.Object);
				if (hasAccess && access.Kind == M68kExactMemoryAccessKind.Read)
				{
					if (stack.TryPeek(out var current) && current >= 0 &&
						instruction.Definitions is [var definition])
					{
						block.Instructions[index] = AsForwardedRead(
							instruction,
							current,
							function.Values[current],
							function.Values[definition]);
						statistics.LoadsForwarded++;
						changed = true;
					}
					else if (instruction.Definitions is [var loaded])
					{
						index++;
						var normalized = NormalizeMemoryValue(
							function,
							block,
							index,
							instruction,
							loaded,
							candidate.Prototype,
							statistics);
						stack.Push(normalized);
						if (normalized != loaded)
						{
							index++;
							changed = true;
						}
						continue;
					}
					index++;
					continue;
				}
				if (hasAccess && access.Kind == M68kExactMemoryAccessKind.Write)
				{
					var stored = StoreValue(instruction, access);
					stored ??= instruction.Immediate == 0
						? candidate.ZeroValue
						: null;
					if (stored is { } storedValue)
					{
						var normalized = NormalizeMemoryValue(
							function,
							block,
							index,
							instruction,
							storedValue,
							candidate.Prototype,
							statistics);
						stack.Push(normalized);
						if (normalized != storedValue)
						{
							index++;
							changed = true;
						}
					}
					if (candidate.RemoveStores)
					{
						block.Instructions.RemoveAt(index);
						statistics.StoresRemoved++;
						changed = true;
						continue;
					}
					index++;
					continue;
				}
				if (IsBarrier(instruction, candidate.Object, context))
				{
					stack.Push(-1);
				}
				index++;
				if (candidate.NeedsGcKeepAlive &&
					instruction.IsSafepoint &&
					stack.TryPeek(out var root) && root >= 0 &&
					function.Values[root].IsGcReference)
				{
					var roots = new List<int> { root };
					if (candidate.Object.OwnerValueId is { } owner &&
						owner != root &&
						function.Values.TryGetValue(owner, out var ownerValue) &&
						ownerValue.IsGcReference &&
						ValueDominatesInstruction(
							function,
							owner,
							block,
							instruction,
							dominators))
					{
						roots.Add(owner);
					}
					block.Instructions.Insert(index++, function.CreateInstruction(
						M68kMachineOperation.GcKeepAlive,
						instruction.IlOffset,
						uses: roots,
						sourceInstruction: instruction.SourceInstruction));
					statistics.KeepAlivesInserted++;
					changed = true;
				}
			}

			foreach (var successor in block.Successors)
			{
				if (phiInputs.TryGetValue(successor, out var inputs) &&
					stack.TryPeek(out var current) && current >= 0)
				{
					inputs[blockId] = current;
				}
			}
			foreach (var child in children[blockId].Order())
			{
				Rename(child);
			}
			while (stack.Count > depth)
			{
				stack.Pop();
			}
		}

		Rename(function.EntryBlockId);
		foreach (var (blockId, inputs) in phiInputs)
		{
			if (inputs.Count != blocks[blockId].Predecessors.Distinct().Count())
			{
				throw new InvalidOperationException(
					$"Memory promotion failed to construct all inputs for " +
					$"{candidate.Object} in block {blockId} of '{function.DisplayName}'.");
			}
		}
		return changed;
	}

	private static (
		IReadOnlyDictionary<int, bool> In,
		IReadOnlyDictionary<int, bool> Out) ComputeAvailability(
		M68kMachineFunction function,
		M68kMemoryPromotionContext context,
		Candidate candidate)
	{
		var incoming = function.Blocks.ToDictionary(
			static block => block.Id,
			block => block.Id == function.EntryBlockId
				? candidate.SeedValue is not null
				: true);
		var outgoing = function.Blocks.ToDictionary(static block => block.Id, static _ => true);
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks.OrderBy(static block => block.Id))
			{
				var nextIn = block.Id == function.EntryBlockId
					? candidate.SeedValue is not null
					: block.Predecessors.Count != 0 &&
						block.Predecessors.All(predecessor => outgoing[predecessor]);
				var nextOut = TransferAvailability(
					block,
					nextIn,
					candidate.Object,
					context);
				if (incoming[block.Id] != nextIn || outgoing[block.Id] != nextOut)
				{
					incoming[block.Id] = nextIn;
					outgoing[block.Id] = nextOut;
					changed = true;
				}
			}
		}
		return (incoming, outgoing);
	}

	private static bool TransferAvailability(
		M68kMachineBlock block,
		bool available,
		M68kMemoryObject memoryObject,
		M68kMemoryPromotionContext context)
	{
		foreach (var instruction in block.Instructions)
		{
			var access = instruction.ExactMemoryAccesses.FirstOrDefault(item =>
				item.Object == memoryObject);
			if (instruction.ExactMemoryAccesses.Any(item => item.Object == memoryObject) &&
				access.Kind is M68kExactMemoryAccessKind.Read or
					M68kExactMemoryAccessKind.Write)
			{
				available = true;
				continue;
			}
			if (IsBarrier(instruction, memoryObject, context))
			{
				available = false;
			}
		}
		return available;
	}

	private static bool IsBarrier(
		M68kMachineInstruction instruction,
		M68kMemoryObject memoryObject,
		M68kMemoryPromotionContext context)
	{
		if (instruction.ExactMemoryAccesses.Any(access =>
			access.Object != memoryObject && memoryObject.Overlaps(access.Object)))
		{
			return true;
		}
		if ((instruction.MemoryEffect & M68kMachineMemoryEffect.Volatile) != 0)
		{
			return true;
		}
		if (memoryObject.Kind is
			M68kMemoryObjectKind.FrameSlot or
			M68kMemoryObjectKind.ArgumentHome ||
			memoryObject.IsHeapObject)
		{
			return false;
		}
		if (instruction.Operation != M68kMachineOperation.Call)
		{
			// Exact global locations cannot be reached by ordinary heap/frame
			// operations. Type initialization is the remaining implicit managed
			// global observer; address escapes were rejected with the candidate.
			return instruction.Operation == M68kMachineOperation.TypeInitialize;
		}
		if (memoryObject.Kind == M68kMemoryObjectKind.LibraryBase &&
			instruction.HasExplicitPlatformBase &&
			!HasManagedCallbackArgument(context, instruction))
		{
			// Native callees consume an explicit base value and cannot discover a
			// compiler-owned slot. A managed callback is the only re-entry path that
			// can make the slot observable here.
			return false;
		}

		var targets = instruction.LogicalCall?.ResolvedTargets ?? [];
		if (targets.Length == 0)
		{
			// Writable platform slots are compiler-owned. A native call consumes
			// its explicit base SSA operand and cannot discover the slot unless a
			// managed callback was explicitly supplied.
			return memoryObject.Kind != M68kMemoryObjectKind.LibraryBase;
		}
		foreach (var target in targets)
		{
			if (!context.MethodSummaries.TryGetValue(target, out var summary) ||
				summary.ReadsUnknownGlobals || summary.WritesUnknownGlobals ||
				summary.MayReenter ||
				summary.ExactGlobalReads.Any(memoryObject.Overlaps) ||
				summary.ExactGlobalWrites.Any(memoryObject.Overlaps))
			{
				return true;
			}
		}
		return false;
	}

	private static bool HasManagedCallbackArgument(
		M68kMemoryPromotionContext context,
		M68kMachineInstruction call)
	{
		var definitions = context.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.SelectMany(static instruction => instruction.Definitions.Select(
				definition => (definition, instruction)))
			.ToDictionary(static item => item.definition, static item => item.instruction);
		// Function and delegate values are explicit in machine IR. Follow copies
		// because call ABI preparation commonly introduces one before the call.
		foreach (var argument in call.LogicalCall?.ArgumentValueIds ?? call.Uses)
		{
			var value = argument;
			var visited = new HashSet<int>();
			while (visited.Add(value) &&
				definitions.TryGetValue(value, out var definition))
			{
				if (definition.Operation is
					M68kMachineOperation.FunctionAddress or
					M68kMachineOperation.DelegateCreate)
				{
					return true;
				}
				if (definition.Operation == M68kMachineOperation.Copy &&
					definition.Uses is [var source])
				{
					value = source;
					continue;
				}
				break;
			}
		}
		return false;
	}

	private static IEnumerable<(
		M68kMachineBlock Block,
		M68kMachineInstruction Instruction,
		M68kExactMemoryAccess Access)> AccessingInstructions(
		M68kMachineFunction function,
		M68kMemoryObject memoryObject)
	{
		foreach (var block in function.Blocks)
		{
			foreach (var instruction in block.Instructions)
			{
				foreach (var access in instruction.ExactMemoryAccesses)
				{
					if (access.Object == memoryObject)
					{
						yield return (block, instruction, access);
					}
				}
			}
		}
	}

	private static int? StoreValue(
		M68kMachineInstruction instruction,
		M68kExactMemoryAccess access) =>
		access.ValueId ?? (instruction.Uses.Length == 0
			? null
			: instruction.Uses[^1]);

	private static M68kMachineInstruction AsCopy(
		M68kMachineInstruction instruction,
		int source) =>
		instruction with
		{
			Operation = M68kMachineOperation.Copy,
			Uses = [source],
			Clobbers = M68kRegisterSet.None,
			MemoryEffect = M68kMachineMemoryEffect.None,
			IsSafepoint = false,
			MayThrow = false,
			ProducesConditionCodes = false,
			ConsumesConditionCodes = false,
			SpillSlotIndex = null,
			ArgumentIndex = null,
			StackVarargsRegister = null,
			Immediate = null,
			AllowCopyCoalescing = true,
			TransportsManagedByrefOwner = false,
			BranchCondition = null,
			RequiresLiveCallerFrame = false,
			ConstantValue = null,
			LogicalCall = null,
			ExactMemoryAccesses = [],
			PlatformBaseConvention = null,
			HasExplicitPlatformBase = false
		};

	private static M68kMachineInstruction AsForwardedRead(
		M68kMachineInstruction instruction,
		int source,
		M68kMachineValue sourceValue,
		M68kMachineValue destinationValue)
	{
		if (CanBitCopy(sourceValue, destinationValue))
		{
			return AsCopy(instruction, source);
		}
		return instruction with
		{
			Operation = M68kMachineOperation.Convert,
			Uses = [source],
			Clobbers = M68kRegisterSet.None,
			MemoryEffect = M68kMachineMemoryEffect.None,
			IsSafepoint = false,
			MayThrow = false,
			ProducesConditionCodes = false,
			ConsumesConditionCodes = false,
			SourceInstruction = ConversionInstruction(
				instruction,
				destinationValue.Kind,
				sourceValue.Width),
			SpillSlotIndex = null,
			ArgumentIndex = null,
			StackVarargsRegister = null,
			Immediate = null,
			AllowCopyCoalescing = true,
			TransportsManagedByrefOwner = false,
			BranchCondition = null,
			RequiresLiveCallerFrame = false,
			ConstantValue = null,
			LogicalCall = null,
			ExactMemoryAccesses = [],
			PlatformBaseConvention = null,
			HasExplicitPlatformBase = false
		};
	}

	private static int NormalizeMemoryValue(
		M68kMachineFunction function,
		M68kMachineBlock block,
		int insertionIndex,
		M68kMachineInstruction memoryInstruction,
		int source,
		M68kMachineValue prototype,
		MutableStatistics statistics)
	{
		var sourceValue = function.Values[source];
		if (SameRepresentation(sourceValue, prototype))
		{
			return source;
		}
		var normalized = function.CreateValue(
			prototype.Kind,
			prototype.Width,
			DefaultRegisters(prototype),
			isGcReference: prototype.IsGcReference,
			spillWeight: prototype.SpillWeight);
		var bitCopy = CanBitCopy(sourceValue, prototype);
		block.Instructions.Insert(insertionIndex, function.CreateInstruction(
			bitCopy
				? M68kMachineOperation.Copy
				: M68kMachineOperation.Convert,
			memoryInstruction.IlOffset,
			definitions: [normalized.Id],
			uses: [source],
			sourceInstruction: bitCopy
				? memoryInstruction.SourceInstruction
				: ConversionInstruction(
					memoryInstruction,
					prototype.Kind,
					sourceValue.Width),
			allowCopyCoalescing: true,
			origin: memoryInstruction.Origin));
		statistics.NormalizationsInserted++;
		return normalized.Id;
	}

	private static CilInstruction ConversionInstruction(
		M68kMachineInstruction instruction,
		CilStackValueKind targetKind,
		M68kMachineValueWidth sourceWidth)
	{
		var opCode = targetKind switch
		{
			CilStackValueKind.SignedByte => System.Reflection.Emit.OpCodes.Conv_I1,
			CilStackValueKind.BooleanByte or
				CilStackValueKind.UnsignedByte => System.Reflection.Emit.OpCodes.Conv_U1,
			CilStackValueKind.SignedWord => System.Reflection.Emit.OpCodes.Conv_I2,
			CilStackValueKind.UnsignedWord => System.Reflection.Emit.OpCodes.Conv_U2,
			CilStackValueKind.Int32 => NarrowLoadConversion(
				instruction,
				sourceWidth),
			_ => throw new InvalidOperationException(
				$"Cannot normalize exact memory as {targetKind}.")
		};
		return new CilInstruction(
			instruction.IlOffset,
			opCode,
			null,
			instruction.SourceInstruction?.NextOffset ?? instruction.IlOffset + 1);
	}

	private static System.Reflection.Emit.OpCode NarrowLoadConversion(
		M68kMachineInstruction instruction,
		M68kMachineValueWidth sourceWidth)
	{
		var source = instruction.Origin?.SourceInstruction ??
			instruction.SourceInstruction;
		if (source?.OpCode is var op)
		{
			if (op == System.Reflection.Emit.OpCodes.Ldelem_I1 ||
				op == System.Reflection.Emit.OpCodes.Ldind_I1)
			{
				return System.Reflection.Emit.OpCodes.Conv_I1;
			}
			if (op == System.Reflection.Emit.OpCodes.Ldelem_U1 ||
				op == System.Reflection.Emit.OpCodes.Ldind_U1)
			{
				return System.Reflection.Emit.OpCodes.Conv_U1;
			}
			if (op == System.Reflection.Emit.OpCodes.Ldelem_I2 ||
				op == System.Reflection.Emit.OpCodes.Ldind_I2)
			{
				return System.Reflection.Emit.OpCodes.Conv_I2;
			}
			if (op == System.Reflection.Emit.OpCodes.Ldelem_U2 ||
				op == System.Reflection.Emit.OpCodes.Ldind_U2)
			{
				return System.Reflection.Emit.OpCodes.Conv_U2;
			}
		}
		return sourceWidth == M68kMachineValueWidth.Byte
			? System.Reflection.Emit.OpCodes.Conv_U1
			: System.Reflection.Emit.OpCodes.Conv_U2;
	}

	private static M68kMachineValue? CanonicalPrototype(
		M68kMemoryObject memoryObject,
		IReadOnlyList<M68kMachineValue> values)
	{
		if (values.Count == 0)
		{
			return null;
		}
		var first = values[0];
		if (memoryObject.Size is 1 or 2 &&
			values.All(static value => IsIntegerValue(value) &&
				!value.IsGcReference))
		{
			var kind = memoryObject.Size == 1
				? CilStackValueKind.UnsignedByte
				: CilStackValueKind.UnsignedWord;
			return first with
			{
				Kind = kind,
				Width = memoryObject.Size == 1
					? M68kMachineValueWidth.Byte
					: M68kMachineValueWidth.Word,
				AllowedRegisters = M68kRegisterSet.Data,
				PrecoloredRegister = null,
				IsGcReference = false,
				IsRematerializable = false
			};
		}
		if (values.All(static value => IsPointerSizedScalar(value)))
		{
			return first with
			{
				Kind = CilStackValueKind.Int32,
				Width = M68kMachineValueWidth.Long,
				AllowedRegisters = M68kRegisterSet.DataOrAddress,
				PrecoloredRegister = null,
				IsGcReference = false,
				IsRematerializable = false
			};
		}
		return values.All(value => SameRepresentation(first, value))
			? first
			: null;
	}

	private static bool IsIntegerValue(M68kMachineValue value) =>
		value.Kind is
			CilStackValueKind.BooleanByte or
			CilStackValueKind.UnsignedByte or
			CilStackValueKind.SignedByte or
			CilStackValueKind.UnsignedWord or
			CilStackValueKind.SignedWord or
			CilStackValueKind.Int32;

	private static bool IsPointerSizedScalar(M68kMachineValue value) =>
		!value.IsGcReference &&
		value.Width == M68kMachineValueWidth.Long &&
		value.Kind is
			CilStackValueKind.Int32 or
			CilStackValueKind.ManagedPointer;

	private static bool CanBitCopy(
		M68kMachineValue first,
		M68kMachineValue second) =>
		SameRepresentation(first, second) ||
		IsPointerSizedScalar(first) && IsPointerSizedScalar(second);

	private static bool SameRepresentation(
		M68kMachineValue first,
		M68kMachineValue second) =>
		first.Kind == second.Kind &&
		first.Width == second.Width &&
		first.IsGcReference == second.IsGcReference;

	private static M68kRegisterSet DefaultRegisters(M68kMachineValue value) =>
		value.IsRegisterPair
			? M68kRegisterSet.DataPairStarts
			: value.Kind is
				CilStackValueKind.Reference or
				CilStackValueKind.ManagedPointer or
				CilStackValueKind.AggregateAddress
					? M68kRegisterSet.DataOrAddress
					: M68kRegisterSet.Data;

	private static bool ValueDominatesInstruction(
		M68kMachineFunction function,
		int value,
		M68kMachineBlock useBlock,
		M68kMachineInstruction useInstruction,
		IReadOnlyDictionary<int, HashSet<int>> dominators)
	{
		foreach (var block in function.Blocks)
		{
			if (block.Phis.Any(phi => phi.Definition == value))
			{
				return dominators[useBlock.Id].Contains(block.Id);
			}
			var definitionIndex = block.Instructions.FindIndex(instruction =>
				instruction.Definitions.Contains(value));
			if (definitionIndex < 0)
			{
				continue;
			}
			if (block.Id != useBlock.Id)
			{
				return dominators[useBlock.Id].Contains(block.Id);
			}
			var useIndex = useBlock.Instructions.FindIndex(instruction =>
				instruction.Id == useInstruction.Id);
			return useIndex >= 0 && definitionIndex < useIndex;
		}
		return false;
	}

	private static IReadOnlyDictionary<int, HashSet<int>> ComputeNormalDominators(
		M68kMachineFunction function)
	{
		var all = function.Blocks.Select(static block => block.Id).ToHashSet();
		var dominators = function.Blocks.ToDictionary(
			static block => block.Id,
			block => block.Id == function.EntryBlockId
				? new HashSet<int> { block.Id }
				: new HashSet<int>(all));
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks.Where(block =>
				block.Id != function.EntryBlockId))
			{
				HashSet<int> next;
				if (block.Predecessors.Count == 0)
				{
					next = [block.Id];
				}
				else
				{
					next = new HashSet<int>(dominators[block.Predecessors[0]]);
					foreach (var predecessor in block.Predecessors.Skip(1))
					{
						next.IntersectWith(dominators[predecessor]);
					}
					next.Add(block.Id);
				}
				if (!next.SetEquals(dominators[block.Id]))
				{
					dominators[block.Id] = next;
					changed = true;
				}
			}
		}
		return dominators;
	}

	private static IReadOnlyDictionary<int, int?> ComputeImmediateDominators(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, HashSet<int>> dominators)
	{
		var result = new Dictionary<int, int?>
		{
			[function.EntryBlockId] = null
		};
		foreach (var block in function.Blocks.Where(block =>
			block.Id != function.EntryBlockId))
		{
			var strict = dominators[block.Id]
				.Where(id => id != block.Id)
				.OrderByDescending(id => dominators[id].Count)
				.ToArray();
			result[block.Id] = strict.Length == 0 ? null : strict[0];
		}
		return result;
	}

	private static IReadOnlyDictionary<int, HashSet<int>>
		ComputeDominanceFrontiers(
			M68kMachineFunction function,
			IReadOnlyDictionary<int, int?> immediateDominators)
	{
		var frontiers = function.Blocks.ToDictionary(
			static block => block.Id,
			static _ => new HashSet<int>());
		foreach (var block in function.Blocks.Where(static block =>
			block.Predecessors.Distinct().Count() >= 2))
		{
			var parent = immediateDominators[block.Id];
			foreach (var predecessor in block.Predecessors.Distinct())
			{
				int? runner = predecessor;
				while (runner is { } runnerId && runner != parent)
				{
					frontiers[runnerId].Add(block.Id);
					runner = immediateDominators[runnerId];
				}
			}
		}
		return frontiers;
	}
}
