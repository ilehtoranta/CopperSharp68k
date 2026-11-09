namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryRemoveQuickArithmeticTest(M68kInstructionDataflow dataflow)
    {
        var instructions = ExecutableCandidates(dataflow);
        for (int i = 0; i + 1 < instructions.Count; i++)
        {
            var arithmetic = instructions[i];
            var test = instructions[i + 1];
            bool quick = (arithmetic.Opcode & 0xF038) == 0x5000;
            bool rotate = (arithmetic.Opcode & 0xF018) == 0xE018;
            if (arithmetic.Length != 2 || !(quick || rotate) ||
                (arithmetic.Opcode & 0xC0) == 0xC0 || test.Length != 2 ||
                test.Offset != arithmetic.Offset + 2 ||
                _assembler.TryGetInstructionEffects(arithmetic.Offset, out _) ||
                _assembler.TryGetInstructionEffects(test.Offset, out _) ||
                test.Opcode != (0x4A00 | (arithmetic.Opcode & 0xC7)) ||
                IsReferencedLabelAt(test.Offset) ||
                !CanFoldAcrossUnreferencedIlLabels(arithmetic.Offset, test.Offset + 2, true) ||
                !dataflow.TryGetFacts(test.Offset, out var facts) ||
                (facts.LiveConditionsAfter & M68kConditionCodeSet.Carry) != 0 ||
                ((quick || _assembler.GeneratedCodeSizeOptions?.PreserveRotateOverflowTests == true) &&
                 (facts.LiveConditionsAfter & M68kConditionCodeSet.Overflow) != 0))
                continue;

            // NZ describe the same result and TST preserves X. Register rotates
            // also clear V, so a signed branch may consume their flags directly.
            RelocateLabels(test.Offset, arithmetic.Offset);
            _buffer.RemoveBytes(test.Offset, 2);
            return true;
        }
        return false;
    }
}
