/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Backend;

internal enum CilOptimizationKind
{
	DiscardCallResult,
	ComparisonBranch,
	PredicateBranch,
	Suppress
}

internal sealed record CilOptimization(
	CilOptimizationKind Kind,
	int StartIndex,
	int EndIndex,
	OpCode ComparisonOpCode,
	bool BranchOnComparisonTrue,
	int BranchTarget);

internal sealed class CilOptimizationPlan
{
	private readonly IReadOnlyDictionary<int, CilOptimization> _optimizations;

	public CilOptimizationPlan(IReadOnlyDictionary<int, CilOptimization> optimizations)
	{
		_optimizations = optimizations;
	}

	public bool TryGet(int ilOffset, out CilOptimization optimization) =>
		_optimizations.TryGetValue(ilOffset, out optimization!);
}

internal static class CilOptimizer
{
	public static CilOptimizationPlan Optimize(
		CilMethod method,
		CompilationModule module)
	{
		var instructions = method.Instructions;
		var branchTargets = GetBranchTargets(instructions);
		var optimizations = new Dictionary<int, CilOptimization>();

		for (var index = 0; index < instructions.Count;)
		{
			if (TryCreateDiscardCallResult(
					method,
					module,
					instructions,
					index,
					branchTargets,
					out var discard))
			{
				optimizations.Add(instructions[index].Offset, discard);
				index = discard.EndIndex + 1;
				continue;
			}

			if (TryCreateComparisonBranch(
					method,
					instructions,
					index,
					branchTargets,
					out var comparison))
			{
				optimizations.Add(instructions[index].Offset, comparison);
				index = comparison.EndIndex + 1;
				continue;
			}

			if (TryCreatePredicateBranch(
					method,
					module,
					instructions,
					index,
					branchTargets,
					out var predicate))
			{
				optimizations.Add(instructions[index].Offset, predicate);
				index = predicate.EndIndex + 1;
				continue;
			}

			if (TryCreateSuppressedRange(
					method,
					instructions,
					index,
					branchTargets,
					out var suppressed))
			{
				optimizations.Add(instructions[index].Offset, suppressed);
				index = suppressed.EndIndex + 1;
				continue;
			}

			index++;
		}

		return new CilOptimizationPlan(optimizations);
	}

	private static bool TryCreateSuppressedRange(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out CilOptimization optimization)
	{
		optimization = null!;
		var instruction = instructions[startIndex];
		if (instruction.OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instruction.Offset))
		{
			var endIndex = startIndex;
			while (endIndex + 1 < instructions.Count &&
				instructions[endIndex + 1].OpCode == OpCodes.Nop &&
				!branchTargets.Contains(instructions[endIndex + 1].Offset) &&
				CanCombineRange(
					method,
					instructions,
					startIndex,
					endIndex + 1,
					branchTargets))
			{
				endIndex++;
			}

			optimization = new CilOptimization(
				CilOptimizationKind.Suppress,
				startIndex,
				endIndex,
				default,
				false,
				0);
			return true;
		}

		if ((instruction.OpCode == OpCodes.Br ||
			 instruction.OpCode == OpCodes.Br_S) &&
			instruction.Operand is int target)
		{
			var endIndex = startIndex;
			while (endIndex + 1 < instructions.Count &&
				instructions[endIndex + 1].Offset != target &&
				instructions[endIndex + 1].OpCode == OpCodes.Nop &&
				!branchTargets.Contains(instructions[endIndex + 1].Offset))
			{
				endIndex++;
			}

			if (endIndex + 1 < instructions.Count &&
				instructions[endIndex + 1].Offset == target &&
				CanCombineRange(
					method,
					instructions,
					startIndex,
					endIndex,
					branchTargets))
			{
				optimization = new CilOptimization(
					CilOptimizationKind.Suppress,
					startIndex,
					endIndex,
					default,
					false,
					0);
				return true;
			}
		}

