/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryDropWordMultiplyMasks(M68kInstructionDataflow dataflow)
    {
        var instructions = ExecutableCandidates(dataflow);
        for (int index = 1; index < instructions.Count; index++) {
            var mul = instructions[index];
            if (mul.Length != 2 || (mul.Opcode & 0xF1F8) is not (0xC0C0 or 0xC1C0) ||
                _assembler.TryGetInstructionEffects(mul.Offset, out _)) continue;
            var op = instructions[index - 1];
            int destination = (mul.Opcode >> 9) & 7;
            // MUL[SU].W ignores both input upper halves and defines the full destination.
            if (op.Opcode == (0x0280 | destination) && op.Length == 6 &&
                op.ExtensionLong == 0xffff && op.Offset + op.Length == mul.Offset &&
                !_assembler.TryGetInstructionEffects(op.Offset, out _) &&
                CanFoldAcrossUnreferencedIlLabels(op.Offset, mul.Offset + mul.Length, true)) {
                RelocateLabels(op.Offset, mul.Offset);
                _buffer.RemoveBytes(op.Offset, op.Length);
                return true;
            }
            int source = mul.Opcode & 7;
            if (index < 2 || op.Opcode != (0x4840 | source) || op.Length != 2 ||
                op.Offset + 2 != mul.Offset || !dataflow.TryGetFacts(mul.Offset, out var facts) ||
                source != destination && (facts.LiveDataAfter & (1 << source)) != 0) continue;
            var clear = instructions[index - 2];
            if (clear.Opcode != (0x4240 | source) || clear.Length != 2 || clear.Offset + 2 != op.Offset ||
                _assembler.TryGetInstructionEffects(clear.Offset, out _) ||
                _assembler.TryGetInstructionEffects(op.Offset, out _) ||
                !CanFoldAcrossUnreferencedIlLabels(clear.Offset, mul.Offset + mul.Length, true)) continue;
            // SWAP moves the discarded low half to the unobserved upper half.
            RelocateLabels(clear.Offset, op.Offset);
            _buffer.RemoveBytes(clear.Offset, 2);
            return true;
        }
        return false;
    }
}
