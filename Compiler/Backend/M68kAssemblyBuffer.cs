/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kAssemblyBuffer
{
	internal List<byte> Bytes { get; } = new();

	internal M68kOffsetMap Labels { get; } = new();

	internal M68kOffsetMap AnalysisAnchors { get; } = new();

	internal List<BranchFixup> Branches { get; } = new();

	internal List<AddressFixup> Addresses { get; } = new();

	internal List<PcRelativeFixup> PcRelative { get; } = new();

	internal Dictionary<int, M68kInstructionEffects> InstructionEffectOverrides { get; } = new();

	internal int? DataStartOffset { get; private set; }

	internal int? WritableDataStartOffset { get; private set; }

	internal int? BssStartOffset { get; private set; }

	internal bool HasLabelAt(int offset) => Labels.HasOffset(offset);

	internal void MarkDataStart() => DataStartOffset ??= Bytes.Count;

	internal void MarkWritableDataStart() => WritableDataStartOffset ??= Bytes.Count;

	internal void MarkBssStart() => BssStartOffset ??= Bytes.Count;

	internal ushort ReadWord(int offset) =>
		(ushort)((Bytes[offset] << 8) | Bytes[offset + 1]);

	internal uint ReadLong(int offset) =>
		((uint)ReadWord(offset) << 16) | ReadWord(offset + 2);

	internal void WriteWord(int offset, int value)
	{
		Bytes[offset] = (byte)(value >> 8);
		Bytes[offset + 1] = (byte)value;
	}

	internal void InsertBytes(int offset, int count) =>
		InsertBytes(offset, count, shiftBoundaryLabels: false, shiftBoundaryAnchors: false);

	internal void InsertBytes(
		int offset,
		int count,
		bool shiftBoundaryLabels,
		bool shiftBoundaryAnchors)
	{
		Bytes.InsertRange(offset, Enumerable.Repeat((byte)0, count));
		if (DataStartOffset is { } dataStartOffset && dataStartOffset >= offset)
		{
			DataStartOffset = dataStartOffset + count;
		}
		if (WritableDataStartOffset is { } writableDataStartOffset &&
			writableDataStartOffset >= offset)
		{
			WritableDataStartOffset = writableDataStartOffset + count;
		}
		if (BssStartOffset is { } bssStartOffset && bssStartOffset >= offset)
		{
			BssStartOffset = bssStartOffset + count;
		}

		Labels.InsertBytes(offset, count, shiftBoundaryLabels);
		AnalysisAnchors.InsertBytes(offset, count, shiftBoundaryAnchors);

		for (var index = 0; index < Branches.Count; index++)
		{
			if (Branches[index].OpcodeOffset >= offset)
			{
				Branches[index] = Branches[index] with
				{
					OpcodeOffset = Branches[index].OpcodeOffset + count
				};
			}
		}
		for (var index = 0; index < Addresses.Count; index++)
		{
			if (Addresses[index].Offset >= offset)
			{
				Addresses[index] = Addresses[index] with
				{
					Offset = Addresses[index].Offset + count
				};
			}
		}
		for (var index = 0; index < PcRelative.Count; index++)
		{
			if (PcRelative[index].DisplacementOffset >= offset)
			{
				PcRelative[index] = PcRelative[index] with
				{
					DisplacementOffset = PcRelative[index].DisplacementOffset + count
				};
			}
		}
		foreach (var instructionOffset in InstructionEffectOverrides.Keys
			.Where(instructionOffset => instructionOffset >= offset)
			.OrderDescending()
			.ToArray())
		{
			var effects = InstructionEffectOverrides[instructionOffset];
			InstructionEffectOverrides.Remove(instructionOffset);
			InstructionEffectOverrides.Add(instructionOffset + count, effects);
		}
	}

	internal void RemoveBytes(int offset, int count)
	{
		var end = checked(offset + count);
		Bytes.RemoveRange(offset, count);
		if (DataStartOffset is { } dataStartOffset && dataStartOffset >= end)
		{
			DataStartOffset = dataStartOffset - count;
		}
		if (WritableDataStartOffset is { } writableDataStartOffset &&
			writableDataStartOffset >= end)
		{
			WritableDataStartOffset = writableDataStartOffset - count;
		}
		if (BssStartOffset is { } bssStartOffset && bssStartOffset >= end)
		{
			BssStartOffset = bssStartOffset - count;
		}
		Branches.RemoveAll(branch =>
			branch.OpcodeOffset >= offset && branch.OpcodeOffset < end);
		Addresses.RemoveAll(address =>
			address.Offset >= offset && address.Offset < end);
		PcRelative.RemoveAll(fixup =>
			fixup.DisplacementOffset >= offset && fixup.DisplacementOffset < end);
		foreach (var instructionOffset in InstructionEffectOverrides.Keys
			.Where(instructionOffset => instructionOffset >= offset && instructionOffset < end)
			.ToArray())
		{
			InstructionEffectOverrides.Remove(instructionOffset);
		}

		Labels.RemoveBytes(offset, count, clampInterior: false);
		AnalysisAnchors.RemoveBytes(offset, count, clampInterior: true);

		for (var index = 0; index < Branches.Count; index++)
		{
			var branch = Branches[index];
			if (branch.OpcodeOffset >= end)
			{
				Branches[index] = branch with { OpcodeOffset = branch.OpcodeOffset - count };
			}
		}

		for (var index = 0; index < Addresses.Count; index++)
		{
			var address = Addresses[index];
			if (address.Offset >= end)
			{
				Addresses[index] = address with { Offset = address.Offset - count };
			}
		}

		for (var index = 0; index < PcRelative.Count; index++)
		{
			var fixup = PcRelative[index];
			if (fixup.DisplacementOffset >= end)
			{
				PcRelative[index] = fixup with { DisplacementOffset = fixup.DisplacementOffset - count };
			}
		}
		foreach (var instructionOffset in InstructionEffectOverrides.Keys
			.Where(instructionOffset => instructionOffset >= end)
			.Order()
			.ToArray())
		{
			var effects = InstructionEffectOverrides[instructionOffset];
			InstructionEffectOverrides.Remove(instructionOffset);
			InstructionEffectOverrides.Add(instructionOffset - count, effects);
		}
	}

	internal void RemoveByteRanges(
		IReadOnlyList<(int Offset, int Count)> ranges)
	{
		if (ranges.Count == 0)
		{
			return;
		}

		var ordered = ranges.OrderBy(static range => range.Offset).ToArray();
		var removedBefore = new int[ordered.Length + 1];
		var totalRemoved = 0;
		var previousEnd = 0;
		for (var index = 0; index < ordered.Length; index++)
		{
			var range = ordered[index];
			var end = checked(range.Offset + range.Count);
			if (range.Count <= 0 || range.Offset < previousEnd || end > Bytes.Count)
			{
				throw new ArgumentOutOfRangeException(
					nameof(ranges),
					"Removed byte ranges must be positive, ordered, non-overlapping, and inside the assembly buffer.");
			}
			totalRemoved = checked(totalRemoved + range.Count);
			removedBefore[index + 1] = totalRemoved;
			previousEnd = end;
		}

		var compacted = new byte[Bytes.Count - totalRemoved];
		var sourceOffset = 0;
		var destinationOffset = 0;
		foreach (var range in ordered)
		{
			var retained = range.Offset - sourceOffset;
			Bytes.CopyTo(sourceOffset, compacted, destinationOffset, retained);
			sourceOffset = range.Offset + range.Count;
			destinationOffset += retained;
		}
		Bytes.CopyTo(
			sourceOffset,
			compacted,
			destinationOffset,
			Bytes.Count - sourceOffset);
		Bytes.Clear();
		Bytes.AddRange(compacted);

		DataStartOffset = MapSectionOffset(DataStartOffset);
		WritableDataStartOffset = MapSectionOffset(WritableDataStartOffset);
		BssStartOffset = MapSectionOffset(BssStartOffset);

		// Descending edits preserve the original coordinates for each range.
		// Byte compaction and the other metadata mappings still happen once.
		for (var index = ordered.Length - 1; index >= 0; index--)
		{
			var range = ordered[index];
			Labels.RemoveBytes(range.Offset, range.Count, clampInterior: false);
			AnalysisAnchors.RemoveBytes(range.Offset, range.Count, clampInterior: true);
		}

		for (var index = Branches.Count - 1; index >= 0; index--)
		{
			var branch = Branches[index];
			if (IsRemoved(branch.OpcodeOffset))
			{
				Branches.RemoveAt(index);
			}
			else
			{
				Branches[index] = branch with
				{
					OpcodeOffset = MapOffset(branch.OpcodeOffset)
				};
			}
		}
		for (var index = Addresses.Count - 1; index >= 0; index--)
		{
			var address = Addresses[index];
			if (IsRemoved(address.Offset))
			{
				Addresses.RemoveAt(index);
			}
			else
			{
				Addresses[index] = address with { Offset = MapOffset(address.Offset) };
			}
		}
		for (var index = PcRelative.Count - 1; index >= 0; index--)
		{
			var fixup = PcRelative[index];
			if (IsRemoved(fixup.DisplacementOffset))
			{
				PcRelative.RemoveAt(index);
			}
			else
			{
				PcRelative[index] = fixup with
				{
					DisplacementOffset = MapOffset(fixup.DisplacementOffset)
				};
			}
		}

		var effects = InstructionEffectOverrides.ToArray();
		InstructionEffectOverrides.Clear();
		foreach (var effect in effects)
		{
			if (!IsRemoved(effect.Key))
			{
				InstructionEffectOverrides.Add(MapOffset(effect.Key), effect.Value);
			}
		}

		int? MapSectionOffset(int? offset) =>
			offset is { } value ? MapOffset(value) : null;

		int MapOffset(int value)
		{
			var low = 0;
			var high = ordered.Length;
			while (low < high)
			{
				var middle = low + ((high - low) / 2);
				if (ordered[middle].Offset + ordered[middle].Count <= value)
				{
					low = middle + 1;
				}
				else
				{
					high = middle;
				}
			}

			return value - removedBefore[low];
		}

		bool IsRemoved(int value)
		{
			var low = 0;
			var high = ordered.Length;
			while (low < high)
			{
				var middle = low + ((high - low) / 2);
				if (ordered[middle].Offset <= value)
				{
					low = middle + 1;
				}
				else
				{
					high = middle;
				}
			}
			if (low == 0)
			{
				return false;
			}
			var candidate = ordered[low - 1];
			return value < candidate.Offset + candidate.Count;
		}
	}
}

internal readonly record struct BranchFixup(
	int OpcodeOffset,
	string Target,
	bool CanRelaxToShort = true,
	bool PreferFastFallthrough = false);

internal readonly record struct AddressFixup(
	int Offset,
	string Target,
	bool External,
	int Addend = 0,
	bool CanRelaxToPcRelative = true,
	M68kRegister? StoreAddressScratchRegister = null);

internal readonly record struct PcRelativeFixup(int DisplacementOffset, string Target);
