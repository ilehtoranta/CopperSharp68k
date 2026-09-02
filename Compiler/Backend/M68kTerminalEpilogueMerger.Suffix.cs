namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kTerminalEpilogueMerger
{
    // Only explicit stack restoration before RTS is considered. No arithmetic,
    // guest access, call, PC-relative operand or opaque instruction is moved.
    private IEnumerable<Candidate> FindLocalEpilogueSuffixCandidates()
    {
        if (!_enableStackRestoreSuffixReuse)
            yield break;
        var instructions = _assembler.GetExecutableInstructionStream();
        var fixups = _buffer.Branches.Select(b => b.OpcodeOffset)
            .Concat(_buffer.Addresses.Select(a => a.Offset))
            .Concat(_buffer.PcRelative.Select(p => p.DisplacementOffset))
            .Concat(_buffer.InstructionEffectOverrides.Keys)
            .Concat(_buffer.AnalysisAnchors.Values).Distinct().Order().ToArray();
        for (var returnIndex = 0; returnIndex < instructions.Count; returnIndex++)
        {
            var terminal = instructions[returnIndex];
            if (!terminal.IsDecoded || terminal.Opcode != 0x4e75 ||
                terminal.Kind != M68kInstructionKind.Return) continue;
            var startIndex = returnIndex;
            var end = terminal.Offset + terminal.Length;
            while (startIndex > 0)
            {
                var current = instructions[startIndex];
                var previous = instructions[startIndex - 1];
                if (HasLabelAt(current.Offset) || previous.Offset + previous.Length != current.Offset ||
                    end - previous.Offset > 64 || !IsStackRestoreInstruction(previous)) break;
                startIndex--;
            }
            var start = instructions[startIndex].Offset;
            if (end - start <= 4 || ContainsOffset(fixups, start, end) ||
                _assembler.HasRequestedAlignmentInRange(start, end)) continue;
            // Conservatively retain blocks with externally addressable entries.
            var blockStart = startIndex;
            while (blockStart > 0 && !HasLabelAt(instructions[blockStart].Offset) &&
                instructions[blockStart - 1].Kind == M68kInstructionKind.Normal)
                blockStart--;
            if (HasAddressTakenEntry(_labelsByOffset.GetValueOrDefault(
                instructions[blockStart].Offset, Array.Empty<string>()))) continue;
            for (var candidateIndex = startIndex; candidateIndex < returnIndex; candidateIndex++)
            {
                var offset = instructions[candidateIndex].Offset;
                if (end - offset <= 4) break;
                var labels = _labelsByOffset.GetValueOrDefault(offset, Array.Empty<string>());
                if (HasAddressTakenEntry(labels)) continue;
                TryGetInvertingPredecessor(instructions, candidateIndex, offset, end, labels,
                    out var predecessor, out var continuation);
                yield return new Candidate(offset, end,
                    _buffer.Bytes.GetRange(offset, end - offset).ToArray(), labels,
                    predecessor, continuation);
            }
        }
    }

    private static bool IsStackRestoreInstruction(M68kEmittedInstruction instruction)
    {
        if (!instruction.IsDecoded || instruction.Kind != M68kInstructionKind.Normal ||
            M68kInstructionDataflow.GetEffects(instruction).IsBarrier) return false;
        var opcode = instruction.Opcode;
        if (opcode is 0x4cdf or 0x4cd7 or 0x4cef) return true; // MOVEM.L from SP.
        if ((opcode & 0xf1ff) is 0x201f or 0x205f or 0x202f or 0x206f)
            return true; // MOVE.L/MOVEA.L (SP)+ or d16(SP),register.
        if ((opcode & 0xf1ff) == 0x508f) return true; // ADDQ.L #1..8,SP.
        if (opcode == 0x4fef) return (short)instruction.ExtensionWord > 0; // LEA d16(SP),SP.
        if (opcode == 0xdefc) return (short)instruction.ExtensionWord > 0; // ADDA.W #n,SP.
        if (opcode == 0xdffc) return instruction.ExtensionLong is > 0 and <= int.MaxValue;
        return opcode is >= 0x4e58 and <= 0x4e5e; // UNLK A0..A6.
    }
}
