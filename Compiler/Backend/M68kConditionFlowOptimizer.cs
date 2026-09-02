/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal static class M68kConditionFlowOptimizer
{
	private sealed record Definition(
		M68kMachineBlock Block,
		M68kMachineInstruction? Instruction,
		M68kMachinePhi? Phi);

	private sealed record ConditionLeaf(
		M68kMachineBlock Block,
		M68kMachineInstruction Producer,
		M68kMachineBranchCondition Condition,
		HashSet<M68kMachineInstruction> Removable,
		bool Inverted = false);

	private sealed record BooleanResolution(
		bool? Constant,
		ConditionLeaf? Leaf,
		M68kMachinePhi? Phi,
		bool Inverted,
		HashSet<M68kMachineInstruction> Removable)
	{
		public BooleanResolution Invert() => this with { Inverted = !Inverted };
	}

	public static void Run(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module)
	{
		while (true)
		{
			if (TryThreadBooleanPhi(function, method, module))
			{
				// Threading a constant phi can make the untaken successor of the
				// removed routing block unreachable. Repair that transformation
				// immediately, before another CFG analysis observes the orphan.
				M68kControlFlowCleanup.RemoveUnreachableBlocks(function);
				continue;
			}
			if (!TryFuseDirectBooleanBranch(function, method, module))
			{
				break;
			}
		}
		RemoveUnreferencedValues(function);
		M68kControlFlowAnalysis.ComputeLoopDepths(function);
		M68kMachineIrVerifier.Verify(function);
	}

	private static void RemoveUnreferencedValues(M68kMachineFunction function)
	{
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

	private static bool TryFuseDirectBooleanBranch(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module)
	{
		var definitions = BuildDefinitions(function);
		var useCounts = BuildUseCounts(function);
		foreach (var block in function.Blocks)
		{
			if (block.Instructions.LastOrDefault() is not { } branch ||
				!IsValueBranch(branch) ||
				branch.BranchCondition is not null ||
				!TryResolveBoolean(
					branch.Uses[0],
					definitions,
					useCounts,
					method,
					module,
					new HashSet<int>(),
					out var resolution) ||
				resolution.Leaf is not { } leaf ||
				leaf.Block != block ||
				!CanRemoveForBranch(block, branch, leaf.Removable, useCounts))
			{
				continue;
			}

			var condition = ApplyBranchPolarity(
				leaf.Condition.Condition,
				resolution.Inverted ^ leaf.Inverted,
				branch.SourceInstruction!.OpCode);
			foreach (var removable in leaf.Removable)
			{
				block.Instructions.Remove(removable);
			}
			block.Instructions[^1] = CreateFusedBranch(
				branch,
				leaf,
				condition);
			return true;
		}
		return false;
	}

	private static bool TryThreadBooleanPhi(
		M68kMachineFunction function,
		CilMethod method,
		CompilationModule module)
	{
		var definitions = BuildDefinitions(function);
		var useCounts = BuildUseCounts(function);
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		foreach (var merge in function.Blocks.ToArray())
		{
			if (merge.Id == function.EntryBlockId ||
				merge.IsExceptionEntry ||
				IsExceptionBoundary(method, merge.StartIlOffset) ||
				merge.Instructions.LastOrDefault() is not { } branch ||
				!IsValueBranch(branch) ||
				branch.BranchCondition is not null ||
				merge.Successors.Count != 2 ||
				!TryResolveBoolean(
					branch.Uses[0],
					definitions,
					useCounts,
					method,
					module,
					new HashSet<int>(),
					out var merged) ||
				merged.Phi is not { } phi ||
				!IsPureBooleanRoutingBlock(merge, branch, merged.Removable))
			{
				continue;
			}

			var incoming = new Dictionary<int, BooleanResolution>();
			var valid = true;
			foreach (var (predecessorId, value) in phi.Inputs)
			{
				if (!blocks.TryGetValue(predecessorId, out var predecessor) ||
					predecessor.Successors.Count != 1 ||
					predecessor.Successors[0] != merge.Id ||
					!HasSameProtectedState(method, predecessor.StartIlOffset, merge.StartIlOffset) ||
					!TryResolveBoolean(
						value,
						definitions,
						useCounts,
						method,
						module,
						new HashSet<int>(),
						out var resolution) ||
					resolution.Phi is not null ||
					(resolution.Constant is not null
						? !CanRemoveForBranch(
							predecessor,
							predecessor.Instructions.LastOrDefault(),
							resolution.Removable,
							useCounts)
						: resolution.Leaf is not { } leaf ||
						  leaf.Block != predecessor ||
						  !CanRemoveForBranch(
							  predecessor,
							  predecessor.Instructions.LastOrDefault(),
							  resolution.Removable,
							  useCounts)))
				{
					valid = false;
					break;
				}
				incoming.Add(predecessorId, resolution);
			}
			if (!valid || !CanRedirectSuccessorPhis(merge, incoming, blocks))
			{
				continue;
			}

			var branchInverted = IsBranchFalse(branch.SourceInstruction!.OpCode);
			foreach (var (predecessorId, resolution) in incoming)
			{
				var predecessor = blocks[predecessorId];
				RemoveTerminator(predecessor);
				if (resolution.Constant is { } constant)
				{
					var value = constant ^ resolution.Inverted ^ merged.Inverted;
					var target = merge.Successors[
						value ^ branchInverted ? 0 : 1];
					RedirectToConstantTarget(
						function,
						merge,
						predecessor,
						target,
						blocks);
					RemoveSafeRemovableInstructions(
						predecessor,
						resolution.Removable,
						blocks,
						[]);
					predecessor.Instructions.Add(function.CreateInstruction(
						M68kMachineOperation.Branch,
						branch.IlOffset,
						sourceInstruction: new CilInstruction(
							branch.IlOffset,
							OpCodes.Br,
							blocks[target].StartIlOffset,
							branch.SourceInstruction.NextOffset)));
					continue;
				}

				var leaf = resolution.Leaf!;
				var inverted = resolution.Inverted ^ leaf.Inverted ^
					merged.Inverted ^ branchInverted;
				var condition = inverted
					? InvertCondition(leaf.Condition.Condition)
					: leaf.Condition.Condition;
				RedirectToConditionalTargets(
					function,
					merge,
					predecessor,
					blocks);
				RemoveSafeRemovableInstructions(
					predecessor,
					resolution.Removable,
					blocks,
					leaf.Producer.Uses);
				predecessor.Instructions.Add(CreateFusedBranch(
					function,
					branch,
					leaf,
					condition));
			}

			foreach (var successorId in merge.Successors.ToArray())
			{
				blocks[successorId].Predecessors.Remove(merge.Id);
				RemovePhiInput(blocks[successorId], merge.Id);
			}
			function.RemoveBlocks(new HashSet<int> { merge.Id });
			return true;
		}
		return false;
	}

	private static void RemoveSafeRemovableInstructions(
		M68kMachineBlock predecessor,
		IReadOnlySet<M68kMachineInstruction> removable,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks,
		IReadOnlyCollection<int> branchUses)
	{
		var protectedValues = new HashSet<int>(branchUses);
		foreach (var successorId in predecessor.Successors)
		{
			foreach (var phi in blocks[successorId].Phis)
			{
				if (phi.Inputs.TryGetValue(predecessor.Id, out var input))
					protectedValues.Add(input);
			}
		}

		bool changed;
		do
		{
			changed = false;
			foreach (var instruction in removable)
			{
				if (!instruction.Definitions.Any(protectedValues.Contains))
					continue;
				foreach (var use in instruction.Uses)
					changed |= protectedValues.Add(use);
			}
		}
		while (changed);

		foreach (var instruction in removable)
		{
			if (!instruction.Definitions.Any(protectedValues.Contains))
				predecessor.Instructions.Remove(instruction);
		}
	}

	private static bool TryResolveBoolean(
		int value,
		IReadOnlyDictionary<int, Definition> definitions,
		IReadOnlyDictionary<int, int> useCounts,
		CilMethod method,
		CompilationModule module,
		HashSet<int> visiting,
		out BooleanResolution resolution)
	{
		resolution = null!;
		if (!visiting.Add(value) ||
			!definitions.TryGetValue(value, out var definition))
		{
			return false;
		}
		try
		{
			if (definition.Phi is { } phi)
			{
				resolution = new BooleanResolution(
					null,
					null,
					phi,
					false,
					[]);
				return true;
			}

			var instruction = definition.Instruction!;
			if (instruction.Operation == M68kMachineOperation.Constant &&
				TryGetBooleanConstant(instruction.SourceInstruction, out var constant))
			{
				resolution = new BooleanResolution(
					constant,
					null,
					null,
					false,
					[instruction]);
				return true;
			}

			if ((instruction.Operation is
					M68kMachineOperation.Copy or
					M68kMachineOperation.Convert) &&
				instruction.Uses.Length == 1 &&
				useCounts.GetValueOrDefault(value) == 1 &&
				TryResolveBoolean(
					instruction.Uses[0],
					definitions,
					useCounts,
					method,
					module,
					visiting,
					out var forwarded))
			{
				var removable = new HashSet<M68kMachineInstruction>(
					forwarded.Removable)
				{
					instruction
				};
				resolution = forwarded with
				{
					Removable = removable,
					Leaf = forwarded.Leaf is { } leaf
						? leaf with { Removable = removable }
						: null
				};
				return true;
			}

			if (instruction.Operation == M68kMachineOperation.Compare &&
				instruction.Uses.Length == 2 &&
				useCounts.GetValueOrDefault(value) == 1)
			{
				if (instruction.SourceInstruction?.OpCode == OpCodes.Ceq &&
					TryGetZeroOperand(
						instruction,
						definitions,
						out var booleanOperand,
						out var zeroConstant) &&
					TryResolveBoolean(
						booleanOperand,
						definitions,
						useCounts,
						method,
						module,
						visiting,
						out var inverted) &&
					inverted.Phi is null)
				{
					var invertedRemovable = new HashSet<M68kMachineInstruction>(
						inverted.Removable)
					{
						instruction,
						zeroConstant
					};
					var invertedResolution = inverted.Invert();
					resolution = invertedResolution with
					{
						Removable = invertedRemovable,
						Leaf = invertedResolution.Leaf is { } invertedLeaf
							? invertedLeaf with { Removable = invertedRemovable }
							: null
					};
					return true;
				}

				var condition = new M68kMachineBranchCondition(
					M68kMachineConditionSourceKind.Compare,
					ComparisonCondition(instruction.SourceInstruction!.OpCode),
					instruction.SourceInstruction);
				var removable = new HashSet<M68kMachineInstruction> { instruction };
				var leaf = new ConditionLeaf(
					definition.Block,
					instruction,
					condition,
					removable);
				resolution = new BooleanResolution(
					null,
					leaf,
					null,
					false,
					removable);
				return true;
			}

			if (instruction.Operation == M68kMachineOperation.Call &&
				instruction.Uses.Length == 1 &&
				useCounts.GetValueOrDefault(value) == 1 &&
				TryGetPredicateCondition(
					method,
					module,
					instruction,
					out var predicateCondition))
			{
				var condition = new M68kMachineBranchCondition(
					M68kMachineConditionSourceKind.Predicate,
					predicateCondition,
					instruction.SourceInstruction);
				var removable = new HashSet<M68kMachineInstruction> { instruction };
				var leaf = new ConditionLeaf(
					definition.Block,
					instruction,
					condition,
					removable);
				resolution = new BooleanResolution(
					null,
					leaf,
					null,
					false,
					removable);
				return true;
			}
			return false;
		}
		finally
		{
			visiting.Remove(value);
		}
	}

	private static bool IsPureBooleanRoutingBlock(
		M68kMachineBlock block,
		M68kMachineInstruction branch,
		IReadOnlySet<M68kMachineInstruction> removable)
	{
		foreach (var instruction in block.Instructions)
		{
			if (instruction == branch || removable.Contains(instruction))
			{
				continue;
			}
			return false;
		}
		return true;
	}

	private static bool CanRemoveForBranch(
		M68kMachineBlock block,
		M68kMachineInstruction? branch,
		IReadOnlySet<M68kMachineInstruction> removable,
		IReadOnlyDictionary<int, int> useCounts)
	{
		if (branch is null)
		{
			return false;
		}
		var first = block.Instructions.FindIndex(removable.Contains);
		var last = block.Instructions.IndexOf(branch);
		if (first < 0 || last < first)
		{
			return false;
		}
		for (var index = first; index < last; index++)
		{
			if (!removable.Contains(block.Instructions[index]))
			{
				return false;
			}
		}
		return removable.All(instruction =>
			instruction.Definitions.All(definition =>
				useCounts.GetValueOrDefault(definition) == 1));
	}

	private static M68kMachineInstruction CreateFusedBranch(
		M68kMachineInstruction branch,
		ConditionLeaf leaf,
		M68kCondition condition) =>
		branch with
		{
			Uses = leaf.Producer.Uses,
			Clobbers = leaf.Producer.Clobbers,
			MemoryEffect = leaf.Producer.MemoryEffect,
			IsSafepoint = leaf.Producer.IsSafepoint,
			MayThrow = leaf.Producer.MayThrow,
			ProducesConditionCodes = false,
			ConsumesConditionCodes = false,
			BranchCondition = leaf.Condition with { Condition = condition }
		};

	private static M68kMachineInstruction CreateFusedBranch(
		M68kMachineFunction function,
		M68kMachineInstruction branch,
		ConditionLeaf leaf,
		M68kCondition condition) =>
		function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			branch.IlOffset,
			uses: leaf.Producer.Uses,
			clobbers: leaf.Producer.Clobbers,
			memoryEffect: leaf.Producer.MemoryEffect,
			isSafepoint: leaf.Producer.IsSafepoint,
			mayThrow: leaf.Producer.MayThrow,
			sourceInstruction: branch.SourceInstruction,
			branchCondition: leaf.Condition with { Condition = condition });

	private static void RemoveTerminator(M68kMachineBlock block)
	{
		if (block.Instructions.LastOrDefault()?.Operation ==
			M68kMachineOperation.Branch)
		{
			block.Instructions.RemoveAt(block.Instructions.Count - 1);
		}
	}

	private static void RedirectToConstantTarget(
		M68kMachineFunction function,
		M68kMachineBlock merge,
		M68kMachineBlock predecessor,
		int targetId,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks)
	{
		predecessor.Successors.Clear();
		predecessor.Successors.Add(targetId);
		merge.Predecessors.Remove(predecessor.Id);
		var target = blocks[targetId];
		if (!target.Predecessors.Contains(predecessor.Id))
		{
			target.Predecessors.Add(predecessor.Id);
		}
		ComposePhiInputs(function, merge, predecessor, target);
	}

	private static void RedirectToConditionalTargets(
		M68kMachineFunction function,
		M68kMachineBlock merge,
		M68kMachineBlock predecessor,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks)
	{
		predecessor.Successors.Clear();
		foreach (var targetId in merge.Successors)
		{
			predecessor.Successors.Add(targetId);
			var target = blocks[targetId];
			if (!target.Predecessors.Contains(predecessor.Id))
			{
				target.Predecessors.Add(predecessor.Id);
			}
			ComposePhiInputs(function, merge, predecessor, target);
		}
		merge.Predecessors.Remove(predecessor.Id);
	}

	private static bool CanRedirectSuccessorPhis(
		M68kMachineBlock merge,
		IReadOnlyDictionary<int, BooleanResolution> incoming,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks)
	{
		foreach (var predecessorId in incoming.Keys)
		{
			var predecessor = blocks[predecessorId];
			if (merge.Successors.Any(target =>
				predecessor.Successors.Contains(target)))
			{
				return false;
			}
		}
		return true;
	}

	private static void ComposePhiInputs(
		M68kMachineFunction function,
		M68kMachineBlock merge,
		M68kMachineBlock predecessor,
		M68kMachineBlock target)
	{
		var mergePhis = merge.Phis.ToDictionary(static phi => phi.Definition);
		for (var index = 0; index < target.Phis.Count; index++)
		{
			var phi = target.Phis[index];
			if (!phi.Inputs.TryGetValue(merge.Id, out var value))
			{
				throw new InvalidOperationException(
					$"Target phi v{phi.Definition} has no input from bypassed block {merge.Id} in '{function.DisplayName}'.");
			}
			if (mergePhis.TryGetValue(value, out var mergePhi))
			{
				value = mergePhi.Inputs[predecessor.Id];
			}
			var inputs = phi.Inputs.ToDictionary(
				static input => input.Key,
				static input => input.Value);
			inputs[predecessor.Id] = value;
			target.Phis[index] = phi with { Inputs = inputs };
		}
	}

	private static void RemovePhiInput(M68kMachineBlock block, int predecessor)
	{
		for (var index = 0; index < block.Phis.Count; index++)
		{
			var phi = block.Phis[index];
			if (!phi.Inputs.ContainsKey(predecessor))
			{
				continue;
			}
			var inputs = phi.Inputs.ToDictionary(
				static input => input.Key,
				static input => input.Value);
			inputs.Remove(predecessor);
			block.Phis[index] = phi with { Inputs = inputs };
		}
	}

	private static Dictionary<int, Definition> BuildDefinitions(
		M68kMachineFunction function)
	{
		var result = new Dictionary<int, Definition>();
		foreach (var block in function.Blocks)
		{
			foreach (var phi in block.Phis)
			{
				result.Add(phi.Definition, new Definition(block, null, phi));
			}
			foreach (var instruction in block.Instructions)
			{
				foreach (var definition in instruction.Definitions)
				{
					result.Add(
						definition,
						new Definition(block, instruction, null));
				}
			}
		}
		return result;
	}

	private static Dictionary<int, int> BuildUseCounts(
		M68kMachineFunction function)
	{
		var result = new Dictionary<int, int>();
		foreach (var value in function.Blocks.SelectMany(block =>
			block.Instructions.SelectMany(static instruction => instruction.Uses)
				.Concat(block.Phis.SelectMany(static phi => phi.Inputs.Values))))
		{
			result[value] = result.GetValueOrDefault(value) + 1;
		}
		return result;
	}

	private static bool TryGetZeroOperand(
		M68kMachineInstruction comparison,
		IReadOnlyDictionary<int, Definition> definitions,
		out int booleanOperand,
		out M68kMachineInstruction zeroConstant)
	{
		for (var index = 0; index < 2; index++)
		{
			var candidate = comparison.Uses[index];
			if (definitions.TryGetValue(candidate, out var definition) &&
				definition.Instruction is { } instruction &&
				TryGetBooleanConstant(instruction.SourceInstruction, out var value) &&
				!value)
			{
				booleanOperand = comparison.Uses[1 - index];
				zeroConstant = instruction;
				return true;
			}
		}
		booleanOperand = 0;
		zeroConstant = null!;
		return false;
	}

	private static bool TryGetBooleanConstant(
		CilInstruction? instruction,
		out bool value)
	{
		value = false;
		if (instruction is null)
		{
			return false;
		}
		if (instruction.OpCode == OpCodes.Ldc_I4_0)
		{
			return true;
		}
		if (instruction.OpCode == OpCodes.Ldc_I4_1)
		{
			value = true;
			return true;
		}
		if (instruction.OpCode == OpCodes.Ldc_I4 &&
			instruction.Operand is int constant &&
			constant is 0 or 1)
		{
			value = constant != 0;
			return true;
		}
		if (instruction.OpCode == OpCodes.Ldc_I4_S)
		{
			var shortConstant = Convert.ToInt32(instruction.Operand);
			if (shortConstant is 0 or 1)
			{
				value = shortConstant != 0;
				return true;
			}
		}
		return false;
	}

	private static bool TryGetPredicateCondition(
		CilMethod method,
		CompilationModule module,
		M68kMachineInstruction instruction,
		out M68kCondition condition)
	{
		condition = default;
		if (instruction.SourceInstruction is not
			{ Operand: int token } source)
		{
			return false;
		}
		var target = module.ResolveMethodToken(token, method, source.Offset);
		if (target.ImportName == "intrinsic:aptr-is-null")
		{
			condition = M68kCondition.Equal;
			return true;
		}
		if (target.ImportName == "intrinsic:aptr-is-not-null" ||
			target.ImportName?.StartsWith(
				"intrinsic:nullable-has-value:",
				StringComparison.Ordinal) == true)
		{
			condition = M68kCondition.NotEqual;
			return true;
		}
		return false;
	}

	private static bool IsValueBranch(M68kMachineInstruction instruction) =>
		instruction.Operation == M68kMachineOperation.ConditionalBranch &&
		instruction.Uses.Length == 1 &&
		instruction.SourceInstruction is { OpCode: var op } &&
		(op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ||
		 op == OpCodes.Brfalse || op == OpCodes.Brfalse_S);

	private static M68kCondition ApplyBranchPolarity(
		M68kCondition condition,
		bool inverted,
		OpCode branch) =>
		inverted ^ IsBranchFalse(branch)
			? InvertCondition(condition)
			: condition;

	private static bool IsBranchFalse(OpCode op) =>
		op == OpCodes.Brfalse || op == OpCodes.Brfalse_S;

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

	private static M68kCondition InvertCondition(M68kCondition condition) =>
		(M68kCondition)((int)condition ^ 1);

	private static bool IsExceptionBoundary(CilMethod method, int offset) =>
		method.ExceptionRegions.Any(region =>
			region.TryOffset == offset ||
			region.TryEnd == offset ||
			region.HandlerOffset == offset ||
			region.HandlerEnd == offset ||
			region.FilterOffset == offset);

	private static bool HasSameProtectedState(
		CilMethod method,
		int leftOffset,
		int rightOffset) =>
		method.ExceptionRegions
			.Select(static region => (region.TryOffset, region.TryEnd))
			.Distinct()
			.All(range =>
				(range.TryOffset <= leftOffset && leftOffset < range.TryEnd) ==
				(range.TryOffset <= rightOffset && rightOffset < range.TryEnd));
}
