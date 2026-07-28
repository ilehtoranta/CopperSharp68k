/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed class M68kPeepholeOptimizer : IM68kOptimizerPass
{
	private readonly M68kAssembler _assembler;
	private readonly M68kAssemblyBuffer _buffer;

	public M68kPeepholeOptimizer(M68kAssembler assembler, M68kAssemblyBuffer buffer)
	{
		_assembler = assembler;
		_buffer = buffer;
	}

	public void Run()
	{
		bool changed;
		do
		{
			var dataflow = M68kInstructionDataflow.Analyze(_assembler);
			changed =
				TryFoldTailReturn() ||
				TryRemoveBranchToNextLabel() ||
				TryReplaceCompareZeroWithTest() ||
				TryRemoveRedundantTest() ||
				TryRemoveDeadTest(dataflow) ||
				TryNarrowAddition(dataflow) ||
				TryNarrowLogicalImmediate(dataflow) ||
				TryMergeStackAdjustments();
		}
		while (changed);
	}

	private bool TryRemoveDeadTest(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if ((instruction.Opcode & 0xFFF8) != 0x4A80 ||
				!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!facts.ConditionsAreDeadAfter)
			{
				continue;
			}

			_buffer.RemoveBytes(instruction.Offset, instruction.Length);
			return true;
		}

		return false;
	}

	private bool TryNarrowAddition(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				!facts.ConditionsAreDeadAfter)
			{
				continue;
			}

			var opcode = instruction.Opcode;
			if ((opcode & 0xFFF8) == 0x0680 &&
				instruction.Length == 6)
			{
				var destination = opcode & 7;
				if (!PreservesUpperWord(
					dataflow.GetDataValueBefore(instruction.Offset, destination),
					M68kValueRange.Exact(instruction.ExtensionLong)))
				{
					continue;
				}

				_buffer.WriteWord(instruction.Offset, 0x0640 | destination); // ADDI.L -> ADDI.W
				_buffer.RemoveBytes(instruction.Offset + 2, 2);
				return true;
			}

			if ((opcode & 0xF1F8) != 0xD080)
			{
				continue;
			}

			var source = opcode & 7;
			var destinationRegister = (opcode >> 9) & 7;
			if (!PreservesUpperWord(
				dataflow.GetDataValueBefore(instruction.Offset, destinationRegister),
				dataflow.GetDataValueBefore(instruction.Offset, source)))
			{
				continue;
			}

			_buffer.WriteWord(instruction.Offset, opcode - 0x40); // ADD.L Dm,Dn -> ADD.W Dm,Dn
			return true;
		}

		return false;
	}

	private static bool PreservesUpperWord(
		M68kValueRange destination,
		M68kValueRange source)
	{
		if (!TrySplitWordRange(destination, out var destinationLowMinimum, out var destinationLowMaximum, out _) ||
			!TrySplitWordRange(source, out var sourceLowMinimum, out var sourceLowMaximum, out var sourceUpper))
		{
			return false;
		}

		var minimumCarry = destinationLowMinimum + sourceLowMinimum > ushort.MaxValue;
		var maximumCarry = destinationLowMaximum + sourceLowMaximum > ushort.MaxValue;
		return minimumCarry == maximumCarry &&
			((sourceUpper + (minimumCarry ? 1u : 0u)) & ushort.MaxValue) == 0;
	}

	private static bool TrySplitWordRange(
		M68kValueRange range,
		out uint lowMinimum,
		out uint lowMaximum,
		out uint upper)
	{
		lowMinimum = 0;
		lowMaximum = 0;
		upper = 0;
		if (!range.IsKnown ||
			(range.Minimum >> 16) != (range.Maximum >> 16))
		{
			return false;
		}

		lowMinimum = range.Minimum & ushort.MaxValue;
		lowMaximum = range.Maximum & ushort.MaxValue;
		upper = range.Minimum >> 16;
		return true;
	}

	private bool TryNarrowLogicalImmediate(M68kInstructionDataflow dataflow)
	{
		foreach (var instruction in dataflow.Instructions)
		{
			if (!dataflow.TryGetFacts(instruction.Offset, out var facts) ||
				(facts.LiveConditionsAfter & (M68kConditionCodeSet.Negative | M68kConditionCodeSet.Zero)) !=
				M68kConditionCodeSet.None ||
				instruction.Length < 6 ||
				!TryReadLong(instruction.Offset + 2, out var immediate))
			{
				continue;
			}

			var opcode = instruction.Opcode;
			var dataRegister = opcode & 7;
			ushort wordOpcode;
			if ((opcode & 0xFFF8) == 0x0280 && (immediate & 0xFFFF0000u) == 0xFFFF0000u)
			{
				wordOpcode = (ushort)(0x0240 | dataRegister); // ANDI.L -> ANDI.W
			}
			else if ((opcode & 0xFFF8) == 0x0080 && (immediate & 0xFFFF0000u) == 0)
			{
				wordOpcode = (ushort)(0x0040 | dataRegister); // ORI.L -> ORI.W
			}
			else if ((opcode & 0xFFF8) == 0x0A80 && (immediate & 0xFFFF0000u) == 0)
			{
				wordOpcode = (ushort)(0x0A40 | dataRegister); // EORI.L -> EORI.W
			}
			else
			{
				continue;
			}

			_buffer.WriteWord(instruction.Offset, wordOpcode);
			_buffer.RemoveBytes(instruction.Offset + 2, 2);
			return true;
		}

		return false;
	}

	private bool TryFoldTailReturn()
	{
		for (var index = _buffer.Branches.Count - 1; index >= 0; index--)
		{
			var branch = _buffer.Branches[index];
			if (branch.OpcodeOffset + 5 >= _buffer.Bytes.Count ||
				_buffer.ReadWord(branch.OpcodeOffset) != 0x6100 ||
				_buffer.ReadWord(branch.OpcodeOffset + 4) != 0x4E75 ||
				_buffer.HasLabelAt(branch.OpcodeOffset + 4))
			{
				continue;
			}

			_buffer.WriteWord(branch.OpcodeOffset, 0x6000); // BSR.W -> BRA.W
			_buffer.Branches.RemoveAt(index);
			_buffer.Branches.Insert(index, branch);
			_buffer.RemoveBytes(branch.OpcodeOffset + 4, 2);
			return true;
		}

		for (var index = _buffer.Addresses.Count - 1; index >= 0; index--)
		{
			var address = _buffer.Addresses[index];
			var opcodeOffset = address.Offset - 2;
			if (opcodeOffset < 0 ||
				opcodeOffset + 7 >= _buffer.Bytes.Count ||
				_buffer.ReadWord(opcodeOffset) != 0x4EB9 ||
				_buffer.ReadWord(opcodeOffset + 6) != 0x4E75 ||
				_buffer.HasLabelAt(opcodeOffset + 6) ||
				address.External)
			{
				continue;
			}

			_buffer.WriteWord(opcodeOffset, 0x4EF9); // JSR -> JMP
			_buffer.RemoveBytes(opcodeOffset + 6, 2);
			return true;
		}

		return false;
	}

	private bool TryReplaceCompareZeroWithTest()
	{
		for (var offset = 0; offset + 3 < _buffer.Bytes.Count; offset += 2)
		{
			var opcode = _buffer.ReadWord(offset);
			if ((opcode & 0xFFF8) == 0x0C80 &&
				_buffer.ReadLong(offset + 2) == 0)
			{
				_buffer.WriteWord(offset, (ushort)(0x4A80 | (opcode & 7))); // CMPI.L #0,Dn -> TST.L Dn
				_buffer.RemoveBytes(offset + 2, 4);
				return true;
			}

			if ((opcode & 0xFFF8) == 0x0C40 &&
				_buffer.ReadWord(offset + 2) == 0)
			{
				_buffer.WriteWord(offset, (ushort)(0x4A40 | (opcode & 7))); // CMPI.W #0,Dn -> TST.W Dn
				_buffer.RemoveBytes(offset + 2, 2);
				return true;
			}

			if ((opcode & 0xFFF8) == 0x0C00 &&
				_buffer.ReadWord(offset + 2) == 0)
			{
				_buffer.WriteWord(offset, (ushort)(0x4A00 | (opcode & 7))); // CMPI.B #0,Dn -> TST.B Dn
				_buffer.RemoveBytes(offset + 2, 2);
				return true;
			}
		}

		return false;
	}

	private bool TryRemoveBranchToNextLabel()
	{
		for (var index = _buffer.Branches.Count - 1; index >= 0; index--)
		{
			var branch = _buffer.Branches[index];
			var opcode = _buffer.ReadWord(branch.OpcodeOffset);
			if ((opcode & 0xF000) != 0x6000 ||
				branch.OpcodeOffset + 3 >= _buffer.Bytes.Count ||
				!_buffer.Labels.TryGetValue(branch.Target, out var targetOffset) ||
				targetOffset != branch.OpcodeOffset + 4)
			{
				continue;
			}

			_buffer.Branches.RemoveAt(index);
			_buffer.RemoveBytes(branch.OpcodeOffset, 4);
			return true;
		}

		return false;
	}

	private bool TryRemoveRedundantTest()
	{
		for (var offset = 0; offset + 1 < _buffer.Bytes.Count; offset += 2)
		{
			if (!TryGetFlagSettingMoveLength(
				offset,
				out var moveLength,
				out var destination))
			{
				continue;
			}

			var testOffset = offset + moveLength;
			if (testOffset + 1 >= _buffer.Bytes.Count ||
				_buffer.HasLabelAt(testOffset))
			{
				continue;
			}

			var test = _buffer.ReadWord(testOffset);
			if ((test & 0xFFF8) != 0x4A80 ||
				destination != (test & 7))
			{
				continue;
			}

			_buffer.RemoveBytes(testOffset, 2);
			return true;
		}

		return false;
	}

	private bool TryMergeStackAdjustments()
	{
		for (var offset = 0; offset + 3 < _buffer.Bytes.Count; offset += 2)
		{
			if (_buffer.HasLabelAt(offset + 2) ||
				!TryGetStackAdjustment(offset, out var firstLength, out var firstBytes) ||
				!TryGetStackAdjustment(offset + firstLength, out var secondLength, out var secondBytes))
			{
				continue;
			}

			var total = checked(firstBytes + secondBytes);
			if (firstLength == 2 && secondLength == 2 && total <= 8)
			{
				_buffer.WriteWord(offset, EncodeAddQuick(total));
				_buffer.RemoveBytes(offset + 2, 2);
				return true;
			}

			if (firstLength == 4 && secondLength == 4 && total <= short.MaxValue)
			{
				_buffer.WriteWord(offset, 0xDEFC); // ADDA.W #bytes,A7
				_buffer.WriteWord(offset + 2, total);
				_buffer.RemoveBytes(offset + 4, 4);
				return true;
			}

			if (firstLength == 4 && secondLength == 2 && total <= short.MaxValue)
			{
				_buffer.WriteWord(offset + 2, total);
				_buffer.RemoveBytes(offset + 4, 2);
				return true;
			}

			if (firstLength == 2 && secondLength == 4 && total <= short.MaxValue)
			{
				_buffer.RemoveBytes(offset, 2);
				_buffer.WriteWord(offset, 0xDEFC); // ADDA.W #bytes,A7
				_buffer.WriteWord(offset + 2, total);
				return true;
			}
		}

		return false;
	}

	private bool TryGetStackAdjustment(int offset, out int length, out int bytes)
	{
		length = 0;
		bytes = 0;
		if (offset + 1 >= _buffer.Bytes.Count)
		{
			return false;
		}

		var opcode = _buffer.ReadWord(offset);
		if ((opcode & 0xF1FF) == 0x508F)
		{
			length = 2;
			bytes = QuickCount(opcode);
			return true;
		}

		if (opcode == 0xDEFC &&
			TryReadWord(offset + 2, out var wordBytes))
		{
			length = 4;
			bytes = wordBytes;
			return true;
		}

		return false;
	}

	private bool TryGetFlagSettingMoveLength(
		int offset,
		out int length,
		out int destination)
	{
		length = 0;
		destination = 0;
		if (offset + 1 >= _buffer.Bytes.Count)
		{
			return false;
		}

		var opcode = _buffer.ReadWord(offset);
		if ((opcode & 0xFFC0) is 0x4880 or 0x48C0)
		{
			destination = opcode & 7;
			length = 2;
			return true;
		}

		if ((opcode & 0xF000) != 0x2000 ||
			((opcode >> 6) & 7) != 0)
		{
			return false;
		}

		destination = (opcode >> 9) & 7;
		var sourceMode = (opcode >> 3) & 7;
		var sourceRegister = opcode & 7;
		length = sourceMode switch
		{
			0 or 1 or 2 or 3 or 4 => 2,
			5 or 6 => 4,
			7 => sourceRegister is 0 or 2 or 3 ? 4 :
				sourceRegister is 1 or 4 ? 6 : 0,
			_ => 0
		};
		return length != 0 && offset + length <= _buffer.Bytes.Count;
	}

	private bool TryReadWord(int offset, out ushort value)
	{
		if (offset + 1 >= _buffer.Bytes.Count)
		{
			value = 0;
			return false;
		}

		value = _buffer.ReadWord(offset);
		return true;
	}

	private bool TryReadLong(int offset, out uint value)
	{
		if (offset + 3 >= _buffer.Bytes.Count)
		{
			value = 0;
			return false;
		}

		value = _buffer.ReadLong(offset);
		return true;
	}

	private static ushort EncodeAddQuick(int bytes) =>
		(ushort)(0x508F | ((bytes == 8 ? 0 : bytes) << 9));

	private static int QuickCount(ushort opcode)
	{
		var count = (opcode >> 9) & 7;
		return count == 0 ? 8 : count;
	}
}
