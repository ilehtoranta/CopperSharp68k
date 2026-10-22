/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryCleanupApplicationArithmetic(M68kInstructionDataflow dataflow)
	{
		if (!_assembler.GeneratedTransportRegistersOnly) return false;
		var options = _assembler.GeneratedCodeSizeOptions;
		if (options is null || !(options.RemoveApplicationDeadArithmetic ||
			options.TrackApplicationNarrowValues || options.FoldApplicationMemoryOperands)) return false;
		// Scoped analysis has no successors for exits outside the decoded CFG.
		// Do not interpret those missing edges (including computed jumps) as dead.
		var offsets = dataflow.Instructions.Select(instruction => instruction.Offset).ToHashSet();
		var closedFlow = !dataflow.Instructions.Any(instruction =>
			instruction.Kind is M68kInstructionKind.ConditionalBranch or M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Dbcc &&
			(instruction.ExternalTarget || instruction.TargetOffset is not { } target || !offsets.Contains(target)));
		return closedFlow && options.RemoveApplicationDeadArithmetic && TryRemoveApplicationDeadArithmetic(dataflow) ||
			options?.TrackApplicationNarrowValues == true && TryTrackApplicationNarrowValues(dataflow) ||
			options?.FoldApplicationMemoryOperands == true && TryFoldApplicationMemoryOperands(dataflow);
	}

	private bool ApplicationArithmeticCandidate(M68kEmittedInstruction instruction) =>
		instruction.IsDecoded && instruction.Kind == M68kInstructionKind.Normal &&
		GeneratedSizeEligible(instruction.Offset, instruction.Offset + instruction.Length) &&
		!HasInternalLabel(instruction) && !IsReferencedLabelAt(instruction.Offset) &&
		!_assembler.HasReturnAddressInRange(instruction.Offset, instruction.Offset + instruction.Length) &&
		!_assembler.TryGetInstructionEffects(instruction.Offset, out _) &&
		CanFoldAcrossUnreferencedIlLabels(instruction.Offset, instruction.Offset + instruction.Length,
			allowUnreferencedMarkers: true);

	private bool TryRemoveApplicationDeadArithmetic(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			// MOVE remains controlled by the independent transport policy. Only
			// decoded, non-trapping computations with no memory/stack effects qualify.
			var family = instruction.Opcode & 0xF000;
			if (family is 0x1000 or 0x2000 or 0x3000 or 0x7000 ||
				!ApplicationArithmeticCandidate(instruction) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts)) continue;
			var effects = facts.Effects;
			if (!effects.CanRemoveWhenOutputsDead || effects.IsBarrier ||
				(effects.DefinesData | effects.DefinesAddress) == 0 ||
				(effects.DefinesData & facts.LiveDataAfter) != 0 ||
				(effects.DefinesAddress & facts.LiveAddressAfter) != 0 ||
				(effects.WritesConditions & facts.LiveConditionsAfter) != 0 ||
				effects.ReadsMemory != M68kMemorySet.None || effects.WritesMemory != M68kMemorySet.None ||
				effects.StackDelta != 0 || (effects.DefinesAddress & 0x80) != 0) continue;
			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.RemoveApplicationDeadArithmetic), instruction.Length);
			return true;
		}
		return false;
	}

	private bool TryTrackApplicationNarrowValues(M68kInstructionDataflow dataflow)
	{
		if (TryNarrowApplicationComparisons(dataflow)) return true;
		var instructions = dataflow.Instructions;
		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			var opcode = instruction.Opcode;
			uint changedBits = 0;
			var register = opcode & 7;
			if (instruction.Length == 6 && (opcode & 0xFFF8) == 0x0280 &&
				instruction.ExtensionLong is 0xFF or 0xFFFF) changedBits = ~instruction.ExtensionLong;
			else if (instruction.Length == 4 && (opcode & 0xFFF8) == 0x0240 &&
				instruction.ExtensionWord == 0xFF) changedBits = 0xFF00;
			else if (instruction.Length == 2 && (opcode & 0xFFF8) == 0x4880) changedBits = 0xFF00;
			else if (instruction.Length == 2 && (opcode & 0xFFF8) == 0x48C0) changedBits = 0xFFFF0000;
			else if (instruction.Length == 2 && (opcode & 0xF1FF) == 0x7000)
			{
				changedBits = uint.MaxValue; register = opcode >> 9 & 7;
			}
			if (changedBits == 0 || !ApplicationArithmeticCandidate(instruction) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				(facts.Effects.WritesConditions & facts.LiveConditionsAfter) != 0 ||
				!ApplicationBitsAreUnobserved(dataflow, index + 1, register, changedBits)) continue;
			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.TrackApplicationNarrowValues), instruction.Length);
			return true;
		}
		return false;
	}

	private bool TryNarrowApplicationComparisons(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			var opcode = instruction.Opcode;
			var compare = (opcode & 0xF1F8) == 0xB080;
			var immediateCompare = instruction.Length == 6 && (opcode & 0xFFF8) == 0x0C80 &&
				!HasAddressFixup(instruction);
			if ((!immediateCompare && (instruction.Length != 2 || (!compare && (opcode & 0xFFF8) != 0x4A80))) ||
				!ApplicationArithmeticCandidate(instruction) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				(facts.LiveConditionsAfter & (M68kConditionCodeSet.Negative |
					(compare || immediateCompare ? M68kConditionCodeSet.Overflow : M68kConditionCodeSet.None))) != 0) continue;
			var zeroBits = dataflow.GetDataValueBefore(instruction.Offset, opcode & 7).KnownZeroMask;
			if (compare) zeroBits &= dataflow.GetDataValueBefore(instruction.Offset, opcode >> 9 & 7).KnownZeroMask;
			if (immediateCompare) zeroBits &= ~instruction.ExtensionLong;
			var size = (zeroBits & 0xFFFFFF00) == 0xFFFFFF00 ? 0 :
				(zeroBits & 0xFFFF0000) == 0xFFFF0000 ? 0x40 : -1;
			if (size < 0) continue;
			// Equal-width zero-extended operands have the same Z and unsigned
			// borrow. Signed N/V may differ. A narrow test still clears C/V.
			_buffer.WriteWord(instruction.Offset, (ushort)((opcode & ~0xC0) | size));
			if (immediateCompare)
			{
				_buffer.WriteWord(instruction.Offset + 2, (ushort)instruction.ExtensionLong);
				_buffer.RemoveBytes(instruction.Offset + 4, 2);
			}
			_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.TrackApplicationNarrowValues), immediateCompare ? 2 : 0);
			return true;
		}
		return false;
	}

	private bool ApplicationBitsAreUnobserved(M68kInstructionDataflow dataflow, int next, int register, uint changedBits)
	{
		var instructions = dataflow.Instructions;
		var byOffset = instructions.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(item => item.Offset, item => item.index);
		var pending = new Stack<(int Index, uint Bits)>();
		var visited = new HashSet<(int Index, uint Bits)>();
		pending.Push((next, changedBits));
		while (pending.TryPop(out var state))
		{
			if (!visited.Add(state)) continue;
			if (visited.Count > 512 || state.Index >= instructions.Count) return false;
			// A loop must not use the very definition being deleted to kill its
			// dirty bits. Conservatively keep normalizations on such backedges.
			if (state.Index == next - 1) return false;
			var instruction = instructions[state.Index];
			if (!instruction.IsDecoded || !GeneratedSizeEligible(instruction.Offset, instruction.Offset + instruction.Length) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts)) return false;
			// Calls, including collector/native calls, are boundaries. Do not infer
			// a callee's upper-bit behavior from its ABI or walk into another method.
			if (instruction.Kind == M68kInstructionKind.Call || instruction.IsNonReturning) return false;
			if (instruction.Kind is M68kInstructionKind.ConditionalBranch or M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Dbcc &&
				(instruction.ExternalTarget || instruction.TargetOffset is not { } branchTarget || !byOffset.ContainsKey(branchTarget))) return false;
			var reads = (facts.Effects.UsesData & (1 << register)) != 0 ? uint.MaxValue : 0;
			uint writes = 0;
			if (!_assembler.TryGetInstructionEffects(instruction.Offset, out _))
				ApplicationRegisterBitEffects(instruction, register, ref reads, ref writes);
			var dirtyBits = state.Bits;
			if ((reads & dirtyBits) != 0)
			{
				// Addition/subtraction and bitwise operations cannot propagate high
				// operand bits downwards. Carry/overflow/sign may differ, so all their
				// produced flags must be dead. Keep the dirty bits in the same register.
				if (_assembler.TryGetInstructionEffects(instruction.Offset, out _) ||
					(facts.Effects.WritesConditions & facts.LiveConditionsAfter) != 0 ||
					!ApplicationPreservesLowBits(instruction, register) ||
					(dirtyBits != 0xFF00 && dirtyBits != 0xFFFFFF00 && dirtyBits != 0xFFFF0000)) return false;
				// Carry can propagate upwards from an EXT.W's dirty middle byte.
				if (dirtyBits == 0xFF00) dirtyBits = 0xFFFFFF00;
				writes = 0;
			}
			var remaining = dirtyBits & ~writes;
			if (remaining == 0 || (facts.LiveDataAfter & (1 << register)) == 0) continue;
			if (instruction.Kind == M68kInstructionKind.Return) return false;
			if (instruction.Kind is M68kInstructionKind.ConditionalBranch or M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Dbcc)
			{
				if (instruction.ExternalTarget || instruction.TargetOffset is not { } target ||
					!byOffset.TryGetValue(target, out var targetIndex)) return false;
				pending.Push((targetIndex, remaining));
				if (instruction.Kind == M68kInstructionKind.UnconditionalBranch) continue;
			}
			else if (facts.Effects.IsBarrier) return false;
			pending.Push((state.Index + 1, remaining));
		}
		return true;
	}

	private static bool ApplicationPreservesLowBits(M68kEmittedInstruction instruction, int register)
	{
		var opcode = instruction.Opcode;
		// Register source -> same long destination; no DIV/MUL/ADDX/SUBX/BCD.
		if (instruction.Length == 2 && (opcode & 0x01F8) == 0x0080 &&
			(opcode >> 9 & 7) == register && (opcode & 0xF000) is 0x8000 or 0x9000 or 0xC000 or 0xD000) return true;
		// Long immediate, quick, negate or complement on this register.
		return (opcode & 0x3F) == register && (opcode >> 6 & 3) == 2 &&
			((opcode & 0xFF00) is 0x0000 or 0x0200 or 0x0400 or 0x0600 or 0x0A00 or 0x4400 or 0x4600 ||
			 (opcode & 0xF000) == 0x5000);
	}

	private static void ApplicationRegisterBitEffects(M68kEmittedInstruction instruction, int register, ref uint reads, ref uint writes)
	{
		var opcode = instruction.Opcode;
		var family = opcode & 0xF000;
		var source = opcode & 7;
		var mode = opcode >> 3 & 7;
		var destination = opcode >> 9 & 7;
		if (family is 0x1000 or 0x2000 or 0x3000)
		{
			var width = family == 0x1000 ? 0xFFu : family == 0x3000 ? 0xFFFFu : uint.MaxValue;
			// Preserve the conservative address/index uses. For ordinary MOVE
			// operands, only the source bits are observed, not preserved upper bits.
			var destinationMode = opcode >> 6 & 7;
			if (mode != 6 && (opcode & 0x3F) != 0x3B && destinationMode != 6)
				reads = mode == 0 && source == register ? width : 0;
			if (destinationMode == 0 && destination == register) writes = width;
			return;
		}
		if ((opcode & 0xF100) == 0x7000 && destination == register)
		{
			reads = 0; writes = uint.MaxValue; return;
		}
		if ((opcode & 0xF0F8) == 0x50C0 && source == register)
		{
			reads = 0; writes = 0xFF; return; // Scc writes only the low byte.
		}
		if (instruction.Length == 2 && (opcode & 0xFFF8) is 0x4880 or 0x48C0 && source == register)
		{
			reads = (opcode & 0x40) == 0 ? 0xFFu : 0xFFFFu;
			writes = (opcode & 0x40) == 0 ? 0xFFFFu : uint.MaxValue;
			return;
		}
		var size = opcode >> 6 & 3;
		if (mode != 0 || size == 3) return;
		var operandBits = size == 0 ? 0xFFu : size == 1 ? 0xFFFFu : uint.MaxValue;
		// Register-only arithmetic/logical operations observe their operand width.
		// Exclude ADDX/SUBX, BCD, EXG, multiply/divide and all memory destinations.
		if (family is 0x8000 or 0x9000 or 0xB000 or 0xC000 or 0xD000 && (opcode & 0x0100) == 0)
		{
			reads = source == register || destination == register ? operandBits : 0;
			if (family != 0xB000 && destination == register) writes = operandBits;
		}
		else if (family == 0 && (opcode & 0xFF00) is 0x0000 or 0x0200 or 0x0400 or 0x0600 or 0x0A00 or 0x0C00 ||
			(opcode & 0xFF00) is 0x4200 or 0x4400 or 0x4600 or 0x4A00 || family == 0x5000)
		{
			reads = source == register ? operandBits : 0;
			if (source == register && (opcode & 0xFF00) is not (0x0C00 or 0x4A00)) writes = operandBits;
			if ((opcode & 0xFF00) == 0x4200) reads = 0;
		}
	}

	private bool TryFoldApplicationMemoryOperands(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var load = instructions[index];
			var family = load.Opcode & 0xF000;
			var mode = load.Opcode >> 3 & 7;
			var ea = load.Opcode & 0x3F;
			if (family is not (0x1000 or 0x2000 or 0x3000) || (load.Opcode >> 6 & 7) != 0 ||
				mode is not (2 or 5 or 6 or 7) || mode == 7 && (ea & 7) > 1 ||
				!ApplicationArithmeticCandidate(load) || !dataflow.TryGetFacts(load.Offset, out var loadFacts)) continue;
			var temporary = load.Opcode >> 9 & 7;
			var size = family == 0x1000 ? 0 : family == 0x3000 ? 1 : 2;
			var gaps = new List<M68kInstructionDataflowFacts>();
			for (var next = index + 1; next < instructions.Count && next <= index + 8; next++)
			{
				var use = instructions[next];
				if (!ApplicationArithmeticCandidate(use) ||
					!CanFoldAcrossUnreferencedIlLabels(load.Offset, use.Offset + use.Length, allowUnreferencedMarkers: true) ||
					!dataflow.TryGetFacts(use.Offset, out var facts) || facts.Effects.IsBarrier) break;
				var operation = use.Opcode & 0xF000;
				var destination = use.Opcode >> 9 & 7;
				if (use.Length == 2 && (use.Opcode & 0x013F) == temporary &&
					(use.Opcode >> 6 & 3) == size && destination != temporary &&
					operation is 0x8000 or 0x9000 or 0xB000 or 0xC000 or 0xD000 &&
					(facts.LiveDataAfter & (1 << temporary)) == 0)
				{
					// Keep the read at its original position. Only independent,
					// non-trapping register computations may separate it from its use.
					// Earlier arithmetic must not change any observed intermediate CCR.
					if (gaps.Any(gap => ((gap.Effects.UsesData | gap.Effects.DefinesData) & (1 << destination)) != 0 ||
						(gap.Effects.ReadsConditions & (facts.Effects.WritesConditions | loadFacts.Effects.WritesConditions)) != 0 ||
						(gap.Effects.WritesConditions & facts.Effects.WritesConditions & facts.LiveConditionsAfter) != 0)) break;
					_buffer.WriteWord(load.Offset, (ushort)((use.Opcode & 0xFFC0) | ea));
					_buffer.RemoveBytes(use.Offset, use.Length);
					_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.FoldApplicationMemoryOperands), 2);
					return true;
				}
				var effects = facts.Effects;
				if (!effects.CanRemoveWhenOutputsDead || effects.ReadsMemory != M68kMemorySet.None ||
					effects.WritesMemory != M68kMemorySet.None || effects.StackDelta != 0 ||
					(effects.DefinesAddress & 0x80) != 0 ||
					((effects.UsesData | effects.DefinesData) & (1 << temporary)) != 0) break;
				gaps.Add(facts);
			}
		}
		return false;
	}
}
