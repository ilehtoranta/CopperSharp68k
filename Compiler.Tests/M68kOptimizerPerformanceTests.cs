/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class M68kOptimizerPerformanceCollection
{
	public const string Name = "M68k optimizer performance";
}

[Collection(M68kOptimizerPerformanceCollection.Name)]
public sealed class M68kOptimizerPerformanceTests
{
	[Fact(Timeout = 10_000)]
	public void BoundedPeepholeCompletesManyIndependentSingleRewriteCandidates()
	{
		const int candidateCount = 4_096;
		var first = ManyIndependentAddressZeroMoves(candidateCount);
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();
		first.OptimizeForCpu(M68kCpuTarget.M68000,
			peepholeOptimization: M68kPeepholeOptimizationMode.Bounded);
		stopwatch.Stop();

		var firstInstructions = first.GetInstructionStream();
		Assert.Equal(8, firstInstructions.Count(static instruction =>
			instruction.Opcode == 0x91C8));
		Assert.Equal(candidateCount - 8, firstInstructions.Count(static instruction =>
			instruction.Opcode == 0x207C && instruction.ExtensionLong == 0));
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
			$"Bounded optimizer took {stopwatch.Elapsed.TotalSeconds:N3}s.");

		var second = ManyIndependentAddressZeroMoves(candidateCount);
		second.OptimizeForCpu(M68kCpuTarget.M68000,
			peepholeOptimization: M68kPeepholeOptimizationMode.Bounded);
		Assert.Equal(first.GetInstructionStream(), second.GetInstructionStream());
	}

	[Fact(Timeout = 10_000)]
	public void FixedPointBatchesIndependentRewritesBeforeReanalyzing()
	{
		const int candidateCount = 4_096;
		var assembler = ManyIndependentAddressZeroMoves(candidateCount);
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		assembler.OptimizeForCpu(M68kCpuTarget.M68000,
			peepholeOptimization: M68kPeepholeOptimizationMode.FixedPoint);
		stopwatch.Stop();

		Assert.DoesNotContain(assembler.GetInstructionStream(), instruction =>
			instruction.Opcode == 0x207C && instruction.ExtensionLong == 0);
		Assert.True(
			assembler.PeepholeOptimizationStatistics.Rewrites >= candidateCount);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(5),
			$"Fixed-point batch took {stopwatch.Elapsed.TotalSeconds:N3}s.");
	}

	[Fact(Timeout = 10_000)]
	public void FixedPointBatchesZeroDisplacementMemoryCopies()
	{
		const int candidateCount = 512;
		var assembler = new M68kAssembler();
		for (var index = 0; index < candidateCount; index++)
		{
			assembler.EmitWord(0x2028); // MOVE.L 0(A0),D0
			assembler.EmitWord(0);
			assembler.EmitWord(0x2340); // MOVE.L D0,0(A1)
			assembler.EmitWord(0);
			assembler.EmitWord(0x7000); // MOVEQ #0,D0 makes the copy temporary dead.
		}
		assembler.EmitWord(0x4E75); // RTS
		var stopwatch = System.Diagnostics.Stopwatch.StartNew();

		assembler.OptimizeForCpu(
			M68kCpuTarget.M68000,
			peepholeOptimization: M68kPeepholeOptimizationMode.FixedPoint);
		stopwatch.Stop();

		var instructions = assembler.GetInstructionStream();
		Assert.Equal(candidateCount,
			instructions.Count(static instruction => instruction.Opcode == 0x2290));
		Assert.DoesNotContain(instructions, instruction =>
			instruction.Opcode is 0x2028 or 0x2340);
		Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(8),
			$"Zero-displacement memory-copy batch took " +
			$"{stopwatch.Elapsed.TotalSeconds:N3}s.");
	}

	private static M68kAssembler ManyIndependentAddressZeroMoves(int count)
	{
		var assembler = new M68kAssembler();
		for (var index = 0; index < count; index++)
		{
			assembler.Mark($"candidate:{index}");
			assembler.EmitWord(0x207C); // MOVE.L #0,A0
			assembler.EmitLong(0);
			assembler.EmitWord(0x4E75); // RTS
		}
		return assembler;
	}
}
