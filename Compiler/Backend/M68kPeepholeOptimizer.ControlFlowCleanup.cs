/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private IReadOnlyList<M68kEmittedInstruction> ExecutableCandidates(M68kInstructionDataflow dataflow)
	{
		var instructions = dataflow.Instructions;
		var end = _assembler.GetAnalysisRange().End;
		if (_buffer.DataStartOffset is { } dataStart) end = Math.Min(end, dataStart);
		if (instructions.Count == 0 || instructions[^1].Offset + instructions[^1].Length <= end)
			return instructions;
		return instructions.TakeWhile(instruction => instruction.Offset + instruction.Length <= end).ToArray();
	}

	private bool HasControlTransferAddend(M68kEmittedInstruction instruction) =>
		instruction.Kind is M68kInstructionKind.Call or M68kInstructionKind.UnconditionalBranch &&
		_buffer.Addresses.Any(address => address.Offset == instruction.Offset + 2 && address.Addend != 0);

	private bool CanFoldAcrossUnreferencedIlLabels(int startOffset, int endOffset)
	{
		// The allocated emitter records IL positions even when they are not
		// control-flow targets. They are hidden by assembly rendering, but still
		// need to be moved when their original instruction is replaced. A named
		// block/method entry or any referenced IL marker remains a hard boundary.
		IEnumerable<string> labels = _assembler.AnalysisLabelNames ??
			(IEnumerable<string>)_buffer.Labels.Keys;
		foreach (var name in labels)
		{
			if (!_buffer.Labels.TryGetValue(name, out var offset) ||
				offset <= startOffset || offset >= endOffset) continue;
			var marker = name.LastIndexOf(":IL_", StringComparison.Ordinal);
			if (!name.StartsWith("method:", StringComparison.Ordinal) ||
				marker < 0 || name.Length - marker - 4 < 4 ||
				!name[(marker + 4)..].All(character => character is
					>= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f') ||
				_unrelocatableLabelNames.Contains(name)) return false;
		}
		// An address can enter through a label plus an addend without naming an
		// interior marker. Even a target after the replacement is unsafe when
		// the label and target straddle the bytes being shortened: its literal
		// addend would otherwise keep the original distance.
		// Callers finish all boundary queries before mutating the buffer. A
		// successful rewrite ends the round; both entry points clear this
		// snapshot at round start and in finally.
		// Calls outside a round deliberately rebuild from the current buffer.
		var entries = _addressFixupOffsets is null
			? new AddressEntryBoundaryIndex(_buffer)
			: _addressEntryBoundaryIndex ??= new AddressEntryBoundaryIndex(_buffer);
		return !entries.BlocksReplacement(startOffset, endOffset);
	}

	private sealed class AddressEntryBoundaryIndex
	{
		private readonly long[] _destinations;
		private readonly long[] _spanStarts;
		private readonly long[] _spanMaximumEnds;

		internal AddressEntryBoundaryIndex(M68kAssemblyBuffer buffer)
		{
			var destinations = new HashSet<long>();
			var spans = new List<(long Start, long End)>();
			foreach (var address in buffer.Addresses)
			{
				if (address.External || !buffer.Labels.TryGetValue(address.Target, out var target))
					continue;
				var destination = (long)target + address.Addend;
				destinations.Add(destination);
				if (address.Addend != 0)
				{
					spans.Add((Math.Min(target, destination), Math.Max(target, destination)));
				}
			}
			_destinations = destinations.OrderBy(static destination => destination).ToArray();
			spans.Sort(static (left, right) => left.Start.CompareTo(right.Start));
			_spanStarts = new long[spans.Count];
			_spanMaximumEnds = new long[spans.Count];
			var maximumEnd = long.MinValue;
			for (var index = 0; index < spans.Count; index++)
			{
				_spanStarts[index] = spans[index].Start;
				maximumEnd = Math.Max(maximumEnd, spans[index].End);
				_spanMaximumEnds[index] = maximumEnd;
			}
		}

		internal bool BlocksReplacement(int startOffset, int endOffset)
		{
			// Preserve the original open destination range and mixed span
			// bounds exactly. long arithmetic retains label+addend overflow safety.
			var destinationIndex = LowerBound(_destinations, (long)startOffset + 1);
			if (destinationIndex < _destinations.Length &&
				_destinations[destinationIndex] < endOffset) return true;
			var spanCount = LowerBound(_spanStarts, endOffset);
			return spanCount != 0 && _spanMaximumEnds[spanCount - 1] >= startOffset;
		}

		private static int LowerBound(long[] offsets, long value)
		{
			var low = 0;
			var high = offsets.Length;
			while (low < high)
			{
				var middle = low + ((high - low) / 2);
				if (offsets[middle] < value) low = middle + 1;
				else high = middle;
			}
			return low;
		}
	}

	private bool CallTargetReadsConditionsBeforeOverwrite(int targetOffset,
		M68kConditionCodeSet conditions, IReadOnlyList<M68kEmittedInstruction> instructions)
	{
		var end = NextOffsetAfter(ControlFlowMethodEnds(), targetOffset, int.MaxValue);
		if (end == int.MaxValue) return true;
		foreach (var instruction in instructions.Where(instruction =>
			instruction.Offset >= targetOffset && instruction.Offset < end))
		{
			// A simple callee prefix is sufficient for this extension. Do not
			// assume a call kills incoming NZVC merely because the normal call
			// ABI marks them clobbered: a callee can first consume them with Scc.
			if (!instruction.IsDecoded || instruction.Kind != M68kInstructionKind.Normal ||
				instruction.IsNonReturning) return true;
			var effects = _assembler.TryGetInstructionEffects(instruction.Offset, out var annotated)
				? annotated : M68kInstructionDataflow.GetEffects(instruction);
			if (effects.IsBarrier || (effects.ReadsConditions & conditions) != M68kConditionCodeSet.None)
				return true;
			conditions &= ~effects.WritesConditions;
			if (conditions == M68kConditionCodeSet.None) return false;
		}
		return true;
	}

	private bool TryFoldBooleanComparisonInversion(M68kInstructionDataflow dataflow)
	{
		var instructions = ExecutableCandidates(dataflow);
		for (var index = 0; index < instructions.Count; index++)
		{
			if (!TryReadBooleanNormalization(index, out var firstEnd)) continue;
			var first = instructions[index];
			var source = first.Opcode & 7;
			var next = firstEnd + 1;
			if (next < instructions.Count &&
				(instructions[next].Opcode & 0xFFF8) is (0x4A00 or 0x4A40 or 0x4A80) &&
				(instructions[next].Opcode & 7) == source &&
				instructions[next].Length == 2)
			{
				next++;
			}
			if (next >= instructions.Count ||
				(instructions[next].Opcode & 0xFFF8) is not (0x57C0 or 0x56C0) ||
				!TryReadBooleanNormalization(next, out var lastEnd)) continue;

			var second = instructions[next];
			var destination = second.Opcode & 7;
			var last = instructions[lastEnd];
			var length = last.Offset + last.Length - first.Offset;
			if (length <= 8 ||
				source != destination &&
					(!dataflow.TryGetFacts(last.Offset, out var facts) ||
					 (facts.LiveDataAfter & (1 << source)) != 0) ||
				Enumerable.Range(index + 1, lastEnd - index).Any(position =>
					instructions[position].Offset != first.Offset + (position - index) * 2) ||
				!CanFoldAcrossUnreferencedIlLabels(first.Offset, last.Offset + last.Length)) continue;

			// SEQ inverts canonical 0/1; SNE merely normalizes it again. Keep
			// one full-width normalization so its result and all five final CCR
			// bits agree even when an earlier pass shortened either NEG to .B.
			var condition = (first.Opcode >> 8) & 15;
			if ((second.Opcode & 0xFF00) == 0x5700) condition ^= 1;
			_buffer.WriteWord(first.Offset, 0x50C0 | (condition << 8) | destination);
			_buffer.WriteWord(first.Offset + 2, 0x4880 | destination);
			_buffer.WriteWord(first.Offset + 4, 0x48C0 | destination);
			_buffer.WriteWord(first.Offset + 6, 0x4480 | destination);
			for (var offset = first.Offset; offset < first.Offset + 8; offset += 2)
			{
				_buffer.InstructionEffectOverrides.Remove(offset);
			}
			MoveLabelsToOffset(first.Offset + 2, first.Offset + length, first.Offset);
			_buffer.RemoveBytes(first.Offset + 8, length - 8);
			return true;
		}
		return false;

		bool TryReadBooleanNormalization(int start, out int end)
		{
			end = start;
			if (start + 1 >= instructions.Count) return false;
			var set = instructions[start];
			if (set.Length != 2 || (set.Opcode & 0xF0F8) != 0x50C0) return false;
			var register = set.Opcode & 7;
			if (instructions[start + 1].Opcode == (0x4400 | register) &&
				instructions[start + 1].Length == 2 &&
				(dataflow.GetDataValueBefore(set.Offset, register).KnownZeroMask &
					0xFFFF_FF00u) == 0xFFFF_FF00u)
			{
				end = start + 1;
				return true;
			}
			if (start + 3 >= instructions.Count ||
				instructions[start + 1].Opcode != (0x4880 | register) ||
				instructions[start + 2].Opcode != (0x48C0 | register) ||
				instructions[start + 3].Opcode != (0x4480 | register)) return false;
			end = start + 3;
			return true;
		}
	}

	private bool TryInlineBareReturnBranches()
	{
		var instructions = _assembler.GetExecutableInstructionStream()
			.ToDictionary(static instruction => instruction.Offset);
		var methodEnds = ControlFlowMethodEnds();
		var replacements = new List<M68kEmittedInstruction>();
		foreach (var fixup in _buffer.Branches)
		{
			if (!instructions.TryGetValue(fixup.OpcodeOffset, out var branch) ||
				(branch.Opcode & 0xFF00) != 0x6000 ||
				HasInternalLabel(branch) ||
				!TryFollowPlainBranches(branch, instructions, methodEnds, out var terminal) ||
				terminal.Opcode != 0x4E75) continue;
			replacements.Add(branch);
		}
		if (replacements.Count == 0) return false;
		var offsets = replacements.Select(static instruction => instruction.Offset).ToHashSet();
		_buffer.Branches.RemoveAll(branch => offsets.Contains(branch.OpcodeOffset));
		foreach (var branch in replacements)
		{
			_buffer.WriteWord(branch.Offset, 0x4E75);
			_buffer.InstructionEffectOverrides.Remove(branch.Offset);
		}
		_buffer.RemoveByteRanges(replacements
			.Where(static branch => branch.Length > 2)
			.Select(static branch => (branch.Offset + 2, branch.Length - 2)).ToArray());
		return true;
	}

	private bool TryBypassEmptySwitchEdges()
	{
		var instructions = _assembler.GetExecutableInstructionStream();
		var byOffset = instructions.ToDictionary(static instruction => instruction.Offset);
		var labelsByOffset = _buffer.Labels.GroupBy(static label => label.Value)
			.ToDictionary(static group => group.Key,
				static group => group.Select(static label => label.Key).ToArray());
		var methodEnds = ControlFlowMethodEnds();
		var removals = new List<(int Offset, int Count)>();
		var redirects = new List<(string Label, int Offset)>();
		for (var index = 1; index < instructions.Count; index++)
		{
			var edge = instructions[index];
			var previous = instructions[index - 1];
			if ((edge.Opcode & 0xFF00) != 0x6000 ||
				!labelsByOffset.TryGetValue(edge.Offset, out var labels) ||
				!labels.Any(static label => label.StartsWith(
					"generated:allocated-switch-edge:", StringComparison.Ordinal)) ||
				labels.Any(static label => !IsPrivateControlFlowLabel(label)) ||
				previous.Offset + previous.Length != edge.Offset ||
				previous.Kind is not (M68kInstructionKind.UnconditionalBranch or
					M68kInstructionKind.Return) && !previous.IsNonReturning ||
				HasInternalLabel(edge) ||
				!TryFollowPlainBranches(edge, byOffset, methodEnds, out var target) ||
				!CanRedirectControlFlowLabels(labels, target.Offset)) continue;

			// Keep the label names (and thus table relocations), but make them
			// aliases of the real case. Only generated, empty edge blocks with
			// no physical fallthrough are removed; phi-copy blocks stay intact.
			foreach (var label in labels) redirects.Add((label, target.Offset));
			removals.Add((edge.Offset, edge.Length));
		}
		if (removals.Count == 0) return false;
		foreach (var redirect in redirects) _buffer.Labels[redirect.Label] = redirect.Offset;
		_buffer.RemoveByteRanges(removals);
		return true;
	}

	private bool CanRedirectControlFlowLabels(IReadOnlyCollection<string> labels, int target)
	{
		var names = labels.ToHashSet(StringComparer.Ordinal);
		foreach (var branch in _buffer.Branches)
		{
			if (names.Contains(branch.Target) &&
				!CanReachControlFlowTarget(branch.OpcodeOffset, target)) return false;
		}
		// A nonzero addend may designate bytes inside the old stub, not its
		// entry point, so preserve such address-taken code verbatim.
		if (_buffer.Addresses.Any(address => names.Contains(address.Target) &&
			(address.External || address.Addend != 0))) return false;
		return !_buffer.PcRelative.Any(reference => names.Contains(reference.Target) &&
			(target - reference.DisplacementOffset is < short.MinValue or > short.MaxValue));
	}

	private static bool IsPrivateControlFlowLabel(string label)
	{
		if (label.StartsWith("generated:", StringComparison.Ordinal)) return true;
		var segment = label.LastIndexOf(":BB", StringComparison.Ordinal);
		return segment >= 0 && segment + 3 < label.Length &&
			label[(segment + 3)..].All(static character =>
				character is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f');
	}

	private bool TryThreadConstantBooleanEdge(M68kInstructionDataflow dataflow)
	{
		var instructions = ExecutableCandidates(dataflow);
		var byOffset = instructions.ToDictionary(static instruction => instruction.Offset);
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var constant = instructions[index];
			var edge = instructions[index + 1];
			if ((constant.Opcode & 0xF100) != 0x7000 ||
				(constant.Opcode & 0xFF) is not (0 or 1) || constant.Length != 2 ||
				(edge.Opcode & 0xFF00) != 0x6000 ||
				edge.Offset != constant.Offset + constant.Length || HasLabelAt(edge.Offset) ||
				edge.TargetOffset is not { } testOffset ||
				!byOffset.TryGetValue(testOffset, out var test) ||
				(test.Opcode & 0xFFF8) is not (0x4A00 or 0x4A40 or 0x4A80) ||
				(test.Opcode & 7) != ((constant.Opcode >> 9) & 7) ||
				!byOffset.TryGetValue(test.Offset + test.Length, out var condition) ||
				condition.Kind != M68kInstructionKind.ConditionalBranch ||
				((condition.Opcode >> 8) & 15) is not (6 or 7) ||
				condition.TargetOffset is not { } takenOffset) continue;

			var nonzero = (constant.Opcode & 0xFF) != 0;
			var taken = ((condition.Opcode >> 8) & 15) == 6 ? nonzero : !nonzero;
			var successor = taken ? takenOffset : condition.Offset + condition.Length;
			var branchIndex = _buffer.Branches.FindIndex(branch => branch.OpcodeOffset == edge.Offset);
			if (successor == testOffset || !byOffset.ContainsKey(successor) || branchIndex < 0 ||
				!CanReachControlFlowTarget(edge.Offset, successor)) continue;

			var scopeLabels = _assembler.AnalysisLabelNames;
			var targetLabel = _buffer.Labels.FirstOrDefault(label => label.Value == successor &&
				!label.Key.EndsWith(":end", StringComparison.Ordinal) &&
				(scopeLabels is null || scopeLabels.Contains(label.Key))).Key;
			if (targetLabel is null)
			{
				// Method scopes retain an immutable list of their original labels.
				// Do not create an entry that later scoped passes cannot see.
				if (scopeLabels is not null) continue;
				var suffix = 0;
				do targetLabel = $"generated:known-boolean-edge:{edge.Offset:X}:{suffix++}";
				while (_buffer.Labels.ContainsKey(targetLabel));
				_buffer.Labels.Add(targetLabel, successor);
			}
			// Retain MOVEQ: it supplies both the live register value and exactly
			// the NZVC state of TST of 0/1. Neither instruction changes X.
			_buffer.Branches[branchIndex] = _buffer.Branches[branchIndex] with { Target = targetLabel };
			_referencedLabelNames.Add(targetLabel);
			_unrelocatableLabelNames.Add(targetLabel);
			return true;
		}
		return false;
	}

	private bool TryFoldConstantAddressDisplacement(M68kInstructionDataflow dataflow)
	{
		var instructions = ExecutableCandidates(dataflow);
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var load = instructions[index];
			var add = instructions[index + 1];
			var loadImmediate = (load.Opcode & 0xF100) == 0x7000 && load.Length == 2 ||
				(load.Opcode & 0xF1FF) == 0x203C && load.Length == 6 ||
				(load.Opcode & 0xF1FF) == 0x303C && load.Length == 4;
			if (!loadImmediate || (add.Opcode & 0xF1F8) is not
				(0xD1C0 or 0x91C0 or 0xD0C0 or 0x90C0) || add.Length != 2 ||
				add.Offset != load.Offset + load.Length ||
				!CanFoldAcrossUnreferencedIlLabels(load.Offset, add.Offset + add.Length) ||
				HasInternalLabel(load) || HasAddressFixup(load)) continue;
			var temporary = (load.Opcode >> 9) & 7;
			if ((add.Opcode & 7) != temporary ||
				!dataflow.GetDataValueBefore(add.Offset, temporary).IsExact(out var value) ||
				!dataflow.TryGetFacts(load.Offset, out var loadFacts) ||
				!dataflow.TryGetFacts(add.Offset, out var facts) ||
				(facts.LiveDataAfter & (1 << temporary)) != 0 ||
				(facts.LiveConditionsAfter & loadFacts.Effects.WritesConditions) !=
					M68kConditionCodeSet.None) continue;

			var operand = (add.Opcode & 0x0100) != 0
				? unchecked((int)value) : unchecked((short)value);
			var displacement = (add.Opcode & 0xF000) == 0x9000 ? -(long)operand : operand;
			if (displacement is < short.MinValue or > short.MaxValue) continue;
			var address = (add.Opcode >> 9) & 7;
			_buffer.WriteWord(load.Offset, 0x41E8 | (address << 9) | address);
			_buffer.WriteWord(load.Offset + 2, unchecked((ushort)(short)displacement));
			_buffer.InstructionEffectOverrides.Remove(load.Offset);
			_buffer.InstructionEffectOverrides.Remove(add.Offset);
			MoveLabelsToOffset(load.Offset + 2, add.Offset + add.Length, load.Offset);
			var removed = load.Length + add.Length - 4;
			if (removed != 0) _buffer.RemoveBytes(load.Offset + 4, removed);
			return true;
		}
		return false;
	}

	private bool CanTailCallWithSharedReturn(string label)
	{
		if (!_buffer.Labels.TryGetValue(label, out var targetOffset)) return false;
		var instructions = _assembler.GetExecutableInstructionStream()
			.ToDictionary(static instruction => instruction.Offset);
		var end = NextOffsetAfter(ControlFlowMethodEnds(), targetOffset, int.MaxValue);
		if (end == int.MaxValue) return false;
		var pending = new Stack<int>();
		var visited = new HashSet<int>();
		pending.Push(targetOffset);
		while (pending.TryPop(out var offset))
		{
			if (!visited.Add(offset)) continue;
			if (offset < targetOffset || offset >= end ||
				!instructions.TryGetValue(offset, out var instruction) || !instruction.IsDecoded)
				return false;
			if (instruction.Opcode == 0x4E75) continue;
			if (instruction.Kind is M68kInstructionKind.Call or M68kInstructionKind.Return ||
				instruction.IsNonReturning) return false;
			// TargetOffset identifies the label, not an absolute relocation's
			// addend. A jump past a labeled RTS could actually enter code that
			// reads the incoming stack, invalidating the tail-call proof.
			if (HasControlTransferAddend(instruction)) return false;
			var effects = M68kInstructionDataflow.GetEffects(instruction);
			// The omitted JSR return address changes the incoming stack layout.
			// Only extend the fold to shared returns when the known callee never
			// inspects or changes A7 (including copying it into another register).
			if (effects.IsBarrier || ((effects.UsesAddress | effects.DefinesAddress) & 0x80) != 0)
				return false;
			if (instruction.Kind is M68kInstructionKind.UnconditionalBranch or
				M68kInstructionKind.ConditionalBranch or M68kInstructionKind.Dbcc)
			{
				if (instruction.TargetOffset is not { } branchTarget) return false;
				pending.Push(branchTarget);
			}
			if (instruction.Kind != M68kInstructionKind.UnconditionalBranch)
				pending.Push(instruction.Offset + instruction.Length);
		}
		return true;
	}

	private int[] ControlFlowMethodEnds() => _buffer.Labels
		.Where(static label => label.Key.EndsWith(":end", StringComparison.Ordinal))
		.Select(static label => label.Value).Distinct().Order().ToArray();

	private static bool TryFollowPlainBranches(
		M68kEmittedInstruction branch,
		IReadOnlyDictionary<int, M68kEmittedInstruction> instructions,
		int[] methodEnds,
		out M68kEmittedInstruction terminal)
	{
		terminal = default;
		var end = NextOffsetAfter(methodEnds, branch.Offset, int.MaxValue);
		var start = methodEnds.LastOrDefault(offset => offset <= branch.Offset);
		var seen = new HashSet<int> { branch.Offset };
		while ((branch.Opcode & 0xFF00) == 0x6000 &&
			branch.Kind == M68kInstructionKind.UnconditionalBranch &&
			branch.TargetOffset is { } target && target >= start && target < end &&
			seen.Add(target) && instructions.TryGetValue(target, out var next) && next.IsDecoded)
		{
			if ((next.Opcode & 0xFF00) != 0x6000)
			{
				terminal = next;
				return true;
			}
			branch = next;
		}
		return false;
	}

	private bool CanReachControlFlowTarget(int branchOffset, int target)
	{
		var opcode = _buffer.ReadWord(branchOffset);
		var displacement = target - (branchOffset + 2);
		return (opcode & 0xF000) == 0x6000 && (opcode & 0xFF) != 0
			? displacement is >= sbyte.MinValue and <= sbyte.MaxValue && displacement != 0
			: displacement is >= short.MinValue and <= short.MaxValue;
	}
}
