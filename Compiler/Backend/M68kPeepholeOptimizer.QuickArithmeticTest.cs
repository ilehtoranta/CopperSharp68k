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
            // ADDQ/SUBQ to Dn, byte/word/long. Address arithmetic does not set CCR.
            if (arithmetic.Length != 2 || (arithmetic.Opcode & 0xF038) != 0x5000 ||
                (arithmetic.Opcode & 0xC0) == 0xC0 || test.Length != 2 ||
                test.Offset != arithmetic.Offset + 2 ||
                _assembler.TryGetInstructionEffects(arithmetic.Offset, out _) ||
                _assembler.TryGetInstructionEffects(test.Offset, out _) ||
                test.Opcode != (0x4A00 | (arithmetic.Opcode & 0xC7)) ||
                IsReferencedLabelAt(test.Offset) ||
                !CanFoldAcrossUnreferencedIlLabels(arithmetic.Offset, test.Offset + 2, true) ||
                !dataflow.TryGetFacts(test.Offset, out var facts) ||
                (facts.LiveConditionsAfter & (M68kConditionCodeSet.Carry | M68kConditionCodeSet.Overflow)) != 0)
                continue;

            // NZ describe the identical-width result; TST preserves X. C/V are
            // different, so their complete CFG liveness must permit removal.
            RelocateLabels(test.Offset, arithmetic.Offset);
            _buffer.RemoveBytes(test.Offset, 2);
            return true;
        }
        return false;
    }
}
