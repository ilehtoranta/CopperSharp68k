/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kWideShiftEmitterTests
{
	[Theory]
	[InlineData(M68kCpuModel.M68000, false)]
	[InlineData(M68kCpuModel.M68000, true)]
	[InlineData(M68kCpuModel.M68020, false)]
	[InlineData(M68kCpuModel.M68020, true)]
	[InlineData(M68kCpuModel.M68040, false)]
	[InlineData(M68kCpuModel.M68040, true)]
	public void EveryBitAndCountMatchesClrWithinBoundedInstructionBudget(M68kCpuModel model, bool immediate)
	{
		var values = Enumerable.Range(0, 64).Select(bit => 1UL << bit)
			.Concat(new ulong[] { 0, ulong.MaxValue, 0x89abcdef01234567, 0x0123456789abcdef }).ToArray();
		var counts = Enumerable.Range(0, 64).Concat(new[] { -65, -1, 64, 65, 127, int.MinValue, int.MaxValue });
		var bus = new TestBus(0x4000);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		foreach (var operation in new[] { OpCodes.Shl, OpCodes.Shr, OpCodes.Shr_Un })
		foreach (var count in counts)
		{
			var assembler = new M68kAssembler();
			M68kWideShiftEmitter.Emit(assembler, operation, immediate ? count : null, name => name);
			assembler.EmitWord(0x4E75);
			var code = assembler.Link(0x100, new Dictionary<string, uint>()).Bytes;
			code.CopyTo(bus.Memory, 0x100);
			foreach (var value in values)
			{
				bus.WriteLong(0x3F00, 0x20);
				cpu.Reset(0x100, 0x3F00);
				for (var register = 0; register < 8; register++) cpu.State.D[register] = 0xA5A50000u + (uint)register;
				for (var register = 0; register < 7; register++) cpu.State.A[register] = 0x2000u + (uint)register * 4;
				cpu.State.D[0] = (uint)(value >> 32);
				cpu.State.D[1] = (uint)value;
				cpu.State.D[2] = unchecked((uint)count);
				for (var instruction = 0; instruction < 40 && cpu.State.ProgramCounter != 0x20; instruction++)
					cpu.ExecuteInstruction();
				Assert.False(cpu.State.Halted);
				Assert.Equal(0x20u, cpu.State.ProgramCounter);
				var expected = operation == OpCodes.Shl ? value << count
					: operation == OpCodes.Shr ? unchecked((ulong)((long)value >> count)) : value >> count;
				Assert.Equal(expected, ((ulong)cpu.State.D[0] << 32) | cpu.State.D[1]);
				for (var register = immediate ? 3 : 4; register < 8; register++)
					Assert.Equal(0xA5A50000u + (uint)register, cpu.State.D[register]);
				for (var register = 0; register < 7; register++)
					Assert.Equal(0x2000u + (uint)register * 4, cpu.State.A[register]);
				Assert.Equal(0x3F04u, cpu.State.A[7]);
			}
		}
	}
}
