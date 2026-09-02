/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Numerics;
using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kCodeGenerator
{
	private IReadOnlyDictionary<int, M68kIntegerArithmeticPlan> _integerArithmeticPlans =
		new Dictionary<int, M68kIntegerArithmeticPlan>();

	private void PrepareAllocatedIntegerArithmetic(M68kAllocatedFunction allocated)
	{
		var instructions = allocated.Function.Blocks.SelectMany(static block => block.Instructions).ToArray();
		foreach (var instruction in instructions)
		{
			if (!_integerArithmeticPlans.TryGetValue(instruction.Id, out var plan) ||
				plan.Kind is M68kIntegerArithmeticKind.SignedWordMultiply or M68kIntegerArithmeticKind.UnsignedWordMultiply)
				continue;
			var constantOperand = plan.Kind == M68kIntegerArithmeticKind.FactoredWordMultiply
				? 1 - plan.SourceOperand : 1;
			if (!TryCollectExclusiveConstantChain(instructions, instruction.Uses[constantOperand], instruction, out var chain))
				continue;
			foreach (var constant in chain)
			{
				_allocatedSuppressedInstructions.Add(constant.Id);
				_allocatedFoldedCopyConstants.Remove(constant.Id);
			}
		}
	}

	private bool TryEmitAllocatedIntegerMultiply(M68kAllocatedFunction allocated, M68kMachineInstruction instruction)
	{
		if (!_integerArithmeticPlans.TryGetValue(instruction.Id, out var plan)) return false;
		switch (plan.Kind)
		{
			case M68kIntegerArithmeticKind.SignedWordMultiply:
			case M68kIntegerArithmeticKind.UnsignedWordMultiply:
				_assembler.EmitWord(plan.Kind == M68kIntegerArithmeticKind.SignedWordMultiply
					? (ushort)0xC1C1 : (ushort)0xC0C1); // MUL[SU].W D1,D0
				return true;
			case M68kIntegerArithmeticKind.FactoredWordMultiply:
				var source = allocated.Allocation.Registers[instruction.Uses[plan.SourceOperand]].Register;
				EmitAllocatedMove(source, M68kRegister.D0, M68kMachineValueWidth.Long);
				_assembler.EmitWord(0xC1FC); // MULS.W #wordFactor,D0
				_assembler.EmitWord(checked((ushort)plan.WordFactor));
				EmitAllocatedMultiplyByConstant(M68kRegister.D0, M68kRegister.D2, plan.RemainingFactor);
				EmitAllocatedMove(M68kRegister.D2, M68kRegister.D0, M68kMachineValueWidth.Long);
				return true;
			default:
				return false;
		}
	}

	private bool TryEmitAllocatedIntegerDivide(M68kMachineInstruction instruction)
	{
		if (!_integerArithmeticPlans.TryGetValue(instruction.Id, out var plan)) return false;
		var remainder = instruction.Operation == M68kMachineOperation.Remainder;
		var paired = instruction.Definitions.Length == 2;
		switch (plan.Kind)
		{
			case M68kIntegerArithmeticKind.UnsignedPowerOfTwoDivision:
				var shift = BitOperations.TrailingZeroCount(unchecked((uint)plan.Constant));
				var mask = unchecked((uint)plan.Constant) - 1;
				if (paired)
				{
					if (remainder)
					{
						EmitAllocatedMove(M68kRegister.D0, M68kRegister.D2, M68kMachineValueWidth.Long);
						EmitLogicalShift(M68kRegister.D2, shift);
						EmitMask(M68kRegister.D0, mask);
					}
					else
					{
						EmitAllocatedMove(M68kRegister.D0, M68kRegister.D3, M68kMachineValueWidth.Long);
						EmitMask(M68kRegister.D3, mask);
						EmitLogicalShift(M68kRegister.D0, shift);
					}
				}
				else if (remainder) EmitMask(M68kRegister.D0, mask);
				else EmitLogicalShift(M68kRegister.D0, shift);
				return true;
			case M68kIntegerArithmeticKind.SignedWordDivision:
			case M68kIntegerArithmeticKind.UnsignedWordDivision:
				var signed = plan.Kind == M68kIntegerArithmeticKind.SignedWordDivision;
				_assembler.EmitWord(signed ? (ushort)0x81FC : (ushort)0x80FC); // DIV[SU].W #constant,D0
				_assembler.EmitWord(unchecked((ushort)plan.Constant));
				if (paired)
				{
					var other = remainder ? M68kRegister.D2 : M68kRegister.D3;
					EmitAllocatedMove(M68kRegister.D0, other, M68kMachineValueWidth.Long);
					if (remainder) _assembler.EmitWord(0x4840); // SWAP D0
					else _assembler.EmitWord((ushort)(0x4840 | (int)other));
					NormalizeWord(other, signed);
				}
				else if (remainder) _assembler.EmitWord(0x4840); // SWAP D0
				NormalizeWord(M68kRegister.D0, signed);
				return true;
			default:
				return false;
		}

		void EmitMask(M68kRegister register, uint mask)
		{
			_assembler.EmitWord((ushort)(0x0280 | (int)register)); // ANDI.L #mask,Dn
			_assembler.EmitLong(mask);
		}
		void EmitLogicalShift(M68kRegister register, int count)
		{
			while (count != 0)
			{
				var part = Math.Min(8, count);
				EmitAllocatedShiftImmediate(register, left: false, part);
				count -= part;
			}
		}
		void NormalizeWord(M68kRegister register, bool signed)
		{
			if (signed) _assembler.EmitWord((ushort)(0x48C0 | (int)register)); // EXT.L Dn
			else EmitMask(register, ushort.MaxValue);
		}
	}

	private void EmitAllocatedGeneralDivide(M68kMachineInstruction instruction, bool divisorKnownNonZero)
	{
		var signed = instruction.SourceInstruction!.OpCode != OpCodes.Div_Un &&
			instruction.SourceInstruction.OpCode != OpCodes.Rem_Un;
		var remainder = instruction.Operation == M68kMachineOperation.Remainder;
		var paired = instruction.Definitions.Length == 2;
		EmitDivide(signed, remainder && !paired, divisorKnownNonZero);
		if (paired && remainder)
		{
			_assembler.EmitWord(0x2400); // MOVE.L D0,D2; preserve quotient for the second definition
			_assembler.EmitWord(0x2003); // MOVE.L D3,D0; primary remainder result
		}
	}
}
