/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
    internal int RunGeneratedHelperInlining()
    {
        var rewrites = 0;
        if (_assembler.GeneratedCodeSizeOptions?.InlineMemoryHelpers != true) return 0;
        while (rewrites < _rewriteBudget) {
            _assembler.BeginInstructionAnalysisRound();
            bool changed;
            try { changed = TryInlineGeneratedPrivateHelper(M68kInstructionDataflow.Analyze(_assembler)); }
            finally { _assembler.EndInstructionAnalysisRound(); }
            if (!changed) break;
            rewrites++;
        }
        return rewrites;
    }
    // Inline only a complete leaf body with one final RTS, no stack dependence,
    // no external branches and no address fixups. Internal branches and multiple
    // memory accesses are cloned in order. Retain every address-taken entry.
    private bool TryInlineGeneratedPrivateHelper(M68kInstructionDataflow dataflow)
    {
        if (_assembler.GeneratedCodeSizeOptions?.InlineMemoryHelpers != true) return false;
        foreach (var range in _assembler.GeneratedPrivateHelperRanges) {
            if (!_buffer.Labels.TryGetValue(range.StartLabel, out var start) ||
                !_buffer.Labels.TryGetValue(range.EndLabel, out var end) || end - start is < 4 or > 64) continue;
            var body = dataflow.Instructions.Where(i => i.Offset >= start && i.Offset < end).ToArray();
            if (body.Length == 0 || body[^1].Opcode != 0x4e75 || body[^1].Offset + 2 != end ||
                body.Take(body.Length - 1).Any(i => !i.IsDecoded || i.Kind is M68kInstructionKind.Call or M68kInstructionKind.Return or M68kInstructionKind.Dbcc ||
                    !dataflow.TryGetFacts(i.Offset, out var facts) || facts.Effects.IsBarrier ||
                    (facts.Effects.UsesAddress & 0x80) != 0 || (facts.Effects.DefinesAddress & 0x80) != 0 ||
                    (facts.Effects.DefinesAddress & 0x60) != 0 || facts.Effects.StackDelta != 0 ||
                    _assembler.TryGetInstructionEffects(i.Offset, out _) ||
                    i.TargetOffset is { } target && (target < start || target >= end)) ||
                _buffer.Addresses.Any(a => a.Offset >= start && a.Offset < end) ||
                _buffer.PcRelative.Any(a => a.DisplacementOffset >= start && a.DisplacementOffset < end)) continue;
            var labels = _buffer.Labels.Where(p => p.Value >= start && p.Value < end).Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            if (_buffer.Labels.Any(p => p.Value >= start && p.Value < end && !body.Any(i => i.Offset == p.Value)) ||
                _assembler.HasRequestedAlignmentInRange(start, end)) continue;
            var calls = dataflow.Instructions.Where(i => i.Kind == M68kInstructionKind.Call && i.TargetOffset == start && !i.ExternalTarget).ToArray();
            if (calls.Length == 0 || calls.Any(c => !GeneratedSizeEligible(c.Offset, c.Offset + c.Length) || c.Offset >= start && c.Offset < end)) continue;
            // Absolute data references and non-call branch references preserve
            // function identity, including references to internal labels.
            if (_buffer.Addresses.Any(a => labels.Contains(a.Target) &&
                    !calls.Any(c => c.Offset + 2 == a.Offset)) ||
                _buffer.PcRelative.Any(a => labels.Contains(a.Target) &&
                    !calls.Any(c => c.Offset + 2 == a.DisplacementOffset)) ||
                _buffer.Branches.Any(b => labels.Contains(b.Target) &&
                    (b.OpcodeOffset < start || b.OpcodeOffset >= end) && !calls.Any(c => c.Offset == b.OpcodeOffset))) continue;
            var copiedBytes = end - start - 2;
            var saving = end - start + calls.Sum(c => c.Length - copiedBytes);
            if (saving <= 0) continue;
            var words = Enumerable.Range(0, copiedBytes / 2).Select(index => _buffer.ReadWord(start + index * 2)).ToArray();
            var localLabels = _buffer.Labels.Where(p => p.Value >= start && p.Value < end).ToArray();
            var localBranches = _buffer.Branches.Where(b => b.OpcodeOffset >= start && b.OpcodeOffset < end - 2).ToArray();
            // Work in reverse physical order; then rediscover the source range
            // through labels after the caller insertions shifted its offsets.
            foreach (var call in calls.OrderByDescending(c => c.Offset)) {
                var prefix = "generated-inline:" + _assembler.GeneratedCodeSizeStatistics.GetValueOrDefault(nameof(M68kCodeSizeOptions.InlineMemoryHelpers)).Rewrites + ":" + call.Offset + ":";
                var names = localLabels.ToDictionary(p => p.Key, p => prefix + p.Key);
                var anchors = _buffer.AnalysisAnchors.Where(p => p.Value >= call.Offset && p.Value <= call.Offset + call.Length).ToArray();
                _buffer.RemoveBytes(call.Offset, call.Length);
                _buffer.InsertBytes(call.Offset, copiedBytes, shiftBoundaryLabels: false, shiftBoundaryAnchors: true);
                for (var index = 0; index < words.Length; index++) _buffer.WriteWord(call.Offset + index * 2, words[index]);
                foreach (var label in localLabels) _buffer.Labels[names[label.Key]] = call.Offset + label.Value - start;
                foreach (var anchor in anchors) _buffer.AnalysisAnchors[anchor.Key] =
                    anchor.Value == call.Offset + call.Length ? call.Offset + copiedBytes : call.Offset;
                foreach (var branch in localBranches) _buffer.Branches.Add(branch with {
                    OpcodeOffset = call.Offset + branch.OpcodeOffset - start, Target = names[branch.Target] });
            }
            start = _buffer.Labels[range.StartLabel]; end = _buffer.Labels[range.EndLabel];
            _buffer.RemoveBytes(start, end - start);
            // Removed internal labels cannot denote bytes in the following body.
            foreach (var label in localLabels) _buffer.Labels[label.Key] = start;
            _assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.InlineMemoryHelpers), saving);
            return true;
        }
        return false;
    }
}
