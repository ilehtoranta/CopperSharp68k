/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryFoldPostincrementLongCopy(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.CompactGuestMemory != true) return false;
        var instructions = dataflow.Instructions;
        for (var n = 0; n + 1 < instructions.Count; n++)
        {
            var load = instructions[n]; var store = instructions[n + 1];
            var temporary = load.Opcode >> 9 & 7;
            if (!load.IsDecoded || !store.IsDecoded || load.Length != 2 || store.Length != 2 ||
                (load.Opcode & 0xF1F8) != 0x2018 || (store.Opcode & 0xF1F8) != 0x20C0 ||
                (store.Opcode & 7) != temporary || store.Offset != load.Offset + 2 ||
                (load.Opcode & 7) == 7 || (store.Opcode >> 9 & 7) == 7 ||
                (load.Opcode & 7) == (store.Opcode >> 9 & 7) ||
                !GeneratedSizeEligible(load.Offset, store.Offset + 2) ||
                !CanFoldAcrossUnreferencedIlLabels(load.Offset, store.Offset + 2) ||
                HasInternalLabel(load) || HasInternalLabel(store) || HasAddressFixup(load) || HasAddressFixup(store) ||
                _assembler.TryGetInstructionEffects(load.Offset, out _) || _assembler.TryGetInstructionEffects(store.Offset, out _) ||
                !dataflow.TryGetFacts(store.Offset, out var facts) || (facts.LiveDataAfter & (1 << temporary)) != 0) continue;
            // The combined MOVE reads and advances its source before writing
            // and advancing its destination. Its final CCR matches the store;
            // only the dead temporary definition is removed.
            MoveLabelsToOffset(store.Offset, store.Offset + 2, load.Offset);
            _buffer.WriteWord(load.Offset, (ushort)((store.Opcode & 0xFFC0) | (load.Opcode & 0x3F)));
            _buffer.RemoveBytes(store.Offset, 2);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.CompactGuestMemory), 2);
            return true;
        }
        return false;
    }

    private bool TryFoldLongMemoryPointerAdvance(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.CompactGuestMemory != true) return false;
        var instructions = dataflow.Instructions;
        for (var n = 0; n + 1 < instructions.Count; n++)
        {
            var move = instructions[n]; var advance = instructions[n + 1];
            var load = (move.Opcode >> 3 & 7) == 2 && (move.Opcode >> 6 & 7) <= 1;
            var store = (move.Opcode >> 6 & 7) == 2 && (move.Opcode >> 3 & 7) <= 1;
            var address = load ? move.Opcode & 7 : move.Opcode >> 9 & 7;
            if (!move.IsDecoded || !advance.IsDecoded || (move.Opcode & 0xF000) != 0x2000 ||
                move.Length != 2 || !(load || store) || address == 7 ||
                load && (move.Opcode >> 6 & 7) == 1 && (move.Opcode >> 9 & 7) == address ||
                store && (move.Opcode >> 3 & 7) == 1 && (move.Opcode & 7) == address ||
                advance.Offset != move.Offset + 2 ||
                !(advance.Opcode == (0x5888 | address) && advance.Length == 2 ||
                  advance.Opcode == (0x41E8 | address << 9 | address) && advance.Length == 4 && advance.ExtensionWord == 4) ||
                !GeneratedSizeEligible(move.Offset, advance.Offset + advance.Length) ||
                !CanFoldAcrossUnreferencedIlLabels(move.Offset, advance.Offset + advance.Length) ||
                HasInternalLabel(move) || HasInternalLabel(advance) || HasAddressFixup(move) || HasAddressFixup(advance) ||
                _assembler.TryGetInstructionEffects(move.Offset, out _) || _assembler.TryGetInstructionEffects(advance.Offset, out _)) continue;
            // MOVE's ordinary postincrement performs the same longword access
            // and address update. ADDQ/LEA on An do not change CCR, so every
            // flag and all register values remain identical.
            MoveLabelsToOffset(advance.Offset, advance.Offset + advance.Length, move.Offset);
            _buffer.WriteWord(move.Offset, (ushort)(move.Opcode | (load ? 8 : 64)));
            _buffer.RemoveBytes(advance.Offset, advance.Length);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.CompactGuestMemory), advance.Length);
            return true;
        }
        return false;
    }

    private bool TryGroupLoadedLongMemoryCopy(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.CompactGuestMemory != true) return false;
        var instructions = dataflow.Instructions;
        for (var start = 0; start + 5 < instructions.Count; start++)
        {
            var first = instructions[start];
            if (!TryLoad(first, out var source, out var sourceOffset, out _)) continue;
            var count = 0;
            ushort temporaries = 0;
            while (start + count < instructions.Count && count < 8 &&
                TryLoad(instructions[start + count], out var nextSource, out var offset, out var temporary) &&
                nextSource == source && offset == sourceOffset + count * 4 &&
                (temporaries & (1 << temporary)) == 0)
            {
                temporaries |= (ushort)(1 << temporary);
                count++;
            }
            if (count < 3 || start + count * 2 > instructions.Count || (temporaries & 0x8000) != 0 ||
                (temporaries & (1 << (source + 8))) != 0) continue;
            var store = instructions[start + count];
            if (!TryStore(store, out var destination, out var destinationOffset, out _) ||
                (temporaries & (1 << (destination + 8))) != 0) continue;
            var valid = true;
            for (var n = 0; n < count; n++)
            {
                TryLoad(instructions[start + n], out _, out _, out var temporary);
                if (!TryStore(instructions[start + count + n], out var nextDestination, out var offset, out var stored) ||
                    nextDestination != destination || offset != destinationOffset + n * 4 || stored != temporary)
                { valid = false; break; }
            }
            var last = instructions[start + count * 2 - 1];
            if (!valid || !GeneratedSizeEligible(first.Offset, last.Offset + last.Length) ||
                !CanFoldAcrossUnreferencedIlLabels(first.Offset, last.Offset + last.Length) ||
                !dataflow.TryGetFacts(last.Offset, out var facts) ||
                (facts.LiveDataAfter & (temporaries & 255)) != 0 ||
                (facts.LiveAddressAfter & (temporaries >> 8)) != 0 ||
                (facts.LiveConditionsAfter & ~M68kConditionCodeSet.Extend) != M68kConditionCodeSet.None) continue;
            for (var n = 0; n < count * 2; n++)
            {
                var instruction = instructions[start + n];
                if (HasAddressFixup(instruction) || _assembler.TryGetInstructionEffects(instruction.Offset, out _) ||
                    HasInternalLabel(instruction) ||
                    n != 0 && instruction.Offset != instructions[start + n - 1].Offset + instructions[start + n - 1].Length)
                { valid = false; break; }
            }
            if (!valid) continue;

            // Keep scalar loads: MC68000 MOVEM loads perform an additional
            // source read. Permute only the original, dead temporaries into
            // MOVEM register order, then group their stores. This preserves
            // every memory access (including overlap) without a new register
            // or ABI clobber. NZVC must be dead; both sequences preserve X.
            var nextRegister = 0;
            for (var n = 0; n < count; n++)
            {
                while ((temporaries & (1 << nextRegister)) == 0) nextRegister++;
                var load = instructions[start + n];
                _buffer.WriteWord(load.Offset, (ushort)((load.Opcode & ~0x0FC0) |
                    (nextRegister & 7) << 9 | (nextRegister >> 3) << 6));
                nextRegister++;
            }
            MoveLabelsToOffset(store.Offset, last.Offset + last.Length, store.Offset);
            var end = WriteStoreMovem(store.Offset, destination, destinationOffset, temporaries);
            _buffer.RemoveBytes(end, last.Offset + last.Length - end);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.CompactGuestMemory), last.Offset + last.Length - end);
            return true;
        }
        return false;

        bool TryLoad(M68kEmittedInstruction instruction, out int address, out int offset, out int temporary)
        {
            address = instruction.Opcode & 7;
            offset = 0;
            temporary = (instruction.Opcode >> 9 & 7) + (instruction.Opcode >> 6 & 7) * 8;
            var mode = instruction.Opcode >> 3 & 7;
            if (!instruction.IsDecoded || (instruction.Opcode & 0xF000) != 0x2000 ||
                (instruction.Opcode >> 6 & 7) > 1 || mode is not (2 or 5) ||
                instruction.Length != (mode == 5 ? 4 : 2)) return false;
            if (mode == 5) offset = unchecked((short)instruction.ExtensionWord);
            return true;
        }

        bool TryStore(M68kEmittedInstruction instruction, out int address, out int offset, out int temporary)
        {
            address = instruction.Opcode >> 9 & 7;
            offset = 0;
            temporary = (instruction.Opcode & 7) + (instruction.Opcode >> 3 & 7) * 8;
            var mode = instruction.Opcode >> 6 & 7;
            if (!instruction.IsDecoded || (instruction.Opcode & 0xF000) != 0x2000 ||
                (instruction.Opcode >> 3 & 7) > 1 || mode is not (2 or 5) ||
                instruction.Length != (mode == 5 ? 4 : 2)) return false;
            if (mode == 5) offset = unchecked((short)instruction.ExtensionWord);
            return true;
        }

        int WriteStoreMovem(int at, int address, int offset, ushort mask)
        {
            _buffer.WriteWord(at, (ushort)(0x48C0 | (offset == 0 ? 2 : 5) << 3 | address));
            _buffer.WriteWord(at + 2, mask);
            if (offset == 0) return at + 4;
            _buffer.WriteWord(at + 4, unchecked((ushort)offset));
            return at + 6;
        }
    }
}
