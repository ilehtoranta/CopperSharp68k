/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal enum M68kConditionFactKind : byte
{
	Unknown,
	False,
	True,
	Zero,
	Negative,
	CompareZero,
	CompareNegative,
	CompareOverflow,
	CompareCarry
}

internal readonly record struct M68kPhysicalValue(long Identity)
{
	internal static M68kPhysicalValue Unknown => default;

	internal bool IsKnown => Identity != 0;
}

internal readonly record struct M68kConditionFact(
	M68kConditionFactKind Kind,
	M68kPhysicalValue Left,
	M68kPhysicalValue Right,
	int Width)
{
	internal static M68kConditionFact Unknown => default;

	internal bool IsKnown => Kind != M68kConditionFactKind.Unknown &&
		(Kind is M68kConditionFactKind.False or M68kConditionFactKind.True ||
		 Left.IsKnown &&
		 (Kind is not
			M68kConditionFactKind.CompareZero and not
			M68kConditionFactKind.CompareNegative and not
			M68kConditionFactKind.CompareOverflow and not
			M68kConditionFactKind.CompareCarry || Right.IsKnown));
}

internal readonly record struct M68kConditionProvenance(
	M68kConditionFact Negative,
	M68kConditionFact Zero,
	M68kConditionFact Overflow,
	M68kConditionFact Carry)
{
	internal static M68kConditionProvenance Unknown => default;

	internal M68kConditionFact Get(M68kConditionCodeSet code) => code switch
	{
		M68kConditionCodeSet.Negative => Negative,
		M68kConditionCodeSet.Zero => Zero,
		M68kConditionCodeSet.Overflow => Overflow,
		M68kConditionCodeSet.Carry => Carry,
		_ => M68kConditionFact.Unknown
	};
}

internal sealed class M68kConditionProvenanceAnalysis
{
	private sealed class State
	{
		internal M68kPhysicalValue[] Data { get; } = new M68kPhysicalValue[8];

		internal M68kPhysicalValue[] Address { get; } = new M68kPhysicalValue[8];

		internal M68kConditionProvenance Conditions { get; set; }

		internal HashSet<M68kPhysicalValue> KnownZero { get; } = [];

		internal HashSet<M68kPhysicalValue> KnownNonZero { get; } = [];

		internal State Clone()
		{
			var result = new State { Conditions = Conditions };
			Array.Copy(Data, result.Data, Data.Length);
			Array.Copy(Address, result.Address, Address.Length);
			result.KnownZero.UnionWith(KnownZero);
			result.KnownNonZero.UnionWith(KnownNonZero);
			return result;
		}

		internal bool JoinFrom(State other)
		{
			var changed = false;
			for (var index = 0; index < 8; index++)
			{
				changed |= Join(ref Data[index], other.Data[index]);
				changed |= Join(ref Address[index], other.Address[index]);
			}
			var joined = JoinConditions(Conditions, other.Conditions);
			if (joined != Conditions)
			{
				Conditions = joined;
				changed = true;
			}
			changed |= Intersect(KnownZero, other.KnownZero);
			changed |= Intersect(KnownNonZero, other.KnownNonZero);
			return changed;
		}

		private static bool Intersect(
			HashSet<M68kPhysicalValue> target,
			HashSet<M68kPhysicalValue> incoming)
		{
			var before = target.Count;
			target.IntersectWith(incoming);
			return target.Count != before;
		}

		private static bool Join(
			ref M68kPhysicalValue target,
			M68kPhysicalValue incoming)
		{
			if (target == incoming || !target.IsKnown)
			{
				return false;
			}
			target = M68kPhysicalValue.Unknown;
			return true;
		}
	}

	private readonly IReadOnlyDictionary<int, State> _before;

	private M68kConditionProvenanceAnalysis(
		IReadOnlyDictionary<int, State> before)
	{
		_before = before;
	}

	internal static M68kConditionProvenanceAnalysis Empty { get; } =
		new(new Dictionary<int, State>());

