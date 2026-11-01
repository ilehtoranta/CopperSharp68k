namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryFoldWordCountdown(M68kInstructionDataflow dataflow)
    {
        var instructions = ExecutableCandidates(dataflow);
        for (int i = 0; i + 2 < instructions.Count; i++)
        {
            var decrement = instructions[i]; var constant = instructions[i + 1];
            bool immediate = constant.Length == 4 && (constant.Opcode & 0xFFF8) == 0x0C40 &&
                _buffer.ReadWord(constant.Offset + 2) == 0xFFFF;
            if (!immediate && i + 3 >= instructions.Count) continue;
            var compare = immediate ? constant : instructions[i + 2];
            var branch = instructions[i + (immediate ? 2 : 3)];
            int counter = decrement.Opcode & 7, scratch = (constant.Opcode >> 9) & 7;
            if (decrement.Length != 2 || (decrement.Opcode & 0xFFF8) != 0x5340 ||
                constant.Offset != decrement.Offset + 2 ||
                (immediate ? (compare.Opcode & 7) != counter :
                    constant.Length != 6 || (constant.Opcode & 0xF1FF) != 0x203C ||
                    _buffer.ReadLong(constant.Offset + 2) != 0xFFFF ||
                    compare.Offset != constant.Offset + 6 || compare.Length != 2 ||
                    compare.Opcode != (0xB040 | (counter << 9) | scratch) || counter == scratch) ||
                branch.Offset != compare.Offset + compare.Length || branch.Kind != M68kInstructionKind.ConditionalBranch ||
                (branch.Opcode & 0xFF00) != 0x6600 || branch.Length is not (2 or 4) ||
                branch.TargetOffset is not { } target || target > decrement.Offset ||
                target - (decrement.Offset + 2) < short.MinValue ||
                !TryGetBranch(branch.Offset, out _) ||
                !CanFoldAcrossUnreferencedIlLabels(decrement.Offset, branch.Offset + branch.Length, true) ||
                IsReferencedLabelAt(constant.Offset) || IsReferencedLabelAt(compare.Offset) || IsReferencedLabelAt(branch.Offset) ||
                _assembler.TryGetInstructionEffects(decrement.Offset, out _) ||
                _assembler.TryGetInstructionEffects(constant.Offset, out _) ||
                _assembler.TryGetInstructionEffects(compare.Offset, out _) ||
                _assembler.TryGetInstructionEffects(branch.Offset, out _) ||
                !dataflow.TryGetFacts(branch.Offset, out var facts) ||
                facts.LiveConditionsAfter != M68kConditionCodeSet.None ||
                (!immediate && (facts.LiveDataAfter & (1 << scratch)) != 0)) continue;
            // ushort decrement wraps exactly like DBRA's low-word update. Only
            // the -1 sentinel is recognized; this is not a 32-bit trip-count guess.
            RelocateLabels(constant.Offset, decrement.Offset);
            RelocateLabels(compare.Offset, decrement.Offset);
            RelocateLabels(branch.Offset, decrement.Offset);
            _buffer.RemoveBytes(decrement.Offset, branch.Offset - decrement.Offset);
            if (branch.Length == 2) _buffer.InsertBytes(decrement.Offset + 2, 2);
            _buffer.WriteWord(decrement.Offset, 0x51C8 | counter);
            _buffer.WriteWord(decrement.Offset + 2, 0);
            return true;
        }
        return false;
    }
}
