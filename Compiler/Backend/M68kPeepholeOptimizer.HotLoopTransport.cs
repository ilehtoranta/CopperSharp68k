/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryForwardGeneratedRegisterCopies(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		for (var i = 0; i < instructions.Count; i++)
		{
			var copy = instructions[i];
			if (!copy.IsDecoded || copy.Length != 2 || (copy.Opcode & 0xF1F8) != 0x2000 ||
				HasInternalLabel(copy) || _assembler.TryGetInstructionEffects(copy.Offset, out _)) continue;
			var source = copy.Opcode & 7;
			var temporary = copy.Opcode >> 9 & 7;
			if (source == temporary) continue;
			var changedFlags = M68kConditionCodeSet.Negative | M68kConditionCodeSet.Zero |
				M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry;
			// An entry at the first copy remains valid after its labels move with
			// RemoveBytes. Later independent entries, memory accesses, barriers and
			// observations of MOVE's flags end this bounded straight-line proof.
			var end = copy.Offset + copy.Length;
			for (var j = i + 1; j < instructions.Count && j <= i + 8; j++)
			{
				var use = instructions[j];
				if (!use.IsDecoded || use.Offset != end || use.Kind != M68kInstructionKind.Normal ||
					HasInternalLabel(use) || IsReferencedLabelAt(use.Offset) ||
					!CanFoldAcrossUnreferencedIlLabels(copy.Offset, use.Offset + use.Length,
						allowUnreferencedMarkers: true) ||
					!GeneratedSizeEligible(copy.Offset, use.Offset + use.Length) ||
					_assembler.TryGetInstructionEffects(use.Offset, out _) ||
					!dataflow.TryGetFacts(use.Offset, out var facts) || facts.Effects.IsBarrier ||
					facts.Effects.ReadsMemory != M68kMemorySet.None || facts.Effects.WritesMemory != M68kMemorySet.None ||
					(facts.Effects.ReadsConditions & changedFlags) != 0) break;
				if ((facts.Effects.UsesData & (1 << temporary)) != 0)
				{
					// CMP reads both operands; arithmetic may only substitute its
					// source. The temporary must be dead on every successor edge.
					if (use.Length != 2 || (use.Opcode >> 3 & 7) != 0 ||
						(facts.LiveDataAfter & (1 << temporary)) != 0 &&
						!CopiedBitsAreUnobserved(dataflow, j + 1, temporary)) break;
					var operation = use.Opcode & 0xF1C0;
					var comparison = operation == 0xB080;
					var sourceUse = (use.Opcode & 7) == temporary;
					var destinationUse = (use.Opcode >> 9 & 7) == temporary;
					if (!comparison && (operation is not (0x9080 or 0xD080 or 0x8080 or 0xC080) ||
						!sourceUse || destinationUse)) break;
					if (comparison && !sourceUse && !destinationUse) break;
					var rewritten = (int)use.Opcode;
					if (sourceUse) rewritten = (rewritten & ~7) | source;
					if (comparison && destinationUse) rewritten = (rewritten & ~(7 << 9)) | source << 9;
					_buffer.WriteWord(use.Offset, (ushort)rewritten);
					_buffer.RemoveBytes(copy.Offset, copy.Length);
					RecordGeneratedTransportRewrite(2);
					return true;
				}
				if ((facts.Effects.DefinesData & ((1 << source) | (1 << temporary))) != 0) break;
				changedFlags &= ~facts.Effects.WritesConditions;
				end = use.Offset + use.Length;
			}
		}
		return false;
	}

	private bool CopiedBitsAreUnobserved(M68kInstructionDataflow dataflow, int next, int register)
	{
		// Whole-register liveness conservatively reads the preserved upper bits
		// of MOVE.B/W. Track only bits originating in the removed copy, through
		// a bounded CFG proof. Every unknown use/edge rejects the transformation.
		var instructions = dataflow.Instructions;
		var byOffset = instructions.Select((instruction, index) => (instruction.Offset, index))
			.ToDictionary(item => item.Offset, item => item.index);
		var pending = new Stack<(int Index, uint Bits)>();
		var visited = new HashSet<(int Index, uint Bits)>();
		pending.Push((next, uint.MaxValue));
		while (pending.TryPop(out var state))
		{
			if (state.Bits == 0 || !visited.Add(state)) continue;
			if (visited.Count > 256 || state.Index >= instructions.Count) return false;
			var instruction = instructions[state.Index];
			if (!instruction.IsDecoded ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts)) return false;
			var bits = state.Bits;
			var effects = facts.Effects;
			var reads = (effects.UsesData & (1 << register)) != 0 ? uint.MaxValue : 0;
			uint writes = 0;
			var opcode = instruction.Opcode;
			var annotated = _assembler.TryGetInstructionEffects(instruction.Offset, out _);
			if (!annotated && instruction.Kind == M68kInstructionKind.Call &&
				!instruction.ExternalTarget && instruction.TargetOffset is { } callee &&
				LeafDoesNotObserveRegister(dataflow, byOffset, callee, register)) reads = 0;
			// Only known full definitions kill all copied bits. In particular,
			// an unclassified partial definition must never stand in for one.
			if ((effects.DefinesData & (1 << register)) != 0 &&
				(instruction.Kind == M68kInstructionKind.Call && annotated ||
				 (opcode & 0xF100) == 0x7000 ||
				 (opcode & 0xF1C0) == 0x2000 ||
				 (opcode & 0xFB80) == 0x4880 && (opcode & 0x0400) != 0)) writes = uint.MaxValue;
			if (!annotated)
			{
				var family = opcode & 0xF000;
				if (family is 0x1000 or 0x3000 && (opcode >> 6 & 7) == 0 &&
					(opcode >> 9 & 7) == register)
				{
					var width = family == 0x1000 ? 0xFFu : 0xFFFFu;
					// Only the source is observed; preserved destination bits are
					// not a read. Source index/address uses still read all 32 bits.
					reads = (opcode >> 3 & 7) == 0 ? ((opcode & 7) == register ? width : 0) : reads;
					if ((opcode >> 3 & 7) != 0 && (opcode >> 3 & 7) != 6 &&
						(opcode & 0x3F) != 0x3B) reads = 0;
					writes = width;
				}
				else if (instruction.Length == 2 && (opcode >> 3 & 7) == 0 &&
					(opcode & 0xF1C0) is 0xB000 or 0xB040)
				{
					reads = (effects.UsesData & (1 << register)) != 0
						? ((opcode & 0x40) == 0 ? 0xFFu : 0xFFFFu) : 0;
				}
			}
			if ((reads & bits) != 0) return false;
			bits &= ~writes;
			if (bits == 0) continue;
			if (instruction.Kind is M68kInstructionKind.Return || instruction.IsNonReturning) return false;
			if (instruction.Kind is M68kInstructionKind.ConditionalBranch or
				M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Dbcc)
			{
				if (instruction.ExternalTarget || instruction.TargetOffset is not { } target ||
					!byOffset.TryGetValue(target, out var targetIndex)) return false;
				pending.Push((targetIndex, bits));
				if (instruction.Kind == M68kInstructionKind.UnconditionalBranch) continue;
			}
			else if (effects.IsBarrier && instruction.Kind != M68kInstructionKind.Call) return false;
			pending.Push((state.Index + 1, bits));
		}
		return true;
	}

	private bool LeafDoesNotObserveRegister(M68kInstructionDataflow dataflow,
		IReadOnlyDictionary<int, int> byOffset, int target, int register)
	{
		// A decoded internal leaf can be more precise than the conservative
		// all-register call ABI. Do not infer through calls, saves of this
		// register, opaque instructions, annotations or unresolved exits.
		var pending = new Stack<int>(); var visited = new HashSet<int>();
		pending.Push(target);
		while (pending.TryPop(out var offset))
		{
			if (!visited.Add(offset)) continue;
			if (visited.Count > 64 || !byOffset.TryGetValue(offset, out var index)) return false;
			var instruction = dataflow.Instructions[index];
			if (!instruction.IsDecoded) return false;
			// RTS's ABI annotation keeps return/preserved registers live in its
			// own method. The instruction does not inspect data registers; their
			// actual consumers in this caller are checked by the outer CFG proof.
			if (instruction.Opcode == 0x4E75) continue;
			if (_assembler.TryGetInstructionEffects(offset, out _)) return false;
			var effects = M68kInstructionDataflow.GetEffects(instruction);
			if (((effects.UsesData | effects.DefinesData) & (1 << register)) != 0 ||
				effects.IsBarrier || instruction.IsNonReturning ||
				instruction.Kind is M68kInstructionKind.Call or M68kInstructionKind.Return) return false;
			if (instruction.Kind is M68kInstructionKind.ConditionalBranch or
				M68kInstructionKind.UnconditionalBranch or M68kInstructionKind.Dbcc)
			{
				if (instruction.ExternalTarget || instruction.TargetOffset is not { } destination) return false;
				pending.Push(destination);
				if (instruction.Kind == M68kInstructionKind.UnconditionalBranch) continue;
			}
			pending.Push(offset + instruction.Length);
		}
		return true;
	}

	private bool TryRemoveGeneratedZeroOperandTransport(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			// The expensive label and dataflow checks only matter for these two
			// opcodes. Most instructions in a ROM cannot match either rewrite.
			if ((instruction.Opcode & 0xF1F8) is not (0x8080 or 0x1000)) continue;
			if (!instruction.IsDecoded || instruction.Length != 2 || HasInternalLabel(instruction) ||
				!CanFoldAcrossUnreferencedIlLabels(instruction.Offset, instruction.Offset + instruction.Length,
					allowUnreferencedMarkers: true) ||
				!GeneratedSizeEligible(instruction.Offset, instruction.Offset + instruction.Length) ||
				_assembler.TryGetInstructionEffects(instruction.Offset, out _) ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts)) continue;
			var source = instruction.Opcode & 7;
			var destination = instruction.Opcode >> 9 & 7;
			if ((instruction.Opcode & 0xF1F8) == 0x8080 && // OR.L Ds,Dd
				dataflow.GetDataValueBefore(instruction.Offset, source).IsExact(out var zero) && zero == 0)
			{
				if ((facts.Effects.WritesConditions & facts.LiveConditionsAfter) == 0)
				{
					_buffer.RemoveBytes(instruction.Offset, 2);
					RecordGeneratedTransportRewrite(2);
				}
				else
				{
					_buffer.WriteWord(instruction.Offset, (ushort)(0x4A80 | destination)); // TST.L Dd, same NZVC and X
					RecordGeneratedTransportRewrite(0);
				}
				return true;
			}
			if ((instruction.Opcode & 0xF1F8) == 0x1000 && // MOVE.B Ds,Dd
				dataflow.GetDataValueBefore(instruction.Offset, source).IsExact(out var sourceValue) &&
				dataflow.GetDataValueBefore(instruction.Offset, destination).IsExact(out var destinationValue) &&
				(sourceValue & 0xFF) == (destinationValue & 0xFF) &&
				(facts.Effects.WritesConditions & facts.LiveConditionsAfter) == 0)
			{
				_buffer.RemoveBytes(instruction.Offset, 2);
				RecordGeneratedTransportRewrite(2);
				return true;
			}
		}
		return false;
	}
}