	internal bool TryGetKnownZeroTest(
		M68kEmittedInstruction instruction,
		out bool nonZero)
	{
		nonZero = false;
		if (!_before.TryGetValue(instruction.Offset, out var state) ||
			!TryDescribeCandidate(state, instruction, out var expected) ||
			expected.Zero.Kind != M68kConditionFactKind.Zero ||
			expected.Zero.Width != 4 ||
			!expected.Zero.Left.IsKnown)
		{
			return false;
		}

		if (state.KnownNonZero.Contains(expected.Zero.Left))
		{
			nonZero = true;
			return true;
		}
		return state.KnownZero.Contains(expected.Zero.Left);
	}

	internal bool IsRedundant(
		M68kEmittedInstruction instruction,
		M68kConditionCodeSet required)
	{
		if (!_before.TryGetValue(instruction.Offset, out var state) ||
			!TryDescribeCandidate(state, instruction, out var expected))
		{
			return false;
		}
		var relevant = required &
			(M68kConditionCodeSet.Negative |
			 M68kConditionCodeSet.Zero |
			 M68kConditionCodeSet.Overflow |
			 M68kConditionCodeSet.Carry);
		if (relevant == M68kConditionCodeSet.None)
		{
			return false;
		}
		foreach (var code in new[]
		{
			M68kConditionCodeSet.Negative,
			M68kConditionCodeSet.Zero,
			M68kConditionCodeSet.Overflow,
			M68kConditionCodeSet.Carry
		})
		{
			if ((relevant & code) == 0)
			{
				continue;
			}
			var actual = state.Conditions.Get(code);
			var wanted = expected.Get(code);
			if (!actual.IsKnown || !wanted.IsKnown || actual != wanted)
			{
				return false;
			}
		}
		return true;
	}

	internal static M68kConditionProvenanceAnalysis Analyze(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		IReadOnlyList<int>[] successors,
		IReadOnlyList<int>[] predecessors,
		IReadOnlyList<M68kInstructionEffects> effects)
	{
		var before = new State?[instructions.Count];
		var pending = new Queue<int>();
		var queued = new bool[instructions.Count];
		for (var index = 0; index < instructions.Count; index++)
		{
			if (predecessors[index].Count != 0)
			{
				continue;
			}
			before[index] = CreateEntry(index);
			pending.Enqueue(index);
			queued[index] = true;
		}

		while (pending.Count != 0)
		{
			var index = pending.Dequeue();
			queued[index] = false;
			var output = Transfer(before[index]!, instructions[index], effects[index]);
			if (instructions[index].IsNonReturning)
			{
				continue;
			}
			foreach (var successor in successors[index])
			{
				var successorState = RefineBranch(
					output,
					instructions[index],
					instructions[successor]);
				var changed = before[successor] is null
					? SetInitial(before, successor, successorState)
					: before[successor]!.JoinFrom(successorState);
				if (changed && !queued[successor])
				{
					pending.Enqueue(successor);
					queued[successor] = true;
				}
			}
		}

		return new M68kConditionProvenanceAnalysis(
			instructions
				.Select((instruction, index) => (instruction.Offset, State: before[index]))
				.Where(static item => item.State is not null)
				.ToDictionary(static item => item.Offset, static item => item.State!));
	}

	private static State RefineBranch(
		State output,
		M68kEmittedInstruction branch,
		M68kEmittedInstruction successor)
	{
		var result = output.Clone();
		if (branch.Kind != M68kInstructionKind.ConditionalBranch ||
			branch.TargetOffset is not { } targetOffset ||
			successor.Offset != targetOffset &&
			successor.Offset != branch.Offset + branch.Length ||
			((branch.Opcode >> 8) & 0x0F) is not (6 or 7) ||
			output.Conditions.Zero.Kind != M68kConditionFactKind.Zero ||
			output.Conditions.Zero.Width != 4 ||
			!output.Conditions.Zero.Left.IsKnown)
		{
			return result;
		}

		var isTarget = successor.Offset == targetOffset;
		var isZero = ((branch.Opcode >> 8) & 0x0F) == 7
			? isTarget
			: !isTarget;
		var value = output.Conditions.Zero.Left;
		if (isZero)
		{
			result.KnownZero.Add(value);
			result.KnownNonZero.Remove(value);
		}
		else
		{
			result.KnownNonZero.Add(value);
			result.KnownZero.Remove(value);
		}
		return result;
	}

