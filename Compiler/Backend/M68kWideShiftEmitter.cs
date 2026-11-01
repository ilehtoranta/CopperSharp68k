/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;

namespace CopperSharp.Compiler.Backend;

// D0:D1 contains the high and low halves. D2 is the variable count/scratch;
// variable shifts also use D3. The IR must model these clobbers.
internal static class M68kWideShiftEmitter
{
	internal static void Emit(M68kAssembler assembler, OpCode operation, int? immediate,
		Func<string, string> uniqueLabel)
	{
		if (operation != OpCodes.Shl && operation != OpCodes.Shr && operation != OpCodes.Shr_Un)
			throw new ArgumentOutOfRangeException(nameof(operation));
		var left = operation == OpCodes.Shl;
		var arithmetic = operation == OpCodes.Shr;
		if (immediate is { } value)
		{
			var count = value & 63;
			if (count <= 3)
			{
				for (var bit = 0; bit < count; bit++) PairBit();
			}
			else if (count >= 32)
			{
				assembler.EmitWord(left ? (ushort)0x2001 : (ushort)0x2200); // MOVE.L D1,D0 / D0,D1
				HalfImmediate(left ? OpCodes.Shl : operation, left ? 0 : 1, count - 32);
				if (left) assembler.EmitWord(0x4281); // CLR.L D1
				else FillHigh();
			}
			else if (left)
			{
				assembler.EmitWord(0x2401); // MOVE.L D1,D2
				HalfImmediate(OpCodes.Shl, 0, count);
				HalfImmediate(OpCodes.Shr_Un, 2, 32 - count);
				assembler.EmitWord(0x8082); // OR.L D2,D0
				HalfImmediate(OpCodes.Shl, 1, count);
			}
			else
			{
				assembler.EmitWord(0x2400); // MOVE.L D0,D2
				HalfImmediate(OpCodes.Shl, 2, 32 - count);
				HalfImmediate(OpCodes.Shr_Un, 1, count);
				assembler.EmitWord(0x8282); // OR.L D2,D1
				HalfImmediate(operation, 0, count);
			}
			return;
		}

		var done = uniqueLabel("wide_shift_done");
		var general = uniqueLabel("wide_shift_general");
		var wide = uniqueLabel("wide_shift_high_half");
		var smallLoop = uniqueLabel("wide_shift_small");
		assembler.EmitWord(0x0282); assembler.EmitLong(63); // ANDI.L #63,D2
		assembler.EmitBranch(M68kCondition.Equal, done);
		assembler.EmitWord(0x0C82); assembler.EmitLong(4); // CMPI.L #4,D2
		assembler.EmitBranch(M68kCondition.CarryClear, general);
		assembler.Mark(smallLoop);
		PairBit();
		assembler.EmitWord(0x5382); // SUBQ.L #1,D2
		assembler.EmitBranch(M68kCondition.NotEqual, smallLoop);
		assembler.EmitBranch(M68kCondition.True, done);
		assembler.Mark(general);
		assembler.EmitWord(0x0C82); assembler.EmitLong(32); // CMPI.L #32,D2
		assembler.EmitBranch(M68kCondition.CarryClear, wide);
		assembler.EmitWord(left ? (ushort)0x2601 : (ushort)0x2600); // MOVE.L D1/D0,D3
		HalfRegister(left ? OpCodes.Shl : operation, 0);
		HalfRegister(left ? OpCodes.Shl : OpCodes.Shr_Un, 1);
		assembler.EmitWord(0x4482); // NEG.L D2
		assembler.EmitWord(0x0682); assembler.EmitLong(32); // ADDI.L #32,D2
		HalfRegister(left ? OpCodes.Shr_Un : OpCodes.Shl, 3);
		assembler.EmitWord(left ? (ushort)0x8083 : (ushort)0x8283); // OR.L D3,D0/D1
		assembler.EmitBranch(M68kCondition.True, done);
		assembler.Mark(wide);
		assembler.EmitWord(0x0482); assembler.EmitLong(32); // SUBI.L #32,D2
		assembler.EmitWord(left ? (ushort)0x2001 : (ushort)0x2200);
		HalfRegister(left ? OpCodes.Shl : operation, left ? 0 : 1);
		if (left) assembler.EmitWord(0x4281);
		else FillHigh();
		assembler.Mark(done);

		void FillHigh()
		{
			if (!arithmetic) assembler.EmitWord(0x4280); // CLR.L D0
			else
			{
				assembler.EmitWord(0x741F); // MOVEQ #31,D2
				HalfRegister(OpCodes.Shr, 0);
			}
		}
		void PairBit()
		{
			assembler.EmitWord(left ? (ushort)0xE389 : arithmetic ? (ushort)0xE280 : (ushort)0xE288);
			assembler.EmitWord(left ? (ushort)0xE390 : (ushort)0xE291);
		}
		void HalfRegister(OpCode op, int register) =>
			assembler.EmitWord((ushort)((Base(op) & ~(7 << 9)) | (2 << 9) | 0x20 | register));
		void HalfImmediate(OpCode op, int register, int count)
		{
			while (count != 0)
			{
				var chunk = Math.Min(count, 8);
				assembler.EmitWord((ushort)((Base(op) & ~(7 << 9)) | ((chunk & 7) << 9) | register));
				count -= chunk;
			}
		}
		static int Base(OpCode op) => op == OpCodes.Shl ? 0xE388 : op == OpCodes.Shr ? 0xE280 : 0xE288;
	}
}
