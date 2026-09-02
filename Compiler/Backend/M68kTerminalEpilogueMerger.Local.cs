/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Security.Cryptography;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kTerminalEpilogueMerger
{
	private sealed record LocalMethodRange(string Label, int Start, int End);
	private sealed record LocalGroup(Candidate Kept, string Label, IReadOnlyList<Candidate> Copies);

	private Statistics RunMethodLocalReuse()
	{
		if (_assembler.MethodLocalTerminalRanges.Count == 0) return Statistics.Empty;
		// The preceding global pass has changed offsets. Rebuild the indices from
		// that exact buffer; do not reuse any pre-edit instruction or label facts.
		_labelsByOffset = _buffer.Labels.GroupBy(static label => label.Value)
			.ToDictionary(static group => group.Key,
				static group => group.Select(static label => label.Key).Order(StringComparer.Ordinal).ToArray());
		_branchReferencedLabels = _buffer.Branches.Select(static branch => branch.Target)
			.ToHashSet(StringComparer.Ordinal);
		_addressTakenLabels = _buffer.Addresses.Where(static fixup => !fixup.External)
			.Select(static fixup => fixup.Target).Concat(_buffer.PcRelative.Select(static fixup => fixup.Target))
			.ToHashSet(StringComparer.Ordinal);
		var ranges = _assembler.MethodLocalTerminalRanges
			.Where(range => _buffer.Labels.ContainsKey(range.StartLabel) && _buffer.Labels.ContainsKey(range.EndLabel))
			.Select(range => new LocalMethodRange(range.StartLabel,
				_buffer.Labels[range.StartLabel], _buffer.Labels[range.EndLabel]))
			.Where(range => range.Start >= 0 && range.End > range.Start &&
				range.End <= (_buffer.DataStartOffset ?? _buffer.Bytes.Count))
			.GroupBy(static range => (range.Start, range.End))
			.Select(static group => group.OrderBy(static range => range.Label, StringComparer.Ordinal).First())
			.OrderBy(static range => range.Start).ToArray();
		var ambiguous = new HashSet<LocalMethodRange>();
		for (var index = 0; index + 1 < ranges.Length; index++)
		{
			if (ranges[index].End <= ranges[index + 1].Start) continue;
			ambiguous.Add(ranges[index]);
			ambiguous.Add(ranges[index + 1]);
		}
		var starts = ranges.Select(static range => range.Start).ToArray();
		var instructionByOffset = _assembler.GetExecutableInstructionStream()
			.ToDictionary(static instruction => instruction.Offset);
		var anchorOffsets = _buffer.AnalysisAnchors.Values.Distinct().Order().ToArray();
		var effectOffsets = _buffer.InstructionEffectOverrides.Keys.Order().ToArray();
		var candidates = new List<(LocalMethodRange Method, Candidate Candidate)>();
		foreach (var candidate in FindCandidates().Concat(FindLocalEpilogueSuffixCandidates())
            .GroupBy(static candidate => (candidate.Start, candidate.End))
            .Select(static group => group.OrderBy(candidate => candidate.InvertingBranchOffset is null ? 1 : 0).First()))
		{
			var index = Array.BinarySearch(starts, candidate.Start);
			if (index < 0) index = ~index - 1;
			if (index < 0) continue;
			var range = ranges[index];
			if (ambiguous.Contains(range) || candidate.Start <= range.Start || candidate.End > range.End ||
				candidate.EntryLabels.Any(static label => label.StartsWith("__c68k_", StringComparison.Ordinal) ||
                    label.StartsWith("__m68k_", StringComparison.Ordinal)) ||
				!IsSafeLocalCandidate(candidate, instructionByOffset, anchorOffsets, effectOffsets)) continue;
			candidates.Add((range, candidate));
		}
		var groups = new List<LocalGroup>();
        var claimedReturns = new HashSet<int>();
        // A return belongs to at most one selected group. This prevents nested
        // suffix removals and avoids adding a second branch to any return path.
		foreach (var group in candidates.GroupBy(static item =>
			(item.Method.Label, Signature: Convert.ToHexString(item.Candidate.Signature)))
            .OrderByDescending(group => (group.Count() - 1) * (group.First().Candidate.Signature.Length - 4))
            .ThenByDescending(group => group.First().Candidate.Signature.Length)
            .ThenBy(group => group.Key.Label, StringComparer.Ordinal)
            .ThenBy(group => group.Key.Signature, StringComparer.Ordinal))
		{
			var copies = group.Select(static item => item.Candidate)
                .Where(candidate => !claimedReturns.Contains(candidate.End)).ToArray();
			if (copies.Length < 2) continue;
			// Keep an existing fallthrough exit where possible. No extra copy is
			// appended, and no other method's frame or entry contract is borrowed.
			var kept = copies.OrderBy(static candidate => candidate.InvertingBranchOffset is null ? 0 : 1)
				.ThenByDescending(static candidate => candidate.Start).First();
			var selected = copies.Where(candidate => candidate != kept &&
				(candidate.InvertingBranchOffset is not null || candidate.End - candidate.Start > 4) &&
				CanReachExistingTail(candidate, kept.Start) &&
				(candidate.InvertingBranchOffset is not null || candidate.EntryLabels.Count == 0 ||
				 candidate.EntryLabels.Any(IsBranchReferenced))).ToArray();
			if (selected.Length == 0) continue;
			var label = "__m68k_local_epilogue_" + kept.Start.ToString("X8") + "_" +
				Convert.ToHexString(SHA256.HashData(kept.Signature).AsSpan(0, 8));
			if (_buffer.Labels.ContainsKey(label)) continue;
			groups.Add(new(kept, label, selected));
            claimedReturns.Add(kept.End);
            foreach (var candidate in selected) claimedReturns.Add(candidate.End);
		}
		if (groups.Count == 0) return Statistics.Empty;

		var removals = new List<(int Offset, int Count)>();
		var aliases = new List<(string Label, string Target)>();
		var anchors = new List<(string Anchor, string Target)>();
		var inverted = 0;
		var trampolines = 0;
		var gross = 0;
		var added = 0;
		var beforeBytes = _buffer.Bytes.Count;
		foreach (var group in groups)
		{
			_buffer.Labels.Add(group.Label, group.Kept.Start);
			foreach (var candidate in group.Copies)
			{
				var length = candidate.End - candidate.Start;
				gross += length;
				if (candidate.InvertingBranchOffset is { } predecessor)
				{
					InvertAndRetargetBranch(predecessor, group.Label);
					removals.Add((candidate.Start, length));
					aliases.AddRange(candidate.EntryLabels.Select(label => (label, group.Label)));
					anchors.AddRange(_buffer.AnalysisAnchors.Where(anchor => anchor.Value == candidate.Start)
						.Select(anchor => (anchor.Key, group.Label)));
					inverted++;
				}
				else
				{
					_buffer.WriteWord(candidate.Start, 0x6000);
					_buffer.WriteWord(candidate.Start + 2, 0);
					_buffer.Branches.Add(new BranchFixup(candidate.Start, group.Label));
					removals.Add((candidate.Start + 4, length - 4));
					trampolines++;
					added += 4;
				}
			}
		}
		_buffer.RemoveByteRanges(removals.OrderBy(static range => range.Offset).ToArray());
		foreach (var alias in aliases) _buffer.Labels[alias.Label] = _buffer.Labels[alias.Target];
		foreach (var anchor in anchors) _buffer.AnalysisAnchors[anchor.Anchor] = _buffer.Labels[anchor.Target];
		if (beforeBytes - _buffer.Bytes.Count != gross - added)
			throw new InvalidOperationException("Method-local terminal byte accounting is inconsistent.");
		return new(groups.Count, groups.Sum(static group => group.Copies.Count), inverted,
			trampolines, gross, added, gross - added);
	}

	private bool IsSafeLocalCandidate(Candidate candidate,
		IReadOnlyDictionary<int, M68kEmittedInstruction> instructions,
		int[] anchorOffsets, int[] effectOffsets)
	{
		if (_assembler.HasRequestedAlignmentInRange(candidate.Start, candidate.End) ||
			ContainsOffset(anchorOffsets, candidate.Start + 1, candidate.End) ||
			ContainsOffset(effectOffsets, candidate.Start, candidate.End) ||
			candidate.InvertingBranchOffset is { } branch && _buffer.InstructionEffectOverrides.ContainsKey(branch))
			return false;
		var offset = candidate.Start;
		while (offset < candidate.End)
		{
			if (!instructions.TryGetValue(offset, out var instruction)) return false;
			// Also reject raw PC-relative encodings without a recorded fixup. This
			// intentionally permits false negatives for immediate-only opcodes.
			if ((instruction.Opcode & 0x3F) is 0x3A or 0x3B ||
				instruction.Opcode != 0x4E75 && M68kInstructionDataflow.GetEffects(instruction).IsBarrier)
				return false;
			offset += instruction.Length;
		}
		return offset == candidate.End;
	}

	private static bool ContainsOffset(int[] sorted, int start, int end)
	{
		var index = Array.BinarySearch(sorted, start);
		if (index < 0) index = ~index;
		return index < sorted.Length && sorted[index] < end;
	}

	private static bool CanReachExistingTail(Candidate candidate, int target)
	{
		var source = candidate.InvertingBranchOffset ?? candidate.Start;
		var displacement = (long)target - (source + 2);
		// Other selected ranges only remove bytes between these points. Word
		// reach therefore remains valid after the single compaction batch.
		return displacement >= short.MinValue && displacement <= short.MaxValue;
	}
}
