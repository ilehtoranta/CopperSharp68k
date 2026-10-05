/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryRemoveGeneratedTransport(M68kInstructionDataflow dataflow)
	{
		if (_assembler.GeneratedCodeSizeOptions?.RemoveRedundantTransport != true) return false;
		var instructions = dataflow.Instructions;
		for (var i = 0; i < instructions.Count; i++)
		{
			var move = instructions[i];
			if (!move.IsDecoded || !GeneratedSizeEligible(move.Offset, move.Offset + move.Length) ||
				HasInternalLabel(move) || _assembler.TryGetInstructionEffects(move.Offset, out _)) continue;
			var family = move.Opcode & 0xF000;
			// A self-move at an entry remains equivalent after retargeting that entry
			// to the next instruction. RemoveBytes preserves labels at the start.
			var selfStack = move.Length == 6 && family is 0x1000 or 0x2000 or 0x3000 &&
				(move.Opcode & 0x1FF) == 0x16F && (move.Opcode >> 9 & 7) == 7 &&
				_buffer.ReadWord(move.Offset + 2) == _buffer.ReadWord(move.Offset + 4);
			if (selfStack && dataflow.TryGetFacts(move.Offset, out var selfFacts) &&
				(selfFacts.Effects.WritesConditions & selfFacts.LiveConditionsAfter) == 0)
			{
				_buffer.RemoveBytes(move.Offset, move.Length);
				_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.RemoveRedundantTransport), move.Length);
				return true;
			}
			if (i + 1 == instructions.Count) continue;
			var next = instructions[i + 1];
			if (!next.IsDecoded || next.Offset != move.Offset + move.Length ||
				!GeneratedSizeEligible(move.Offset, next.Offset + next.Length) || HasFrameTransferBoundary(next) ||
				_assembler.TryGetInstructionEffects(next.Offset, out _)) continue;
			// Only full MOVE.L register definitions qualify. Partial writes use the
			// old upper bits. MOVE and MOVEQ preserve X and replace NZVC completely.
			var fullRegisterMove = move.Length == 2 && (move.Opcode & 0xF1C0) == 0x2000 &&
				(move.Opcode >> 3 & 7) <= 1;
			if (fullRegisterMove && dataflow.TryGetFacts(next.Offset, out var nextFacts))
			{
				var destination = move.Opcode >> 9 & 7;
				var fullNext = next.Length == 2 && ((next.Opcode & 0xF1C0) == 0x2000 && (next.Opcode >> 3 & 7) <= 1 || (next.Opcode & 0xF100) == 0x7000) &&
					(next.Opcode >> 9 & 7) == destination;
				if (fullNext && (nextFacts.Effects.UsesData & (1 << destination)) == 0 &&
					nextFacts.Effects.ReadsConditions == 0)
				{
					_buffer.RemoveBytes(move.Offset, move.Length);
					_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.RemoveRedundantTransport), move.Length);
					return true;
				}
			}
			// MOVE.[L/W/B] private-SP-slot,Rn; MOVE same Rn,same slot.
			// The first MOVE already established identical NZVC; MOVEA does not,
			// so require dead flags for its writeback. No alternative store entry.
			if (family is not (0x1000 or 0x2000 or 0x3000) ||
				(move.Opcode >> 6 & 7) > 1 || family == 0x1000 && (move.Opcode >> 6 & 7) == 1 ||
				(move.Opcode & 7) != 7 || (move.Opcode >> 3 & 7) is not (2 or 5)) continue;
			var mode = move.Opcode >> 3 & 7;
			if (move.Length != (mode == 5 ? 4 : 2) || next.Length != move.Length ||
				(next.Opcode & 0xF000) != family || (next.Opcode >> 9 & 7) != 7 ||
				(next.Opcode >> 6 & 7) != mode || (next.Opcode & 7) != (move.Opcode >> 9 & 7) ||
				(next.Opcode >> 3 & 7) != (move.Opcode >> 6 & 7) ||
				mode == 5 && next.ExtensionWord != move.ExtensionWord ||
				!dataflow.TryGetFacts(next.Offset, out var storeFacts) ||
				(move.Opcode >> 6 & 7) == 1 && (storeFacts.Effects.WritesConditions & storeFacts.LiveConditionsAfter) != 0)
				continue;
			_buffer.RemoveBytes(next.Offset, next.Length);
			_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.RemoveRedundantTransport), next.Length);
			return true;
		}
		return false;
	}
}
