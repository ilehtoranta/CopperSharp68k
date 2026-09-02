/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Security.Cryptography;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Performs the one whole-image transformation which is intentionally outside
/// method-local dataflow: identical, self-contained RTS blocks are moved to
/// cold generated tails and their original entries become branches.
/// </summary>
internal sealed partial class M68kTerminalEpilogueMerger
{
	internal readonly record struct Statistics(
		int Groups,
		int MergedCopies,
		int InvertedBranches,
		int Trampolines,
		int GrossBytesRemoved,
		int BranchBytesAdded,
		int NetBytesSaved)
	{
		internal static Statistics Empty => default;
	}

	private sealed record Candidate(
		int Start,
		int End,
		byte[] Signature,
		IReadOnlyList<string> EntryLabels,
		int? InvertingBranchOffset,
		string? ContinuationLabel);

	private sealed record PlannedCopy(
		Candidate Candidate,
		string TailLabel,
		bool InvertBranch);

	private sealed record PlannedGroup(
		byte[] Signature,
		string TailLabel,
		IReadOnlyList<PlannedCopy> Copies);

	private sealed record RegionalGroup(
		Candidate Kept,
		string TailLabel,
		IReadOnlyList<Candidate> Copies);

	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;
	private readonly bool _enableMethodLocalReuse;
	private readonly bool _enableStackRestoreSuffixReuse;
	private readonly bool _enableRegionalReuse;
	private Dictionary<int, string[]> _labelsByOffset = new();
	private HashSet<string> _branchReferencedLabels = new(StringComparer.Ordinal);
	private HashSet<string> _addressTakenLabels = new(StringComparer.Ordinal);

	public M68kTerminalEpilogueMerger(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer)
		: this(assembler, buffer, false, false, false)
	{
	}

	internal M68kTerminalEpilogueMerger(
		M68kAssembler assembler, M68kAssemblyBuffer buffer, bool enableMethodLocalReuse,
		bool enableStackRestoreSuffixReuse = false,
		bool enableRegionalReuse = false)
	{
		_assembler = assembler;
		_buffer = buffer;
		_enableMethodLocalReuse = enableMethodLocalReuse;
		_enableStackRestoreSuffixReuse = enableStackRestoreSuffixReuse;
		_enableRegionalReuse = enableRegionalReuse;
	}

	public Statistics Run()
	{
		// Preserve the cheapest decisions first. Method-local reuse can replace a
		// terminal copy with a four-byte word branch (or an inverted predecessor),
		// while regional reuse needs a fixed six-byte absolute JMP. Let regional
		// reuse consider only the copies that remain after the local pass.
		var global = RunGlobal();
		var local = _enableMethodLocalReuse
			? RunMethodLocalReuse()
			: Statistics.Empty;
		var regional = _enableRegionalReuse
			? RunRegionalGlobalReuse()
			: Statistics.Empty;
		return Add(Add(global, local), regional);
	}

	private static Statistics Add(Statistics left, Statistics right) => new(
		left.Groups + right.Groups,
		left.MergedCopies + right.MergedCopies,
		left.InvertedBranches + right.InvertedBranches,
		left.Trampolines + right.Trampolines,
		left.GrossBytesRemoved + right.GrossBytesRemoved,
		left.BranchBytesAdded + right.BranchBytesAdded,
		left.NetBytesSaved + right.NetBytesSaved);