		return false;
	}

	private static bool TryCreateDiscardCallResult(
		CilMethod method,
		CompilationModule module,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out CilOptimization optimization)
	{
		optimization = null!;
		var instruction = instructions[startIndex];
		if (instruction.OpCode != OpCodes.Call &&
			instruction.OpCode != OpCodes.Callvirt)
		{
			return false;
		}

		var popIndex = startIndex + 1;
		while (popIndex < instructions.Count &&
			instructions[popIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[popIndex].Offset))
		{
			popIndex++;
		}

		if (popIndex >= instructions.Count ||
			instructions[popIndex].OpCode != OpCodes.Pop ||
			!CanCombineRange(method, instructions, startIndex, popIndex, branchTargets))
		{
			return false;
		}

		var target = module.ResolveMethodToken(
			(int)instruction.Operand!,
			method,
			instruction.Offset);
		if (target.Definition is null ||
			target.Signature.ReturnType.IsVoid ||
			(target.Definition.ExternalCall is not null &&
			 target.Signature.ParameterTypes.LastOrDefault()?.ElementType is not null))
		{
			// Stack-varargs calls need their preceding newarr/stelem sequence intact
			// so machine-IR lowering can replace it with direct stack staging.
			return false;
		}

		optimization = new CilOptimization(
			CilOptimizationKind.DiscardCallResult,
			startIndex,
			popIndex,
			default,
			false,
			0);
		return true;
	}

	private static bool TryCreateComparisonBranch(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out CilOptimization optimization)
	{
		optimization = null!;
		var comparison = instructions[startIndex];
		if (!IsComparisonOp(comparison.OpCode))
		{
			return false;
		}

		var index = startIndex + 1;
		var inverted = false;
		if (index + 1 < instructions.Count &&
			TryGetConstant(instructions[index], out var constant) &&
			constant == 0 &&
			instructions[index + 1].OpCode == OpCodes.Ceq)
		{
			inverted = true;
			index += 2;
		}

		int? temporaryLocal = null;
		if (index + 2 < instructions.Count &&
			TryGetStoreLocalIndex(instructions[index], out var storedLocal) &&
			TryGetLoadLocalIndex(instructions[index + 1], out var loadedLocal) &&
			storedLocal == loadedLocal)
		{
			temporaryLocal = storedLocal;
			index += 2;
		}

		if (index >= instructions.Count ||
			!TryGetBooleanBranch(instructions[index], out var branchOnTrue, out var target))
		{
			return false;
		}

		if (temporaryLocal is { } local &&
			IsLocalObservedAfter(instructions, index, local))
		{
			return false;
		}

		if (!CanCombineRange(method, instructions, startIndex, index, branchTargets))
		{
			return false;
		}

		optimization = new CilOptimization(
			CilOptimizationKind.ComparisonBranch,
			startIndex,
			index,
			comparison.OpCode,
			inverted ? !branchOnTrue : branchOnTrue,
			target);
		return true;
	}

	private static bool TryCreatePredicateBranch(
		CilMethod method,
		CompilationModule module,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		IReadOnlySet<int> branchTargets,
		out CilOptimization optimization)
	{
		optimization = null!;
		var predicate = instructions[startIndex];
		if (predicate.OpCode != OpCodes.Call &&
			predicate.OpCode != OpCodes.Callvirt)
		{
			return false;
		}

		var target = module.ResolveMethodToken(
			(int)predicate.Operand!,
			method,
			predicate.Offset);
		if (!IsConditionCodePredicate(target.ImportName))
		{
			return false;
		}

		var branchIndex = startIndex + 1;
		var inverted = false;
		if (branchIndex + 1 < instructions.Count &&
			TryGetConstant(instructions[branchIndex], out var constant) &&
			constant == 0 &&
			instructions[branchIndex + 1].OpCode == OpCodes.Ceq)
		{
			inverted = true;
			branchIndex += 2;
		}

		int? temporaryLocal = null;
		if (branchIndex + 2 < instructions.Count &&
			TryGetStoreLocalIndex(
				instructions[branchIndex],
				out var storedLocal) &&
			TryGetLoadLocalIndex(
				instructions[branchIndex + 1],
				out var loadedLocal) &&
			storedLocal == loadedLocal)
		{
			temporaryLocal = storedLocal;
			branchIndex += 2;
		}

		while (branchIndex < instructions.Count &&
			instructions[branchIndex].OpCode == OpCodes.Nop &&
			!branchTargets.Contains(instructions[branchIndex].Offset))
		{
			branchIndex++;
		}
		if (branchIndex >= instructions.Count ||
			!TryGetBooleanBranch(
				instructions[branchIndex],
				out var branchOnTrue,
				out var branchTarget) ||
			(temporaryLocal is { } local &&
			 IsLocalObservedAfter(instructions, branchIndex, local)) ||
			!CanCombineRange(
				method,
				instructions,
				startIndex,
				branchIndex,
				branchTargets))
		{
			return false;
		}

		optimization = new CilOptimization(
			CilOptimizationKind.PredicateBranch,
			startIndex,
			branchIndex,
			default,
			inverted ? !branchOnTrue : branchOnTrue,
			branchTarget);
		return true;
	}

	private static bool IsConditionCodePredicate(string? importName) =>
		importName is
			"intrinsic:aptr-is-null" or
			"intrinsic:aptr-is-not-null" ||
		importName?.StartsWith(
			"intrinsic:nullable-has-value:",
			StringComparison.Ordinal) == true;

	private static bool CanCombineRange(
		CilMethod method,
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		int endIndex,
		IReadOnlySet<int> branchTargets)
	{
		var startOffset = instructions[startIndex].Offset;
		for (var index = startIndex + 1; index <= endIndex; index++)
		{
			var offset = instructions[index].Offset;
			if (branchTargets.Contains(offset) ||
				IsExceptionBoundary(method, offset) ||
				!HasSameProtectedState(method, startOffset, offset))
			{
				return false;
			}
		}

		return true;
	}

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

	private static HashSet<int> GetBranchTargets(
		IReadOnlyList<CilInstruction> instructions)
	{
		var targets = new HashSet<int>();
		foreach (var instruction in instructions)
		{
			if (instruction.OpCode == OpCodes.Switch)
			{
				targets.UnionWith((int[])instruction.Operand!);
			}
			else if (instruction.OpCode.FlowControl is
					FlowControl.Branch or FlowControl.Cond_Branch &&
				instruction.Operand is int target)
			{
				targets.Add(target);
			}
		}

		return targets;
	}

	private static bool IsComparisonOp(OpCode op) =>
		op == OpCodes.Ceq ||
		op == OpCodes.Cgt ||
		op == OpCodes.Cgt_Un ||
		op == OpCodes.Clt ||
		op == OpCodes.Clt_Un;

	private static bool TryGetBooleanBranch(
		CilInstruction instruction,
		out bool branchOnTrue,
		out int target)
	{
		if ((instruction.OpCode == OpCodes.Brtrue ||
			 instruction.OpCode == OpCodes.Brtrue_S) &&
			instruction.Operand is int trueTarget)
		{
			branchOnTrue = true;
			target = trueTarget;
			return true;
		}

		if ((instruction.OpCode == OpCodes.Brfalse ||
			 instruction.OpCode == OpCodes.Brfalse_S) &&
			instruction.Operand is int falseTarget)
		{
			branchOnTrue = false;
			target = falseTarget;
			return true;
		}

		branchOnTrue = false;
		target = 0;
		return false;
	}

	private static bool IsLocalObservedAfter(
		IReadOnlyList<CilInstruction> instructions,
		int startIndex,
		int localIndex)
	{
		for (var index = startIndex + 1; index < instructions.Count; index++)
		{
			if ((TryGetLoadLocalIndex(instructions[index], out var loaded) &&
				 loaded == localIndex) ||
				(TryGetLoadLocalAddressIndex(instructions[index], out var addressed) &&
				 addressed == localIndex))
			{
				return true;
			}
		}

		return false;
	}

	private static bool TryGetConstant(CilInstruction instruction, out int value)
	{
		if (instruction.OpCode == OpCodes.Ldc_I4)
		{
			value = (int)instruction.Operand!;
			return true;
		}

		if (instruction.OpCode == OpCodes.Ldc_I4_S)
		{
			value = (sbyte)instruction.Operand!;
			return true;
		}

		value = instruction.OpCode.Value switch
		{
			var op when op == OpCodes.Ldc_I4_M1.Value => -1,
			var op when op == OpCodes.Ldc_I4_0.Value => 0,
			var op when op == OpCodes.Ldc_I4_1.Value => 1,
			var op when op == OpCodes.Ldc_I4_2.Value => 2,
			var op when op == OpCodes.Ldc_I4_3.Value => 3,
			var op when op == OpCodes.Ldc_I4_4.Value => 4,
			var op when op == OpCodes.Ldc_I4_5.Value => 5,
			var op when op == OpCodes.Ldc_I4_6.Value => 6,
			var op when op == OpCodes.Ldc_I4_7.Value => 7,
			var op when op == OpCodes.Ldc_I4_8.Value => 8,
			_ => 0
		};
		return instruction.OpCode.Value >= OpCodes.Ldc_I4_M1.Value &&
			instruction.OpCode.Value <= OpCodes.Ldc_I4_8.Value;
	}

	private static bool TryGetLoadLocalIndex(
		CilInstruction instruction,
		out int index) =>
		TryGetLocalIndex(
			instruction,
			OpCodes.Ldloc,
			OpCodes.Ldloc_S,
			OpCodes.Ldloc_0,
			out index);

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

	private static bool TryGetStoreLocalIndex(
		CilInstruction instruction,
		out int index) =>
		TryGetLocalIndex(
			instruction,
			OpCodes.Stloc,
			OpCodes.Stloc_S,
			OpCodes.Stloc_0,
			out index);

	private static bool TryGetLocalIndex(
		CilInstruction instruction,
		OpCode longForm,
		OpCode shortForm,
		OpCode zeroForm,
		out int index)
	{
		if (instruction.OpCode == longForm ||
			instruction.OpCode == shortForm)
		{
			index = Convert.ToInt32(instruction.Operand);
			return true;
		}

		var delta = instruction.OpCode.Value - zeroForm.Value;
		if (delta is >= 0 and <= 3)
		{
			index = delta;
			return true;
		}

		index = 0;
		return false;
	}
}
