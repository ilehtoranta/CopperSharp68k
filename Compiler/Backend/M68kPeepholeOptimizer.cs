/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kPeepholeOptimizer : IM68kOptimizerPass
{
	private static readonly ConstantSynthesisTransform[] M68020ConstantSynthesisTransforms =
	[
		// MC68020 instruction-cache timings: MOVEQ is added separately (2 cycles),
		// SWAP costs 2 cycles, and immediate-count ROR costs 8 cycles.
		new(ConstantSynthesisOperation.Swap, 0, CacheCycles: 2),
		new(ConstantSynthesisOperation.RotateRight, 1, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 2, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 3, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 4, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 5, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 6, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 7, CacheCycles: 8),
		new(ConstantSynthesisOperation.RotateRight, 8, CacheCycles: 8)
	];

	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;
	private readonly M68kCpuTarget _cpu;
	private readonly M68kClrPolicy _clrPolicy;
	private readonly IReadOnlyList<M68kLoopLayout> _sizeFirstLoops;
	private readonly int _rewriteBudget;

	private readonly record struct TerminalBlockLookup(
		int[] MethodEndOffsets,
		int[] LabelOffsets,
		int[] BranchOffsets,
		int[] AddressOffsets,
		int[] PcRelativeOffsets);

	public M68kPeepholeOptimizer(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer,
		M68kCpuTarget cpu,
		M68kClrPolicy clrPolicy,
		IReadOnlyList<M68kLoopLayout> sizeFirstLoops,
		int rewriteBudget = int.MaxValue)
	{
		_assembler = assembler;
		_buffer = buffer;
		_cpu = cpu;
		_clrPolicy = clrPolicy;
		_sizeFirstLoops = sizeFirstLoops;
		_rewriteBudget = rewriteBudget;
	}

	public bool Changed { get; private set; }

	public void Run()
	{
		Changed = false;
		var rewrites = 0;
		bool changed;
		do
		{
			var dataflow = M68kInstructionDataflow.Analyze(_assembler);
			changed =
				TryFoldTailReturn() ||
				TryLayoutColdTerminalBranch() ||
				TryRemoveBranchToNextLabel() ||
				TryPromoteBranchStackSpillToD0() ||
				TryRemoveAliasedRuntimeFrameClear() ||
				TryRemoveRedundantRuntimeFrameClear() ||
				TryRemoveDeadStackStoreBeforeClear(dataflow) ||
				TryHoistZeroMoveAcrossStackClears(dataflow) ||
				TryReuseZeroRegisterForStackStores(dataflow) ||
				// Disabled: this local rewrite corrupts Console.Read/ReadLine state
				// across shared terminal blocks on every supported CPU.
				// TryBypassTerminalStackReloadOnFallthrough(dataflow) ||
				TryForwardStackStoreReload(dataflow) ||
				TryReplaceZeroAddressMove() ||
				TryOptimizeSmallAddressImmediate(dataflow) ||
				TryNarrowAddressArithmeticImmediate() ||
				TryReplaceStackImmediateWithPea(dataflow) ||
				TryOptimizeDataRegisterImmediate(dataflow) ||
				TrySynthesizeSizeFirstDataRegisterConstant(dataflow) ||
				TryUseDestructiveQuickImmediate(dataflow) ||
				TryReplaceCompareZeroWithTest() ||
				TryUseRegisterCompareForSmallImmediate(dataflow) ||
				TryDistributeMoveQuickAcrossConditionalBranch(dataflow) ||
				TryReplaceAddressNullCheckWithTest(dataflow) ||
				TryRemoveRedundantTest() ||
				TryRemoveRedundantAndTest() ||
				TryRemoveRedundantKnownMoveQuick(dataflow) ||
				TryRemoveDeadMoveQuick(dataflow) ||
				TryRemoveDeadTest(dataflow) ||
				TryRemoveDiscardedStackPush(dataflow) ||
				TryRewriteByteStackPreservation(dataflow) ||
				TryMaterializeZeroExtendedByteCopy(dataflow) ||
				TryRemoveByteMasksBeforeNormalizedAdd() ||
				TryRemoveDeadByteNormalizationBeforeFrameStore(dataflow) ||
				TryFoldNormalizedWordRotateAdd(dataflow) ||
				TryMaterializeZeroExtendedWordCopy(dataflow) ||
				TryMaterializeZeroExtendedPartialLoad(dataflow) ||
				TryFoldByteAddIntoFrameStore(dataflow) ||
				TryRemoveSelfMove(dataflow) ||
				TryRemoveDeadAddressRegisterCopyBeforeBranch(dataflow) ||
				TryRemoveRepeatedLea() ||
				TryFoldZeroDisplacementLeaToDataMove(dataflow) ||
				TryRemoveRedundantAddressRegisterReload(dataflow) ||
				TryForwardAddressRegisterBase(dataflow) ||
				TryForwardAddressToDataCopyChain(dataflow) ||
				TryForwardDataRegisterCopyChain(dataflow) ||
				TryRemoveDataRegisterRoundTrip(dataflow) ||
				TryFoldDataRegisterCopyUpdate(dataflow) ||
				TryFoldDataRegisterExchange(dataflow) ||
				TryRemoveAddressRegisterRoundTrip(dataflow) ||
				TryFoldAddressToDataRegisterMove(dataflow) ||
				TryFoldAddressRegisterMemoryTransfer(dataflow) ||
				TryFoldDataToAddressRegisterMove(dataflow) ||
				TryForwardMoveQuickThroughDataMove(dataflow) ||
				TryFoldMoveQuickIntoQuickArithmetic(dataflow) ||
				TryFuseOwnedMemoryReadModifyWrite(dataflow) ||
				TryForwardLongImmediateThroughRegisterMove(dataflow) ||
				TryForwardMemoryLoadIntoArithmetic(dataflow) ||
				TryForwardMemoryLoadThroughStackToAddressRegister() ||
				TryUseSwapClearForLongShiftBySixteen(dataflow) ||
				TryCompactCanonicalBooleanMaterialization(dataflow) ||
				TryFoldZeroExtendedWordAdd(dataflow) ||
				TryNarrowAddition(dataflow) ||
				TryNarrowCompareAddressImmediate() ||
				TryFoldWordConstantRegisterUse(dataflow) ||
				TryRemoveRedundantLogicalImmediate() ||
				TryRemoveRepeatedMaskAcrossUntouchedRegister(dataflow) ||
				TryUseSingleBitLogicalImmediate(dataflow) ||
				TryUseMoveQuickAndMask(dataflow) ||
				TryNarrowLogicalImmediate(dataflow) ||
				TryFoldStackAllocationIntoRegisterPush() ||
				TryGroupOrderedDataRegisterPushes(dataflow) ||
				TryCanonicalizeAddressAdjustments() ||
				TryRewriteStackPreservation(dataflow) ||
				TryRemoveRedundantStackShuffle(dataflow) ||
				TryRemoveRedundantStackArgumentShuffle() ||
				TryRemoveZeroExtendedByteRegisterStackRoundTrip() ||
				TryRemoveByteRegisterStackRoundTrip() ||
				TryRemoveByteMaskAndTestBeforeFrameStore() ||
				TryRemoveTerminalWordStoreNormalization(dataflow) ||
				TryReplaceClearStackRoundTrip() ||
				TryReplaceZeroStackPush(dataflow) ||
				TryReplaceZeroRegisterRoundTrip() ||
				TryForwardImmediateStackTemporary() ||
				TryReplaceAddressStackRoundTrip() ||
				TryRemoveStackDuplicateRoundTrip(dataflow) ||
				TryForwardRegisterStackStore() ||
				TryForwardStackLoadToRegister(dataflow) ||
				TryReplaceMemoryStackTransferWithRegister(dataflow) ||
				TryRemoveRegisterStackRoundTrip(dataflow) ||
				TryReplaceCompareRegisterZeroWithTest(dataflow) ||
				TryFoldCopyMoveQuickCompare(dataflow) ||
				TryFoldMoveQuickIntoCompareImmediate(dataflow) ||
				TryRemoveRedundantRegisterSpill() ||
				TryCanonicalizeZeroDisplacementEffectiveAddress();
			if (!changed)
			{
				changed = TryRemoveDeadInstruction(dataflow);
			}
			Changed |= changed;
			if (changed && ++rewrites >= _rewriteBudget)
			{
				break;
			}
		}
		while (changed);
	}

	private bool TryCompactCanonicalBooleanMaterialization(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 3 < instructions.Count; index++)
		{
			var set = instructions[index];
			var extendWord = instructions[index + 1];
			var extendLong = instructions[index + 2];
			var negateLong = instructions[index + 3];
			if ((set.Opcode & 0xF0F8) != 0x50C0 || // Scc Dn
				set.Length != 2 ||
				extendWord.Length != 2 ||
				extendLong.Length != 2 ||
				negateLong.Length != 2)
			{
				continue;
			}

			var register = set.Opcode & 7;
			if (extendWord.Opcode != (0x4880 | register) || // EXT.W Dn
				extendLong.Opcode != (0x48C0 | register) || // EXT.L Dn
				negateLong.Opcode != (0x4480 | register) || // NEG.L Dn
				_buffer.HasLabelAt(extendWord.Offset) ||
				_buffer.HasLabelAt(extendLong.Offset) ||
				_buffer.HasLabelAt(negateLong.Offset) ||
				!dataflow.TryGetFacts(negateLong.Offset, out var facts) ||
				facts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			// Scc only changes the low byte.  If the remaining bytes are already
			// zero, NEG.B turns $00/$FF into canonical 0/1 without the two sign
			// extensions and long negate.
			var incoming = dataflow.GetDataValueBefore(set.Offset, register);
			if ((incoming.KnownZeroMask & 0xFFFF_FF00u) != 0xFFFF_FF00u)
			{
				continue;
			}

			_buffer.WriteWord(extendWord.Offset, (ushort)(0x4400 | register)); // NEG.B Dn
			_buffer.RemoveBytes(extendLong.Offset, 4);
			return true;
		}

		return false;
	}

	private bool TryUseSwapClearForLongShiftBySixteen(
		M68kInstructionDataflow dataflow)
	{
		if (_cpu != M68kCpuTarget.M68000)
		{
			return false;
		}

		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if ((first.Opcode & 0xFFF8) != 0xE188 ||
				second.Opcode != first.Opcode ||
				first.Length != 2 ||
				second.Length != 2 ||
				_buffer.HasLabelAt(second.Offset) ||
				!dataflow.TryGetFacts(second.Offset, out var facts) ||
				facts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			var register = first.Opcode & 7;
			_buffer.WriteWord(first.Offset, (ushort)(0x4840 | register)); // SWAP Dn
			_buffer.WriteWord(second.Offset, (ushort)(0x4240 | register)); // CLR.W Dn
			return true;
		}

		return false;
	}

	private bool TryGroupOrderedDataRegisterPushes(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var start = 0; start + 1 < instructions.Count; start++)
		{
			var first = instructions[start];
			if (!TryGetLongRegisterPush(first, out var firstRegister))
			{
				continue;
			}

			var registers = new List<int> { firstRegister };
			var end = start + 1;
			while (end < instructions.Count && registers.Count < 16)
			{
				var push = instructions[end];
				if (!TryGetLongRegisterPush(push, out var register) ||
					register >= registers[^1] ||
					_buffer.HasLabelAt(push.Offset))
				{
					break;
				}
				registers.Add(register);
				end++;
			}

			if (registers.Count < 2)
			{
				continue;
			}
			var last = instructions[start + registers.Count - 1];
			if (!dataflow.TryGetFacts(last.Offset, out var facts) ||
				facts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			ushort mask = 0;
			foreach (var register in registers)
			{
				mask |= (ushort)(1 << (15 - register));
			}
			_buffer.WriteWord(first.Offset, 0x48E7); // MOVEM.L Dregisters,-(A7)
			_buffer.WriteWord(first.Offset + 2, mask);
			_buffer.RemoveBytes(
				first.Offset + 4,
				(registers.Count * 2) - 4);
			return true;
		}

		return false;

		static bool TryGetLongRegisterPush(
			M68kEmittedInstruction instruction,
			out int register)
		{
			if (instruction.Length == 2 &&
				(instruction.Opcode & 0xFFF8) is var family &&
				family is 0x2F00 or 0x2F08)
			{
				register = (family == 0x2F08 ? 8 : 0) | (instruction.Opcode & 7);
				return true;
			}
			register = 0;
			return false;
		}
	}

	private bool TryCanonicalizeZeroDisplacementEffectiveAddress()
	{
		foreach (var instruction in _assembler.GetInstructionStream())
		{
			if (!instruction.IsDecoded ||
				instruction.Length < 4 ||
				HasInternalLabel(instruction))
			{
				continue;
			}

			var opcode = instruction.Opcode;
			var sizeCode = (opcode >> 12) & 0x0F;
			if (sizeCode is 1 or 2 or 3)
			{
				// MOVE encodes its source and destination effective addresses in
				// different bit fields. Source extensions precede destination
				// extensions, so canonicalize one operand per optimizer iteration.
				if (((opcode >> 3) & 7) == 5 &&
					_buffer.ReadWord(instruction.Offset + 2) == 0)
				{
					_buffer.WriteWord(
						instruction.Offset,
						(ushort)((opcode & 0xFFC7) | 0x0010)); // 0(An) -> (An)
					_buffer.RemoveBytes(instruction.Offset + 2, 2);
					return true;
				}

				if (((opcode >> 6) & 7) == 5 &&
					_buffer.ReadWord(instruction.Offset + instruction.Length - 2) == 0)
				{
					_buffer.WriteWord(
						instruction.Offset,
						(ushort)((opcode & 0xFE3F) | 0x0080)); // 0(An) -> (An)
					_buffer.RemoveBytes(instruction.Offset + instruction.Length - 2, 2);
					return true;
				}

				continue;
			}

			// For every other decoded instruction the effective-address field is
			// the low six opcode bits. Any opcode-specific extension words precede
			// the effective-address extension, making its displacement the final
			// word of the instruction.
			if (((opcode >> 3) & 7) != 5 ||
				_buffer.ReadWord(instruction.Offset + instruction.Length - 2) != 0)
			{
				continue;
			}

			_buffer.WriteWord(
				instruction.Offset,
				(ushort)((opcode & 0xFFC7) | 0x0010)); // 0(An) -> (An)
			_buffer.RemoveBytes(instruction.Offset + instruction.Length - 2, 2);
			return true;
		}

		return false;
	}

	private bool TrySynthesizeSizeFirstDataRegisterConstant(
		M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xF1FF) != 0x203C ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset) ||
				!IsSizeFirstOffset(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!TrySelectM68020ConstantSynthesis(
					instruction.ExtensionLong,
					facts.LiveConditionsAfter,
					out var candidate))
			{
				continue;
			}

			var destination = instruction.Opcode >> 9 & 7;
			_buffer.WriteWord(
				instruction.Offset,
				(ushort)(0x7000 | (destination << 9) | (byte)candidate.MoveQuickValue));
			_buffer.WriteWord(
				instruction.Offset + 2,
				candidate.EncodeTransform(destination));
			_buffer.RemoveBytes(instruction.Offset + candidate.EncodedBytes, 2);
			return true;
		}

		return false;
	}

	private static bool TrySelectM68020ConstantSynthesis(
		uint value,
		M68kConditionCodeSet liveConditions,
		out ConstantSynthesisCandidate selected)
	{
		selected = default;
		var found = false;
		// The direct MOVE.L immediate is 6 bytes and 6 cache-case cycles. Within
		// a size-first loop compare bytes first, then cache cycles for equal sizes.
		var bestBytes = 6;
		var bestCycles = 6;
		for (var seed = (int)sbyte.MinValue; seed <= sbyte.MaxValue; seed++)
		{
			var moveQuickValue = unchecked((uint)(int)seed);
			foreach (var transform in M68020ConstantSynthesisTransforms)
			{
				var transformed = transform.Operation switch
				{
					ConstantSynthesisOperation.Swap =>
						(moveQuickValue << 16) | (moveQuickValue >> 16),
					ConstantSynthesisOperation.RotateRight =>
						RotateRight(moveQuickValue, transform.Count),
					_ => throw new ArgumentOutOfRangeException()
				};
				if (transformed != value)
				{
					continue;
				}

				// MOVE clears C. ROR instead copies its last shifted-out bit to C;
				// reject that alternative only when the differing carry is live.
				var differingConditions = transform.Operation == ConstantSynthesisOperation.RotateRight &&
					((moveQuickValue >> (transform.Count - 1)) & 1) != 0
						? M68kConditionCodeSet.Carry
						: M68kConditionCodeSet.None;
				if ((liveConditions & differingConditions) != 0)
				{
					continue;
				}

				var candidate = new ConstantSynthesisCandidate(
					unchecked((sbyte)seed),
					transform,
					EncodedBytes: 4,
					CacheCycles: 2 + transform.CacheCycles);
				if (candidate.EncodedBytes < bestBytes ||
					candidate.EncodedBytes == bestBytes &&
					candidate.CacheCycles < bestCycles)
				{
					selected = candidate;
					found = true;
					bestBytes = candidate.EncodedBytes;
					bestCycles = candidate.CacheCycles;
				}
			}
		}

		return found;
	}

	private static uint RotateRight(uint value, int count) =>
		(value >> count) | (value << (32 - count));

	private bool TryRemoveRedundantAddressRegisterReload(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			if ((first.Opcode & 0xF1C0) != 0x2040 ||
				((first.Opcode >> 3) & 7) > 1)
			{
				continue;
			}

			var sourceRegister = first.Opcode & 7;
			var sourceIsAddress = ((first.Opcode >> 3) & 7) == 1;
			var destinationRegister = (first.Opcode >> 9) & 7;
			var sourceMask = (ushort)(1 << sourceRegister);
			var destinationMask = (ushort)(1 << destinationRegister);
			for (var candidateIndex = index + 1;
				candidateIndex < instructions.Count;
				candidateIndex++)
			{
				var candidate = instructions[candidateIndex];
				if (IsReferencedLabelAt(candidate.Offset))
				{
					break;
				}
				if (candidate.Opcode == first.Opcode && candidate.Length == first.Length)
				{
					_buffer.RemoveBytes(candidate.Offset, candidate.Length);
					return true;
				}
				if (!dataflow.TryGetFacts(candidate.Offset, out var facts) ||
					facts.Effects.IsBarrier ||
					(facts.Effects.DefinesAddress & destinationMask) != 0 ||
					(sourceIsAddress
						? (facts.Effects.DefinesAddress & sourceMask) != 0
						: (facts.Effects.DefinesData & sourceMask) != 0))
				{
					break;
				}
			}
		}
		return false;
	}

	private bool TryOptimizeSmallAddressImmediate(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		M68kEmittedInstruction? narrowCandidate = null;
		for (var index = 0; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if ((instruction.Opcode & 0xF1FF) != 0x207C ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset))
			{
				continue;
			}

			var destination = instruction.Opcode >> 9 & 7;
			var value = instruction.ExtensionLong;
			if (destination == 7)
			{
				if (IsSignedWord(value))
				{
					narrowCandidate ??= instruction;
				}
				continue;
			}

			if (index != 0)
			{
				var previous = instructions[index - 1];
				if ((previous.Opcode & 0xF1FF) == 0x207C &&
					previous.Length == 6 &&
					!HasAddressFixup(previous))
				{
					var previousDestination = previous.Opcode >> 9 & 7;
					if (previousDestination == destination)
					{
						var displacement = unchecked((int)(value - previous.ExtensionLong));
						if (displacement == 0)
						{
							_buffer.RemoveBytes(instruction.Offset, instruction.Length);
							return true;
						}
						if (displacement is >= -8 and <= 8)
						{
							var count = Math.Abs(displacement);
							var encodedCount = count == 8 ? 0 : count;
							_buffer.WriteWord(
								instruction.Offset,
								(ushort)((displacement < 0 ? 0x5188 : 0x5088) |
									(encodedCount << 9) |
									destination));
							_buffer.RemoveBytes(instruction.Offset + 2, 4);
							return true;
						}
					}
					else if (previous.ExtensionLong == value)
					{
						_buffer.WriteWord(
							instruction.Offset,
							(ushort)(0x2048 |
								(destination << 9) |
								previousDestination));
						_buffer.RemoveBytes(instruction.Offset + 2, 4);
						return true;
					}
				}
			}

			for (var source = 0; source < 8; source++)
			{
				if (!dataflow.GetDataValueBefore(instruction.Offset, source)
						.IsExact(out var sourceValue) ||
					sourceValue != value)
				{
					continue;
				}

				_buffer.WriteWord(
					instruction.Offset,
					(ushort)(0x2040 | (destination << 9) | source));
				_buffer.RemoveBytes(instruction.Offset + 2, 4);
				return true;
			}

			if (IsSignedWord(value))
			{
				narrowCandidate ??= instruction;
			}
		}

		if (narrowCandidate is not { } candidate)
		{
			return false;
		}

		_buffer.WriteWord(
			candidate.Offset,
			(ushort)(candidate.Opcode | 0x1000)); // MOVEA.L #imm,An -> MOVEA.W
		_buffer.RemoveBytes(candidate.Offset + 2, 2);
		return true;
	}

	private bool IsSizeFirstOffset(int offset) =>
		_cpu == M68kCpuTarget.M68020 &&
		_sizeFirstLoops.Any(loop => loop.Blocks.Any(block =>
			offset >= _buffer.Labels[block.StartLabel] &&
			offset < _buffer.AnalysisAnchors[block.EndLabel]));

	private bool HasAddressFixup(M68kEmittedInstruction instruction) =>
		_buffer.Addresses.Any(address =>
			address.Offset >= instruction.Offset &&
			address.Offset < instruction.Offset + instruction.Length);

	private static bool IsSignedWord(uint value) =>
		unchecked((int)value) is >= short.MinValue and <= short.MaxValue;

	private bool TryNarrowAddressArithmeticImmediate()
	{
		foreach (var instruction in _assembler.GetInstructionStream())
		{
			var opcodeFamily = instruction.Opcode & 0xF1FF;
			if (opcodeFamily is not 0xD1FC and not 0x91FC ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset) ||
				!IsSignedWord(instruction.ExtensionLong))
			{
				continue;
			}

			_buffer.WriteWord(
				instruction.Offset,
				(ushort)(instruction.Opcode & 0xFEFF)); // ADDA/SUBA.L -> .W
			_buffer.RemoveBytes(instruction.Offset + 2, 2);
			return true;
		}

		return false;
	}

	private bool TryReplaceStackImmediateWithPea(
		M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (instruction.Opcode != 0x2F3C ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset) ||
				!IsSignedWord(instruction.ExtensionLong) ||
				!IsSizeFirstOffset(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!facts.ConditionsAreDeadAfter)
			{
				continue;
			}

			_buffer.WriteWord(instruction.Offset, 0x4878); // PEA (xxx).W
			_buffer.RemoveBytes(instruction.Offset + 2, 2);
			return true;
		}

		return false;
	}

	private bool TryOptimizeDataRegisterImmediate(
		M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xF1FF) != 0x203C ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				facts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			var destination = instruction.Opcode >> 9 & 7;
			var value = instruction.ExtensionLong;
			if (dataflow.GetDataValueBefore(instruction.Offset, destination)
				.IsExact(out var currentValue))
			{
				var displacement = unchecked((int)(value - currentValue));
				if (displacement == 0)
				{
					_buffer.RemoveBytes(instruction.Offset, instruction.Length);
					return true;
				}
				if (displacement is >= -8 and <= 8)
				{
					var wordSized = CanAdjustDataConstantAsWord(
						currentValue,
						value,
						displacement);
					var candidateCycles = GetQuickDataRegisterCycles(wordSized);
					if (IsBetterConstantMaterialization(
						candidateBytes: 2,
						candidateCycles))
					{
						_buffer.WriteWord(
							instruction.Offset,
							EncodeQuickDataRegisterAdjustment(
								destination,
								displacement,
								wordSized));
						_buffer.RemoveBytes(instruction.Offset + 2, 4);
						return true;
					}
				}
			}

			if (dataflow.GetDataValueBefore(instruction.Offset, destination)
					.IsExact(out var priorValue) &&
				(priorValue & 0xFFFF0000u) == (value & 0xFFFF0000u))
			{
				_buffer.WriteWord(
					instruction.Offset,
					(ushort)(instruction.Opcode | 0x1000)); // MOVE.L #imm,Dn -> MOVE.W
				_buffer.RemoveBytes(instruction.Offset + 2, 2);
				return true;
			}

			var signedValue = unchecked((int)value);
			int baseValue;
			int displacementFromBase;
			if (signedValue is >= 128 and <= 135)
			{
				baseValue = 127;
				displacementFromBase = signedValue - baseValue;
			}
			else if (signedValue is >= -136 and <= -129)
			{
				baseValue = -128;
				displacementFromBase = signedValue - baseValue;
			}
			else
			{
				continue;
			}

			var sequenceCycles = GetMoveQuickCycles() +
				GetQuickDataRegisterCycles(wordSized: true);
			if (!IsBetterConstantMaterialization(
				candidateBytes: 4,
				sequenceCycles))
			{
				continue;
			}

			_buffer.WriteWord(
				instruction.Offset,
				(ushort)(0x7000 | (destination << 9) | (byte)baseValue));
			_buffer.WriteWord(
				instruction.Offset + 2,
				EncodeQuickDataRegisterAdjustment(
					destination,
					displacementFromBase,
					wordSized: true));
			_buffer.RemoveBytes(instruction.Offset + 4, 2);
			return true;
		}

		return false;
	}

	private bool TryUseDestructiveQuickImmediate(
		M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				continue;
			}

			var opcodeFamily = instruction.Opcode & 0xFFF8;
			var destination = instruction.Opcode & 7;
			var immediate = unchecked((int)instruction.ExtensionLong);
			int subtractCount;
			if (opcodeFamily == 0x0C80 &&
				immediate is >= 1 and <= 8 &&
				(facts.LiveDataAfter & (1 << destination)) == 0 &&
				(facts.LiveConditionsAfter & M68kConditionCodeSet.Extend) == 0)
			{
				// CMPI and SUBQ produce identical N/Z/V/C subtraction flags.
				// SUBQ additionally writes the destination and X, so both must be dead.
				subtractCount = immediate;
			}
			else if (opcodeFamily == 0x0680 &&
				immediate is >= -8 and <= -1 &&
				(facts.LiveConditionsAfter &
					(M68kConditionCodeSet.Carry | M68kConditionCodeSet.Extend)) == 0)
			{
				// Adding -n and subtracting n have the same result and N/Z/V flags,
				// but their carry and extend results differ.
				subtractCount = -immediate;
			}
			else
			{
				continue;
			}

			_buffer.WriteWord(
				instruction.Offset,
				EncodeQuickDataRegisterAdjustment(
					destination,
					-subtractCount,
					wordSized: false));
			_buffer.RemoveBytes(instruction.Offset + 2, 4);
			return true;
		}

		return false;
	}

	private static bool CanAdjustDataConstantAsWord(
		uint currentValue,
		uint value,
		int displacement)
	{
		if ((currentValue & 0xFFFF0000) != (value & 0xFFFF0000))
		{
			return false;
		}

		var adjusted = displacement > 0
			? (ushort)((currentValue & 0xFFFF) + displacement)
			: (ushort)((currentValue & 0xFFFF) - -displacement);
		return adjusted == (ushort)value;
	}

	private static ushort EncodeQuickDataRegisterAdjustment(
		int destination,
		int displacement,
		bool wordSized)
	{
		var count = Math.Abs(displacement);
		var encodedCount = count == 8 ? 0 : count;
		var opcode = displacement < 0
			? wordSized ? 0x5140 : 0x5180
			: wordSized ? 0x5040 : 0x5080;
		return (ushort)(opcode | (encodedCount << 9) | destination);
	}

	private bool IsBetterConstantMaterialization(
		int candidateBytes,
		int candidateCycles)
	{
		// CPU manual timings and Copper68k's executable timing profiles.
		// Prefer execution time first, then encoded size when cycles tie.
		const int immediateBytes = 6;
		var immediateCycles = _cpu switch
		{
			M68kCpuTarget.M68000 => 12,
			M68kCpuTarget.M68020 => 6,
			M68kCpuTarget.M68040 => 1,
			M68kCpuTarget.M68060 => 1,
			_ => throw new ArgumentOutOfRangeException()
		};
		return candidateCycles < immediateCycles ||
			candidateCycles == immediateCycles && candidateBytes < immediateBytes;
	}

	private int GetMoveQuickCycles() => _cpu switch
	{
		M68kCpuTarget.M68000 => 4,
		M68kCpuTarget.M68020 => 2,
		M68kCpuTarget.M68040 => 1,
		M68kCpuTarget.M68060 => 1,
		_ => throw new ArgumentOutOfRangeException()
	};

	private int GetQuickDataRegisterCycles(bool wordSized) => _cpu switch
	{
		M68kCpuTarget.M68000 => wordSized ? 4 : 8,
		// The MC68020 cache-case table gives both ADDQ and SUBQ to Rn
		// two clocks, independently of operand width.
		M68kCpuTarget.M68020 => 2,
		M68kCpuTarget.M68040 => 1,
		M68kCpuTarget.M68060 => 1,
		_ => throw new ArgumentOutOfRangeException()
	};

	private bool TryRemoveSelfMove(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in _assembler.GetInstructionStream())
		{
			if (instruction.Length != 2)
			{
				continue;
			}
			var opcode = instruction.Opcode;
			var sizeFamily = opcode & 0xF000;
			if (sizeFamily is 0x1000 or 0x2000 or 0x3000 &&
				((opcode >> 3) & 7) == 0 &&
				((opcode >> 6) & 7) == 0 &&
				(opcode & 7) == ((opcode >> 9) & 7) &&
				dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				if (facts.LiveConditionsAfter == M68kConditionCodeSet.None &&
					!IsReferencedLabelAt(instruction.Offset))
				{
					_buffer.RemoveBytes(instruction.Offset, instruction.Length);
				}
				else
				{
					var testBase = sizeFamily switch
					{
						0x1000 => 0x4A00,
						0x3000 => 0x4A40,
						_ => 0x4A80
					};
					_buffer.WriteWord(
						instruction.Offset,
						(ushort)(testBase | (opcode & 7)));
				}
				return true;
			}
			if ((opcode & 0xF1F8) == 0x2048 &&
				(opcode & 7) == ((opcode >> 9) & 7) &&
				!IsReferencedLabelAt(instruction.Offset))
			{
				_buffer.RemoveBytes(instruction.Offset, instruction.Length);
				return true;
			}
		}
		return false;
	}

	private bool TryLayoutColdTerminalBranch()
	{
		var instructions = _assembler.GetInstructionStream();
		var terminalLookup = CreateTerminalBlockLookup();
		for (var conditionalIndex = 0; conditionalIndex < instructions.Count; conditionalIndex++)
		{
			var conditional = instructions[conditionalIndex];
			if (conditional.Kind != M68kInstructionKind.ConditionalBranch ||
				conditional.Length != 4 ||
				!TryGetBranch(conditional.Offset, out var conditionalBranch) ||
				IsAllocatedBlockLabel(conditionalBranch.Target) ||
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
				!TryGetTerminalBlockEnd(
					tailOffset,
					failureOffset,
					instructions,
					terminalLookup,
					out var tailEnd))
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

	private static bool IsAllocatedBlockLabel(string label) =>
		label.Contains("_003ABB", StringComparison.Ordinal);

	private bool TryRemoveRedundantRuntimeFrameClear()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if (first.Opcode != 0x42AD ||
				first.Length != 4 ||
				second.Opcode != first.Opcode ||
				second.Length != first.Length ||
				second.ExtensionWord != first.ExtensionWord ||
				IsReferencedLabelAt(second.Offset))
			{
				continue;
			}

			_buffer.RemoveBytes(second.Offset, second.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveDeadStackStoreBeforeClear(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var store = instructions[index];
			var clear = instructions[index + 1];
			if ((store.Opcode & 0xF000) != 0x2000 ||
				!TryGetMoveDestination(store, out var storeMode, out var storeRegister) ||
				storeMode is not (2 or 5) ||
				((store.Opcode >> 3) & 7) > 1 ||
				(clear.Opcode & 0xFFC0) != 0x4280 ||
				(clear.Opcode >> 3 & 7) != storeMode ||
				(clear.Opcode & 7) != storeRegister ||
				clear.Length != (storeMode == 5 ? 4 : 2) ||
				storeMode == 5 && clear.ExtensionWord != store.ExtensionWord ||
				IsReferencedLabelAt(clear.Offset) ||
				dataflow.GetAddressAliasBefore(store.Offset, storeRegister).Kind !=
					M68kAddressAliasKind.Stack)
			{
				continue;
			}

			MoveLabelsToOffset(store.Offset, clear.Offset, clear.Offset);
			_buffer.RemoveBytes(store.Offset, store.Length);
			return true;
		}

		return false;
	}

	private bool TryForwardStackStoreReload(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var store = instructions[index];
			var reload = instructions[index + 1];
			if ((store.Opcode & 0xF038) != 0x2000 ||
				!TryGetMoveDestination(store, out var storeMode, out var baseRegister) ||
				storeMode is not (2 or 5) ||
				(reload.Opcode & 0xF1C0) != 0x2000 ||
				(reload.Opcode >> 3 & 7) != storeMode ||
				(reload.Opcode & 7) != baseRegister ||
				reload.Length != (storeMode == 5 ? 4 : 2) ||
				storeMode == 5 && reload.ExtensionWord != store.ExtensionWord ||
				IsReferencedLabelAt(reload.Offset) ||
				dataflow.GetAddressAliasBefore(store.Offset, baseRegister).Kind !=
					M68kAddressAliasKind.Stack)
			{
				continue;
			}

			var source = store.Opcode & 7;
			var destination = (reload.Opcode >> 9) & 7;
			var destinationIsRead = source != destination &&
				IsDataRegisterReadBeforeOverwrite(
					instructions,
					index + 2,
					destination,
					dataflow,
					treatReturnAsRead: true);
			if (destinationIsRead)
			{
				_buffer.WriteWord(
					reload.Offset,
					(ushort)(0x2000 | (destination << 9) | source));
				if (reload.Length > 2)
				{
					_buffer.RemoveBytes(reload.Offset + 2, reload.Length - 2);
				}
			}
			else
			{
				MoveLabelsToOffset(
					reload.Offset,
					reload.Offset + reload.Length,
					store.Offset);
				_buffer.RemoveBytes(reload.Offset, reload.Length);
			}
			return true;
		}

		return false;
	}

	private bool TryBypassTerminalStackReloadOnFallthrough(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var store = instructions[index];
			var reload = instructions[index + 1];
			var release = instructions[index + 2];
			if ((store.Opcode & 0xF038) != 0x2000 ||
				store.Length != 4 ||
				!TryGetMoveDestination(store, out var storeMode, out var storeBase) ||
				storeMode != 5 ||
				storeBase != 7 ||
				(reload.Opcode & 0xF1C0) != 0x2000 ||
				reload.Length != 4 ||
				((reload.Opcode >> 3) & 7) != 5 ||
				(reload.Opcode & 7) != 7 ||
				reload.ExtensionWord != store.ExtensionWord ||
				(store.Opcode & 7) != ((reload.Opcode >> 9) & 7) ||
				!IsReferencedLabelAt(reload.Offset) ||
				IsReferencedLabelAt(store.Offset) ||
				!IsPositiveStackRelease(release) ||
				!dataflow.TryGetFacts(reload.Offset, out var reloadFacts) ||
				reloadFacts.LiveConditionsAfter != M68kConditionCodeSet.None ||
				dataflow.GetAddressAliasBefore(store.Offset, 7).Kind !=
					M68kAddressAliasKind.Stack)
			{
				continue;
			}

			var targetOffset = reload.Offset + reload.Length;
			var target = _buffer.Labels
				.FirstOrDefault(label => label.Value == targetOffset).Key;
			if (target is null)
			{
				target = $"generated:terminal-reload-bypass:{store.Offset:X}";
				var suffix = 0;
				while (_buffer.Labels.ContainsKey(target))
				{
					target = $"generated:terminal-reload-bypass:{store.Offset:X}:{++suffix}";
				}
				_buffer.Labels.Add(target, targetOffset);
			}

			// Dn already contains the value this edge stored, but the store can
			// also publish exception/finally state. Preserve it and insert a
			// fallthrough-only branch around the common reload. Existing labeled
			// cleanup/leave predecessors continue to enter at the shifted reload.
			_buffer.InsertBytes(reload.Offset, 4);
			_buffer.WriteWord(reload.Offset, 0x6000); // BRA.W
			_buffer.WriteWord(reload.Offset + 2, 0);
			_buffer.Branches.Add(new BranchFixup(reload.Offset, target));
			return true;
		}

		return false;
	}

	private bool TryHoistZeroMoveAcrossStackClears(
		M68kInstructionDataflow dataflow)
	{
		if (_clrPolicy == M68kClrPolicy.Always)
		{
			return false;
		}

		var instructions = dataflow.Instructions;
		for (var moveIndex = 1; moveIndex < instructions.Count; moveIndex++)
		{
			var moveQuick = instructions[moveIndex];
			if ((moveQuick.Opcode & 0xF1FF) != 0x7000 ||
				moveQuick.Length != 2 ||
				IsReferencedLabelAt(moveQuick.Offset))
			{
				continue;
			}

			var zeroRegister = (moveQuick.Opcode >> 9) & 7;
			var zeroMask = (ushort)(1 << zeroRegister);
			var clears = new List<M68kEmittedInstruction>();
			for (var cursor = moveIndex - 1;
				cursor >= 0 && moveIndex - cursor <= 8;
				cursor--)
			{
				var candidate = instructions[cursor];
				if (candidate.Opcode == 0x42AF &&
					candidate.Length == 4 &&
					!IsReferencedLabelAt(candidate.Offset))
				{
					clears.Add(candidate);
					continue;
				}

				if (clears.Count == 0 &&
					!IsReferencedLabelAt(candidate.Offset) &&
					dataflow.TryGetFacts(candidate.Offset, out var facts) &&
					!facts.Effects.IsBarrier &&
					(facts.Effects.UsesData & zeroMask) == 0 &&
					(facts.Effects.DefinesData & zeroMask) == 0 &&
					facts.Effects.ReadsConditions == M68kConditionCodeSet.None &&
					facts.Effects.WritesConditions == M68kConditionCodeSet.None &&
					facts.Effects.ReadsMemory == M68kMemorySet.None &&
					facts.Effects.WritesMemory == M68kMemorySet.None)
				{
					continue;
				}

				break;
			}

			if (clears.Count == 0)
			{
				continue;
			}

			var firstOffset = clears[^1].Offset;
			_buffer.RemoveBytes(moveQuick.Offset, moveQuick.Length);
			_buffer.InsertBytes(firstOffset, 2);
			_buffer.WriteWord(firstOffset, moveQuick.Opcode);
			foreach (var clear in clears)
			{
				_buffer.WriteWord(
					clear.Offset + 2,
					0x2F40 | zeroRegister); // MOVE.L Dn,d16(A7)
			}
			return true;
		}

		return false;
	}

	private bool TryReuseZeroRegisterForStackStores(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		var referencedLabelOffsets = ReferencedLabelOffsets();
		var addressFixupOffsets = _assembler.AddressFixupOffsets;
		var rewrites = new List<(M68kEmittedInstruction Store, int Register)>();
		var selectedOffsets = new HashSet<int>();
		for (var zeroIndex = 0; zeroIndex + 1 < instructions.Count; zeroIndex++)
		{
			var zero = instructions[zeroIndex];
			var zeroRegister = (zero.Opcode & 0xFFF8) == 0x4280 && zero.Length == 2
				? zero.Opcode & 7 // CLR.L Dn
				: (zero.Opcode & 0xF1FF) == 0x7000 && zero.Length == 2
					? (zero.Opcode >> 9) & 7 // MOVEQ #0,Dn
					: -1;
			if (zeroRegister < 0)
			{
				continue;
			}

			var zeroMask = (ushort)(1 << zeroRegister);
			for (var cursor = zeroIndex + 1;
				cursor < instructions.Count && cursor - zeroIndex <= 8;
				cursor++)
			{
				var candidate = instructions[cursor];
				if (referencedLabelOffsets.Contains(candidate.Offset))
				{
					break;
				}

				if (candidate.Opcode is 0x2F7C or 0x2B7C &&
					candidate.Length == 8 &&
					!addressFixupOffsets.Contains(candidate.Offset + 2) &&
					_buffer.ReadLong(candidate.Offset + 2) == 0)
				{
					if (selectedOffsets.Add(candidate.Offset))
					{
						rewrites.Add((candidate, zeroRegister));
					}
					continue;
				}

				if (IsFrameStoreFromDataRegister(candidate, zeroRegister))
				{
					continue;
				}

				if (!dataflow.TryGetFacts(candidate.Offset, out var facts) ||
					candidate.Kind != M68kInstructionKind.Normal ||
					facts.Effects.IsBarrier ||
					(facts.Effects.DefinesData & zeroMask) != 0)
				{
					break;
				}
			}
		}

		for (var index = rewrites.Count - 1; index >= 0; index--)
		{
			var (store, register) = rewrites[index];
			var registerStore = store.Opcode == 0x2F7C
				? 0x2F40 // MOVE.L Dn,d16(A7)
				: 0x2B40; // MOVE.L Dn,d16(A5)
			_buffer.WriteWord(store.Offset, registerStore | register);
			_buffer.RemoveBytes(store.Offset + 2, 4);
		}
		return rewrites.Count != 0;
	}

	private static bool IsFrameStoreFromDataRegister(
		M68kEmittedInstruction instruction,
		int register)
	{
		var size = instruction.Opcode & 0xF000;
		var sourceMode = (instruction.Opcode >> 3) & 7;
		var destinationMode = (instruction.Opcode >> 6) & 7;
		var destinationRegister = (instruction.Opcode >> 9) & 7;
		return instruction.Length == 4 &&
			size is 0x1000 or 0x2000 or 0x3000 &&
			sourceMode == 0 &&
			(instruction.Opcode & 7) == register &&
			destinationMode == 5 &&
			destinationRegister is 5 or 7;
	}

	private static bool IsPositiveStackRelease(M68kEmittedInstruction instruction)
	{
		if ((instruction.Opcode & 0xF1FF) == 0x508F)
		{
			return true; // ADDQ.L #n,A7
		}
		if ((instruction.Opcode & 0xF1FF) is 0xD0FC or 0xD1FC)
		{
			return ((instruction.Opcode >> 9) & 7) == 7;
		}
		return (instruction.Opcode & 0xFFFF) == 0x4FEF &&
			unchecked((short)instruction.ExtensionWord) > 0; // LEA d(A7),A7
	}

	private static bool TryGetMoveDestination(
		M68kEmittedInstruction instruction,
		out int mode,
		out int register)
	{
		mode = (instruction.Opcode >> 6) & 7;
		register = (instruction.Opcode >> 9) & 7;
		return mode is not (0 or 1);
	}

	private bool TryRemoveAliasedRuntimeFrameClear()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var aliasIndex = 1; aliasIndex + 1 < instructions.Count; aliasIndex++)
		{
			var alias = instructions[aliasIndex];
			var frameClear = instructions[aliasIndex + 1];
			if (alias.Opcode != 0x2A4F || // MOVEA.L A7,A5
				alias.Length != 2 ||
				frameClear.Opcode != 0x42AD || // CLR.L d16(A5)
				frameClear.Length != 4 ||
				IsReferencedLabelAt(alias.Offset) ||
				IsReferencedLabelAt(frameClear.Offset))
			{
				continue;
			}

			var matchingStackClear = false;
			for (var clearIndex = aliasIndex - 1; clearIndex >= 0; clearIndex--)
			{
				var stackClear = instructions[clearIndex];
				if (stackClear.Opcode != 0x42AF || // CLR.L d16(A7)
					stackClear.Length != 4 ||
					IsReferencedLabelAt(stackClear.Offset))
				{
					break;
				}

				matchingStackClear |= stackClear.ExtensionWord == frameClear.ExtensionWord;
			}

			if (!matchingStackClear)
			{
				continue;
			}

			_buffer.RemoveBytes(frameClear.Offset, frameClear.Length);
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
		TerminalBlockLookup lookup,
		out int tailEnd)
	{
		tailEnd = 0;
		var candidateTailEnd = NextOffsetAfter(
			lookup.MethodEndOffsets,
			tailOffset,
			NextOffsetAfter(lookup.LabelOffsets, tailOffset, _buffer.Bytes.Count));
		if (candidateTailEnd <= tailOffset ||
			candidateTailEnd - 2 < tailOffset ||
			_buffer.ReadWord(candidateTailEnd - 2) != 0x4E75 ||
			HasOffsetInRange(lookup.BranchOffsets, failureOffset, candidateTailEnd) ||
			HasOffsetInRange(lookup.AddressOffsets, failureOffset, candidateTailEnd) ||
			HasOffsetInRange(lookup.PcRelativeOffsets, failureOffset, candidateTailEnd))
		{
			return false;
		}

		var instructionIndex = LowerBoundInstruction(instructions, tailOffset);
		var hasTailInstruction = false;
		var lastKind = M68kInstructionKind.Normal;
		for (; instructionIndex < instructions.Count; instructionIndex++)
		{
			var instruction = instructions[instructionIndex];
			if (instruction.Offset >= candidateTailEnd)
			{
				break;
			}
			if (instruction.Kind is not (M68kInstructionKind.Normal or M68kInstructionKind.Return))
			{
				return false;
			}

			hasTailInstruction = true;
			lastKind = instruction.Kind;
		}
		if (!hasTailInstruction || lastKind != M68kInstructionKind.Return)
		{
			return false;
		}

		tailEnd = candidateTailEnd;
		return true;
	}

	private TerminalBlockLookup CreateTerminalBlockLookup()
	{
		var labelOffsets = new int[_buffer.Labels.Count];
		var methodEndOffsets = new List<int>();
		var labelIndex = 0;
		foreach (var label in _buffer.Labels)
		{
			labelOffsets[labelIndex++] = label.Value;
			if (label.Key.EndsWith(":end", StringComparison.Ordinal))
			{
				methodEndOffsets.Add(label.Value);
			}
		}

		var branchOffsets = _buffer.Branches
			.Select(static branch => branch.OpcodeOffset)
			.ToArray();
		var addressOffsets = _buffer.Addresses
			.Select(static address => address.Offset)
			.ToArray();
		var pcRelativeOffsets = _buffer.PcRelative
			.Select(static reference => reference.DisplacementOffset)
			.ToArray();
		var methodEnds = methodEndOffsets.ToArray();
		Array.Sort(methodEnds);
		Array.Sort(labelOffsets);
		Array.Sort(branchOffsets);
		Array.Sort(addressOffsets);
		Array.Sort(pcRelativeOffsets);
		return new TerminalBlockLookup(
			methodEnds,
			labelOffsets,
			branchOffsets,
			addressOffsets,
			pcRelativeOffsets);
	}

	private static int NextOffsetAfter(int[] sortedOffsets, int offset, int fallback)
	{
		var low = 0;
		var high = sortedOffsets.Length;
		while (low < high)
		{
			var middle = low + ((high - low) >> 1);
			if (sortedOffsets[middle] <= offset)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low < sortedOffsets.Length ? sortedOffsets[low] : fallback;
	}

	private static bool HasOffsetInRange(int[] sortedOffsets, int start, int end)
	{
		var low = 0;
		var high = sortedOffsets.Length;
		while (low < high)
		{
			var middle = low + ((high - low) >> 1);
			if (sortedOffsets[middle] < start)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low < sortedOffsets.Length && sortedOffsets[low] < end;
	}

	private static int LowerBoundInstruction(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int offset)
	{
		var low = 0;
		var high = instructions.Count;
		while (low < high)
		{
			var middle = low + ((high - low) >> 1);
			if (instructions[middle].Offset < offset)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
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

			var temporaryRemainsLive = IsDataRegisterReadBeforeOverwrite(
				instructions,
				index + 2,
				temporaryRegister,
				dataflow);
			if (!temporaryRemainsLive &&
				!IsReferencedLabelAt(first.Offset) &&
				facts.LiveConditionsAfter == M68kConditionCodeSet.None)
			{
				MoveLabelsToOffset(
					first.Offset + first.Length,
					second.Offset + second.Length,
					first.Offset);
				_buffer.RemoveBytes(second.Offset, second.Length);
				_buffer.RemoveBytes(first.Offset, first.Length);
			}
			else if (!temporaryRemainsLive &&
				!IsReferencedLabelAt(first.Offset))
			{
				_buffer.WriteWord(
					first.Offset,
					(ushort)(0x4A80 | sourceRegister)); // TST.L Dn
				_buffer.RemoveBytes(second.Offset, second.Length);
			}
			else
			{
				// Both moves publish the same long value and therefore the same
				// N/Z/V/C flags. Keep the first copy when its destination remains
				// live, but do not copy the unchanged value back to its source.
				_buffer.RemoveBytes(second.Offset, second.Length);
			}

			return true;
		}

		return false;
	}

	private bool TryForwardDataRegisterCopyChain(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if ((first.Opcode & 0xF1F8) != 0x2000 ||
				first.Length != 2 ||
				(second.Opcode & 0xF1F8) != 0x2000 ||
				second.Length != 2 ||
				IsReferencedLabelAt(second.Offset) ||
				!dataflow.TryGetFacts(second.Offset, out var facts))
			{
				continue;
			}

			var source = first.Opcode & 7;
			var temporary = (first.Opcode >> 9) & 7;
			var destination = (second.Opcode >> 9) & 7;
			if (source == temporary ||
				(second.Opcode & 7) != temporary ||
				destination == source ||
				destination == temporary ||
				(facts.LiveDataAfter & (1 << temporary)) != 0)
			{
				continue;
			}

			// Both MOVEs publish the same value and condition codes. Write it to
			// the final destination directly when the temporary dies here.
			_buffer.WriteWord(
				first.Offset,
				0x2000 | (destination << 9) | source);
			_buffer.RemoveBytes(second.Offset, second.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveRepeatedLea()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if ((first.Opcode & 0xF1F8) != 0x41D0 ||
				((first.Opcode >> 9) & 7) == (first.Opcode & 7) ||
				first.Length != second.Length ||
				first.Opcode != second.Opcode ||
				first.ExtensionWord != second.ExtensionWord ||
				first.ExtensionLong != second.ExtensionLong ||
				IsReferencedLabelAt(second.Offset))
			{
				continue;
			}

			// LEA does not alter condition codes. Repeating the exact same address
			// calculation into the same register has no observable effect.
			_buffer.RemoveBytes(second.Offset, second.Length);
			return true;
		}

		return false;
	}

	private bool TryFoldZeroDisplacementLeaToDataMove(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var lea = instructions[index];
			var move = instructions[index + 1];
			var leaMode = (lea.Opcode >> 3) & 7;
			if ((lea.Opcode & 0xF1C0) != 0x41C0 ||
				leaMode is not (2 or 5) ||
				(leaMode == 5 && (lea.Length != 4 || lea.ExtensionWord != 0)) ||
				(leaMode == 2 && lea.Length != 2) ||
				(move.Opcode & 0xF1F8) != 0x2008 ||
				move.Length != 2 ||
				IsReferencedLabelAt(move.Offset) ||
				!dataflow.TryGetFacts(move.Offset, out var facts))
			{
				continue;
			}

			var temporaryAddress = (lea.Opcode >> 9) & 7;
			if ((move.Opcode & 7) != temporaryAddress ||
				(facts.LiveAddressAfter & (1 << temporaryAddress)) != 0)
			{
				continue;
			}

			var sourceAddress = lea.Opcode & 7;
			var destinationData = (move.Opcode >> 9) & 7;
			_buffer.WriteWord(
				lea.Offset,
				(ushort)(0x2008 | (destinationData << 9) | sourceAddress));
			_buffer.RemoveBytes(lea.Offset + 2, lea.Length - 2 + move.Length);
			return true;
		}

		return false;
	}

	private bool TryForwardAddressToDataCopyChain(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if ((first.Opcode & 0xF1F8) != 0x2008 ||
				first.Length != 2 ||
				(second.Opcode & 0xF1F8) != 0x2000 ||
				second.Length != 2 ||
				IsReferencedLabelAt(second.Offset) ||
				!dataflow.TryGetFacts(second.Offset, out var facts))
			{
				continue;
			}

			var temporary = (first.Opcode >> 9) & 7;
			var destination = (second.Opcode >> 9) & 7;
			if ((second.Opcode & 7) != temporary ||
				destination == temporary ||
				(facts.LiveDataAfter & (1 << temporary)) != 0)
			{
				continue;
			}

			// Both long MOVEs publish the same N/Z/V/C state. Move the address
			// directly to the final data register when the temporary dies here.
			_buffer.WriteWord(
				first.Offset,
				(ushort)(0x2008 | (destination << 9) | (first.Opcode & 7)));
			_buffer.RemoveBytes(second.Offset, second.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveDeadAddressRegisterCopyBeforeBranch(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if ((instruction.Opcode & 0xF1F8) != 0x2040 ||
				instruction.Length != 2 ||
				instructions[index + 1].Kind != M68kInstructionKind.ConditionalBranch ||
				HasInternalLabel(instruction))
			{
				continue;
			}

			var addressRegister = (instruction.Opcode >> 9) & 7;
			if (IsAddressRegisterReadBeforeOverwrite(
				instructions,
				index + 1,
				addressRegister,
				dataflow))
			{
				continue;
			}

			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			return true;
		}

		return false;
	}

	private bool TryFoldDataRegisterExchange(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			var third = instructions[index + 2];
			if ((first.Opcode & 0xF1F8) != 0x2000 ||
				first.Length != 2 ||
				(second.Opcode & 0xF1F8) != 0x2000 ||
				second.Length != 2 ||
				(third.Opcode & 0xF1F8) != 0x2000 ||
				third.Length != 2 ||
				_buffer.HasLabelAt(second.Offset) ||
				_buffer.HasLabelAt(third.Offset) ||
				!dataflow.TryGetFacts(third.Offset, out var facts) ||
				facts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			var firstSource = first.Opcode & 7;
			var temporary = (first.Opcode >> 9) & 7;
			var secondSource = second.Opcode & 7;
			var secondDestination = (second.Opcode >> 9) & 7;
			var thirdSource = third.Opcode & 7;
			var thirdDestination = (third.Opcode >> 9) & 7;
			if (firstSource == temporary ||
				firstSource == secondSource ||
				secondDestination != firstSource ||
				thirdSource != temporary ||
				thirdDestination != secondSource ||
				temporary == secondSource ||
				(facts.LiveDataAfter & (1 << temporary)) != 0)
			{
				continue;
			}

			_buffer.WriteWord(
				first.Offset,
				(ushort)(0xC140 | (firstSource << 9) | secondSource)); // EXG Dn,Dm
			_buffer.RemoveBytes(second.Offset, second.Length);
			_buffer.RemoveBytes(third.Offset - second.Length, third.Length);
			return true;
		}

		return false;
	}

	private bool TryFoldDataRegisterCopyUpdate(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var copy = instructions[index];
			var arithmetic = instructions[index + 1];
			var copyBack = instructions[index + 2];
			if ((copy.Opcode & 0xF1F8) != 0x2000 ||
				copy.Length != 2 ||
				(copyBack.Opcode & 0xF1F8) != 0x2000 ||
				copyBack.Length != 2 ||
				IsReferencedLabelAt(arithmetic.Offset) ||
				IsReferencedLabelAt(copyBack.Offset) ||
				!dataflow.TryGetFacts(copyBack.Offset, out var facts))
			{
				continue;
			}

			var source = copy.Opcode & 7;
			var temporary = (copy.Opcode >> 9) & 7;
			var temporaryRemainsLive =
				(facts.LiveDataAfter & (1 << temporary)) != 0;
			if (source == temporary ||
				(copyBack.Opcode & 7) != temporary ||
				((copyBack.Opcode >> 9) & 7) != source)
			{
				continue;
			}

			var registerDestinationOperation = arithmetic.Opcode & 0xF1F8;
			var isRegisterDestination = registerDestinationOperation is
				0xD080 or 0x9080 or 0x8080 or 0xC080;
			var isEor = (arithmetic.Opcode & 0xF1F8) == 0xB180;
			if (arithmetic.Length != 2 ||
				isRegisterDestination && ((arithmetic.Opcode >> 9) & 7) != temporary ||
				isEor && (arithmetic.Opcode & 7) != temporary ||
				!isRegisterDestination && !isEor)
			{
				continue;
			}

			var isAddOrSubtract = registerDestinationOperation is 0xD080 or 0x9080;
			if (!temporaryRemainsLive &&
				isAddOrSubtract &&
				(facts.LiveConditionsAfter &
				 (M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry)) != 0)
			{
				continue;
			}

			var operand = isEor
				? (arithmetic.Opcode >> 9) & 7
				: arithmetic.Opcode & 7;
			if (operand == temporary)
			{
				operand = source;
			}
			var replacement = isEor
				? (ushort)((arithmetic.Opcode & 0xF1F8) | (operand << 9) | source)
				: (ushort)((arithmetic.Opcode & 0xF1F8) | (source << 9) | operand);
			_buffer.WriteWord(copy.Offset, replacement);
			if (temporaryRemainsLive)
			{
				_buffer.WriteWord(
					arithmetic.Offset,
					(ushort)(0x2000 | (temporary << 9) | source));
				_buffer.RemoveBytes(copyBack.Offset, copyBack.Length);
			}
			else
			{
				_buffer.RemoveBytes(copyBack.Offset, copyBack.Length);
				_buffer.RemoveBytes(arithmetic.Offset, arithmetic.Length);
			}
			return true;
		}

		return false;
	}

	private static bool IsDataRegisterReadBeforeOverwrite(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int register,
		M68kInstructionDataflow dataflow,
		bool treatReturnAsRead = false)
	{
		var mask = (ushort)(1 << register);
		for (var index = startIndex; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (instruction.Kind == M68kInstructionKind.Return)
			{
				return treatReturnAsRead;
			}
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				facts.Effects.IsBarrier &&
				instruction.Kind is not (M68kInstructionKind.ConditionalBranch or
					M68kInstructionKind.Dbcc or
					M68kInstructionKind.UnconditionalBranch))
			{
				return true;
			}
			if ((facts.Effects.UsesData & mask) != 0)
			{
				return true;
			}
			if ((facts.Effects.DefinesData & mask) != 0)
			{
				return false;
			}
			if (instruction.Kind is M68kInstructionKind.ConditionalBranch or
				M68kInstructionKind.Dbcc &&
				IsBranchTargetReadBeforeOverwrite(
					instructions,
					instruction.TargetOffset,
					register,
					addressRegister: false,
					dataflow))
			{
				return true;
			}
			if (instruction.Kind == M68kInstructionKind.UnconditionalBranch)
			{
				return IsBranchTargetReadBeforeOverwrite(
					instructions,
					instruction.TargetOffset,
					register,
					addressRegister: true,
					dataflow);
			}
		}

		return false;
	}

	private bool TryForwardAddressRegisterBase(M68kInstructionDataflow dataflow)
	{
		if (TryForwardLeaToMemoryLoad(dataflow))
		{
			return true;
		}

		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var copy = instructions[index];
			var memory = instructions[index + 1];
			if ((copy.Opcode & 0xF1F8) != 0x2048 ||
				copy.Length != 2 ||
				IsReferencedLabelAt(memory.Offset) ||
				!dataflow.TryGetFacts(memory.Offset, out var facts))
			{
				continue;
			}
			var source = copy.Opcode & 7;
			var temporary = (copy.Opcode >> 9) & 7;
			if (source > 6 || temporary > 6 || source == temporary ||
				IsAddressRegisterReadBeforeOverwrite(
					instructions,
					index + 2,
					temporary,
					dataflow))
			{
				continue;
			}

			var opcode = memory.Opcode;
			var sizeCode = (opcode >> 12) & 0xF;
			if (sizeCode is not (1 or 2 or 3))
			{
				continue;
			}
			var sourceMode = (opcode >> 3) & 7;
			var destinationMode = (opcode >> 6) & 7;
			if (sourceMode is 2 or 5 or 6 && (opcode & 7) == temporary)
			{
				opcode = (ushort)((opcode & ~7) | source);
			}
			else if (destinationMode is 2 or 5 or 6 &&
				((opcode >> 9) & 7) == temporary)
			{
				opcode = (ushort)((opcode & ~(7 << 9)) | (source << 9));
			}
			else
			{
				continue;
			}

			_buffer.WriteWord(memory.Offset, opcode);
			MoveLabelsToOffset(copy.Offset, memory.Offset, copy.Offset);
			_buffer.RemoveBytes(copy.Offset, copy.Length);
			return true;
		}
		return false;
	}

	private bool TryForwardLeaToMemoryLoad(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var lea = instructions[index];
			var load = instructions[index + 1];
			if ((lea.Opcode & 0xF1F8) != 0x41E8 ||
				lea.Length != 4 ||
				load.Length != 2 ||
				((load.Opcode >> 12) & 0xF) is not (1 or 2 or 3) ||
				((load.Opcode >> 3) & 7) != 2 ||
				((load.Opcode >> 6) & 7) is var destinationMode &&
				destinationMode != 0 &&
				(destinationMode != 1 || (load.Opcode >> 12) != 2) ||
				IsReferencedLabelAt(lea.Offset) ||
				IsReferencedLabelAt(load.Offset))
			{
				continue;
			}

			var addressRegister = (lea.Opcode >> 9) & 7;
			if (addressRegister == 7 ||
				!dataflow.TryGetFacts(load.Offset, out _) ||
				IsAddressRegisterReadBeforeOverwrite(
					instructions,
					index + 2,
					addressRegister,
					dataflow))
			{
				continue;
			}

			var baseRegister = lea.Opcode & 7;
			var replacement = (ushort)((load.Opcode & ~0x3F) | 0x28 | baseRegister);
			_buffer.WriteWord(lea.Offset, replacement); // MOVE.<size> d16(An),Dn
			_buffer.WriteWord(lea.Offset + 2, lea.ExtensionWord);
			_buffer.RemoveBytes(load.Offset, load.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveAddressRegisterRoundTrip(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if ((first.Opcode & 0xF1F8) != 0x2048 ||
				first.Length != 2 ||
				(second.Opcode & 0xF1F8) != 0x2048 ||
				second.Length != 2 ||
				IsReferencedLabelAt(second.Offset))
			{
				continue;
			}

			var sourceRegister = first.Opcode & 7;
			var temporaryRegister = (first.Opcode >> 9) & 7;
			if (sourceRegister > 6 ||
				temporaryRegister > 6 ||
				sourceRegister == temporaryRegister ||
				(second.Opcode & 7) != temporaryRegister ||
				((second.Opcode >> 9) & 7) != sourceRegister ||
				!dataflow.TryGetFacts(second.Offset, out var facts))
			{
				continue;
			}

			var temporaryRemainsLive = IsAddressRegisterReadBeforeOverwrite(
				instructions,
				index + 2,
				temporaryRegister,
				dataflow);
			if (!temporaryRemainsLive &&
				!IsReferencedLabelAt(first.Offset))
			{
				MoveLabelsToOffset(
					first.Offset + first.Length,
					second.Offset + second.Length,
					first.Offset);
				_buffer.RemoveBytes(second.Offset, second.Length);
				_buffer.RemoveBytes(first.Offset, first.Length);
			}
			else
			{
				_buffer.RemoveBytes(second.Offset, second.Length);
			}
			return true;
		}
		return false;
	}

	private static bool IsBranchTargetReadBeforeOverwrite(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int? targetOffset,
		int register,
		bool addressRegister,
		M68kInstructionDataflow dataflow)
	{
		var indexByOffset = instructions
			.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(static item => item.Offset, static item => item.index);
		if (!targetOffset.HasValue ||
			!indexByOffset.TryGetValue(targetOffset.Value, out var startIndex))
		{
			return false;
		}
		var pending = new Stack<int>();
		var visited = new HashSet<int>();
		pending.Push(startIndex);
		var mask = (ushort)(1 << register);

		while (pending.Count != 0)
		{
			var index = pending.Pop();
			if (!visited.Add(index))
			{
				continue;
			}

			var instruction = instructions[index];
			// RTS models the full ABI register set as used so prologue restores stay
			// live.  It does not consume a temporary value for this local rewrite.
			if (instruction.Kind == M68kInstructionKind.Return)
			{
				continue;
			}
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				return true;
			}

			var uses = addressRegister
				? facts.Effects.UsesAddress
				: facts.Effects.UsesData;
			var definitions = addressRegister
				? facts.Effects.DefinesAddress
				: facts.Effects.DefinesData;
			if ((uses & mask) != 0 || facts.Effects.IsBarrier)
			{
				return true;
			}
			if ((definitions & mask) != 0)
			{
				continue;
			}

			var next = index + 1;
			switch (instruction.Kind)
			{
				case M68kInstructionKind.ConditionalBranch:
				case M68kInstructionKind.Dbcc:
					PushTarget(instruction.TargetOffset);
					PushNext();
					break;
				case M68kInstructionKind.UnconditionalBranch:
					PushTarget(instruction.TargetOffset);
					break;
				default:
					PushNext();
					break;
			}

			void PushTarget(int? targetOffset)
			{
				if (targetOffset.HasValue &&
					indexByOffset.TryGetValue(targetOffset.Value, out var targetIndex))
				{
					pending.Push(targetIndex);
				}
			}

			void PushNext()
			{
				if (next < instructions.Count)
				{
					pending.Push(next);
				}
			}
		}

		return false;
	}

	private static bool IsAddressRegisterReadBeforeOverwrite(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int register,
		M68kInstructionDataflow dataflow)
	{
		var mask = (ushort)(1 << register);
		for (var index = startIndex; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (instruction.Kind == M68kInstructionKind.Return)
			{
				return false;
			}
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				facts.Effects.IsBarrier)
			{
				return true;
			}
			if ((facts.Effects.UsesAddress & mask) != 0)
			{
				return true;
			}
			if ((facts.Effects.DefinesAddress & mask) != 0)
			{
				return false;
			}
			if (instruction.Kind is M68kInstructionKind.ConditionalBranch or
				M68kInstructionKind.Dbcc &&
				IsBranchTargetReadBeforeOverwrite(
					instructions,
					instruction.TargetOffset,
					register,
					addressRegister: true,
					dataflow))
			{
				return true;
			}
		}
		return false;
	}

	private bool TryFoldDataToAddressRegisterMove(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var moveData = instructions[index];
			var moveAddress = instructions[index + 1];
			if ((moveData.Opcode & 0xF000) != 0x2000 ||
				((moveData.Opcode >> 6) & 7) != 0 ||
				moveAddress.Length != 2 ||
				(moveAddress.Opcode & 0xF1F8) != 0x2040 ||
				IsReferencedLabelAt(moveAddress.Offset) ||
				!dataflow.TryGetFacts(moveData.Offset, out var moveFacts) ||
				!dataflow.TryGetFacts(moveAddress.Offset, out _))
			{
				continue;
			}

			var temporary = (moveData.Opcode >> 9) & 7;
			if ((moveAddress.Opcode & 7) != temporary ||
				(moveFacts.LiveConditionsAfter != M68kConditionCodeSet.None) ||
				!dataflow.TryGetFacts(moveAddress.Offset, out var addressFacts) ||
				(addressFacts.LiveDataAfter & (1 << temporary)) != 0)
			{
				continue;
			}

			var destination = (moveAddress.Opcode >> 9) & 7;
			var replacement = (ushort)(
				(moveData.Opcode & 0xF03F) |
				(destination << 9) |
				0x0040);
			_buffer.WriteWord(moveData.Offset, replacement);
			_buffer.RemoveBytes(moveAddress.Offset, moveAddress.Length);
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
			if ((moveAddress.Opcode & 0xF1C0) != 0x2040 ||
				(moveData.Opcode & 0xF1F8) != 0x2008 ||
				moveData.Length != 2 ||
				IsReferencedLabelAt(moveData.Offset))
			{
				continue;
			}

			var addressRegister = (moveAddress.Opcode >> 9) & 7;
			var destinationDataRegister = (moveData.Opcode >> 9) & 7;
			if (addressRegister == 7 ||
				(moveData.Opcode & 7) != addressRegister ||
				!dataflow.TryGetFacts(moveData.Offset, out var facts) ||
				(facts.LiveAddressAfter & (1 << addressRegister)) != 0)
			{
				continue;
			}

			_buffer.WriteWord(
				moveAddress.Offset,
				(ushort)((moveAddress.Opcode & 0xF03F) |
					(destinationDataRegister << 9)));
			_buffer.RemoveBytes(moveData.Offset, moveData.Length);
			return true;
		}

		return false;
	}

	private bool TryFoldAddressRegisterMemoryTransfer(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var load = instructions[index];
			var store = instructions[index + 1];
			if ((load.Opcode & 0xF1C0) != 0x2040 || // MOVEA.L <ea>,An
				(store.Opcode & 0xF038) != 0x2008 || // MOVE.L An,<ea>
				!load.IsDecoded ||
				!store.IsDecoded ||
				_buffer.HasLabelAt(store.Offset) ||
				!dataflow.TryGetFacts(load.Offset, out var loadFacts) ||
				!dataflow.TryGetFacts(store.Offset, out var storeFacts))
			{
				continue;
			}

			var temporary = load.Opcode >> 9 & 7;
			var destinationMode = store.Opcode >> 6 & 7;
			var destinationRegister = store.Opcode >> 9 & 7;
			var temporaryMask = (ushort)(1 << temporary);
			if (temporary == 7 ||
				(store.Opcode & 7) != temporary ||
				!IsAlterableMemoryDestination(
					destinationMode,
					destinationRegister) ||
				(loadFacts.Effects.UsesAddress & temporaryMask) != 0 ||
				DestinationUsesAddressRegister(
					store,
					destinationMode,
					destinationRegister,
					temporary) ||
				(storeFacts.LiveAddressAfter & temporaryMask) != 0)
			{
				continue;
			}

			// MOVE evaluates its source before its destination. Removing the dead
			// address temporary therefore preserves both memory effects and the
			// N/Z/V/C result of the original store. Dropping only the second opcode
			// leaves its destination extension words after the source extensions.
			var replacement = (ushort)(
				0x2000 |
				(store.Opcode & 0x0FC0) |
				(load.Opcode & 0x003F));
			_buffer.WriteWord(load.Offset, replacement);
			_buffer.RemoveBytes(store.Offset, 2);
			return true;
		}

		return false;
	}

	private static bool IsAlterableMemoryDestination(int mode, int register) =>
		mode is >= 2 and <= 6 ||
		mode == 7 && register is 0 or 1;

	private static bool DestinationUsesAddressRegister(
		M68kEmittedInstruction instruction,
		int mode,
		int baseRegister,
		int register)
	{
		if (mode is >= 2 and <= 6 && baseRegister == register)
		{
			return true;
		}
		return mode == 6 &&
			(instruction.ExtensionWord & 0x8000) != 0 &&
			((instruction.ExtensionWord >> 12) & 7) == register;
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

	private bool TryRemoveRedundantKnownMoveQuick(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xF100) != 0x7000 ||
				instruction.Length != 2 ||
				IsReferencedLabelAt(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				facts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			var register = (instruction.Opcode >> 9) & 7;
			var immediate = unchecked((uint)(int)(sbyte)(instruction.Opcode & 0xFF));
			if (!dataflow.GetDataValueBefore(instruction.Offset, register)
				.IsExact(out var value) ||
				value != immediate)
			{
				continue;
			}

			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveDeadMoveQuick(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xF100) != 0x7000 ||
				instruction.Length != 2 ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				continue;
			}

			var register = (instruction.Opcode >> 9) & 7;
			if ((facts.LiveDataAfter & (1 << register)) != 0 ||
				(facts.Effects.WritesConditions & facts.LiveConditionsAfter) != 0 ||
				HasInternalLabel(instruction))
			{
				continue;
			}

			// Labels exactly at the instruction remain at the same byte offset;
			// after removal they naturally name the following instruction.
			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			return true;
		}

		return false;
	}

	private bool TryFoldMoveQuickIntoQuickArithmetic(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var moveQuick = instructions[index];
			var arithmetic = instructions[index + 1];
			var arithmeticOperation = arithmetic.Opcode & 0xF1F8;
			if ((moveQuick.Opcode & 0xF100) != 0x7000 ||
				moveQuick.Length != 2 ||
				(arithmeticOperation != 0xD080 && arithmeticOperation != 0x9080) ||
				arithmetic.Length != 2 ||
				// The replacement stays at MOVEQ's offset, so a block-entry label
				// remains valid.  A label on the removed arithmetic must be preserved.
				IsReferencedLabelAt(arithmetic.Offset))
			{
				continue;
			}

			var immediate = unchecked((sbyte)(moveQuick.Opcode & 0xFF));
			var sourceRegister = (moveQuick.Opcode >> 9) & 7;
			var destinationRegister = (arithmetic.Opcode >> 9) & 7;
			if (immediate is < 1 or > 8 ||
				sourceRegister == destinationRegister ||
				(arithmetic.Opcode & 7) != sourceRegister ||
				!dataflow.TryGetFacts(arithmetic.Offset, out var facts) ||
				(facts.LiveDataAfter & (1 << sourceRegister)) != 0)
			{
				continue;
			}

			var isSubtraction = arithmeticOperation == 0x9080;
			var encodedImmediate = immediate == 8 ? 0 : immediate;
			_buffer.WriteWord(
				moveQuick.Offset,
				(ushort)((isSubtraction ? 0x5180 : 0x5080) |
					(encodedImmediate << 9) |
					destinationRegister)); // ADDQ/SUBQ.L #n,Dn
			_buffer.RemoveBytes(arithmetic.Offset, arithmetic.Length);
			return true;
		}

		return false;
	}

	private bool TryFuseOwnedMemoryReadModifyWrite(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var loadIndex = 0; loadIndex + 2 < instructions.Count; loadIndex++)
		{
			var load = instructions[loadIndex];
			var arithmeticIndex = loadIndex + 1;
			M68kEmittedInstruction? middle = null;
			if (!TryGetReadModifyWriteArithmetic(
					instructions[arithmeticIndex],
					out _,
					out _,
					out _) &&
				!TryGetQuickReadModifyWriteArithmetic(
					instructions[arithmeticIndex],
					out _,
					out _,
					out _))
			{
				if (loadIndex + 3 >= instructions.Count ||
					(instructions[arithmeticIndex].Opcode & 0xF100) != 0x7000 ||
					instructions[arithmeticIndex].Length != 2 ||
					IsReferencedLabelAt(instructions[arithmeticIndex].Offset))
				{
					continue;
				}
				middle = instructions[arithmeticIndex++];
			}

			var arithmetic = instructions[arithmeticIndex];
			var store = instructions[arithmeticIndex + 1];
			var isQuickArithmetic = TryGetQuickReadModifyWriteArithmetic(
				arithmetic,
				out var immediateSubtract,
				out var immediateCount,
				out var quickResultRegister);
			var isRegisterArithmetic = TryGetReadModifyWriteArithmetic(
					arithmetic,
					out var registerSubtract,
					out var arithmeticSource,
					out var registerResultRegister);
			var resultRegister = isQuickArithmetic
				? quickResultRegister
				: registerResultRegister;
			if ((!isQuickArithmetic && !isRegisterArithmetic) ||
				!TryGetOwnedExactLongMovePair(
					load,
					store,
					out var loadedRegister,
					out var destinationEa) ||
				(store.Opcode & 7) != resultRegister ||
				IsReferencedLabelAt(arithmetic.Offset) ||
				IsReferencedLabelAt(store.Offset) ||
				!dataflow.TryGetFacts(store.Offset, out var storeFacts))
			{
				continue;
			}
			var subtract = isQuickArithmetic ? immediateSubtract : registerSubtract;

			int deltaRegister;
			if (isQuickArithmetic)
			{
				if (loadedRegister != resultRegister)
				{
					continue;
				}
				deltaRegister = -1;
			}
			else if (loadedRegister == resultRegister)
			{
				deltaRegister = arithmeticSource;
			}
			else if (!subtract && loadedRegister == arithmeticSource)
			{
				deltaRegister = resultRegister;
			}
			else
			{
				continue;
			}
			var loadedRegisterIsLive =
				(storeFacts.LiveDataAfter & (1 << loadedRegister)) != 0;
			if ((!isQuickArithmetic && deltaRegister == loadedRegister) ||
				(storeFacts.LiveDataAfter & (1 << resultRegister)) != 0 ||
				loadedRegister == resultRegister && loadedRegisterIsLive ||
				(storeFacts.LiveConditionsAfter &
				 (M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry)) != 0)
			{
				continue;
			}

			ushort replacement;
			var usesQuickEncoding = false;
			if (isQuickArithmetic)
			{
				usesQuickEncoding = true;
				var encodedCount = immediateCount == 8 ? 0 : immediateCount;
				replacement = (ushort)(
					(subtract ? 0x5180 : 0x5080) |
					(encodedCount << 9) |
					destinationEa);
			}
			else if (dataflow.GetDataValueBefore(
					arithmetic.Offset,
					deltaRegister).IsExact(out var exactValue) &&
				TryNormalizeQuickMemoryArithmetic(
					exactValue,
					subtract,
					out var quickSubtract,
					out var quickCount))
			{
				usesQuickEncoding = true;
				var encodedCount = quickCount == 8 ? 0 : quickCount;
				replacement = (ushort)(
					(quickSubtract ? 0x5180 : 0x5080) |
					(encodedCount << 9) |
					destinationEa);
			}
			else
			{
				replacement = (ushort)(
					(subtract ? 0x9180 : 0xD180) |
					(deltaRegister << 9) |
					destinationEa);
			}

			_buffer.WriteWord(store.Offset, replacement);
			_buffer.RemoveBytes(arithmetic.Offset, arithmetic.Length);
			if (usesQuickEncoding &&
				middle is { } removableMiddle &&
				deltaRegister >= 0 &&
				(storeFacts.LiveDataAfter & (1 << deltaRegister)) == 0)
			{
				_buffer.RemoveBytes(removableMiddle.Offset, removableMiddle.Length);
			}
			if (!loadedRegisterIsLive)
			{
				_buffer.RemoveBytes(load.Offset, load.Length);
			}
			return true;
		}

		return false;
	}

	private static bool TryGetQuickReadModifyWriteArithmetic(
		M68kEmittedInstruction instruction,
		out bool subtract,
		out int count,
		out int resultRegister)
	{
		var operation = instruction.Opcode & 0xF1F8;
		subtract = operation == 0x5180;
		count = (instruction.Opcode >> 9) & 7;
		count = count == 0 ? 8 : count;
		resultRegister = instruction.Opcode & 7;
		return instruction.Length == 2 &&
			(operation == 0x5080 || subtract);
	}

	private static bool TryGetReadModifyWriteArithmetic(
		M68kEmittedInstruction instruction,
		out bool subtract,
		out int sourceRegister,
		out int destinationRegister)
	{
		var operation = instruction.Opcode & 0xF1F8;
		subtract = operation == 0x9080;
		sourceRegister = instruction.Opcode & 7;
		destinationRegister = (instruction.Opcode >> 9) & 7;
		return instruction.Length == 2 &&
			(operation == 0xD080 || subtract);
	}

	private bool TryGetOwnedExactLongMovePair(
		M68kEmittedInstruction load,
		M68kEmittedInstruction store,
		out int loadedRegister,
		out int destinationEa)
	{
		loadedRegister = (load.Opcode >> 9) & 7;
		var loadMode = (load.Opcode >> 3) & 7;
		var loadRegister = load.Opcode & 7;
		var storeMode = (store.Opcode >> 6) & 7;
		var storeRegister = (store.Opcode >> 9) & 7;
		destinationEa = (storeMode << 3) | storeRegister;
		if ((load.Opcode & 0xF1C0) != 0x2000 ||
			(store.Opcode & 0xF000) != 0x2000 ||
			((store.Opcode >> 3) & 7) != 0 ||
			!IsAlterableMemoryDestination(storeMode, storeRegister))
		{
			return false;
		}

		if (loadMode == 7 && loadRegister is 1 or 2 &&
			storeMode == 7 && storeRegister == 1)
		{
			var loadTarget = loadRegister == 2
				? _buffer.PcRelative.FirstOrDefault(fixup =>
					fixup.DisplacementOffset == load.Offset + 2).Target
				: _buffer.Addresses.FirstOrDefault(fixup =>
					fixup.Offset == load.Offset + 2 && !fixup.External).Target;
			var storeTarget = _buffer.Addresses.FirstOrDefault(fixup =>
				fixup.Offset == store.Offset + 2 && !fixup.External).Target;
			return loadTarget is not null &&
				string.Equals(loadTarget, storeTarget, StringComparison.Ordinal);
		}

		return loadMode == storeMode &&
			loadRegister == storeRegister &&
			loadRegister is 5 or 7 &&
			loadMode is 2 or 5 or 6 &&
			load.Length == store.Length &&
			(loadMode == 2 || load.ExtensionWord == store.ExtensionWord);
	}

	private static bool TryNormalizeQuickMemoryArithmetic(
		uint value,
		bool subtract,
		out bool normalizedSubtract,
		out int count)
	{
		var signed = unchecked((int)value);
		normalizedSubtract = subtract;
		if (signed is >= 1 and <= 8)
		{
			count = signed;
			return true;
		}
		if (signed is >= -8 and <= -1)
		{
			count = -signed;
			normalizedSubtract = !subtract;
			return true;
		}

		count = 0;
		return false;
	}

	private bool TryForwardLongImmediateThroughRegisterMove(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var immediate = instructions[index];
			var move = instructions[index + 1];
			if ((immediate.Opcode & 0xF1FF) != 0x203C ||
				immediate.Length != 6 ||
				move.Length != 2 ||
				IsReferencedLabelAt(immediate.Offset) ||
				IsReferencedLabelAt(move.Offset))
			{
				continue;
			}

			var sourceRegister = immediate.Opcode >> 9 & 7;
			var movesToData = (move.Opcode & 0xF1F8) == 0x2000;
			var movesToAddress = (move.Opcode & 0xF1F8) == 0x2040;
			if ((!movesToData && !movesToAddress) ||
				(move.Opcode & 7) != sourceRegister ||
				!dataflow.TryGetFacts(move.Offset, out var facts) ||
				(facts.LiveDataAfter & (1 << sourceRegister)) != 0)
			{
				continue;
			}

			var destinationRegister = move.Opcode >> 9 & 7;
			_buffer.WriteWord(
				immediate.Offset,
				(ushort)(
					(movesToAddress ? 0x207C : 0x203C) |
					(destinationRegister << 9)));
			MoveLabelsToOffset(
				move.Offset,
				move.Offset + move.Length,
				immediate.Offset);
			_buffer.RemoveBytes(move.Offset, move.Length);
			return true;
		}

		return false;
	}

	private bool TryForwardMemoryLoadIntoArithmetic(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var load = instructions[index];
			var arithmetic = instructions[index + 1];
			var sourceMode = (load.Opcode >> 3) & 7;
			var arithmeticOperation = arithmetic.Opcode & 0xF000;
			if ((load.Opcode & 0xF000) != 0x2000 ||
				((load.Opcode >> 6) & 7) != 0 ||
				!load.IsDecoded ||
				load.Length is not (2 or 4 or 6) ||
				sourceMode < 2 ||
				sourceMode == 7 && (load.Opcode & 7) == 4 ||
				!arithmetic.IsDecoded ||
				arithmetic.Length != 2 ||
				arithmeticOperation is not (0x9000 or 0xB000 or 0xD000) ||
				((arithmetic.Opcode >> 6) & 7) != 2 ||
				IsReferencedLabelAt(arithmetic.Offset) ||
				!dataflow.TryGetFacts(arithmetic.Offset, out _))
			{
				continue;
			}

			var temporary = (load.Opcode >> 9) & 7;
			var destination = (arithmetic.Opcode >> 9) & 7;
			if ((arithmetic.Opcode & 7) != temporary ||
				destination == temporary ||
				!dataflow.TryGetFacts(arithmetic.Offset, out var facts) ||
				(facts.LiveDataAfter & (1 << temporary)) != 0)
			{
				continue;
			}

			var replacement = (ushort)(
				(arithmetic.Opcode & 0xFFC0) |
				(load.Opcode & 0x003F));
			_buffer.WriteWord(load.Offset, replacement);
			if (load.Length >= 4)
			{
				_buffer.WriteWord(load.Offset + 2, load.ExtensionWord);
			}
			if (load.Length >= 6)
			{
				_buffer.WriteWord(load.Offset + 4, (ushort)load.ExtensionLong);
			}
			_buffer.RemoveBytes(arithmetic.Offset, arithmetic.Length);
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
			var pushesRegister = (push.Opcode & 0xFFF8) == 0x2F00 &&
				push.Length == 2;
			var pushesImmediate = push.Opcode == 0x2F3C &&
				push.Length == 6;
			if ((!pushesRegister && !pushesImmediate) ||
				(reload.Opcode & 0xF1FF) != 0x2017 ||
				reload.Length != 2 ||
				(pushesRegister &&
					(push.Opcode & 7) != ((reload.Opcode >> 9) & 7)) ||
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
			if (pushesImmediate)
			{
				var destinationRegister = (reload.Opcode >> 9) & 7;
				_buffer.WriteWord(
					push.Offset,
					(ushort)(0x203C | (destinationRegister << 9))); // MOVE.L #value,Dn
			}
			else
			{
				_buffer.RemoveBytes(push.Offset, push.Length);
			}
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

			// MC68040 issues one integer instruction per clock, so the compact
			// three-instruction final-store form below is faster there. Keep the
			// register-preserving alternative for the dual-pipeline MC68060.
			if (_cpu == M68kCpuTarget.M68060 &&
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
		// On MC68000, CLR.L -(A7) and MOVEQ+MOVE.L take the same total
		// cycles, but CLR performs a read-before-write bus cycle.  This is a
		// size optimization only on that CPU, not a speed optimization.
		if (_cpu == M68kCpuTarget.M68000)
		{
			return false;
		}

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

	private bool TryMaterializeZeroExtendedByteCopy(
		M68kInstructionDataflow dataflow)
	{
		const M68kConditionCodeSet logicalConditions =
			M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow |
			M68kConditionCodeSet.Carry;
		var instructions = dataflow.Instructions;
		for (var copyIndex = 0; copyIndex + 1 < instructions.Count; copyIndex++)
		{
			var copy = instructions[copyIndex];
			if ((copy.Opcode & 0xF1F8) != 0x2000 ||
				copy.Length != 2 ||
				!dataflow.TryGetFacts(copy.Offset, out var copyFacts) ||
				copyFacts.LiveConditionsAfter != M68kConditionCodeSet.None)
			{
				continue;
			}

			var source = copy.Opcode & 7;
			var destination = (copy.Opcode >> 9) & 7;
			if (source == destination)
			{
				continue;
			}

			var destinationMask = (ushort)(1 << destination);
			for (var index = copyIndex + 1; index < instructions.Count; index++)
			{
				var instruction = instructions[index];
				if (IsReferencedLabelAt(instruction.Offset) ||
					!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
					facts.Effects.IsBarrier)
				{
					break;
				}

				if (instruction.Opcode == (ushort)(0x0280 | destination) &&
					instruction.Length == 6 &&
					instruction.ExtensionLong == 0x000000FF &&
					(facts.LiveConditionsAfter & logicalConditions) == 0)
				{
					var redundantMasks = new List<M68kEmittedInstruction> { instruction };
					for (var laterIndex = index + 1;
						laterIndex < instructions.Count;
						laterIndex++)
					{
						var later = instructions[laterIndex];
						if (IsReferencedLabelAt(later.Offset) ||
							!dataflow.TryGetFacts(later.Offset, out var laterFacts) ||
							laterFacts.Effects.IsBarrier)
						{
							break;
						}
						if (later.Opcode == instruction.Opcode &&
							later.Length == instruction.Length &&
							later.ExtensionLong == instruction.ExtensionLong &&
							(laterFacts.LiveConditionsAfter & logicalConditions) == 0)
						{
							redundantMasks.Add(later);
							continue;
						}
						if ((laterFacts.Effects.DefinesData & destinationMask) != 0)
						{
							break;
						}
					}

					foreach (var redundantMask in redundantMasks.AsEnumerable().Reverse())
					{
						_buffer.RemoveBytes(redundantMask.Offset, redundantMask.Length);
					}
					_buffer.InsertBytes(copy.Offset, 2);
					_buffer.WriteWord(
						copy.Offset,
						0x7000 | (destination << 9)); // MOVEQ #0,Dd
					_buffer.WriteWord(
						copy.Offset + 2,
						0x1000 | (destination << 9) | source); // MOVE.B Ds,Dd
					return true;
				}

				if ((facts.Effects.DefinesData & destinationMask) != 0 ||
					(facts.Effects.UsesData & destinationMask) != 0 &&
					!ObservesOnlyLowByte(instruction, destination))
				{
					break;
				}
			}
		}

		return false;
	}

	private bool TryMaterializeZeroExtendedWordCopy(
		M68kInstructionDataflow dataflow)
	{
		const M68kConditionCodeSet logicalConditions =
			M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow |
			M68kConditionCodeSet.Carry;
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var copy = instructions[index];
			var normalization = instructions[index + 1];
			if ((copy.Opcode & 0xF1F8) != 0x2000 ||
				copy.Length != 2 ||
				(copy.Opcode & 7) == ((copy.Opcode >> 9) & 7) ||
				IsReferencedLabelAt(normalization.Offset) ||
				(normalization.Opcode & 0xFFF8) != 0x0280 ||
				normalization.Length != 6 ||
				_buffer.ReadLong(normalization.Offset + 2) != 0x0000FFFF ||
				(normalization.Opcode & 7) != ((copy.Opcode >> 9) & 7) ||
				!dataflow.TryGetFacts(normalization.Offset, out var facts) ||
				(facts.LiveConditionsAfter & logicalConditions) != 0)
			{
				continue;
			}

			var source = copy.Opcode & 7;
			var destination = (copy.Opcode >> 9) & 7;
			_buffer.WriteWord(
				copy.Offset,
				0x7000 | (destination << 9)); // MOVEQ #0,Dd
			_buffer.WriteWord(
				normalization.Offset,
				0x3000 | (destination << 9) | source); // MOVE.W Ds,Dd
			_buffer.RemoveBytes(normalization.Offset + 2, 4);
			return true;
		}

		return false;
	}

	private bool TryMaterializeZeroExtendedPartialLoad(
		M68kInstructionDataflow dataflow)
	{
		const M68kConditionCodeSet logicalConditions =
			M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow |
			M68kConditionCodeSet.Carry;
		var instructions = dataflow.Instructions;
		for (var loadIndex = 0; loadIndex + 1 < instructions.Count; loadIndex++)
		{
			var load = instructions[loadIndex];
			var sizeFamily = load.Opcode & 0xF000;
			if (sizeFamily is not (0x1000 or 0x3000) ||
				((load.Opcode >> 6) & 7) != 0)
			{
				continue;
			}

			var destination = (load.Opcode >> 9) & 7;
			var sourceMode = (load.Opcode >> 3) & 7;
			var sourceRegister = load.Opcode & 7;
			if (sourceMode == 0 && sourceRegister == destination ||
				sourceMode == 6 ||
				sourceMode == 7 && sourceRegister == 3)
			{
				// Clearing the destination first would change a self-copy or an
				// indexed effective address that reads the same data register.
				continue;
			}

			var destinationMask = (ushort)(1 << destination);
			var normalizationMask = sizeFamily == 0x1000
				? 0x000000FFu
				: 0x0000FFFFu;
			for (var index = loadIndex + 1; index < instructions.Count; index++)
			{
				var instruction = instructions[index];
				if (IsReferencedLabelAt(instruction.Offset) ||
					!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
					facts.Effects.IsBarrier)
				{
					break;
				}

				if (instruction.Opcode == (ushort)(0x0280 | destination) &&
					instruction.Length == 6 &&
					instruction.ExtensionLong == normalizationMask &&
					(facts.LiveConditionsAfter & logicalConditions) == 0)
				{
					// MOVEQ plus a partial MOVE is smaller and faster than retaining
					// the original upper bits and clearing them with ANDI.L later.
					_buffer.RemoveBytes(instruction.Offset, instruction.Length);
					_buffer.InsertBytes(load.Offset, 2);
					_buffer.WriteWord(load.Offset, 0x7000 | (destination << 9));
					return true;
				}

				if ((facts.Effects.DefinesData & destinationMask) != 0 ||
					(facts.Effects.UsesData & destinationMask) != 0)
				{
					break;
				}
			}
		}

		return false;
	}

	private static bool ObservesOnlyLowByte(
		M68kEmittedInstruction instruction,
		int register)
	{
		var opcode = instruction.Opcode;
		if ((opcode & 0xF000) == 0x1000 &&
			((opcode >> 3) & 7) == 0 &&
			(opcode & 7) == register)
		{
			return true; // MOVE.B Dn,<ea>
		}

		if ((opcode & 0xF000) is 0x8000 or 0x9000 or 0xB000 or 0xC000 or 0xD000 &&
			(opcode & 0x00C0) == 0)
		{
			return (((opcode >> 3) & 7) == 0 && (opcode & 7) == register) ||
				((opcode >> 9) & 7) == register;
		}

		return (opcode & 0xFFF8) == 0x4A00 && (opcode & 7) == register; // TST.B Dn
	}

	private bool TryRemoveByteMasksBeforeNormalizedAdd()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 1; index + 1 < instructions.Count; index++)
		{
			var add = instructions[index];
			if (!TryGetByteDataAdd(add, out var sourceRegister, out var destinationRegister) ||
				!IsByteNormalization(instructions[index + 1], destinationRegister))
			{
				continue;
			}

			var firstIndex = index;
			while (firstIndex > 0 &&
				TryGetByteNormalizationRegister(
					instructions[firstIndex - 1],
					out var normalizedRegister) &&
				(normalizedRegister == sourceRegister ||
				 normalizedRegister == destinationRegister))
			{
				firstIndex--;
			}
			if (firstIndex == index)
			{
				continue;
			}

			var firstMask = instructions[firstIndex];
			// ADD.B observes only the low bytes, and the following mask establishes
			// the complete zero-extended result. Any input masks are therefore dead.
			MoveLabelsToOffset(
				firstMask.Offset,
				add.Offset + add.Length,
				firstMask.Offset);
			_buffer.RemoveBytes(firstMask.Offset, add.Offset - firstMask.Offset);
			return true;
		}

		return false;
	}

	private bool TryRemoveDeadByteNormalizationBeforeFrameStore(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var mask = instructions[index];
			var store = instructions[index + 1];
			if (!TryGetByteNormalizationRegister(mask, out var register) ||
				!TryGetByteFrameStore(store, out var storedRegister) ||
				storedRegister != register ||
				!dataflow.TryGetFacts(store.Offset, out var facts) ||
				(facts.LiveDataAfter & (1 << register)) != 0 &&
				!IsReplacedByNormalizedWord(
					instructions,
					index + 2,
					register,
					dataflow))
			{
				continue;
			}

			// The mask leaves the stored low byte unchanged, and MOVE.B replaces
			// all condition codes written by it. With the register dead afterwards,
			// its zero-extended upper bits are unobservable.
			_buffer.RemoveBytes(mask.Offset, mask.Length);
			return true;
		}

		return false;
	}

	private bool IsReplacedByNormalizedWord(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int index,
		int register,
		M68kInstructionDataflow dataflow)
	{
		if (index + 1 >= instructions.Count)
		{
			return false;
		}

		var move = instructions[index];
		var normalization = instructions[index + 1];
		return (move.Opcode & 0xF000) == 0x3000 && // MOVE.W <ea>,Dn
			((move.Opcode >> 6) & 7) == 0 &&
			((move.Opcode >> 9) & 7) == register &&
			((move.Opcode >> 3) & 7) == 0 && // Keep the proof to MOVE.W Dm,Dn.
			dataflow.TryGetFacts(move.Offset, out _) &&
			(normalization.Opcode & 0xFFF8) == 0x0280 &&
			(normalization.Opcode & 7) == register &&
			normalization.Length == 6 &&
			_buffer.ReadLong(normalization.Offset + 2) == 0x0000FFFF;
	}

	private bool IsByteNormalization(
		M68kEmittedInstruction instruction,
		int register) =>
		TryGetByteNormalizationRegister(instruction, out var normalizedRegister) &&
		normalizedRegister == register;

	private bool TryGetByteNormalizationRegister(
		M68kEmittedInstruction instruction,
		out int register)
	{
		register = instruction.Opcode & 7;
		return (instruction.Opcode & 0xFFF8) == 0x0280 &&
			instruction.Length == 6 &&
			_buffer.ReadLong(instruction.Offset + 2) == 0x000000FF;
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

	private bool TryFoldNormalizedWordRotateAdd(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		if (TryFoldDeferredWordRotateAdd(dataflow, instructions))
		{
			return true;
		}

		const int unmaskedPatternLength = 12;
		for (var index = 0; index + unmaskedPatternLength <= instructions.Count; index++)
		{
			var firstMove = instructions[index];
			var firstMask = instructions[index + 1];
			var leftShift = instructions[index + 2];
			var save = instructions[index + 3];
			var secondMove = instructions[index + 4];
			var secondMask = instructions[index + 5];
			var rightShiftEight = instructions[index + 6];
			var rightShiftThree = instructions[index + 7];
			var combine = instructions[index + 8];
			var possibleByteMask = instructions[index + 9];
			var hasByteMask =
				TryGetByteNormalizationRegister(possibleByteMask, out var normalizedByteRegister) &&
				possibleByteMask.Length == 6 &&
				_buffer.ReadLong(possibleByteMask.Offset + 2) == 0x000000FF;
			var addIndex = index + (hasByteMask ? 10 : 9);
			var patternLength = hasByteMask ? 13 : unmaskedPatternLength;
			if (index + patternLength > instructions.Count)
			{
				continue;
			}
			var add = instructions[addIndex];
			var copyBack = instructions[addIndex + 1];
			var resultMask = instructions[addIndex + 2];

			if ((firstMove.Opcode & 0xF1F8) != 0x3000 || // MOVE.W Ds,Dt
				firstMove.Length != 2 ||
				(secondMove.Opcode & 0xF1F8) != 0x3000 ||
				secondMove.Length != 2)
			{
				continue;
			}

			var source = firstMove.Opcode & 7;
			var temporary = (firstMove.Opcode >> 9) & 7;
			var savedLeft = (save.Opcode >> 9) & 7;
			var byteValue = add.Opcode & 7;
			if (source == temporary ||
				source == savedLeft ||
				source == byteValue ||
				hasByteMask && normalizedByteRegister != byteValue ||
				(firstMask.Opcode & 0xFFF8) != 0x0280 ||
				(firstMask.Opcode & 7) != temporary ||
				firstMask.Length != 6 ||
				_buffer.ReadLong(firstMask.Offset + 2) != 0x0000FFFF ||
				(leftShift.Opcode & 0xF1F8) != 0xE188 ||
				(leftShift.Opcode & 7) != temporary ||
				QuickCount(leftShift.Opcode) != 5 ||
				(save.Opcode & 0xF1F8) != 0x2000 ||
				(save.Opcode & 7) != temporary ||
				save.Length != 2 ||
				secondMove.Opcode != firstMove.Opcode ||
				secondMask.Opcode != firstMask.Opcode ||
				secondMask.Length != 6 ||
				_buffer.ReadLong(secondMask.Offset + 2) != 0x0000FFFF ||
				(rightShiftEight.Opcode & 0xF1F8) != 0xE080 ||
				(rightShiftEight.Opcode & 7) != temporary ||
				QuickCount(rightShiftEight.Opcode) != 8 ||
				(rightShiftThree.Opcode & 0xF1F8) != 0xE080 ||
				(rightShiftThree.Opcode & 7) != temporary ||
				QuickCount(rightShiftThree.Opcode) != 3 ||
				(combine.Opcode & 0xF1F8) != 0x8080 ||
				(combine.Opcode & 7) != savedLeft ||
				((combine.Opcode >> 9) & 7) != temporary ||
				(add.Opcode & 0xF1F8) != 0xD080 ||
				(add.Opcode & 7) != byteValue ||
				((add.Opcode >> 9) & 7) != temporary ||
				(copyBack.Opcode & 0xF1F8) != 0x2000 ||
				(copyBack.Opcode & 7) != temporary ||
				((copyBack.Opcode >> 9) & 7) != source ||
				(resultMask.Opcode & 0xFFF8) != 0x0280 ||
				(resultMask.Opcode & 7) != source ||
				resultMask.Length != 6 ||
				_buffer.ReadLong(resultMask.Offset + 2) != 0x0000FFFF ||
				IsDataRegisterReadBeforeOverwrite(
					instructions,
					index + patternLength,
					temporary,
					dataflow) ||
				IsDataRegisterReadBeforeOverwrite(
					instructions,
					index + patternLength,
					savedLeft,
					dataflow))
			{
				continue;
			}

			var endOffset = resultMask.Offset + resultMask.Length;
			if (instructions
				.Skip(index + 1)
				.Take(patternLength - 1)
				.Any(instruction => IsReferencedLabelAt(instruction.Offset)))
			{
				continue;
			}

			var offset = firstMove.Offset;
			MoveLabelsToOffset(offset + firstMove.Length, endOffset, offset);
			_buffer.WriteWord(offset, 0xE158 | (5 << 9) | source); // ROL.W #5,Ds
			var replacementLength = 10;
			var addOffset = 2;
			if (hasByteMask)
			{
				_buffer.WriteWord(offset + 2, 0x0280 | byteValue); // ANDI.L #$FF,Db
				_buffer.WriteWord(offset + 4, 0);
				_buffer.WriteWord(offset + 6, 0x00FF);
				addOffset = 8;
				replacementLength = 16;
			}
			_buffer.WriteWord(offset + addOffset, 0xD040 | (source << 9) | byteValue); // ADD.W Db,Ds
			_buffer.WriteWord(offset + addOffset + 2, 0x0280 | source); // ANDI.L #$FFFF,Ds
			_buffer.WriteWord(offset + addOffset + 4, 0);
			_buffer.WriteWord(offset + addOffset + 6, 0xFFFF);
			_buffer.RemoveBytes(
				offset + replacementLength,
				endOffset - offset - replacementLength);
			return true;
		}

		return false;
	}

	private bool TryFoldDeferredWordRotateAdd(
		M68kInstructionDataflow dataflow,
		IReadOnlyList<M68kEmittedInstruction> instructions)
	{
		const int unmaskedPatternLength = 9;
		for (var index = 0; index + unmaskedPatternLength <= instructions.Count; index++)
		{
			var firstMove = instructions[index];
			var leftShift = instructions[index + 1];
			var save = instructions[index + 2];
			var secondMove = instructions[index + 3];
			var rightShiftEight = instructions[index + 4];
			var rightShiftThree = instructions[index + 5];
			var combine = instructions[index + 6];
			var possibleByteMask = instructions[index + 7];
			var hasByteMask =
				TryGetByteNormalizationRegister(possibleByteMask, out var normalizedByteRegister) &&
				possibleByteMask.Length == 6 &&
				_buffer.ReadLong(possibleByteMask.Offset + 2) == 0x000000FF;
			var addIndex = index + (hasByteMask ? 8 : 7);
			var copyBackIndex = addIndex + 1;
			if (copyBackIndex >= instructions.Count)
			{
				continue;
			}

			var add = instructions[addIndex];
			var copyBack = instructions[copyBackIndex];
			var source = firstMove.Opcode & 7;
			var temporary = (firstMove.Opcode >> 9) & 7;
			var savedLeft = (save.Opcode >> 9) & 7;
			var byteValue = add.Opcode & 7;
			var result = (copyBack.Opcode >> 9) & 7;
			var resultMaskIndex = copyBackIndex + 1;
			var hasResultMask = resultMaskIndex < instructions.Count &&
				(instructions[resultMaskIndex].Opcode & 0xFFF8) == 0x0280 &&
				(instructions[resultMaskIndex].Opcode & 7) == result &&
				instructions[resultMaskIndex].Length == 6 &&
				_buffer.ReadLong(instructions[resultMaskIndex].Offset + 2) == 0x0000FFFF;
			var patternLength = resultMaskIndex - index + (hasResultMask ? 1 : 0);
			var lastInstruction = hasResultMask
				? instructions[resultMaskIndex]
				: copyBack;

			if ((firstMove.Opcode & 0xF1F8) != 0x3000 || // MOVE.W Ds,Dt
				firstMove.Length != 2 ||
				source == temporary ||
				source == savedLeft ||
				source == byteValue ||
				temporary == savedLeft ||
				temporary == byteValue ||
				savedLeft == byteValue ||
				hasByteMask && normalizedByteRegister != byteValue ||
				(leftShift.Opcode & 0xF1F8) != 0xE148 || // LSL.W #5,Dt
				(leftShift.Opcode & 7) != temporary ||
				QuickCount(leftShift.Opcode) != 5 ||
				(save.Opcode & 0xF1F8) != 0x3000 || // MOVE.W Dt,Dl
				(save.Opcode & 7) != temporary ||
				save.Length != 2 ||
				secondMove.Opcode != firstMove.Opcode ||
				(rightShiftEight.Opcode & 0xF1F8) != 0xE048 || // LSR.W #8,Dt
				(rightShiftEight.Opcode & 7) != temporary ||
				QuickCount(rightShiftEight.Opcode) != 8 ||
				(rightShiftThree.Opcode & 0xF1F8) != 0xE048 ||
				(rightShiftThree.Opcode & 7) != temporary ||
				QuickCount(rightShiftThree.Opcode) != 3 ||
				(combine.Opcode & 0xF1F8) != 0x8040 || // OR.W Dl,Dt
				(combine.Opcode & 7) != savedLeft ||
				((combine.Opcode >> 9) & 7) != temporary ||
				(add.Opcode & 0xF1F8) != 0xD040 || // ADD.W Db,Dt
				((add.Opcode >> 9) & 7) != temporary ||
				(copyBack.Opcode & 0xF1F8) != 0x2000 || // MOVE.L Dt,Ds
				(copyBack.Opcode & 7) != temporary ||
				result == temporary ||
				result == savedLeft ||
				result == byteValue ||
				!hasResultMask &&
					(!dataflow.TryGetFacts(copyBack.Offset, out var copyBackFacts) ||
					 copyBackFacts.LiveConditionsAfter != M68kConditionCodeSet.None) ||
				IsDataRegisterReadBeforeOverwrite(
					instructions,
					index + patternLength,
					temporary,
					dataflow) ||
				IsDataRegisterReadBeforeOverwrite(
					instructions,
					index + patternLength,
					savedLeft,
					dataflow) ||
				instructions
					.Skip(index + 1)
					.Take(patternLength - 1)
					.Any(instruction => IsReferencedLabelAt(instruction.Offset)))
			{
				continue;
			}

			var offset = firstMove.Offset;
			var endOffset = lastInstruction.Offset + lastInstruction.Length;
			MoveLabelsToOffset(offset + firstMove.Length, endOffset, offset);
			var rotateOffset = 0;
			if (result != source)
			{
				_buffer.WriteWord(
					offset,
					0x2000 | (result << 9) | source); // MOVE.L Ds,Dr
				rotateOffset = 2;
			}
			_buffer.WriteWord(
				offset + rotateOffset,
				0xE158 | (5 << 9) | result); // ROL.W #5,Dr
			var addOffset = rotateOffset + 2;
			var replacementLength = addOffset + 2;
			if (hasByteMask)
			{
				_buffer.WriteWord(offset + addOffset, 0x0280 | byteValue); // ANDI.L #$FF,Db
				_buffer.WriteWord(offset + addOffset + 2, 0);
				_buffer.WriteWord(offset + addOffset + 4, 0x00FF);
				addOffset += 6;
				replacementLength += 6;
			}
			_buffer.WriteWord(offset + addOffset, 0xD040 | (result << 9) | byteValue); // ADD.W Db,Dr
			if (hasResultMask)
			{
				_buffer.WriteWord(offset + addOffset + 2, 0x0280 | result); // ANDI.L #$FFFF,Dr
				_buffer.WriteWord(offset + addOffset + 4, 0);
				_buffer.WriteWord(offset + addOffset + 6, 0xFFFF);
				replacementLength += 6;
			}
			_buffer.RemoveBytes(offset + replacementLength, endOffset - offset - replacementLength);
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

	private bool TryForwardImmediateStackTemporary()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var pushIndex = 0; pushIndex + 3 < instructions.Count; pushIndex++)
		{
			var push = instructions[pushIndex];
			if (push.Opcode != 0x2F3C ||
				push.Length != 6)
			{
				continue;
			}

			var reloadIndex = pushIndex + 1;
			while (reloadIndex < instructions.Count &&
				instructions[reloadIndex].Opcode == 0x42AF &&
				instructions[reloadIndex].Length == 4 &&
				unchecked((short)instructions[reloadIndex].ExtensionWord) >= 4)
			{
				reloadIndex++;
			}

			if (reloadIndex + 1 >= instructions.Count)
			{
				continue;
			}

			var reload = instructions[reloadIndex];
			var cleanup = instructions[reloadIndex + 1];
			if ((reload.Opcode & 0xF1FF) != 0x2017 ||
				reload.Length != 2 ||
				cleanup.Opcode != 0x588F ||
				cleanup.Length != 2 ||
				IsReferencedLabelAt(reload.Offset) ||
				IsReferencedLabelAt(cleanup.Offset))
			{
				continue;
			}

			var destinationRegister = (reload.Opcode >> 9) & 7;
			_buffer.WriteWord(
				push.Offset,
				(ushort)(0x203C | (destinationRegister << 9))); // MOVE.L #value,Dn
			for (var index = pushIndex + 1; index < reloadIndex; index++)
			{
				var frameClear = instructions[index];
				_buffer.WriteWord(
					frameClear.Offset + 2,
					unchecked((ushort)(
						(short)frameClear.ExtensionWord - 4)));
			}

			if (reloadIndex + 2 < instructions.Count &&
				instructions[reloadIndex + 2].Kind == M68kInstructionKind.Call)
			{
				// Calls receive values through the ABI, never through condition
				// codes, so the reload's flags are unobservable here.
				MoveLabelsToOffset(
					reload.Offset,
					cleanup.Offset + cleanup.Length,
					cleanup.Offset + cleanup.Length);
				_buffer.RemoveBytes(cleanup.Offset, cleanup.Length);
				_buffer.RemoveBytes(reload.Offset, reload.Length);
				return true;
			}

			// The intervening CLR instructions replace the immediate's flags.
			// Retain the reload's final N/Z/V/C result without reading the stack.
			_buffer.WriteWord(
				reload.Offset,
				(ushort)(0x4A80 | destinationRegister)); // TST.L Dn
			MoveLabelsToOffset(
				cleanup.Offset,
				cleanup.Offset + cleanup.Length,
				cleanup.Offset + cleanup.Length);
			_buffer.RemoveBytes(cleanup.Offset, cleanup.Length);
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

	private bool TryForwardRegisterStackStore()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var push = instructions[index];
			var store = instructions[index + 1];
			if (push.Length != 2 ||
				(push.Opcode & 0xFFF8) != 0x2F00 ||
				IsReferencedLabelAt(store.Offset))
			{
				continue;
			}

			var sourceRegister = push.Opcode & 7;
			ushort replacement;
			if (store.Opcode == 0x2F5F && store.Length == 4)
			{
				replacement = (ushort)(0x2F40 | sourceRegister); // MOVE.L Dn,d16(A7)
			}
			else if (store.Opcode == 0x23DF && store.Length == 6)
			{
				replacement = (ushort)(0x23C0 | sourceRegister); // MOVE.L Dn,abs.l
			}
			else
			{
				continue;
			}

			MoveLabelsToOffset(store.Offset, store.Offset + store.Length, push.Offset);
			_buffer.WriteWord(push.Offset, replacement);
			_buffer.RemoveBytes(store.Offset, 2);
			return true;
		}

		return false;
	}

	private bool TryForwardStackLoadToRegister(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var push = instructions[index];
			var pop = instructions[index + 1];
			if (IsReferencedLabelAt(pop.Offset) ||
				!TryGetRegisterStackPop(
					pop.Opcode,
					out var destinationIsAddress,
					out var destinationRegister) ||
				destinationIsAddress &&
					(!dataflow.TryGetFacts(pop.Offset, out var facts) ||
					 !facts.ConditionsAreDeadAfter))
			{
				continue;
			}

			ushort replacement;
			if (push.Opcode == 0x2F2F && push.Length == 4)
			{
				replacement = (ushort)((destinationIsAddress ? 0x206F : 0x202F) |
					(destinationRegister << 9));
			}
			else if (push.Opcode == 0x2F39 && push.Length == 6)
			{
				replacement = (ushort)((destinationIsAddress ? 0x2079 : 0x2039) |
					(destinationRegister << 9));
			}
			else
			{
				continue;
			}

			MoveLabelsToOffset(pop.Offset, pop.Offset + pop.Length, push.Offset);
			_buffer.WriteWord(push.Offset, replacement);
			_buffer.RemoveBytes(pop.Offset, pop.Length);
			return true;
		}

		return false;
	}

	private bool TryReplaceMemoryStackTransferWithRegister(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var push = instructions[index];
			var store = instructions[index + 1];
			if (push.Opcode != 0x2F2F ||
				push.Length != 4 ||
				IsReferencedLabelAt(store.Offset) ||
				!dataflow.TryGetFacts(store.Offset, out var storeFacts))
			{
				continue;
			}

			var destinationKind = store.Opcode switch
			{
				0x2F5F when store.Length == 4 => 0,
				0x23DF when store.Length == 6 => 1,
				_ => -1
			};
			if (destinationKind < 0)
			{
				continue;
			}

			var scratchRegister = Enumerable.Range(0, 8)
				.FirstOrDefault(
					register => (storeFacts.LiveDataAfter & (1 << register)) == 0,
					-1);
			if (scratchRegister < 0)
			{
				continue;
			}

			_buffer.WriteWord(
				push.Offset,
				(ushort)(0x202F | (scratchRegister << 9))); // MOVE.L d16(A7),Dn
			_buffer.WriteWord(
				store.Offset,
				(ushort)((destinationKind == 0 ? 0x2F40 : 0x23C0) |
					scratchRegister));
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

	private HashSet<int> ReferencedLabelOffsets()
	{
		var result = new HashSet<int>();
		foreach (var branch in _buffer.Branches)
		{
			if (_buffer.Labels.TryGetValue(branch.Target, out var target))
			{
				result.Add(target);
			}
		}
		foreach (var address in _buffer.Addresses)
		{
			if (!address.External &&
				_buffer.Labels.TryGetValue(address.Target, out var target))
			{
				result.Add(target);
			}
		}
		foreach (var reference in _buffer.PcRelative)
		{
			if (_buffer.Labels.TryGetValue(reference.Target, out var target))
			{
				result.Add(target);
			}
		}
		return result;
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

	private bool TryReplaceAddressNullCheckWithTest(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var moveAddress = instructions[index];
			var compare = instructions[index + 1];
			if ((moveAddress.Opcode & 0xF1F8) != 0x2040 ||
				moveAddress.Length != 2 ||
				(compare.Opcode & 0xF1FF) != 0xB0FC ||
				compare.Length != 4 ||
				compare.ExtensionWord != 0 ||
				IsReferencedLabelAt(compare.Offset) ||
				!dataflow.TryGetFacts(compare.Offset, out _))
			{
				continue;
			}

			var addressRegister = (moveAddress.Opcode >> 9) & 7;
			if (((compare.Opcode >> 9) & 7) != addressRegister)
			{
				continue;
			}

			var sourceRegister = moveAddress.Opcode & 7;
			if (IsAddressRegisterReadBeforeOverwrite(
				instructions,
				index + 2,
				addressRegister,
				dataflow))
			{
				_buffer.WriteWord(
					compare.Offset,
					0x4A80 | sourceRegister); // CMPA.W #0,An -> TST.L Dn
				_buffer.RemoveBytes(compare.Offset + 2, 2);
			}
			else
			{
				_buffer.WriteWord(
					moveAddress.Offset,
					0x4A80 | sourceRegister); // TST.L Dn
				_buffer.RemoveBytes(compare.Offset, compare.Length);
			}
			return true;
		}

		return false;
	}

	private bool TryReplaceCompareRegisterZeroWithTest(M68kInstructionDataflow dataflow)
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
				// A direct incoming edge to CMP would bypass MOVEQ.  When CMP
				// itself is not a target, every edge entering MOVEQ executes both
				// adjacent instructions, so its block-entry labels can move.
				IsReferencedLabelAt(compare.Offset) ||
				!dataflow.TryGetFacts(compare.Offset, out var compareFacts))
			{
				continue;
			}

			var destination = (compare.Opcode >> 9) & 7;
			_buffer.WriteWord(compare.Offset, (ushort)(0x4A80 | destination));
			var zeroRegister = (zero.Opcode >> 9) & 7;
			if ((compareFacts.LiveDataAfter & (1 << zeroRegister)) != 0 ||
				destination == zeroRegister)
			{
				// Keep MOVEQ when its zero value is still observable, but the
				// compare itself is still exactly equivalent to TST.L.
				return true;
			}

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

	private bool TryFoldMoveQuickIntoCompareImmediate(
		M68kInstructionDataflow dataflow)
	{
		// With instructions in the MC68020 cache, MOVEQ and register CMP
		// cost two clocks each. CMPI.L costs six clocks after fetching its
		// long immediate, and is also two bytes larger, so retain the
		// compact register form on this target.
		if (_cpu == M68kCpuTarget.M68020)
		{
			return false;
		}

		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var moveQuick = instructions[index];
			var compare = instructions[index + 1];
			if ((moveQuick.Opcode & 0xF100) != 0x7000 ||
				moveQuick.Length != 2 ||
				(moveQuick.Opcode & 0xFF) == 0 ||
				(compare.Opcode & 0xF1C0) != 0xB080 ||
				compare.Length != 2 ||
				(compare.Opcode & 7) != ((moveQuick.Opcode >> 9) & 7) ||
				((compare.Opcode >> 9) & 7) == ((moveQuick.Opcode >> 9) & 7) ||
				IsReferencedLabelAt(compare.Offset) ||
				!dataflow.TryGetFacts(compare.Offset, out var facts))
			{
				continue;
			}

			var constantRegister = (moveQuick.Opcode >> 9) & 7;
			if ((facts.LiveDataAfter & (1 << constantRegister)) != 0)
			{
				continue;
			}

			var destinationRegister = (compare.Opcode >> 9) & 7;
			if (_cpu == M68kCpuTarget.M68060 &&
				index > 0 &&
				!IsReferencedLabelAt(moveQuick.Offset))
			{
				var priorCopy = instructions[index - 1];
				if (priorCopy.Length == 2 &&
					(priorCopy.Opcode & 0xF1F8) == 0x2000 &&
					(priorCopy.Opcode & 7) == destinationRegister &&
					((priorCopy.Opcode >> 9) & 7) != destinationRegister)
				{
					// Preserve the source-register CMP form introduced below;
					// otherwise the next optimizer iteration would fold it back
					// into the longer CMPI form.
					continue;
				}
			}
			var hasCopiedCompareSource = false;
			var immediateDestination = destinationRegister;
			if (index > 0 && !IsReferencedLabelAt(moveQuick.Offset))
			{
				var copy = instructions[index - 1];
				if (copy.Length == 2 &&
					(copy.Opcode & 0xF1F8) == 0x2000 &&
					((copy.Opcode >> 9) & 7) == destinationRegister)
				{
					// MOVE.L Dsrc,Ddst followed by MOVEQ/CMP can keep the
					// copy for later uses while comparing the original source.
					// When the long CMPI form is selected, MOVEQ is removed
					// below, so Dsrc still holds the copied value at CMPI.
					immediateDestination = copy.Opcode & 7;
					hasCopiedCompareSource = true;
				}
			}

			var immediate = unchecked((uint)(int)(sbyte)(moveQuick.Opcode & 0xFF));
			if (_cpu == M68kCpuTarget.M68060 && hasCopiedCompareSource)
			{
				// On the superscalar MC68060, avoid the long CMPI
				// extension when a register compare can use the dead constant
				// register (or another dead data-register scratch).
				var compareSource = constantRegister;
				if (compareSource == immediateDestination)
				{
					compareSource = -1;
					for (var candidate = 0; candidate < 8; candidate++)
					{
						if (candidate == immediateDestination ||
							candidate == destinationRegister ||
							(facts.LiveDataAfter & (1 << candidate)) != 0)
						{
							continue;
						}
						compareSource = candidate;
						break;
					}
				}

				if (compareSource >= 0)
				{
					_buffer.WriteWord(
						moveQuick.Offset,
						(ushort)(0x7000 |
							(compareSource << 9) |
							(moveQuick.Opcode & 0xFF))); // MOVEQ #imm,Dscratch
					_buffer.WriteWord(
						compare.Offset,
						(ushort)(0xB080 |
							(immediateDestination << 9) |
							compareSource)); // CMP.L Dscratch,Dn
					return true;
				}
			}

			_buffer.WriteWord(
				moveQuick.Offset,
				(ushort)(0x0C80 | immediateDestination)); // CMPI.L #imm,Dn
			_buffer.RemoveBytes(compare.Offset, compare.Length);
			_buffer.InsertBytes(moveQuick.Offset + 2, 4);
			_buffer.WriteWord(moveQuick.Offset + 2, (ushort)(immediate >> 16));
			_buffer.WriteWord(moveQuick.Offset + 4, (ushort)immediate);
			return true;
		}

		return false;
	}

	private bool TryUseRegisterCompareForSmallImmediate(
		M68kInstructionDataflow dataflow)
	{
		if (_cpu != M68kCpuTarget.M68020)
		{
			return false;
		}

		foreach (var compare in _assembler.GetInstructionStream())
		{
			if ((compare.Opcode & 0xFFF8) != 0x0C80 ||
				compare.Length != 6 ||
				HasAddressFixup(compare) ||
				!dataflow.TryGetFacts(compare.Offset, out var facts))
			{
				continue;
			}

			var immediate = unchecked((int)compare.ExtensionLong);
			if (immediate == 0 || immediate is < sbyte.MinValue or > sbyte.MaxValue)
			{
				continue;
			}

			var destination = compare.Opcode & 7;
			for (var scratch = 0; scratch < 8; scratch++)
			{
				if (scratch == destination ||
					(facts.LiveDataAfter & (1 << scratch)) != 0)
				{
					continue;
				}

				// MC68020 cache case: MOVEQ plus register CMP is four
				// clocks and four bytes; CMPI.L is six clocks and six bytes.
				_buffer.WriteWord(
					compare.Offset,
					(ushort)(0x7000 | (scratch << 9) | (byte)immediate));
				_buffer.WriteWord(
					compare.Offset + 2,
					(ushort)(0xB080 | (destination << 9) | scratch));
				_buffer.RemoveBytes(compare.Offset + 4, 2);
				return true;
			}
		}

		return false;
	}

	private bool TryFoldCopyMoveQuickCompare(
		M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var copy = instructions[index];
			var moveQuick = instructions[index + 1];
			var compare = instructions[index + 2];
			if ((copy.Opcode & 0xF1F8) != 0x2000 ||
				copy.Length != 2 ||
				(moveQuick.Opcode & 0xF100) != 0x7000 ||
				moveQuick.Length != 2 ||
				(moveQuick.Opcode & 0xFF) == 0 ||
				(compare.Opcode & 0xF1C0) != 0xB080 ||
				compare.Length != 2 ||
				IsReferencedLabelAt(moveQuick.Offset) ||
				IsReferencedLabelAt(compare.Offset) ||
				!dataflow.TryGetFacts(compare.Offset, out var facts))
			{
				continue;
			}

			var sourceRegister = copy.Opcode & 7;
			var destinationRegister = (copy.Opcode >> 9) & 7;
			var constantRegister = (moveQuick.Opcode >> 9) & 7;
			if (sourceRegister == destinationRegister ||
				(compare.Opcode & 7) != constantRegister ||
				((compare.Opcode >> 9) & 7) != destinationRegister ||
				constantRegister == destinationRegister ||
				(facts.LiveDataAfter &
				 ((1 << sourceRegister) |
				  (1 << destinationRegister) |
				  (1 << constantRegister))) != 0)
			{
				continue;
			}

			if (_cpu == M68kCpuTarget.M68020)
			{
				// Dsrc is the value being compared and is dead afterwards.
				// Put the constant in the dead copy destination instead:
				//   MOVE.L Dsrc,Ddst; MOVEQ #k,Dsrc; CMP.L Dsrc,Ddst
				// becomes
				//   MOVEQ #k,Ddst; CMP.L Ddst,Dsrc
				// This is four cache-case clocks and four bytes on MC68020.
				_buffer.WriteWord(
					copy.Offset,
					(ushort)(0x7000 |
						(destinationRegister << 9) |
						(moveQuick.Opcode & 0xFF)));
				_buffer.WriteWord(
					moveQuick.Offset,
					(ushort)(0xB080 |
						(sourceRegister << 9) |
						destinationRegister));
				_buffer.RemoveBytes(compare.Offset, compare.Length);
				return true;
			}

			var immediate = unchecked((uint)(int)(sbyte)(moveQuick.Opcode & 0xFF));
			_buffer.RemoveBytes(compare.Offset, compare.Length);
			_buffer.RemoveBytes(moveQuick.Offset, moveQuick.Length);
			_buffer.WriteWord(
				copy.Offset,
				(ushort)(0x0C80 | sourceRegister)); // CMPI.L #imm,Dn
			_buffer.InsertBytes(copy.Offset + 2, 4);
			_buffer.WriteWord(copy.Offset + 2, (ushort)(immediate >> 16));
			_buffer.WriteWord(copy.Offset + 4, (ushort)immediate);
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

	private bool TryRemoveDiscardedStackPush(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var push = instructions[index];
			var cleanup = instructions[index + 1];
			var isRegisterPush =
				((push.Opcode & 0xFFF8) == 0x2F00 ||
				 (push.Opcode & 0xFFF8) == 0x2F08) &&
				push.Length == 2;
			var isImmediatePush = push.Opcode == 0x2F3C &&
				push.Length == 6;
			if ((!isRegisterPush && !isImmediatePush) ||
				cleanup.Opcode != 0x588F ||
				cleanup.Length != 2 ||
				IsReferencedLabelAt(push.Offset) ||
				IsReferencedLabelAt(cleanup.Offset) ||
				!dataflow.TryGetFacts(push.Offset, out var pushFacts) ||
				!pushFacts.ConditionsAreDeadAfter)
			{
				continue;
			}

			var endOffset = cleanup.Offset + cleanup.Length;
			MoveLabelsToOffset(push.Offset, endOffset, endOffset);
			_buffer.RemoveBytes(push.Offset, endOffset - push.Offset);
			return true;
		}

		return false;
	}

	private bool TryRemoveDeadInstruction(M68kInstructionDataflow dataflow)
	{
		if (dataflow.Instructions.Any(static instruction =>
			instruction.Kind is
				M68kInstructionKind.ConditionalBranch or
				M68kInstructionKind.UnconditionalBranch or
				M68kInstructionKind.Dbcc or
				M68kInstructionKind.Call))
		{
			return false;
		}
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

	private bool TryRemoveTerminalWordStoreNormalization(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var mask = instructions[index];
			if (mask.Length != 6 ||
				(mask.Opcode & 0xFFF8) != 0x0280 ||
				mask.ExtensionLong != 0x0000FFFF ||
				IsReferencedLabelAt(mask.Offset))
			{
				continue;
			}

			var register = mask.Opcode & 7;
			var registerMask = (ushort)(1 << register);
			for (var nextIndex = index + 1;
				nextIndex < instructions.Count && nextIndex <= index + 4;
				nextIndex++)
			{
				var next = instructions[nextIndex];
				if (IsReferencedLabelAt(next.Offset) ||
					!dataflow.TryGetFacts(next.Offset, out var facts) ||
					facts.Effects.IsBarrier ||
					facts.Effects.ReadsConditions != M68kConditionCodeSet.None)
				{
					break;
				}

				var isLowWordMove =
					(next.Opcode & 0xF000) == 0x3000 &&
					((next.Opcode >> 3) & 7) == 0 &&
					(next.Opcode & 7) == register;
				if (isLowWordMove)
				{
					if (!IsDataRegisterReadBeforeOverwrite(
							instructions,
							nextIndex + 1,
							register,
							dataflow))
					{
						_buffer.RemoveBytes(mask.Offset, mask.Length);
						return true;
					}
					break;
				}

				if ((facts.Effects.UsesData & registerMask) != 0 ||
					(facts.Effects.DefinesData & registerMask) != 0 ||
					facts.Effects.WritesConditions != M68kConditionCodeSet.None)
				{
					break;
				}
			}
		}

		return false;
	}

	private bool HasInternalLabel(M68kEmittedInstruction instruction)
	{
		var end = instruction.Offset + instruction.Length;
		return _buffer.Labels.Values.Any(offset =>
			offset > instruction.Offset && offset < end);
	}

	private bool TryFoldZeroExtendedWordAdd(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var mask = instructions[index];
			var moveQuick = instructions[index + 1];
			var add = instructions[index + 2];
			if (mask.Length != 6 ||
				(mask.Opcode & 0xFFF8) != 0x0280 ||
				mask.ExtensionLong != ushort.MaxValue ||
				moveQuick.Length != 2 ||
				(moveQuick.Opcode & 0xF100) != 0x7000 ||
				unchecked((sbyte)(byte)moveQuick.Opcode) < 0 ||
				(add.Opcode & 0xF1F8) != 0xD080 ||
				(add.Opcode & 7) != (mask.Opcode & 7) ||
				((add.Opcode >> 9) & 7) != ((moveQuick.Opcode >> 9) & 7) ||
				(mask.Opcode & 7) == ((moveQuick.Opcode >> 9) & 7) ||
				IsReferencedLabelAt(moveQuick.Offset) ||
				IsReferencedLabelAt(add.Offset) ||
				!dataflow.TryGetFacts(add.Offset, out var facts) ||
				!facts.ConditionsAreDeadAfter ||
				(facts.LiveDataAfter & (1 << (mask.Opcode & 7))) != 0)
			{
				continue;
			}

			// MOVEQ has already cleared the destination's upper word. A word add
			// therefore produces the same zero-extended ushort result without
			// first normalizing a source whose upper word is otherwise dead.
			_buffer.WriteWord(add.Offset, (ushort)(add.Opcode - 0x40));
			_buffer.RemoveBytes(mask.Offset, mask.Length);
			return true;
		}

		return false;
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

	private bool TryNarrowCompareAddressImmediate()
	{
		foreach (var instruction in _assembler.GetInstructionStream())
		{
			if ((instruction.Opcode & 0xF1FF) != 0xB1FC ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				!IsSignedWord(instruction.ExtensionLong) ||
				IsReferencedLabelAt(instruction.Offset))
			{
				continue;
			}

			// The word immediate is sign-extended before the 32-bit address
			// comparison, so every signed-word literal has identical flags.
			_buffer.WriteWord(
				instruction.Offset,
				(ushort)(instruction.Opcode & 0xFEFF));
			_buffer.RemoveBytes(instruction.Offset + 2, 2);
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

	private bool TryFoldWordConstantRegisterUse(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var constant = instructions[index];
			if ((constant.Opcode & 0xF1FF) != 0x203C ||
				constant.Length != 6 ||
				HasAddressFixup(constant) ||
				IsReferencedLabelAt(constant.Offset) ||
				(constant.ExtensionLong & 0xFFFF0000u) != 0)
			{
				continue;
			}

			var source = constant.Opcode >> 9 & 7;
			var sourceMask = (ushort)(1 << source);
			for (var useIndex = index + 1;
				useIndex < instructions.Count && useIndex <= index + 4;
				useIndex++)
			{
				var use = instructions[useIndex];
				if (IsReferencedLabelAt(use.Offset) ||
					!dataflow.TryGetFacts(use.Offset, out var useFacts) ||
					useFacts.Effects.IsBarrier ||
					useFacts.Effects.ReadsConditions != M68kConditionCodeSet.None)
				{
					break;
				}

				var isWordCopy =
					(use.Opcode & 0xF1F8) == 0x3000 &&
					(use.Opcode & 7) == source;
				var logicalFamily = use.Opcode & 0xF1F8;
				var isWordLogical = logicalFamily is 0x8040 or 0xC040 or 0xB040 &&
					(use.Opcode & 7) == source;
				var isLongAnd = (use.Opcode & 0xF1F8) == 0xC080;
				if (isLongAnd)
				{
					var andSource = use.Opcode & 7;
					var andDestination = use.Opcode >> 9 & 7;
					var sourceIsConstant = andSource == source;
					var destinationIsConstant = andDestination == source;
					var destinationRange = dataflow.GetDataValueBefore(
						use.Offset,
						andDestination);
					var destinationUpperWordIsZero =
						(destinationRange.KnownZeroMask & 0xFFFF0000u) == 0xFFFF0000u;
					var sourceConstantIsDead = sourceIsConstant &&
						!IsDataRegisterReadBeforeOverwrite(
							instructions,
							useIndex + 1,
							source,
							dataflow);
					var constantUpperWordIsUnobserved = destinationIsConstant &&
						!IsDataRegisterUpperWordObservedBeforeOverwrite(
							instructions,
							useIndex + 1,
							source,
							dataflow);
					if ((sourceConstantIsDead && destinationUpperWordIsZero ||
						constantUpperWordIsUnobserved) &&
						useFacts.LiveConditionsAfter == M68kConditionCodeSet.None &&
						!InterveningInstructionTouchesRegister(
							instructions,
							index + 1,
							useIndex,
							andDestination,
							dataflow))
					{
						if (destinationIsConstant)
						{
							_buffer.WriteWord(
								constant.Offset,
								(ushort)(0x3000 | (source << 9) | andSource)); // MOVE.W Dn,Dconstant
							_buffer.WriteWord(
								constant.Offset + 2,
								(ushort)(0x0240 | source)); // ANDI.W #mask,Dconstant
							_buffer.WriteWord(
								constant.Offset + 4,
								(ushort)constant.ExtensionLong);
							_buffer.RemoveBytes(use.Offset, use.Length);
						}
						else
						{
							_buffer.WriteWord(
								constant.Offset,
								(ushort)(0x0240 | andDestination)); // ANDI.W #mask,Dn
							_buffer.RemoveBytes(constant.Offset + 2, 2);
							_buffer.RemoveBytes(use.Offset - 2, use.Length);
						}
						return true;
					}
				}
				if (isWordCopy || isWordLogical)
				{
					var destination = use.Opcode >> 9 & 7;
					if (destination == source ||
						IsDataRegisterUpperWordObservedBeforeOverwrite(
							instructions,
							useIndex + 1,
							source,
							dataflow) ||
						InterveningInstructionTouchesRegister(
							instructions,
							index + 1,
							useIndex,
							destination,
							dataflow))
					{
						break;
					}

					var replacement = isWordCopy
						? 0x303C | (destination << 9) // MOVE.W #imm,Dn
						: logicalFamily switch
						{
							0x8040 => 0x0040 | destination, // ORI.W
							0xC040 => 0x0240 | destination, // ANDI.W
							_ => 0x0A40 | destination // EORI.W
						};
					_buffer.WriteWord(constant.Offset, (ushort)replacement);
					_buffer.RemoveBytes(constant.Offset + 2, 2);
					_buffer.RemoveBytes(use.Offset - 2, use.Length);
					return true;
				}

				if ((useFacts.Effects.UsesData & sourceMask) != 0 ||
					(useFacts.Effects.DefinesData & sourceMask) != 0)
				{
					break;
				}
			}
		}

		return false;
	}

	private static bool IsDataRegisterUpperWordObservedBeforeOverwrite(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int register,
		M68kInstructionDataflow dataflow)
	{
		var mask = (ushort)(1 << register);
		for (var index = startIndex; index < instructions.Count; index++)
		{
			var instruction = instructions[index];
			if (instruction.Kind == M68kInstructionKind.Return)
			{
				return false;
			}
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				facts.Effects.IsBarrier ||
				instruction.Kind is M68kInstructionKind.ConditionalBranch or
					M68kInstructionKind.UnconditionalBranch or
					M68kInstructionKind.Dbcc)
			{
				return true;
			}

			var isWordMoveSource =
				(instruction.Opcode & 0xF000) == 0x3000 &&
				((instruction.Opcode >> 3) & 7) == 0 &&
				(instruction.Opcode & 7) == register;
			var isWordMoveDestination =
				(instruction.Opcode & 0xF1C0) == 0x3000 &&
				((instruction.Opcode >> 9) & 7) == register;
			var wordBinaryFamily = instruction.Opcode & 0xF1C0;
			var isWordBinaryDestination =
				wordBinaryFamily is 0x8040 or 0x9040 or 0xC040 or 0xD040 &&
				((instruction.Opcode >> 9) & 7) == register;
			var isWordImmediateDestination =
				(instruction.Opcode & 0xFFC0) is 0x0040 or 0x0240 or 0x0A40 &&
				(instruction.Opcode & 7) == register;
			if (isWordMoveSource ||
				isWordMoveDestination ||
				isWordBinaryDestination ||
				isWordImmediateDestination)
			{
				continue;
			}

			if ((facts.Effects.UsesData & mask) != 0)
			{
				return true;
			}
			if ((facts.Effects.DefinesData & mask) != 0)
			{
				return false;
			}
		}

		return false;
	}

	private static bool InterveningInstructionTouchesRegister(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int endIndex,
		int register,
		M68kInstructionDataflow dataflow)
	{
		var mask = (ushort)(1 << register);
		for (var index = startIndex; index < endIndex; index++)
		{
			if (!dataflow.TryGetFacts(instructions[index].Offset, out var facts) ||
				((facts.Effects.UsesData | facts.Effects.DefinesData) & mask) != 0)
			{
				return true;
			}
		}
		return false;
	}

	private bool TryUseMoveQuickAndMask(
		M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xFFF8) != 0x0280 ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts))
			{
				continue;
			}

			var signedMask = unchecked((int)instruction.ExtensionLong);
			if (signedMask == 0 || signedMask is < sbyte.MinValue or > sbyte.MaxValue)
			{
				continue;
			}

			var canUseWordImmediate =
				(instruction.ExtensionLong & 0xFFFF0000u) == 0xFFFF0000u &&
				(facts.LiveConditionsAfter &
					(M68kConditionCodeSet.Negative | M68kConditionCodeSet.Zero)) == 0;
			var baselineBytes = canUseWordImmediate ? 4 : 6;
			var baselineCycles = GetLogicalImmediateDataRegisterCycles(canUseWordImmediate);
			const int candidateBytes = 4;
			var candidateCycles = GetMoveQuickCycles() + GetLogicalDataRegisterCycles();
			var generallyProfitable =
				candidateBytes <= baselineBytes && candidateCycles <= baselineCycles;
			var cacheSizeProfitable =
				candidateBytes < baselineBytes && IsSizeFirstOffset(instruction.Offset);
			if (!generallyProfitable && !cacheSizeProfitable)
			{
				continue;
			}

			var destination = instruction.Opcode & 7;
			var scratch = -1;
			for (var priority = 0; priority < 8; priority++)
			{
				var candidate = priority switch
				{
					0 => 1,
					1 => 0,
					_ => priority
				};
				if (candidate != destination &&
					(facts.LiveDataAfter & (1 << candidate)) == 0)
				{
					scratch = candidate;
					break;
				}
			}

			if (scratch < 0)
			{
				continue;
			}

			_buffer.WriteWord(
				instruction.Offset,
				(ushort)(0x7000 | (scratch << 9) | (byte)signedMask));
			_buffer.WriteWord(
				instruction.Offset + 2,
				(ushort)(0xC080 | (destination << 9) | scratch));
			_buffer.RemoveBytes(instruction.Offset + 4, 2);
			return true;
		}

		return false;
	}

	private bool TryUseSingleBitLogicalImmediate(
		M68kInstructionDataflow dataflow)
	{
		const M68kConditionCodeSet logicalConditions =
			M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow |
			M68kConditionCodeSet.Carry;
		foreach (var instruction in dataflow.Instructions)
		{
			var operation = instruction.Opcode & 0xFFF8;
			if (operation is not 0x0080 and not 0x0A80 ||
				instruction.Length != 6 ||
				HasAddressFixup(instruction) ||
				IsReferencedLabelAt(instruction.Offset) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				(facts.LiveConditionsAfter & logicalConditions) != 0)
			{
				continue;
			}

			var immediate = instruction.ExtensionLong;
			if (immediate == 0 || (immediate & (immediate - 1)) != 0)
			{
				continue;
			}

			var bit = System.Numerics.BitOperations.TrailingZeroCount(immediate);
			var baselineWordSized = bit < 16;
			var baselineBytes = baselineWordSized ? 4 : 6;
			var baselineCycles = GetLogicalImmediateDataRegisterCycles(baselineWordSized);
			const int candidateBytes = 4;
			var candidateCycles = GetImmediateBitChangeDataRegisterCycles(bit);
			var generallyProfitable =
				candidateBytes <= baselineBytes && candidateCycles <= baselineCycles;
			var cacheSizeProfitable =
				candidateBytes < baselineBytes && IsSizeFirstOffset(instruction.Offset);
			if (!generallyProfitable && !cacheSizeProfitable)
			{
				continue;
			}

			var destination = instruction.Opcode & 7;
			var bitOpcode = operation == 0x0080
				? 0x08C0 // ORI.L single bit -> BSET #bit,Dn
				: 0x0840; // EORI.L single bit -> BCHG #bit,Dn
			_buffer.WriteWord(
				instruction.Offset,
				(ushort)(bitOpcode | destination));
			_buffer.WriteWord(instruction.Offset + 2, (ushort)bit);
			_buffer.RemoveBytes(instruction.Offset + 4, 2);
			return true;
		}

		return false;
	}

	private int GetLogicalImmediateDataRegisterCycles(bool wordSized) => _cpu switch
	{
		M68kCpuTarget.M68000 => wordSized ? 8 : 16,
		M68kCpuTarget.M68020 => wordSized ? 4 : 6,
		M68kCpuTarget.M68040 => 1,
		M68kCpuTarget.M68060 => 1,
		_ => throw new ArgumentOutOfRangeException()
	};

	private int GetLogicalDataRegisterCycles() => _cpu switch
	{
		M68kCpuTarget.M68000 => 4,
		M68kCpuTarget.M68020 => 2,
		M68kCpuTarget.M68040 => 1,
		M68kCpuTarget.M68060 => 1,
		_ => throw new ArgumentOutOfRangeException()
	};

	private int GetImmediateBitChangeDataRegisterCycles(int bit) => _cpu switch
	{
		// On the MC68000, immediate BCHG/BSET takes two extra cycles for bits 16-31.
		M68kCpuTarget.M68000 => bit < 16 ? 10 : 12,
		M68kCpuTarget.M68020 => 4,
		M68kCpuTarget.M68040 => 1,
		M68kCpuTarget.M68060 => 1,
		_ => throw new ArgumentOutOfRangeException()
	};

	private bool TryRemoveRedundantLogicalImmediate()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var first = instructions[index];
			var second = instructions[index + 1];
			if (first.Length != 6 ||
				second.Length != first.Length ||
				IsReferencedLabelAt(second.Offset))
			{
				continue;
			}

			var firstOperation = first.Opcode & 0xFFF8;
			if (firstOperation != 0x0280 && firstOperation != 0x0080 ||
				second.Opcode != first.Opcode)
			{
				continue;
			}

			var firstImmediate = _buffer.ReadLong(first.Offset + 2);
			var secondImmediate = _buffer.ReadLong(second.Offset + 2);
			var secondIsRedundant = firstOperation == 0x0280
				? (firstImmediate & secondImmediate) == secondImmediate
				: (firstImmediate | secondImmediate) == firstImmediate;
			if (!secondIsRedundant)
			{
				continue;
			}

			_buffer.RemoveBytes(second.Offset, second.Length);
			return true;
		}

		return false;
	}

	private bool TryRemoveRepeatedMaskAcrossUntouchedRegister(
		M68kInstructionDataflow dataflow)
	{
		const M68kConditionCodeSet logicalConditions =
			M68kConditionCodeSet.Negative |
			M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow |
			M68kConditionCodeSet.Carry;
		foreach (var mask in dataflow.Instructions)
		{
			if ((mask.Opcode & 0xFFF8) != 0x0280 ||
				mask.Length != 6 ||
				HasAddressFixup(mask) ||
				!dataflow.TryGetFacts(mask.Offset, out var maskFacts) ||
				(maskFacts.LiveConditionsAfter & logicalConditions) != 0)
			{
				continue;
			}

			var immediate = mask.ExtensionLong;
			var incoming = dataflow.GetDataValueBefore(mask.Offset, mask.Opcode & 7);
			var maskIsContiguousLowBits =
				(immediate & unchecked(immediate + 1)) == 0;
			var clearedBits = ~immediate;
			if ((incoming.KnownZeroMask & clearedBits) == clearedBits ||
				incoming.IsKnown &&
				(maskIsContiguousLowBits && incoming.Maximum <= immediate ||
				 incoming.IsExact(out var exact) && (exact & immediate) == exact))
			{
				_buffer.RemoveBytes(mask.Offset, mask.Length);
				return true;
			}
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
				!_assembler.AddressFixupOffsets.Contains(offset + 2) &&
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
			if (testOffset + 1 >= _buffer.Bytes.Count)
			{
				continue;
			}
			if (_buffer.HasLabelAt(testOffset) &&
				!CanRelocateLabels(testOffset))
			{
				continue;
			}

			var test = _buffer.ReadWord(testOffset);
			if ((test & 0xFFF8) != 0x4A80 ||
				destination != (test & 7))
			{
				continue;
			}

			RelocateLabels(testOffset, offset);
			_buffer.RemoveBytes(testOffset, 2);
			return true;
		}

		return false;
	}

	private bool TryDistributeMoveQuickAcrossConditionalBranch(
		M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var index = 0; index + 3 < instructions.Count; index++)
		{
			var move = instructions[index];
			var moveQuick = instructions[index + 1];
			var test = instructions[index + 2];
			var branch = instructions[index + 3];
			if ((move.Opcode & 0xF1F8) != 0x2000 ||
				move.Length != 2 ||
				(moveQuick.Opcode & 0xF100) != 0x7000 ||
				moveQuick.Length != 2 ||
				(test.Opcode & 0xFFF8) != 0x4A80 ||
				test.Length != 2 ||
				(test.Opcode & 7) != ((move.Opcode >> 9) & 7) ||
				((moveQuick.Opcode >> 9) & 7) == ((move.Opcode >> 9) & 7) ||
				branch.Kind != M68kInstructionKind.ConditionalBranch ||
				branch.TargetOffset is not { } targetOffset ||
				targetOffset <= branch.Offset + branch.Length ||
				IsReferencedLabelAt(moveQuick.Offset) ||
				IsReferencedLabelAt(test.Offset) ||
				IsReferencedLabelAt(branch.Offset) ||
				!TryGetBranch(branch.Offset, out var branchFixup))
			{
				continue;
			}

			var moveQuickRegister = (moveQuick.Opcode >> 9) & 7;
			var moveQuickMask = 1 << moveQuickRegister;
			var fallthroughOffset = branch.Offset + branch.Length;
			if (!dataflow.TryGetFacts(fallthroughOffset, out var fallthroughFacts) ||
				!dataflow.TryGetFacts(targetOffset, out var targetFacts))
			{
				continue;
			}
			var needsFallthroughMove =
				(fallthroughFacts.LiveDataBefore & moveQuickMask) != 0;
			var needsTargetMove =
				(targetFacts.LiveDataBefore & moveQuickMask) != 0;
			if (needsFallthroughMove &&
				(IsReferencedLabelAt(fallthroughOffset) ||
				 fallthroughFacts.LiveConditionsBefore != M68kConditionCodeSet.None) ||
				needsTargetMove &&
				(!HasUniqueBranchPredecessor(instructions, index + 3, targetOffset) ||
				 targetFacts.LiveConditionsBefore != M68kConditionCodeSet.None ||
				 _buffer.Addresses.Any(address =>
					 !address.External &&
					 string.Equals(address.Target, branchFixup.Target, StringComparison.Ordinal)) ||
				 _buffer.PcRelative.Any(reference =>
					 string.Equals(reference.Target, branchFixup.Target, StringComparison.Ordinal))))
			{
				continue;
			}

			var moveQuickOpcode = moveQuick.Opcode;
			_buffer.RemoveBytes(moveQuick.Offset, moveQuick.Length + test.Length);
			var shiftedBranchOffset = move.Offset + move.Length;
			if (needsFallthroughMove)
			{
				_buffer.InsertBytes(shiftedBranchOffset + branch.Length, 2);
				_buffer.WriteWord(shiftedBranchOffset + branch.Length, moveQuickOpcode);
			}
			if (needsTargetMove)
			{
				if (!_buffer.Labels.TryGetValue(branchFixup.Target, out var shiftedTargetOffset))
				{
					throw new InvalidOperationException("Conditional branch target disappeared during MOVEQ distribution.");
				}
				_buffer.InsertBytes(shiftedTargetOffset, 2);
				_buffer.WriteWord(shiftedTargetOffset, moveQuickOpcode);
			}
			return true;
		}

		return false;
	}

	private static bool HasUniqueBranchPredecessor(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int branchIndex,
		int targetOffset)
	{
		var targetIndex = -1;
		var branchPredecessors = 0;
		var matchingBranchIndex = -1;
		for (var index = 0; index < instructions.Count; index++)
		{
			if (instructions[index].Offset == targetOffset)
			{
				targetIndex = index;
			}
			if (instructions[index].TargetOffset == targetOffset &&
				instructions[index].Kind is M68kInstructionKind.ConditionalBranch or
					M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Dbcc)
			{
				branchPredecessors++;
				matchingBranchIndex = index;
			}
		}

		if (targetIndex < 0 ||
			branchPredecessors != 1 ||
			matchingBranchIndex != branchIndex)
		{
			return false;
		}

		if (targetIndex == 0)
		{
			return true;
		}
		return instructions[targetIndex - 1].Kind is
			M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Return;
	}

	private bool TryRemoveRedundantAndTest()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var and = instructions[index];
			var test = instructions[index + 1];
			if ((and.Opcode & 0xF1C0) != 0xC080 ||
				and.Length != 2 ||
				(test.Opcode & 0xFFF8) != 0x4A80 ||
				test.Length != 2 ||
				(test.Opcode & 7) != ((and.Opcode >> 9) & 7) ||
				IsReferencedLabelAt(test.Offset))
			{
				continue;
			}

			_buffer.RemoveBytes(test.Offset, test.Length);
			return true;
		}

		return false;
	}

	private bool CanRelocateLabels(int sourceOffset)
	{
		foreach (var label in _buffer.Labels
			.Where(item => item.Value == sourceOffset)
			.Select(static item => item.Key))
		{
			if (_buffer.Branches.Any(branch =>
					string.Equals(branch.Target, label, StringComparison.Ordinal)) ||
				_buffer.Addresses.Any(address =>
					string.Equals(address.Target, label, StringComparison.Ordinal)) ||
				_buffer.PcRelative.Any(fixup =>
					string.Equals(fixup.Target, label, StringComparison.Ordinal)))
			{
				return false;
			}
		}
		return true;
	}

	private void RelocateLabels(int sourceOffset, int destinationOffset)
	{
		foreach (var label in _buffer.Labels
			.Where(item => item.Value == sourceOffset)
			.Select(static item => item.Key)
			.ToArray())
		{
			_buffer.Labels[label] = destinationOffset;
		}
	}

	private bool TryFoldStackAllocationIntoRegisterPush()
	{
		var instructions = _assembler.GetInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var allocation = instructions[index];
			var store = instructions[index + 1];
			if (allocation.Opcode != 0x598F || // SUBQ.L #4,A7
				allocation.Length != 2 ||
				(store.Opcode & 0xFFF0) != 0x2E80 || // MOVE.L Dn/An,(A7)
				store.Length != 2 ||
				(store.Opcode & 0x000F) == 0x000F || // A7 source changes semantics
				_buffer.HasLabelAt(store.Offset))
			{
				continue;
			}

			_buffer.WriteWord(
				allocation.Offset,
				(ushort)(0x2F00 | (store.Opcode & 0x000F))); // MOVE.L Dn/An,-(A7)
			_buffer.RemoveBytes(store.Offset, store.Length);
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

	private enum ConstantSynthesisOperation : byte
	{
		Swap,
		RotateRight
	}

	private readonly record struct ConstantSynthesisTransform(
		ConstantSynthesisOperation Operation,
		int Count,
		int CacheCycles);

	private readonly record struct ConstantSynthesisCandidate(
		sbyte MoveQuickValue,
		ConstantSynthesisTransform Transform,
		int EncodedBytes,
		int CacheCycles)
	{
		internal ushort EncodeTransform(int destination) => Transform.Operation switch
		{
			ConstantSynthesisOperation.Swap => (ushort)(0x4840 | destination),
			ConstantSynthesisOperation.RotateRight => (ushort)(
				0xE098 |
				((Transform.Count == 8 ? 0 : Transform.Count) << 9) |
				destination),
			_ => throw new ArgumentOutOfRangeException()
		};
	}

	private readonly record struct AddressAdjustment(int Register, int Length, int Displacement);
}