	private Statistics RunGlobal()
	{
		RefreshIndexes();
		var candidates = FindCandidates();
		if (candidates.Count < 2)
		{
			return Statistics.Empty;
		}

		var groups = PlanGroups(candidates);
		if (groups.Count == 0)
		{
			return Statistics.Empty;
		}

		// This is deliberately one edit batch.  All offsets in the plan refer to
		// the same pre-edit image; RemoveByteRanges then fixes every later label
		// and fixup in one pass.
		var removals = new List<(int Offset, int Count)>();
		var aliases = new List<(string Label, string TailLabel)>();
		var invertedBranches = 0;
		var trampolines = 0;
		var grossBytesRemoved = 0;
		var branchBytesAdded = 0;
		foreach (var group in groups)
		{
			foreach (var copy in group.Copies)
			{
				var candidate = copy.Candidate;
				grossBytesRemoved += candidate.End - candidate.Start;
				if (copy.InvertBranch)
				{
					InvertAndRetargetBranch(copy.Candidate.InvertingBranchOffset!.Value, copy.TailLabel);
					removals.Add((candidate.Start, candidate.End - candidate.Start));
					foreach (var label in candidate.EntryLabels)
					{
						aliases.Add((label, copy.TailLabel));
					}
					invertedBranches++;
				}
				else
				{
					// A word BRA has the same fixed representation that the normal
					// assembler emits.  Final branch relaxation remains authoritative.
					_buffer.WriteWord(candidate.Start, 0x6000);
					_buffer.WriteWord(candidate.Start + 2, 0);
					_buffer.Branches.Add(new BranchFixup(candidate.Start, copy.TailLabel));
					removals.Add((candidate.Start + 4, candidate.End - candidate.Start - 4));
					trampolines++;
					branchBytesAdded += 4;
				}
			}
		}

		_buffer.RemoveByteRanges(removals.OrderBy(static range => range.Offset).ToArray());
		var insertionOffset = _buffer.DataStartOffset ?? _buffer.Bytes.Count;
		var dataBoundaryLabels = _buffer.DataStartOffset is null
			? Array.Empty<string>()
			: _buffer.Labels
				.Where(label => label.Value == insertionOffset)
				.Select(static label => label.Key)
				.ToArray();
		var tailBytes = groups.Sum(static group => group.Signature.Length);
		_buffer.InsertBytes(insertionOffset, tailBytes);
		var cursor = insertionOffset;
		foreach (var group in groups)
		{
			_buffer.Labels.Add(group.TailLabel, cursor);
			for (var index = 0; index < group.Signature.Length; index++)
			{
				_buffer.Bytes[cursor + index] = group.Signature[index];
			}
			cursor += group.Signature.Length;
		}
		foreach (var label in dataBoundaryLabels)
		{
			_buffer.Labels[label] += tailBytes;
		}
		foreach (var (label, tailLabel) in aliases)
		{
			_buffer.Labels[label] = _buffer.Labels[tailLabel];
		}

		return new(
			groups.Count,
			groups.Sum(static group => group.Copies.Count),
			invertedBranches,
			trampolines,
			grossBytesRemoved,
			branchBytesAdded,
			grossBytesRemoved - branchBytesAdded - tailBytes);
	}