	private static State CreateEntry(int entryIndex)
	{
		var result = new State();
		for (var register = 0; register < 8; register++)
		{
			result.Data[register] = new M68kPhysicalValue(
				-1 - entryIndex * 16 - register);
			result.Address[register] = new M68kPhysicalValue(
				-9 - entryIndex * 16 - register);
		}
		return result;
	}

	private static bool SetInitial(State?[] states, int index, State value)
	{
		states[index] = value.Clone();
		return true;
	}

	private static State Transfer(
		State input,
		M68kEmittedInstruction instruction,
		M68kInstructionEffects effects)
	{
		var output = input.Clone();
		UpdateRegisterValues(input, output, instruction, effects);
		if (effects.IsBarrier)
		{
			output.KnownZero.Clear();
			output.KnownNonZero.Clear();
		}
		if (effects.WritesConditions != M68kConditionCodeSet.None)
		{
			output.Conditions = MergeWrittenConditions(
				input.Conditions,
				DescribeWrittenConditions(
					input,
					output,
					instruction,
					effects),
				effects.WritesConditions);
		}
		return output;
	}

	private static void UpdateRegisterValues(
		State input,
		State output,
		M68kEmittedInstruction instruction,
		M68kInstructionEffects effects)
	{
		for (var register = 0; register < 8; register++)
		{
			if ((effects.DefinesData & (1 << register)) != 0)
			{
				output.Data[register] = DefinedValue(instruction, register, address: false);
			}
			if ((effects.DefinesAddress & (1 << register)) != 0)
			{
				output.Address[register] = DefinedValue(instruction, register, address: true);
			}
		}

		var opcode = instruction.Opcode;
		if ((opcode & 0xF000) is 0x1000 or 0x2000 or 0x3000)
		{
			var destinationMode = (opcode >> 6) & 7;
			var destination = (opcode >> 9) & 7;
			var sourceMode = (opcode >> 3) & 7;
			var source = opcode & 7;
			if (sourceMode == 0 && destinationMode == 0)
			{
				output.Data[destination] = input.Data[source];
			}
			else if (sourceMode == 1 && destinationMode == 1)
			{
				output.Address[destination] = input.Address[source];
			}
			else if (sourceMode == 0 && destinationMode == 1)
			{
				output.Address[destination] = input.Data[source];
			}
			else if (sourceMode == 1 && destinationMode == 0)
			{
				output.Data[destination] = input.Address[source];
			}
		}
	}

	private static M68kPhysicalValue DefinedValue(
		M68kEmittedInstruction instruction,
		int register,
		bool address) =>
		new(1L + instruction.Offset * 32L + (address ? 16 : 0) + register);

	private static M68kConditionProvenance DescribeWrittenConditions(
		State input,
		State output,
		M68kEmittedInstruction instruction,
		M68kInstructionEffects effects)
	{
		if (TryDescribeCandidate(input, instruction, out var candidate))
		{
			return candidate;
		}

		var opcode = instruction.Opcode;
		var width = InstructionWidth(opcode);
		if ((opcode & 0xF100) == 0x7000)
		{
			return TestFacts(output.Data[(opcode >> 9) & 7], 4, clearOverflowCarry: true);
		}
		if ((opcode & 0xF000) is 0x1000 or 0x2000 or 0x3000 &&
			((opcode >> 6) & 7) != 1)
		{
			var value = MoveSourceValue(input, output, instruction);
			return TestFacts(value, width, clearOverflowCarry: true);
		}
		if ((opcode & 0xFFC0) is 0x4280 or 0x4240 or 0x4200)
		{
			return new M68kConditionProvenance(
				FalseFact,
				TrueFact,
				FalseFact,
				FalseFact);
		}

		var defined = SingleDefinedData(effects.DefinesData);
		if (defined >= 0 &&
			TryDescribeResultConditions(opcode, out var clearOverflowCarry))
		{
			return TestFacts(
				output.Data[defined],
				width,
				clearOverflowCarry);
		}
		return M68kConditionProvenance.Unknown;
	}

