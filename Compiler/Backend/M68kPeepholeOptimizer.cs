/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kPeepholeOptimizer : IM68kOptimizerPass
{
	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;

	public M68kPeepholeOptimizer(M68kAssembler assembler, M68kAssemblyBuffer buffer)
	{
		_assembler = assembler;
		_buffer = buffer;
	}

	public void Run()
	{
		bool changed;
		do
		{
			var dataflow = M68kInstructionDataflow.Analyze(_assembler);
			changed =
				TryFoldTailReturn() ||
				TryRemoveBranchToNextLabel() ||
				TryPromoteBranchStackSpillToD0() ||
				TryReplaceZeroAddressMove() ||
				TryReplaceCompareZeroWithTest() ||
				TryRemoveRedundantTest() ||
				TryRemoveDeadTest(dataflow) ||
				TryRemoveDeadInstruction(dataflow) ||
				TryNarrowAddition(dataflow) ||
				TryNarrowLogicalImmediate(dataflow) ||
				TryCanonicalizeAddressAdjustments() ||
				TryRemoveRedundantStackShuffle() ||
				TryRemoveRedundantStackArgumentShuffle() ||
				TryReplaceZeroRegisterRoundTrip() ||
				TryReplaceAddressStackRoundTrip() ||
				TryRemoveStackDuplicateRoundTrip() ||
				TryRemoveRegisterStackRoundTrip() ||
				TryReplaceCompareRegisterZeroWithTest() ||
				TryRemoveRedundantRegisterSpill();
		}
		while (changed);
	}

	private bool TryRemoveRedundantRegisterSpill()
	{
		var instructions = _assembler.GetInstructionStream();
		var dataflow = M68kInstructionDataflow.Analyze(_assembler);

		for (var pushIndex = 0; pushIndex + 2 < instructions.Count; pushIndex++)
		{
			var push = instructions[pushIndex];
			var reload = instructions[pushIndex + 1];
			if ((push.Opcode & 0xFFF8) != 0x2F00 ||
				push.Length != 2 ||
				(reload.Opcode & 0xF1FF) != 0x2017 ||
				reload.Length != 2 ||
				(push.Opcode & 7) != ((reload.Opcode >> 9) & 7) ||
				IsReferencedLabelAt(reload.Offset))
			{
				continue;
			}

			var cleanupIndex = pushIndex + 2;
			while (cleanupIndex < instructions.Count &&
				instructions[cleanupIndex].Opcode != 0x588F)
			{
				var expression = instructions[cleanupIndex];
				if (IsReferencedLabelAt(expression.Offset) ||
					!dataflow.TryGetFacts(expression.Offset, out var facts) ||
					expression.Kind != M68kInstructionKind.Normal ||
					facts.Effects.IsBarrier ||
					facts.Effects.ReadsMemory != M68kMemorySet.None ||
					facts.Effects.WritesMemory != M68kMemorySet.None ||
					(facts.Effects.DefinesAddress & (1 << 7)) != 0 ||
					facts.Effects.StackDelta is { } stackDelta && stackDelta != 0)
				{
					break;
				}

				cleanupIndex++;
			}

			if (cleanupIndex >= instructions.Count ||
				instructions[cleanupIndex].Length != 2 ||
				IsReferencedLabelAt(instructions[cleanupIndex].Offset))
			{
				continue;
			}
			if (instructions[cleanupIndex].Opcode != 0x588F)
			{
				continue;
			}

			foreach (var label in _buffer.Labels.Keys.ToArray())
			{
				if (_buffer.Labels[label] > push.Offset &&
					_buffer.Labels[label] < instructions[cleanupIndex].Offset + instructions[cleanupIndex].Length)
				{
					_buffer.Labels[label] = push.Offset;
				}
			}

			_buffer.RemoveBytes(instructions[cleanupIndex].Offset, instructions[cleanupIndex].Length);
			_buffer.RemoveBytes(reload.Offset, reload.Length);
			_buffer.RemoveBytes(push.Offset, push.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveRedundantStackArgumentShuffle()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 3 < instructions.Count; index++)
		{
			var firstPush = instructions[index];
			var secondPush = instructions[index + 1];
			var firstPop = instructions[index + 2];
			var secondPop = instructions[index + 3];
			if (firstPush.Opcode != 0x2F17 ||
				firstPush.Length != 2 ||
				secondPush.Opcode != 0x2F2F ||
				secondPush.Length != 4 ||
				(firstPop.Opcode & 0xF1FF) != 0x201F ||
				firstPop.Length != 2 ||
				(secondPop.Opcode & 0xF1FF) != 0x205F ||
				secondPop.Length != 2 ||
				IsReferencedLabelAt(secondPush.Offset) ||
				IsReferencedLabelAt(firstPop.Offset) ||
				IsReferencedLabelAt(secondPop.Offset))
			{
				continue;
			}

			var sourceDisplacement = unchecked((short)secondPush.ExtensionWord);
			var originalDisplacement = sourceDisplacement - 4;
			if (originalDisplacement < short.MinValue || originalDisplacement > short.MaxValue)
			{
				continue;
			}

			var dataRegister = (firstPop.Opcode >> 9) & 7;
			var addressRegister = (secondPop.Opcode >> 9) & 7;
			var replacementLength = originalDisplacement == 0 ? 4 : 6;
			var endOffset = secondPop.Offset + secondPop.Length;

			foreach (var label in _buffer.Labels.Keys.ToArray())
			{
				if (_buffer.Labels[label] > firstPush.Offset &&
					_buffer.Labels[label] < endOffset)
				{
					_buffer.Labels[label] = firstPush.Offset;
				}
			}

			_buffer.WriteWord(
				firstPush.Offset,
				(ushort)(originalDisplacement == 0
					? 0x2017 | (dataRegister << 9)
					: 0x202F | (dataRegister << 9)));
			if (originalDisplacement != 0)
			{
				_buffer.WriteWord(firstPush.Offset + 2, unchecked((ushort)originalDisplacement));
			}

			_buffer.WriteWord(firstPush.Offset + replacementLength - 2, (ushort)(0x2057 | (addressRegister << 9)));
			_buffer.RemoveBytes(firstPush.Offset + replacementLength, endOffset - firstPush.Offset - replacementLength);
			return true;
		}

		return false;
	}

	private bool TryReplaceZeroRegisterRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var zero = instructions[index];
			var push = instructions[index + 1];
			var pop = instructions[index + 2];
			var dataRegister = (zero.Opcode >> 9) & 7;
			if ((zero.Opcode & 0xF100) != 0x7000 ||
				(zero.Opcode & 0xFF) != 0 ||
				zero.Length != 2 ||
				push.Opcode != (ushort)(0x2F00 | dataRegister) ||
				push.Length != 2 ||
				pop.Opcode != (ushort)(0x201F | (dataRegister << 9)) ||
				pop.Length != 2 ||
				IsReferencedLabelAt(push.Offset) ||
				IsReferencedLabelAt(pop.Offset))
			{
				continue;
			}

			MoveLabelsToOffset(push.Offset, pop.Offset + pop.Length, zero.Offset);
			_buffer.WriteWord(zero.Offset, (ushort)(0x4280 | dataRegister));
			_buffer.RemoveBytes(zero.Offset + 2, push.Length + pop.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveStackDuplicateRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var duplicate = instructions[index];
			var pop = instructions[index + 1];
			if (duplicate.Opcode != 0x2F17 ||
				duplicate.Length != 2 ||
				pop.Length != 2 ||
				IsReferencedLabelAt(pop.Offset) ||
				!TryGetRegisterStackPop(pop.Opcode, out var destinationIsAddress, out var destinationRegister))
			{
				continue;
			}

			var opcode = destinationIsAddress
				? 0x2057 | (destinationRegister << 9)
				: 0x2017 | (destinationRegister << 9);
			MoveLabelsToOffset(pop.Offset, pop.Offset + pop.Length, duplicate.Offset);
			_buffer.WriteWord(duplicate.Offset, (ushort)opcode);
			_buffer.RemoveBytes(duplicate.Offset + duplicate.Length, pop.Length);
			return true;
		}

		return false;
	}

	private bool TryReplaceAddressStackRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var pushAddress = instructions[index];
			var duplicate = instructions[index + 1];
			var popData = instructions[index + 2];
			if ((pushAddress.Opcode & 0xFFF8) != 0x4850 ||
				pushAddress.Length != 2 ||
				duplicate.Opcode != 0x2F17 ||
				duplicate.Length != 2 ||
				(popData.Opcode & 0xF1FF) != 0x201F ||
				popData.Length != 2 ||
				IsReferencedLabelAt(duplicate.Offset) ||
				IsReferencedLabelAt(popData.Offset))
			{
				continue;
			}

			var addressRegister = pushAddress.Opcode & 7;
			var dataRegister = (popData.Opcode >> 9) & 7;
			MoveLabelsToOffset(duplicate.Offset, popData.Offset + popData.Length, pushAddress.Offset);
			_buffer.WriteWord(pushAddress.Offset, (ushort)(0x2F08 | addressRegister));
			_buffer.WriteWord(
				pushAddress.Offset + 2,
				(ushort)(0x2008 | (dataRegister << 9) | addressRegister));
			_buffer.RemoveBytes(pushAddress.Offset + 4, duplicate.Length + popData.Length - 2);
			return true;
		}

		return false;
	}

	private bool TryRemoveRegisterStackRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var push = instructions[index];
			var pop = instructions[index + 1];
			if (push.Length != 2 ||
				pop.Length != 2 ||
				IsReferencedLabelAt(pop.Offset) ||
				!TryGetRegisterStackPush(push.Opcode, out var sourceIsAddress, out var sourceRegister) ||
				!TryGetRegisterStackPop(pop.Opcode, out var destinationIsAddress, out var destinationRegister))
			{
				continue;
			}

			var opcode = (sourceIsAddress, destinationIsAddress) switch
			{
				(false, false) => 0x2000 | (destinationRegister << 9) | sourceRegister,
				(false, true) => 0x2040 | (destinationRegister << 9) | sourceRegister,
				(true, false) => 0x2008 | (destinationRegister << 9) | sourceRegister,
				(true, true) => 0x2048 | (destinationRegister << 9) | sourceRegister
			};
			MoveLabelsToOffset(push.Offset + push.Length, pop.Offset + pop.Length, push.Offset);
			_buffer.WriteWord(push.Offset, (ushort)opcode);
			_buffer.RemoveBytes(push.Offset + 2, pop.Length);
			return true;
		}

		return false;
	}

	private void MoveLabelsToOffset(int startOffset, int endOffset, int targetOffset)
	{
		foreach (var label in _buffer.Labels.Keys.ToArray())
		{
			if (_buffer.Labels[label] >= startOffset && _buffer.Labels[label] < endOffset)
			{
				_buffer.Labels[label] = targetOffset;
			}
		}
	}

	private static bool TryGetRegisterStackPush(ushort opcode, out bool addressRegister, out int register)
	{
		if ((opcode & 0xFFF8) == 0x2F00)
		{
			addressRegister = false;
			register = opcode & 7;
			return true;
		}
		if ((opcode & 0xFFF8) == 0x2F08)
		{
			addressRegister = true;
			register = opcode & 7;
			return true;
		}

		addressRegister = false;
		register = 0;
		return false;
	}

	private static bool TryGetRegisterStackPop(ushort opcode, out bool addressRegister, out int register)
	{
		if ((opcode & 0xF1FF) == 0x201F)
		{
			addressRegister = false;
			register = (opcode >> 9) & 7;
			return true;
		}
		if ((opcode & 0xF1FF) == 0x205F)
		{
			addressRegister = true;
			register = (opcode >> 9) & 7;
			return true;
		}

		addressRegister = false;
		register = 0;
		return false;
	}

	private bool IsReferencedLabelAt(int offset)
	{
		return _buffer.Branches.Any(branch =>
			_buffer.Labels.TryGetValue(branch.Target, out var target) && target == offset) ||
			_buffer.Addresses.Any(address =>
				!address.External &&
				_buffer.Labels.TryGetValue(address.Target, out var target) && target == offset) ||
			_buffer.PcRelative.Any(reference =>
				_buffer.Labels.TryGetValue(reference.Target, out var target) && target == offset);
	}

	private bool TryRemoveRedundantStackShuffle()
	{
		var instructions = _assembler.GetInstructionStream();
		var dataflow = M68kInstructionDataflow.Analyze(_assembler);
		for (var index = 0; index + 4 < instructions.Count; index++)
		{
			var firstPush = instructions[index];
			var constant = instructions[index + 1];
			var secondPush = instructions[index + 2];
			var firstPop = instructions[index + 3];
			var secondPop = instructions[index + 4];
			if (firstPush.Length != 2 ||
				(firstPush.Opcode & 0xFFF8) != 0x2F00 ||
				constant.Length != 2 ||
				(constant.Opcode & 0xF1FF) != 0x7000 ||
				secondPush.Length != 2 ||
				secondPush.Opcode != (ushort)(0x2F00 | ((constant.Opcode >> 9 & 7))) ||
				firstPop.Length != 2 ||
				(firstPop.Opcode & 0xF1FF) != 0x201F ||
				secondPop.Length != 2 ||
				secondPop.Opcode != (ushort)(0x201F | ((firstPush.Opcode & 7) << 9)) ||
				!dataflow.TryGetFacts(constant.Offset, out var constantFacts) ||
				!constantFacts.ConditionsAreDeadAfter ||
				IsReferencedLabelAt(constant.Offset) ||
				IsReferencedLabelAt(secondPush.Offset) ||
				IsReferencedLabelAt(firstPop.Offset) ||
				IsReferencedLabelAt(secondPop.Offset))
			{
				continue;
			}

			_buffer.WriteWord(
				constant.Offset,
				(constant.Opcode & 0xF1FF) | (((firstPop.Opcode >> 9) & 7) << 9));
			foreach (var label in _buffer.Labels.Keys.ToArray())
			{
				if (_buffer.Labels[label] > firstPush.Offset &&
					_buffer.Labels[label] < secondPop.Offset + secondPop.Length)
				{
					_buffer.Labels[label] = constant.Offset;
				}
			}

			foreach (var instruction in new[] { secondPop, firstPop, secondPush, firstPush })
			{
				_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			}
			return true;
		}

		return false;
	}

	private bool TryReplaceCompareRegisterZeroWithTest()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var zero = instructions[index];
			var compare = instructions[index + 1];
			if (zero.Length != 2 ||
				(zero.Opcode & 0xF1FF) != 0x7000 ||
				(zero.Opcode & 0xFF) != 0 ||
				(compare.Opcode & 0xF1C0) != 0xB080 ||
				compare.Length != 2 ||
				(compare.Opcode & 7) != (zero.Opcode >> 9 & 7) ||
				IsReferencedLabelAt(zero.Offset) ||
				IsReferencedLabelAt(compare.Offset))
			{
				continue;
			}

			var destination = (compare.Opcode >> 9) & 7;
			_buffer.WriteWord(compare.Offset, (ushort)(0x4A80 | destination));
			foreach (var label in _buffer.Labels.Keys.ToArray())
			{
				if (_buffer.Labels[label] == zero.Offset)
				{
					_buffer.Labels[label] = compare.Offset;
				}
			}
			_buffer.RemoveBytes(zero.Offset, zero.Length);
			return true;
		}

		return false;
	}

	private bool TryPromoteBranchStackSpillToD0()
	{
		var instructions = _assembler.GetInstructionStream();
		var instructionIndexByOffset = instructions
			.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(static item => item.Offset, static item => item.index);

		for (var popIndex = 0; popIndex < instructions.Count; popIndex++)
		{
			var pop = instructions[popIndex];
			if (pop.Opcode != 0x2F5F || pop.Length != 4 ||
				!TryGetLabelsAt(pop.Offset, out var targetLabels))
			{
				continue;
			}

			var incomingBranches = _buffer.Branches
				.Where(branch => targetLabels.Contains(branch.Target))
				.ToArray();
			if (incomingBranches.Length == 0)
			{
				continue;
			}

			var pushOffsets = new HashSet<int>();
			if (HasFallthroughPredecessor(instructions, popIndex))
			{
				if (!TryGetPreviousPushD0(instructions, popIndex, out var fallthroughPush))
				{
					continue;
				}

				pushOffsets.Add(fallthroughPush.Offset);
			}

			var valid = true;
			foreach (var branch in incomingBranches)
			{
				if (!instructionIndexByOffset.TryGetValue(branch.OpcodeOffset, out var branchIndex) ||
					instructions[branchIndex].Kind != M68kInstructionKind.UnconditionalBranch ||
					!TryGetPreviousPushD0(instructions, branchIndex, out var branchPush))
				{
					valid = false;
					break;
				}

				pushOffsets.Add(branchPush.Offset);
			}

			if (!valid)
			{
				continue;
			}

			// Replace MOVE.L (A7)+,d16(A7) with MOVE.L D0,d16(A7).
			// Removing the balanced pushes leaves A7 at the same depth.
			_buffer.WriteWord(pop.Offset, 0x2F40);
			foreach (var pushOffset in pushOffsets.OrderByDescending(static offset => offset))
			{
				_buffer.RemoveBytes(pushOffset, 2);
			}
			return true;
		}

		return false;
	}

	private bool TryReplaceZeroAddressMove()
	{
		var instructions = _assembler.GetInstructionStream();
		foreach (var instruction in instructions)
		{
			if ((instruction.Opcode & 0xF1FF) != 0x207C ||
				instruction.Length != 6 ||
				instruction.ExtensionLong != 0 ||
				_buffer.Addresses.Any(address => address.Offset == instruction.Offset + 2))
			{
				continue;
			}

			var addressRegister = (instruction.Opcode >> 9) & 7;
			_buffer.WriteWord(
				instruction.Offset,
				0x91C8 | (addressRegister << 9) | addressRegister);
			_buffer.RemoveBytes(instruction.Offset + 2, 4);
			return true;
		}

		return false;
	}

	private bool TryGetLabelsAt(int offset, out HashSet<string> labels)
	{
		labels = _buffer.Labels
			.Where(label => label.Value == offset)
			.Select(static label => label.Key)
			.ToHashSet(StringComparer.Ordinal);
		return labels.Count != 0;
	}

	private static bool HasFallthroughPredecessor(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int instructionIndex) =>
		instructionIndex > 0 &&
		instructions[instructionIndex - 1].Kind is not
			M68kInstructionKind.UnconditionalBranch and not
			M68kInstructionKind.Return;

	private static bool TryGetPreviousPushD0(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int instructionIndex,
		out M68kEmittedInstruction push)
	{
		push = default;
		if (instructionIndex == 0)
		{
			return false;
		}

		var candidate = instructions[instructionIndex - 1];
		if (candidate.Opcode != 0x2F00 || candidate.Length != 2)
		{
			return false;
		}

		push = candidate;
		return true;
	}

	private bool TryRemoveDeadTest(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xFFF8) != 0x4A80 ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!facts.ConditionsAreDeadAfter)
			{
				continue;
			}

			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveDeadInstruction(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (!instruction.IsDecoded ||
				instruction.Kind != M68kInstructionKind.Normal ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				continue;
			}

			var effects = facts.Effects;
			var producesValue =
				effects.DefinesData != 0 ||
				effects.DefinesAddress != 0 ||
				effects.WritesConditions != M68kConditionCodeSet.None;
			if (!effects.CanRemoveWhenOutputsDead ||
				effects.IsBarrier ||
				!producesValue ||
				(effects.DefinesData & facts.LiveDataAfter) != 0 ||
				(effects.DefinesAddress & facts.LiveAddressAfter) != 0 ||
				(effects.WritesConditions & facts.LiveConditionsAfter) != 0 ||
				effects.ReadsMemory != M68kMemorySet.None ||
				effects.WritesMemory != M68kMemorySet.None ||
				effects.StackDelta != 0 ||
				HasInternalLabel(instruction))
			{
				continue;
			}

			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			return true;
		}

		return false;
	}

	private bool HasInternalLabel(M68kEmittedInstruction instruction)
	{
		var end = instruction.Offset + instruction.Length;
		return _buffer.Labels.Values.Any(offset =>
			offset > instruction.Offset && offset < end);
	}

	private bool TryNarrowAddition(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!facts.ConditionsAreDeadAfter)
			{
				continue;
			}

			var opcode = instruction.Opcode;
			if ((opcode & 0xFFF8) == 0x0680 &&
				instruction.Length == 6)
			{
				var destination = opcode & 7;
				if (!PreservesUpperWord(
					dataflow.GetDataValueBefore(instruction.Offset, destination),
					M68kValueRange.Exact(instruction.ExtensionLong)))
				{
					continue;
				}

				_buffer.WriteWord(instruction.Offset, 0x0640 | destination); // ADDI.L -> ADDI.W
				_buffer.RemoveBytes(instruction.Offset + 2, 2);
				return true;
			}

			if ((opcode & 0xF1F8) != 0xD080)
			{
				continue;
			}

			var source = opcode & 7;
			var destinationRegister = (opcode >> 9) & 7;
			if (!PreservesUpperWord(
				dataflow.GetDataValueBefore(instruction.Offset, destinationRegister),
				dataflow.GetDataValueBefore(instruction.Offset, source)))
			{
				continue;
			}

			_buffer.WriteWord(instruction.Offset, opcode - 0x40); // ADD.L Dm,Dn -> ADD.W Dm,Dn
			return true;
		}

		return false;
	}

	private static bool PreservesUpperWord(
		M68kValueRange destination,
		M68kValueRange source)
	{
		if (!TrySplitWordRange(destination, out var destinationLowMinimum, out var destinationLowMaximum, out _) ||
			!TrySplitWordRange(source, out var sourceLowMinimum, out var sourceLowMaximum, out var sourceUpper))
		{
			return false;
		}

		var minimumCarry = destinationLowMinimum + sourceLowMinimum > ushort.MaxValue;
		var maximumCarry = destinationLowMaximum + sourceLowMaximum > ushort.MaxValue;
		return minimumCarry == maximumCarry &&
			((sourceUpper + (minimumCarry ? 1u : 0u)) & ushort.MaxValue) == 0;
	}

	private static bool TrySplitWordRange(
		M68kValueRange range,
		out uint lowMinimum,
		out uint lowMaximum,
		out uint upper)
	{
		lowMinimum = 0;
		lowMaximum = 0;
		upper = 0;
		if (!range.IsKnown ||
			(range.Minimum >> 16) != (range.Maximum >> 16))
		{
			return false;
		}

		lowMinimum = range.Minimum & ushort.MaxValue;
		lowMaximum = range.Maximum & ushort.MaxValue;
		upper = range.Minimum >> 16;
		return true;
	}

	private bool TryNarrowLogicalImmediate(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				(facts.LiveConditionsAfter & (M68kConditionCodeSet.Negative | M68kConditionCodeSet.Zero)) !=
				M68kConditionCodeSet.None ||
				instruction.Length < 6 ||
				!TryReadLong(instruction.Offset + 2, out var immediate))
			{
				continue;
			}

			var opcode = instruction.Opcode;
			var dataRegister = opcode & 7;
			ushort wordOpcode;
			if ((opcode & 0xFFF8) == 0x0280 && (immediate & 0xFFFF0000u) == 0xFFFF0000u)
			{
				wordOpcode = (ushort)(0x0240 | dataRegister); // ANDI.L -> ANDI.W
			}
			else if ((opcode & 0xFFF8) == 0x0080 && (immediate & 0xFFFF0000u) == 0)
			{
				wordOpcode = (ushort)(0x0040 | dataRegister); // ORI.L -> ORI.W
			}
			else if ((opcode & 0xFFF8) == 0x0A80 && (immediate & 0xFFFF0000u) == 0)
			{
				wordOpcode = (ushort)(0x0A40 | dataRegister); // EORI.L -> EORI.W
			}
			else
			{
				continue;
			}

			_buffer.WriteWord(instruction.Offset, wordOpcode);
			_buffer.RemoveBytes(instruction.Offset + 2, 2);
			return true;
		}

		return false;
	}

	private bool TryFoldTailReturn()
	{
		for (var index = _buffer.Branches.Count - 1; index >= 0; index--)
		{
			var branch = _buffer.Branches[index];
			if (branch.OpcodeOffset + 5 >= _buffer.Bytes.Count ||
				_buffer.ReadWord(branch.OpcodeOffset) != 0x6100 ||
				_buffer.ReadWord(branch.OpcodeOffset + 4) != 0x4E75 ||
				_buffer.HasLabelAt(branch.OpcodeOffset + 4))
			{
				continue;
			}

			_buffer.WriteWord(branch.OpcodeOffset, 0x6000); // BSR.W -> BRA.W
			_buffer.Branches.RemoveAt(index);
			_buffer.Branches.Insert(index, branch);
			_buffer.RemoveBytes(branch.OpcodeOffset + 4, 2);
			return true;
		}

		for (var index = _buffer.Addresses.Count - 1; index >= 0; index--)
		{
			var address = _buffer.Addresses[index];
			var opcodeOffset = address.Offset - 2;
			if (opcodeOffset < 0 ||
				opcodeOffset + 7 >= _buffer.Bytes.Count ||
				_buffer.ReadWord(opcodeOffset) != 0x4EB9 ||
				_buffer.ReadWord(opcodeOffset + 6) != 0x4E75 ||
				_buffer.HasLabelAt(opcodeOffset + 6) ||
				address.External)
			{
				continue;
			}

			_buffer.WriteWord(opcodeOffset, 0x4EF9); // JSR -> JMP
			_buffer.RemoveBytes(opcodeOffset + 6, 2);
			return true;
		}

		return false;
	}

	private bool TryReplaceCompareZeroWithTest()
	{
		for (var offset = 0; offset + 3 < _buffer.Bytes.Count; offset += 2)
		{
			var opcode = _buffer.ReadWord(offset);
			if ((opcode & 0xFFF8) == 0x0C80 &&
				_buffer.ReadLong(offset + 2) == 0)
			{
				_buffer.WriteWord(offset, (ushort)(0x4A80 | (opcode & 7))); // CMPI.L #0,Dn -> TST.L Dn
				_buffer.RemoveBytes(offset + 2, 4);
				return true;
			}

			if ((opcode & 0xFFF8) == 0x0C40 &&
				_buffer.ReadWord(offset + 2) == 0)
			{
				_buffer.WriteWord(offset, (ushort)(0x4A40 | (opcode & 7))); // CMPI.W #0,Dn -> TST.W Dn
				_buffer.RemoveBytes(offset + 2, 2);
				return true;
			}

			if ((opcode & 0xFFF8) == 0x0C00 &&
				_buffer.ReadWord(offset + 2) == 0)
			{
				_buffer.WriteWord(offset, (ushort)(0x4A00 | (opcode & 7))); // CMPI.B #0,Dn -> TST.B Dn
				_buffer.RemoveBytes(offset + 2, 2);
				return true;
			}
		}

		return false;
	}

	private bool TryRemoveBranchToNextLabel()
	{
		for (var index = _buffer.Branches.Count - 1; index >= 0; index--)
		{
			var branch = _buffer.Branches[index];
			var opcode = _buffer.ReadWord(branch.OpcodeOffset);
			if ((opcode & 0xF000) != 0x6000 ||
				branch.OpcodeOffset + 3 >= _buffer.Bytes.Count ||
				!_buffer.Labels.TryGetValue(branch.Target, out var targetOffset) ||
				targetOffset != branch.OpcodeOffset + 4)
			{
				continue;
			}

			_buffer.Branches.RemoveAt(index);
			_buffer.RemoveBytes(branch.OpcodeOffset, 4);
			return true;
		}

		return false;
	}

	private bool TryRemoveRedundantTest()
	{
		for (var offset = 0; offset + 1 < _buffer.Bytes.Count; offset += 2)
		{
			if (!TryGetFlagSettingMoveLength(
				offset,
				out var moveLength,
				out var destination))
			{
				continue;
			}

			var testOffset = offset + moveLength;
			if (testOffset + 1 >= _buffer.Bytes.Count ||
				_buffer.HasLabelAt(testOffset))
			{
				continue;
			}

			var test = _buffer.ReadWord(testOffset);
			if ((test & 0xFFF8) != 0x4A80 ||
				destination != (test & 7))
			{
				continue;
			}

			_buffer.RemoveBytes(testOffset, 2);
			return true;
		}

		return false;
	}

	private bool TryCanonicalizeAddressAdjustments()
	{
		for (var offset = 0; offset + 1 < _buffer.Bytes.Count; offset += 2)
		{
			if (!TryGetAddressAdjustment(offset, out var first))
			{
				continue;
			}

			var secondOffset = offset + first.Length;
			if (secondOffset < _buffer.Bytes.Count &&
				!_buffer.HasLabelAt(secondOffset) &&
				TryGetAddressAdjustment(secondOffset, out var second) &&
				first.Register == second.Register &&
				!_buffer.Labels.Values.Any(label =>
					label > offset &&
					label < secondOffset + second.Length))
			{
				var total = checked(first.Displacement + second.Displacement);
				if (total >= short.MinValue && total <= short.MaxValue)
				{
					var replacementLength = WriteAddressAdjustment(offset, first.Register, total);
					_buffer.RemoveBytes(
						offset + replacementLength,
						first.Length + second.Length - replacementLength);
					return true;
				}
			}

			if (first.Length == 4 &&
				Math.Abs(first.Displacement) <= 8 &&
				!_buffer.HasLabelAt(offset + 2))
			{
				WriteAddressAdjustment(offset, first.Register, first.Displacement);
				_buffer.RemoveBytes(offset + 2, 2);
				return true;
			}
		}

		return false;
	}

	private bool TryGetAddressAdjustment(int offset, out AddressAdjustment adjustment)
	{
		adjustment = default;
		if (offset + 1 >= _buffer.Bytes.Count)
		{
			return false;
		}

		var opcode = _buffer.ReadWord(offset);
		if ((opcode & 0xF1F8) is 0x5088 or 0x5188)
		{
			var displacement = QuickCount(opcode);
			if ((opcode & 0x0100) != 0)
			{
				displacement = -displacement;
			}

			adjustment = new AddressAdjustment(opcode & 7, 2, displacement);
			return true;
		}

		if ((opcode & 0xF1F8) == 0x41E8 &&
			((opcode >> 9) & 7) == (opcode & 7) &&
			TryReadWord(offset + 2, out var displacementWord))
		{
			adjustment = new AddressAdjustment(
				opcode & 7,
				4,
				unchecked((short)displacementWord));
			return true;
		}

		if ((opcode & 0xF1FF) == 0xD0FC &&
			TryReadWord(offset + 2, out var immediateWord))
		{
			adjustment = new AddressAdjustment(
				(opcode >> 9) & 7,
				4,
				unchecked((short)immediateWord));
			return true;
		}

		return false;
	}

	private int WriteAddressAdjustment(int offset, int register, int displacement)
	{
		if (displacement == 0)
		{
			return 0;
		}

		if (displacement is >= 1 and <= 8)
		{
			_buffer.WriteWord(offset, EncodeAddressQuick(register, displacement, subtract: false));
			return 2;
		}

		if (displacement is >= -8 and <= -1)
		{
			_buffer.WriteWord(offset, EncodeAddressQuick(register, -displacement, subtract: true));
			return 2;
		}

		_buffer.WriteWord(offset, 0x41E8 | (register << 9) | register);
		_buffer.WriteWord(offset + 2, unchecked((ushort)displacement));
		return 4;
	}

	private bool TryGetFlagSettingMoveLength(
		int offset,
		out int length,
		out int destination)
	{
		length = 0;
		destination = 0;
		if (offset + 1 >= _buffer.Bytes.Count)
		{
			return false;
		}

		var opcode = _buffer.ReadWord(offset);
		if ((opcode & 0xFFC0) is 0x4880 or 0x48C0 or 0x49C0)
		{
			destination = opcode & 7;
			length = 2;
			return true;
		}

		if ((opcode & 0xF000) != 0x2000 ||
			((opcode >> 6) & 7) != 0)
		{
			return false;
		}

		destination = (opcode >> 9) & 7;
		var sourceMode = (opcode >> 3) & 7;
		var sourceRegister = opcode & 7;
		length = sourceMode switch
		{
			0 or 1 or 2 or 3 or 4 => 2,
			5 or 6 => 4,
			7 => sourceRegister is 0 or 2 or 3 ? 4 :
				sourceRegister is 1 or 4 ? 6 : 0,
			_ => 0
		};
		return length != 0 && offset + length <= _buffer.Bytes.Count;
	}

	private bool TryReadWord(int offset, out ushort value)
	{
		if (offset + 1 >= _buffer.Bytes.Count)
		{
			value = 0;
			return false;
		}

		value = _buffer.ReadWord(offset);
		return true;
	}

	private bool TryReadLong(int offset, out uint value)
	{
		if (offset + 3 >= _buffer.Bytes.Count)
		{
			value = 0;
			return false;
		}

		value = _buffer.ReadLong(offset);
		return true;
	}

	private static ushort EncodeAddressQuick(int register, int bytes, bool subtract) =>
		(ushort)((subtract ? 0x5188 : 0x5088) |
			((bytes == 8 ? 0 : bytes) << 9) |
			register);

	private static int QuickCount(ushort opcode)
	{
		var count = (opcode >> 9) & 7;
		return count == 0 ? 8 : count;
	}

	private readonly record struct AddressAdjustment(int Register, int Length, int Displacement);
}
