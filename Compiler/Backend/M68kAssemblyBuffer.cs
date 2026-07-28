/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kAssemblyBuffer
{
	internal List<byte> Bytes { get; } = new();

	internal Dictionary<string, int> Labels { get; } = new(StringComparer.Ordinal);

	internal List<BranchFixup> Branches { get; } = new();

	internal List<AddressFixup> Addresses { get; } = new();

	internal List<PcRelativeFixup> PcRelative { get; } = new();

	internal bool HasLabelAt(int offset) => Labels.Values.Contains(offset);

	internal ushort ReadWord(int offset) =>
		(ushort)((Bytes[offset] << 8) | Bytes[offset + 1]);

	internal uint ReadLong(int offset) =>
		((uint)ReadWord(offset) << 16) | ReadWord(offset + 2);

	internal void WriteWord(int offset, int value)
	{
		Bytes[offset] = (byte)(value >> 8);
		Bytes[offset + 1] = (byte)value;
	}

	internal void RemoveBytes(int offset, int count)
	{
		var end = checked(offset + count);
		Bytes.RemoveRange(offset, count);
		Branches.RemoveAll(branch =>
			branch.OpcodeOffset >= offset && branch.OpcodeOffset < end);
		Addresses.RemoveAll(address =>
			address.Offset >= offset && address.Offset < end);
		PcRelative.RemoveAll(fixup =>
			fixup.DisplacementOffset >= offset && fixup.DisplacementOffset < end);

		foreach (var label in Labels.Keys.ToArray())
		{
			var value = Labels[label];
			if (value >= end)
			{
				Labels[label] = value - count;
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
	}
}

internal readonly record struct BranchFixup(int OpcodeOffset, string Target);

internal readonly record struct AddressFixup(int Offset, string Target, bool External);

internal readonly record struct PcRelativeFixup(int DisplacementOffset, string Target);
