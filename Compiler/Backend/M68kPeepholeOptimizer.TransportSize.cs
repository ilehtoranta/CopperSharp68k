/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryRemoveGeneratedTransport(M68kInstructionDataflow dataflow)
	{
		if (TryCompactPrivateStackCopies(dataflow)) return true;
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
			if (selfStack && dataflow.TryGetFacts(move.Offset, out var selfFacts))
			{
				if ((selfFacts.Effects.WritesConditions & selfFacts.LiveConditionsAfter) == 0)
				{
					_buffer.RemoveBytes(move.Offset, move.Length);
					_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.RemoveRedundantTransport), move.Length);
				}
				else
				{
					// A private self-store changes no bytes. TST keeps its exact
					// NZVC result and preserves X, using only one displacement.
					var size = family == 0x1000 ? 0 : family == 0x3000 ? 0x40 : 0x80;
					_buffer.WriteWord(move.Offset, (ushort)(0x4A2F | size));
					_buffer.RemoveBytes(move.Offset + 4, 2);
					_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.RemoveRedundantTransport), 2);
				}
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
	private bool TryCompactPrivateStackCopies(M68kInstructionDataflow dataflow)
	{
		if (_assembler.GeneratedCodeSizeOptions?.CompactPrivateStackCopies != true) return false;
		var instructions = dataflow.Instructions;
		for (var i = 0; i + 1 < instructions.Count; i++)
		{
			var load = instructions[i];
			var store = instructions[i + 1];
			var family = load.Opcode & 0xF000;
			var bank = load.Opcode >> 6 & 7;
			var register = load.Opcode >> 9 & 7;
			if (!load.IsDecoded || !store.IsDecoded || family is not (0x1000 or 0x2000 or 0x3000) ||
				load.Length != 4 || store.Length != 4 || store.Offset != load.Offset + 4 ||
				(load.Opcode & 0x3F) != 0x2F || bank > 1 || family == 0x1000 && bank == 1 ||
				bank == 1 && (family != 0x2000 || register == 7) ||
				(store.Opcode & 0xF000) != family || (store.Opcode >> 6 & 7) != 5 ||
				(store.Opcode >> 9 & 7) != 7 || (store.Opcode & 0x3F) != (bank << 3 | register) ||
				unchecked((short)load.ExtensionWord) < 0 || unchecked((short)store.ExtensionWord) < 0 ||
				HasInternalLabel(load) || HasFrameTransferBoundary(store) ||
				!GeneratedSizeEligible(load.Offset, store.Offset + store.Length) ||
				_assembler.TryGetInstructionEffects(load.Offset, out _) ||
				_assembler.TryGetInstructionEffects(store.Offset, out _) ||
				!dataflow.TryGetFacts(store.Offset, out var facts) ||
				((bank == 1 ? facts.LiveAddressAfter : facts.LiveDataAfter) & (1 << register)) != 0) continue;
			// MOVE memory-to-memory reads before writing and establishes identical
			// NZVC while preserving X. Address-register word sign extension is excluded.
			_buffer.WriteWord(load.Offset, (ushort)(family | 7 << 9 | 5 << 6 | 0x2F));
			_buffer.WriteWord(load.Offset + 4, store.ExtensionWord);
			_buffer.RemoveBytes(load.Offset + 6, 2);
			_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.CompactPrivateStackCopies), 2);
			return true;
		}
		return false;
	}
}
