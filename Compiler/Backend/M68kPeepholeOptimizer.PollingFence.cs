namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    // Discarded word read followed by a byte-bit polling loop at one constant
    // address. Absolute operands need no scratch registers. Keep both accesses,
    // their widths/order and the word read's NZVC through every byte test.
    private bool TryFoldAbsolutePollingFence(M68kInstructionDataflow dataflow)
    {
        var ins = dataflow.Instructions;
        for (int i = 0; i + 6 < ins.Count; i++) {
            var address = ins[i]; var zero = ins[i + 1]; var read = ins[i + 2];
            var reload = ins[i + 3]; var bit = ins[i + 4]; var branch = ins[i + 5];
            int an = address.Opcode >> 9 & 7, dn = zero.Opcode >> 9 & 7;
            if (!address.IsDecoded || (address.Opcode & 0xF1FF) != 0x207C || an == 7 ||
                address.Length != 6 || zero.Opcode != (0x7000 | dn << 9) || zero.Length != 2 ||
                read.Opcode != (0x3010 | dn << 9 | an) || read.Length != 2 ||
                reload.Opcode != address.Opcode || reload.Length != 6 || reload.ExtensionLong != address.ExtensionLong ||
                bit.Opcode != (0x0810 | an) || bit.Length != 4 || bit.ExtensionWord >= 8 ||
                (branch.Opcode & 0xFF00) != 0x6600 || branch.TargetOffset != reload.Offset ||
                !TryGetBranch(branch.Offset, out var fixup) ||
                !GeneratedSizeEligible(address.Offset, branch.Offset + branch.Length) ||
                !dataflow.TryGetFacts(branch.Offset + branch.Length, out var exit) ||
                (exit.LiveDataBefore & (1 << dn)) != 0 || (exit.LiveAddressBefore & (1 << an)) != 0)
                continue;
            var span = ins.Skip(i).Take(6).ToArray();
            if (span.Any(x => !x.IsDecoded || HasAddressFixup(x) ||
                    _assembler.TryGetInstructionEffects(x.Offset, out _)) ||
                span.Zip(span.Skip(1)).Any(pair => pair.First.Offset + pair.First.Length != pair.Second.Offset)) continue;
            int start = address.Offset, end = branch.Offset + branch.Length;
            bool Interior(string label, int addend = 0) => _buffer.Labels.TryGetValue(label, out int at) &&
                (long)at + addend > start && (long)at + addend < end;
            // Only this loop backedge may enter the sequence. Analysis/IL markers
            // are harmless; named method entries and address-taken interiors are not.
            if (_buffer.Branches.Any(b => b != fixup && Interior(b.Target)) ||
                _buffer.Addresses.Any(a => Interior(a.Target, a.Addend)) ||
                _buffer.PcRelative.Any(p => Interior(p.Target)) ||
                _unrelocatableLabelNames.Any(n => n != fixup.Target && Interior(n)) ||
                !CanFoldAcrossUnreferencedIlLabels(start + 13, start + 20, allowUnreferencedMarkers: true)) continue;
            uint absolute = address.ExtensionLong;
            _buffer.WriteWord(start, 0x4A79); // TST.W absolute.L
            _buffer.WriteWord(start + 2, (int)(absolute >> 16));
            _buffer.WriteWord(start + 4, (int)(absolute & 0xFFFF));
            _buffer.WriteWord(start + 6, 0x0839); // BTST #bit,absolute.L
            _buffer.WriteWord(start + 8, bit.ExtensionWord);
            _buffer.WriteWord(start + 10, (int)(absolute >> 16));
            _buffer.WriteWord(start + 12, (int)(absolute & 0xFFFF));
            _buffer.Labels[fixup.Target] = start + 6;
            _buffer.RemoveBytes(start + 14, 6);
            RecordGeneratedTransportRewrite(6);
            return true;
        }
        return false;
    }
}
