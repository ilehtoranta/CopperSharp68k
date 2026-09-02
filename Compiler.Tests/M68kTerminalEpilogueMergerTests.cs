/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kTerminalEpilogueMergerTests
{
	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void InvertsConditionalFallthroughIntoOneSharedTerminalTail(
		M68kCpuTarget cpu)
	{
		var assembler = CreateEnabledAssembler();
		EmitConditionalFailureMethod(assembler, "first");
		EmitConditionalFailureMethod(assembler, "second");

		assembler.OptimizeForCpu(cpu, peepholeOptimization:
			M68kPeepholeOptimizationMode.FixedPoint);

		var assembly = assembler.RenderAssembly(cpu);
		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.TerminalGroups);
		Assert.Equal(2, statistics.TerminalMergedCopies);
		Assert.Equal(2, statistics.TerminalInvertedBranches);
		Assert.Equal(0, statistics.TerminalTrampolines);
		Assert.True(statistics.TerminalNetBytesSaved > 0);
		Assert.Contains("beq.", assembly, StringComparison.Ordinal);
		Assert.Contains("__m68k_shared_epilogue_", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain(assembler.Labels.Keys,
			label => label.StartsWith("__c68k_", StringComparison.Ordinal));
		Assert.Equal(1, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void PreciseReturnEffectsDoNotDisableTerminalSharing()
	{
		var assembler = CreateEnabledAssembler();
		EmitConditionalFailureMethod(assembler, "first", preciseReturnEffects: true);
		EmitConditionalFailureMethod(assembler, "second", preciseReturnEffects: true);

		assembler.OptimizeForM68000();

		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.TerminalGroups);
		Assert.Equal(2, statistics.TerminalMergedCopies);
		Assert.True(statistics.TerminalNetBytesSaved > 0);
	}

	[Fact]
	public void RetainsBranchReferencedEntriesAsTrampolines()
	{
		var assembler = CreateEnabledAssembler();
		EmitBranchReferencedFailureMethod(assembler, "first");
		EmitBranchReferencedFailureMethod(assembler, "second");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.TerminalGroups);
		Assert.Equal(2, statistics.TerminalTrampolines);
		Assert.Equal(0, statistics.TerminalInvertedBranches);
		Assert.Contains("C68K_first_failure:", assembly, StringComparison.Ordinal);
		Assert.Contains("C68K_second_failure:", assembly, StringComparison.Ordinal);
		Assert.Equal(1, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void LeavesFarSharedTailBranchesInWordForm()
	{
		var assembler = CreateEnabledAssembler();
		EmitConditionalFailureMethod(assembler, "first");
		EmitConditionalFailureMethod(assembler, "second");
		for (var index = 0; index < 128; index++)
		{
			assembler.EmitWord(0x4E71); // Keep the generated tail out of byte range.
		}

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("beq.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReusesExistingRegionalTailWhenGeneratedTailIsOutOfRange()
	{
		var assembler = CreateEnabledAssembler();
		assembler.EnableRegionalTerminalReuse = true;
		EmitBranchReferencedFailureMethod(assembler, "first");
		EmitBranchReferencedFailureMethod(assembler, "second");
		for (var index = 0; index < 17_000; index++)
		{
			assembler.EmitWord(0x4E71); // Put the generated end tail beyond word reach.
		}

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.TerminalGroups);
		Assert.Equal(1, statistics.TerminalMergedCopies);
		Assert.Equal(1, statistics.TerminalTrampolines);
		Assert.True(statistics.TerminalNetBytesSaved > 0);
		Assert.Contains("__m68k_regional_epilogue_", assembly,
			StringComparison.Ordinal);
		Assert.Contains("jmp\t__m68k_regional_epilogue_", assembly,
			StringComparison.Ordinal);
		Assert.Equal(1,
			assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void KeepsRegionalJumpAbsoluteAcrossAlignmentRegion()
	{
		var assembler = CreateEnabledAssembler();
		assembler.EnableRegionalTerminalReuse = true;
		EmitBranchReferencedFailureMethod(assembler, "first");
		var firstTail = assembler.Labels["first_failure"];

		assembler.EmitWord(0x4E71);
		for (var index = 0; index < 3; index++)
		{
			var label = "regional_alignment_" + index;
			assembler.Mark(label);
			assembler.RequestLongAlignment(label);
			assembler.EmitWord(0x4E71);
			assembler.EmitWord(0x4E71);
		}
		const int tailDistance = 32_764;
		while (assembler.Offset + 6 < firstTail + tailDistance)
		{
			assembler.EmitWord(0x4E71);
		}
		Assert.Equal(firstTail + tailDistance, assembler.Offset + 6);
		EmitBranchReferencedFailureMethod(assembler, "second");
		for (var index = 0; index < 17_000; index++)
		{
			assembler.EmitWord(0x4E71);
		}

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(1, statistics.TerminalGroups);
		Assert.Equal(1, statistics.TerminalMergedCopies);
		Assert.Equal(1, statistics.TerminalTrampolines);
		Assert.Equal(0, statistics.TerminalInvertedBranches);
		Assert.True(statistics.TerminalNetBytesSaved > 0);
		Assert.Contains("jmp\t__m68k_regional_epilogue_", assembly,
			StringComparison.Ordinal);
		Assert.Equal(1,
			assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RejectsTerminalBlocksWhoseReturnSetupDiffers()
	{
		var assembler = CreateEnabledAssembler();
		EmitBranchReferencedFailureMethod(assembler, "first");
		assembler.EmitWord(0x4E71); // NOP separates the methods.
		assembler.EmitBranch(M68kCondition.True, "second_failure");
		assembler.EmitWord(0x4E71); // Keep the source branch non-adjacent.
		assembler.Mark("second_failure");
		assembler.EmitWord(0x7200); // MOVEQ #0,D1, unlike the first D0 return.
		assembler.EmitWord(0x4FEF); // LEA 308(A7),A7
		assembler.EmitWord(308);
		assembler.EmitWord(0x4CDF); // MOVEM.L (A7)+,D2-D7/A2-A6
		assembler.EmitWord(0x7CFC);
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("second:end");

		assembler.OptimizeForM68000();

		Assert.Equal(0, assembler.PeepholeOptimizationStatistics.TerminalGroups);
	}

	[Fact]
	public void DisabledModeDoesNotCreateGeneratedTails()
	{
		var assembler = CreateEnabledAssembler();
		EmitConditionalFailureMethod(assembler, "first");
		EmitConditionalFailureMethod(assembler, "second");

		assembler.OptimizeForCpu(M68kCpuTarget.M68000, peepholeOptimization:
			M68kPeepholeOptimizationMode.Disabled);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(0, assembler.PeepholeOptimizationStatistics.TerminalGroups);
		Assert.DoesNotContain("__m68k_shared_epilogue_", assembly, StringComparison.Ordinal);
		Assert.Equal(2, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void DefaultPipelineDoesNotRunRomOnlyTerminalSharing()
	{
		var assembler = new M68kAssembler();
		EmitConditionalFailureMethod(assembler, "first");
		EmitConditionalFailureMethod(assembler, "second");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(0, assembler.PeepholeOptimizationStatistics.TerminalGroups);
		Assert.DoesNotContain("__m68k_shared_epilogue_", assembly, StringComparison.Ordinal);
		Assert.Equal(2, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	private static M68kAssembler CreateEnabledAssembler() =>
		new()
		{
			EnableMethodLocalTerminalReuse = true
		};

	private static void EmitConditionalFailureMethod(
		M68kAssembler assembler,
		string name,
		bool preciseReturnEffects = false)
	{
		assembler.Mark(name);
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.NotEqual, name + "_continue");
		assembler.Mark(name + "_failure");
		EmitFailureEpilogue(assembler, preciseReturnEffects);
		assembler.Mark(name + "_continue");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark(name + ":end");
	}

	private static void EmitBranchReferencedFailureMethod(M68kAssembler assembler, string name)
	{
		assembler.Mark(name);
		assembler.EmitBranch(M68kCondition.True, name + "_failure");
		assembler.EmitWord(0x4E71); // Do not allow branch-to-next removal.
		assembler.Mark(name + "_failure");
		EmitFailureEpilogue(assembler);
		assembler.Mark(name + ":end");
	}

	private static void EmitFailureEpilogue(
		M68kAssembler assembler,
		bool preciseReturnEffects = false)
	{
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4FEF); // LEA 308(A7),A7
		assembler.EmitWord(308);
		assembler.EmitWord(0x4CDF); // MOVEM.L (A7)+,D2-D7/A2-A6
		assembler.EmitWord(0x7CFC);
		var returnOffset = assembler.Offset;
		assembler.EmitWord(0x4E75); // RTS
		if (preciseReturnEffects)
		{
			assembler.SetInstructionEffects(
				returnOffset,
				new M68kInstructionEffects(
					0x00FC,
					0,
					0x00FC,
					0x0080,
					M68kConditionCodeSet.None,
					M68kConditionCodeSet.None,
					M68kMemorySet.Stack,
					M68kMemorySet.None,
					4,
					true,
					false));
		}
	}
}
