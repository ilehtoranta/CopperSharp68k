/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool TryDeferGeneratedNormalization(M68kInstructionDataflow dataflow)
	{
		if (_assembler.GeneratedCodeSizeOptions?.NarrowOperations != true) return false;
		var instructions = dataflow.Instructions;
		for (var i = 0; i < instructions.Count; i++)
		{
			var mask = instructions[i];
			if (mask.Length != 6 || (mask.Opcode & 0xFFF8) != 0x0280 ||
				mask.ExtensionLong is not (0xFF or 0xFFFF) || HasInternalLabel(mask) ||
				!dataflow.TryGetFacts(mask.Offset, out var maskFacts) || !maskFacts.ConditionsAreDeadAfter) continue;
			var register = mask.Opcode & 7;
			var width = mask.ExtensionLong == 0xFF ? 0x1000 : 0x3000;
			var lowUse = false;
			for (var j = i + 1; j < Math.Min(i + 16, instructions.Count); j++)
			{
				var use = instructions[j];
				if (!GeneratedSizeEligible(mask.Offset, use.Offset + use.Length) || HasFrameTransferBoundary(use) ||
					use.Kind != M68kInstructionKind.Normal || !dataflow.TryGetFacts(use.Offset, out var facts) || facts.Effects.IsBarrier) break;
				if ((facts.Effects.UsesData & (1 << register)) != 0)
				{
					// Stores read only the selected byte/word. Keep all long, address,
					// arithmetic and unknown consumers, including partial updates.
					if ((use.Opcode & 0xF000) != width || (use.Opcode & 0x3F) != register ||
						(use.Opcode >> 6 & 7) is not (2 or 5 or 6)) break;
					lowUse = true;
				}
				if (lowUse && (facts.LiveDataAfter & (1 << register)) == 0)
				{
					_buffer.RemoveBytes(mask.Offset, mask.Length);
					_assembler.RecordCodeSizeRewrite(nameof(M68kCodeSizeOptions.NarrowOperations), mask.Length);
					return true;
				}
				if ((facts.Effects.DefinesData & (1 << register)) != 0) break;
			}
		}
		return false;
	}
}
