/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryFoldLiteralAddressOffset(M68kInstructionDataflow dataflow)
	{
		var instructions = ExecutableCandidates(dataflow);
		for (var index = 0; index + 2 < instructions.Count; index++)
		{
			var address = instructions[index];
			var constant = instructions[index + 1];
			var add = instructions[index + 2];
			if ((address.Opcode & 0xF1FF) != 0x203C || address.Length != 6 ||
				!((constant.Opcode & 0xF100) == 0x7000 && constant.Length == 2 ||
				  (constant.Opcode & 0xF1FF) == 0x203C && constant.Length == 6) ||
				(add.Opcode & 0xF1F8) is not (0xD080 or 0x9080) || add.Length != 2 ||
				constant.Offset != address.Offset + address.Length ||
				add.Offset != constant.Offset + constant.Length ||
				HasInternalLabel(address) || HasAddressFixup(constant) || HasAddressFixup(add) ||
				!CanFoldAcrossUnreferencedIlLabels(address.Offset, add.Offset + add.Length)) continue;
			var destination = (address.Opcode >> 9) & 7;
			var temporary = (constant.Opcode >> 9) & 7;
			if (destination == temporary || ((add.Opcode >> 9) & 7) != destination ||
				(add.Opcode & 7) != temporary ||
				!dataflow.GetDataValueBefore(add.Offset, temporary).IsExact(out var value) ||
				!dataflow.TryGetFacts(add.Offset, out var facts) ||
				(facts.LiveConditionsAfter & facts.Effects.WritesConditions) != M68kConditionCodeSet.None)
				continue;
			var fixupIndex = _buffer.Addresses.FindIndex(f => f.Offset == address.Offset + 2);
			if (fixupIndex < 0) continue;
			var fixup = _buffer.Addresses[fixupIndex];
			var delta = (add.Opcode & 0xF000) == 0x9000 ? -(long)unchecked((int)value) : unchecked((int)value);
			var addend = fixup.Addend + delta;
			if (addend is < int.MinValue or > int.MaxValue ||
				fixup.StoreAddressScratchRegister is not null || fixup.NativeTailRestore) continue;
			// Code compaction can change the distance between two executable
			// positions. Fold internal offsets only within the trailing data area,
			// where the base and both derived addresses move together.
			if (!fixup.External && !StableDataOffset(fixup.Target, fixup.Addend, addend)) continue;
			_buffer.Addresses[fixupIndex] = fixup with { Addend = (int)addend };
			// An opaque call can observe the offset register too. Retain its
			// materialization in that case; only the redundant ADD disappears.
			var removeConstant = (facts.LiveDataAfter & (1 << temporary)) == 0;
			var start = removeConstant ? constant.Offset : add.Offset;
			MoveLabelsToOffset(start, add.Offset + add.Length, address.Offset);
			_buffer.RemoveBytes(start, add.Offset + add.Length - start);
			return true;
		}
		return false;
	}

	private bool StableDataOffset(string label, int original, long folded)
	{
		if (_buffer.DeferredDataSizes.TryGetValue(label, out var length))
			return original >= 0 && original < length && folded >= 0 && folded < length;
		return _buffer.Labels.TryGetValue(label, out var target) &&
			_buffer.DataStartOffset is { } dataStart && target >= dataStart &&
			target + (long)original >= dataStart && target + (long)original < _buffer.Bytes.Count &&
			target + folded >= dataStart && target + folded < _buffer.Bytes.Count;
	}
}
