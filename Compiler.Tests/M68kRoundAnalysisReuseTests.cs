/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kRoundAnalysisReuseTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!;

	[Fact]
	public void SpillCleanupDoesNotRebuildSuppliedAnalysis()
	{
		var (assembler, buffer) = Create(false);
		assembler.MarkDataStart();
		for (var i = 0; i < 4096; i++) assembler.EmitAddress("entry");
		// A barrier leaves the candidate unchanged so the warmed helper can be
		// measured with exactly the same immutable analysis and buffer.
		var expression = assembler.GetInstructionStream().Single(instruction => instruction.Offset == 4);
		assembler.SetInstructionEffects(4, M68kInstructionDataflow.GetEffects(expression) with { IsBarrier = true });
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var dataflow = M68kInstructionDataflow.Analyze(assembler, assembler.AddressFixupOffsets);
			var optimizer = new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
			var cleanup = typeof(M68kPeepholeOptimizer).GetMethod("TryRemoveRedundantRegisterSpill", PrivateInstance)!
				.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer);
			for (var i = 0; i < 3; i++) Assert.False(cleanup(dataflow));
			var start = GC.GetAllocatedBytesForCurrentThread();
			var changed = cleanup(dataflow);
			var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
			Assert.False(changed);
			Assert.True(allocated < 32768, $"Spill cleanup allocated {allocated} bytes with supplied analysis.");
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SpillCleanupWithRoundAnalysisPreservesCompiledMc68000Behavior(bool immediate)
	{
		var (assembler, buffer) = Create(immediate);
		var before = buffer.Bytes.ToArray();
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var dataflow = M68kInstructionDataflow.Analyze(assembler, assembler.AddressFixupOffsets);
			Assert.True(Cleanup(assembler, buffer, dataflow));
		}
		finally { assembler.EndInstructionAnalysisRound(); }
		var expected = immediate
			? new ushort[] { 0x243C, 0x1234, 0x5678, 0x5283, 0x4E75 }
			: new ushort[] { 0x5283, 0x4E75 };
		Assert.Equal(expected.SelectMany(word => new[] { (byte)(word >> 8), (byte)word }), buffer.Bytes);
		foreach (var value in new uint[] { 0, 1, 0x7FFF_FFFF, 0x8000_0000, 0xFFFF_FFFF })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(before, value, flags), Execute(buffer.Bytes.ToArray(), value, flags));
	}

	[Theory]
	[InlineData("barrier")]
	[InlineData("memory read")]
	[InlineData("memory write")]
	[InlineData("stack read")]
	[InlineData("stack write")]
	[InlineData("condition read")]
	[InlineData("stack delta")]
	public void SpillCleanupRetainsAnnotatedRoundEffects(string guard)
	{
		var (assembler, buffer) = Create(false);
		var expression = assembler.GetInstructionStream().Single(instruction => instruction.Offset == 4);
		var effects = M68kInstructionDataflow.GetEffects(expression);
		effects = guard switch
		{
			"barrier" => effects with { IsBarrier = true },
			"memory read" => effects with { ReadsMemory = M68kMemorySet.Unknown },
			"memory write" => effects with { WritesMemory = M68kMemorySet.Unknown },
			"stack read" => effects with { UsesAddress = 0x80 },
			"stack write" => effects with { DefinesAddress = 0x80 },
			"condition read" => effects with { ReadsConditions = M68kConditionCodeSet.Zero },
			_ => effects with { StackDelta = 4 }
		};
		assembler.SetInstructionEffects(expression.Offset, effects);
		var bytes = buffer.Bytes.ToArray();
		var dataflow = M68kInstructionDataflow.Analyze(assembler, assembler.AddressFixupOffsets);
		Assert.True(dataflow.TryGetFacts(expression.Offset, out var facts));
		Assert.Equal(effects, facts.Effects);
		Assert.False(Cleanup(assembler, buffer, dataflow));
		Assert.Equal(bytes, buffer.Bytes);
	}

	[Fact]
	public void LaterRoundUsesRebasedEffectsAndFixups()
	{
		var (assembler, buffer) = Create(false);
		assembler.MarkDataStart(); assembler.EmitAddress("entry");
		var first = M68kInstructionDataflow.Analyze(assembler, assembler.AddressFixupOffsets);
		var expression = first.Instructions.Single(instruction => instruction.Offset == 4);
		assembler.SetInstructionEffects(4, M68kInstructionDataflow.GetEffects(expression) with { IsBarrier = true });
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var snapshot = assembler.AddressFixupOffsets;
			Assert.Equal(new[] { 12 }, snapshot);
			var current = M68kInstructionDataflow.Analyze(assembler, snapshot);
			Assert.True(current.TryGetFacts(6, out var facts));
			Assert.True(facts.Effects.IsBarrier);
			Assert.False(Cleanup(assembler, buffer, current));
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) Create(bool immediate)
	{
		var assembler = new M68kAssembler(); assembler.Mark("entry");
		assembler.EmitWord((ushort)(immediate ? 0x2F3C : 0x2F02)); // MOVE.L #value/D2,-(SP)
		if (immediate) assembler.EmitLong(0x12345678);
		assembler.EmitWord(0x2417); // MOVE.L (SP),D2
		assembler.EmitWord(0x5283); // ADDQ.L #1,D3
		assembler.EmitWord(0x588F); // ADDQ.L #4,SP
		assembler.EmitWord(0x4E75);
		return (assembler, (M68kAssemblyBuffer)BufferField.GetValue(assembler)!);
	}

	private static bool Cleanup(M68kAssembler assembler, M68kAssemblyBuffer buffer, M68kInstructionDataflow dataflow)
	{
		var optimizer = new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		return typeof(M68kPeepholeOptimizer).GetMethod("TryRemoveRedundantRegisterSpill", PrivateInstance)!
			.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer)(dataflow);
	}

	private static (uint D2, uint D3, uint Sp, ushort Flags) Execute(byte[] code, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[2] = value; cpu.State.D[3] = value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 16 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[2], cpu.State.D[3], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