	private Statistics RunRegionalGlobalReuse()
	{
		// The generated global tail deliberately stays close to the end of code,
		// which excludes terminal blocks in the rest of a large ROM. Reuse an
		// existing byte-identical block through a fixed absolute JMP. A word branch
		// is not stable across final alignment: a later relaxation before its source
		// can be absorbed by an aligned label between source and target, increasing
		// the displacement again. The six-byte transfer remains smaller than every
		// admitted regional candidate and is independent of final layout distance.
		RefreshIndexes();
		var instructions = _assembler.GetExecutableInstructionStream()
			.ToDictionary(static instruction => instruction.Offset);
		var anchorOffsets = _buffer.AnalysisAnchors.Values.Distinct().Order().ToArray();
		var effectOffsets = _buffer.InstructionEffectOverrides.Keys.Order().ToArray();
		var candidates = FindCandidates()
			.Where(candidate => IsSafeLocalCandidate(
				candidate,
				instructions,
				anchorOffsets,
				effectOffsets))
			.ToArray();
		var groups = PlanRegionalGroups(candidates);
		if (groups.Count == 0)
		{
			return Statistics.Empty;
		}

		var removals = new List<(int Offset, int Count)>();
		var trampolines = 0;
		var grossBytesRemoved = 0;
		var branchBytesAdded = 0;
		var beforeBytes = _buffer.Bytes.Count;
		foreach (var group in groups)
		{
			_buffer.Labels.Add(group.TailLabel, group.Kept.Start);
			foreach (var candidate in group.Copies)
			{
				var length = candidate.End - candidate.Start;
				grossBytesRemoved += length;
				_buffer.WriteWord(candidate.Start, 0x4EF9); // JMP abs.l
				_buffer.WriteWord(candidate.Start + 2, 0);
				_buffer.WriteWord(candidate.Start + 4, 0);
				_buffer.Addresses.Add(new AddressFixup(
					candidate.Start + 2,
					group.TailLabel,
					External: false,
					Addend: 0,
					CanRelaxToPcRelative: false));
				removals.Add((candidate.Start + 6, length - 6));
				trampolines++;
				branchBytesAdded += 6;
			}
		}

		_buffer.RemoveByteRanges(removals.OrderBy(static range => range.Offset).ToArray());
		if (beforeBytes - _buffer.Bytes.Count !=
			grossBytesRemoved - branchBytesAdded)
		{
			throw new InvalidOperationException(
				"Regional terminal byte accounting is inconsistent.");
		}

		return new(
			groups.Count,
			groups.Sum(static group => group.Copies.Count),
			0,
			trampolines,
			grossBytesRemoved,
			branchBytesAdded,
			grossBytesRemoved - branchBytesAdded);
	}

	private List<RegionalGroup> PlanRegionalGroups(
		IReadOnlyList<Candidate> candidates)
	{
		var result = new List<RegionalGroup>();
		foreach (var signatureGroup in candidates
			.GroupBy(static candidate => Convert.ToHexString(candidate.Signature),
				StringComparer.Ordinal)
			.OrderBy(static group => group.Key, StringComparer.Ordinal))
		{
			var remaining = signatureGroup
				.OrderBy(static candidate => candidate.Start)
				.ToList();
			while (remaining.Count > 1)
			{
				var plan = remaining
					.Select(kept =>
					{
						var copies = remaining.Where(candidate =>
							candidate != kept &&
							CanReuseRegionalCandidate(candidate))
							.ToArray();
						var saving = copies.Sum(static candidate =>
							candidate.End - candidate.Start -
							6);
						return (Kept: kept, Copies: copies, Saving: saving);
					})
					.Where(static plan => plan.Saving > 0)
					.OrderByDescending(static plan => plan.Saving)
					.ThenByDescending(static plan => plan.Copies.Length)
					.ThenBy(static plan => plan.Kept.Start)
					.Cast<(Candidate Kept, Candidate[] Copies, int Saving)?>()
					.FirstOrDefault();
				if (plan is null)
				{
					break;
				}

				var selected = plan.Value;
				var tailLabel = "__m68k_regional_epilogue_" +
					selected.Kept.Start.ToString("X8") + "_" +
					Convert.ToHexString(SHA256.HashData(
						selected.Kept.Signature).AsSpan(0, 8));
				if (_buffer.Labels.ContainsKey(tailLabel))
				{
					break;
				}
				result.Add(new(selected.Kept, tailLabel, selected.Copies));
				remaining.Remove(selected.Kept);
				foreach (var copy in selected.Copies)
				{
					remaining.Remove(copy);
				}
			}
		}
		return result;
	}

	private bool CanReuseRegionalCandidate(Candidate candidate)
	{
		if (candidate.End - candidate.Start <= 6)
		{
			return false;
		}
		if (candidate.InvertingBranchOffset is null)
		{
			if (candidate.EntryLabels.Count != 0 &&
				!candidate.EntryLabels.Any(IsBranchReferenced))
			{
				return false;
			}
		}
		return true;
	}

