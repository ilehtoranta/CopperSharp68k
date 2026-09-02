/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kFrameClearRunPlannerTests
{
	[Fact]
	public void RepeatedSetupRejectsAnOtherwiseLocallyProfitableLoop()
	{
		var displacements = Displacements([1, -1, 13, -1, 1]);
		Assert.Null(M68kFrameClearRunPlanner.Create(displacements, true, M68kFrameClearLoopKind.Scratch));
	}

	[Fact]
	public void PreservedLoopIsComparedAgainstTheActualZeroRegisterFallback()
	{
		var displacements = Displacements([33, -1, 33]);
		Assert.Null(M68kFrameClearRunPlanner.Create(displacements, true, M68kFrameClearLoopKind.PreserveData));
		var plan = M68kFrameClearRunPlanner.Create(displacements, false, M68kFrameClearLoopKind.PreserveData);
		Assert.NotNull(plan);
		Assert.True(plan!.PlannedCycles <= plan.OriginalCycles);
		Assert.True(plan.PlannedBytes < plan.OriginalBytes);
	}

	[Theory]
	[InlineData(512, 26)]
	[InlineData(516, 30)]
	public void CounterEncodingCostIncludesMoveqToLongImmediateTransition(int words, int expectedBytes)
	{
		var plan = M68kFrameClearRunPlanner.Create(Displacements([words, -1, 1]), true, M68kFrameClearLoopKind.Scratch);
		Assert.NotNull(plan);
		Assert.Equal(expectedBytes, plan!.PlannedBytes);
	}

	[Fact]
	public void PreservesTheExactInitializedPositionsAndOrder()
	{
		var displacements = Displacements([2, -1, 33, -2, 1, -1, 66, -1, 3]);
		var plan = M68kFrameClearRunPlanner.Create(displacements, true, M68kFrameClearLoopKind.Scratch);
		Assert.NotNull(plan);
		Assert.Equal(displacements, plan!.Runs.SelectMany(run =>
			Enumerable.Range(run.Start, run.Count).Select(index => displacements[index])));
		foreach (var run in plan.Runs.Where(run => run.Loop))
			Assert.Equal(Enumerable.Range(0, run.Count).Select(index => displacements[run.Start] + index * 4),
				displacements.Skip(run.Start).Take(run.Count));
	}

	[Fact]
	public void KeepsExistingSingleRunAndAllSmallSequences()
	{
		Assert.Null(M68kFrameClearRunPlanner.Create(Displacements([33]), true, M68kFrameClearLoopKind.Scratch));
		Assert.Null(M68kFrameClearRunPlanner.Create(Displacements([1, -1, 1, -1, 1]), true, M68kFrameClearLoopKind.Scratch));
		Assert.Null(M68kFrameClearRunPlanner.Create([], false, M68kFrameClearLoopKind.PreserveDataAndAddress));
	}

	private static int[] Displacements(int[] homes)
	{
		var result = new List<int>();
		var offset = 0;
		foreach (var home in homes)
		{
			if (home > 0) result.AddRange(Enumerable.Range(0, home).Select(index => offset + index * 4));
			offset += Math.Abs(home) * 4;
		}
		return result.ToArray();
	}
}