	private static bool TryDescribeCandidate(
		State state,
		M68kEmittedInstruction instruction,
		out M68kConditionProvenance provenance)
	{
		var opcode = instruction.Opcode;
		if ((opcode & 0xFFF8) is 0x4A00 or 0x4A40 or 0x4A80)
		{
			provenance = TestFacts(
				state.Data[opcode & 7],
				InstructionWidth(opcode),
				clearOverflowCarry: true);
			return true;
		}

		if ((opcode & 0xFFF8) is 0x0C00 or 0x0C40 or 0x0C80 &&
			((opcode & 0xFFF8) == 0x0C80
				? instruction.ExtensionLong == 0
				: instruction.ExtensionWord == 0))
		{
			provenance = TestFacts(
				state.Data[opcode & 7],
				InstructionWidth(opcode),
				clearOverflowCarry: true);
			return true;
		}

		if ((opcode & 0xF100) == 0xB000 &&
			((opcode >> 6) & 7) <= 2 &&
			((opcode >> 3) & 7) == 0)
		{
			var left = state.Data[(opcode >> 9) & 7];
			var right = state.Data[opcode & 7];
			var width = InstructionWidth(opcode);
			provenance = CompareFacts(left, right, width);
			return true;
		}

		if ((opcode & 0xF1FF) is 0xB0FC or 0xB1FC &&
			((opcode & 0xF1FF) == 0xB0FC
				? instruction.ExtensionWord == 0
				: instruction.ExtensionLong == 0))
		{
			// CMPA #0 has the same N/Z/V/C result as testing the address value.
			provenance = TestFacts(
				state.Address[(opcode >> 9) & 7],
				4,
				clearOverflowCarry: true);
			return true;
		}

		provenance = default;
		return false;
	}

	private static M68kPhysicalValue MoveSourceValue(
		State input,
		State output,
		M68kEmittedInstruction instruction)
	{
		var opcode = instruction.Opcode;
		var sourceMode = (opcode >> 3) & 7;
		var source = opcode & 7;
		if (sourceMode == 0)
		{
			return input.Data[source];
		}
		if (sourceMode == 1)
		{
			return input.Address[source];
		}
		var destinationMode = (opcode >> 6) & 7;
		if (destinationMode == 0)
		{
			return output.Data[(opcode >> 9) & 7];
		}
		return new M68kPhysicalValue(1L + instruction.Offset * 32L + 31);
	}

	private static M68kConditionProvenance TestFacts(
		M68kPhysicalValue value,
		int width,
		bool clearOverflowCarry) =>
		new(
			new M68kConditionFact(
				M68kConditionFactKind.Negative,
				value,
				default,
				width),
			new M68kConditionFact(
				M68kConditionFactKind.Zero,
				value,
				default,
				width),
			clearOverflowCarry ? FalseFact : M68kConditionFact.Unknown,
			clearOverflowCarry ? FalseFact : M68kConditionFact.Unknown);

	private static M68kConditionProvenance CompareFacts(
		M68kPhysicalValue left,
		M68kPhysicalValue right,
		int width) =>
		new(
			new M68kConditionFact(
				M68kConditionFactKind.CompareNegative,
				left,
				right,
				width),
			new M68kConditionFact(
				M68kConditionFactKind.CompareZero,
				left,
				right,
				width),
			new M68kConditionFact(
				M68kConditionFactKind.CompareOverflow,
				left,
				right,
				width),
			new M68kConditionFact(
				M68kConditionFactKind.CompareCarry,
				left,
				right,
				width));

	private static M68kConditionFact FalseFact =>
		new(M68kConditionFactKind.False, default, default, 0);

	private static M68kConditionFact TrueFact =>
		new(M68kConditionFactKind.True, default, default, 0);

	private static int SingleDefinedData(ushort definitions)
	{
		if (definitions == 0 || (definitions & (definitions - 1)) != 0)
		{
			return -1;
		}
		for (var register = 0; register < 8; register++)
		{
			if (definitions == 1 << register)
			{
				return register;
			}
		}
		return -1;
	}