	private void RefreshIndexes()
	{
		_labelsByOffset = _buffer.Labels
			.GroupBy(static label => label.Value)
			.ToDictionary(
				static group => group.Key,
				static group => group
					.Select(static label => label.Key)
					.OrderBy(static label => label, StringComparer.Ordinal)
					.ToArray());
		_branchReferencedLabels = _buffer.Branches
			.Select(static branch => branch.Target)
			.ToHashSet(StringComparer.Ordinal);
		_addressTakenLabels = _buffer.Addresses
			.Where(static address => !address.External)
			.Select(static address => address.Target)
			.Concat(_buffer.PcRelative.Select(static fixup => fixup.Target))
			.ToHashSet(StringComparer.Ordinal);
	}

	private List<Candidate> FindCandidates()
	{
		var instructions = _assembler.GetExecutableInstructionStream();
		var result = new List<Candidate>();
		for (var returnIndex = 0; returnIndex < instructions.Count; returnIndex++)
		{
			var terminal = instructions[returnIndex];
			if (terminal.Kind != M68kInstructionKind.Return || terminal.Opcode != 0x4E75)
			{
				continue;
			}

			var startIndex = returnIndex;
			while (startIndex > 0)
			{
				var current = instructions[startIndex];
				var previous = instructions[startIndex - 1];
				if (HasLabelAt(current.Offset) ||
					previous.Kind is not M68kInstructionKind.Normal)
				{
					break;
				}
				startIndex--;
			}

			var start = instructions[startIndex].Offset;
			var end = terminal.Offset + terminal.Length;
			if (!IsSafeTerminalBlock(instructions, startIndex, returnIndex, start, end))
			{
				continue;
			}
			var entryLabels = _labelsByOffset.GetValueOrDefault(start, Array.Empty<string>());
			if (HasAddressTakenEntry(entryLabels))
			{
				continue;
			}

			TryGetInvertingPredecessor(
				instructions,
				startIndex,
				start,
				end,
				entryLabels,
				out var branchOffset,
				out var continuationLabel);
			result.Add(new(
				start,
				end,
				_buffer.Bytes.GetRange(start, end - start).ToArray(),
				entryLabels,
				branchOffset,
				continuationLabel));
		}
		return result;
	}

	private List<PlannedGroup> PlanGroups(IReadOnlyList<Candidate> candidates)
	{
		var groups = new List<PlannedGroup>();
		foreach (var signatureGroup in candidates
			.GroupBy(static candidate => Convert.ToHexString(candidate.Signature), StringComparer.Ordinal)
			.OrderBy(static group => group.Key, StringComparer.Ordinal))
		{
			if (signatureGroup.Count() < 2)
			{
				continue;
			}
			var signature = signatureGroup.First().Signature;
			var tailLabel = "__m68k_shared_epilogue_" +
				Convert.ToHexString(SHA256.HashData(signature).AsSpan(0, 8));
			if (_buffer.Labels.ContainsKey(tailLabel))
			{
				continue;
			}
			var copies = new List<PlannedCopy>();
			foreach (var candidate in signatureGroup.OrderBy(static candidate => candidate.Start))
			{
				var invert = candidate.InvertingBranchOffset is not null;
				if (!CanReachGeneratedTail(candidate, invert))
				{
					continue;
				}
				// A labelled block reached only by linear fallthrough is normally a
				// method entry, not a cold exit.  Do not turn independently callable
				// leaf methods into anonymous branch trampolines merely because their
				// bodies happen to match.
				if (!invert && candidate.EntryLabels.Count != 0 &&
					!candidate.EntryLabels.Any(IsBranchReferenced))
				{
					continue;
				}
				if (!invert && candidate.End - candidate.Start <= 4)
				{
					continue;
				}
				copies.Add(new(candidate, tailLabel, invert));
			}
			if (copies.Count < 2)
			{
				continue;
			}
			var grossRemoved = copies.Sum(copy => copy.Candidate.End - copy.Candidate.Start);
			var branchBytes = copies.Count(copy => !copy.InvertBranch) * 4;
			if (grossRemoved - branchBytes - signature.Length <= 0)
			{
				continue;
			}
			groups.Add(new(signature, tailLabel, copies));
		}
		return groups;
	}

