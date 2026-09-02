/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Text;

namespace CopperSharp.Compiler.Backend;

/// <summary>
/// Replaces structurally identical closed-world methods with distinct absolute
/// jump entries to one retained body. Method addresses remain distinct, while
/// every compared branch and relocation target is part of the identity proof.
/// </summary>
internal sealed class M68kIdenticalMethodMerger
{
	internal readonly record struct Statistics(
		int Groups,
		int Thunks,
		int GrossBytesRemoved,
		int JumpBytesAdded,
		int NetBytesSaved)
	{
		internal static Statistics Empty => default;
	}

	private sealed record MethodRange(
		string StartLabel,
		string EndLabel,
		int Start,
		int End,
		string Signature,
		IReadOnlyList<string> InteriorLabels);

	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;

	internal M68kIdenticalMethodMerger(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer)
	{
		_assembler = assembler;
		_buffer = buffer;
	}

	internal Statistics Run()
	{
		if (_assembler.IdenticalMethodRanges.Count < 2)
			return Statistics.Empty;

		var ranges = BuildRanges();
		if (ranges.Count < 2)
			return Statistics.Empty;

		var copies = new List<(MethodRange Copy, MethodRange Kept)>();
		var groups = 0;
		foreach (var group in ranges
			.GroupBy(static range => range.Signature, StringComparer.Ordinal)
			.OrderBy(static group => group.Key, StringComparer.Ordinal))
		{
			var ordered = group.OrderBy(static range => range.Start).ToArray();
			if (ordered.Length < 2)
				continue;
			var kept = ordered[0];
			for (var index = 1; index < ordered.Length; index++)
				copies.Add((ordered[index], kept));
			groups++;
		}
		if (copies.Count == 0)
			return Statistics.Empty;

		var removals = new List<(int Offset, int Count)>(copies.Count);
		var removedLabels = new HashSet<string>(StringComparer.Ordinal);
		var gross = 0;
		foreach (var (copy, kept) in copies)
		{
			RemoveOwnedFixups(copy);
			_buffer.WriteWord(copy.Start, 0x4EF9); // JMP absolute long.
			_buffer.WriteWord(copy.Start + 2, 0);
			_buffer.WriteWord(copy.Start + 4, 0);
			_buffer.Addresses.Add(new AddressFixup(
				copy.Start + 2, kept.StartLabel, External: false));
			removals.Add((copy.Start + 6, copy.End - copy.Start - 6));
			gross += copy.End - copy.Start;
			removedLabels.UnionWith(copy.InteriorLabels);
		}

		_buffer.RemoveByteRanges(removals.OrderBy(static range => range.Offset).ToArray());
		foreach (var label in removedLabels)
			_buffer.Labels.Remove(label);
		foreach (var (copy, _) in copies)
		{
			var expectedEnd = _buffer.Labels[copy.StartLabel] + 6;
			if (_buffer.Labels[copy.EndLabel] != expectedEnd)
			{
				throw new InvalidOperationException(
					"Identical-method thunk did not retain a six-byte method range.");
			}
		}

		var jumpBytes = copies.Count * 6;
		return new Statistics(groups, copies.Count, gross, jumpBytes,
			gross - jumpBytes);
	}

	private List<MethodRange> BuildRanges()
	{
		var result = new List<MethodRange>();
		var instructionByOffset = _assembler.GetExecutableInstructionStream()
			.ToDictionary(static instruction => instruction.Offset);
		foreach (var (startLabel, endLabel) in _assembler.IdenticalMethodRanges)
		{
			if (!_buffer.Labels.TryGetValue(startLabel, out var start) ||
				!_buffer.Labels.TryGetValue(endLabel, out var end) ||
				start < 0 || end - start <= 6 ||
				end > (_buffer.DataStartOffset ?? _buffer.Bytes.Count) ||
				_assembler.HasRequestedAlignmentInRange(start, end) ||
				_buffer.AnalysisAnchors.Values.Any(offset =>
					offset >= start && offset < end) ||
				_buffer.InstructionEffectOverrides.Keys.Any(offset =>
					offset >= start && offset < end) ||
				HasStraddlingFixup(start, end) ||
				HasUnsafePositionDependentInstruction(start, end, instructionByOffset) ||
				HasInteriorReferenceFromOutside(start, end) ||
				HasObservableInternalAddress(start, end))
			{
				continue;
			}

			var interiorLabels = _buffer.Labels
				.Where(label => label.Value > start && label.Value < end)
				.Select(static label => label.Key)
				.ToArray();
			result.Add(new MethodRange(startLabel, endLabel, start, end,
				Signature(start, end), interiorLabels));
		}
		return result;
	}

