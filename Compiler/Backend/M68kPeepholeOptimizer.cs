/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kPeepholeOptimizer : IM68kOptimizerPass
{
	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;
	private readonly M68kCpuTarget _cpu;

	public M68kPeepholeOptimizer(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer,
		M68kCpuTarget cpu)
	{
		_assembler = assembler;
		_buffer = buffer;
		_cpu = cpu;
	}

	public void Run()
	{
		bool changed;
		do
		{
			var dataflow = M68kInstructionDataflow.Analyze(_assembler);
			changed =
				TryFoldTailReturn() ||
				TryLayoutColdTerminalBranch() ||
				TryRemoveBranchToNextLabel() ||
				TryPromoteBranchStackSpillToD0() ||
				TryReplaceZeroAddressMove() ||
				TryReplaceCompareZeroWithTest() ||
				TryRemoveRedundantTest() ||
				TryRemoveDeadTest(dataflow) ||
				TryRewriteByteStackPreservation(dataflow) ||
				TryFoldByteAddIntoFrameStore(dataflow) ||
				TryRemoveDeadInstruction(dataflow) ||
				TryRemoveDataRegisterRoundTrip(dataflow) ||
				TryFoldAddressToDataRegisterMove(dataflow) ||
				TryForwardMoveQuickThroughDataMove(dataflow) ||
				TryForwardMemoryLoadThroughStackToAddressRegister() ||
				TryNarrowAddition(dataflow) ||
				TryNarrowLogicalImmediate(dataflow) ||
				TryCanonicalizeAddressAdjustments() ||
				TryRewriteStackPreservation(dataflow) ||
				TryRemoveRedundantStackShuffle(dataflow) ||
				TryRemoveRedundantStackArgumentShuffle() ||
				TryRemoveZeroExtendedByteRegisterStackRoundTrip() ||
				TryRemoveByteRegisterStackRoundTrip() ||
				TryRemoveByteMaskAndTestBeforeFrameStore() ||
				TryReplaceClearStackRoundTrip() ||
				TryReplaceZeroStackPush(dataflow) ||
				TryReplaceZeroRegisterRoundTrip() ||
				TryReplaceAddressStackRoundTrip() ||
				TryRemoveStackDuplicateRoundTrip(dataflow) ||
				TryRemoveRegisterStackRoundTrip(dataflow) ||
				TryReplaceCompareRegisterZeroWithTest() ||
				TryRemoveRedundantRegisterSpill();
		}
		while (changed);
	}

	private bool TryLayoutColdTerminalBranch()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var conditionalIndex = 0; conditionalIndex < instructions.Count; conditionalIndex++)
		{
			var conditional = instructions[conditionalIndex];
			if (conditional.Kind != M68kInstructionKind.ConditionalBranch ||
				conditional.Length != 4 ||
				!TryGetBranch(conditional.Offset, out var conditionalBranch) ||
				!_buffer.Labels.TryGetValue(conditionalBranch.Target, out var failureOffset))
			{
				continue;
			}

			var successIndex = conditionalIndex + 1;
			var unconditionalIndex = successIndex;
			while (unconditionalIndex < instructions.Count &&
				instructions[unconditionalIndex].Offset < failureOffset)
			{
				if (!IsInternalBlockInstruction(
					instructions[unconditionalIndex],
					instructions[successIndex].Offset,
					failureOffset))
				{
					break;
				}

				unconditionalIndex++;
			}

			if (unconditionalIndex >= instructions.Count ||
				instructions[unconditionalIndex].Offset != failureOffset - 4)
			{
				continue;
			}

			var unconditional = instructions[unconditionalIndex];
			if (unconditional.Kind != M68kInstructionKind.UnconditionalBranch ||
				unconditional.Length != 4 ||
				!TryGetBranch(unconditional.Offset, out var unconditionalBranch) ||
				!_buffer.Labels.TryGetValue(unconditionalBranch.Target, out var tailOffset) ||
				unconditional.Offset + unconditional.Length != failureOffset ||
				!TryGetTerminalBlockEnd(tailOffset, failureOffset, instructions, out var tailEnd))
			{
				continue;
			}

			var originalEndOffset = _buffer.Bytes.Count;
			var tailLength = tailEnd - tailOffset;
			var failureLength = tailOffset - failureOffset;
			var tail = _buffer.Bytes.GetRange(tailOffset, tailLength);
			var failure = _buffer.Bytes.GetRange(failureOffset, failureLength);
			var suffix = _buffer.Bytes.GetRange(tailEnd, originalEndOffset - tailEnd);
			var newFailureOffset = unconditional.Offset + tailLength;
			var newSuffixOffset = newFailureOffset + failureLength + tailLength;

			_buffer.Bytes.RemoveRange(unconditional.Offset, _buffer.Bytes.Count - unconditional.Offset);
			_buffer.Bytes.AddRange(tail);
			_buffer.Bytes.AddRange(failure);
			_buffer.Bytes.AddRange(tail);
			_buffer.Bytes.AddRange(suffix);

			foreach (var label in _buffer.Labels.Keys.ToArray())
			{
				var offset = _buffer.Labels[label];
				if (offset == failureOffset)
				{
					_buffer.Labels[label] = newFailureOffset;
				}
				else if (offset == unconditional.Offset)
				{
					_buffer.Labels[label] = unconditional.Offset;
				}
				else if (offset == tailOffset)
				{
					_buffer.Labels[label] = unconditional.Offset;
				}
				else if (offset > failureOffset && offset < tailOffset)
				{
					_buffer.Labels[label] = newFailureOffset + offset - failureOffset;
				}
				else if (offset > tailOffset && offset < tailEnd)
				{
					_buffer.Labels[label] = unconditional.Offset + offset - tailOffset;
				}
				else if (offset == originalEndOffset)
				{
					_buffer.Labels[label] = newSuffixOffset + (offset - tailEnd);
				}
				else if (offset >= tailEnd)
				{
					_buffer.Labels[label] = newSuffixOffset + (offset - tailEnd);
				}
			}

			_buffer.Branches.RemoveAll(branch => branch.OpcodeOffset == unconditional.Offset);
			for (var index = 0; index < _buffer.Branches.Count; index++)
			{
				var branch = _buffer.Branches[index];
				if (branch.OpcodeOffset >= tailEnd)
				{
					_buffer.Branches[index] = branch with
					{
						OpcodeOffset = newSuffixOffset + branch.OpcodeOffset - tailEnd
					};
				}
			}

			for (var index = 0; index < _buffer.Addresses.Count; index++)
			{
				var address = _buffer.Addresses[index];
				if (address.Offset >= tailEnd)
				{
					_buffer.Addresses[index] = address with
					{
						Offset = newSuffixOffset + address.Offset - tailEnd
					};
				}
			}

			for (var index = 0; index < _buffer.PcRelative.Count; index++)
			{
				var reference = _buffer.PcRelative[index];
				if (reference.DisplacementOffset >= tailEnd)
				{
					_buffer.PcRelative[index] = reference with
					{
						DisplacementOffset = newSuffixOffset + reference.DisplacementOffset - tailEnd
					};
				}
			}
			return true;
		}

		return false;
	}

	private static bool IsInternalBlockInstruction(
		M68kEmittedInstruction instruction,
		int blockStart,
		int blockEnd)
	{
		if (instruction.Kind is M68kInstructionKind.Normal or M68kInstructionKind.Call)
		{
			return true;
		}

		return instruction.TargetOffset is { } targetOffset &&
			targetOffset >= blockStart &&
			targetOffset < blockEnd;
	}

	private bool TryGetTerminalBlockEnd(
		int tailOffset,
		int failureOffset,
		IReadOnlyList<M68kEmittedInstruction> instructions,
		out int tailEnd)
	{
		tailEnd = 0;
		var candidateTailEnd = _buffer.Labels
			.Where(item =>
				item.Value > tailOffset &&
				item.Key.EndsWith(":end", StringComparison.Ordinal))
			.Select(static item => item.Value)
			.DefaultIfEmpty(_buffer.Labels.Values
				.Where(offset => offset > tailOffset)
				.DefaultIfEmpty(_buffer.Bytes.Count)
				.Min())
			.Min();
		if (candidateTailEnd <= tailOffset ||
			candidateTailEnd - 2 < tailOffset ||
			_buffer.ReadWord(candidateTailEnd - 2) != 0x4E75 ||
			_buffer.HasLabelAt(tailOffset) == false ||
			_buffer.HasLabelAt(failureOffset) == false ||
			_buffer.Branches.Any(branch =>
				branch.OpcodeOffset >= failureOffset &&
				branch.OpcodeOffset < candidateTailEnd) ||
			_buffer.Addresses.Any(address =>
				address.Offset >= failureOffset && address.Offset < candidateTailEnd) ||
			_buffer.PcRelative.Any(reference =>
				reference.DisplacementOffset >= failureOffset &&
				reference.DisplacementOffset < candidateTailEnd))
		{
			return false;
		}

		var tailInstructions = instructions
			.Where(instruction => instruction.Offset >= tailOffset && instruction.Offset < candidateTailEnd)
			.ToArray();
		if (tailInstructions.Length == 0 ||
			 tailInstructions[^1].Kind != M68kInstructionKind.Return ||
			!tailInstructions.All(instruction =>
				instruction.Kind is M68kInstructionKind.Normal or M68kInstructionKind.Return))
		{
			return false;
		}

		tailEnd = candidateTailEnd;
		return true;
	}

	private bool TryGetBranch(int offset, out BranchFixup branch)
	{
		branch = default;
		for (var index = 0; index < _buffer.Branches.Count; index++)
		{
			if (_buffer.Branches[index].OpcodeOffset == offset)
			{
				branch = _buffer.Branches[index];
				return true;
			}
		}

		return false;
	}

	private bool TryRemoveDataRegisterRoundTrip(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if ((first.Opcode & 0xF1F8) != 0x2000 ||
				first.Length != 2 ||
				(second.Opcode & 0xF1F8) != 0x2000 ||
				second.Length != 2 ||
				IsReferencedLabelAt(first.Offset) ||
				IsReferencedLabelAt(second.Offset))
			{
				continue;
			}

			var sourceRegister = first.Opcode & 7;
			var temporaryRegister = (first.Opcode >> 9) & 7;
			if (sourceRegister == temporaryRegister ||
				(second.Opcode & 7) != temporaryRegister ||
				((second.Opcode >> 9) & 7) != sourceRegister ||
				!dataflow.TryGetFacts(second.Offset, out var facts))
			{
				continue;
			}

			if (facts.LiveConditionsAfter == M68kConditionCodeSet.None)
			{
				MoveLabelsToOffset(
					first.Offset + first.Length,
					second.Offset + second.Length,
					first.Offset);
				_buffer.RemoveBytes(second.Offset, second.Length);
				_buffer.RemoveBytes(first.Offset, first.Length);
			}
			else if ((facts.LiveConditionsAfter &
				(M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry)) ==
				M68kConditionCodeSet.None)
			{
				_buffer.WriteWord(first.Offset, (ushort)(0x4A80 | sourceRegister));
				_buffer.RemoveBytes(second.Offset, second.Length);
			}
			else
			{
				_buffer.WriteWord(
					first.Offset,
					(ushort)(0x2000 | (sourceRegister << 9) | sourceRegister));
				_buffer.RemoveBytes(second.Offset, second.Length);
			}

			return true;
		}

		return false;
	}

	private bool TryFoldAddressToDataRegisterMove(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var moveAddress = instructions[index];
			var moveData = instructions[index + 1];
			if ((moveAddress.Opcode & 0xF1F8) != 0x2040 ||
				moveAddress.Length != 2 ||
				(moveData.Opcode & 0xF1F8) != 0x2008 ||
				moveData.Length != 2 ||
				IsReferencedLabelAt(moveAddress.Offset) ||
				IsReferencedLabelAt(moveData.Offset))
			{
				continue;
			}

			var sourceDataRegister = moveAddress.Opcode & 7;
			var addressRegister = (moveAddress.Opcode >> 9) & 7;
			var destinationDataRegister = (moveData.Opcode >> 9) & 7;
			if (addressRegister == 7 ||
				(moveData.Opcode & 7) != addressRegister ||
				!dataflow.TryGetFacts(moveData.Offset, out var facts) ||
				(facts.LiveAddressAfter & (1 << addressRegister)) != 0)
			{
				continue;
			}

			MoveLabelsToOffset(
				moveAddress.Offset + moveAddress.Length,
				moveData.Offset + moveData.Length,
				moveAddress.Offset);
			_buffer.WriteWord(
				moveAddress.Offset,
				(ushort)(0x2000 | (destinationDataRegister << 9) | sourceDataRegister));
			_buffer.RemoveBytes(moveData.Offset, moveData.Length);
			return true;
		}

		return false;
	}

	private bool TryForwardMoveQuickThroughDataMove(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var moveQuick = instructions[index];
			var moveData = instructions[index + 1];
			if ((moveQuick.Opcode & 0xF100) != 0x7000 ||
				moveQuick.Length != 2 ||
				(moveData.Opcode & 0xF1F8) != 0x2000 ||
				moveData.Length != 2 ||
				IsReferencedLabelAt(moveQuick.Offset) ||
				IsReferencedLabelAt(moveData.Offset))
			{
				continue;
			}

			var sourceRegister = moveQuick.Opcode >> 9 & 7;
			var destinationRegister = moveData.Opcode >> 9 & 7;
			if ((moveData.Opcode & 7) != sourceRegister ||
				sourceRegister != destinationRegister &&
				(!dataflow.TryGetFacts(moveData.Offset, out var facts) ||
				 (facts.LiveDataAfter & (1 << sourceRegister)) != 0))
			{
				continue;
			}

			_buffer.WriteWord(
				moveQuick.Offset,
				(ushort)(0x7000 | (destinationRegister << 9) | (moveQuick.Opcode & 0x00FF)));
			_buffer.RemoveBytes(moveData.Offset, moveData.Length);
			return true;
		}

		return false;
	}

	private bool TryForwardMemoryLoadThroughStackToAddressRegister()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 4 < instructions.Count; index++)
		{
			var load = instructions[index];
			var push = instructions[index + 1];
			var middle = instructions[index + 2];
			var pop = instructions[index + 3];
			var call = instructions[index + 4];
			if ((load.Opcode & 0xF1FF) != 0x2039 ||
				load.Length != 6 ||
				push.Length != 2 ||
				(middle.Opcode & 0xF1FF) != 0x207A ||
				middle.Length != 4 ||
				!IsStackAddressLoad(pop) ||
				!_buffer.Addresses.Any(address => address.Offset == load.Offset + 2) ||
				!_buffer.PcRelative.Any(reference => reference.DisplacementOffset == middle.Offset + 2) ||
				(call.Kind != M68kInstructionKind.Call &&
					(call.Opcode & 0xFFF8) != 0x4EE8) ||
				IsReferencedLabelAt(push.Offset) ||
				IsReferencedLabelAt(middle.Offset) ||
				IsReferencedLabelAt(pop.Offset) ||
				(push.Opcode & 0xFFF8) != 0x2F00)
			{
				continue;
			}

			var dataRegister = (load.Opcode >> 9) & 7;
			var pushedDataRegister = push.Opcode & 7;
			var addressRegister = (pop.Opcode >> 9) & 7;
			if (dataRegister != pushedDataRegister)
			{
				continue;
			}

			MoveLabelsToOffset(push.Offset, pop.Offset + pop.Length, load.Offset);
			_buffer.WriteWord(
				load.Offset,
				(ushort)(0x2079 | (addressRegister << 9)));
			_buffer.RemoveBytes(push.Offset, pop.Offset + pop.Length - push.Offset);
			return true;
		}

		return false;
	}

	private static bool IsStackAddressLoad(M68kEmittedInstruction instruction) =>
		(instruction.Opcode & 0xF1FF) == 0x2057 && instruction.Length == 2 ||
		(instruction.Opcode & 0xF1FF) == 0x206F &&
			instruction.Length == 4 &&
			instruction.ExtensionWord == 0;

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
					(facts.Effects.UsesAddress & (1 << 7)) != 0 ||
					(facts.Effects.DefinesAddress & (1 << 7)) != 0 ||
					facts.Effects.ReadsConditions != M68kConditionCodeSet.None ||
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

	private bool TryRewriteStackPreservation(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var store = instructions[index];
			var constant = instructions[index + 1];
			var shuffle = instructions[index + 2];
			if (store.Length != 2 ||
				(store.Opcode & 0xFFF8) != 0x2E80 ||
				constant.Length != 2 ||
				(constant.Opcode & 0xF100) != 0x7000 ||
				shuffle.Opcode != 0x2F6F ||
				shuffle.Length != 6 ||
				shuffle.ExtensionWord != 0 ||
				IsReferencedLabelAt(store.Offset) ||
				IsReferencedLabelAt(constant.Offset) ||
				IsReferencedLabelAt(shuffle.Offset))
			{
				continue;
			}

			var sourceRegister = store.Opcode & 7;
			var constantRegister = (constant.Opcode >> 9) & 7;
			if (sourceRegister != constantRegister)
			{
				continue;
			}

			if (_cpu == M68kCpuTarget.M68040 &&
				TryGetScratchRegister(instructions, index + 2, sourceRegister, dataflow, out var scratchRegister))
			{
				_buffer.WriteWord(
					store.Offset,
					0x2000 | (scratchRegister << 9) | sourceRegister);
				_buffer.WriteWord(
					shuffle.Offset,
					0x2E80 | sourceRegister);
				_buffer.WriteWord(
					shuffle.Offset + 2,
					0x2F40 | scratchRegister);
				return true;
			}

			// Store the original value in its final slot before the constant overwrites
			// the source register, then materialize the constant in the first slot.
			_buffer.WriteWord(store.Offset, 0x2F40 | sourceRegister);
			_buffer.WriteWord(store.Offset + 2, _buffer.ReadWord(shuffle.Offset + 4));
			_buffer.WriteWord(store.Offset + 4, constant.Opcode);
			_buffer.WriteWord(
				store.Offset + 6,
				0x2E80 | sourceRegister);
			_buffer.RemoveBytes(store.Offset + 8, 2);
			return true;
		}

		return false;
	}

	private bool TryRewriteByteStackPreservation(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 6 < instructions.Count; index++)
		{
			var save = instructions[index];
			var push = instructions[index + 1];
			var reload = instructions[index + 2];
			var copy = instructions[index + 3];
			var restore = instructions[index + 4];
			var add = instructions[index + 5];
			var store = instructions[index + 6];
			if (!TryGetByteFrameLoad(save, out var preservedRegister, out _) ||
				!TryGetByteRegisterStackPush(push.Opcode, out var pushedRegister) ||
				!TryGetByteFrameLoad(reload, out var reloadedRegister, out var reloadDisplacement) ||
				!TryGetByteDataMove(copy, out var copiedRegister, out var temporaryRegister) ||
				!TryGetByteRegisterStackPop(restore.Opcode, out var restoredRegister) ||
				!TryGetByteDataAdd(add, out var addSourceRegister, out var addDestinationRegister) ||
				!TryGetByteFrameStore(store, out var storedRegister) ||
				preservedRegister != pushedRegister ||
				preservedRegister != reloadedRegister ||
				preservedRegister != restoredRegister ||
				preservedRegister != addDestinationRegister ||
				preservedRegister != storedRegister ||
				preservedRegister != copiedRegister ||
				temporaryRegister != addSourceRegister ||
				!CanRemoveByteStackPreservation(
					push,
					reload,
					copy,
					restore,
					add,
					store,
					preservedRegister,
					temporaryRegister,
					dataflow,
					instructions,
					index + 6,
					out var preservePrimaryRegister))
			{
				continue;
			}

			var unshiftedReloadDisplacement = reloadDisplacement - 2;
			if (unshiftedReloadDisplacement < short.MinValue ||
				unshiftedReloadDisplacement > short.MaxValue)
			{
				continue;
			}

			// Dtemporary is dead until COPY overwrites it, so retain the first byte
			// there rather than using a two-byte stack slot.  The byte push made
			// subsequent A7-relative loads two bytes larger; undo that bias too.
			_buffer.WriteWord(
				save.Offset,
				(ushort)(0x102F | (temporaryRegister << 9)));
			_buffer.WriteWord(
				reload.Offset + 2,
				unchecked((ushort)unshiftedReloadDisplacement));
			_buffer.WriteWord(
				add.Offset,
				(ushort)(0xD000 | (preservedRegister << 9) | temporaryRegister));
			MoveLabelsToOffset(push.Offset, push.Offset + push.Length, save.Offset);
			MoveLabelsToOffset(copy.Offset, copy.Offset + copy.Length, reload.Offset);
			if (preservePrimaryRegister)
			{
				_buffer.WriteWord(
					restore.Offset,
					(ushort)(0x1000 | (preservedRegister << 9) | temporaryRegister));
			}
			else
			{
				_buffer.WriteWord(
					store.Offset,
					(ushort)(0x1F40 | temporaryRegister));
				MoveLabelsToOffset(restore.Offset, restore.Offset + restore.Length, add.Offset);
				_buffer.RemoveBytes(restore.Offset, restore.Length);
			}
			_buffer.RemoveBytes(copy.Offset, copy.Length);
			_buffer.RemoveBytes(push.Offset, push.Length);
			return true;
		}

		return false;
	}

	private bool CanRemoveByteStackPreservation(
		M68kEmittedInstruction push,
		M68kEmittedInstruction reload,
		M68kEmittedInstruction copy,
		M68kEmittedInstruction restore,
		M68kEmittedInstruction add,
		M68kEmittedInstruction store,
		int preservedRegister,
		int temporaryRegister,
		M68kInstructionDataflow dataflow,
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int storeIndex,
		out bool preservePrimaryRegister)
	{
		preservePrimaryRegister = false;
		if (IsReferencedLabelAt(push.Offset) ||
			IsReferencedLabelAt(reload.Offset) ||
			IsReferencedLabelAt(copy.Offset) ||
			IsReferencedLabelAt(restore.Offset) ||
			IsReferencedLabelAt(add.Offset) ||
			IsReferencedLabelAt(store.Offset) ||
			!dataflow.TryGetFacts(store.Offset, out var storeFacts))
		{
			return false;
		}

		var temporaryRegisterMask = (ushort)(1 << temporaryRegister);
		if ((storeFacts.LiveDataAfter & temporaryRegisterMask) != 0 &&
			!IsDataRegisterOverwrittenBeforeUse(
				instructions,
				storeIndex + 1,
				temporaryRegister,
				dataflow))
		{
			return false;
		}

		preservePrimaryRegister =
			(storeFacts.LiveDataAfter & (1 << preservedRegister)) != 0;
		return true;
	}

	private static bool IsDataRegisterOverwrittenBeforeUse(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int register,
		M68kInstructionDataflow dataflow)
	{
		var registerMask = (ushort)(1 << register);
		for (var index = startIndex; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (instruction.Kind != M68kInstructionKind.Normal ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				return false;
			}

			if (TryGetByteFrameLoad(instruction, out var frameLoadRegister, out _))
			{
				if (frameLoadRegister == register)
				{
					return true;
				}

				continue;
			}

			if (TryGetByteFrameStore(instruction, out var frameStoreRegister))
			{
				if (frameStoreRegister == register)
				{
					return false;
				}

				continue;
			}

			if (TryGetByteDataMove(
					instruction,
					out var byteMoveSourceRegister,
					out var byteMoveDestinationRegister))
			{
				if (byteMoveSourceRegister == register)
				{
					return false;
				}

				if (byteMoveDestinationRegister == register)
				{
					return true;
				}

				continue;
			}

			if (facts.Effects.IsBarrier && IsStackOnlyMove(instruction.Opcode))
			{
				continue;
			}

			if ((facts.Effects.UsesData & registerMask) != 0)
			{
				return false;
			}

			if ((facts.Effects.DefinesData & registerMask) != 0)
			{
				return true;
			}

			if (facts.Effects.IsBarrier)
			{
				return false;
			}
		}

		return false;
	}

	private static bool IsStackOnlyMove(ushort opcode)
	{
		if ((opcode & 0xF000) is not (0x1000 or 0x2000 or 0x3000))
		{
			return false;
		}

		var sourceMode = (opcode >> 3) & 7;
		var sourceRegister = opcode & 7;
		var destinationMode = (opcode >> 6) & 7;
		var destinationRegister = (opcode >> 9) & 7;
		return sourceRegister == 7 && destinationRegister == 7 &&
			sourceMode is >= 2 and <= 6 && destinationMode is >= 2 and <= 6;
	}

	private static bool TryGetByteFrameLoad(
		M68kEmittedInstruction instruction,
		out int register,
		out int displacement)
	{
		if (instruction.Length == 4 && (instruction.Opcode & 0xF1FF) == 0x102F)
		{
			register = (instruction.Opcode >> 9) & 7;
			displacement = unchecked((short)instruction.ExtensionWord);
			return true;
		}

		register = 0;
		displacement = 0;
		return false;
	}

	private static bool TryGetByteFrameStore(
		M68kEmittedInstruction instruction,
		out int register)
	{
		if (instruction.Length == 4 && (instruction.Opcode & 0xFFF8) == 0x1F40)
		{
			register = instruction.Opcode & 7;
			return true;
		}

		register = 0;
		return false;
	}

	private static bool TryGetByteDataMove(
		M68kEmittedInstruction instruction,
		out int sourceRegister,
		out int destinationRegister)
	{
		if (instruction.Length == 2 && (instruction.Opcode & 0xF1F8) == 0x1000)
		{
			sourceRegister = instruction.Opcode & 7;
			destinationRegister = (instruction.Opcode >> 9) & 7;
			return true;
		}

		sourceRegister = 0;
		destinationRegister = 0;
		return false;
	}

	private static bool TryGetByteDataAdd(
		M68kEmittedInstruction instruction,
		out int sourceRegister,
		out int destinationRegister)
	{
		if (instruction.Length == 2 && (instruction.Opcode & 0xF1F8) == 0xD000)
		{
			sourceRegister = instruction.Opcode & 7;
			destinationRegister = (instruction.Opcode >> 9) & 7;
			return true;
		}

		sourceRegister = 0;
		destinationRegister = 0;
		return false;
	}

	private bool TryGetScratchRegister(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int shuffleIndex,
		int sourceRegister,
		M68kInstructionDataflow dataflow,
		out int scratchRegister)
	{
		if (!dataflow.TryGetFacts(instructions[shuffleIndex].Offset, out var shuffleFacts))
		{
			scratchRegister = -1;
			return false;
		}

		for (var register = 0; register < 8; register++)
		{
			if (register == sourceRegister ||
				(shuffleFacts.LiveDataAfter & (1 << register)) != 0 &&
				!IsClobberedByFollowingExternalCall(instructions, shuffleIndex, register, dataflow))
			{
				continue;
			}

			scratchRegister = register;
			return true;
		}

		scratchRegister = -1;
		return false;
	}

	private static bool IsClobberedByFollowingExternalCall(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int shuffleIndex,
		int register,
		M68kInstructionDataflow dataflow)
	{
		var registerMask = (ushort)(1 << register);
		for (var index = shuffleIndex + 1; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if ((instruction.Opcode & 0xFFC0) == 0x4E80)
			{
				return true;
			}

			if (instruction.Kind == M68kInstructionKind.Call)
			{
				return instruction.ExternalTarget;
			}

			if (instruction.Kind != M68kInstructionKind.Normal ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				(facts.Effects.UsesData & registerMask) != 0 ||
				(facts.Effects.DefinesData & registerMask) != 0)
			{
				return false;
			}
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

	private bool TryReplaceZeroStackPush(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var zero = instructions[index];
			var push = instructions[index + 1];
			var dataRegister = (zero.Opcode >> 9) & 7;
			if ((zero.Opcode & 0xF1FF) != 0x7000 ||
				zero.Length != 2 ||
				push.Opcode != (ushort)(0x2F00 | dataRegister) ||
				push.Length != 2 ||
				IsReferencedLabelAt(push.Offset) ||
				!dataflow.TryGetFacts(push.Offset, out var pushFacts) ||
				(pushFacts.LiveDataAfter & (1 << dataRegister)) != 0)
			{
				continue;
			}

			_buffer.WriteWord(zero.Offset, 0x42A7); // CLR.L -(A7)
			_buffer.RemoveBytes(push.Offset, push.Length);
			return true;
		}

		return false;
	}

	private bool TryReplaceClearStackRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var clear = instructions[index];
			var pop = instructions[index + 1];
			if (clear.Opcode != 0x42A7 ||
				clear.Length != 2 ||
				(pop.Opcode & 0xF1FF) != 0x201F ||
				pop.Length != 2 ||
				IsReferencedLabelAt(pop.Offset))
			{
				continue;
			}

			var dataRegister = (pop.Opcode >> 9) & 7;
			MoveLabelsToOffset(pop.Offset, pop.Offset + pop.Length, clear.Offset);
			_buffer.WriteWord(clear.Offset, (ushort)(0x4280 | dataRegister)); // CLR.L Dn
			_buffer.RemoveBytes(pop.Offset, pop.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveZeroExtendedByteRegisterStackRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var push = instructions[index];
			var widen = instructions[index + 1];
			var pop = instructions[index + 2];
			if (push.Length != 2 ||
				!TryGetByteRegisterStackPush(push.Opcode, out var sourceRegister) ||
				widen.Length != 2 ||
				(widen.Opcode & 0xF100) != 0x7000 ||
				(widen.Opcode & 0xFF) != 0 ||
				pop.Length != 2 ||
				!TryGetByteRegisterStackPop(pop.Opcode, out var destinationRegister) ||
				sourceRegister == destinationRegister ||
				IsReferencedLabelAt(widen.Offset) ||
				IsReferencedLabelAt(pop.Offset))
			{
				continue;
			}

			MoveLabelsToOffset(
				widen.Offset,
				pop.Offset + pop.Length,
				push.Offset);
			_buffer.WriteWord(
				push.Offset,
				(ushort)(0x7000 | (destinationRegister << 9)));
			_buffer.WriteWord(
				push.Offset + push.Length,
				(ushort)(0x1000 | (destinationRegister << 9) | sourceRegister));
			_buffer.RemoveBytes(pop.Offset, pop.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveByteRegisterStackRoundTrip()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var push = instructions[index];
			var pop = instructions[index + 1];
			if (push.Length != 2 ||
				!TryGetByteRegisterStackPush(push.Opcode, out var sourceRegister) ||
				pop.Length != 2 ||
				!TryGetByteRegisterStackPop(pop.Opcode, out var destinationRegister) ||
				IsReferencedLabelAt(pop.Offset))
			{
				continue;
			}

			var replacement = sourceRegister == destinationRegister
				? (ushort)(0x4A00 | sourceRegister) // TST.B Dn preserves the final MOVE.B flags.
				: (ushort)(0x1000 | (destinationRegister << 9) | sourceRegister);
			MoveLabelsToOffset(
				push.Offset + push.Length,
				pop.Offset + pop.Length,
				push.Offset);
			_buffer.WriteWord(push.Offset, replacement);
			_buffer.RemoveBytes(pop.Offset, pop.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveByteMaskAndTestBeforeFrameStore()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var mask = instructions[index];
			var test = instructions[index + 1];
			var store = instructions[index + 2];
			var dataRegister = mask.Opcode & 7;
			if (mask.Opcode != (ushort)(0x0280 | dataRegister) ||
				mask.Length != 6 ||
				_buffer.ReadLong(mask.Offset + 2) != 0x000000FF ||
				test.Opcode != (ushort)(0x4A00 | dataRegister) ||
				test.Length != 2 ||
				store.Opcode != (ushort)(0x1F40 | dataRegister) ||
				store.Length != 4)
			{
				continue;
			}

			// MOVE.B to the frame slot writes the same byte and establishes the
			// same final condition codes as TST.B, so neither predecessor is needed.
			MoveLabelsToOffset(mask.Offset, test.Offset + test.Length, store.Offset);
			_buffer.RemoveBytes(mask.Offset, mask.Length + test.Length);
			return true;
		}

		return false;
	}

	private bool TryFoldByteAddIntoFrameStore(M68kInstructionDataflow dataflow)
	{
		for (var offset = 0; offset + 5 < _buffer.Bytes.Count; offset += 2)
		{
			var addOpcode = _buffer.ReadWord(offset);
			if ((addOpcode & 0xF1F8) != 0xD000)
			{
				continue;
			}

			var sourceRegister = addOpcode & 7;
			var resultRegister = (addOpcode >> 9) & 7;
			if (_buffer.ReadWord(offset + 2) != (ushort)(0x1F40 | resultRegister) ||
				IsReferencedLabelAt(offset + 2) ||
				!dataflow.TryGetFacts(offset + 2, out var facts) ||
				(facts.LiveDataAfter & (1 << resultRegister)) != 0 ||
				(facts.LiveConditionsAfter &
					(M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry)) != 0)
			{
				continue;
			}

			// ADD.B Dn,d16(A7) has the same stored byte, N/Z, and X as the
			// register add followed by MOVE.B. C and V differ, so retain the
			// original sequence whenever either flag is live.
			_buffer.WriteWord(
				offset,
				(ushort)(0xD100 | (sourceRegister << 9) | 0x2F)); // ADD.B Dn,d16(A7)
			_buffer.WriteWord(offset + 2, _buffer.ReadWord(offset + 4));
			_buffer.RemoveBytes(offset + 4, 2);
			return true;
		}

		return false;
	}

	private bool TryRemoveStackDuplicateRoundTrip(M68kInstructionDataflow dataflow)
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
				!TryGetRegisterStackPop(pop.Opcode, out var destinationIsAddress, out var destinationRegister) ||
				destinationIsAddress &&
				(!dataflow.TryGetFacts(pop.Offset, out var facts) ||
				 facts.LiveConditionsAfter != M68kConditionCodeSet.None))
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

	private bool TryRemoveRegisterStackRoundTrip(M68kInstructionDataflow dataflow)
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
				!TryGetRegisterStackPop(pop.Opcode, out var destinationIsAddress, out var destinationRegister) ||
				destinationIsAddress &&
				(!dataflow.TryGetFacts(pop.Offset, out var facts) ||
				 facts.LiveConditionsAfter != M68kConditionCodeSet.None))
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

	private static bool TryGetByteRegisterStackPush(ushort opcode, out int register)
	{
		if ((opcode & 0xFFF8) == 0x1F00)
		{
			register = opcode & 7;
			return true;
		}

		register = 0;
		return false;
	}

	private static bool TryGetByteRegisterStackPop(ushort opcode, out int register)
	{
		if ((opcode & 0xF1FF) == 0x101F)
		{
			register = (opcode >> 9) & 7;
			return true;
		}

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

	private bool TryRemoveRedundantStackShuffle(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
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
				!dataflow.TryGetFacts(secondPop.Offset, out var secondPopFacts) ||
				!secondPopFacts.ConditionsAreDeadAfter ||
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

		var instructions = _assembler.GetInstructionStream();
		for (var index = instructions.Count - 2; index >= 0; index--)
		{
			var call = instructions[index];
			var returnInstruction = instructions[index + 1];
			if (call.Kind != M68kInstructionKind.Call ||
				call.Length != 4 ||
				(call.Opcode & 0xFFF8) != 0x4EA8 ||
				returnInstruction.Kind != M68kInstructionKind.Return ||
				returnInstruction.Offset != call.Offset + call.Length ||
				IsReferencedLabelAt(returnInstruction.Offset))
			{
				continue;
			}

			_buffer.WriteWord(call.Offset, call.Opcode + 0x40); // JSR d16(An) -> JMP d16(An)
			MoveLabelsToOffset(
				returnInstruction.Offset,
				returnInstruction.Offset + returnInstruction.Length,
				call.Offset);
			_buffer.RemoveBytes(returnInstruction.Offset, returnInstruction.Length);
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
