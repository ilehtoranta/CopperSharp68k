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
						type.IsReference));
			}
		}
		foreach (var argumentIndex in method.Instructions
			.Select(instruction =>
				TryGetLoadArgumentAddressIndex(instruction, out var index)
					? (int?)index
					: null)
			.OfType<int>()
			.Distinct())
		{
			var type = ArgumentType(method, argumentIndex);
			function.ArgumentHomes.Add(
				argumentIndex,
				new M68kFrameHome(
					argumentIndex,
					FrameHomeSize(module, type, includeAlignmentPadding: false),
					type.IsReference));
		}
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
				argumentRegisters);
		}
		PopulatePhiInputs(states);
		EliminateDeadMachineValues(function);
		M68kConditionFlowOptimizer.Run(function, method, module);
		EliminateDeadMachineValues(function);
		M68kMachineCostAnalysis.Apply(function);
		M68kMachineIrVerifier.Verify(function);
		return function;
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
		IReadOnlyList<M68kRegister?>? argumentRegisters)
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
				var argumentValue = GetOrCreateArgumentValue(
					function,
					entryBlock,
					loadedArgument,
					pushedKinds,
					argumentValues,
					argumentRegisters);
				uses = [argumentValue];
				operation = M68kMachineOperation.Copy;
			}
			else if (TryGetLoadLocalIndex(instruction, out var loadedLocal) &&
				localValues[loadedLocal] is { } loadedValue)
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
				if (TryGetLoadLocalIndex(instruction, out loadedLocal))
				{
					operation = M68kMachineOperation.LocalLoad;
					frameIndex = loadedLocal;
				}
				else if (TryGetStoreLocalIndex(instruction, out storedLocal))
				{
					operation = M68kMachineOperation.LocalStore;
					frameIndex = storedLocal;
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
			if (instruction.OpCode == OpCodes.Newarr)
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
				state.Block.Instructions.Add(function.CreateInstruction(
					operation,
					instruction.Offset,
					uses,
					definitions,
					ClobbersFor(method, module, instruction, cpu),
					MemoryEffectFor(operation, instruction.OpCode),
					mayThrow: true,
					sourceInstruction: instruction));
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
					stackVarargsArrays.TryGetValue(
						uses[^1],
						out var callCandidate) &&
					TryFlattenStackVarargsCall(
						method,
						module,
						instruction,
						uses,
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
			.. uses.Take(uses.Count - 1),
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
		for (var index = block.Instructions.Count - 1; index >= 0; index--)
		{
			var instruction = block.Instructions[index];
			if (!instruction.Definitions.Contains(value))
			{
				continue;
			}
			if (instruction.Operation != M68kMachineOperation.Constant ||
				instruction.SourceInstruction is not { } source)
			{
				break;
			}
			return TryGetIlIntegerConstant(source, out constant);
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

	private static void AddConstrainedArrayAccess(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction instruction,
		M68kCpuTarget cpu,
		M68kMachineOperation operation,
		IReadOnlyList<int> uses,
		IReadOnlyList<int> definitions)
	{
		var isStore = operation == M68kMachineOperation.ArrayStore;
		var isLength = instruction.OpCode == OpCodes.Ldlen;
		if (uses.Count != (isStore ? 3 : isLength ? 1 : 2) ||
			definitions.Count != (isStore ? 0 : 1))
		{
			throw new InvalidOperationException(
				$"Array access at IL_{instruction.Offset:X4} has invalid arity.");
		}
		var constrainedUses = uses.ToArray();
		constrainedUses[0] = AddRegisterClassCopy(
			function,
			block,
			instruction,
			uses[0],
			M68kRegisterSet.Address);
		if (!isLength)
		{
			var elementSize = ArrayElementSize(instruction.OpCode);
			constrainedUses[1] = AddRegisterClassCopy(
				function,
				block,
				instruction,
				uses[1],
				M68kRegisterSet.Data,
				allowCopyCoalescing:
					cpu != M68kCpuTarget.M68000 || elementSize == 1);
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
		var argumentConstraints = (stackVarargsRegister is not null
			? GetStackVarargsArgumentRegisters(target, uses.Count)
			: GetCallArgumentRegisters(
				function,
				target,
				uses,
				hasInstanceArgumentOverride ??
					(target.Signature.Header.IsInstance &&
						(instruction.OpCode != OpCodes.Newobj ||
						 target.ImportName?.StartsWith(
						"intrinsic:nullable-ctor:",
						StringComparison.Ordinal) == true))))
			.ToList();
		var sourceUses = uses.ToList();
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
		var stackArgumentBytes = 0;
		for (var index = Math.Min(constrainedUses.Length, argumentConstraints.Count) - 1;
			index >= 0;
			index--)
		{
			if (!argumentConstraints[index].IsStack)
			{
				continue;
			}
			var source = function.Values[constrainedUses[index]];
			var bytes = source.Width == M68kMachineValueWidth.LongPair ? 8 : 4;
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

		var constrainedDefinitions = definitions.ToArray();
		M68kMachineValue? fixedReturn = null;
		if (definitions.Count == 1 &&
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
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			instruction.Offset,
			registerUses,
			constrainedDefinitions,
			ClobbersFor(caller, module, instruction, cpu),
			M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: instruction,
			stackVarargsRegister: stackVarargsRegister,
			immediate: embeddedDisplacement));
		if (stackArgumentBytes != 0)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.OutgoingArgumentCleanup,
				instruction.Offset,
				argumentIndex: stackArgumentBytes));
		}
		if (fixedReturn is not null)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				instruction.Offset,
				uses: [fixedReturn.Id],
				definitions: definitions));
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

	private static bool IsLiteralAddressIntrinsic(string? name) =>
		name is
			"intrinsic:cstring-from-literal" or
			"intrinsic:amiga-vararg-from-literal" or
			"intrinsic:aptr-export-address";

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
		bool hasInstanceArgument)
	{
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
		if (target.ImportName == "intrinsic:aptr-read-uint32")
		{
			return [
				CallArgumentConstraint.RegisterClass(M68kRegisterSet.Address),
				CallArgumentConstraint.Fixed(M68kRegister.D0)];
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
			if (parameter.Kind != CilTypeKind.GenericParameter &&
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
		name is
			"intrinsic:aptr-raw" or
			"intrinsic:aptr-is-null" or
			"intrinsic:aptr-is-not-null" or
			"intrinsic:iff-handle-stream" or
			"intrinsic:iff-handle-set-stream" or
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
		if (op == OpCodes.Call || op == OpCodes.Callvirt ||
			op == OpCodes.Newobj)
		{
			return M68kMachineOperation.Call;
		}
		if (op == OpCodes.Newarr)
		{
			return M68kMachineOperation.ArrayAllocate;
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
		if (op == OpCodes.Mul)
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
		op == OpCodes.Ldelem_I1 ||
		op == OpCodes.Ldelem_U1 ||
		op == OpCodes.Ldelem_I2 ||
		op == OpCodes.Ldelem_U2 ||
		op == OpCodes.Ldelem_I4 ||
		op == OpCodes.Ldelem_U4 ||
		op == OpCodes.Ldelem_I ||
		op == OpCodes.Ldelem_Ref;

	private static bool IsMachineArrayStore(OpCode op) =>
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
		if (op == OpCodes.Call || op == OpCodes.Callvirt ||
			op == OpCodes.Newobj)
		{
			var target = module.ResolveMethodToken(
				(int)instruction.Operand!,
				method,
				instruction.Offset);
			if (target.Definition is null &&
				target.ImportName is
					"intrinsic:aptr-read-uint32" or
					"intrinsic:aptr-write-uint32" or
					"intrinsic:aptr-raw" or
					"intrinsic:iff-handle-stream" or
					"intrinsic:iff-handle-set-stream" or
					"intrinsic:string-length")
			{
				// These intrinsics lower to a single effective-address operation.
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
		op == OpCodes.Ldind_Ref ||
		op == OpCodes.Ldobj;

	private static bool IsIndirectStore(OpCode op) =>
		op == OpCodes.Stind_I1 ||
		op == OpCodes.Stind_I2 ||
		op == OpCodes.Stind_I4 ||
		op == OpCodes.Stind_I ||
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
			M68kMachineOperation.LocalAddress or
				M68kMachineOperation.ArgumentAddress =>
				M68kMachineMemoryEffect.None,
			_ => MemoryEffectFor(op)
		};

	private static bool IsConservativeSafepoint(OpCode op) =>
		op == OpCodes.Call || op == OpCodes.Callvirt ||
		op == OpCodes.Newobj || op == OpCodes.Newarr;

	private static bool MayThrow(OpCode op) =>
		IsConservativeSafepoint(op) ||
		op == OpCodes.Throw ||
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