	private static bool TryDescribeResultConditions(
		ushort opcode,
		out bool clearOverflowCarry)
	{
		clearOverflowCarry = false;
		var family = opcode & 0xF000;
		var operationMode = (opcode >> 6) & 7;
		var effectiveAddressMode = (opcode >> 3) & 7;

		if (family == 0x5000 &&
			(opcode & 0x00C0) != 0x00C0 &&
			effectiveAddressMode == 0)
		{
			return true;
		}
		if (family is 0x9000 or 0xD000 && operationMode <= 2)
		{
			return true;
		}
		if (family is 0x8000 or 0xC000 && operationMode <= 2)
		{
			clearOverflowCarry = true;
			return true;
		}
		if (family == 0xB000 &&
			operationMode is >= 4 and <= 6 &&
			effectiveAddressMode == 0)
		{
			clearOverflowCarry = true;
			return true;
		}
		if (family == 0 &&
			effectiveAddressMode == 0 &&
			(opcode & 0x0F00) is 0x0000 or 0x0200 or 0x0600 or 0x0A00 or 0x0400)
		{
			clearOverflowCarry = (opcode & 0x0F00) is
				0x0000 or 0x0200 or 0x0A00;
			return true;
		}
		if ((opcode & 0xFFC0) is 0x4880 or 0x48C0 or 0x49C0 ||
			(opcode & 0xFFC0) is 0x4600 or 0x4640 or 0x4680)
		{
			clearOverflowCarry = true;
			return true;
		}
		if ((opcode & 0xFFC0) is 0x4400 or 0x4440 or 0x4480)
		{
			return true;
		}
		return false;
	}

	private static M68kConditionProvenance MergeWrittenConditions(
		M68kConditionProvenance original,
		M68kConditionProvenance written,
		M68kConditionCodeSet writes) =>
		new(
			(writes & M68kConditionCodeSet.Negative) != 0
				? written.Negative
				: original.Negative,
			(writes & M68kConditionCodeSet.Zero) != 0
				? written.Zero
				: original.Zero,
			(writes & M68kConditionCodeSet.Overflow) != 0
				? written.Overflow
				: original.Overflow,
			(writes & M68kConditionCodeSet.Carry) != 0
				? written.Carry
				: original.Carry);

	private static int InstructionWidth(ushort opcode)
	{
		if ((opcode & 0xF000) == 0x1000)
		{
			return 1;
		}
		if ((opcode & 0xF000) == 0x2000)
		{
			return 4;
		}
		if ((opcode & 0xF000) == 0x3000)
		{
			return 2;
		}
		return (opcode >> 6 & 3) switch
		{
			0 => 1,
			1 => 2,
			_ => 4
		};
	}

	private static M68kConditionProvenance JoinConditions(
		M68kConditionProvenance left,
		M68kConditionProvenance right) =>
		new(
			Join(left.Negative, right.Negative),
			Join(left.Zero, right.Zero),
			Join(left.Overflow, right.Overflow),
			Join(left.Carry, right.Carry));

	private static M68kConditionFact Join(
		M68kConditionFact left,
		M68kConditionFact right) =>
		left == right ? left : M68kConditionFact.Unknown;
}

internal sealed class M68kConditionCodeOptimizer : IM68kOptimizerPass
{
	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;

	internal M68kConditionCodeOptimizer(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer)
	{
		_assembler = assembler;
		_buffer = buffer;
	}

	public bool Changed { get; private set; }
	public int RewriteCount { get; private set; }

	public void Run()
	{
		Changed = false;
		RewriteCount = 0;
		bool changed;
		do
		{
			changed = false;
			_assembler.BeginInstructionAnalysisRound();
			try
			{
				var dataflow = M68kInstructionDataflow.Analyze(_assembler);
				if (FoldKnownZeroBranches(dataflow) ||
					TryRewriteSingleBitTest(dataflow))
				{
					changed = true;
					Changed = true;
					RewriteCount++;
					continue;
				}

				if (RemoveRedundantConditionInstructions(dataflow))
				{
					changed = true;
					Changed = true;
					RewriteCount++;
				}
			}
			finally
			{
				_assembler.EndInstructionAnalysisRound();
			}
		}
		while (changed);
	}

