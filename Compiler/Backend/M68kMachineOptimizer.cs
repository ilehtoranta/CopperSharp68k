/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kMachineOptimizationStatistics(
	int Rounds,
	int CopiesRemoved,
	int PhisRemoved,
	int ConstantsFolded,
	int CommonExpressionsRemoved,
	int BranchesFolded,
	int BlocksRemoved,
	int InstructionsRemoved,
	int LoadsForwarded,
	int StoresRemoved,
	int LoopInstructionsHoisted,
	bool ReachedRoundLimit);

/// <summary>
/// Target-independent SSA cleanup that runs before provenance tracking and
/// register allocation. Every rewrite is deliberately independent of physical
/// condition-code and register choices; those remain the responsibility of the
/// allocated emitter and post-emission optimizer.
/// </summary>
internal static class M68kMachineOptimizer
{
	private const int MaximumRounds = 16;

	internal sealed class MutableStatistics
	{
		public int CopiesRemoved;
		public int PhisRemoved;
		public int ConstantsFolded;
		public int CommonExpressionsRemoved;
		public int BranchesFolded;
		public int BlocksRemoved;
		public int InstructionsRemoved;
		public int LoadsForwarded;
		public int StoresRemoved;
		public int LoopInstructionsHoisted;
	}

	private readonly record struct DefinitionSite(
		M68kMachineBlock Block,
		int InstructionIndex,
		M68kMachineInstruction Instruction);

	private readonly record struct ExpressionKey(
		M68kMachineOperation Operation,
		short SourceOpCode,
		string Uses,
		int? Immediate);

	public static M68kMachineOptimizationStatistics Run(
		M68kMachineFunction function,
		M68kCpuTarget cpu)
	{
		ArgumentNullException.ThrowIfNull(function);
		_ = cpu;
		M68kMachineIrVerifier.Verify(function);
		var statistics = new MutableStatistics();
		var rounds = 0;
		var changed = false;
		do
		{
			rounds++;
			changed = SeedExplicitConstants(function);
			changed |= CanonicalizeScalarZeroComparisons(function);
			if (function.Blocks.All(static block => block.LoopDepth == 0))
			{
				changed |= SimplifyCopiesAndPhis(function, statistics);
			}
			changed |= FoldConstantsAndIdentities(function, statistics);
			changed |= FoldConstantBranches(function, statistics);
			changed |= RemoveUnreachableBlocks(function, statistics);
			changed |= CleanControlFlow(function, statistics);
			changed |= EliminateCommonExpressions(function, statistics);
			if (function.Blocks.All(static block => block.LoopDepth == 0))
			{
				changed |= M68kMachineMemoryAnalysis.Optimize(
					function,
					statistics);
			}
			changed |= RemoveDeadInstructions(function, statistics);
			RemoveUnreferencedValues(function);
		}
		while (changed && rounds < MaximumRounds);

		statistics.LoopInstructionsHoisted +=
			M68kMachineLoopOptimizer.HoistLoopInvariants(function);
		if (statistics.LoopInstructionsHoisted != 0)
		{
			RemoveDeadInstructions(function, statistics);
			RemoveUnreferencedValues(function);
		}
		M68kControlFlowAnalysis.ComputeLoopDepths(function);
		M68kMachineIrVerifier.Verify(function);
		return new M68kMachineOptimizationStatistics(
			rounds,
			statistics.CopiesRemoved,
			statistics.PhisRemoved,
			statistics.ConstantsFolded,
			statistics.CommonExpressionsRemoved,
			statistics.BranchesFolded,
			statistics.BlocksRemoved,
			statistics.InstructionsRemoved,
			statistics.LoadsForwarded,
			statistics.StoresRemoved,
			statistics.LoopInstructionsHoisted,
			changed && rounds == MaximumRounds);
	}

