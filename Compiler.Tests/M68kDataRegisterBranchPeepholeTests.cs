/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDataRegisterBranchPeepholeTests
{
	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Bounded)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Bounded)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.Bounded)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	public void PreservesDataCopyUsedByLoopAfterUnconditionalBranch(
		M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		var baseline = CreateFixture();
		var optimized = CreateFixture();
		optimized.OptimizeForCpu(target, peepholeOptimization: mode);

		Assert.Equal(0x091A_2B3Cu, Execute(baseline, model));
		Assert.Equal(Execute(baseline, model), Execute(optimized, model));
	}

	private static M68kAssembler CreateFixture()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:branch-copy");
		assembler.EmitWord(0x2202); // MOVE.L D2,D1; saved high word in Eval
		assembler.EmitWord(0x2401); // MOVE.L D1,D2; redundant copy back
		assembler.EmitWord(0x4A83); // TST.L D3; discard MOVE condition codes
		assembler.EmitBranch(M68kCondition.True, "loop-test");
		assembler.Mark("loop");
		assembler.EmitWord(0xE289); // LSR.L #1,D1; reads the saved value
		assembler.EmitWord(0x5383); // SUBQ.L #1,D3
		assembler.Mark("loop-test");
		assembler.EmitWord(0x4A83); // TST.L D3
		assembler.EmitBranch(M68kCondition.NotEqual, "loop");
		assembler.EmitWord(0x2081); // MOVE.L D1,(A0); no A1 use on this path
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:branch-copy:end");
		return assembler;
	}

	private static uint Execute(M68kAssembler assembler, M68kCpuModel model)
	{
		const uint codeAddress = 0x0001_0000;
		const uint stackPointer = 0x0008_0000;
		const uint returnSentinel = 0x0000_1000;
		const uint outputAddress = 0x0000_3000;
		var linked = assembler.Link(codeAddress, new Dictionary<string, uint>());
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)codeAddress));
		bus.WriteLong(stackPointer, returnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(codeAddress, stackPointer);
		cpu.State.D[1] = 0xA5A5_A5A5;
		cpu.State.D[2] = 0x1234_5678;
		cpu.State.D[3] = 1;
		cpu.State.A[0] = outputAddress;

		for (var instruction = 0; instruction < 64; instruction++)
		{
			if (cpu.State.ProgramCounter == returnSentinel)
			{
				Assert.Equal(stackPointer + 4, cpu.State.A[7]);
				return bus.ReadLong(outputAddress);
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		throw new Xunit.Sdk.XunitException("Branch-copy fixture did not return.");
	}
}
