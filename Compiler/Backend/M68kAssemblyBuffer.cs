/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kAssemblyBuffer
{
	internal List<byte> Bytes { get; } = new();

	internal Dictionary<string, int> Labels { get; } = new(StringComparer.Ordinal);

	internal Dictionary<string, int> AnalysisAnchors { get; } = new(StringComparer.Ordinal);

	internal List<BranchFixup> Branches { get; } = new();

	internal List<AddressFixup> Addresses { get; } = new();

	internal List<PcRelativeFixup> PcRelative { get; } = new();

	internal Dictionary<int, M68kInstructionEffects> InstructionEffectOverrides { get; } = new();

	internal int? DataStartOffset { get; private set; }

	internal bool HasLabelAt(int offset) => Labels.Values.Contains(offset);

	internal void MarkDataStart() => DataStartOffset ??= Bytes.Count;

	internal ushort ReadWord(int offset) =>
		(ushort)((Bytes[offset] << 8) | Bytes[offset + 1]);

	internal uint ReadLong(int offset) =>
		((uint)ReadWord(offset) << 16) | ReadWord(offset + 2);

	internal void WriteWord(int offset, int value)
	{
		Bytes[offset] = (byte)(value >> 8);
		Bytes[offset + 1] = (byte)value;
	}

	internal void InsertBytes(int offset, int count)
	{
		Bytes.InsertRange(offset, Enumerable.Repeat((byte)0, count));
		if (DataStartOffset is { } dataStartOffset && dataStartOffset >= offset)
		{
			DataStartOffset = dataStartOffset + count;
		}

		foreach (var label in Labels.Keys.ToArray())
		{
			if (Labels[label] > offset)
			{
				Labels[label] += count;
			}
		}
		foreach (var anchor in AnalysisAnchors.Keys.ToArray())
		{
			if (AnalysisAnchors[anchor] > offset)
			{
				AnalysisAnchors[anchor] += count;
			}
		}

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

		foreach (var label in Labels.Keys.ToArray())
		{
			var value = Labels[label];
			if (value >= end)
			{
				Labels[label] = value - count;
			}
		}
		foreach (var anchor in AnalysisAnchors.Keys.ToArray())
		{
			var value = AnalysisAnchors[anchor];
			if (value >= end)
			{
				AnalysisAnchors[anchor] = value - count;
			}
			else if (value > offset)
			{
				AnalysisAnchors[anchor] = offset;
			}
		}

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
			.OrderBy(static instructionOffset => instructionOffset)
			.ToArray())
		{
			var effects = InstructionEffectOverrides[instructionOffset];
			InstructionEffectOverrides.Remove(instructionOffset);
			InstructionEffectOverrides.Add(instructionOffset - count, effects);
		}
	}
}

internal readonly record struct BranchFixup(
	int OpcodeOffset,
	string Target,
	bool CanRelaxToShort = true);

internal readonly record struct AddressFixup(int Offset, string Target, bool External);

internal readonly record struct PcRelativeFixup(int DisplacementOffset, string Target);
