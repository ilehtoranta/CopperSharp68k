/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kRepeatedCallResultTestOptimizerTests
{
	[Fact]
	public void HoistsRepeatedCallResultTestsIntoOneCalleeReturn()
	{
		var assembler = CreateEnabledAssembler();
		EmitCaller(assembler, "first", "callee");
		EmitCaller(assembler, "second", "callee");
		EmitCaller(assembler, "third", "callee");
		assembler.Mark("callee");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("callee:end");

		assembler.OptimizeForM68000();

		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.ReturnConditionTargets);
		Assert.Equal(3, statistics.ReturnConditionTestsRemoved);
		Assert.Equal(1, statistics.ReturnConditionTestsInserted);
		Assert.Equal(4, statistics.ReturnConditionNetBytesSaved);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("callee\n\ttst.b\td0", assembly,
			StringComparison.Ordinal);
		Assert.Equal(1,
			assembly.Split("tst.b\td0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void LeavesOneCallAndOneReturnAtTheCallSite()
	{
		var assembler = CreateEnabledAssembler();
		EmitCaller(assembler, "only", "callee");
		assembler.Mark("callee");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("callee:end");

		assembler.OptimizeForM68000();

		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(0, statistics.ReturnConditionTargets);
		Assert.Equal(0, statistics.ReturnConditionNetBytesSaved);
		Assert.Contains("tst.b\td0", assembler.RenderAssembly(M68kCpuTarget.M68000),
			StringComparison.Ordinal);
	}

	[Fact]
	public void IgnoresUnreferencedBookkeepingLabelsOnCallerTests()
	{
		var assembler = CreateEnabledAssembler();
		for (var index = 0; index < 3; index++)
		{
			var name = "caller" + index;
			assembler.Mark(name);
			assembler.EmitBsr("callee");
			assembler.Mark(name + "_bookkeeping");
			assembler.EmitWord(0x4A00); // TST.B D0
			assembler.EmitBranch(M68kCondition.NotEqual, name + "_true");
			assembler.EmitWord(0x7000);
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + "_true");
			assembler.EmitWord(0x7001);
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + ":end");
		}
		assembler.Mark("callee");
		assembler.EmitWord(0x7001);
		assembler.EmitWord(0x4E75);
		assembler.Mark("callee:end");

		assembler.OptimizeForM68000();

		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.ReturnConditionTargets);
		Assert.Equal(3, statistics.ReturnConditionTestsRemoved);
		Assert.Equal(1, statistics.ReturnConditionTestsInserted);
	}

	[Fact]
	public void CoversEveryReachableCalleeReturn()
	{
		var assembler = CreateEnabledAssembler();
		EmitCaller(assembler, "first", "callee");
		EmitCaller(assembler, "second", "callee");
		EmitCaller(assembler, "third", "callee");
		assembler.Mark("callee");
		assembler.EmitWord(0x4A81); // TST.L D1
		assembler.EmitBranch(M68kCondition.Equal, "callee_false");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("callee_false");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("callee:end");

		assembler.OptimizeForM68000();

		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.ReturnConditionTargets);
		Assert.Equal(3, statistics.ReturnConditionTestsRemoved);
		Assert.InRange(statistics.ReturnConditionTestsInserted, 1, 2);
		Assert.True(statistics.ReturnConditionNetBytesSaved > 0);
	}

	[Fact]
	public void RetainsTestsThatAreIndependentBranchEntries()
	{
		var assembler = CreateEnabledAssembler();
		for (var index = 0; index < 3; index++)
		{
			var name = "caller" + index;
			assembler.Mark(name);
			assembler.EmitWord(0x4A81); // TST.L D1
			assembler.EmitBranch(M68kCondition.Equal, name + "_test");
			assembler.EmitBsr("callee");
			assembler.Mark(name + "_test");
			assembler.EmitWord(0x4A00); // TST.B D0
			assembler.EmitBranch(M68kCondition.NotEqual, name + "_true");
			assembler.EmitWord(0x7000);
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + "_true");
			assembler.EmitWord(0x7001);
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + ":end");
		}
		assembler.Mark("callee");
		assembler.EmitWord(0x7001);
		assembler.EmitWord(0x4E75);
		assembler.Mark("callee:end");

		assembler.OptimizeForM68000();

		Assert.Equal(0,
			assembler.PeepholeOptimizationStatistics.ReturnConditionTargets);
	}

	[Fact]
	public void DefaultPipelineDoesNotRunRomOnlyResultTestOptimization()
	{
		var assembler = new M68kAssembler();
		EmitCaller(assembler, "first", "callee");
		EmitCaller(assembler, "second", "callee");
		EmitCaller(assembler, "third", "callee");
		assembler.Mark("callee");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("callee:end");

		assembler.OptimizeForM68000();

		Assert.Equal(0,
			assembler.PeepholeOptimizationStatistics.ReturnConditionTargets);
		Assert.Equal(3, assembler.RenderAssembly(M68kCpuTarget.M68000)
			.Split("tst.b\td0", StringSplitOptions.None).Length - 1);
	}

	private static M68kAssembler CreateEnabledAssembler() =>
		new()
		{
			EnableRepeatedCallResultTestOptimization = true
		};

	private static void EmitCaller(
		M68kAssembler assembler,
		string name,
		string callee)
	{
		assembler.Mark(name);
		assembler.EmitBsr(callee);
		assembler.EmitWord(0x4A00); // TST.B D0
		assembler.EmitBranch(M68kCondition.NotEqual, name + "_true");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark(name + "_true");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark(name + ":end");
	}
}
