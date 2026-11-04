namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    private bool TryFoldNativeRamStoreRun(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.FoldNativeRamUpdates != true) return false;
        var instructions = ExecutableCandidates(dataflow);
        for (int i = 0; i + 2 < instructions.Count; i++)
        {
            var first = instructions[i];
            int family = first.Opcode & 0xF000, address = (first.Opcode >> 9) & 7;
            int width = family == 0x1000 ? 1 : family == 0x3000 ? 2 : family == 0x2000 ? 4 : 0;
            if (width == 0 || address == 7 || first.Length != 2 ||
                (first.Opcode & 0x01F8) != 0x0080) continue;
            int next = i + 1, bytes = width, end = first.Offset + first.Length;
            while (next < instructions.Count && bytes <= short.MaxValue)
            {
                var store = instructions[next];
                if (store.Offset != end || store.Length != 4 ||
                    (store.Opcode & 0xFFF8) != (family | (address << 9) | 0x140) ||
                    _buffer.ReadWord(store.Offset + 2) != bytes) break;
                bytes += width; end += store.Length; next++;
            }
            if (next == i + 1 || next == instructions.Count) continue;
            var update = instructions[next];
            bool quick = bytes <= 8 && update.Length == 2 &&
                update.Opcode == (0x5088 | ((bytes & 7) << 9) | address);
            bool lea = bytes <= short.MaxValue && update.Length == 4 &&
                update.Opcode == (0x41E8 | (address << 9) | address) &&
                _buffer.ReadWord(update.Offset + 2) == bytes;
            if (update.Offset != end || !(quick || lea) ||
                !GeneratedSizeEligible(first.Offset, end + update.Length) ||
                !CanFoldAcrossUnreferencedIlLabels(first.Offset, end + update.Length, true) ||
                instructions.Skip(i).Take(next - i + 1).Any(x =>
                    (x.Offset != first.Offset && IsReferencedLabelAt(x.Offset)) ||
                    HasAddressFixup(x) || _buffer.PcRelative.Any(f =>
                        f.DisplacementOffset >= x.Offset && f.DisplacementOffset < x.Offset + x.Length) ||
                    _assembler.TryGetInstructionEffects(x.Offset, out _))) continue;

            // The opted-in native RAM run writes the same widths, values and
            // ascending addresses in the same order. Each MOVE retains its CCR;
            // LEA/ADDQ to An never changes it. Only the final An is observable:
            // no interior entry or explicit effect is allowed, and A7 is excluded.
            int removed = update.Length + 2 * (next - i - 1);
            RelocateLabels(update.Offset, first.Offset);
            _buffer.RemoveBytes(update.Offset, update.Length);
            for (int j = next - 1; j >= i; j--)
            {
                var store = instructions[j];
                _buffer.WriteWord(store.Offset, family | (address << 9) | 0xC0 | (store.Opcode & 7));
                if (store.Length == 4) _buffer.RemoveBytes(store.Offset + 2, 2);
            }
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.FoldNativeRamUpdates), removed);
            return true;
        }
        return false;
    }

    private bool TryFoldNativeRamStoreIncrement(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.FoldNativeRamUpdates != true) return false;
        var instructions = ExecutableCandidates(dataflow);
        for (int i = 0; i + 1 < instructions.Count; i++)
        {
            var store = instructions[i]; var add = instructions[i + 1];
            int family = store.Opcode & 0xF000, address = (store.Opcode >> 9) & 7;
            int size = family == 0x1000 ? 1 : family == 0x3000 ? 2 : family == 0x2000 ? 4 : 0;
            if (size == 0 || address == 7 || store.Length != 2 ||
                (store.Opcode & 0x01F8) != 0x0080 ||
                add.Offset != store.Offset + 2 || add.Length != 2 ||
                add.Opcode != (0x5088 | (size << 9) | address) ||
                !GeneratedSizeEligible(store.Offset, add.Offset + 2) ||
                !CanFoldAcrossUnreferencedIlLabels(store.Offset, add.Offset + 2, true) ||
                IsReferencedLabelAt(add.Offset) ||
                _assembler.TryGetInstructionEffects(store.Offset, out _) ||
                _assembler.TryGetInstructionEffects(add.Offset, out _)) continue;
            // MOVE Dn,(An)+ writes the same width/address/value and leaves the
            // same An/CCR as MOVE Dn,(An); ADDQ.L #width,An. A7 is excluded.
            _buffer.WriteWord(store.Offset, store.Opcode | 0x40);
            RelocateLabels(add.Offset, store.Offset);
            _buffer.RemoveBytes(add.Offset, 2);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.FoldNativeRamUpdates), 2);
            return true;
        }
        return false;
    }

    private bool TryCarryNativeRowAddress(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.FoldNativeRamUpdates != true) return false;
        var instructions = ExecutableCandidates(dataflow);
        for (int i = 0; i + 4 < instructions.Count; i++)
        {
            var head = instructions[i]; var memory = instructions[i + 1];
            var add = instructions[i + 2]; var branch = instructions[i + 3];
            int pointer = head.Opcode & 7, address = (head.Opcode >> 9) & 7;
            int stride = add.Opcode & 7;
            if (head.Length != 2 || (head.Opcode & 0xF1F8) != 0x2040 || address == 7 ||
                memory.Offset != head.Offset + 2 || memory.Length != 2 ||
                (memory.Opcode & 0x013F) != (0x110 | address) || (memory.Opcode & 0xC0) == 0xC0 ||
                (memory.Opcode & 0xF000) is not (0x8000 or 0xB000 or 0xC000) ||
                ((memory.Opcode >> 9) & 7) == pointer ||
                add.Offset != memory.Offset + 2 || add.Length != 2 ||
                add.Opcode != (0xD080 | (pointer << 9) | stride) || stride == pointer ||
                branch.Offset != add.Offset + 2 || branch.Length != 4 ||
                (branch.Opcode & 0xFFF8) != 0x51C8 || (branch.Opcode & 7) == pointer ||
                branch.TargetOffset != head.Offset || !TryGetBranch(branch.Offset, out var fixup) ||
                !GeneratedSizeEligible(head.Offset, branch.Offset + branch.Length) ||
                !CanFoldAcrossUnreferencedIlLabels(head.Offset, branch.Offset + branch.Length, true) ||
                IsReferencedLabelAt(memory.Offset) || IsReferencedLabelAt(add.Offset) || IsReferencedLabelAt(branch.Offset) ||
                !dataflow.TryGetFacts(branch.Offset + branch.Length, out var exit) ||
                (exit.LiveAddressBefore & (1 << address)) != 0 ||
                IsReferencedLabelAt(branch.Offset + branch.Length) ||
                exit.LiveConditionsBefore != M68kConditionCodeSet.None ||
                new[] { head, memory, add, branch }.Any(x => _assembler.TryGetInstructionEffects(x.Offset, out _))) continue;
            // Retain the initial MOVEA on entry, but carry An on the back edge.
            // The dead exit pointer/CCR proof permits replacing ADD with ADDA.
            string label = "generated:native-row-address:" + head.Offset;
            if (_buffer.Labels.ContainsKey(label)) continue;
            _buffer.Labels[label] = memory.Offset;
            int branchIndex = _buffer.Branches.IndexOf(fixup);
            if (branchIndex < 0) throw new InvalidOperationException("Missing countdown branch fixup.");
            _buffer.Branches[branchIndex] = fixup with { Target = label };
            _buffer.WriteWord(add.Offset, 0xD1C0 | (address << 9) | stride);
            if ((exit.LiveDataBefore & (1 << pointer)) != 0)
            {
                int exitOffset = branch.Offset + branch.Length;
                _buffer.InsertBytes(exitOffset, 2);
                _buffer.WriteWord(exitOffset, 0x2008 | (pointer << 9) | address);
            }
            return true;
        }
        return false;
    }

    private bool TryFoldNativeRamUpdates(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.FoldNativeRamUpdates != true) return false;
        var instructions = ExecutableCandidates(dataflow);
        for (int i = 0; i + 2 < instructions.Count; i++)
        {
            var load = instructions[i]; var op = instructions[i + 1]; var store = instructions[i + 2];
            int family = load.Opcode & 0xF000;
            if (family is not (0x1000 or 0x2000 or 0x3000) ||
                (load.Opcode & 0x01F8) != 0x0010 || load.Length != 2 || op.Length != 2 || store.Length != 2 ||
                op.Offset != load.Offset + 2 || store.Offset != op.Offset + 2 ||
                !GeneratedSizeEligible(load.Offset, store.Offset + 2) ||
                !CanFoldAcrossUnreferencedIlLabels(load.Offset, store.Offset + 2, true) ||
                IsReferencedLabelAt(op.Offset) || IsReferencedLabelAt(store.Offset) ||
                _assembler.TryGetInstructionEffects(load.Offset, out _) ||
                _assembler.TryGetInstructionEffects(op.Offset, out _) ||
                _assembler.TryGetInstructionEffects(store.Offset, out _) ||
                !dataflow.TryGetFacts(store.Offset, out var facts)) continue;
            int temporary = (load.Opcode >> 9) & 7, address = load.Opcode & 7;
            int size = family == 0x1000 ? 0 : family == 0x3000 ? 1 : 2;
            if (store.Opcode != (family | (address << 9) | 0x80 | temporary) ||
                (facts.LiveDataAfter & (1 << temporary)) != 0) continue;
            int operation = op.Opcode & 0xF000;
            int source;
            if (operation is 0x8000 or 0xC000 &&
                (op.Opcode & 0x01F8) == (size << 6) && ((op.Opcode >> 9) & 7) == temporary)
                source = op.Opcode & 7;
            else if (operation == 0xB000 && (op.Opcode & 0x01FF) == (0x100 | (size << 6) | temporary))
                source = (op.Opcode >> 9) & 7;
            else continue;
            if (source == temporary) continue;
            // Exactly one read and write of the same width/address, no pointer
            // update. Logical ALU and the original final MOVE have identical CCR.
            _buffer.WriteWord(load.Offset, (ushort)(operation | (source << 9) | 0x100 | (size << 6) | 0x10 | address));
            RelocateLabels(op.Offset, load.Offset);
            RelocateLabels(store.Offset, load.Offset);
            _buffer.RemoveBytes(op.Offset, 4);
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.FoldNativeRamUpdates), 4);
            return true;
        }
        return false;
    }
}