	private bool RemoveRedundantConditionInstructions(
		M68kInstructionDataflow dataflow)
	{
		var redundant = new List<M68kEmittedInstruction>();
		foreach (var instruction in dataflow.Instructions)
		{
			if (_buffer.HasLabelAt(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!dataflow.IsConditionInstructionRedundant(
					instruction,
					facts.LiveConditionsAfter))
			{
				continue;
			}
			redundant.Add(instruction);
		}
		for (var index = redundant.Count - 1; index >= 0; index--)
		{
			var instruction = redundant[index];
			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
		}
		return redundant.Count != 0;
	}

	private readonly record struct KnownZeroBranchFold(
		M68kEmittedInstruction Test,
		M68kEmittedInstruction Branch,
		bool BranchTaken);

	private bool FoldKnownZeroBranches(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		var instructionByOffset = instructions.ToDictionary(
			static instruction => instruction.Offset);
		var folds = new List<KnownZeroBranchFold>();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var test = instructions[index];
			var branch = instructions[index + 1];
			if (branch.Kind != M68kInstructionKind.ConditionalBranch ||
				((branch.Opcode >> 8) & 0x0F) is not (6 or 7) ||
				branch.TargetOffset is null ||
				_buffer.HasLabelAt(test.Offset) ||
				_buffer.HasLabelAt(branch.Offset) ||
				!dataflow.TryGetKnownZeroTest(test, out var nonZero))
			{
				continue;
			}

			var isEqual = ((branch.Opcode >> 8) & 0x0F) == 7;
			var branchTaken = isEqual ? !nonZero : nonZero;
			var successorOffset = branchTaken
				? branch.TargetOffset.Value
				: branch.Offset + branch.Length;
			if (!instructionByOffset.TryGetValue(successorOffset,
				out var successor) ||
				!successor.IsNonReturning &&
					(!dataflow.TryGetFacts(
					successor.Offset,
					out var successorFacts) ||
					successorFacts.LiveConditionsBefore != M68kConditionCodeSet.None))
			{
				continue;
			}
			folds.Add(new KnownZeroBranchFold(test, branch, branchTaken));
		}

		for (var index = folds.Count - 1; index >= 0; index--)
		{
			var fold = folds[index];
			if (!fold.BranchTaken)
			{
				_buffer.RemoveBytes(
					fold.Test.Offset,
					fold.Test.Length + fold.Branch.Length);
				continue;
			}

			// The condition is known true, so retain the target transfer but no
			// longer require the zero-test to establish condition codes.
			_buffer.WriteWord(
				fold.Branch.Offset,
				(fold.Branch.Opcode & 0xF0FF) | 0x6000); // Bcc -> BRA
			_buffer.RemoveBytes(fold.Test.Offset, fold.Test.Length);
		}
		return folds.Count != 0;
	}

