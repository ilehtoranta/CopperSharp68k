/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryFoldDeadWordOperands(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.NarrowOperations != true) return false;
        var instructions = dataflow.Instructions;
        for (int i = 0; i + 1 < instructions.Count; i++)
        {
            var first = instructions[i];
            var next = instructions[i + 1];
            int register = first.Opcode & 7;
            if (!first.IsDecoded || !next.IsDecoded || next.Offset != first.Offset + first.Length)
                continue;
            // MOVEQ supplies a zero high word for a nonnegative literal.
            // A word move then replaces exactly the cleared part of Dn.
            if ((first.Opcode & 0xFFF8) == 0x4240 && first.Length == 2 &&
                (next.Opcode & 0xF180) == 0x7000 && next.Length == 2 &&
                i + 2 < instructions.Count)
            {
                int source = (next.Opcode >> 9) & 7;
                var combine = instructions[i + 2];
                if (source != register && combine.IsDecoded && combine.Length == 2 &&
                    combine.Offset == next.Offset + 2 &&
                    combine.Opcode == (0x8080 | (register << 9) | source) &&
                    CanRewrite(first, next) && CanRewrite(next, combine) &&
                    !IsReferencedLabelAt(next.Offset) && !IsReferencedLabelAt(combine.Offset) &&
                    dataflow.TryGetFacts(combine.Offset, out var result) &&
                    (result.LiveConditionsAfter & (M68kConditionCodeSet.Negative | M68kConditionCodeSet.Zero)) == 0)
                {
                    _buffer.WriteWord(first.Offset, next.Opcode);
                    _buffer.WriteWord(next.Offset, (ushort)(0x3000 | (register << 9) | source));
                    MoveLabelsToOffset(combine.Offset, combine.Offset + 2, next.Offset);
                    _buffer.RemoveBytes(combine.Offset, 2);
                    _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.NarrowOperations), 2);
                    return true;
                }
            }
            if ((first.Opcode & 0xFFF8) != 0x4240 || next.Opcode != (0x4840 | register) ||
                first.Length != 2 || next.Length != 2 || i + 2 >= instructions.Count) continue;
            var store = instructions[i + 2];
            // A word store sees only the original high half after SWAP.
            // Exclude indexed effective addresses which can also use Dn.
            if (!store.IsDecoded || store.Offset != next.Offset + 2 ||
                (store.Opcode & 0xF03F) != (0x3000 | register) ||
                ((store.Opcode >> 6) & 7) is not (2 or 3 or 4 or 5) ||
                !dataflow.TryGetFacts(store.Offset, out var stored) ||
                (stored.LiveDataAfter & (1 << register)) != 0 || !CanRewrite(first, next) ||
                _assembler.TryGetInstructionEffects(store.Offset, out _)) continue;
            _buffer.WriteWord(first.Offset, next.Opcode);
            MoveLabelsToOffset(next.Offset, next.Offset + 2, first.Offset);
            _buffer.RemoveBytes(next.Offset, 2);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.NarrowOperations), 2);
            return true;
        }
        return false;

        bool CanRewrite(M68kEmittedInstruction first, M68kEmittedInstruction next) =>
            GeneratedSizeEligible(first.Offset, next.Offset + next.Length) &&
            !HasAddressFixup(first) && !HasAddressFixup(next) &&
            !HasInternalLabel(first) && !HasInternalLabel(next) &&
            CanFoldAcrossUnreferencedIlLabels(first.Offset, next.Offset + next.Length) &&
            !_assembler.TryGetInstructionEffects(first.Offset, out _) &&
            !_assembler.TryGetInstructionEffects(next.Offset, out _);
    }
}