	private bool CanReachGeneratedTail(Candidate candidate, bool invert)
	{
		// Generated tails are inserted immediately before data.  The subsequent
		// batch only removes code between this source and the tail, so the current
		// displacement is a conservative upper bound.  Do not introduce a far
		// JMP fallback: a small shared epilogue is not worth a larger, slower exit.
		var target = _buffer.DataStartOffset ?? _buffer.Bytes.Count;
		var source = invert ? candidate.InvertingBranchOffset!.Value : candidate.Start;
		return target - (source + 4) <= short.MaxValue;
	}

	private bool IsSafeTerminalBlock(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int returnIndex,
		int start,
		int end)
	{
		if (_buffer.Branches.Any(branch => branch.OpcodeOffset >= start && branch.OpcodeOffset < end) ||
			_buffer.Addresses.Any(address => address.Offset >= start && address.Offset < end) ||
			_buffer.PcRelative.Any(fixup => fixup.DisplacementOffset >= start && fixup.DisplacementOffset < end) ||
			Enumerable.Range(startIndex + 1, returnIndex - startIndex)
				.Any(index => _labelsByOffset.ContainsKey(instructions[index].Offset)))
		{
			return false;
		}
		for (var index = startIndex; index <= returnIndex; index++)
		{
			var instruction = instructions[index];
			if (!instruction.IsDecoded ||
				instruction.Kind is not (M68kInstructionKind.Normal or M68kInstructionKind.Return) ||
				IsTrap(instruction.Opcode))
			{
				return false;
			}
		}
		return true;
	}

	private bool HasAddressTakenEntry(IReadOnlyList<string> entryLabels) =>
		entryLabels.Any(_addressTakenLabels.Contains);

	private void TryGetInvertingPredecessor(
		IReadOnlyList<M68kEmittedInstruction> instructions,
		int startIndex,
		int start,
		int end,
		IReadOnlyList<string> entryLabels,
		out int? branchOffset,
		out string? continuationLabel)
	{
		branchOffset = null;
		continuationLabel = null;
		if (startIndex == 0 ||
			entryLabels.Any(IsBranchReferenced) ||
			!_labelsByOffset.ContainsKey(end))
		{
			return;
		}
		var predecessor = instructions[startIndex - 1];
		if (predecessor.Kind != M68kInstructionKind.ConditionalBranch ||
			predecessor.Offset + predecessor.Length != start ||
			predecessor.TargetOffset != end ||
			!TryGetBranch(predecessor.Offset, out var branch))
		{
			return;
		}
		branchOffset = predecessor.Offset;
		continuationLabel = branch.Target;
	}

	private void InvertAndRetargetBranch(int offset, string target)
	{
		var opcode = _buffer.ReadWord(offset);
		if ((opcode & 0xF000) != 0x6000 || ((opcode >> 8) & 0x0F) == 0)
		{
			throw new InvalidOperationException("Terminal epilogue predecessor is not a conditional branch.");
		}
		_buffer.WriteWord(offset, opcode ^ 0x0100);
		for (var index = 0; index < _buffer.Branches.Count; index++)
		{
			if (_buffer.Branches[index].OpcodeOffset == offset)
			{
				_buffer.Branches[index] = _buffer.Branches[index] with { Target = target };
				return;
			}
		}
		throw new InvalidOperationException("Terminal epilogue predecessor has no branch fixup.");
	}

	private bool IsBranchReferenced(string label) => _branchReferencedLabels.Contains(label);

	private bool HasLabelAt(int offset) => _labelsByOffset.ContainsKey(offset);

	private bool TryGetBranch(int offset, out BranchFixup branch)
	{
		foreach (var fixup in _buffer.Branches)
		{
			if (fixup.OpcodeOffset == offset)
			{
				branch = fixup;
				return true;
			}
		}
		branch = default;
		return false;
	}

	private static bool IsTrap(ushort opcode) =>
		(opcode & 0xFFF0) == 0x4E40 || opcode == 0x4AFC;
}
