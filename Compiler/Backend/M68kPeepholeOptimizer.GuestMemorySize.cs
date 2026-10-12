/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryCompactGeneratedMemory(M68kInstructionDataflow dataflow)
	{
		if (_assembler.GeneratedCodeSizeOptions?.CompactGuestMemory != true) return false;
		var instructions = dataflow.Instructions;
		for (var i = 0; i + 1 < instructions.Count; i++)
		{
			var load = instructions[i];
			var test = instructions[i + 1];
			var family = load.Opcode & 0xF000;
			var width = family == 0x1000 ? 0 : family == 0x3000 ? 0x40 : 0x80;
			var ea = load.Opcode & 0x3F;
			if (family is 0x1000 or 0x2000 or 0x3000 && (load.Opcode >> 6 & 7) == 0 &&
				(ea >> 3) is 2 or 5 or 6 && test.Offset == load.Offset + load.Length &&
				test.Length == 2 && test.Opcode == (0x4A00 | width | (load.Opcode >> 9 & 7)) &&
				GeneratedSizeEligible(load.Offset, test.Offset + test.Length) && !HasInternalLabel(load) &&
				!HasFrameTransferBoundary(test) && dataflow.TryGetFacts(test.Offset, out var facts) &&
				(facts.LiveDataAfter & (1 << (load.Opcode >> 9 & 7))) == 0 &&
				!_assembler.TryGetInstructionEffects(load.Offset, out _) && !_assembler.TryGetInstructionEffects(test.Offset, out _))
			{
				_buffer.WriteWord(load.Offset, (ushort)(0x4A00 | width | ea));
				_buffer.RemoveBytes(test.Offset, test.Length);
				_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.CompactGuestMemory), 2);
				return true;
			}
			// The existing indexed pass handles adjacent ADDA/access. This form
			// also allows the independent MOVEQ zero used by canonical byte loads.
			var copy = load;
			if (copy.Length != 2 || (copy.Opcode & 0xF1F8) != 0x2048) continue;
			var baseRegister = copy.Opcode & 7;
			var temporary = copy.Opcode >> 9 & 7;
			if (baseRegister == temporary || baseRegister == 7 || temporary == 7) continue;
			for (var j = i + 1; j <= Math.Min(i + 3, instructions.Count - 3); j++)
			{
				var add = instructions[j];
				var zero = instructions[j + 1];
				var access = instructions[j + 2];
				var sourceMode = access.Opcode >> 3 & 7;
				if (add.Length != 2 || (add.Opcode & 0xF1F8) != 0xD1C0 || (add.Opcode >> 9 & 7) != temporary ||
					zero.Length != 2 || (zero.Opcode & 0xF1FF) != 0x7000 ||
					(add.Opcode & 7) == (zero.Opcode >> 9 & 7) ||
					access.Length != (sourceMode == 5 ? 4 : 2) || sourceMode is not (2 or 5) ||
					(access.Opcode & 7) != temporary || (access.Opcode & 0xF1C0) is not (0x1000 or 0x3000 or 0x2000) ||
					(access.Opcode >> 9 & 7) != (zero.Opcode >> 9 & 7) ||
					sourceMode == 5 && unchecked((short)access.ExtensionWord) is < sbyte.MinValue or > sbyte.MaxValue ||
					!GeneratedSizeEligible(copy.Offset, access.Offset + access.Length) || HasInternalLabel(copy) ||
					HasFrameTransferBoundary(add) || HasFrameTransferBoundary(zero) || HasFrameTransferBoundary(access) ||
					!InterveningInstructionsPreserveBaseIndexAddress(instructions, i + 1, j, temporary, baseRegister, dataflow) ||
					!dataflow.TryGetFacts(access.Offset, out var accessFacts) ||
					(accessFacts.LiveAddressAfter & (1 << temporary)) != 0) continue;
				if (sourceMode == 2) _buffer.InsertBytes(access.Offset + 2, 2, shiftBoundaryLabels: true, shiftBoundaryAnchors: true);
				_buffer.WriteWord(access.Offset, (ushort)((access.Opcode & ~0x3F) | 0x30 | baseRegister));
				_buffer.WriteWord(access.Offset + 2, (ushort)(((add.Opcode & 7) << 12) | 0x0800 |
					(sourceMode == 5 ? (byte)access.ExtensionWord : 0)));
				_buffer.RemoveBytes(add.Offset, add.Length);
				_buffer.RemoveBytes(copy.Offset, copy.Length);
				_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.CompactGuestMemory), sourceMode == 5 ? 4 : 2);
				return true;
			}
		}
		return false;
	}
}