	private string Signature(int start, int end)
	{
		var bytes = _buffer.Bytes.GetRange(start, end - start).ToArray();
		var branches = _buffer.Branches
			.Where(branch => branch.OpcodeOffset >= start && branch.OpcodeOffset < end)
			.OrderBy(static branch => branch.OpcodeOffset).ToArray();
		var addresses = _buffer.Addresses
			.Where(address => address.Offset >= start && address.Offset < end)
			.OrderBy(static address => address.Offset).ToArray();
		var pcRelative = _buffer.PcRelative
			.Where(fixup => fixup.DisplacementOffset >= start &&
				fixup.DisplacementOffset < end)
			.OrderBy(static fixup => fixup.DisplacementOffset).ToArray();
		foreach (var branch in branches)
		{
			var relative = branch.OpcodeOffset - start;
			if ((_buffer.ReadWord(branch.OpcodeOffset) & 0x00FF) != 0)
				bytes[relative + 1] = 0;
			else
			{
				bytes[relative + 2] = 0;
				bytes[relative + 3] = 0;
			}
		}
		foreach (var address in addresses)
			Array.Clear(bytes, address.Offset - start, 4);
		foreach (var fixup in pcRelative)
			Array.Clear(bytes, fixup.DisplacementOffset - start, 2);

		var text = new StringBuilder();
		text.Append(Convert.ToHexString(bytes));
		foreach (var branch in branches)
		{
			text.Append("|B:").Append(branch.OpcodeOffset - start).Append(':')
				.Append(branch.CanRelaxToShort ? '1' : '0').Append(':')
				.Append(TargetIdentity(branch.Target, start, end));
		}
		foreach (var address in addresses)
		{
			text.Append("|A:").Append(address.Offset - start).Append(':')
				.Append(address.External ? '1' : '0').Append(':')
				.Append(address.Addend).Append(':')
				.Append(TargetIdentity(address.Target, start, end));
		}
		foreach (var fixup in pcRelative)
		{
			text.Append("|P:").Append(fixup.DisplacementOffset - start).Append(':')
				.Append(TargetIdentity(fixup.Target, start, end));
		}
		return text.ToString();
	}

	private string TargetIdentity(string target, int start, int end)
	{
		if (_buffer.Labels.TryGetValue(target, out var offset) &&
			offset >= start && offset <= end)
		{
			return "@" + (offset - start);
		}
		return target;
	}

	private bool HasUnsafePositionDependentInstruction(
		int start,
		int end,
		IReadOnlyDictionary<int, M68kEmittedInstruction> instructions)
	{
		var offset = start;
		while (offset < end)
		{
			if (!instructions.TryGetValue(offset, out var instruction) ||
				!instruction.IsDecoded)
				return true;
			if ((instruction.Opcode & 0x3F) is 0x3A or 0x3B)
				return true;
			var branchOpcode = (instruction.Opcode & 0xF000) == 0x6000 ||
				(instruction.Opcode & 0xF0F8) == 0x50C8;
			if (branchOpcode &&
				!_buffer.Branches.Any(branch => branch.OpcodeOffset == offset))
				return true;
			offset += instruction.Length;
		}
		return offset != end;
	}

	private bool HasStraddlingFixup(int start, int end) =>
		_buffer.Branches.Any(fixup => fixup.OpcodeOffset >= start &&
			fixup.OpcodeOffset < end &&
			fixup.OpcodeOffset +
				((_buffer.ReadWord(fixup.OpcodeOffset) & 0x00FF) == 0 ? 4 : 2) > end) ||
		_buffer.Addresses.Any(fixup => fixup.Offset >= start &&
			fixup.Offset < end && fixup.Offset + 4 > end) ||
		_buffer.PcRelative.Any(fixup => fixup.DisplacementOffset >= start &&
			fixup.DisplacementOffset < end && fixup.DisplacementOffset + 2 > end);

	private bool HasInteriorReferenceFromOutside(int start, int end)
	{
		bool InteriorTarget(string target) =>
			_buffer.Labels.TryGetValue(target, out var offset) &&
			offset > start && offset < end;
		return _buffer.Branches.Any(fixup =>
			(fixup.OpcodeOffset < start || fixup.OpcodeOffset >= end) &&
			InteriorTarget(fixup.Target)) ||
			_buffer.Addresses.Any(fixup =>
				(fixup.Offset < start || fixup.Offset >= end) &&
				InteriorTarget(fixup.Target)) ||
			_buffer.PcRelative.Any(fixup =>
				(fixup.DisplacementOffset < start || fixup.DisplacementOffset >= end) &&
				InteriorTarget(fixup.Target));
	}

	private bool HasObservableInternalAddress(int start, int end) =>
		_buffer.Addresses.Any(fixup => fixup.Offset >= start && fixup.Offset < end &&
			_buffer.Labels.TryGetValue(fixup.Target, out var target) &&
			target >= start && target <= end) ||
		_buffer.PcRelative.Any(fixup => fixup.DisplacementOffset >= start &&
			fixup.DisplacementOffset < end &&
			_buffer.Labels.TryGetValue(fixup.Target, out var target) &&
			target >= start && target <= end);

	private void RemoveOwnedFixups(MethodRange range)
	{
		_buffer.Branches.RemoveAll(fixup =>
			fixup.OpcodeOffset >= range.Start && fixup.OpcodeOffset < range.End);
		_buffer.Addresses.RemoveAll(fixup =>
			fixup.Offset >= range.Start && fixup.Offset < range.End);
		_buffer.PcRelative.RemoveAll(fixup =>
			fixup.DisplacementOffset >= range.Start &&
			fixup.DisplacementOffset < range.End);
	}
}
