/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kConditionPeepholeHandoffTests
{
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;
	private static M68kPeepholeOptimizer Peephole(M68kAssembler assembler, int budget = int.MaxValue) =>
		new(assembler, Buffer(assembler), M68kCpuTarget.M68000, M68kClrPolicy.Auto, [], budget);

	[Theory]
	[InlineData(false, false, false)]
	[InlineData(false, false, true)]
	[InlineData(false, true, false)]
	[InlineData(false, true, true)]
	[InlineData(true, false, false)]
	[InlineData(true, false, true)]
	[InlineData(true, true, false)]
	[InlineData(true, true, true)]
	public void FinalAnalysisHandoffPreservesPassResultsAndAccounting(bool scoped, bool conditionsRewrite, bool peepholeRewrite)
	{
		var separate = Create(scoped, conditionsRewrite, peepholeRewrite);
		var shared = Create(scoped, conditionsRewrite, peepholeRewrite);
		var expectedConditions = new M68kConditionCodeOptimizer(separate, Buffer(separate));
		var expectedPeephole = Peephole(separate);
		var conditions = new M68kConditionCodeOptimizer(shared, Buffer(shared)); var peephole = Peephole(shared);
		expectedConditions.Run(); expectedPeephole.Run(); conditions.RunThen(peephole);
		Assert.Equal(expectedConditions.Changed, conditions.Changed);
		Assert.Equal(expectedConditions.RewriteCount, conditions.RewriteCount);
		Assert.Equal(expectedPeephole.Changed, peephole.Changed);
		Assert.Equal(expectedPeephole.RewriteCount, peephole.RewriteCount);
		Assert.Equal(conditionsRewrite, conditions.Changed);
		Assert.Equal(separate.InstructionAnalysisBytes, shared.InstructionAnalysisBytes);
		Assert.Equal(separate.InstructionAnalysisRounds, shared.InstructionAnalysisRounds);
		Assert.Equal(separate.RenderAssembly(M68kCpuTarget.M68000), shared.RenderAssembly(M68kCpuTarget.M68000));
		Assert.Equal(separate.Link(0x10000, new Dictionary<string, uint>()).Bytes,
			shared.Link(0x10000, new Dictionary<string, uint>()).Bytes);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void LaterDefaultCallsReadCurrentEffectsFixupsAndRebasedScopes(bool scoped)
	{
		var separate = Create(scoped, true, true); var shared = Create(scoped, true, true);
		var conditions = new M68kConditionCodeOptimizer(shared, Buffer(shared)); var peephole = Peephole(shared, 1);
		var expectedConditions = new M68kConditionCodeOptimizer(separate, Buffer(separate)); var expectedPeephole = Peephole(separate, 1);
		expectedConditions.Run(); expectedPeephole.Run(); conditions.RunThen(peephole);
		foreach (var assembler in new[] { separate, shared })
		{
			var buffer = Buffer(assembler);
			buffer.Addresses[0] = buffer.Addresses[0] with { Addend = 4 };
			buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
			var first = assembler.GetInstructionStream().First(i => i.Offset == 2);
			assembler.SetInstructionEffects(2, M68kInstructionDataflow.GetEffects(first) with { IsBarrier = true });
		}
		// Default entry points do not retain or reuse a preceding handoff.
		expectedConditions.Run(); expectedPeephole.Run(); conditions.Run(); peephole.Run();
		Assert.Equal(Buffer(separate).Bytes, Buffer(shared).Bytes);
		Assert.Equal(separate.InstructionAnalysisBytes, shared.InstructionAnalysisBytes);
		Assert.Equal(separate.InstructionAnalysisRounds, shared.InstructionAnalysisRounds);
		Assert.Equal(Buffer(separate).Addresses, Buffer(shared).Addresses);
		Assert.Equal(separate.RenderAssembly(M68kCpuTarget.M68000), shared.RenderAssembly(M68kCpuTarget.M68000));
	}

	[Fact]
	public void HandoffRejectsAnOptimizerForAnotherAssembler()
	{
		var assembler = Create(false, false, false);
		var conditions = new M68kConditionCodeOptimizer(assembler, Buffer(assembler));
		Assert.Throws<ArgumentException>(() => conditions.RunThen(Peephole(Create(false, false, false))));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void HandoffRewritesPreserveCompiledMc68000BranchesStoresFlagsAndStack(bool scoped)
	{
		var original = Create(scoped, true, true).Link(0x10000, new Dictionary<string, uint>());
		var assembler = Create(scoped, true, true);
		new M68kConditionCodeOptimizer(assembler, Buffer(assembler)).RunThen(Peephole(assembler));
		var optimized = assembler.Link(0x10000, new Dictionary<string, uint>());
		Assert.True(optimized.Bytes.Length < original.Bytes.Length);
		foreach (var value in new uint[] { 0, 1, 0x80000000, uint.MaxValue })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(original, value, flags), Execute(optimized, value, flags));
	}

	private static M68kAssembler Create(bool scoped, bool conditionsRewrite, bool peepholeRewrite)
	{
		var assembler = new M68kAssembler(); assembler.Mark("entry");
		if (peepholeRewrite) { assembler.EmitWord(0x223C); assembler.EmitLong(7); }
		else assembler.EmitWord(0x7207);
		assembler.EmitWord(0x23C1); assembler.EmitAddress("data"); // Publish D1.
		assembler.EmitWord(0x4A82);
		if (conditionsRewrite) { assembler.EmitWord(0x0C82); assembler.EmitLong(0); }
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x700A); assembler.EmitWord(0x4E75);
		assembler.Mark("nonzero"); assembler.EmitWord(0x7014); assembler.EmitWord(0x4E75);
		assembler.Mark("entry:end"); assembler.MarkDataStart(); assembler.Mark("data"); assembler.EmitLong(0); assembler.EmitLong(0);
		if (scoped) assembler.SetAnalysisScope(null, "entry:end", ["entry", "nonzero", "entry:end"]);
		return assembler;
	}

	private static (uint D0, uint D2, uint Sp, ushort Flags, uint Stored) Execute(LinkedCode code, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.Bytes.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[2] = value; cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 32 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[2], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F), bus.ReadLong(load + (uint)code.Labels["data"]));
	}
}
