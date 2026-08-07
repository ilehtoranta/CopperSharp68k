/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal static class CilMachineIrBuilder
{
	private sealed class StackVarargsArrayCandidate
	{
		public StackVarargsArrayCandidate(int length)
		{
			Elements = new int?[length];
		}

		public int?[] Elements { get; }

		public HashSet<int> GeneratedInstructionIds { get; } = [];

		public bool IsInvalidated { get; set; }
	}

	private sealed record BlockBuildState(
		M68kMachineBlock Block,
		IReadOnlyList<CilInstruction> Instructions,
		ImmutableArray<CilStackValueKind> EntryKinds,
		List<int> EntryValues,
		List<int> ExitValues,
		int?[] EntryLocals,
		int?[] ExitLocals,
		Dictionary<int, M68kMachinePhi> LocalPhis);

	public static M68kMachineFunction Build(
		CilMethod method,
		CompilationModule module,
		M68kCpuTarget cpu = M68kCpuTarget.M68000,
		bool hasRuntimeFrame = false,
		CilOptimizationPlan? optimizations = null,
		IReadOnlyList<M68kRegister?>? argumentRegisters = null)
	{
		optimizations ??= CilOptimizer.Optimize(method, module);
		var stackStates = CilStackAnalyzer.AnalyzeTypes(method, module);
		var reachableOffsets = stackStates.Keys.ToHashSet();
		var instructions = method.Instructions
			.Where(instruction => reachableOffsets.Contains(instruction.Offset))
			.ToArray();
		if (instructions.Length == 0)
		{
			throw new InvalidOperationException(
				$"Cannot build machine IR for empty method '{method.DisplayName}'.");
		}
		var leaders = FindLeaders(method, instructions, reachableOffsets);
		var blockInstructions = PartitionBlocks(instructions, leaders);
		var blocksByOffset = new Dictionary<int, M68kMachineBlock>();
		var function = new M68kMachineFunction(method.DisplayName, 0)
		{
			ReservedRegisters = M68kRegisterSet.None,
			HasExceptionHandlers = method.ExceptionRegions.Count != 0
		};
		for (var index = 0; index < blockInstructions.Count; index++)
		{
			var block = new M68kMachineBlock(
				index,
				blockInstructions[index][0].Offset)
			{
				IsExceptionEntry = method.ExceptionRegions.Any(region =>
					region.HandlerOffset == blockInstructions[index][0].Offset ||
					region.FilterOffset == blockInstructions[index][0].Offset)
			};
			function.Blocks.Add(block);
			blocksByOffset.Add(block.StartIlOffset, block);
		}

		ConnectBlocks(blockInstructions, function.Blocks, blocksByOffset);
		ComputeLoopDepths(function);
		var promotableLocals = GetPromotableLocals(method, module);
		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (!promotableLocals[index])
			{
				var type = method.Locals[index];
				function.LocalHomes.Add(
					index,
					new M68kFrameHome(
						index,
							FrameHomeSize(module, type, includeAlignmentPadding: true),
							type.IsReference,
							GcReferenceOffsets:
								GcReferenceOffsets(module, type, method.ModuleName)));
			}
		}
		var argumentHomeIndices = method.Instructions
			.Select(instruction =>
				TryGetLoadArgumentAddressIndex(instruction, out var index)
					? (int?)index
					: null)
			.OfType<int>()
			.ToHashSet();
		for (var argumentIndex = 0;
			argumentIndex < method.ParameterCount;
			argumentIndex++)
		{
			var type = ArgumentType(method, argumentIndex);
			if (module.TryGetReferenceFreeStructLayout(
					type,
					method.ModuleName,
					out var layout) &&
				layout.Size > 4)
			{
				argumentHomeIndices.Add(argumentIndex);
			}
		}
		foreach (var argumentIndex in argumentHomeIndices.Order())
		{
			var type = ArgumentType(method, argumentIndex);
			function.ArgumentHomes.Add(
				argumentIndex,
				new M68kFrameHome(
					argumentIndex,
						FrameHomeSize(module, type, includeAlignmentPadding: false),
						type.IsReference,
						GcReferenceOffsets:
							GcReferenceOffsets(module, type, method.ModuleName)));
		}
		var aggregateTemporaryHomes = CreateAggregateTemporaryHomes(
			function,
			method,
			module,
			instructions);
		var entryLocalValues = new int?[method.Locals.Length];
		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (promotableLocals[index])
			{
				entryLocalValues[index] = CreateValueForType(
					function,
					method.Locals[index],
					module).Id;
			}
		}

		var states = new List<BlockBuildState>();
		var argumentValues = new int?[method.ParameterCount];
		var entryBlock = function.Blocks.Single(
			block => block.Id == function.EntryBlockId);
		foreach (var block in function.Blocks)
		{
			var entryKinds = stackStates[block.StartIlOffset];
			var entryValues = CreateStackValues(function, entryKinds);
			var localValues = block.Id == function.EntryBlockId
				? (int?[])entryLocalValues.Clone()
				: CreateBlockLocalValues(
					function,
					method,
					module,
					promotableLocals);
			if (block.Id == function.EntryBlockId || block.IsExceptionEntry)
			{
				foreach (var value in entryValues.Distinct())
				{
					block.Instructions.Add(function.CreateInstruction(
						M68kMachineOperation.Other,
						block.StartIlOffset,
						definitions: [value]));
				}
			}
			if (block.Id == function.EntryBlockId)
			{
				foreach (var value in localValues.OfType<int>())
				{
					block.Instructions.Add(function.CreateInstruction(
						method.InitializeLocals
							? M68kMachineOperation.Constant
							: M68kMachineOperation.Other,
						block.StartIlOffset,
						definitions: [value]));
				}
			}

			states.Add(new BlockBuildState(
				block,
				blockInstructions[block.Id],
				entryKinds,
				entryValues,
				new List<int>(),
				localValues,
				new int?[method.Locals.Length],
				new Dictionary<int, M68kMachinePhi>()));
		}

		foreach (var state in states)
		{
			if (state.Block.Id != function.EntryBlockId &&
				!state.Block.IsExceptionEntry)
			{
				AddEntryPhis(state);
				AddLocalPhis(state);
			}
			LowerBlock(
				function,
				method,
				module,
				cpu,
				optimizations,
				state,
				entryBlock,
				argumentValues,
				argumentRegisters,
				aggregateTemporaryHomes);
		}
		PopulatePhiInputs(states);
		EliminateDeadMachineValues(function);
		M68kConditionFlowOptimizer.Run(function, method, module);
		EliminateDeadMachineValues(function);
		EliminateUnusedLocalHomes(function);
		M68kManagedByrefTypeTracker.TrackAndValidate(function, method, module);
		M68kMachineCostAnalysis.Apply(function);
		M68kMachineIrVerifier.Verify(function);
		return function;
	}

	private static void EliminateUnusedLocalHomes(M68kMachineFunction function)
	{
		var readHomes = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation is
					M68kMachineOperation.LocalLoad or
					M68kMachineOperation.LocalAddress or
					M68kMachineOperation.Unbox or
					M68kMachineOperation.AggregateFieldLoad or
					M68kMachineOperation.AggregateArrayLoad or
					M68kMachineOperation.AggregateIndirectLoad or
					M68kMachineOperation.AggregateIndirectCopy)
			.Select(static instruction => instruction.ArgumentIndex)
			.OfType<int>()
			.ToHashSet();
		var unusedHomes = function.LocalHomes
			.Where(entry => !entry.Value.HasGcReferences && !readHomes.Contains(entry.Key))
			.Select(static entry => entry.Key)
			.ToHashSet();
		if (unusedHomes.Count == 0)
		{
			return;
		}
		foreach (var block in function.Blocks)
		{
			block.Instructions.RemoveAll(instruction =>
				instruction.Operation == M68kMachineOperation.LocalStore &&
				instruction.ArgumentIndex is { } index &&
				unusedHomes.Contains(index));
		}
		foreach (var index in unusedHomes)
		{
			function.LocalHomes.Remove(index);
		}
	}

	private static void EliminateDeadMachineValues(
		M68kMachineFunction function)
	{
		bool changed;
		do
		{
			var phisByDefinition = function.Blocks
				.SelectMany(static block => block.Phis)
				.ToDictionary(static phi => phi.Definition);
			var livePhiValues = function.Blocks
				.SelectMany(static block => block.Instructions)
				.SelectMany(static instruction => instruction.Uses)
				.ToHashSet();
			var pending = new Stack<int>(livePhiValues);
			while (pending.TryPop(out var value))
			{
				if (!phisByDefinition.TryGetValue(value, out var phi))
				{
					continue;
				}
				foreach (var input in phi.Inputs.Values)
				{
					if (livePhiValues.Add(input))
					{
						pending.Push(input);
					}
				}
			}
			changed = false;
			foreach (var block in function.Blocks)
			{
				changed |= block.Phis.RemoveAll(phi =>
					!livePhiValues.Contains(phi.Definition)) != 0;
			}
			var used = function.Blocks
				.SelectMany(static block =>
					block.Instructions.SelectMany(static instruction =>
						instruction.Uses)
					.Concat(block.Phis.SelectMany(static phi =>
						phi.Inputs.Values)))
				.ToHashSet();
			foreach (var block in function.Blocks)
			{
				changed |= block.Instructions.RemoveAll(instruction =>
					instruction.Definitions.Length != 0 &&
					instruction.Definitions.All(definition =>
						!used.Contains(definition)) &&
					IsDeadMachineInstructionRemovable(instruction)) != 0;
			}
		}
		while (changed);

		var referenced = function.Blocks
			.SelectMany(static block =>
				block.Instructions.SelectMany(static instruction =>
					instruction.Uses.Concat(instruction.Definitions))
				.Concat(block.Phis.SelectMany(static phi =>
					phi.Inputs.Values.Append(phi.Definition))))
			.ToHashSet();
		foreach (var value in function.Values.Keys
			.Where(value => !referenced.Contains(value))
			.ToArray())
		{
			function.Values.Remove(value);
		}
	}

	private static bool IsDeadMachineInstructionRemovable(
		M68kMachineInstruction instruction) =>
		instruction.MemoryEffect == M68kMachineMemoryEffect.None &&
		!instruction.IsSafepoint &&
		!instruction.MayThrow &&
		instruction.Operation is
			M68kMachineOperation.Argument or
			M68kMachineOperation.Copy or
			M68kMachineOperation.Constant or
			M68kMachineOperation.Address or
			M68kMachineOperation.Other;

	private static bool[] GetPromotableLocals(
		CilMethod method,
		CompilationModule module)
	{
		var result = new bool[method.Locals.Length];
		if (method.ExceptionRegions.Count != 0)
		{
			return result;
		}
		for (var index = 0; index < method.Locals.Length; index++)
		{
			var localIndex = index;
			result[index] =
				!method.Instructions.Select((instruction, instructionIndex) =>
						(Instruction: instruction, Index: instructionIndex))
					.Any(item =>
						TryGetLoadLocalAddressIndex(
							item.Instruction,
							out var addressed) &&
						addressed == localIndex &&
						!IsRegisterTransparentAddressAccess(
							method,
							module,
							localIndex,
							item.Index)) &&
				(method.Locals[index].IsSupportedScalar ||
				 method.Locals[index].IsReference ||
				 method.Locals[index].IsNullable ||
				 IsAddressType(method.Locals[index]) ||
				 module.IsTransparentScalarType(method.Locals[index]));
		}
		return result;
	}
	private static bool IsRegisterTransparentAddressAccess(
		CilMethod method,
		CompilationModule module,
		int localIndex,
		int addressInstructionIndex)
	{
		var localType = method.Locals[localIndex];
		var isTransparentScalar = module.IsTransparentScalarType(localType);
		var isCompactNullable =
			localType.NullableElementType is { } nullableElement &&
			module.IsTransparentScalarType(nullableElement);
		if (!isTransparentScalar && !isCompactNullable)
		{
			return false;
		}
		for (var index = addressInstructionIndex + 1;
			index < method.Instructions.Count;
			index++)
		{
			var instruction = method.Instructions[index];
			if (instruction.OpCode == OpCodes.Nop)
			{
				continue;
			}
			if (instruction.OpCode != OpCodes.Call &&
				instruction.OpCode != OpCodes.Callvirt)
			{
				return false;
			}
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (isCompactNullable)
			{
				return target.ImportName?.StartsWith(
						"intrinsic:nullable-has-value:",
						StringComparison.Ordinal) == true ||
					target.ImportName?.StartsWith(
						"intrinsic:nullable-get-value:",
						StringComparison.Ordinal) == true;
			}
			return target.Signature.ParameterTypes.Length == 0 &&
				target.Signature.ReturnType.DisplayName == "uint" &&
				(target.ImportName?.EndsWith("-raw", StringComparison.Ordinal) == true ||
				 target.Definition is { } definition &&
				 definition.Signature.Header.IsInstance &&
				 definition.Name == "get_Raw");
		}
		return false;
	}

	private static int FrameHomeSize(
		CompilationModule module,
		CilType type,
		bool includeAlignmentPadding)
	{
		var slotLongs =
			type.IsSupportedScalar && type.Size == 8 ||
			type.IsNullable && !(type.NullableElementType is { } nullableElement &&
				module.IsTransparentScalarType(nullableElement))
				? 2
				: module.IsSupportedStructType(type)
					? module.GetStructSlotLongs(type)
					: 1;
		if (includeAlignmentPadding &&
			module.RequiresLongAlignedStackAddress(type))
		{
			slotLongs++;
		}
		return checked(slotLongs * 4);
	}

	private static IReadOnlyList<int> GcReferenceOffsets(
		CompilationModule module,
		CilType type,
		string moduleName)
	{
		if (CompilationModule.IsSupportedSpanLikeType(type))
		{
			return [8];
		}
		if (!module.TryGetStructLayout(type, moduleName, out var layout) ||
			layout.ReferenceBitmap == 0)
		{
			return [];
		}
		return Enumerable.Range(0, 32)
			.Where(index => (layout.ReferenceBitmap & (1u << index)) != 0)
			.Select(static index => index * 4)
			.ToArray();
	}

	private static IReadOnlyDictionary<int, int> CreateAggregateTemporaryHomes(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyList<CilInstruction> reachableInstructions)
	{
		var result = new Dictionary<int, int>();
		var nextHomeIndex = method.Locals.Length;
		foreach (var instruction in reachableInstructions)
		{
			CilType type;
			if (instruction.OpCode == OpCodes.Unbox_Any)
			{
				type = module.ResolveTypeToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
			}
			else if (instruction.OpCode == OpCodes.Ldfld ||
				instruction.OpCode == OpCodes.Ldsfld)
			{
				type = module.ResolveFieldToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset).Type;
			}
			else if (instruction.OpCode == OpCodes.Ldelem)
			{
				type = module.ResolveTypeToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
			}
			else if (instruction.OpCode == OpCodes.Ldobj)
			{
				type = module.ResolveTypeToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset);
			}
			else
			{
				continue;
			}
			if (!module.TryGetReferenceFreeStructLayout(
					type,
					method.ModuleName,
					out var layout) ||
				layout.Size <= 4)
			{
				continue;
			}

			var next = method.Instructions.FirstOrDefault(candidate =>
				candidate.Offset == instruction.NextOffset);
			if (next is not null &&
				TryGetStoreLocalIndex(next, out var localIndex) &&
				localIndex >= 0 &&
				localIndex < method.Locals.Length &&
				method.Locals[localIndex].DisplayName == type.DisplayName &&
				function.LocalHomes.ContainsKey(localIndex))
			{
				continue;
			}

			var homeIndex = nextHomeIndex++;
			function.LocalHomes.Add(
				homeIndex,
				new M68kFrameHome(
					homeIndex,
					layout.Size,
					IsGcReference: false,
					Initialize: false));
			result.Add(instruction.Offset, homeIndex);
		}
		return result;
	}

	private static int AllocateAggregateTemporaryHome(
		M68kMachineFunction function,
		CilMethod method,
		int size,
		IReadOnlyList<int>? gcReferenceOffsets = null)
	{
		var homeIndex = function.LocalHomes.Keys
			.Where(index => index >= method.Locals.Length)
			.DefaultIfEmpty(method.Locals.Length - 1)
			.Max() + 1;
		function.LocalHomes.Add(
			homeIndex,
			new M68kFrameHome(
				homeIndex,
				size,
				IsGcReference: false,
				Initialize: gcReferenceOffsets is { Count: > 0 },
				GcReferenceOffsets: gcReferenceOffsets));
		return homeIndex;
	}

	private static CilType ArgumentType(CilMethod method, int argumentIndex)
	{
		if (method.Signature.Header.IsInstance)
		{
			if (argumentIndex == 0)
			{
				return new CilType(
					CilTypeKind.ManagedReference,
					4,
					method.DisplayName.Split("::", 2, StringSplitOptions.None)[0]);
			}
			argumentIndex--;
		}
		return method.Signature.ParameterTypes[argumentIndex];
	}

	private static int?[] CreateBlockLocalValues(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		IReadOnlyList<bool> promotableLocals)
	{
		var result = new int?[method.Locals.Length];
		for (var index = 0; index < method.Locals.Length; index++)
		{
			if (promotableLocals[index])
			{
				result[index] = CreateValueForType(
					function,
					method.Locals[index],
					module).Id;
			}
		}
		return result;
	}

	private static HashSet<int> FindLeaders(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		IReadOnlySet<int> reachableOffsets)
	{
		var leaders = new HashSet<int> { instructions[0].Offset };
		foreach (var region in method.ExceptionRegions)
		{
			if (reachableOffsets.Contains(region.HandlerOffset))
			{
				leaders.Add(region.HandlerOffset);
			}
			if (region.FilterOffset >= 0 &&
				reachableOffsets.Contains(region.FilterOffset))
			{
				leaders.Add(region.FilterOffset);
			}
		}

		foreach (var instruction in instructions)
		{
			if (instruction.OpCode == OpCodes.Switch)
			{
				foreach (var target in (int[])instruction.Operand!)
				{
					if (reachableOffsets.Contains(target))
					{
						leaders.Add(target);
					}
				}
			}
			else if (instruction.OpCode.FlowControl is
					FlowControl.Branch or FlowControl.Cond_Branch &&
				instruction.Operand is int target &&
				reachableOffsets.Contains(target))
			{
				leaders.Add(target);
			}

			if (EndsBlock(instruction.OpCode) &&
				reachableOffsets.Contains(instruction.NextOffset))
			{
				leaders.Add(instruction.NextOffset);
			}
		}
		return leaders;
	}

	private static List<IReadOnlyList<CilInstruction>> PartitionBlocks(
		IReadOnlyList<CilInstruction> instructions,
		IReadOnlySet<int> leaders)
	{
		var result = new List<IReadOnlyList<CilInstruction>>();
		var current = new List<CilInstruction>();
		foreach (var instruction in instructions)
		{
			if (current.Count != 0 && leaders.Contains(instruction.Offset))
			{
				result.Add(current);
				current = new List<CilInstruction>();
			}
			current.Add(instruction);
		}
		if (current.Count != 0)
		{
			result.Add(current);
		}
		return result;
	}

	private static void ConnectBlocks(
		IReadOnlyList<IReadOnlyList<CilInstruction>> blockInstructions,
		IReadOnlyList<M68kMachineBlock> blocks,
		IReadOnlyDictionary<int, M68kMachineBlock> blocksByOffset)
	{
		for (var index = 0; index < blocks.Count; index++)
		{
			var block = blocks[index];
			var last = blockInstructions[index][^1];
			if (last.OpCode == OpCodes.Switch)
			{
				foreach (var target in (int[])last.Operand!)
				{
					AddEdge(block, blocksByOffset[target]);
				}
				if (blocksByOffset.TryGetValue(last.NextOffset, out var switchFallthrough))
				{
					AddEdge(block, switchFallthrough);
				}
				continue;
			}
			if (last.OpCode.FlowControl == FlowControl.Cond_Branch)
			{
				AddEdge(block, blocksByOffset[(int)last.Operand!]);
				if (blocksByOffset.TryGetValue(last.NextOffset, out var fallthrough))
				{
					AddEdge(block, fallthrough);
				}
				continue;
			}
			if (last.OpCode.FlowControl == FlowControl.Branch)
			{
				AddEdge(block, blocksByOffset[(int)last.Operand!]);
				continue;
			}
			if (last.OpCode.FlowControl is FlowControl.Return or FlowControl.Throw ||
				last.OpCode == OpCodes.Endfinally)
			{
				continue;
			}
			if (index + 1 < blocks.Count)
			{
				AddEdge(block, blocks[index + 1]);
			}
		}
	}

	private static void AddEdge(
		M68kMachineBlock from,
		M68kMachineBlock to)
	{
		if (!from.Successors.Contains(to.Id))
		{
			from.Successors.Add(to.Id);
			to.Predecessors.Add(from.Id);
		}
	}

	private static void ComputeLoopDepths(M68kMachineFunction function)
	{
		M68kControlFlowAnalysis.ComputeLoopDepths(function);
	}

	private static List<int> CreateStackValues(
		M68kMachineFunction function,
		ImmutableArray<CilStackValueKind> kinds)
	{
		var result = new List<int>(kinds.Length);
		for (var index = 0; index < kinds.Length;)
		{
			var kind = kinds[index];
			if (kind is CilStackValueKind.Int64 or CilStackValueKind.Float64 &&
				index + 1 < kinds.Length &&
				kinds[index + 1] == kind)
			{
				var pair = CreateValue(function, kind, M68kMachineValueWidth.LongPair);
				result.Add(pair.Id);
				result.Add(pair.Id);
				index += 2;
				continue;
			}

			var value = CreateValue(function, kind, WidthFor(kind));
			result.Add(value.Id);
			index++;
		}
		return result;
	}

	private static void AddEntryPhis(BlockBuildState state)
	{
		for (var index = 0; index < state.EntryValues.Count; index++)
		{
			if (index != 0 &&
				state.EntryValues[index] == state.EntryValues[index - 1])
			{
				continue;
			}
			state.Block.Phis.Add(new M68kMachinePhi(
				state.EntryValues[index],
				new Dictionary<int, int>()));
		}
	}

	private static void AddLocalPhis(BlockBuildState state)
	{
		for (var index = 0; index < state.EntryLocals.Length; index++)
		{
			if (state.EntryLocals[index] is not { } value)
			{
				continue;
			}
			var phi = new M68kMachinePhi(value, new Dictionary<int, int>());
			state.Block.Phis.Add(phi);
			state.LocalPhis.Add(index, phi);
		}
	}

	private static void LowerBlock(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		M68kCpuTarget cpu,
		CilOptimizationPlan optimizations,
		BlockBuildState state,
		M68kMachineBlock entryBlock,
		int?[] argumentValues,
		IReadOnlyList<M68kRegister?>? argumentRegisters,
		IReadOnlyDictionary<int, int> aggregateTemporaryHomes)
	{
		var stackKinds = state.EntryKinds;
		var stackValues = new List<int>(state.EntryValues);
		var localValues = (int?[])state.EntryLocals.Clone();
		var stackVarargsArrays =
			new Dictionary<int, StackVarargsArrayCandidate>();
		for (var instructionIndex = 0;
			instructionIndex < state.Instructions.Count;
			instructionIndex++)
		{
			var instruction = state.Instructions[instructionIndex];
			if (optimizations.TryGet(instruction.Offset, out var optimization))
			{
				var endOffset = method.Instructions[optimization.EndIndex].Offset;
				var localEndIndex = instructionIndex;
				while (localEndIndex < state.Instructions.Count &&
					state.Instructions[localEndIndex].Offset != endOffset)
				{
					localEndIndex++;
				}
				if (localEndIndex >= state.Instructions.Count)
				{
					throw new InvalidOperationException(
						$"IL optimization at IL_{instruction.Offset:X4} crosses a machine block.");
				}
				if (LowerOptimization(
						function,
						state.Block,
						method,
						module,
						cpu,
						optimization,
						instruction,
						state.Instructions[localEndIndex],
						ref stackKinds,
						stackValues))
				{
					instructionIndex = localEndIndex;
					continue;
				}
			}

			if (TryLowerMultiwordUnboxAny(
					function,
					method,
					module,
					cpu,
					state,
					instructionIndex,
					instruction,
					ref stackKinds,
					stackValues,
					aggregateTemporaryHomes,
					out var consumedStore))
			{
				if (consumedStore)
				{
					instructionIndex++;
				}
				continue;
			}

			if (TryLowerReferenceFreeAggregateIndirectOperation(
					function,
					method,
					module,
					state,
					instructionIndex,
					instruction,
					ref stackKinds,
					stackValues,
					aggregateTemporaryHomes,
					out consumedStore))
			{
				if (consumedStore)
				{
					instructionIndex++;
				}
				continue;
			}

			if (TryLowerMultiwordFieldLoad(
					function,
					method,
					module,
					state,
					instructionIndex,
					instruction,
					ref stackKinds,
					stackValues,
					aggregateTemporaryHomes,
					out consumedStore))
			{
				if (consumedStore)
				{
					instructionIndex++;
				}
				continue;
			}

			if (TryLowerMultiwordArrayLoad(
					function,
					method,
					module,
					state,
					instructionIndex,
					instruction,
					ref stackKinds,
					stackValues,
					aggregateTemporaryHomes,
					out consumedStore))
			{
				if (consumedStore)
				{
					instructionIndex++;
				}
				continue;
			}

			if (instruction.OpCode == OpCodes.Dup)
			{
				var duplicatedValue = stackValues[^1];
				var generatedStart = state.Block.Instructions.Count;
				LowerDuplicate(
					function,
					state.Block,
					instruction,
					stackKinds,
					stackValues);
				if (stackVarargsArrays.TryGetValue(
						duplicatedValue,
						out var candidate))
				{
					var alias = stackValues[^1];
					stackVarargsArrays[alias] = candidate;
					AddGeneratedInstructionIds(
						state.Block,
						generatedStart,
						candidate);
				}
				stackKinds = CilStackAnalyzer.ApplyStackEffect(
					method,
					module,
					instruction,
					stackKinds);
				continue;
			}

			var popSlots = CilStackAnalyzer.GetPopSlotCount(
				method,
				module,
				instruction,
				stackKinds);
			var uses = CollapseStackOperands(
				stackKinds,
				stackValues,
				popSlots);
			if (popSlots != 0)
			{
				stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
			}
			var nextKinds = CilStackAnalyzer.ApplyStackEffect(
				method,
				module,
				instruction,
				stackKinds);
			IReadOnlyList<int> definitions;
			var operation = OperationFor(instruction.OpCode);
			int? frameIndex = null;
			if (TryGetLoadArgumentIndex(instruction, out var loadedArgument))
			{
				var pushedKinds = nextKinds
					.Skip(stackValues.Count)
					.ToImmutableArray();
				var stackDefinitions = CreateStackValues(function, pushedKinds);
				stackValues.AddRange(stackDefinitions);
				definitions = stackDefinitions.Distinct().ToArray();
				if (pushedKinds.Length == 1 &&
					pushedKinds[0] == CilStackValueKind.AggregateAddress)
				{
					uses = [];
					operation = M68kMachineOperation.ArgumentAddress;
					frameIndex = loadedArgument;
				}
				else
				{
					var argumentValue = GetOrCreateArgumentValue(
						function,
						entryBlock,
						loadedArgument,
						pushedKinds,
						argumentValues,
						argumentRegisters);					uses = [argumentValue];
					operation = M68kMachineOperation.Copy;
				}
			}
			else if (TryGetLoadLocalIndex(instruction, out var loadedLocal) &&
				localValues[loadedLocal] is { } loadedValue &&
				nextKinds[stackValues.Count] !=
					CilStackValueKind.AggregateAddress)
			{
				var copy = CreateValueForType(
					function,
					method.Locals[loadedLocal],
					module);
				definitions = [copy.Id];
				uses = [loadedValue];
				if (copy.IsRegisterPair)
				{
					stackValues.Add(copy.Id);
				}
				stackValues.Add(copy.Id);
				operation = M68kMachineOperation.Copy;
			}
			else if (TryGetStoreLocalIndex(instruction, out var storedLocal) &&
				localValues[storedLocal] is not null)
			{
				var copy = CreateValueForType(
					function,
					method.Locals[storedLocal],
					module);
				definitions = [copy.Id];
				localValues[storedLocal] = copy.Id;
				operation = M68kMachineOperation.Copy;
			}
			else if (TryGetLoadLocalAddressIndex(
					instruction,
					out var promotedAddressLocal) &&
				localValues[promotedAddressLocal] is { } promotedAddressValue)
			{
				var pushedKinds = nextKinds
					.Skip(stackValues.Count)
					.ToImmutableArray();
				var stackDefinitions = CreateStackValues(function, pushedKinds);
				stackValues.AddRange(stackDefinitions);
				definitions = stackDefinitions.Distinct().ToArray();
				uses = [promotedAddressValue];
				operation = M68kMachineOperation.Copy;
			}
			else
			{
				var pushedKinds = nextKinds.Skip(stackValues.Count).ToImmutableArray();
				var stackDefinitions = CreateStackValues(function, pushedKinds);
				stackValues.AddRange(stackDefinitions);
				definitions = stackDefinitions.Distinct().ToArray();
				if (instruction.OpCode == OpCodes.Localloc)
				{
					if (uses.Length == 1 &&
						TryGetMachineIntegerConstant(
							state.Block,
							uses[0],
							out var byteCount) &&
						byteCount >= 0)
					{
						operation = M68kMachineOperation.LocalAddress;
						frameIndex = AllocateAggregateTemporaryHome(
							function,
							method,
							Math.Max(byteCount, 1));
						uses = [];
					}
					else
					{
						operation = M68kMachineOperation.DynamicStackAllocate;
						function.HasDynamicStackAllocation = true;
						function.ReservedRegisters =
							function.ReservedRegisters.Add(M68kRegister.A5);
					}
				}
				else if (TryGetLoadLocalIndex(instruction, out loadedLocal))
				{
					operation = pushedKinds.Length == 1 &&
						pushedKinds[0] ==
							CilStackValueKind.AggregateAddress
						? M68kMachineOperation.LocalAddress
						: M68kMachineOperation.LocalLoad;
					frameIndex = loadedLocal;
				}
				else if (TryGetStoreLocalIndex(instruction, out storedLocal))
				{
					operation = M68kMachineOperation.LocalStore;
					frameIndex = storedLocal;
				}
				else if (TryGetStoreArgumentIndex(
					instruction,
					out var storedArgument))
				{
					var type = ArgumentType(method, storedArgument);
					if (!module.TryGetReferenceFreeStructLayout(
							type,
							method.ModuleName,
							out var layout) ||
						layout.Size <= 4)
					{
						throw new M68kCompilationException(
							M68kDiagnosticIds.UnsupportedInstruction,
							$"Assigning argument '{type.DisplayName}' with starg is not supported by the current runtime profile.",
							method.DisplayName,
							instruction.Offset);
					}
					operation = M68kMachineOperation.ArgumentStore;
					frameIndex = storedArgument;
				}
				else if (TryGetLoadLocalAddressIndex(
					instruction,
					out var localAddress))
				{
					operation = M68kMachineOperation.LocalAddress;
					frameIndex = localAddress;
				}
				else if (TryGetLoadArgumentAddressIndex(
					instruction,
					out var argumentAddress))
				{
					operation = M68kMachineOperation.ArgumentAddress;
					frameIndex = argumentAddress;
				}
			}
			if (operation == M68kMachineOperation.LocalStore &&
				frameIndex is { } aggregateLocalIndex)
			{
				uses = RewriteMultiwordLocalStore(
					function,
					state.Block,
					method,
					module,
					instruction,
					aggregateLocalIndex,
					uses);
			}
			if (operation == M68kMachineOperation.ArgumentStore &&
				(uses.Length != 1 ||
				 function.Values[uses[0]].Kind !=
					CilStackValueKind.AggregateAddress))
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					"Storing a multiword argument requires a stable aggregate expression.",
					method.DisplayName,
					instruction.Offset);
			}
			uses = RewriteMultiwordFieldStore(
				function,
				state.Block,
				method,
				module,
				instruction,
				uses);
			if (operation == M68kMachineOperation.Load &&
				(IsIndirectLoad(instruction.OpCode) ||
					instruction.OpCode == OpCodes.Ldfld) &&
				uses.Length == 1 &&
				TryFoldDirectFrameAddress(
					state.Block,
					uses[0],
					method,
					module,
					instruction,
					out var frameLoadOperation,
					out var frameLoadIndex))
			{
				operation = frameLoadOperation;
				frameIndex = frameLoadIndex;
				uses = [];
			}
			if (operation == M68kMachineOperation.Return)
			{
				uses = RewriteMultiwordReturn(
					function,
					state.Block,
					method,
					module,
					instruction,
					uses);
			}
			if (module.GetTriggeredTypeInitializer(method, instruction) is not null)
			{
				state.Block.Instructions.Add(function.CreateInstruction(
					M68kMachineOperation.TypeInitialize,
					instruction.Offset,
					clobbers: M68kRegisterSet.From(
						M68kRegister.D0,
						M68kRegister.D1,
						M68kRegister.A0,
						M68kRegister.A1),
					memoryEffect: M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
					isSafepoint: true,
					mayThrow: true,
					sourceInstruction: instruction));
			}
			if (instruction.OpCode == OpCodes.Box)
			{
				AddConstrainedBox(
					function,
					state.Block,
					method,
					module,
					instruction,
					uses,
					definitions);
			}
			else if (instruction.OpCode == OpCodes.Newarr)
			{
				var generatedStart = state.Block.Instructions.Count;
				AddConstrainedArrayAllocation(
					function,
					state.Block,
					instruction,
					uses,
					definitions);
				if (definitions.Count == 1 &&
					uses.Length == 1 &&
					IsStackVarargsNewArray(method, module, instruction) &&
					TryGetMachineIntegerConstant(
						state.Block,
						uses[0],
						out var varargsLength) &&
					varargsLength >= 0)
				{
					var candidate =
						new StackVarargsArrayCandidate(varargsLength);
					stackVarargsArrays.Add(definitions[0], candidate);
					AddGeneratedInstructionIds(
						state.Block,
						generatedStart,
						candidate);
				}
			}
			else if (instruction.OpCode == OpCodes.Stelem &&
				uses.Length == 3 &&
				stackVarargsArrays.TryGetValue(
					uses[0],
					out var genericStoreCandidate))
			{
				var generatedStart = state.Block.Instructions.Count;
				AddConstrainedArrayAccess(
					function,
					state.Block,
					method,
					module,
					instruction,
					cpu,
					operation,
					uses,
					definitions);
				if (!genericStoreCandidate.IsInvalidated &&
					TryGetMachineIntegerConstant(
						state.Block,
						uses[1],
						out var elementIndex) &&
					elementIndex >= 0 &&
					elementIndex < genericStoreCandidate.Elements.Length &&
					genericStoreCandidate.Elements[elementIndex] is null)
				{
					genericStoreCandidate.Elements[elementIndex] = uses[2];
					AddGeneratedInstructionIds(
						state.Block,
						generatedStart,
						genericStoreCandidate);
				}
				else
				{
					genericStoreCandidate.IsInvalidated = true;
				}
			}
			else if (IsMachineArrayAccess(instruction.OpCode))
			{
				var generatedStart = state.Block.Instructions.Count;
				AddConstrainedArrayAccess(
					function,
					state.Block,
					method,
					module,
					instruction,
					cpu,
					operation,
					uses,
					definitions);
				if (operation == M68kMachineOperation.ArrayStore &&
					uses.Length == 3 &&
					stackVarargsArrays.TryGetValue(
						uses[0],
						out var storedCandidate) &&
					!storedCandidate.IsInvalidated &&
					TryGetMachineIntegerConstant(
						state.Block,
						uses[1],
						out var elementIndex) &&
					elementIndex >= 0 &&
					elementIndex < storedCandidate.Elements.Length &&
					storedCandidate.Elements[elementIndex] is null)
				{
					storedCandidate.Elements[elementIndex] = uses[2];
					AddGeneratedInstructionIds(
						state.Block,
						generatedStart,
						storedCandidate);
				}
				else if (uses.Length != 0 &&
					stackVarargsArrays.TryGetValue(
						uses[0],
						out var invalidCandidate))
				{
					invalidCandidate.IsInvalidated = true;
				}
			}
			else if (RequiresAddressBase(instruction.OpCode) &&
				operation is not
					M68kMachineOperation.LocalLoad and not
					M68kMachineOperation.ArgumentLoad)
			{
				AddAddressConstrainedOperation(
					function,
					state.Block,
					method,
					module,
					instruction,
					cpu,
					operation,
					uses,
					definitions);
			}
			else if (instruction.OpCode == OpCodes.Newobj &&
				module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset).ImportName == "intrinsic:delegate-ctor")
			{
				AddConstrainedDelegateConstruction(
					function,
					state.Block,
					instruction,
					uses,
					definitions);
			}
			else if (instruction.OpCode == OpCodes.Newobj &&
				module.ResolveMethodToken(
					(int)instruction.Operand!,
					method,
					instruction.Offset).Definition is { } constructor &&
				!module.IsTransparentScalarConstructor(constructor))
			{
				AddConstrainedObjectConstruction(
					function,
					state.Block,
					method,
					module,
					instruction,
					cpu,
					uses,
					definitions);
			}
			else if (instruction.OpCode == OpCodes.Call ||
				instruction.OpCode == OpCodes.Callvirt ||
				instruction.OpCode == OpCodes.Newobj)
			{
				M68kRegister? stackVarargsRegister = null;


				if (uses.Length != 0 &&
					TryFindStackVarargsCandidate(
						state.Block,
						uses,
						stackVarargsArrays,
						out var callCandidate,
						out var arrayUseIndex) &&
					TryFlattenStackVarargsCall(
						method,
						module,
						instruction,
						uses,
						arrayUseIndex,
						callCandidate,
						out var flattenedUses,
						out stackVarargsRegister))
				{
					uses = flattenedUses;
					state.Block.Instructions.RemoveAll(candidateInstruction =>
						callCandidate.GeneratedInstructionIds.Contains(
							candidateInstruction.Id));
				}
				AddConstrainedCall(
					function,
					state.Block,
					method,
					module,
					instruction,
					cpu,
					uses,
					definitions,
					stackVarargsRegister: stackVarargsRegister);
			}
			else if (RequiresFixedDataOperands(instruction.OpCode) &&
				(uses.Length == 0 || function.Values[uses[0]].Kind is not
					(CilStackValueKind.Float32 or CilStackValueKind.Float64)))
			{
				AddFixedDataOperation(
					function,
					state.Block,
					method,
					module,
					instruction,
					cpu,
					operation,
					uses,
					definitions);
			}
			else
			{
				state.Block.Instructions.Add(function.CreateInstruction(
					operation,
					instruction.Offset,
					uses,
					definitions,
					ClobbersFor(method, module, instruction, cpu),
					MemoryEffectFor(operation, instruction.OpCode),
					isSafepoint: IsConservativeSafepoint(instruction.OpCode),
					mayThrow: MayThrow(instruction.OpCode),
					producesConditionCodes: IsComparison(instruction.OpCode),
					sourceInstruction: instruction,
					argumentIndex: frameIndex));
			}
			stackKinds = nextKinds;
		}
		state.ExitValues.AddRange(stackValues);
		Array.Copy(localValues, state.ExitLocals, localValues.Length);
	}

	private static bool TryLowerMultiwordUnboxAny(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		M68kCpuTarget cpu,
		BlockBuildState state,
		int instructionIndex,
		CilInstruction instruction,
		ref ImmutableArray<CilStackValueKind> stackKinds,
		List<int> stackValues,
		IReadOnlyDictionary<int, int> aggregateTemporaryHomes,
		out bool consumedStore)
	{
		consumedStore = false;
		if (instruction.OpCode != OpCodes.Unbox_Any)
		{
			return false;
		}

		var type = module.ResolveTypeToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (!module.TryGetReferenceFreeStructLayout(
				type,
				method.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			return false;
		}

		var localIndex = -1;
		var hasDirectLocalDestination =
			instructionIndex + 1 < state.Instructions.Count &&
			TryGetStoreLocalIndex(
				state.Instructions[instructionIndex + 1],
				out localIndex) &&
			localIndex >= 0 &&
			localIndex < method.Locals.Length &&
			method.Locals[localIndex].DisplayName == type.DisplayName &&
			function.LocalHomes.ContainsKey(localIndex);
		if (!hasDirectLocalDestination &&
			!aggregateTemporaryHomes.TryGetValue(
				instruction.Offset,
				out localIndex))
		{
			throw new InvalidOperationException(
				$"Multiword expression at IL_{instruction.Offset:X4} has no frame home.");
		}

		var popSlots = CilStackAnalyzer.GetPopSlotCount(
			method,
			module,
			instruction,
			stackKinds);
		var uses = CollapseStackOperands(stackKinds, stackValues, popSlots);
		stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
		state.Block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Unbox,
			instruction.Offset,
			uses,
			clobbers: ClobbersFor(method, module, instruction, cpu),
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Write,
			mayThrow: true,
			sourceInstruction: instruction,
			argumentIndex: localIndex));

		var unboxedKinds = CilStackAnalyzer.ApplyStackEffect(
			method,
			module,
			instruction,
			stackKinds);
		if (hasDirectLocalDestination)
		{
			stackKinds = CilStackAnalyzer.ApplyStackEffect(
				method,
				module,
				state.Instructions[instructionIndex + 1],
				unboxedKinds);
			consumedStore = true;
			return true;
		}

		var address = function.CreateValue(
			CilStackValueKind.AggregateAddress,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		state.Block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalAddress,
			instruction.Offset,
			definitions: [address.Id],
			argumentIndex: localIndex));
		stackValues.Add(address.Id);
		stackKinds = unboxedKinds;
		return true;
	}

	private static bool TryLowerReferenceFreeAggregateIndirectOperation(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		BlockBuildState state,
		int instructionIndex,
		CilInstruction instruction,
		ref ImmutableArray<CilStackValueKind> stackKinds,
		List<int> stackValues,
		IReadOnlyDictionary<int, int> aggregateTemporaryHomes,
		out bool consumedStore)
	{
		consumedStore = false;
		var op = instruction.OpCode;
		if (op != OpCodes.Ldobj && op != OpCodes.Stobj &&
			op != OpCodes.Cpobj && op != OpCodes.Initobj)
		{
			return false;
		}

		var type = module.ResolveTypeToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		var hasLayout = op == OpCodes.Initobj
			? module.TryGetIndirectInitializeLayout(
				type,
				method.ModuleName,
				out var layout)
			: module.TryGetReferenceFreeStructLayout(
				type,
				method.ModuleName,
				out layout);
		if (!hasLayout)
		{
			if (op == OpCodes.Cpobj || !type.IsSupportedScalar)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					op == OpCodes.Cpobj
						? $"cpobj requires an exact reference-free struct layout, not '{type.DisplayName}'."
						: $"{op.Name} is only supported for the current scalar profile or an exact reference-free struct layout, not '{type.DisplayName}'.",
					method.DisplayName,
					instruction.Offset);
			}
			return false;
		}
		if (layout.Size <= 4 && op != OpCodes.Cpobj)
		{
			return false;
		}

		var popSlots = CilStackAnalyzer.GetPopSlotCount(
			method,
			module,
			instruction,
			stackKinds);
		var uses = CollapseStackOperands(stackKinds, stackValues, popSlots);
		stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
		uses[0] = AddRegisterClassCopy(
			function,
			state.Block,
			instruction,
			uses[0],
			M68kRegisterSet.Address);

		if (op == OpCodes.Ldobj)
		{
			var localIndex = -1;
			var hasDirectLocalDestination =
				instructionIndex + 1 < state.Instructions.Count &&
				TryGetStoreLocalIndex(
					state.Instructions[instructionIndex + 1],
					out localIndex) &&
				localIndex >= 0 &&
				localIndex < method.Locals.Length &&
				method.Locals[localIndex].DisplayName == type.DisplayName &&
				function.LocalHomes.ContainsKey(localIndex);
			if (!hasDirectLocalDestination &&
				!aggregateTemporaryHomes.TryGetValue(
					instruction.Offset,
					out localIndex))
			{
				localIndex = AllocateAggregateTemporaryHome(
					function,
					method,
					layout.Size);
			}

			var definitions = Array.Empty<int>();
			if (!hasDirectLocalDestination)
			{
				var address = function.CreateValue(
					CilStackValueKind.AggregateAddress,
					M68kMachineValueWidth.Long,
					M68kRegisterSet.Address);
				definitions = [address.Id];
				stackValues.Add(address.Id);
			}
			state.Block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.AggregateIndirectLoad,
				instruction.Offset,
				uses,
				definitions,
				M68kRegisterSet.From(M68kRegister.D0),
				M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
				mayThrow: true,
				sourceInstruction: instruction,
				argumentIndex: localIndex));

			var loadedKinds = CilStackAnalyzer.ApplyStackEffect(
				method,
				module,
				instruction,
				stackKinds);
			if (hasDirectLocalDestination)
			{
				stackKinds = CilStackAnalyzer.ApplyStackEffect(
					method,
					module,
					state.Instructions[instructionIndex + 1],
					loadedKinds);
				consumedStore = true;
				return true;
			}
			stackKinds = loadedKinds;
			return true;
		}

		M68kMachineOperation operation;
		int? temporaryHome = null;
		if (op == OpCodes.Stobj)
		{
			if (uses.Length != 2 ||
				function.Values[uses[1]].Kind !=
					CilStackValueKind.AggregateAddress)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					$"stobj '{type.DisplayName}' requires a stable aggregate value.",
					method.DisplayName,
					instruction.Offset);
			}
			uses[1] = AddRegisterClassCopy(
				function,
				state.Block,
				instruction,
				uses[1],
				M68kRegisterSet.Address);
			operation = M68kMachineOperation.AggregateIndirectStore;
		}
		else if (op == OpCodes.Cpobj)
		{
			uses[1] = AddRegisterClassCopy(
				function,
				state.Block,
				instruction,
				uses[1],
				M68kRegisterSet.Address);
			temporaryHome = AllocateAggregateTemporaryHome(
				function,
				method,
				layout.Size);
			operation = M68kMachineOperation.AggregateIndirectCopy;
		}
		else
		{
			operation = M68kMachineOperation.AggregateIndirectInitialize;
		}

		state.Block.Instructions.Add(function.CreateInstruction(
			operation,
			instruction.Offset,
			uses,
			clobbers: M68kRegisterSet.From(M68kRegister.D0),
			memoryEffect: operation ==
				M68kMachineOperation.AggregateIndirectInitialize
					? M68kMachineMemoryEffect.Write
					: M68kMachineMemoryEffect.Read |
						M68kMachineMemoryEffect.Write,
			mayThrow: true,
			sourceInstruction: instruction,
			argumentIndex: temporaryHome));
		stackKinds = CilStackAnalyzer.ApplyStackEffect(
			method,
			module,
			instruction,
			stackKinds);
		return true;
	}

	private static bool TryLowerMultiwordFieldLoad(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		BlockBuildState state,
		int instructionIndex,
		CilInstruction instruction,
		ref ImmutableArray<CilStackValueKind> stackKinds,
		List<int> stackValues,
		IReadOnlyDictionary<int, int> aggregateTemporaryHomes,
		out bool consumedStore)
	{
		consumedStore = false;
		if (instruction.OpCode != OpCodes.Ldfld &&
			instruction.OpCode != OpCodes.Ldsfld)
		{
			return false;
		}

		var field = module.ResolveFieldToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (!module.TryGetReferenceFreeStructLayout(
				field.Type,
				field.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			return false;
		}

		var localIndex = -1;
		var hasDirectLocalDestination =
			instructionIndex + 1 < state.Instructions.Count &&
			TryGetStoreLocalIndex(
				state.Instructions[instructionIndex + 1],
				out localIndex) &&
			localIndex >= 0 &&
			localIndex < method.Locals.Length &&
			method.Locals[localIndex].DisplayName == field.Type.DisplayName &&
			function.LocalHomes.ContainsKey(localIndex);
		if (!hasDirectLocalDestination &&
			!aggregateTemporaryHomes.TryGetValue(
				instruction.Offset,
				out localIndex))
		{
			localIndex = AllocateAggregateTemporaryHome(
				function,
				method,
				layout.Size);
		}

		var popSlots = CilStackAnalyzer.GetPopSlotCount(
			method,
			module,
			instruction,
			stackKinds);
		var uses = CollapseStackOperands(stackKinds, stackValues, popSlots);
		if (popSlots != 0)
		{
			stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
		}
		if (!field.IsStatic)
		{
			uses[0] = AddRegisterClassCopy(
				function,
				state.Block,
				instruction,
				uses[0],
				M68kRegisterSet.Address);
		}

		if (module.GetTriggeredTypeInitializer(method, instruction) is not null)
		{
			state.Block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.TypeInitialize,
				instruction.Offset,
				clobbers: M68kRegisterSet.From(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kRegister.A0,
					M68kRegister.A1),
				memoryEffect: M68kMachineMemoryEffect.Read |
					M68kMachineMemoryEffect.Write,
				isSafepoint: true,
				mayThrow: true,
				sourceInstruction: instruction));
		}

		var definitions = Array.Empty<int>();
		if (!hasDirectLocalDestination)
		{
			var address = function.CreateValue(
				CilStackValueKind.AggregateAddress,
				M68kMachineValueWidth.Long,
				M68kRegisterSet.Address);
			definitions = [address.Id];
			stackValues.Add(address.Id);
		}
		var clobbers = M68kRegisterSet.From(M68kRegister.D0);
		if (field.IsStatic)
		{
			clobbers = clobbers.Add(M68kRegister.A1);
		}
		state.Block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.AggregateFieldLoad,
			instruction.Offset,
			uses,
			definitions,
			clobbers,
			M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			mayThrow: !field.IsStatic,
			sourceInstruction: instruction,
			argumentIndex: localIndex));

		var loadedKinds = CilStackAnalyzer.ApplyStackEffect(
			method,
			module,
			instruction,
			stackKinds);
		if (hasDirectLocalDestination)
		{
			stackKinds = CilStackAnalyzer.ApplyStackEffect(
				method,
				module,
				state.Instructions[instructionIndex + 1],
				loadedKinds);
			consumedStore = true;
			return true;
		}

		stackKinds = loadedKinds;
		return true;
	}

	private static bool TryLowerMultiwordArrayLoad(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module,
		BlockBuildState state,
		int instructionIndex,
		CilInstruction instruction,
		ref ImmutableArray<CilStackValueKind> stackKinds,
		List<int> stackValues,
		IReadOnlyDictionary<int, int> aggregateTemporaryHomes,
		out bool consumedStore)
	{
		consumedStore = false;
		if (instruction.OpCode != OpCodes.Ldelem)
		{
			return false;
		}
		var type = module.ResolveTypeToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (!module.TryGetReferenceFreeStructLayout(
				type,
				method.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			return false;
		}

		var localIndex = -1;
		var hasDirectLocalDestination =
			instructionIndex + 1 < state.Instructions.Count &&
			TryGetStoreLocalIndex(
				state.Instructions[instructionIndex + 1],
				out localIndex) &&
			localIndex >= 0 &&
			localIndex < method.Locals.Length &&
			method.Locals[localIndex].DisplayName == type.DisplayName &&
			function.LocalHomes.ContainsKey(localIndex);
		if (!hasDirectLocalDestination &&
			!aggregateTemporaryHomes.TryGetValue(
				instruction.Offset,
				out localIndex))
		{
			localIndex = AllocateAggregateTemporaryHome(
				function,
				method,
				layout.Size);
		}

		var popSlots = CilStackAnalyzer.GetPopSlotCount(
			method,
			module,
			instruction,
			stackKinds);
		var uses = CollapseStackOperands(stackKinds, stackValues, popSlots);
		stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
		uses[0] = AddFixedRegisterCopy(
			function,
			state.Block,
			instruction,
			uses[0],
			M68kRegister.A2);
		uses[1] = AddFixedRegisterCopy(
			function,
			state.Block,
			instruction,
			uses[1],
			M68kRegister.D2);

		var definitions = Array.Empty<int>();
		if (!hasDirectLocalDestination)
		{
			var address = function.CreateValue(
				CilStackValueKind.AggregateAddress,
				M68kMachineValueWidth.Long,
				M68kRegisterSet.Address);
			definitions = [address.Id];
			stackValues.Add(address.Id);
		}
		state.Block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.AggregateArrayLoad,
			instruction.Offset,
			uses,
			definitions,
			M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0),
			M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			mayThrow: true,
			sourceInstruction: instruction,
			argumentIndex: localIndex));

		var loadedKinds = CilStackAnalyzer.ApplyStackEffect(
			method,
			module,
			instruction,
			stackKinds);
		if (hasDirectLocalDestination)
		{
			stackKinds = CilStackAnalyzer.ApplyStackEffect(
				method,
				module,
				state.Instructions[instructionIndex + 1],
				loadedKinds);
			consumedStore = true;
			return true;
		}
		stackKinds = loadedKinds;
		return true;
	}

	private static bool TryFindStackVarargsCandidate(
		M68kMachineBlock block,
		IReadOnlyList<int> uses,
		IReadOnlyDictionary<int, StackVarargsArrayCandidate> candidates,
		out StackVarargsArrayCandidate candidate,
		out int useIndex)
	{
		for (var index = 0; index < uses.Count; index++)
		{
			if (TryGetStackVarargsCandidate(
					block,
					uses[index],
					candidates,
					out candidate))
			{
				useIndex = index;
				return true;
			}
		}
		candidate = null!;
		useIndex = -1;
		return false;
	}
	private static bool TryGetStackVarargsCandidate(
		M68kMachineBlock block,
		int value,
		IReadOnlyDictionary<int, StackVarargsArrayCandidate> candidates,
		out StackVarargsArrayCandidate candidate)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			if (candidates.TryGetValue(value, out candidate!))
			{
				return true;
			}
			var copy = block.Instructions.LastOrDefault(instruction =>
				instruction.Operation == M68kMachineOperation.Copy &&
				instruction.Definitions.Contains(value) &&
				instruction.Uses.Length == 1);
			if (copy is null)
			{
				break;
			}
			value = copy.Uses[0];
		}

		candidate = null!;
		return false;
	}
	private static void AddGeneratedInstructionIds(
		M68kMachineBlock block,
		int startIndex,
		StackVarargsArrayCandidate candidate)
	{
		for (var index = startIndex; index < block.Instructions.Count; index++)
		{
			candidate.GeneratedInstructionIds.Add(block.Instructions[index].Id);
		}
	}

	private static bool TryFlattenStackVarargsCall(
		CilMethod caller,
		CompilationModule module,
		CilInstruction instruction,
		IReadOnlyList<int> uses,
		int arrayUseIndex,
		StackVarargsArrayCandidate candidate,
		out int[] flattenedUses,
		out M68kRegister? varargsRegister)
	{
		flattenedUses = [];
		varargsRegister = null;
		if (candidate.IsInvalidated ||
			candidate.Elements.Any(static element => element is null))
		{
			return false;
		}
		var target = module.ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		if (target.ImportName ==
			"intrinsic:boopsi-do-method-stack-varargs")
		{
			if (uses.Count != 2)
			{
				return false;
			}
			varargsRegister = M68kRegister.A1;
		}
		else if (!TryGetStackVarargsCallInfo(
			module,
			target,
			uses.Count - 1,
			out varargsRegister))
		{
			return false;
		}
		flattenedUses =
		[
			.. uses.Where((_, index) => index != arrayUseIndex),
			.. candidate.Elements.Select(static element => element!.Value)
		];
		return true;
	}

	private static bool TryGetStackVarargsCallInfo(
		CompilationModule module,
		MethodReference target,
		int fixedUseCount,
		out M68kRegister? varargsRegister)
	{
		varargsRegister = null;
		if (target.Definition?.ExternalCall is not { } externalCall ||
			target.Signature.ParameterTypes.Length == 0 ||
			fixedUseCount != target.Signature.ParameterTypes.Length - 1 ||
			target.Signature.ParameterTypes[^1].ElementType is not
				{ } elementType ||
			(elementType.DisplayName != "uint" &&
			 elementType.DisplayName != "Amiga.AmigaVarArg" &&
			 !module.IsTransparentScalarType(elementType)) ||
			externalCall.Abi.ParameterRegisters.Count == 0)
		{
			return false;
		}
		varargsRegister = externalCall.Abi.ParameterRegisters[^1];
		return true;
	}

	private static bool IsStackVarargsNewArray(
		CilMethod caller,
		CompilationModule module,
		CilInstruction instruction)
	{
		var elementType = module.ResolveTypeToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		return elementType.DisplayName == "uint" ||
			elementType.DisplayName == "Amiga.AmigaVarArg" ||
			module.IsTransparentScalarType(elementType);
	}

	private static bool TryGetMachineIntegerConstant(
		M68kMachineBlock block,
		int value,
		out int constant)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value))
		{
			var definition = block.Instructions
				.LastOrDefault(instruction =>
					instruction.Definitions.Contains(value));
			if (definition is null)
			{
				break;
			}
			if ((definition.Operation == M68kMachineOperation.Copy ||
				 definition.Operation == M68kMachineOperation.Convert &&
				 definition.SourceInstruction?.OpCode == OpCodes.Conv_U) &&
				definition.Uses.Length == 1)
			{
				value = definition.Uses[0];
				continue;
			}
			if (definition.Operation == M68kMachineOperation.Constant &&
				definition.SourceInstruction is { } source)
			{
				return TryGetIlIntegerConstant(source, out constant);
			}
			break;
		}
		constant = 0;
		return false;
	}

	private static bool TryFoldDirectFrameAddress(
		M68kMachineBlock block,
		int addressValue,
		CilMethod method,
		CompilationModule module,
		CilInstruction sourceInstruction,
		out M68kMachineOperation loadOperation,
		out int frameIndex)
	{
		// A managed pointer created by ldloca/ldarga is already a validated
		// frame address. Fold its sole direct load before register allocation so
		// no address temporary or null check is needed.
		loadOperation = default;
		frameIndex = default;
		if (sourceInstruction.OpCode == OpCodes.Ldfld)
		{
			var field = module.ResolveFieldToken(
				(int)sourceInstruction.Operand!,
				method,
				sourceInstruction.Offset);
			if (field.IsStatic || !module.IsTransparentScalarField(field))
			{
				return false;
			}
		}
		M68kMachineInstruction? addressInstruction = null;
		foreach (var instruction in block.Instructions)
		{
			if (!instruction.Definitions.Contains(addressValue))
			{
				continue;
			}
			addressInstruction = instruction;
			break;
		}
		if (addressInstruction is not
			{
				Operation: M68kMachineOperation.LocalAddress or
					M68kMachineOperation.ArgumentAddress,
				Definitions.Length: 1,
				ArgumentIndex: { } index
			} ||
			block.Instructions.Any(instruction =>
				instruction.Uses.Contains(addressValue)))
		{
			return false;
		}
		block.Instructions.Remove(addressInstruction);
		loadOperation = addressInstruction.Operation ==
			M68kMachineOperation.LocalAddress
			? M68kMachineOperation.LocalLoad
			: M68kMachineOperation.ArgumentLoad;
		frameIndex = index;
		return true;
	}

	private static bool TryGetIlIntegerConstant(
		CilInstruction instruction,
		out int constant)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Ldc_I4_M1)
		{
			constant = -1;
			return true;
		}
		if (op.Value >= OpCodes.Ldc_I4_0.Value &&
			op.Value <= OpCodes.Ldc_I4_8.Value)
		{
			constant = op.Value - OpCodes.Ldc_I4_0.Value;
			return true;
		}
		if (op == OpCodes.Ldc_I4 || op == OpCodes.Ldc_I4_S)
		{
			constant = Convert.ToInt32(instruction.Operand);
			return true;
		}
		constant = 0;
		return false;
	}

	private static int GetOrCreateArgumentValue(
		M68kMachineFunction function,
		M68kMachineBlock entryBlock,
		int argumentIndex,
		ImmutableArray<CilStackValueKind> pushedKinds,
		int?[] argumentValues,
		IReadOnlyList<M68kRegister?>? argumentRegisters)
	{
		if (argumentValues[argumentIndex] is { } prior)
		{
			return prior;
		}
		if (pushedKinds.Length == 0)
		{
			throw new InvalidOperationException(
				$"Argument {argumentIndex} has no machine value kind.");
		}
		var kind = pushedKinds[0];
		var width = kind is CilStackValueKind.Int64 or CilStackValueKind.Float64
			? M68kMachineValueWidth.LongPair
			: WidthFor(kind);
		var value = CreateValue(function, kind, width);
		if (argumentRegisters is not null &&
			argumentIndex < argumentRegisters.Count &&
			argumentRegisters[argumentIndex] is { } register)
		{
			var incoming = function.CreateValue(
				value.Kind,
				value.Width,
				M68kRegisterSet.From(register),
				precoloredRegister: register,
				isGcReference: value.IsGcReference,
				spillWeight: value.SpillWeight);
			entryBlock.Instructions.InsertRange(
				0,
				[
					function.CreateInstruction(
						M68kMachineOperation.Argument,
						entryBlock.StartIlOffset,
						definitions: [incoming.Id],
						argumentIndex: argumentIndex),
					function.CreateInstruction(
						M68kMachineOperation.Copy,
						entryBlock.StartIlOffset,
						uses: [incoming.Id],
						definitions: [value.Id])
				]);
		}
		else
		{
			entryBlock.Instructions.Insert(
				0,
				function.CreateInstruction(
					M68kMachineOperation.Argument,
					entryBlock.StartIlOffset,
					definitions: [value.Id],
					argumentIndex: argumentIndex));
		}
		argumentValues[argumentIndex] = value.Id;
		return value.Id;
	}

	private static void LowerDuplicate(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		ImmutableArray<CilStackValueKind> stackKinds,
		List<int> stackValues)
	{
		if (stackValues.Count == 0)
		{
			throw new InvalidOperationException(
				$"Machine IR duplicate at IL_{instruction.Offset:X4} has an empty stack.");
		}
		var source = stackValues[^1];
		var sourceValue = function.Values[source];
		var copy = function.CreateValue(
			sourceValue.Kind,
			sourceValue.Width,
			sourceValue.AllowedRegisters,
			isGcReference: sourceValue.IsGcReference,
			spillWeight: sourceValue.SpillWeight);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [source],
			definitions: [copy.Id],
			sourceInstruction: instruction));
		if (sourceValue.IsRegisterPair)
		{
			if (stackKinds.Length < 2 ||
				stackKinds[^1] != sourceValue.Kind ||
				stackKinds[^2] != sourceValue.Kind)
			{
				throw new InvalidOperationException(
					$"Machine IR duplicate at IL_{instruction.Offset:X4} has a malformed 64-bit value.");
			}
			stackValues.Add(copy.Id);
		}
		stackValues.Add(copy.Id);
	}

	private static int[] CollapseStackOperands(
		ImmutableArray<CilStackValueKind> stackKinds,
		IReadOnlyList<int> stackValues,
		int popSlots)
	{
		var result = new List<int>();
		var start = stackValues.Count - popSlots;
		for (var index = start; index < stackValues.Count; index++)
		{
			result.Add(stackValues[index]);
			if (stackKinds[index] is CilStackValueKind.Int64 or CilStackValueKind.Float64 &&
				index + 1 < stackValues.Count &&
				stackKinds[index + 1] == stackKinds[index] &&
				stackValues[index + 1] == stackValues[index])
			{
				index++;
			}
		}
		return result.ToArray();
	}

	private static bool RequiresFixedDataOperands(OpCode op) =>
		op == OpCodes.Mul ||
		op == OpCodes.Mul_Ovf_Un ||
		op == OpCodes.Div ||
		op == OpCodes.Div_Un ||
		op == OpCodes.Rem ||
		op == OpCodes.Rem_Un ||
		op == OpCodes.Shl ||
		op == OpCodes.Shr ||
		op == OpCodes.Shr_Un;

	private static void AddConstrainedArrayAllocation(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (uses.Count != 1 || definitions.Count != 1)
		{
			throw new InvalidOperationException(
				$"Array allocation at IL_{instruction.Offset:X4} has invalid arity.");
		}
		var length = function.Values[uses[0]];
		var fixedLength = function.CreateValue(
			length.Kind,
			length.Width,
			M68kRegisterSet.From(M68kRegister.D2),
			precoloredRegister: M68kRegister.D2);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [length.Id],
			definitions: [fixedLength.Id]));
		var result = function.Values[definitions[0]];
		var fixedResult = function.CreateValue(
			result.Kind,
			result.Width,
			M68kRegisterSet.From(M68kRegister.D0),
			precoloredRegister: M68kRegister.D0,
			isGcReference: true);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ArrayAllocate,
			instruction.Offset,
			uses: [fixedLength.Id],
			definitions: [fixedResult.Id],
			clobbers: M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1),
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: instruction));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [fixedResult.Id],
			definitions: definitions));
	}

	private static void AddConstrainedBox(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod caller,
		CompilationModule module,
		CilInstruction instruction,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (uses.Count != 1 || definitions.Count != 1)
		{
			throw new InvalidOperationException(
				$"Box at IL_{instruction.Offset:X4} has invalid arity: " +
				$"{uses.Count} use(s), {definitions.Count} definition(s).");
		}
		var source = function.Values[uses[0]];
		var boxedType = module.ResolveTypeToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		if (module.TryGetReferenceFreeStructLayout(
				boxedType,
				caller.ModuleName,
				out var structLayout) &&
			structLayout.Size > 4)
		{
			var producer = FindFrameValueProducer(block, source.Id);
			if (source.Kind == CilStackValueKind.AggregateAddress)
			{
				// The expression is already materialized in stable frame storage.
			}
			else
			{
				var addressOperation = producer?.Operation switch
				{
					M68kMachineOperation.LocalLoad => M68kMachineOperation.LocalAddress,
					M68kMachineOperation.Argument or M68kMachineOperation.ArgumentLoad =>
						M68kMachineOperation.ArgumentAddress,
					_ => (M68kMachineOperation?)null
				};
				if (addressOperation is null || producer!.ArgumentIndex is null)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"Boxing multiword struct '{boxedType.DisplayName}' requires a direct local, argument, or stable aggregate expression.",
						caller.DisplayName,
						instruction.Offset);
				}

				var address = function.CreateValue(
					CilStackValueKind.ManagedPointer,
					M68kMachineValueWidth.Long,
					M68kRegisterSet.DataOrAddress);
				block.Instructions.Add(function.CreateInstruction(
					addressOperation.Value,
					instruction.Offset,
					definitions: [address.Id],
					argumentIndex: producer.ArgumentIndex));
				source = address;
			}
		}
		var fixedSource = function.CreateValue(
			source.Kind,
			source.Width,
			M68kRegisterSet.From(M68kRegister.D2),
			precoloredRegister: M68kRegister.D2);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [source.Id],
			definitions: [fixedSource.Id]));
		var result = function.Values[definitions[0]];
		var fixedResult = function.CreateValue(
			result.Kind,
			result.Width,
			M68kRegisterSet.From(M68kRegister.D0),
			precoloredRegister: M68kRegister.D0,
			isGcReference: true);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Box,
			instruction.Offset,
			uses: [fixedSource.Id],
			definitions: [fixedResult.Id],
			clobbers: M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1),
			memoryEffect: M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: instruction));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [fixedResult.Id],
			definitions: definitions));
	}

	private static M68kMachineInstruction? FindFrameValueProducer(
		M68kMachineBlock block,
		int valueId)
	{
		var producer = block.Instructions.LastOrDefault(candidate =>
			candidate.Definitions.Contains(valueId));
		while (producer is { Operation: M68kMachineOperation.Copy } &&
			producer.Uses.Length == 1)
		{
			valueId = producer.Uses[0];
			producer = block.Instructions.LastOrDefault(candidate =>
				candidate.Definitions.Contains(valueId));
		}
		return producer;
	}

	private static int[] RewriteMultiwordReturn(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		IReadOnlyList<int> uses)
	{
		if (!module.TryGetReferenceFreeStructLayout(
				method.Signature.ReturnType,
				method.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			return uses.ToArray();
		}
		if (uses.Count != 1)
		{
			throw new InvalidOperationException(
				$"Multiword return at IL_{instruction.Offset:X4} has {uses.Count} values.");
		}

		var source = function.Values[uses[0]];
		var producer = FindFrameValueProducer(block, source.Id);
		if (source.Kind == CilStackValueKind.AggregateAddress)
		{
			return [source.Id];
		}

		var addressOperation = producer?.Operation switch
		{
			M68kMachineOperation.LocalLoad => M68kMachineOperation.LocalAddress,
			M68kMachineOperation.Argument or M68kMachineOperation.ArgumentLoad =>
				M68kMachineOperation.ArgumentAddress,
			_ => (M68kMachineOperation?)null
		};
		if (addressOperation is null || producer!.ArgumentIndex is null)
		{
			var detail = producer is null
				? "aggregate values crossing a control-flow merge require the typed multiword stack model"
				: $"producer is {producer.Operation}";
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Returning multiword struct '{method.Signature.ReturnType.DisplayName}' requires a direct local, argument, or stable aggregate expression; {detail}.",
				method.DisplayName,
				instruction.Offset);
		}

		var address = function.CreateValue(
			CilStackValueKind.AggregateAddress,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			addressOperation.Value,
			instruction.Offset,
			definitions: [address.Id],
			argumentIndex: producer.ArgumentIndex));
		return [address.Id];
	}

	private static int[] RewriteMultiwordLocalStore(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		int localIndex,
		IReadOnlyList<int> uses)
	{
		var type = method.Locals[localIndex];
		if (!module.TryGetReferenceFreeStructLayout(
				type,
				method.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			return uses.ToArray();
		}
		if (uses.Count != 1)
		{
			throw new InvalidOperationException(
				$"Multiword local store at IL_{instruction.Offset:X4} has {uses.Count} values.");
		}

		var source = function.Values[uses[0]];
		var producer = FindFrameValueProducer(block, source.Id);
		if (source.Kind == CilStackValueKind.AggregateAddress)
		{
			return [source.Id];
		}

		var addressOperation = producer?.Operation switch
		{
			M68kMachineOperation.LocalLoad => M68kMachineOperation.LocalAddress,
			M68kMachineOperation.Argument or M68kMachineOperation.ArgumentLoad =>
				M68kMachineOperation.ArgumentAddress,
			_ => (M68kMachineOperation?)null
		};
		if (addressOperation is null || producer!.ArgumentIndex is null)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Storing multiword struct '{type.DisplayName}' requires a direct local, argument, or stable aggregate expression; producer is {producer?.Operation.ToString() ?? "unknown"}.",
				method.DisplayName,
				instruction.Offset);
		}

		var address = function.CreateValue(
			CilStackValueKind.AggregateAddress,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			addressOperation.Value,
			instruction.Offset,
			definitions: [address.Id],
			argumentIndex: producer.ArgumentIndex));
		return [address.Id];
	}

	private static int[] RewriteMultiwordFieldStore(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		IReadOnlyList<int> uses)
	{
		if (instruction.OpCode != OpCodes.Stfld &&
			instruction.OpCode != OpCodes.Stsfld)
		{
			return uses.ToArray();
		}
		var field = module.ResolveFieldToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (!module.TryGetReferenceFreeStructLayout(
				field.Type,
				field.ModuleName,
				out var layout) ||
			layout.Size <= 4)
		{
			return uses.ToArray();
		}
		var sourceIndex = field.IsStatic ? 0 : 1;
		if (uses.Count <= sourceIndex ||
			function.Values[uses[sourceIndex]].Kind !=
				CilStackValueKind.AggregateAddress)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Storing aggregate field '{field.DisplayName}' requires a stable aggregate expression.",
				method.DisplayName,
				instruction.Offset);
		}
		var rewritten = uses.ToArray();
		if (field.IsStatic)
		{
			rewritten[sourceIndex] = AddRegisterClassCopy(
				function,
				block,
				instruction,
				rewritten[sourceIndex],
				M68kRegisterSet.From(M68kRegister.A0),
				allowCopyCoalescing: false);
		}
		return rewritten;
	}

	private static void AddConstrainedDelegateConstruction(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (uses.Count != 2 || definitions.Count != 1)
		{
			throw new InvalidOperationException(
				$"Delegate construction at IL_{instruction.Offset:X4} has invalid arity.");
		}
		var target = AddFixedRegisterCopy(
			function,
			block,
			instruction,
			uses[0],
			M68kRegister.A2);
		var method = AddFixedRegisterCopy(
			function,
			block,
			instruction,
			uses[1],
			M68kRegister.A3);
		var result = function.Values[definitions[0]];
		var fixedResult = function.CreateValue(
			result.Kind,
			result.Width,
			M68kRegisterSet.From(M68kRegister.D0),
			precoloredRegister: M68kRegister.D0,
			isGcReference: true);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.DelegateCreate,
			instruction.Offset,
			uses: [target, method],
			definitions: [fixedResult.Id],
			clobbers: M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1),
			memoryEffect: M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: instruction));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [fixedResult.Id],
			definitions: definitions));
	}

	private static void AddConstrainedArrayAccess(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		M68kCpuTarget cpu,
		M68kMachineOperation operation,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (instruction.OpCode == OpCodes.Stelem)
		{
			var type = module.ResolveTypeToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (module.TryGetReferenceFreeStructLayout(
					type,
					method.ModuleName,
					out var layout) &&
				layout.Size > 4)
			{
				if (uses.Count != 3 || definitions.Count != 0 ||
					function.Values[uses[2]].Kind !=
						CilStackValueKind.AggregateAddress)
				{
					throw new M68kCompilationException(
						M68kDiagnosticIds.UnsupportedInstruction,
						$"Storing array element '{type.DisplayName}' requires a stable aggregate expression.",
						method.DisplayName,
						instruction.Offset);
				}
				var aggregateUses = uses.ToArray();
				aggregateUses[0] = AddFixedRegisterCopy(
					function, block, instruction, uses[0], M68kRegister.A2);
				aggregateUses[1] = AddFixedRegisterCopy(
					function, block, instruction, uses[1], M68kRegister.D2);
				aggregateUses[2] = AddFixedRegisterCopy(
					function, block, instruction, uses[2], M68kRegister.A3);
				block.Instructions.Add(function.CreateInstruction(
					M68kMachineOperation.AggregateArrayStore,
					instruction.Offset,
					aggregateUses,
					clobbers: M68kRegisterSet.From(
						M68kRegister.D0,
						M68kRegister.D1,
						M68kRegister.A0),
					memoryEffect: M68kMachineMemoryEffect.Write,
					mayThrow: true,
					sourceInstruction: instruction));
				return;
			}
		}
		var isStore = operation == M68kMachineOperation.ArrayStore;
		var isLength = instruction.OpCode == OpCodes.Ldlen;
		if (uses.Count != (isStore ? 3 : isLength ? 1 : 2) ||
			definitions.Count != (isStore ? 0 : 1))
		{
			throw new InvalidOperationException(
				$"Array access at IL_{instruction.Offset:X4} has invalid arity.");
		}
		var constrainedUses = uses.ToArray();
		if (instruction.OpCode == OpCodes.Stelem_Ref)
		{
			constrainedUses[0] = AddFixedRegisterCopy(
				function, block, instruction, uses[0], M68kRegister.A3);
			constrainedUses[1] = AddFixedRegisterCopy(
				function, block, instruction, uses[1], M68kRegister.D3);
			constrainedUses[2] = AddFixedRegisterCopy(
				function, block, instruction, uses[2], M68kRegister.A4);
		}
		else
		{
			constrainedUses[0] = AddRegisterClassCopy(
				function,
				block,
				instruction,
				uses[0],
				M68kRegisterSet.Address);
		}
		if (!isLength)
		{
			var elementSize = ArrayElementSize(instruction.OpCode);
			if (instruction.OpCode != OpCodes.Stelem_Ref)
			{
				constrainedUses[1] = AddRegisterClassCopy(
					function,
					block,
					instruction,
					uses[1],
					M68kRegisterSet.Data,
					allowCopyCoalescing:
						cpu != M68kCpuTarget.M68000 || elementSize == 1);
			}
		}
		var constrainedDefinitions = definitions.ToArray();
		M68kMachineValue? addressResult = null;
		if (operation == M68kMachineOperation.ArrayAddress)
		{
			var result = function.Values[definitions[0]];
			addressResult = function.CreateValue(
				result.Kind,
				result.Width,
				M68kRegisterSet.Address,
				isGcReference: result.IsGcReference,
				spillWeight: result.SpillWeight);
			constrainedDefinitions[0] = addressResult.Id;
		}
		block.Instructions.Add(function.CreateInstruction(
			operation,
			instruction.Offset,
			constrainedUses,
			constrainedDefinitions,
			clobbers: instruction.OpCode == OpCodes.Stelem_Ref
				? M68kRegisterSet.From(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kRegister.D2,
					M68kRegister.A0,
					M68kRegister.A1,
					M68kRegister.A2)
				: M68kRegisterSet.None,
			memoryEffect: isStore
				? M68kMachineMemoryEffect.Write
				: M68kMachineMemoryEffect.Read,
			mayThrow: true,
			sourceInstruction: instruction));
		if (addressResult is not null)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				instruction.Offset,
				uses: [addressResult.Id],
				definitions: definitions));
		}
	}

	private static int AddFixedRegisterCopy(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		int sourceId,
		M68kRegister register)
	{
		var source = function.Values[sourceId];
		var constrained = function.CreateValue(
			source.Kind,
			source.Width,
			M68kRegisterSet.From(register),
			precoloredRegister: register,
			isGcReference: source.IsGcReference,
			spillWeight: source.SpillWeight);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [sourceId],
			definitions: [constrained.Id],
			sourceInstruction: instruction));
		return constrained.Id;
	}

	private static void AddAddressConstrainedOperation(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		M68kCpuTarget cpu,
		M68kMachineOperation operation,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (uses.Count == 0)
		{
			throw new InvalidOperationException(
				$"Address operation at IL_{instruction.Offset:X4} has no base operand.");
		}
		var constrainedUses = uses.ToArray();
		constrainedUses[0] = AddRegisterClassCopy(
			function,
			block,
			instruction,
			uses[0],
			M68kRegisterSet.Address);
		var constrainedDefinitions = definitions.ToArray();
		M68kMachineValue? addressResult = null;
		if (instruction.OpCode == OpCodes.Ldflda)
		{
			var result = function.Values[definitions.Single()];
			addressResult = function.CreateValue(
				result.Kind,
				result.Width,
				M68kRegisterSet.Address,
				isGcReference: result.IsGcReference,
				spillWeight: result.SpillWeight);
			constrainedDefinitions[0] = addressResult.Id;
		}
		block.Instructions.Add(function.CreateInstruction(
			operation,
			instruction.Offset,
			constrainedUses,
			constrainedDefinitions,
			ClobbersFor(method, module, instruction, cpu),
			MemoryEffectFor(operation, instruction.OpCode),
			isSafepoint: IsConservativeSafepoint(instruction.OpCode),
			mayThrow: MayThrow(instruction.OpCode),
			producesConditionCodes: IsComparison(instruction.OpCode),
			sourceInstruction: instruction));
		if (addressResult is not null)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				instruction.Offset,
				uses: [addressResult.Id],
				definitions: definitions));
		}
	}

	private static int AddRegisterClassCopy(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		int sourceId,
		M68kRegisterSet allowedRegisters,
		bool allowCopyCoalescing = true)
	{
		var source = function.Values[sourceId];
		var constrained = function.CreateValue(
			source.Kind,
			source.Width,
			allowedRegisters,
			isGcReference: source.IsGcReference,
			spillWeight: source.SpillWeight);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [sourceId],
			definitions: [constrained.Id],
			sourceInstruction: instruction,
			allowCopyCoalescing: allowCopyCoalescing));
		return constrained.Id;
	}

	private static int ArrayElementSize(OpCode op) =>
		op == OpCodes.Ldelem_I1 || op == OpCodes.Ldelem_U1 ||
		op == OpCodes.Stelem_I1
			? 1
			: op == OpCodes.Ldelem_I2 || op == OpCodes.Ldelem_U2 ||
				op == OpCodes.Stelem_I2
					? 2
					: 4;

	private static void AddFixedDataOperation(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		M68kCpuTarget cpu,
		M68kMachineOperation operation,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (uses.Count != 2 || definitions.Count != 1)
		{
			throw new InvalidOperationException(
				$"Fixed operation at IL_{instruction.Offset:X4} has an invalid arity.");
		}
		var constantShiftCount = 0;
		var hasConstantShiftCount = operation == M68kMachineOperation.Shift &&
			TryGetMachineIntegerConstant(block, uses[1], out constantShiftCount);
		var fixedUses = new int[hasConstantShiftCount ? 1 : 2];
		for (var index = 0; index < fixedUses.Length; index++)
		{
			var source = function.Values[uses[index]];
			var register = index == 0 ? M68kRegister.D0 : M68kRegister.D1;
			var fixedValue = function.CreateValue(
				source.Kind,
				source.Width,
				M68kRegisterSet.From(register),
				precoloredRegister: register,
				isGcReference: source.IsGcReference,
				spillWeight: source.SpillWeight);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				instruction.Offset,
				uses: [source.Id],
				definitions: [fixedValue.Id]));
			fixedUses[index] = fixedValue.Id;
		}
		var result = function.Values[definitions[0]];
		var fixedResult = function.CreateValue(
			result.Kind,
			result.Width,
			M68kRegisterSet.From(M68kRegister.D0),
			precoloredRegister: M68kRegister.D0,
			isGcReference: result.IsGcReference,
			spillWeight: result.SpillWeight);
		var fixedClobbers = ClobbersFor(method, module, instruction, cpu)
			.Add(M68kRegister.D0);
		if (!hasConstantShiftCount)
		{
			fixedClobbers = fixedClobbers.Add(M68kRegister.D1);
		}
		block.Instructions.Add(function.CreateInstruction(
			operation,
			instruction.Offset,
			fixedUses,
			[fixedResult.Id],
			fixedClobbers,
			MemoryEffectFor(instruction.OpCode),
			mayThrow: MayThrow(instruction.OpCode),
			immediate: hasConstantShiftCount
				? constantShiftCount & 31
				: null,
			sourceInstruction: instruction));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [fixedResult.Id],
			definitions: definitions));
	}

	private static bool LowerOptimization(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod method,
		CompilationModule module,
		M68kCpuTarget cpu,
		CilOptimization optimization,
		CilInstruction first,
		CilInstruction last,
		ref ImmutableArray<CilStackValueKind> stackKinds,
		List<int> stackValues)
	{
		switch (optimization.Kind)
		{
			case CilOptimizationKind.Suppress:
				return true;

			case CilOptimizationKind.DiscardCallResult:
			{
				var popSlots = CilStackAnalyzer.GetPopSlotCount(
					method,
					module,
					first,
					stackKinds);
				var uses = CollapseStackOperands(
					stackKinds,
					stackValues,
					popSlots);
				stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
				AddConstrainedCall(
					function,
					block,
					method,
					module,
					first,
					cpu,
					uses,
					Array.Empty<int>());
				stackKinds = ApplyRangeStackEffect(
					method,
					module,
					optimization,
					stackKinds);
				return true;
			}

			case CilOptimizationKind.ComparisonBranch:
			{
				var popSlots = stackKinds.Length != 0 &&
					stackKinds[^1] == CilStackValueKind.Float64 ? 4 : 2;
				var uses = CollapseStackOperands(
					stackKinds,
					stackValues,
					popSlots);
				stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
				var condition = ComparisonCondition(first.OpCode);
				if (!optimization.BranchOnComparisonTrue)
				{
					condition = InvertCondition(condition);
				}
				block.Instructions.Add(function.CreateInstruction(
					M68kMachineOperation.ConditionalBranch,
					last.Offset,
					uses: uses,
					sourceInstruction: new CilInstruction(
						last.Offset,
						OpCodes.Brtrue,
						optimization.BranchTarget,
						last.NextOffset),
					branchCondition: new M68kMachineBranchCondition(
						M68kMachineConditionSourceKind.Compare,
						condition,
						first)));
				stackKinds = ApplyRangeStackEffect(
					method,
					module,
					optimization,
					stackKinds);
				return true;
			}

			case CilOptimizationKind.PredicateBranch:
			{
				var popSlots = CilStackAnalyzer.GetPopSlotCount(
					method,
					module,
					first,
					stackKinds);
				var uses = CollapseStackOperands(
					stackKinds,
					stackValues,
					popSlots);
				stackValues.RemoveRange(stackValues.Count - popSlots, popSlots);
				var instructionStart = block.Instructions.Count;
				AddConstrainedCall(
					function,
					block,
					method,
					module,
					first,
					cpu,
					uses,
					Array.Empty<int>());
				var predicateIndex = block.Instructions.FindLastIndex(
					static instruction =>
						instruction.Operation == M68kMachineOperation.Call);
				if (predicateIndex < instructionStart)
				{
					throw new InvalidOperationException(
						"Predicate branch lowering did not produce a call instruction.");
				}
				var predicateCondition = PredicateCondition(
					method,
					module,
					first);
				if (!optimization.BranchOnComparisonTrue)
				{
					predicateCondition = InvertCondition(predicateCondition);
				}
				block.Instructions[predicateIndex] =
					block.Instructions[predicateIndex] with
					{
						Operation = M68kMachineOperation.ConditionalBranch,
						IlOffset = last.Offset,
						MemoryEffect = M68kMachineMemoryEffect.Read,
						IsSafepoint = false,
						MayThrow = false,
						ProducesConditionCodes = false,
						SourceInstruction = new CilInstruction(
							last.Offset,
							OpCodes.Brtrue,
							optimization.BranchTarget,
							last.NextOffset),
						BranchCondition = new M68kMachineBranchCondition(
							M68kMachineConditionSourceKind.Predicate,
							predicateCondition,
							first)
					};
				stackKinds = ApplyRangeStackEffect(
					method,
					module,
					optimization,
					stackKinds);
				return true;
			}

			default:
				return false;
		}
	}

	private static ImmutableArray<CilStackValueKind> ApplyRangeStackEffect(
		CilMethod method,
		CompilationModule module,
		CilOptimization optimization,
		ImmutableArray<CilStackValueKind> stack)
	{
		for (var index = optimization.StartIndex;
			index <= optimization.EndIndex;
			index++)
		{
			stack = CilStackAnalyzer.ApplyStackEffect(
				method,
				module,
				method.Instructions[index],
				stack);
		}
		return stack;
	}

	private static void AddConstrainedObjectConstruction(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod caller,
		CompilationModule module,
		CilInstruction instruction,
		M68kCpuTarget cpu,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (definitions.Count != 1)
		{
			throw new InvalidOperationException(
				"Managed object construction must define one reference.");
		}
		var result = function.Values[definitions[0]];
		var allocatedResult = function.CreateValue(
			result.Kind,
			result.Width,
			M68kRegisterSet.From(M68kRegister.A0),
			precoloredRegister: M68kRegister.A0,
			isGcReference: true,
			spillWeight: result.SpillWeight);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ObjectAllocate,
			instruction.Offset,
			definitions: [allocatedResult.Id],
			clobbers: M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1),
			memoryEffect:
				M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: instruction));
		var stableResult = function.CreateValue(
			result.Kind,
			result.Width,
			result.AllowedRegisters,
			isGcReference: true,
			spillWeight: result.SpillWeight);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [allocatedResult.Id],
			definitions: [stableResult.Id]));

		var constructorCall = new CilInstruction(
			instruction.Offset,
			OpCodes.Call,
			instruction.Operand,
			instruction.NextOffset);
		AddConstrainedCall(
			function,
			block,
			caller,
			module,
			constructorCall,
			cpu,
			[stableResult.Id, .. uses],
			[],
			hasInstanceArgumentOverride: true);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			instruction.Offset,
			uses: [stableResult.Id],
			definitions: definitions));
	}

	private static void AddConstrainedCall(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod caller,
		CompilationModule module,
		CilInstruction instruction,
		M68kCpuTarget cpu,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions,
		bool? hasInstanceArgumentOverride = null,
		M68kRegister? stackVarargsRegister = null)
	{
		var target = module.ResolveMethodToken(
			(int)instruction.Operand!,
			caller,
			instruction.Offset);
		var callInstruction = instruction;
		CilMethod? constrainedImplementation = null;
		var dereferencesConstrainedReference = false;
		if (instruction.ConstrainedTypeToken is { } constrainedTypeToken &&
			target.Definition is { } constrainedDeclaration)
		{
			var constrainedType = module.ResolveTypeToken(
				constrainedTypeToken,
				caller,
				instruction.Offset);
			if (constrainedType.Kind == CilTypeKind.ManagedReference)
			{
				// constrained. supplies &T. For a closed reference T, load the
				// object reference and retain the original callvirt so the normal
				// interface/vtable path preserves dynamic dispatch and null checks.
				dereferencesConstrainedReference = true;
				callInstruction = instruction with { ConstrainedTypeToken = null };
			}
			else
			{
				constrainedImplementation =
					module.ResolveConstrainedInterfaceImplementation(
						caller,
						constrainedTypeToken,
						instruction.Offset,
						constrainedDeclaration);
				target = MethodReference.ForDefinition(constrainedImplementation);
			}
		}
		if (IsLiteralAddressIntrinsic(target.ImportName))
		{
			AddLiteralAddressIntrinsic(
				function,
				block,
				instruction,
				uses,
				definitions);
			return;
		}
		var transportsManagedByrefOwner = IsSpanByrefConstructor(
			target.ImportName);
		var constructsSpanValue = IsSpanValueConstructor(target.ImportName);
		var effectiveReturnType = constructsSpanValue
			? target.ConstructedDeclaringType ??
				throw new InvalidOperationException(
					"Span byref constructor has no constructed declaring type.")
			: target.Signature.ReturnType;
		CilTypeLayout multiwordReturnLayout = null!;
		var hasMultiwordReturn = !module.IsTransparentScalarType(
			effectiveReturnType) &&
			module.TryGetReferenceFreeStructLayout(
				effectiveReturnType,
				target.Definition?.ModuleName ?? caller.ModuleName,
				out multiwordReturnLayout) &&
			multiwordReturnLayout.Size > 4;
		var isSpanAggregateReturn =
			constructsSpanValue ||
			target.ImportName?.StartsWith(
				"intrinsic:span-from-array:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-from-",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-slice-",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-slice-",
				StringComparison.Ordinal) == true;
		if (hasMultiwordReturn &&
			(target.Definition is not { } returnDefinition ||
			 returnDefinition.IsImport ||
			 returnDefinition.ExternalCall is not null ||
			 returnDefinition.DeclaringTypeIsInterface) &&
			!isSpanAggregateReturn)
		{
			throw new M68kCompilationException(
				M68kDiagnosticIds.UnsupportedInstruction,
				$"Multiword return '{target.Signature.ReturnType.DisplayName}' requires a direct managed target; imported and interface return adapters are not implemented yet.",
				caller.DisplayName,
				instruction.Offset);
		}
		var sourceUses = uses.ToList();
		if (dereferencesConstrainedReference)
		{
			if (sourceUses.Count == 0)
			{
				throw new InvalidOperationException(
					$"Constrained call at IL_{instruction.Offset:X4} has no receiver.");
			}
			var receiver = function.CreateValue(
				CilStackValueKind.Reference,
				M68kMachineValueWidth.Long,
				M68kRegisterSet.Address,
				isGcReference: true);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Load,
				instruction.Offset,
				uses: [sourceUses[0]],
				definitions: [receiver.Id],
				memoryEffect: M68kMachineMemoryEffect.Read,
				mayThrow: true,
				sourceInstruction: new CilInstruction(
					instruction.Offset,
					OpCodes.Ldind_Ref,
					null,
					instruction.Offset)));
			sourceUses[0] = receiver.Id;
		}
		var multiwordArgumentBytes = RewriteMultiwordCallArguments(
			function,
			block,
			caller,
			module,
			instruction,
			target,
			sourceUses,
			hasInstanceArgumentOverride ??
				(target.Signature.Header.IsInstance &&
					(instruction.OpCode != OpCodes.Newobj ||
					 target.ImportName?.StartsWith(
						"intrinsic:nullable-ctor:",
						StringComparison.Ordinal) == true)));
		var argumentConstraints = (stackVarargsRegister is not null
			? GetStackVarargsArgumentRegisters(target, uses.Count)
			: GetCallArgumentRegisters(
				function,
				target,
				sourceUses,
				hasInstanceArgumentOverride ??
					(target.Signature.Header.IsInstance &&
						(instruction.OpCode != OpCodes.Newobj ||
						 target.ImportName?.StartsWith(
						"intrinsic:nullable-ctor:",
						StringComparison.Ordinal) == true)),
				multiwordArgumentBytes.Keys.ToHashSet()))
			.ToList();
		if (constrainedImplementation is not null &&
			argumentConstraints.Count != 0 &&
			module.IsTransparentScalarType(new CilType(
				CilTypeKind.ValueType,
				4,
				constrainedImplementation.DisplayName.Split(
					"::",
					2,
					StringSplitOptions.None)[0])))
		{
			// Transparent one-word value-type instance methods use the existing
			// D0 receiver ABI, but constrained. supplies a managed pointer. Keep
			// the pointer unchanged and move it to D0; the callee materializes A0
			// before field access. Non-transparent structs retain the A0 ABI.
			argumentConstraints[0] = CallArgumentConstraint.Fixed(M68kRegister.D0);
		}
		int? embeddedDisplacement = null;
		if (target.ImportName is
				"intrinsic:aptr-read-uint32" or
				"intrinsic:aptr-write-uint32" &&
			sourceUses.Count > 1 &&
			TryGetMachineIntegerConstant(
				block,
				sourceUses[1],
				out var constantDisplacement) &&
			constantDisplacement is >= short.MinValue and <= short.MaxValue)
		{
			embeddedDisplacement = constantDisplacement;
			sourceUses.RemoveAt(1);
			argumentConstraints.RemoveAt(1);
		}
		if (embeddedDisplacement is null &&
			target.ImportName is
				"intrinsic:aptr-read-uint32" or
				"intrinsic:aptr-write-uint32" &&
			argumentConstraints.Count != 0)
		{
			argumentConstraints[0] = argumentConstraints[0] with
			{
				AllowCopyCoalescing = false
			};
		}
		if (embeddedDisplacement is not null &&
			target.ImportName == "intrinsic:aptr-write-uint32" &&
			argumentConstraints.Count != 0)
		{
			// A constant displacement leaves the value as a direct memory-store
			// operand; it does not need the legacy D1 ABI scratch register.
			argumentConstraints[^1] = CallArgumentConstraint.RegisterClass(
				M68kRegisterSet.Data);
		}
		var constrainedUses = sourceUses.ToArray();
		int? aggregateReturnHome = null;
		var stackArgumentBytes = 0;
		if (hasMultiwordReturn)
		{
			aggregateReturnHome = AllocateAggregateTemporaryHome(
				function,
				caller,
				multiwordReturnLayout.Size,
				GcReferenceOffsets(
					module,
					effectiveReturnType,
					target.Definition?.ModuleName ?? caller.ModuleName));
			var returnAddress = function.CreateValue(
				CilStackValueKind.AggregateAddress,
				M68kMachineValueWidth.Long,
				M68kRegisterSet.Address);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.LocalAddress,
				instruction.Offset,
				definitions: [returnAddress.Id],
				argumentIndex: aggregateReturnHome));
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.OutgoingArgumentPush,
				instruction.Offset,
				uses: [returnAddress.Id],
				memoryEffect: M68kMachineMemoryEffect.Write,
				argumentIndex: 4));
			stackArgumentBytes = 4;
		}
		var duplicatesSecondDataArgumentForInterface =
			target.Definition is { DeclaringTypeIsInterface: true } &&
			target.Signature.Header.IsInstance &&
			target.Signature.ParameterTypes.Length == 2 &&
			target.Signature.ParameterTypes.All(parameter =>
				parameter.IsSupportedScalar &&
				parameter.Size <= 4 &&
				!IsAddressType(parameter)) &&
			constrainedUses.Length >= 3;
		if (duplicatesSecondDataArgumentForInterface)
		{
			// Preserve the normal interface ABI in D0/D1 for class targets,
			// while making the second value available at 4(SP) to a boxed
			// transparent-scalar thunk whose payload receiver consumes D0.
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.OutgoingArgumentPush,
				instruction.Offset,
				uses: [constrainedUses[2]],
				memoryEffect: M68kMachineMemoryEffect.Write,
				argumentIndex: 4));
			stackArgumentBytes = 4;
		}
		var duplicatesLongPairArgumentForInterface =
			target.Definition is { DeclaringTypeIsInterface: true } &&
			target.Signature.Header.IsInstance &&
			target.Signature.ParameterTypes is [var longPairParameter] &&
			longPairParameter.IsSupportedScalar &&
			longPairParameter.Size == 8 &&
			constrainedUses.Length >= 2;
		if (duplicatesLongPairArgumentForInterface)
		{
			// Class implementations consume the pair in D0:D1. A boxed
			// transparent-scalar receiver needs D0 for its payload address,
			// so its shared value-type body consumes the duplicate stack pair.
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.OutgoingArgumentPush,
				instruction.Offset,
				uses: [constrainedUses[1]],
				memoryEffect: M68kMachineMemoryEffect.Write,
				argumentIndex: 8));
			stackArgumentBytes = checked(stackArgumentBytes + 8);
		}
		for (var index = Math.Min(constrainedUses.Length, argumentConstraints.Count) - 1;
			index >= 0;
			index--)
		{
			if (!argumentConstraints[index].IsStack)
			{
				continue;
			}
			var source = function.Values[constrainedUses[index]];
			var bytes = multiwordArgumentBytes.TryGetValue(index, out var aggregateBytes)
				? aggregateBytes
				: source.Width == M68kMachineValueWidth.LongPair ? 8 : 4;
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.OutgoingArgumentPush,
				instruction.Offset,
				uses: [source.Id],
				memoryEffect: M68kMachineMemoryEffect.Write,
				argumentIndex: bytes));
			stackArgumentBytes = checked(stackArgumentBytes + bytes);
		}
		for (var index = 0;
			index < constrainedUses.Length && index < argumentConstraints.Count;
			index++)
		{
			var constraint = argumentConstraints[index];
			if (constraint.IsStack)
			{
				continue;
			}
			var source = function.Values[constrainedUses[index]];
			var constrainedValue = function.CreateValue(
				source.Kind,
				source.Width,
				constraint.Registers,
				precoloredRegister: constraint.FixedRegister,
				isGcReference: source.IsGcReference,
				spillWeight: source.SpillWeight);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				instruction.Offset,
				uses: [source.Id],
				definitions: [constrainedValue.Id],
				sourceInstruction: instruction,
				allowCopyCoalescing: constraint.AllowCopyCoalescing));
			constrainedUses[index] = constrainedValue.Id;
		}

		var constrainedDefinitions = hasMultiwordReturn
			? Array.Empty<int>()
			: definitions.ToArray();
		M68kMachineValue? fixedReturn = null;
		if (!hasMultiwordReturn &&
			definitions.Count == 1 &&
			target.ImportName != "intrinsic:aptr-read-uint32")
		{
			var result = function.Values[definitions[0]];
			var returnRegister = GetCallReturnRegister(target, result);
			fixedReturn = function.CreateValue(
				result.Kind,
				result.Width,
				M68kRegisterSet.From(returnRegister),
				precoloredRegister: returnRegister,
				isGcReference: result.IsGcReference,
				spillWeight: result.SpillWeight);
			constrainedDefinitions[0] = fixedReturn.Id;
		}
		var registerUses = constrainedUses
			.Where((_, index) =>
				index >= argumentConstraints.Count ||
				!argumentConstraints[index].IsStack)
			.ToArray();
		var isNonThrowingAddressIntrinsic =
			transportsManagedByrefOwner ||
			target.ImportName?.StartsWith(
				"intrinsic:span-from-array:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-from-",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-length:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-length:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-is-empty:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-is-empty:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-sequence-equal:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:runtimehelpers-is-reference-or-contains-references:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-get-value-or-default-no-argument:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-get-value-or-default:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-has-value:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-ctor:",
				StringComparison.Ordinal) == true ||
			target.ImportName is
			"intrinsic:address-of-ref" or
			"intrinsic:address-to-ref" or
			"intrinsic:ref-cast" or
			"intrinsic:hook-address-of" or
			"intrinsic:boopsi-message-address-of";
		var isNonGcIntrinsic = isNonThrowingAddressIntrinsic ||
			target.ImportName?.StartsWith(
				"intrinsic:span-copy-to:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-copy-to:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-get-item:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-get-item:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-slice-",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-slice-",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-get-value:",
				StringComparison.Ordinal) == true;
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			instruction.Offset,
			registerUses,
			constrainedDefinitions,
			ClobbersFor(caller, module, callInstruction, cpu),
			M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			isSafepoint: !isNonGcIntrinsic,
			mayThrow: !isNonThrowingAddressIntrinsic,
			sourceInstruction: callInstruction,
			stackVarargsRegister: stackVarargsRegister,
			immediate: embeddedDisplacement,
			transportsManagedByrefOwner: transportsManagedByrefOwner));
		if (stackArgumentBytes != 0)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.OutgoingArgumentCleanup,
				instruction.Offset,
				argumentIndex: stackArgumentBytes));
		}
		if (fixedReturn is not null)
		{
			var returnedValue = fixedReturn.Id;
			if (target.Definition is { } borrowedReturnDefinition &&
				CilManagedByrefSummary.TryGetBorrowedParameterReturn(
					borrowedReturnDefinition,
					out var returnedArgumentIndex) &&
				returnedArgumentIndex < sourceUses.Count)
			{
				// The exact ldarg;ret summary proves the native result aliases the
				// original argument. Reusing that SSA value retains its owner/frame
				// provenance without widening the internal return ABI.
				returnedValue = sourceUses[returnedArgumentIndex];
			}
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				instruction.Offset,
				uses: [returnedValue],
				definitions: definitions));
		}
		else if (aggregateReturnHome is { } returnHome && definitions.Count != 0)
		{
			if (definitions.Count != 1)
			{
				throw new InvalidOperationException(
					$"Multiword call at IL_{instruction.Offset:X4} has {definitions.Count} results.");
			}
			var result = function.Values[definitions[0]];
			function.Values[definitions[0]] = result with
			{
				Kind = CilStackValueKind.AggregateAddress,
				Width = M68kMachineValueWidth.Long,
				AllowedRegisters = M68kRegisterSet.Address,
				PrecoloredRegister = null,
				IsGcReference = false,
				IsRematerializable = false
			};
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.LocalAddress,
				instruction.Offset,
				definitions: definitions,
				argumentIndex: returnHome));
		}
	}

	private static void AddLiteralAddressIntrinsic(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		if (uses.Count != 1 || definitions.Count > 1)
		{
			throw new InvalidOperationException(
				$"Literal intrinsic at IL_{instruction.Offset:X4} has invalid arity.");
		}
		if (definitions.Count == 0)
		{
			return;
		}

		// The managed string operand only identifies a compile-time literal. The
		// intrinsic emitter materializes the corresponding native address and
		// never calls managed code, so keeping the operand would create a false
		// GC root and safepoint around a non-call.
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			instruction.Offset,
			definitions: definitions,
			sourceInstruction: instruction));
		}

	private static IReadOnlyDictionary<int, int> RewriteMultiwordCallArguments(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilMethod caller,
		CompilationModule module,
		CilInstruction instruction,
		MethodReference target,
		List<int> uses,
		bool hasInstanceArgument)
	{
		var result = new Dictionary<int, int>();
		var admitsImportedSpanValue =
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-from-span:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-sequence-equal:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-copy-to:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-copy-to:",
				StringComparison.Ordinal) == true;
		var definition = target.Definition;
		if (!admitsImportedSpanValue &&
			(definition is null || definition.IsImport) ||
			definition?.ExternalCall is not null)
		{
			return result;
		}

		var firstParameterUse = hasInstanceArgument ? 1 : 0;
		var parameterTypes = definition?.Signature.ParameterTypes ??
			target.Signature.ParameterTypes;
		var parameterModuleName = definition?.ModuleName ?? caller.ModuleName;
		for (var parameterIndex = 0;
			parameterIndex < parameterTypes.Length;
			parameterIndex++)
		{
			var parameter = parameterTypes[parameterIndex];
			if (!module.TryGetReferenceFreeStructLayout(
					parameter,
					parameterModuleName,
					out var layout) ||
				layout.Size <= 4)
			{
				continue;
			}

			var useIndex = firstParameterUse + parameterIndex;
			if (useIndex >= uses.Count)
			{
				throw new InvalidOperationException(
					$"Call at IL_{instruction.Offset:X4} has no value for multiword parameter {parameterIndex}.");
			}
			var producer = FindFrameValueProducer(block, uses[useIndex]);
			var addressOperation = producer?.Operation switch
			{
				M68kMachineOperation.LocalLoad => M68kMachineOperation.LocalAddress,
				M68kMachineOperation.Argument or M68kMachineOperation.ArgumentLoad =>
					M68kMachineOperation.ArgumentAddress,
				_ => (M68kMachineOperation?)null
			};
			if (function.Values[uses[useIndex]].Kind ==
					CilStackValueKind.AggregateAddress &&
				(!admitsImportedSpanValue || addressOperation is null))
			{
				result.Add(useIndex, layout.Size);
				continue;
			}
			if (addressOperation is null || producer!.ArgumentIndex is null)
			{
				throw new M68kCompilationException(
					M68kDiagnosticIds.UnsupportedInstruction,
					$"Passing multiword struct '{parameter.DisplayName}' requires a direct local or argument value; other expression values require the pending multiword evaluation-stack representation.",
					caller.DisplayName,
					instruction.Offset);
			}

			var address = function.CreateValue(
				CilStackValueKind.ManagedPointer,
				M68kMachineValueWidth.Long,
				M68kRegisterSet.Address);
			block.Instructions.Add(function.CreateInstruction(
				addressOperation.Value,
				instruction.Offset,
				definitions: [address.Id],
				argumentIndex: producer.ArgumentIndex));
			uses[useIndex] = address.Id;
			result.Add(useIndex, layout.Size);
		}
		return result;
	}

	private static bool IsLiteralAddressIntrinsic(string? name) =>
		name is
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal" or
			"intrinsic:aptr-export-address";

	private static bool IsSpanByrefConstructor(string? name) =>
		name?.StartsWith(
			"intrinsic:span-from-ref:",
			StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:readonly-span-from-ref:",
			StringComparison.Ordinal) == true;

	private static bool IsSpanValueConstructor(string? name) =>
		IsSpanByrefConstructor(name) ||
		name?.StartsWith(
			"intrinsic:span-from-pointer:",
			StringComparison.Ordinal) == true;

	private readonly record struct CallArgumentConstraint(
		bool IsStack,
		M68kRegisterSet Registers,
		M68kRegister? FixedRegister,
		bool AllowCopyCoalescing = true)
	{
		public static CallArgumentConstraint Stack =>
			new(true, M68kRegisterSet.None, null);

		public static CallArgumentConstraint Fixed(M68kRegister register) =>
			new(false, M68kRegisterSet.From(register), register);

		public static CallArgumentConstraint RegisterClass(
			M68kRegisterSet registers,
			bool allowCopyCoalescing = true) =>
			new(false, registers, null, allowCopyCoalescing);
	}

	private static IReadOnlyList<CallArgumentConstraint>
		GetStackVarargsArgumentRegisters(
			MethodReference target,
			int useCount)
	{
		if (target.ImportName ==
			"intrinsic:boopsi-do-method-stack-varargs")
		{
			return Enumerable.Range(0, useCount)
				.Select(static index =>
					index == 0
						? CallArgumentConstraint.Fixed(M68kRegister.A0)
						: CallArgumentConstraint.Stack)
				.ToArray();
		}
		var fixedParameterCount =
			target.Signature.ParameterTypes.Length - 1;
		var fixedRegisters =
			target.Definition!.ExternalCall!.Abi.ParameterRegisters;
		return Enumerable.Range(0, useCount)
			.Select(index =>
				index < fixedParameterCount
					? CallArgumentConstraint.Fixed(fixedRegisters[index])
					: CallArgumentConstraint.Stack)
			.ToArray();
	}

	private static IReadOnlyList<CallArgumentConstraint> GetCallArgumentRegisters(
		M68kMachineFunction function,
		MethodReference target,
		IReadOnlyList<int> uses,
		bool hasInstanceArgument,
		IReadOnlySet<int> forcedStackUses)
	{
		if (target.ImportName == "intrinsic:runtime-allocate-string")
		{
			// D2 survives the allocator and its optional collection path, allowing
			// the emitter to reuse the exact requested length after allocation.
			return [CallArgumentConstraint.Fixed(M68kRegister.D2)];
		}
		if (target.ImportName == "intrinsic:runtime-set-string-char")
		{
			return
			[
				CallArgumentConstraint.Fixed(M68kRegister.A2),
				CallArgumentConstraint.Fixed(M68kRegister.D3),
				CallArgumentConstraint.Fixed(M68kRegister.D4)
			];
		}
		if (target.ImportName == "intrinsic:string-concat-two")
		{
			// Both references remain explicit roots in preserved registers across
			// the result allocation and its optional collection path.
			return
			[
				CallArgumentConstraint.Fixed(M68kRegister.A2),
				CallArgumentConstraint.Fixed(M68kRegister.A3)
			];
		}
		if (target.ImportName == "intrinsic:string-substring")
		{
			// The source remains a traced root while the result is allocated.
			// Indices use preserved data registers so allocation and collection
			// cannot destroy the requested source range.
			return uses.Count == 3
				? [
					CallArgumentConstraint.Fixed(M68kRegister.A2),
					CallArgumentConstraint.Fixed(M68kRegister.D3),
					CallArgumentConstraint.Fixed(M68kRegister.D4)
				]
				: [
					CallArgumentConstraint.Fixed(M68kRegister.A2),
					CallArgumentConstraint.Fixed(M68kRegister.D3)
				];
		}
		if (target.ImportName == "intrinsic:string-copy-to-char-array")
		{
			return
			[
				CallArgumentConstraint.Fixed(M68kRegister.A2),
				CallArgumentConstraint.Fixed(M68kRegister.D3),
				CallArgumentConstraint.Fixed(M68kRegister.A3),
				CallArgumentConstraint.Fixed(M68kRegister.D4),
				CallArgumentConstraint.Fixed(M68kRegister.D5)
			];
		}
		if (target.ImportName == "intrinsic:string-copy-to-span-char")
		{
			return
			[
				CallArgumentConstraint.Fixed(M68kRegister.A2),
				CallArgumentConstraint.Fixed(M68kRegister.A3)
			];
		}
		if (target.ImportName == "intrinsic:string-to-char-array")
		{
			return uses.Count == 3
				? [
					CallArgumentConstraint.Fixed(M68kRegister.A2),
					CallArgumentConstraint.Fixed(M68kRegister.D3),
					CallArgumentConstraint.Fixed(M68kRegister.D4)
				]
				: [CallArgumentConstraint.Fixed(M68kRegister.A2)];
		}
		if (target.ImportName is
			"intrinsic:string-starts-with-ordinal" or
			"intrinsic:string-ends-with-ordinal" or
			"intrinsic:string-contains-ordinal" or
			"intrinsic:string-index-of-ordinal")
		{
			return uses.Count == 3
				? [
					CallArgumentConstraint.Fixed(M68kRegister.A2),
					CallArgumentConstraint.Fixed(M68kRegister.A3),
					CallArgumentConstraint.Fixed(M68kRegister.D3)
				]
				: [
					CallArgumentConstraint.Fixed(M68kRegister.A2),
					CallArgumentConstraint.Fixed(M68kRegister.A3)
				];
		}
		if (target.ImportName?.StartsWith(
				"intrinsic:span-from-pointer:",
				StringComparison.Ordinal) == true)
		{
			return
			[
				CallArgumentConstraint.Fixed(M68kRegister.A0),
				CallArgumentConstraint.Fixed(M68kRegister.D0)
			];
		}
		if (target.ImportName?.StartsWith(
				"intrinsic:span-copy-to:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-copy-to:",
				StringComparison.Ordinal) == true)
		{
			return
			[
				CallArgumentConstraint.Fixed(M68kRegister.A0),
				CallArgumentConstraint.Fixed(M68kRegister.A1)
			];
		}
		if (IsSpanByrefConstructor(target.ImportName) ||
			target.ImportName?.StartsWith(
				"intrinsic:span-from-array:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-from-",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-length:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-length:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:span-is-empty:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-is-empty:",
				StringComparison.Ordinal) == true)
		{
			return [CallArgumentConstraint.Fixed(M68kRegister.A0)];
		}
		if (target.ImportName?.StartsWith(
			"intrinsic:span-slice-start:",
			StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-slice-start:",
			StringComparison.Ordinal) == true)
		{
			return [
				CallArgumentConstraint.Fixed(M68kRegister.A0),
				CallArgumentConstraint.Fixed(M68kRegister.D0)];
		}
		if (target.ImportName?.StartsWith(
			"intrinsic:span-slice-range:",
			StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-slice-range:",
			StringComparison.Ordinal) == true)
		{
			return [
				CallArgumentConstraint.Fixed(M68kRegister.A0),
				CallArgumentConstraint.Fixed(M68kRegister.D0),
				CallArgumentConstraint.Fixed(M68kRegister.D1)];
		}
		if (target.ImportName?.StartsWith(
			"intrinsic:span-get-item:",
			StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:readonly-span-get-item:",
			StringComparison.Ordinal) == true)
		{
			return [
				CallArgumentConstraint.Fixed(M68kRegister.A0),
				CallArgumentConstraint.Fixed(M68kRegister.D0)];
		}
		if (target.ImportName?.StartsWith(
				"intrinsic:nullable-ctor:",
				StringComparison.Ordinal) == true ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-get-value-or-default:",
				StringComparison.Ordinal) == true)
		{
			return [
				CallArgumentConstraint.RegisterClass(M68kRegisterSet.Address),
				CallArgumentConstraint.Fixed(M68kRegister.D0)];
		}
		if (target.ImportName == "intrinsic:aptr-read-uint32" ||
			target.ImportName?.StartsWith(
				"intrinsic:file-info-block-read-int32:",
				StringComparison.Ordinal) == true)
		{
			if (uses.Count == 1)
			{
				return [CallArgumentConstraint.RegisterClass(M68kRegisterSet.Address)];
			}
			return [
				CallArgumentConstraint.RegisterClass(M68kRegisterSet.Address),
				CallArgumentConstraint.Fixed(M68kRegister.D0)];
		}
		if (target.ImportName is
			"intrinsic:delegate-combine" or
			"intrinsic:delegate-remove")
		{
			return [
				CallArgumentConstraint.Fixed(M68kRegister.A2),
				CallArgumentConstraint.Fixed(M68kRegister.A3)];
		}
		if (target.ImportName == "intrinsic:aptr-write-uint32")
		{
			return [
				CallArgumentConstraint.RegisterClass(M68kRegisterSet.Address),
				CallArgumentConstraint.Fixed(M68kRegister.D0),
				CallArgumentConstraint.Fixed(M68kRegister.D1)];
		}
		if (IsAddressBaseIntrinsic(target.ImportName))
		{
			var destructiveBase = target.ImportName is
				"intrinsic:iff-handle-stream" or
				"intrinsic:iff-handle-set-stream";
			return Enumerable.Range(0, uses.Count)
				.Select(index => index == 0
					? CallArgumentConstraint.RegisterClass(
						M68kRegisterSet.Address,
						allowCopyCoalescing: !destructiveBase)
					: CallArgumentConstraint.Fixed(M68kRegister.D0))
				.ToArray();
		}
		if (target.ImportName == "intrinsic:boopsi-do-method")
		{
			return Enumerable.Range(0, uses.Count)
				.Select(static index =>
					index == 0
						? CallArgumentConstraint.Fixed(M68kRegister.A0)
						: CallArgumentConstraint.Stack)
				.ToArray();
		}
		if (target.Definition?.ExternalCall is { } externalCall)
		{
			return externalCall.Abi.ParameterRegisters
				.Select(CallArgumentConstraint.Fixed)
				.ToArray();
		}
		if (target.Definition?.ImportAbi is { } importAbi)
		{
			return importAbi.ParameterRegisters
				.Select(CallArgumentConstraint.Fixed)
				.ToArray();
		}
		if (hasInstanceArgument &&
			target.Definition is { ModuleName: "CopperSharp.Runtime.Managed" } shadow &&
			(shadow.DisplayName.StartsWith(
				"CopperSharp.Runtime.ShadowInt32::ToString",
				StringComparison.Ordinal) ||
			 shadow.DisplayName.StartsWith(
				"CopperSharp.Runtime.ShadowUInt32::ToString",
				StringComparison.Ordinal)))
		{
			// Value-type instance shadow bodies receive the managed receiver
			// address in D0, matching their normal internal method ABI. The
			// public call-site value remains an Int32/UInt32 managed pointer.
			return uses.Count == 1
				? [CallArgumentConstraint.Fixed(M68kRegister.D0)]
				: [
					CallArgumentConstraint.Fixed(M68kRegister.D0),
					CallArgumentConstraint.Fixed(M68kRegister.A0)
				];
		}

		var result = new List<CallArgumentConstraint>();
		var nextData = 0;
		var nextAddress = 0;
		var useIndex = 0;
		if (hasInstanceArgument)
		{
			var instanceIsAddress = useIndex < uses.Count &&
				function.Values[uses[useIndex]].Kind is
					CilStackValueKind.Reference or
					CilStackValueKind.ManagedPointer;
			result.Add(CallArgumentConstraint.Fixed(
				instanceIsAddress ? M68kRegister.A0 : M68kRegister.D0));
			if (instanceIsAddress)
			{
				nextAddress = 1;
			}
			else
			{
				nextData = 1;
			}
			useIndex++;
		}
		for (var parameterIndex = 0;
			parameterIndex < target.Signature.ParameterTypes.Length;
			parameterIndex++)
		{
			var parameter = target.Definition is { } definition &&
				parameterIndex < definition.Signature.ParameterTypes.Length
					? definition.Signature.ParameterTypes[parameterIndex]
					: target.Signature.ParameterTypes[parameterIndex];
			var parameterUseIndex = useIndex + parameterIndex;
			if (forcedStackUses.Contains(parameterUseIndex))
			{
				result.Add(CallArgumentConstraint.Stack);
			}
			else if (parameter.Kind != CilTypeKind.GenericParameter &&
				parameter.IsSupportedScalar &&
				parameter.Size == 8 &&
				nextData == 0)
			{
				result.Add(CallArgumentConstraint.Fixed(M68kRegister.D0));
				nextData = 2;
			}
			else if (parameter.Kind != CilTypeKind.GenericParameter &&
				IsAddressType(parameter) &&
				nextAddress < 2)
			{
				result.Add(CallArgumentConstraint.Fixed(
					(M68kRegister)((int)M68kRegister.A0 + nextAddress++)));
			}
			else if (parameter.Kind != CilTypeKind.GenericParameter &&
				parameter.Size != 8 &&
				nextData < 2)
			{
				result.Add(CallArgumentConstraint.Fixed(
					(M68kRegister)((int)M68kRegister.D0 + nextData++)));
			}
			else
			{
				result.Add(CallArgumentConstraint.Stack);
			}
		}
		return result;
	}

	private static M68kRegister GetCallReturnRegister(
		MethodReference target,
		M68kMachineValue result)
	{
		if (target.Definition?.ExternalCall is { } externalCall)
		{
			return externalCall.Abi.ReturnRegister;
		}
		if (target.Definition?.ImportAbi is { } importAbi)
		{
			return importAbi.ReturnRegister;
		}
		if (target.Definition?.Signature.ReturnType.Kind ==
			CilTypeKind.GenericParameter)
		{
			return M68kRegister.D0;
		}
		return result.Kind is
			CilStackValueKind.Reference or
			CilStackValueKind.ManagedPointer
				? M68kRegister.A0
				: M68kRegister.D0;
	}

	private static bool IsAddressType(CilType type) =>
		type.Kind is
			CilTypeKind.ManagedReference or
			CilTypeKind.ManagedPointer or
			CilTypeKind.UnmanagedPointer or
			CilTypeKind.FunctionPointer;

	private static bool IsAddressBaseIntrinsic(string? name) =>
		name?.StartsWith("intrinsic:nullable-has-value:", StringComparison.Ordinal) == true ||
		name?.StartsWith("intrinsic:nullable-get-value:", StringComparison.Ordinal) == true ||
		name?.StartsWith(
			"intrinsic:nullable-get-value-or-default-no-argument:",
			StringComparison.Ordinal) == true ||
		name is
			"intrinsic:aptr-raw" or
			"intrinsic:aptr-is-null" or
			"intrinsic:aptr-is-not-null" or
			"intrinsic:iff-handle-stream" or
			"intrinsic:iff-handle-set-stream" or
			"intrinsic:string-char" or
			"intrinsic:string-length";

	private static void PopulatePhiInputs(IReadOnlyList<BlockBuildState> states)
	{
		var statesById = states.ToDictionary(static state => state.Block.Id);
		foreach (var state in states)
		{
			if (state.Block.Phis.Count == 0)
			{
				continue;
			}
			foreach (var predecessorId in state.Block.Predecessors)
			{
				var predecessor = statesById[predecessorId];
				if (predecessor.ExitValues.Count != state.EntryValues.Count)
				{
					throw new InvalidOperationException(
						$"Machine IR edge {predecessorId}->{state.Block.Id} has incompatible stack arity.");
				}
				for (var slot = 0; slot < state.EntryValues.Count; slot++)
				{
					if (slot != 0 &&
						state.EntryValues[slot] == state.EntryValues[slot - 1])
					{
						continue;
					}
					var phi = state.Block.Phis.Single(item =>
						item.Definition == state.EntryValues[slot]);
					((Dictionary<int, int>)phi.Inputs).Add(
						predecessorId,
						predecessor.ExitValues[slot]);
				}
				foreach (var (localIndex, phi) in state.LocalPhis)
				{
					if (predecessor.ExitLocals[localIndex] is not { } input)
					{
						throw new InvalidOperationException(
							$"Machine IR edge {predecessorId}->{state.Block.Id} " +
							$"has no value for local {localIndex}.");
					}
					((Dictionary<int, int>)phi.Inputs).Add(predecessorId, input);
				}
			}
		}
	}

	private static M68kMachineValue CreateValueForType(
		M68kMachineFunction function,
		CilType type,
		CompilationModule module)
	{
		var kind = module.IsTransparentScalarType(type)
			? CilStackValueKind.ManagedPointer
			: CilStackAnalyzer.StackKindForType(type);
		return CreateValue(
			function,
			kind,
			kind is CilStackValueKind.Int64 or CilStackValueKind.Float64
				? M68kMachineValueWidth.LongPair
				: WidthFor(kind));
	}

	private static M68kMachineValue CreateValue(
		M68kMachineFunction function,
		CilStackValueKind kind,
		M68kMachineValueWidth width)
	{
		var allowed = width switch
		{
			M68kMachineValueWidth.Byte or M68kMachineValueWidth.Word =>
				M68kRegisterSet.Data,
			M68kMachineValueWidth.LongPair =>
				M68kRegisterSet.DataPairStarts,
			_ when kind == CilStackValueKind.AggregateAddress =>
				M68kRegisterSet.Address,
			_ when kind is
				CilStackValueKind.Reference or
				CilStackValueKind.ManagedPointer =>
				M68kRegisterSet.DataOrAddress,
			_ => M68kRegisterSet.Data
		};
		return function.CreateValue(
			kind,
			width,
			allowed,
			isGcReference: kind == CilStackValueKind.Reference);
	}

	private static M68kMachineValueWidth WidthFor(CilStackValueKind kind) =>
		kind switch
		{
			CilStackValueKind.BooleanByte or
				CilStackValueKind.UnsignedByte or
				CilStackValueKind.SignedByte =>
				M68kMachineValueWidth.Byte,
			CilStackValueKind.UnsignedWord or
				CilStackValueKind.SignedWord =>
				M68kMachineValueWidth.Word,
			CilStackValueKind.Int64 or CilStackValueKind.Float64 => M68kMachineValueWidth.LongPair,
			_ => M68kMachineValueWidth.Long
		};

	private static bool TryGetLoadArgumentIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldarg_0.Value &&
			op.Value <= OpCodes.Ldarg_3.Value)
		{
			index = op.Value - OpCodes.Ldarg_0.Value;
			return true;
		}
		if (op == OpCodes.Ldarg || op == OpCodes.Ldarg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = default;
		return false;
	}

	private static bool TryGetLoadArgumentAddressIndex(
		CilInstruction instruction,
		out int index)
	{
		if (instruction.OpCode == OpCodes.Ldarga ||
			instruction.OpCode == OpCodes.Ldarga_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = 0;
		return false;
	}

	private static bool TryGetStoreArgumentIndex(
		CilInstruction instruction,
		out int index)
	{
		if (instruction.OpCode == OpCodes.Starg ||
			instruction.OpCode == OpCodes.Starg_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = default;
		return false;
	}

	private static M68kMachineOperation OperationFor(OpCode op)
	{
		if (op == OpCodes.Nop)
		{
			return M68kMachineOperation.Other;
		}
		if (IsIntegerConstant(op) ||
			op == OpCodes.Ldc_I8 ||
			op == OpCodes.Ldc_R4 ||
			op == OpCodes.Ldc_R8 ||
			op == OpCodes.Ldnull)
		{
			return M68kMachineOperation.Constant;
		}
		if (op == OpCodes.Ldstr)
		{
			return M68kMachineOperation.Address;
		}
		if (op == OpCodes.Ldftn || op == OpCodes.Ldvirtftn)
		{
			return M68kMachineOperation.FunctionAddress;
		}
		if (op == OpCodes.Call || op == OpCodes.Callvirt ||
			op == OpCodes.Newobj)
		{
			return M68kMachineOperation.Call;
		}
		if (op == OpCodes.Newarr)
		{
			return M68kMachineOperation.ArrayAllocate;
		}
		if (op == OpCodes.Localloc)
		{
			return M68kMachineOperation.LocalAddress;
		}
		if (op == OpCodes.Isinst || op == OpCodes.Castclass)
		{
			return M68kMachineOperation.TypeTest;
		}
		if (op == OpCodes.Box)
		{
			return M68kMachineOperation.Box;
		}
		if (op == OpCodes.Unbox || op == OpCodes.Unbox_Any)
		{
			return M68kMachineOperation.Unbox;
		}
		if (op == OpCodes.Ldelema)
		{
			return M68kMachineOperation.ArrayAddress;
		}
		if (IsMachineArrayStore(op))
		{
			return M68kMachineOperation.ArrayStore;
		}
		if (IsMachineArrayLoad(op) || op == OpCodes.Ldlen)
		{
			return M68kMachineOperation.ArrayLoad;
		}
		if (op == OpCodes.Add)
		{
			return M68kMachineOperation.Add;
		}
		if (op == OpCodes.Sub)
		{
			return M68kMachineOperation.Subtract;
		}
		if (op == OpCodes.Mul || op == OpCodes.Mul_Ovf_Un)
		{
			return M68kMachineOperation.Multiply;
		}
		if (op == OpCodes.Div || op == OpCodes.Div_Un)
		{
			return M68kMachineOperation.Divide;
		}
		if (op == OpCodes.Rem || op == OpCodes.Rem_Un)
		{
			return M68kMachineOperation.Remainder;
		}
		if (op == OpCodes.And)
		{
			return M68kMachineOperation.And;
		}
		if (op == OpCodes.Or)
		{
			return M68kMachineOperation.Or;
		}
		if (op == OpCodes.Xor)
		{
			return M68kMachineOperation.Xor;
		}
		if (op == OpCodes.Neg)
		{
			return M68kMachineOperation.Negate;
		}
		if (op == OpCodes.Not)
		{
			return M68kMachineOperation.Not;
		}
		if (op == OpCodes.Shl || op == OpCodes.Shr || op == OpCodes.Shr_Un)
		{
			return M68kMachineOperation.Shift;
		}
		if (IsComparison(op))
		{
			return M68kMachineOperation.Compare;
		}
		if (op == OpCodes.Ret)
		{
			return M68kMachineOperation.Return;
		}
		if (op == OpCodes.Throw || op == OpCodes.Rethrow)
		{
			return M68kMachineOperation.Throw;
		}
		if (op == OpCodes.Switch)
		{
			return M68kMachineOperation.Switch;
		}
		if (op.FlowControl == FlowControl.Cond_Branch)
		{
			return M68kMachineOperation.ConditionalBranch;
		}
		if (op.FlowControl == FlowControl.Branch)
		{
			return M68kMachineOperation.Branch;
		}
		if (op.Name?.StartsWith("ld", StringComparison.Ordinal) == true)
		{
			return M68kMachineOperation.Load;
		}
		if (op.Name?.StartsWith("st", StringComparison.Ordinal) == true)
		{
			return M68kMachineOperation.Store;
		}
		if (op.Name?.StartsWith("conv", StringComparison.Ordinal) == true)
		{
			return M68kMachineOperation.Convert;
		}
		return M68kMachineOperation.Other;
	}

	private static bool IsMachineArrayAccess(OpCode op) =>
		op == OpCodes.Ldelema ||
		op == OpCodes.Ldlen ||
		IsMachineArrayLoad(op) ||
		IsMachineArrayStore(op);

	private static bool IsMachineArrayLoad(OpCode op) =>
		op == OpCodes.Ldelem ||
		op == OpCodes.Ldelem_I1 ||
		op == OpCodes.Ldelem_U1 ||
		op == OpCodes.Ldelem_I2 ||
		op == OpCodes.Ldelem_U2 ||
		op == OpCodes.Ldelem_I4 ||
		op == OpCodes.Ldelem_U4 ||
		op == OpCodes.Ldelem_I ||
		op == OpCodes.Ldelem_Ref;

	private static bool IsMachineArrayStore(OpCode op) =>
		op == OpCodes.Stelem ||
		op == OpCodes.Stelem_I1 ||
		op == OpCodes.Stelem_I2 ||
		op == OpCodes.Stelem_I4 ||
		op == OpCodes.Stelem_I ||
		op == OpCodes.Stelem_Ref;

	private static bool RequiresAddressBase(OpCode op) =>
		op == OpCodes.Ldfld ||
		op == OpCodes.Ldflda ||
		op == OpCodes.Stfld ||
		op == OpCodes.Initobj ||
		IsIndirectLoad(op) ||
		IsIndirectStore(op);

	private static bool IsIntegerConstant(OpCode op) =>
		op == OpCodes.Ldc_I4_M1 ||
		(op.Value >= OpCodes.Ldc_I4_0.Value &&
		 op.Value <= OpCodes.Ldc_I4_8.Value) ||
		op == OpCodes.Ldc_I4_S ||
		op == OpCodes.Ldc_I4;

	private static M68kRegisterSet ClobbersFor(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction,
		M68kCpuTarget cpu)
	{
		var op = instruction.OpCode;
		if (op == OpCodes.Localloc)
		{
			return M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1);
		}
		if ((op == OpCodes.Ldfld || op == OpCodes.Ldsfld ||
			 op == OpCodes.Stfld || op == OpCodes.Stsfld) &&
			module.ResolveFieldToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset) is { } field &&
			module.TryGetReferenceFreeStructLayout(
				field.Type,
				field.ModuleName,
				out var fieldLayout) &&
			fieldLayout.Size > 4)
		{
			var result = M68kRegisterSet.From(M68kRegister.D0);
			return field.IsStatic
				? result.Add(M68kRegister.A1)
				: result;
		}
		if ((TryGetStoreLocalIndex(instruction, out var storedLocal) &&
			module.TryGetReferenceFreeStructLayout(
				method.Locals[storedLocal],
				method.ModuleName,
				out var localLayout) &&
			localLayout.Size > 4) ||
			TryGetStoreArgumentIndex(instruction, out var storedArgument) &&
			module.TryGetReferenceFreeStructLayout(
				ArgumentType(method, storedArgument),
				method.ModuleName,
				out var argumentLayout) &&
			argumentLayout.Size > 4)
		{
			return M68kRegisterSet.From(M68kRegister.D0);
		}
		if (op == OpCodes.Call || op == OpCodes.Callvirt ||
			op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.Definition is null &&
				target.ImportName?.StartsWith(
					"intrinsic:runtimehelpers-is-reference-or-contains-references:",
					StringComparison.Ordinal) == true)
			{
				return M68kRegisterSet.None;
			}
			if (target.Definition is null &&
				target.ImportName is
					"intrinsic:aptr-read-uint32" or
					"intrinsic:aptr-write-uint32" or
					"intrinsic:aptr-raw" or
					"intrinsic:iff-handle-stream" or
					"intrinsic:iff-handle-set-stream" or
					"intrinsic:object-reference-equals" or
					"intrinsic:string-char" or
					"intrinsic:string-length")
			{
				// These intrinsics lower to one effective-address operation or a
				// straight-line comparison.
				// Their explicit uses/definitions already describe every register
				// they touch, so caller-save clobbers would manufacture needless
				// live-range preservation around ordinary loads and stores.
				return M68kRegisterSet.None;
			}
			if (target.Definition is null &&
				(target.ImportName?.StartsWith(
					"intrinsic:nullable-",
					StringComparison.Ordinal) == true ||
				 target.ImportName is
					"intrinsic:aptr-is-null" or
					"intrinsic:aptr-is-not-null"))
			{
				return M68kRegisterSet.From(
					M68kRegister.D0,
					M68kRegister.D1);
			}
			if (target.Definition?.ExternalCall is { } externalCall)
			{
				var result = RegistersUsedByAbi(
					externalCall.Abi,
					target.Signature);
				result = result.Add(externalCall.Convention.BaseRegister);
				if (externalCall.Convention.CacheRegister is { } cache)
				{
					result = result.Add(cache);
				}
				if (externalCall.Convention.ExceptionStatusRegister is { } status)
				{
					result = result.Add(status);
				}
				return result;
			}
			if (target.Definition?.ImportAbi is { } importAbi)
			{
				return RegistersUsedByAbi(importAbi, target.Signature);
			}
			var clobbers = M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1);
			if (target.ImportName is
				"intrinsic:string-equality" or
				"intrinsic:string-inequality")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.ImportName == "intrinsic:string-concat-two")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.A4)
					.Add(M68kRegister.A5);
			}
			if (target.ImportName == "intrinsic:string-substring")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.D4)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3)
					.Add(M68kRegister.A4);
			}
			if (target.ImportName == "intrinsic:string-copy-to-char-array")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.D4)
					.Add(M68kRegister.D5)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.ImportName == "intrinsic:string-copy-to-span-char")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.ImportName == "intrinsic:string-to-char-array")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.D4)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3)
					.Add(M68kRegister.A4);
			}
			if (target.ImportName is
				"intrinsic:string-starts-with-ordinal" or
				"intrinsic:string-ends-with-ordinal")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.ImportName is
				"intrinsic:string-contains-ordinal" or
				"intrinsic:string-index-of-ordinal")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.D4)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.ImportName?.StartsWith(
					"intrinsic:span-slice-",
					StringComparison.Ordinal) == true ||
				target.ImportName?.StartsWith(
					"intrinsic:readonly-span-slice-",
					StringComparison.Ordinal) == true)
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.A2);
			}
			if (target.ImportName?.StartsWith(
					"intrinsic:span-copy-to:",
					StringComparison.Ordinal) == true ||
				target.ImportName?.StartsWith(
					"intrinsic:readonly-span-copy-to:",
					StringComparison.Ordinal) == true)
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.ImportName == "intrinsic:delegate-invoke")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.D3)
					.Add(M68kRegister.D4)
					.Add(M68kRegister.D5)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3)
					.Add(M68kRegister.A4);
			}
			if (target.ImportName == "intrinsic:delegate-combine")
			{
				return M68kRegisterSet.From(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kRegister.D2,
					M68kRegister.D3,
					M68kRegister.D4,
					M68kRegister.A0,
					M68kRegister.A1,
					M68kRegister.A4,
					M68kRegister.A5);
			}
			if (target.ImportName == "intrinsic:delegate-remove")
			{
				return M68kRegisterSet.From(
					M68kRegister.D0,
					M68kRegister.D1,
					M68kRegister.D2,
					M68kRegister.D3,
					M68kRegister.D4,
					M68kRegister.D5,
					M68kRegister.D6,
					M68kRegister.D7,
					M68kRegister.A0,
					M68kRegister.A1,
					M68kRegister.A4,
					M68kRegister.A5);
			}
			if (target.ImportName is
				"intrinsic:delegate-equality" or
				"intrinsic:delegate-inequality")
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3)
					.Add(M68kRegister.A4)
					.Add(M68kRegister.A5);
			}
			if (target.Definition?.DeclaringTypeIsInterface == true)
			{
				return clobbers
					.Add(M68kRegister.D2)
					.Add(M68kRegister.A2)
					.Add(M68kRegister.A3);
			}
			if (target.Definition is { } definition &&
				RequiresVirtualDispatch(instruction, definition))
			{
				return clobbers.Add(M68kRegister.A2);
			}
			return clobbers;
		}
		if (op == OpCodes.Newarr)
		{
			return M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.D2,
				M68kRegister.A0,
				M68kRegister.A1);
		}
		if (op == OpCodes.Isinst || op == OpCodes.Castclass)
		{
			return M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1,
				M68kRegister.A2);
		}
		if (op == OpCodes.Unbox || op == OpCodes.Unbox_Any)
		{
			return M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1);
		}
		if (op == OpCodes.Ldvirtftn)
		{
			return M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.D2,
				M68kRegister.A0,
				M68kRegister.A1,
				M68kRegister.A2,
				M68kRegister.A3);
		}
		if (op == OpCodes.Mul_Ovf_Un)
		{
			return M68kRegisterSet.From(M68kRegister.D2);
		}
		if (op == OpCodes.Mul && cpu == M68kCpuTarget.M68000)
		{
			return M68kRegisterSet.From(M68kRegister.D2, M68kRegister.D3);
		}
		if (op == OpCodes.Div || op == OpCodes.Div_Un ||
			op == OpCodes.Rem || op == OpCodes.Rem_Un)
		{
			return cpu == M68kCpuTarget.M68000
				? M68kRegisterSet.From(
					M68kRegister.D2,
					M68kRegister.D3,
					M68kRegister.D4,
					M68kRegister.D5,
					M68kRegister.D6)
				: M68kRegisterSet.From(M68kRegister.D2);
		}
		if (op == OpCodes.Switch)
		{
			return M68kRegisterSet.From(M68kRegister.D0);
		}
		return M68kRegisterSet.None;
	}

	private static bool RequiresVirtualDispatch(
		CilInstruction instruction,
		CilMethod method) =>
		instruction.OpCode == OpCodes.Callvirt &&
		method.IsVirtual &&
		!method.IsFinal &&
		!method.DeclaringTypeIsSealed;

	private static bool IsIndirectLoad(OpCode op) =>
		op == OpCodes.Ldind_I1 ||
		op == OpCodes.Ldind_U1 ||
		op == OpCodes.Ldind_I2 ||
		op == OpCodes.Ldind_U2 ||
		op == OpCodes.Ldind_I4 ||
		op == OpCodes.Ldind_U4 ||
		op == OpCodes.Ldind_I ||
		op == OpCodes.Ldind_I8 ||
		op == OpCodes.Ldind_R4 ||
		op == OpCodes.Ldind_Ref ||
		op == OpCodes.Ldobj;

	private static bool IsIndirectStore(OpCode op) =>
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I ||
		op == OpCodes.Stind_I8 ||
		op == OpCodes.Stind_R4 ||
		op == OpCodes.Stind_Ref ||
		op == OpCodes.Stobj;

	private static M68kRegisterSet RegistersUsedByAbi(
		CilRegisterAbi abi,
		System.Reflection.Metadata.MethodSignature<CilType> signature)
	{
		var result = M68kRegisterSet.From(abi.ReturnRegister);
		for (var index = 0; index < abi.ParameterRegisters.Count; index++)
		{
			var register = abi.ParameterRegisters[index];
			result = result.Add(register);
			if (index < signature.ParameterTypes.Length &&
				signature.ParameterTypes[index].IsSupportedScalar &&
				signature.ParameterTypes[index].Size == 8 &&
				register >= M68kRegister.D0 &&
				register < M68kRegister.D7)
			{
				result = result.Add(register + 1);
			}
		}
		return result;
	}

	private static M68kMachineMemoryEffect MemoryEffectFor(OpCode op)
	{
		if (IsIntegerConstant(op) ||
			op == OpCodes.Ldnull ||
			op == OpCodes.Ldstr ||
			op.Name?.StartsWith("ldloc", StringComparison.Ordinal) == true ||
			op.Name?.StartsWith("ldarg", StringComparison.Ordinal) == true)
		{
			return M68kMachineMemoryEffect.None;
		}
		if (op == OpCodes.Call || op == OpCodes.Callvirt ||
			op == OpCodes.Newobj || op == OpCodes.Newarr)
		{
			return M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write;
		}
		if (op.Name?.StartsWith("st", StringComparison.Ordinal) == true ||
			op == OpCodes.Initobj)
		{
			return M68kMachineMemoryEffect.Write;
		}
		if (op.Name?.StartsWith("ld", StringComparison.Ordinal) == true)
		{
			return M68kMachineMemoryEffect.Read;
		}
		return M68kMachineMemoryEffect.None;
	}

	private static M68kMachineMemoryEffect MemoryEffectFor(
		M68kMachineOperation operation,
		OpCode op) =>
		operation switch
		{
			M68kMachineOperation.LocalLoad =>
				M68kMachineMemoryEffect.Read,
			M68kMachineOperation.ArgumentLoad =>
				M68kMachineMemoryEffect.Read,
			M68kMachineOperation.LocalStore =>
				M68kMachineMemoryEffect.Write,
			M68kMachineOperation.ArgumentStore =>
				M68kMachineMemoryEffect.Write,
			M68kMachineOperation.LocalAddress or
				M68kMachineOperation.ArgumentAddress =>
				M68kMachineMemoryEffect.None,
			_ => MemoryEffectFor(op)
		};

	private static bool IsConservativeSafepoint(OpCode op) =>
		op == OpCodes.Call || op == OpCodes.Callvirt ||
		op == OpCodes.Newobj || op == OpCodes.Newarr || op == OpCodes.Box;

	private static bool MayThrow(OpCode op) =>
		IsConservativeSafepoint(op) ||
		op == OpCodes.Mul_Ovf_Un ||
		op == OpCodes.Throw || op == OpCodes.Castclass || op == OpCodes.Ldvirtftn ||
		op == OpCodes.Unbox || op == OpCodes.Unbox_Any ||
		op == OpCodes.Div || op == OpCodes.Div_Un ||
		op == OpCodes.Rem || op == OpCodes.Rem_Un ||
		op.Name?.Contains("elem", StringComparison.Ordinal) == true ||
		op.Name?.Contains("ind", StringComparison.Ordinal) == true ||
		op.Name?.Contains("fld", StringComparison.Ordinal) == true;

	private static bool IsComparison(OpCode op) =>
		op == OpCodes.Ceq ||
		op == OpCodes.Cgt ||
		op == OpCodes.Cgt_Un ||
		op == OpCodes.Clt ||
		op == OpCodes.Clt_Un;

	private static M68kCondition ComparisonCondition(OpCode op) =>
		op == OpCodes.Ceq
			? M68kCondition.Equal
			: op == OpCodes.Cgt
				? M68kCondition.GreaterThan
				: op == OpCodes.Cgt_Un
					? M68kCondition.Higher
					: op == OpCodes.Clt
						? M68kCondition.LessThan
						: M68kCondition.CarrySet;

	private static M68kCondition PredicateCondition(
		CilMethod method,
		CompilationModule module,
		CilInstruction instruction)
	{
		var target = module.ResolveMethodToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		return target.ImportName == "intrinsic:aptr-is-null"
			? M68kCondition.Equal
			: M68kCondition.NotEqual;
	}

	private static M68kCondition InvertCondition(M68kCondition condition) =>
		(M68kCondition)((int)condition ^ 1);

	private static bool EndsBlock(OpCode op) =>
		op.FlowControl is
			FlowControl.Branch or
			FlowControl.Cond_Branch or
			FlowControl.Return or
			FlowControl.Throw ||
		op == OpCodes.Switch ||
		op == OpCodes.Endfinally;

	private static bool TryGetLoadLocalIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Ldloc_0.Value &&
			op.Value <= OpCodes.Ldloc_3.Value)
		{
			index = op.Value - OpCodes.Ldloc_0.Value;
			return true;
		}
		if (op == OpCodes.Ldloc || op == OpCodes.Ldloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = 0;
		return false;
	}

	private static bool TryGetStoreLocalIndex(
		CilInstruction instruction,
		out int index)
	{
		var op = instruction.OpCode;
		if (op.Value >= OpCodes.Stloc_0.Value &&
			op.Value <= OpCodes.Stloc_3.Value)
		{
			index = op.Value - OpCodes.Stloc_0.Value;
			return true;
		}
		if (op == OpCodes.Stloc || op == OpCodes.Stloc_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = 0;
		return false;
	}

	private static bool TryGetLoadLocalAddressIndex(
		CilInstruction instruction,
		out int index)
	{
		if (instruction.OpCode == OpCodes.Ldloca ||
			instruction.OpCode == OpCodes.Ldloca_S)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}
		index = 0;
		return false;
	}
}