	private bool TryRewriteSingleBitTest(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var moveQuick = instructions[index];
			if (moveQuick.Length != 2 ||
				(moveQuick.Opcode & 0xF100) != 0x7000 ||
				!TryGetSingleBit(moveQuick.Opcode & 0xFF, out var bit))
			{
				continue;
			}

			var maskRegister = (moveQuick.Opcode >> 9) & 7;
			var andIndex = index + 1;
			var temporary = maskRegister;
			var and = instructions[andIndex];
			M68kEmittedInstruction? copiedValue = null;
			int source;
			if (and.Length == 2 &&
				(and.Opcode & 0xF1C0) == 0xC080 &&
				((and.Opcode >> 9) & 7) == maskRegister)
			{
				source = and.Opcode & 7;
			}
			else if (index + 3 < instructions.Count)
			{
				var copy = instructions[index + 1];
				copiedValue = copy;
				andIndex++;
				and = instructions[andIndex];
				if (copy.Length != 2 ||
					(copy.Opcode & 0xF1F8) != 0x2000 ||
					and.Length != 2 ||
					(and.Opcode & 0xF1C0) != 0xC080 ||
					(and.Opcode & 7) != maskRegister ||
					((and.Opcode >> 9) & 7) != ((copy.Opcode >> 9) & 7) ||
					HasReferencedLabelAt(copy.Offset))
				{
					continue;
				}
				source = copy.Opcode & 7;
				temporary = (copy.Opcode >> 9) & 7;
			}
			else
			{
				continue;
			}
			if (source == maskRegister || source == temporary)
			{
				continue;
			}

			M68kEmittedInstruction? test = null;
			var branchIndex = andIndex + 1;
			var candidate = instructions[branchIndex];
			if (candidate.Length == 2 &&
				(candidate.Opcode & 0xFFF8) == 0x4A80 &&
				(candidate.Opcode & 7) == temporary)
			{
				test = candidate;
				branchIndex++;
				if (branchIndex >= instructions.Count)
				{
					continue;
				}
			}

			var branch = instructions[branchIndex];
			var condition = (branch.Opcode >> 8) & 0x0F;
			var replacementCondition = condition switch
			{
				2 => 6, // BHI after AND of one positive bit is BNE.
				3 => 7, // BLS after AND of one positive bit is BEQ.
				6 or 7 => condition,
				_ => -1
			};
			if (branch.Kind != M68kInstructionKind.ConditionalBranch ||
				replacementCondition < 0 ||
				HasReferencedLabelAt(and.Offset) ||
				test is { } labeledTest && HasReferencedLabelAt(labeledTest.Offset) ||
				AreNonZeroConditionsReadAfterBranch(
					instructions,
					branchIndex,
					dataflow) ||
				IsDataRegisterReadAfterBranch(
					instructions,
					branchIndex,
					temporary,
					dataflow) ||
				maskRegister != temporary &&
					IsDataRegisterReadAfterBranch(
						instructions,
						branchIndex,
						maskRegister,
						dataflow))
			{
				continue;
			}

			MoveLabelsAt(and.Offset, moveQuick.Offset);
			if (copiedValue is { } copyWithLabels)
			{
				MoveLabelsAt(copyWithLabels.Offset, moveQuick.Offset);
			}
			if (test is { } testWithLabels)
			{
				MoveLabelsAt(testWithLabels.Offset, moveQuick.Offset);
			}
			_buffer.WriteWord(moveQuick.Offset, (ushort)(0x0800 | source));
			_buffer.WriteWord(moveQuick.Offset + 2, (ushort)bit);
			if (replacementCondition != condition)
			{
				_buffer.WriteWord(
					branch.Offset,
					(ushort)((branch.Opcode & 0xF0FF) | (replacementCondition << 8)));
			}
			if (test.HasValue)
			{
				_buffer.RemoveBytes(test.Value.Offset, test.Value.Length);
			}
			var overwrittenEnd = moveQuick.Offset + 4;
			var removedStart = and.Offset;
			if (removedStart >= overwrittenEnd)
			{
				_buffer.RemoveBytes(removedStart, and.Length);
			}
			return true;
		}

