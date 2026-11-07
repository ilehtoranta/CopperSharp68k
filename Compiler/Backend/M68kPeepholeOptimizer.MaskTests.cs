/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryNarrowDeadMaskTest(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.NarrowOperations != true) return false;
        var instructions = dataflow.Instructions;
        for (var n = 0; n + 1 < instructions.Count; n++)
        {
            var constant = instructions[n]; var and = instructions[n + 1];
            var quick = constant.Length == 2 && (constant.Opcode & 0xF100) == 0x7000;
            var wide = constant.Length == 6 && (constant.Opcode & 0xF1FF) == 0x203C;
            var mask = quick ? unchecked((uint)(sbyte)constant.Opcode) : constant.ExtensionLong;
            var source = constant.Opcode >> 9 & 7; var destination = and.Opcode >> 9 & 7;
            if (!(quick || wide) || mask > 0x7FFF || !constant.IsDecoded || !and.IsDecoded ||
                and.Length != 2 || and.Offset != constant.Offset + constant.Length ||
                (and.Opcode & 0xF1F8) != 0xC080 || (and.Opcode & 7) != source || source == destination ||
                !GeneratedSizeEligible(constant.Offset, and.Offset + 2) ||
                HasAddressFixup(constant) || HasAddressFixup(and) || HasInternalLabel(constant) || HasInternalLabel(and) ||
                !CanFoldAcrossUnreferencedIlLabels(constant.Offset, and.Offset + 2) ||
                _assembler.TryGetInstructionEffects(constant.Offset, out _) || _assembler.TryGetInstructionEffects(and.Offset, out _) ||
                !dataflow.TryGetFacts(and.Offset, out var facts) ||
                (facts.LiveDataAfter & ((1 << source) | (1 << destination))) != 0) continue;
            // Materialized C# constants use the same mask test. Both scalar
            // results are dead; retain every flag while dropping the temporary.
            MoveLabelsToOffset(constant.Offset + 2, and.Offset + 2, constant.Offset);
            _buffer.WriteWord(constant.Offset, (ushort)(0x0240 | destination));
            _buffer.WriteWord(constant.Offset + 2, (ushort)mask);
            var removed = and.Offset + 2 - constant.Offset - 4;
            if (removed != 0) _buffer.RemoveBytes(constant.Offset + 4, removed);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.NarrowOperations), removed);
            return true;
        }
        foreach (var instruction in dataflow.Instructions)
        {
            if (!instruction.IsDecoded || instruction.Length != 6 ||
                (instruction.Opcode & 0xFFF8) != 0x0280 || instruction.ExtensionLong > 0x7FFF ||
                !GeneratedSizeEligible(instruction.Offset, instruction.Offset + 6) ||
                HasAddressFixup(instruction) || HasInternalLabel(instruction) ||
                !CanFoldAcrossUnreferencedIlLabels(instruction.Offset, instruction.Offset + 6) ||
                _assembler.TryGetInstructionEffects(instruction.Offset, out _) ||
                !dataflow.TryGetFacts(instruction.Offset, out var facts) ||
                (facts.LiveDataAfter & (1 << (instruction.Opcode & 7))) != 0) continue;
            // A small nonnegative AND mask makes N=0 and computes exactly the
            // same Z in both widths. V/C are cleared and X is retained. Only
            // the dead upper half of Dn differs, so live branch flags are safe.
            _buffer.WriteWord(instruction.Offset, (ushort)(0x0240 | (instruction.Opcode & 7)));
            MoveLabelsToOffset(instruction.Offset + 2, instruction.Offset + 6, instruction.Offset);
            _buffer.RemoveBytes(instruction.Offset + 2, 2);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.NarrowOperations), 2);
            return true;
        }
        return false;
    }
}
