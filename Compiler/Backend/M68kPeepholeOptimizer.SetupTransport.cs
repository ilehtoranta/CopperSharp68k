/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryFoldCopiedArgumentBitTest(M68kInstructionDataflow dataflow)
    {
        var instructions = dataflow.Instructions;
        for (int i = 0; i + 2 < instructions.Count; i++) {
            var copy = instructions[i]; var constant = instructions[i + 1]; var and = instructions[i + 2];
            int source = copy.Opcode & 7, temporary = copy.Opcode >> 9 & 7;
            int maskRegister = constant.Opcode >> 9 & 7, mask = constant.Opcode & 255;
            if (!copy.IsDecoded || !constant.IsDecoded || !and.IsDecoded ||
                copy.Length != 2 || (copy.Opcode & 0xF1F8) != 0x2000 ||
                constant.Length != 2 || (constant.Opcode & 0xF100) != 0x7000 ||
                mask == 0 || mask >= 128 || (mask & (mask - 1)) != 0 ||
                temporary == maskRegister || and.Length != 2 ||
                and.Opcode != (0xC080 | temporary << 9 | maskRegister) ||
                constant.Offset != copy.Offset + 2 || and.Offset != copy.Offset + 4 ||
                HasInternalLabel(copy) || HasInternalLabel(constant) || HasInternalLabel(and) ||
                !GeneratedSizeEligible(copy.Offset, and.Offset + 2) ||
                !CanFoldAcrossUnreferencedIlLabels(copy.Offset, and.Offset + 2) ||
                _assembler.TryGetInstructionEffects(copy.Offset, out _) ||
                _assembler.TryGetInstructionEffects(constant.Offset, out _) ||
                _assembler.TryGetInstructionEffects(and.Offset, out _) ||
                !dataflow.TryGetFacts(and.Offset, out var facts) ||
                (facts.LiveDataAfter & ((1 << temporary) | (1 << maskRegister))) != 0 ||
                (facts.LiveConditionsAfter & (M68kConditionCodeSet.Negative |
                    M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry)) != 0) continue;
            // Only Z survives; test the incoming value instead of manufacturing
            // two dead register arguments. BTST and the original sequence keep X.
            _buffer.WriteWord(copy.Offset, (ushort)(0x0800 | source));
            _buffer.WriteWord(copy.Offset + 2, (ushort)System.Numerics.BitOperations.TrailingZeroCount((uint)mask));
            MoveLabelsToOffset(copy.Offset + 2, and.Offset + 2, copy.Offset);
            _buffer.RemoveBytes(and.Offset, 2);
            RecordGeneratedTransportRewrite(2);
            return true;
        }
        return false;
    }

    private bool TryFoldMaterializedConditionBranch(M68kInstructionDataflow dataflow)
    {
        var instructions = dataflow.Instructions;
        for (int i = 0; i + 5 < instructions.Count; i++) {
            var set = instructions[i]; int register = set.Opcode & 7;
            if (set.Length != 2 || (set.Opcode & 0xF0F8) != 0x50C0 ||
                instructions[i + 1].Opcode != (0x4880 | register) ||
                instructions[i + 2].Opcode != (0x48C0 | register) ||
                instructions[i + 3].Opcode != (0x4480 | register)) continue;
            var test = instructions[i + 4]; var branch = instructions[i + 5];
            if ((test.Opcode & 0xFFF8) is not (0x4A00 or 0x4A40 or 0x4A80) ||
                (test.Opcode & 7) != register ||
                (branch.Opcode & 0xFF00) is not (0x6600 or 0x6700) ||
                !_buffer.Branches.Any(b => b.OpcodeOffset == branch.Offset) ||
                !GeneratedSizeEligible(set.Offset, branch.Offset + branch.Length) ||
                !CanFoldAcrossUnreferencedIlLabels(set.Offset, branch.Offset + branch.Length) ||
                !dataflow.TryGetFacts(branch.Offset, out var facts) ||
                (facts.LiveDataAfter & (1 << register)) != 0 ||
                facts.LiveConditionsAfter != M68kConditionCodeSet.None) continue;
            bool contiguous = true;
            for (int n = 0; n <= 5; n++) {
                var instruction = instructions[i + n];
                if (!instruction.IsDecoded || instruction.Offset != set.Offset + n * 2 ||
                    n < 5 && instruction.Length != 2 || HasInternalLabel(instruction) ||
                    _assembler.TryGetInstructionEffects(instruction.Offset, out _)) contiguous = false;
            }
            if (!contiguous) continue;
            // Test(canonical Scc) only consumes its truth value. With the value
            // and all post-branch CCR bits dead, branch on the original condition.
            int condition = (set.Opcode >> 8) & 15;
            if ((branch.Opcode & 0xFF00) == 0x6700) condition ^= 1;
            // ST/SF would require turning a conditional fixup into BRA/removal.
            if (condition < 2) continue;
            _buffer.WriteWord(branch.Offset, (ushort)((branch.Opcode & 0xF0FF) | condition << 8));
            MoveLabelsToOffset(set.Offset + 2, branch.Offset + branch.Length, set.Offset);
            _buffer.RemoveBytes(set.Offset, 10);
            RecordGeneratedTransportRewrite(10);
            return true;
        }
        return false;
    }

    private bool TryFoldLongConstantCompare(M68kInstructionDataflow dataflow)
    {
        var instructions = dataflow.Instructions;
        for (int i = 0; i + 1 < instructions.Count; i++) {
            var move = instructions[i]; var compare = instructions[i + 1];
            int constant = move.Opcode >> 9 & 7, destination = compare.Opcode >> 9 & 7;
            if (!move.IsDecoded || !compare.IsDecoded || move.Length != 6 ||
                (move.Opcode & 0xF1FF) != 0x203C || compare.Length != 2 ||
                (compare.Opcode & 0xF1F8) != 0xB080 || (compare.Opcode & 7) != constant ||
                destination == constant || compare.Offset != move.Offset + 6 ||
                HasAddressFixup(move) || HasAddressFixup(compare) ||
                HasInternalLabel(move) || HasInternalLabel(compare) ||
                !GeneratedSizeEligible(move.Offset, compare.Offset + 2) ||
                !CanFoldAcrossUnreferencedIlLabels(move.Offset, compare.Offset + 2) ||
                _assembler.TryGetInstructionEffects(move.Offset, out _) ||
                _assembler.TryGetInstructionEffects(compare.Offset, out _) ||
                !dataflow.TryGetFacts(compare.Offset, out var facts) ||
                (facts.LiveDataAfter & (1 << constant)) != 0) continue;
            // Both forms compare the same 32-bit values and preserve X. Keep
            // the immediate in place, eliminating its dead transport register.
            _buffer.WriteWord(move.Offset, (ushort)(0x0C80 | destination));
            MoveLabelsToOffset(move.Offset + 2, compare.Offset + 2, move.Offset);
            _buffer.RemoveBytes(compare.Offset, 2);
            RecordGeneratedTransportRewrite(2);
            return true;
        }
        return false;
    }
}