		return false;
	}

	private bool HasReferencedLabelAt(int offset)
	{
		foreach (var label in _buffer.Labels
			.Where(item => item.Value == offset)
			.Select(static item => item.Key))
		{
			if (_buffer.Branches.Any(branch =>
					string.Equals(branch.Target, label, StringComparison.Ordinal)) ||
				_buffer.Addresses.Any(address =>
					string.Equals(address.Target, label, StringComparison.Ordinal)) ||
				_buffer.PcRelative.Any(fixup =>
					string.Equals(fixup.Target, label, StringComparison.Ordinal)))
			{
				return true;
			}
		}
		return false;
	}

	private void MoveLabelsAt(int sourceOffset, int destinationOffset)
	{
		foreach (var label in _buffer.Labels.Keys.ToArray())
		{
			if (_buffer.Labels[label] == sourceOffset)
			{
				_buffer.Labels[label] = destinationOffset;
			}
		}
	}

	private static bool TryGetSingleBit(int mask, out int bit)
	{
		bit = 0;
		if (mask is <= 0 or > 64 || (mask & (mask - 1)) != 0)
		{
			return false;
		}
		while ((1 << bit) != mask)
		{
			bit++;
		}
		return true;
	}

	private static bool IsDataRegisterReadAfterBranch(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int branchIndex,
		int register,
		M68kInstructionDataflow dataflow)
	{
		var mask = (ushort)(1 << register);
		var branch = instructions[branchIndex];
		var indexByOffset = instructions
			.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(static item => item.Offset, static item => item.index);
		var pending = new Stack<int>();
		var visited = new HashSet<int>();
		Push(branchIndex + 1);
		if (branch.TargetOffset is { } targetOffset &&
			indexByOffset.TryGetValue(targetOffset, out var targetIndex))
		{
			Push(targetIndex);
		}

		while (pending.Count != 0)
		{
			var index = pending.Pop();
			if (!visited.Add(index))
			{
				continue;
			}
			var instruction = instructions[index];
			if (instruction.Kind == M68kInstructionKind.Return)
			{
				continue;
			}
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				facts.Effects.IsBarrier)
			{
				return true;
			}
			if ((facts.Effects.UsesData & mask) != 0)
			{
				return true;
			}
			if ((facts.Effects.DefinesData & mask) != 0)
			{
				continue;
			}

			switch (instruction.Kind)
			{
				case M68kInstructionKind.ConditionalBranch:
				case M68kInstructionKind.Dbcc:
					PushTarget(instruction.TargetOffset);
					Push(index + 1);
					break;
				case M68kInstructionKind.UnconditionalBranch:
					PushTarget(instruction.TargetOffset);
					break;
				default:
					Push(index + 1);
					break;
			}
		}

		return false;

		void Push(int index)
		{
			if ((uint)index < (uint)instructions.Count)
			{
				pending.Push(index);
			}
		}

		void PushTarget(int? offset)
		{
			if (offset.HasValue && indexByOffset.TryGetValue(offset.Value, out var index))
			{
				pending.Push(index);
			}
		}
	}

	private static bool AreNonZeroConditionsReadAfterBranch(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int branchIndex,
		M68kInstructionDataflow dataflow)
	{
		var missing = M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Overflow |
			M68kConditionCodeSet.Carry;
		var indexByOffset = instructions
			.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(static item => item.Offset, static item => item.index);
		var pending = new Stack<(int Index, M68kConditionCodeSet Missing)>();
		var visited = new HashSet<(int Index, M68kConditionCodeSet Missing)>();
		var branch = instructions[branchIndex];
		Push(branchIndex + 1, missing);
		PushTarget(branch.TargetOffset, missing);

		while (pending.Count != 0)
		{
			var state = pending.Pop();
			if (!visited.Add(state))
			{
				continue;
			}
			var instruction = instructions[state.Index];
			if (instruction.Kind is M68kInstructionKind.Return or M68kInstructionKind.Call)
			{
				continue;
			}
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				return true;
			}
			if ((facts.Effects.ReadsConditions & state.Missing) != M68kConditionCodeSet.None)
			{
				return true;
			}
			var remaining = state.Missing & ~facts.Effects.WritesConditions;
			if (remaining == M68kConditionCodeSet.None)
			{
				continue;
			}
			if (facts.Effects.IsBarrier)
			{
				return true;
			}

			switch (instruction.Kind)
			{
				case M68kInstructionKind.ConditionalBranch:
				case M68kInstructionKind.Dbcc:
					PushTarget(instruction.TargetOffset, remaining);
					Push(state.Index + 1, remaining);
					break;
				case M68kInstructionKind.UnconditionalBranch:
					PushTarget(instruction.TargetOffset, remaining);
					break;
				default:
					Push(state.Index + 1, remaining);
					break;
			}
		}

		return false;

		void Push(int index, M68kConditionCodeSet conditions)
		{
			if ((uint)index < (uint)instructions.Count)
			{
				pending.Push((index, conditions));
			}
		}

		void PushTarget(int? offset, M68kConditionCodeSet conditions)
		{
			if (offset.HasValue && indexByOffset.TryGetValue(offset.Value, out var index))
			{
				pending.Push((index, conditions));
			}
		}
	}
}