	private static bool CanonicalizeScalarZeroComparisons(
		M68kMachineFunction function)
	{
		var definitions = function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.Definitions.Length == 1)
			.ToDictionary(static instruction => instruction.Definitions[0]);
		var changed = false;
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Operation != M68kMachineOperation.Compare ||
					instruction.Uses is not [var left, var right] ||
					instruction.Immediate is not null ||
					function.Values[left].Width == M68kMachineValueWidth.LongPair ||
					!definitions.TryGetValue(right, out var definition) ||
					definition.Operation != M68kMachineOperation.Constant ||
					definition.ConstantValue is not { } constant ||
					!constant.TryGetIntegral(out var value) ||
					value != 0)
				{
					continue;
				}

				// CMP #0 and TST establish the same N/Z/V/C state for every
				// scalar comparison condition.  Record the implicit zero in the
				// machine instruction so a comparison-only constant can die before
				// allocation instead of evicting a live incoming register value.
				block.Instructions[index] = instruction with
				{
					Uses = ImmutableArray.Create(left),
					Immediate = 0
				};
				changed = true;
			}
		}
		return changed;
	}

	private static bool SeedExplicitConstants(M68kMachineFunction function)
	{
		var changed = false;
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Operation != M68kMachineOperation.Constant ||
					instruction.ConstantValue is not null ||
					instruction.SourceInstruction is not { } source ||
					instruction.Definitions.Length != 1)
				{
					continue;
				}
				var boolean = function.Values[instruction.Definitions[0]].Kind ==
					CilStackValueKind.BooleanByte;
				if (!M68kMachineConstant.TryFromCil(
						source,
						boolean,
						out var constant))
				{
					continue;
				}
				block.Instructions[index] = instruction with
				{
					ConstantValue = constant
				};
				changed = true;
			}
		}
		return changed;
	}

	private static bool SimplifyCopiesAndPhis(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var liveness = M68kLivenessAnalysis.AnalyzeInstructions(
			function,
			M68kLivenessAnalysis.Analyze(function));
		var substitutions = new Dictionary<int, int>();
		foreach (var block in function.Blocks)
		{
			foreach (var instruction in block.Instructions)
			{
				if (instruction is not
					{
						Operation: M68kMachineOperation.Copy,
						Uses: [var source],
						Definitions: [var destination],
						AllowCopyCoalescing: true
					} ||
					liveness.LiveAfter[instruction.Id].Contains(source) ||
					!CanSubstitute(function, destination, source))
				{
					continue;
				}
				substitutions[destination] = Resolve(substitutions, source);
			}

			foreach (var phi in block.Phis)
			{
				var candidates = phi.Inputs.Values
					.Select(value => Resolve(substitutions, value))
					.Where(value => value != phi.Definition)
					.Distinct()
					.ToArray();
				if (candidates is [var candidate] &&
					CanSubstitute(function, phi.Definition, candidate))
				{
					substitutions[phi.Definition] = candidate;
				}
			}
		}
		if (substitutions.Count == 0)
		{
			return false;
		}
		ApplySubstitutions(function, substitutions);
		foreach (var block in function.Blocks)
		{
			statistics.CopiesRemoved += block.Instructions.RemoveAll(instruction =>
				instruction.Operation == M68kMachineOperation.Copy &&
				instruction.Definitions.Length == 1 &&
				substitutions.ContainsKey(instruction.Definitions[0]));
			statistics.PhisRemoved += block.Phis.RemoveAll(phi =>
				substitutions.ContainsKey(phi.Definition));
		}
		return true;
	}

	private static bool FoldConstantsAndIdentities(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var constants = BuildConstantMap(function);
		var changed = false;
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (TryEvaluate(function, instruction, constants, out var folded))
				{
					block.Instructions[index] = AsConstant(instruction, folded);
					constants[instruction.Definitions[0]] = folded;
					statistics.ConstantsFolded++;
					changed = true;
					continue;
				}

				if (TrySimplifyIdentity(
						function,
						instruction,
						constants,
						out var replacement,
						out var replacementConstant))
				{
					if (replacement is { } source &&
						!CanSubstitute(function, instruction.Definitions[0], source))
					{
						continue;
					}
					block.Instructions[index] = replacementConstant is { } constant
						? AsConstant(instruction, constant)
						: AsCopy(instruction, replacement!.Value);
					if (replacementConstant is { } known)
					{
						constants[instruction.Definitions[0]] = known;
					}
					statistics.ConstantsFolded++;
					changed = true;
				}
			}

			for (var index = block.Phis.Count - 1; index >= 0; index--)
			{
				var phi = block.Phis[index];
				var values = phi.Inputs.Values
					.Where(value => value != phi.Definition)
					.Select(value => constants.TryGetValue(value, out var constant)
						? (M68kMachineConstant?)constant
						: null)
					.ToArray();
				if (values.Length == 0 || values.Any(static value => value is null) ||
					values.Select(static value => value!.Value).Distinct().Count() != 1)
				{
					continue;
				}
				var constant = values[0]!.Value;
				block.Phis.RemoveAt(index);
				block.Instructions.Insert(
					0,
					function.CreateInstruction(
						M68kMachineOperation.Constant,
						block.StartIlOffset,
						definitions: [phi.Definition],
						sourceInstruction: ConstantInstruction(
							constant,
							block.StartIlOffset),
						constantValue: constant));
				constants[phi.Definition] = constant;
				statistics.PhisRemoved++;
				statistics.ConstantsFolded++;
				changed = true;
			}
		}
		return changed;
	}

	private static bool FoldConstantBranches(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var constants = BuildConstantMap(function);
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var changed = false;
		foreach (var block in function.Blocks)
		{
			if (block.Instructions.LastOrDefault() is not { } branch)
			{
				continue;
			}
			if (branch.Operation == M68kMachineOperation.Switch &&
				TryEvaluateSwitch(
					branch,
					constants,
					blocks,
					out var switchTarget))
			{
				foreach (var removedSuccessor in block.Successors
					.Where(id => id != switchTarget).ToArray())
				{
					RemovePredecessor(blocks[removedSuccessor], block.Id);
				}
				block.Successors.Clear();
				block.Successors.Add(switchTarget);
				block.Instructions[^1] = AsBranch(
					branch,
					blocks[switchTarget].StartIlOffset);
				statistics.BranchesFolded++;
				changed = true;
				continue;
			}
			if (branch.Operation != M68kMachineOperation.ConditionalBranch ||
				block.Successors.Count != 2 ||
				!TryEvaluateBranch(branch, constants, out var takeTarget))
			{
				continue;
			}
			var retainedId = block.Successors[takeTarget ? 0 : 1];
			var removedId = block.Successors[takeTarget ? 1 : 0];
			RemovePredecessor(blocks[removedId], block.Id);
			block.Successors.Clear();
			block.Successors.Add(retainedId);
			block.Instructions[^1] = AsBranch(
				branch,
				blocks[retainedId].StartIlOffset);
			statistics.BranchesFolded++;
			changed = true;
		}
		return changed;
	}

	private static M68kMachineInstruction AsBranch(
		M68kMachineInstruction instruction,
		int targetOffset) =>
		instruction with
		{
			Operation = M68kMachineOperation.Branch,
			Uses = ImmutableArray<int>.Empty,
			Clobbers = M68kRegisterSet.None,
			MemoryEffect = M68kMachineMemoryEffect.None,
			IsSafepoint = false,
			MayThrow = false,
			ProducesConditionCodes = false,
			ConsumesConditionCodes = false,
			SourceInstruction = new CilInstruction(
				instruction.IlOffset,
				OpCodes.Br,
				targetOffset,
				instruction.SourceInstruction?.NextOffset ?? instruction.IlOffset + 1),
			BranchCondition = null,
			LogicalCall = null
		};

	private static bool RemoveUnreachableBlocks(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var removed = M68kControlFlowCleanup.RemoveUnreachableBlocks(function);
		statistics.BlocksRemoved += removed;
		return removed != 0;
	}

	private static bool EliminateCommonExpressions(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var dominators = M68kControlFlowAnalysis.ComputeDominators(function);
		var expressions = new Dictionary<ExpressionKey, List<DefinitionSite>>();
		var substitutions = new Dictionary<int, int>();
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (!TryCreateExpressionKey(instruction, substitutions, out var key))
				{
					continue;
				}
				var destination = instruction.Definitions[0];
				if (expressions.TryGetValue(key, out var candidates))
				{
					var dominating = candidates.LastOrDefault(candidate =>
						dominators[block.Id].Contains(candidate.Block.Id) &&
						(candidate.Block.Id != block.Id || candidate.InstructionIndex < index));
					if (dominating.Instruction is not null &&
						CanSubstitute(
							function,
							destination,
							dominating.Instruction.Definitions[0]))
					{
						substitutions[destination] =
							dominating.Instruction.Definitions[0];
						continue;
					}
				}
				else
				{
					candidates = [];
					expressions.Add(key, candidates);
				}
				candidates.Add(new DefinitionSite(block, index, instruction));
			}
		}
		if (substitutions.Count == 0)
		{
			return false;
		}
		ApplySubstitutions(function, substitutions);
		foreach (var block in function.Blocks)
		{
			statistics.CommonExpressionsRemoved += block.Instructions.RemoveAll(instruction =>
				instruction.Definitions.Length == 1 &&
				substitutions.ContainsKey(instruction.Definitions[0]));
		}
		return true;
	}

	private static bool CleanControlFlow(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var changed = false;
		bool rewritten;
		do
		{
			rewritten = FoldRedundantConditionalBranch(function, statistics) ||
				ThreadEmptyBlock(function, statistics) ||
				MergeLinearBlock(function, statistics);
			changed |= rewritten;
		}
		while (rewritten);
		return changed;
	}

	private static bool FoldRedundantConditionalBranch(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		foreach (var block in function.Blocks.OrderBy(static block => block.Id))
		{
			if (block.Instructions.LastOrDefault() is not
				{
					Operation: M68kMachineOperation.ConditionalBranch
				} branch ||
				block.Successors.Distinct().ToArray() is not [var targetId])
			{
				continue;
			}
			var target = function.Blocks.Single(candidate => candidate.Id == targetId);
			block.Instructions[^1] = AsBranch(branch, target.StartIlOffset);
			statistics.BranchesFolded++;
			function.SynchronizeNormalEdges();
			return true;
		}
		return false;
	}

	private static bool ThreadEmptyBlock(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		foreach (var block in function.Blocks.OrderBy(static block => block.Id))
		{
			if (block.Id == function.EntryBlockId || block.IsExceptionEntry ||
				block.Phis.Count != 0 || block.Instructions is not
					[{ Operation: M68kMachineOperation.Branch }] ||
				block.Successors is not [var targetId] ||
				block.Predecessors.Count < 2 ||
				HasExceptionalEdges(block) || IsRegionOwned(function, block.Id) ||
				!blocks.TryGetValue(targetId, out var target) ||
				HasExceptionalIncomingEdges(target) ||
				!block.ActiveExceptionRegionIds.SequenceEqual(
					target.ActiveExceptionRegionIds))
			{
				continue;
			}
			var predecessors = block.Predecessors
				.Distinct()
				.Select(id => blocks[id])
				.OrderBy(static predecessor => predecessor.Id)
				.ToArray();
			if (predecessors.Any(predecessor =>
				predecessor.Successors is not [var successor] || successor != block.Id ||
				predecessor.Instructions.LastOrDefault() is not
					{ Operation: M68kMachineOperation.Branch } ||
				!predecessor.ActiveExceptionRegionIds.SequenceEqual(
					block.ActiveExceptionRegionIds) ||
				target.Predecessors.Any(id => id != block.Id && id == predecessor.Id)))
			{
				continue;
			}

			for (var index = 0; index < target.Phis.Count; index++)
			{
				var phi = target.Phis[index];
				if (!phi.Inputs.TryGetValue(block.Id, out var input))
				{
					throw new InvalidOperationException(
						$"{function.DisplayName}: missing phi input while threading block {block.Id}.");
				}
				target.Phis[index] = phi with
				{
					Inputs = phi.Inputs
						.Where(item => item.Key != block.Id)
						.Concat(predecessors.Select(predecessor =>
							new KeyValuePair<int, int>(predecessor.Id, input)))
						.ToDictionary(static item => item.Key, static item => item.Value)
				};
			}
			foreach (var predecessor in predecessors)
			{
				predecessor.Successors[0] = target.Id;
				predecessor.Instructions[^1] = AsBranch(
					predecessor.Instructions[^1],
					target.StartIlOffset);
			}
			target.Predecessors.Remove(block.Id);
			target.Predecessors.AddRange(predecessors.Select(static predecessor =>
				predecessor.Id));
			function.RemoveBlocks(new HashSet<int> { block.Id });
			statistics.BlocksRemoved++;
			return true;
		}
		return false;
	}

	private static bool MergeLinearBlock(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		foreach (var predecessor in function.Blocks.OrderBy(static block => block.Id))
		{
			if (predecessor.Successors is not [var targetId] ||
				predecessor.Instructions.LastOrDefault() is not
					{ Operation: M68kMachineOperation.Branch } ||
				HasExceptionalEdges(predecessor) || IsRegionOwned(function, predecessor.Id) ||
				!blocks.TryGetValue(targetId, out var target) ||
				target.Id == function.EntryBlockId || target.IsExceptionEntry ||
				target.Predecessors is not [var predecessorId] ||
				predecessorId != predecessor.Id || HasExceptionalEdges(target) ||
				IsRegionOwned(function, target.Id) ||
				!predecessor.ActiveExceptionRegionIds.SequenceEqual(
					target.ActiveExceptionRegionIds))
			{
				continue;
			}

			var substitutions = new Dictionary<int, int>();
			foreach (var phi in target.Phis)
			{
				if (!phi.Inputs.TryGetValue(predecessor.Id, out var input) ||
					!CanSubstitute(function, phi.Definition, input))
				{
					substitutions.Clear();
					break;
				}
				substitutions.Add(phi.Definition, input);
			}
			if (target.Phis.Count != substitutions.Count)
			{
				continue;
			}
			ApplySubstitutions(function, substitutions);
			statistics.PhisRemoved += target.Phis.Count;
			predecessor.Instructions.RemoveAt(predecessor.Instructions.Count - 1);
			predecessor.Instructions.AddRange(target.Instructions);
			predecessor.Successors.Clear();
			predecessor.Successors.AddRange(target.Successors);
			foreach (var successorId in target.Successors)
			{
				var successor = blocks[successorId];
				for (var index = 0; index < successor.Phis.Count; index++)
				{
					var phi = successor.Phis[index];
					if (!phi.Inputs.TryGetValue(target.Id, out var input))
					{
						continue;
					}
					successor.Phis[index] = phi with
					{
						Inputs = phi.Inputs
							.Where(item => item.Key != target.Id)
							.Append(new KeyValuePair<int, int>(predecessor.Id, input))
							.ToDictionary(static item => item.Key, static item => item.Value)
					};
				}
				successor.Predecessors.Remove(target.Id);
				if (!successor.Predecessors.Contains(predecessor.Id))
				{
					successor.Predecessors.Add(predecessor.Id);
				}
			}
			function.RemoveBlocks(new HashSet<int> { target.Id });
			statistics.BlocksRemoved++;
			return true;
		}
		return false;
	}

	private static bool HasExceptionalEdges(M68kMachineBlock block) =>
		block.SuccessorEdges.Any(static edge => edge.Kind != M68kMachineEdgeKind.Normal) ||
		HasExceptionalIncomingEdges(block);

	private static bool HasExceptionalIncomingEdges(M68kMachineBlock block) =>
		block.PredecessorEdges.Any(static edge => edge.Kind != M68kMachineEdgeKind.Normal);

	private static bool IsRegionOwned(M68kMachineFunction function, int blockId) =>
		function.ExceptionRegions.Any(region =>
			region.TryBlockIds.Contains(blockId) ||
			region.HandlerBlockIds.Contains(blockId));

	private static bool RemoveDeadInstructions(
		M68kMachineFunction function,
		MutableStatistics statistics)
	{
		var changed = false;
		bool removed;
		do
		{
			var used = function.Blocks.SelectMany(block =>
				block.Instructions.SelectMany(static instruction => instruction.Uses)
					.Concat(block.Phis.SelectMany(static phi => phi.Inputs.Values)))
				.ToHashSet();
			removed = false;
			foreach (var block in function.Blocks)
			{
				var count = block.Instructions.RemoveAll(instruction =>
					instruction.Definitions.Length != 0 &&
					instruction.Definitions.All(definition => !used.Contains(definition)) &&
					IsDeadRemovable(instruction));
				statistics.InstructionsRemoved += count;
				removed |= count != 0;
			}
			changed |= removed;
		}
		while (removed);
		return changed;
	}

	private static bool TryEvaluate(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, M68kMachineConstant> constants,
		out M68kMachineConstant result)
	{
		result = default;
		if (instruction.Definitions.Length != 1 || instruction.Uses.Length is < 1 or > 2 ||
			instruction.Uses.Any(use => !constants.ContainsKey(use)) ||
			instruction.Operation is not (
				M68kMachineOperation.Add or
				M68kMachineOperation.Subtract or
				M68kMachineOperation.Multiply or
				M68kMachineOperation.Divide or
				M68kMachineOperation.Remainder or
				M68kMachineOperation.And or
				M68kMachineOperation.Or or
				M68kMachineOperation.Xor or
				M68kMachineOperation.Negate or
				M68kMachineOperation.Not or
				M68kMachineOperation.Shift or
				M68kMachineOperation.Compare or
				M68kMachineOperation.Convert))
		{
			return false;
		}
		if (!constants[instruction.Uses[0]].TryGetIntegral(out var left) ||
			instruction.Uses.Length == 2 &&
			!constants[instruction.Uses[1]].TryGetIntegral(out _))
		{
			return false;
		}
		var right = instruction.Uses.Length == 2
			? Integral(constants[instruction.Uses[1]])
			: 0;
		var value = function.Values[instruction.Definitions[0]];
		var is64 = value.Width == M68kMachineValueWidth.LongPair;
		// Widening and narrowing conversions have different source and destination
		// widths. Evaluate the conversion in the source domain so conv.u8 zero-
		// extends an Int32 constant instead of first sign-extending it to Int64.
		var evaluationIs64 = instruction.Operation == M68kMachineOperation.Convert
			? function.Values[instruction.Uses[0]].Width ==
				M68kMachineValueWidth.LongPair
			: is64;
		var op = instruction.SourceInstruction?.OpCode;
		try
		{
			long folded;
			if (evaluationIs64)
			{
				folded = EvaluateInt64(instruction.Operation, op, left, right);
			}
			else
			{
				folded = EvaluateInt32(
					instruction.Operation,
					op,
					unchecked((int)left),
					unchecked((int)right));
			}
			result = value.Kind == CilStackValueKind.BooleanByte
				? M68kMachineConstant.Boolean(folded != 0)
				: is64
					? M68kMachineConstant.Int64(folded)
					: M68kMachineConstant.Int32(unchecked((int)folded));
			return true;
		}
		catch (Exception exception) when (
			exception is DivideByZeroException or OverflowException or InvalidOperationException)
		{
			return false;
		}
	}

	private static long EvaluateInt32(
		M68kMachineOperation operation,
		OpCode? op,
		int left,
		int right) =>
		operation switch
		{
			M68kMachineOperation.Add when op == OpCodes.Add_Ovf => checked(left + right),
			M68kMachineOperation.Add when op == OpCodes.Add_Ovf_Un =>
				unchecked((int)checked((uint)left + (uint)right)),
			M68kMachineOperation.Add => unchecked(left + right),
			M68kMachineOperation.Subtract when op == OpCodes.Sub_Ovf =>
				checked(left - right),
			M68kMachineOperation.Subtract when op == OpCodes.Sub_Ovf_Un =>
				unchecked((int)checked((uint)left - (uint)right)),
			M68kMachineOperation.Subtract => unchecked(left - right),
			M68kMachineOperation.Multiply when op == OpCodes.Mul_Ovf =>
				checked(left * right),
			M68kMachineOperation.Multiply when op == OpCodes.Mul_Ovf_Un =>
				checked((int)((uint)left * (ulong)(uint)right)),
			M68kMachineOperation.Multiply => unchecked(left * right),
			M68kMachineOperation.Divide when op == OpCodes.Div_Un =>
				right == 0 ? throw new DivideByZeroException() : (uint)left / (uint)right,
			M68kMachineOperation.Divide =>
				left == int.MinValue && right == -1
					? throw new OverflowException()
					: right == 0
						? throw new DivideByZeroException()
						: left / right,
			M68kMachineOperation.Remainder when op == OpCodes.Rem_Un =>
				right == 0 ? throw new DivideByZeroException() : (uint)left % (uint)right,
			M68kMachineOperation.Remainder =>
				right == 0 ? throw new DivideByZeroException() : left % right,
			M68kMachineOperation.And => left & right,
			M68kMachineOperation.Or => left | right,
			M68kMachineOperation.Xor => left ^ right,
			M68kMachineOperation.Negate => unchecked(-left),
			M68kMachineOperation.Not => ~left,
			M68kMachineOperation.Shift when op == OpCodes.Shl => left << (right & 31),
			M68kMachineOperation.Shift when op == OpCodes.Shr_Un =>
				(int)((uint)left >> (right & 31)),
			M68kMachineOperation.Shift => left >> (right & 31),
			M68kMachineOperation.Compare => CompareInt32(op, left, right) ? 1 : 0,
			M68kMachineOperation.Convert => ConvertInt32(op, left),
			_ => throw new InvalidOperationException()
		};

	private static long EvaluateInt64(
		M68kMachineOperation operation,
		OpCode? op,
		long left,
		long right) =>
		operation switch
		{
			M68kMachineOperation.Add when op == OpCodes.Add_Ovf => checked(left + right),
			M68kMachineOperation.Add when op == OpCodes.Add_Ovf_Un =>
				unchecked((long)checked((ulong)left + (ulong)right)),
			M68kMachineOperation.Add => unchecked(left + right),
			M68kMachineOperation.Subtract when op == OpCodes.Sub_Ovf =>
				checked(left - right),
			M68kMachineOperation.Subtract when op == OpCodes.Sub_Ovf_Un =>
				unchecked((long)checked((ulong)left - (ulong)right)),
			M68kMachineOperation.Subtract => unchecked(left - right),
			M68kMachineOperation.Multiply when op == OpCodes.Mul_Ovf =>
				checked(left * right),
			M68kMachineOperation.Multiply when op == OpCodes.Mul_Ovf_Un =>
				unchecked((long)checked((ulong)left * (ulong)right)),
			M68kMachineOperation.Multiply => unchecked(left * right),
			M68kMachineOperation.Divide when op == OpCodes.Div_Un =>
				right == 0 ? throw new DivideByZeroException() :
					(long)((ulong)left / (ulong)right),
			M68kMachineOperation.Divide =>
				left == long.MinValue && right == -1
					? throw new OverflowException()
					: right == 0
						? throw new DivideByZeroException()
						: left / right,
			M68kMachineOperation.Remainder when op == OpCodes.Rem_Un =>
				right == 0 ? throw new DivideByZeroException() :
					(long)((ulong)left % (ulong)right),
			M68kMachineOperation.Remainder =>
				right == 0 ? throw new DivideByZeroException() : left % right,
			M68kMachineOperation.And => left & right,
			M68kMachineOperation.Or => left | right,
			M68kMachineOperation.Xor => left ^ right,
			M68kMachineOperation.Negate => unchecked(-left),
			M68kMachineOperation.Not => ~left,
			M68kMachineOperation.Shift when op == OpCodes.Shl => left << ((int)right & 63),
			M68kMachineOperation.Shift when op == OpCodes.Shr_Un =>
				(long)((ulong)left >> ((int)right & 63)),
			M68kMachineOperation.Shift => left >> ((int)right & 63),
			M68kMachineOperation.Compare => CompareInt64(op, left, right) ? 1 : 0,
			M68kMachineOperation.Convert => ConvertInt64(op, left),
			_ => throw new InvalidOperationException()
		};

	private static bool CompareInt32(OpCode? op, int left, int right) =>
		op == OpCodes.Ceq ? left == right :
		op == OpCodes.Cgt ? left > right :
		op == OpCodes.Cgt_Un ? (uint)left > (uint)right :
		op == OpCodes.Clt ? left < right :
		op == OpCodes.Clt_Un && (uint)left < (uint)right;

	private static bool CompareInt64(OpCode? op, long left, long right) =>
		op == OpCodes.Ceq ? left == right :
		op == OpCodes.Cgt ? left > right :
		op == OpCodes.Cgt_Un ? (ulong)left > (ulong)right :
		op == OpCodes.Clt ? left < right :
		op == OpCodes.Clt_Un && (ulong)left < (ulong)right;

	private static long ConvertInt32(OpCode? op, int value) =>
		op == OpCodes.Conv_I1 ? (sbyte)value :
		op == OpCodes.Conv_U1 ? (byte)value :
		op == OpCodes.Conv_I2 ? (short)value :
		op == OpCodes.Conv_U2 ? (ushort)value :
		op == OpCodes.Conv_I8 ? value :
		op == OpCodes.Conv_U8 ? (long)(ulong)(uint)value :
		op is { } conversion &&
			(conversion == OpCodes.Conv_I4 || conversion == OpCodes.Conv_U4 ||
			 conversion == OpCodes.Conv_I || conversion == OpCodes.Conv_U)
			? value
			: throw new InvalidOperationException();

	private static long ConvertInt64(OpCode? op, long value) =>
		op == OpCodes.Conv_I1 ? (sbyte)value :
		op == OpCodes.Conv_U1 ? (byte)value :
		op == OpCodes.Conv_I2 ? (short)value :
		op == OpCodes.Conv_U2 ? (ushort)value :
		op == OpCodes.Conv_I4 ? (int)value :
		op == OpCodes.Conv_U4 ? (uint)value :
		op is { } conversion &&
			(conversion == OpCodes.Conv_I8 || conversion == OpCodes.Conv_U8 ||
			 conversion == OpCodes.Conv_I || conversion == OpCodes.Conv_U)
			? value
			: throw new InvalidOperationException();

	private static bool TrySimplifyIdentity(
		M68kMachineFunction function,
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, M68kMachineConstant> constants,
		out int? replacement,
		out M68kMachineConstant? replacementConstant)
	{
		replacement = null;
		replacementConstant = null;
		if (instruction.Definitions.Length != 1 || instruction.Uses.Length == 0 ||
			function.Values[instruction.Definitions[0]].Kind is
				CilStackValueKind.Float32 or CilStackValueKind.Float64)
		{
			return false;
		}
		var left = instruction.Uses[0];
		var right = instruction.Uses.Length > 1 ? instruction.Uses[1] : -1;
		var leftConstant = constants.TryGetValue(left, out var leftValue) &&
			leftValue.TryGetIntegral(out var leftIntegral)
				? leftIntegral
				: (long?)null;
		var rightConstant = right >= 0 && constants.TryGetValue(right, out var rightValue) &&
			rightValue.TryGetIntegral(out var rightIntegral)
				? rightIntegral
				: (long?)null;

		if ((instruction.Operation is M68kMachineOperation.Add or M68kMachineOperation.Or or
			 M68kMachineOperation.Xor) && rightConstant == 0 ||
			instruction.Operation == M68kMachineOperation.Subtract && rightConstant == 0 ||
			instruction.Operation == M68kMachineOperation.Multiply && rightConstant == 1 ||
			instruction.Operation == M68kMachineOperation.And && rightConstant == -1 ||
			instruction.Operation == M68kMachineOperation.Shift && rightConstant == 0)
		{
			replacement = left;
			return true;
		}
		if ((instruction.Operation is M68kMachineOperation.Add or M68kMachineOperation.Or or
			 M68kMachineOperation.Xor) && leftConstant == 0 ||
			instruction.Operation == M68kMachineOperation.Multiply && leftConstant == 1 ||
			instruction.Operation == M68kMachineOperation.And && leftConstant == -1)
		{
			replacement = right;
			return true;
		}
		if (right >= 0 && left == right && instruction.Operation is
			M68kMachineOperation.Subtract or M68kMachineOperation.Xor)
		{
			replacementConstant = ZeroFor(function.Values[instruction.Definitions[0]]);
			return true;
		}
		if (right >= 0 && left == right && instruction.Operation is
			M68kMachineOperation.And or M68kMachineOperation.Or)
		{
			replacement = left;
			return true;
		}
		if ((instruction.Operation is M68kMachineOperation.Multiply or
			 M68kMachineOperation.And) && (leftConstant == 0 || rightConstant == 0))
		{
			replacementConstant = ZeroFor(function.Values[instruction.Definitions[0]]);
			return true;
		}
		return false;
	}

	private static bool TryEvaluateBranch(
		M68kMachineInstruction branch,
		IReadOnlyDictionary<int, M68kMachineConstant> constants,
		out bool takeTarget)
	{
		takeTarget = false;
		if (branch.BranchCondition is null && branch.Uses is [var value] &&
			constants.TryGetValue(value, out var constant) &&
			constant.TryGetIntegral(out var integral) &&
			branch.SourceInstruction is { OpCode: var op } &&
			(op == OpCodes.Brtrue || op == OpCodes.Brtrue_S ||
			 op == OpCodes.Brfalse || op == OpCodes.Brfalse_S))
		{
			var truth = integral != 0;
			takeTarget = op == OpCodes.Brtrue || op == OpCodes.Brtrue_S
				? truth
				: !truth;
			return true;
		}
		if (branch.BranchCondition is not { } condition ||
			branch.Uses.Any(use => !constants.TryGetValue(use, out var value) ||
				!value.TryGetIntegral(out _)))
		{
			return false;
		}
		var left = Integral(constants[branch.Uses[0]]);
		var right = branch.Uses.Length > 1 ? Integral(constants[branch.Uses[1]]) : 0;
		takeTarget = condition.Condition switch
		{
			M68kCondition.True => true,
			M68kCondition.False => false,
			M68kCondition.Higher => (ulong)left > (ulong)right,
			M68kCondition.LowerOrSame => (ulong)left <= (ulong)right,
			M68kCondition.CarryClear => (ulong)left >= (ulong)right,
			M68kCondition.CarrySet => (ulong)left < (ulong)right,
			M68kCondition.NotEqual => left != right,
			M68kCondition.Equal => left == right,
			M68kCondition.Plus => left >= 0,
			M68kCondition.Minus => left < 0,
			M68kCondition.GreaterOrEqual => left >= right,
			M68kCondition.LessThan => left < right,
			M68kCondition.GreaterThan => left > right,
			M68kCondition.LessOrEqual => left <= right,
			_ => false
		};
		return condition.Condition is not
			(M68kCondition.OverflowClear or M68kCondition.OverflowSet);
	}

	private static bool TryEvaluateSwitch(
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, M68kMachineConstant> constants,
		IReadOnlyDictionary<int, M68kMachineBlock> blocks,
		out int targetBlockId)
	{
		targetBlockId = -1;
		if (instruction.Uses is not [var selector] ||
			!constants.TryGetValue(selector, out var constant) ||
			!constant.TryGetIntegral(out var value) ||
			instruction.SourceInstruction is not
			{
				Operand: int[] targets
			} source)
		{
			return false;
		}
		var targetOffset = value >= 0 && value < targets.Length
			? targets[value]
			: source.NextOffset;
		var target = blocks.Values.FirstOrDefault(block =>
			block.StartIlOffset == targetOffset);
		if (target is null)
		{
			return false;
		}
		targetBlockId = target.Id;
		return true;
	}

	private static Dictionary<int, M68kMachineConstant> BuildConstantMap(
		M68kMachineFunction function) =>
		function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.Constant &&
				instruction.Definitions.Length == 1 &&
				instruction.ConstantValue is not null)
			.ToDictionary(
				static instruction => instruction.Definitions[0],
				static instruction => instruction.ConstantValue!.Value);

	private static bool TryCreateExpressionKey(
		M68kMachineInstruction instruction,
		IReadOnlyDictionary<int, int> substitutions,
		out ExpressionKey key)
	{
		key = default;
		if (instruction.Definitions.Length != 1 || instruction.Uses.Length == 0 ||
			instruction.MemoryEffect != M68kMachineMemoryEffect.None ||
			instruction.MayThrow || instruction.IsSafepoint ||
			instruction.ProducesConditionCodes || instruction.ConsumesConditionCodes ||
			!instruction.Clobbers.IsEmpty || instruction.RequiresLiveCallerFrame ||
			instruction.TransportsManagedByrefOwner ||
			instruction.Operation is not (
				M68kMachineOperation.Add or
				M68kMachineOperation.Subtract or
				M68kMachineOperation.Multiply or
				M68kMachineOperation.And or
				M68kMachineOperation.Or or
				M68kMachineOperation.Xor or
				M68kMachineOperation.Negate or
				M68kMachineOperation.Not or
				M68kMachineOperation.Shift or
				M68kMachineOperation.Compare or
				M68kMachineOperation.Convert))
		{
			return false;
		}
		var uses = instruction.Uses.Select(use => Resolve(substitutions, use)).ToArray();
		if ((instruction.Operation is M68kMachineOperation.Add or
			 M68kMachineOperation.Multiply or M68kMachineOperation.And or
			 M68kMachineOperation.Or or M68kMachineOperation.Xor) && uses.Length == 2 &&
			uses[0] > uses[1])
		{
			(uses[0], uses[1]) = (uses[1], uses[0]);
		}
		key = new ExpressionKey(
			instruction.Operation,
			instruction.SourceInstruction?.OpCode.Value ?? 0,
			string.Join(',', uses),
			instruction.Immediate);
		return true;
	}

	private static bool IsDeadRemovable(M68kMachineInstruction instruction) =>
		instruction.MemoryEffect == M68kMachineMemoryEffect.None &&
		!instruction.IsSafepoint &&
		!instruction.MayThrow &&
		!instruction.ProducesConditionCodes &&
		!instruction.ConsumesConditionCodes &&
		!instruction.TransportsManagedByrefOwner &&
		!instruction.RequiresLiveCallerFrame &&
		instruction.Operation is not (
			M68kMachineOperation.Call or
			M68kMachineOperation.TypeInitialize or
			M68kMachineOperation.DynamicStackAllocate or
			M68kMachineOperation.Branch or
			M68kMachineOperation.ConditionalBranch or
			M68kMachineOperation.Switch or
			M68kMachineOperation.Return or
			M68kMachineOperation.Throw or
			M68kMachineOperation.ByrefOwnerKeepAlive or
			M68kMachineOperation.GcKeepAlive);

	private static M68kMachineInstruction AsConstant(
		M68kMachineInstruction instruction,
		M68kMachineConstant constant) =>
		instruction with
		{
			Operation = M68kMachineOperation.Constant,
			Uses = ImmutableArray<int>.Empty,
			Clobbers = M68kRegisterSet.None,
			MemoryEffect = M68kMachineMemoryEffect.None,
			IsSafepoint = false,
			MayThrow = false,
			ProducesConditionCodes = false,
			ConsumesConditionCodes = false,
			SourceInstruction = ConstantInstruction(constant, instruction.IlOffset),
			SpillSlotIndex = null,
			ArgumentIndex = null,
			StackVarargsRegister = null,
			Immediate = null,
			BranchCondition = null,
			ConstantValue = constant,
			MemoryOffset = 0,
			MemorySize = 0
		};

	private static M68kMachineInstruction AsCopy(
		M68kMachineInstruction instruction,
		int source) =>
		instruction with
		{
			Operation = M68kMachineOperation.Copy,
			Uses = ImmutableArray.Create(source),
			Clobbers = M68kRegisterSet.None,
			MemoryEffect = M68kMachineMemoryEffect.None,
			IsSafepoint = false,
			MayThrow = false,
			ProducesConditionCodes = false,
			ConsumesConditionCodes = false,
			SourceInstruction = null,
			SpillSlotIndex = null,
			ArgumentIndex = null,
			StackVarargsRegister = null,
			Immediate = null,
			BranchCondition = null,
			ConstantValue = null,
			MemoryOffset = 0,
			MemorySize = 0
		};

	private static CilInstruction ConstantInstruction(
		M68kMachineConstant constant,
		int offset) =>
		constant.Kind switch
		{
			M68kMachineConstantKind.Int64 => new CilInstruction(
				offset, OpCodes.Ldc_I8, unchecked((long)constant.Bits), offset),
			M68kMachineConstantKind.Null => new CilInstruction(
				offset, OpCodes.Ldnull, null, offset),
			M68kMachineConstantKind.Float32Bits => new CilInstruction(
				offset,
				OpCodes.Ldc_R4,
				BitConverter.UInt32BitsToSingle(unchecked((uint)constant.Bits)),
				offset),
			M68kMachineConstantKind.Float64Bits => new CilInstruction(
				offset,
				OpCodes.Ldc_R8,
				BitConverter.Int64BitsToDouble(unchecked((long)constant.Bits)),
				offset),
			_ => new CilInstruction(
				offset, OpCodes.Ldc_I4, unchecked((int)(uint)constant.Bits), offset)
		};

	private static M68kMachineConstant ZeroFor(M68kMachineValue value) =>
		value.Kind == CilStackValueKind.BooleanByte
			? M68kMachineConstant.Boolean(false)
			: value.Width == M68kMachineValueWidth.LongPair
				? M68kMachineConstant.Int64(0)
				: M68kMachineConstant.Int32(0);

	private static long Integral(M68kMachineConstant constant)
	{
		if (!constant.TryGetIntegral(out var value))
		{
			throw new InvalidOperationException("Machine constant is not integral.");
		}
		return value;
	}

	private static bool CanSubstitute(
		M68kMachineFunction function,
		int destination,
		int source)
	{
		if (destination == source)
		{
			return true;
		}
		var left = function.Values[destination];
		var right = function.Values[source];
		if (left.Kind != right.Kind || left.Width != right.Width ||
			left.AllowedRegisters != right.AllowedRegisters ||
			left.PrecoloredRegister != right.PrecoloredRegister ||
			left.IsGcReference != right.IsGcReference)
		{
			return false;
		}
		return !function.ManagedByrefTypes.TryGetValue(destination, out var leftByref) ||
			function.ManagedByrefTypes.TryGetValue(source, out var rightByref) &&
			leftByref == rightByref;
	}

	private static int Resolve(
		IReadOnlyDictionary<int, int> substitutions,
		int value)
	{
		var visited = new HashSet<int>();
		while (visited.Add(value) && substitutions.TryGetValue(value, out var next))
		{
			value = next;
		}
		return value;
	}

	private static void ApplySubstitutions(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, int> substitutions)
	{
		foreach (var block in function.Blocks)
		{
			for (var index = 0; index < block.Instructions.Count; index++)
			{
				var instruction = block.Instructions[index];
				if (instruction.Uses.Length == 0 && instruction.LogicalCall is null &&
					instruction.ExactMemoryAccesses.IsDefaultOrEmpty)
				{
					continue;
				}
				block.Instructions[index] = instruction with
				{
					Uses = instruction.Uses
						.Select(use => Resolve(substitutions, use))
						.ToImmutableArray(),
					// Exact accesses carry SSA identities too. Keeping a removed copy
					// here can prevent promotion, or associate a heap access with the
					// wrong owner after ordinary operands have been canonicalized.
					ExactMemoryAccesses = instruction.ExactMemoryAccesses.IsDefaultOrEmpty
						? instruction.ExactMemoryAccesses
						: instruction.ExactMemoryAccesses.Select(access => access with
						{
							ValueId = access.ValueId is { } value
								? Resolve(substitutions, value)
								: null,
							Object = access.Object with
							{
								OwnerValueId = access.Object.OwnerValueId is { } owner
									? Resolve(substitutions, owner)
									: null
							}
						}).ToImmutableArray(),
					LogicalCall = instruction.LogicalCall is { } logicalCall
						? logicalCall with
						{
							ArgumentValueIds = logicalCall.ArgumentValueIds
								.Select(value => Resolve(substitutions, value))
								.ToImmutableArray(),
							ResultValueIds = logicalCall.ResultValueIds
								.Select(value => Resolve(substitutions, value))
								.ToImmutableArray()
						}
						: null
				};
			}
			for (var index = 0; index < block.Phis.Count; index++)
			{
				var phi = block.Phis[index];
				block.Phis[index] = phi with
				{
					Inputs = phi.Inputs.ToDictionary(
						static input => input.Key,
						input => Resolve(substitutions, input.Value))
				};
			}
		}
	}

	private static void RemovePredecessor(M68kMachineBlock block, int predecessor)
	{
		block.Predecessors.Remove(predecessor);
		for (var index = 0; index < block.Phis.Count; index++)
		{
			var phi = block.Phis[index];
			if (!phi.Inputs.ContainsKey(predecessor))
			{
				continue;
			}
			block.Phis[index] = phi with
			{
				Inputs = phi.Inputs
					.Where(input => input.Key != predecessor)
					.ToDictionary(static input => input.Key, static input => input.Value)
			};
		}
	}

	private static void RemoveUnreferencedValues(M68kMachineFunction function)
	{
		var referenced = function.Blocks.SelectMany(block =>
			block.Instructions.SelectMany(static instruction =>
				instruction.Uses.Concat(instruction.Definitions)
					.Concat(instruction.LogicalCall?.ArgumentValueIds ?? [])
					.Concat(instruction.LogicalCall?.ResultValueIds ?? []))
				.Concat(block.Phis.SelectMany(static phi =>
					phi.Inputs.Values.Append(phi.Definition))))
			.ToHashSet();
		foreach (var value in function.Values.Keys
			.Where(value => !referenced.Contains(value))
			.ToArray())
		{
			function.Values.Remove(value);
			function.ManagedByrefTypes.Remove(value);
		}
	}
}
