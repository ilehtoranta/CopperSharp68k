/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryCleanupFrameMemoryTransfers(M68kInstructionDataflow dataflow)
	{
		var instructions = _assembler.GetExecutableInstructionStream();
		for (var index = 0; index + 1 < instructions.Count; index++)
		{
			var store = instructions[index];
			var next = instructions[index + 1];
			if (!store.IsDecoded || !next.IsDecoded ||
				(store.Opcode & 0xF000) != 0x2000 ||
				((store.Opcode >> 3) & 7) > 1 ||
				!TryGetMoveDestination(store, out var mode, out var baseRegister) ||
				mode is not (2 or 5) || store.Length != (mode == 5 ? 4 : 2) ||
				store.Offset + store.Length != next.Offset ||
				HasInternalLabel(store) ||
				!CanFoldAcrossUnreferencedIlLabels(store.Offset, store.Offset + store.Length) ||
				HasFrameTransferBoundary(next) ||
				_assembler.TryGetInstructionEffects(store.Offset, out _) ||
				_assembler.TryGetInstructionEffects(next.Offset, out _) ||
				dataflow.GetAddressAliasBefore(store.Offset, baseRegister).Kind !=
					M68kAddressAliasKind.Stack)
			{
				continue;
			}
			bool SameStore(M68kEmittedInstruction instruction) =>
				instruction.Opcode == store.Opcode && instruction.Length == store.Length &&
				(mode != 5 || instruction.ExtensionWord == store.ExtensionWord);
			if (SameStore(next))
			{
				// Same value/address/width and identical MOVE flags, including X.
				_buffer.RemoveBytes(next.Offset, next.Length);
				return true;
			}
			if ((next.Opcode & 0xF1C0) != 0x2040)
			{
				continue;
			}
			var reloadsFrame = ((next.Opcode >> 3) & 7) == mode &&
				(next.Opcode & 7) == baseRegister && next.Length == store.Length &&
				(mode != 5 || next.ExtensionWord == store.ExtensionWord);
			var copiesStoredRegister = next.Length == 2 &&
				(next.Opcode & 0x3F) == (store.Opcode & 0x3F);
			if (!reloadsFrame && !copiesStoredRegister)
			{
				continue;
			}
			var destination = (next.Opcode >> 9) & 7;
			if (destination == 7)
			{
				continue; // Never turn a frame reload into a stack-pointer update.
			}
			var redundantStores = new List<M68kEmittedInstruction>();
			if (destination != baseRegister)
			{
				var expectedStore = (store.Opcode & ~0x3F) | 8 | destination;
				var expectedOffset = next.Offset + next.Length;
				for (var repeatedIndex = index + 2; repeatedIndex < instructions.Count; repeatedIndex++)
				{
					var repeated = instructions[repeatedIndex];
					if (!repeated.IsDecoded || repeated.Opcode != expectedStore ||
						repeated.Length != store.Length || repeated.Offset != expectedOffset ||
						mode == 5 && repeated.ExtensionWord != store.ExtensionWord ||
						HasFrameTransferBoundary(repeated) ||
						_assembler.TryGetInstructionEffects(repeated.Offset, out _))
					{
						break;
					}
					redundantStores.Add(repeated);
					expectedOffset += repeated.Length;
				}
			}
			// The first store established the value and NZVC. MOVEA preserves
			// them, and each later store reproduces exactly those flags (not X).
			foreach (var redundant in redundantStores.AsEnumerable().Reverse())
			{
				_buffer.RemoveBytes(redundant.Offset, redundant.Length);
			}
			if (!reloadsFrame)
			{
				if (redundantStores.Count != 0) return true;
				continue;
			}
			_buffer.WriteWord(next.Offset,
				(ushort)(0x2040 | (destination << 9) | (store.Opcode & 0x3F)));
			if (next.Length > 2)
			{
				_buffer.RemoveBytes(next.Offset + 2, next.Length - 2);
			}
			return true;
		}
		return false;
	}

	private bool HasFrameTransferBoundary(M68kEmittedInstruction instruction) =>
		HasInternalLabel(instruction) || IsReferencedLabelAt(instruction.Offset) ||
		// Include the first byte: unlike the first store, these instructions
		// cannot have an independent entry that bypasses the established value.
		!CanFoldAcrossUnreferencedIlLabels(instruction.Offset - 1,
			instruction.Offset + instruction.Length);
}
