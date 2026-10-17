/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDataflowDemandTests
{
	[Fact]
	public void PropagationRunsOnlyWhenRequestedAndOnlyOncePerSnapshot()
	{
		var assembler = CreateConstantTest();
		using var session = M68kOptimizationProfiler.Start();
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(flow.TryGetFacts(8, out _));
		Assert.DoesNotContain(session.GetSnapshot().Measurements,
			item => item.Name is "dataflow.value-ranges" or "dataflow.condition-provenance");

		for (var repeat = 0; repeat < 3; repeat++)
		{
			Assert.Equal(M68kValueRange.Exact(42), flow.GetDataValueBefore(8, 0));
			Assert.Equal(M68kAddressAlias.Stack(0), flow.GetAddressAliasBefore(8, 7));
		}
		var values = session.GetSnapshot();
		Assert.Equal(1, Assert.Single(values.Measurements, item => item.Name == "dataflow.value-ranges").Calls);
		Assert.DoesNotContain(values.Measurements, item => item.Name == "dataflow.condition-provenance");
		var test = flow.Instructions.Single(item => item.Offset == 8);
		for (var repeat = 0; repeat < 3; repeat++)
		{
			Assert.False(flow.TryGetKnownZeroTest(test, out var nonZero));
			Assert.False(nonZero);
			Assert.True(flow.IsConditionInstructionRedundant(test, M68kConditionCodeSet.Zero));
		}
		Assert.Equal(1, Assert.Single(session.GetSnapshot().Measurements,
			item => item.Name == "dataflow.condition-provenance").Calls);
	}

	[Theory]
	[InlineData("bytes")]
	[InlineData("relocation")]
	[InlineData("effects")]
	[InlineData("scope")]
	[InlineData("layout")]
	public void DeferredPropagationRetainsItsOriginalInputsAcrossRewrites(string mutation)
	{
		var assembler = CreateConstantTest();
		var original = M68kInstructionDataflow.Analyze(assembler);
		var test = original.Instructions.Single(item => item.Offset == 8);
		var buffer = Buffer(assembler);
		switch (mutation)
		{
			case "bytes":
				buffer.WriteWord(4, 0); // Change the immediate to zero, preserving length.
				break;
			case "relocation":
				buffer.Addresses.Add(new(2, "external", true)); // The immediate becomes symbolic.
				break;
			case "effects":
				assembler.SetInstructionEffects(6,
					M68kInstructionDataflow.GetEffects(original.Instructions[1]) with
					{ IsBarrier = true, DefinesData = 1, WritesConditions = M68kConditionCodeSet.All });
				break;
			case "scope":
				assembler.SetAnalysisScope("test", "end", ["test", "end"]);
				break;
			case "layout":
				buffer.RemoveBytes(0, 6);
				break;
		}

		// First propagation occurs after the assembler has changed.
		Assert.Equal(M68kValueRange.Exact(42), original.GetDataValueBefore(8, 0));
		Assert.True(original.IsConditionInstructionRedundant(test, M68kConditionCodeSet.Zero));
		Assert.False(original.TryGetKnownZeroTest(test, out var nonZero));
		Assert.False(nonZero);
		Assert.True(original.TryGetFacts(8, out var facts));
		Assert.Equal(1, facts.Effects.UsesData);
		var current = M68kInstructionDataflow.Analyze(assembler);
		Assert.NotEqual(M68kValueRange.Exact(42), current.GetDataValueBefore(8, 0));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DeferredPropagationRetainsBranchTargetsAndLabels(bool retargetFixup)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitBranch(M68kCondition.True, "test");
		assembler.Mark("other");
		assembler.EmitWord(0x7002); // MOVEQ #2,D0
		assembler.Mark("test");
		var testOffset = assembler.Offset;
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x4E75);
		var expected = M68kInstructionDataflow.Analyze(assembler);
		var range = expected.GetDataValueBefore(testOffset, 0);
		var test = expected.Instructions.Single(item => item.Offset == testOffset);
		var known = expected.TryGetKnownZeroTest(test, out var nonZero);
		var deferred = M68kInstructionDataflow.Analyze(assembler);
		var buffer = Buffer(assembler);
		if (retargetFixup)
			buffer.Branches[0] = buffer.Branches[0] with { Target = "other" };
		else
			buffer.Labels["test"] = buffer.Labels["other"];

		Assert.Equal(range, deferred.GetDataValueBefore(testOffset, 0));
		Assert.Equal(known, deferred.TryGetKnownZeroTest(test, out var actualNonZero));
		Assert.Equal(nonZero, actualNonZero);
		Assert.NotEqual(range, M68kInstructionDataflow.Analyze(assembler).GetDataValueBefore(testOffset, 0));
	}

	[Fact]
	public void SpillCheckReusesTheCurrentRoundInsteadOfRebuildingDataflow()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		assembler.EmitWord(0x5680); // ADDQ.L #3,D0
		assembler.EmitWord(0x0240); assembler.EmitWord(0xFFFC); // ANDI.W #$FFFC,D0
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75);
		using var session = M68kOptimizationProfiler.Start();
		assembler.OptimizeForM68000();
		var snapshot = session.GetSnapshot();
		Assert.True(snapshot.Counters["dataflow.spill-snapshot-reuses"] > 0);
		Assert.Equal(snapshot.Counters["peephole.analysis-rounds"] + snapshot.Counters["condition-code.analysis-rounds"],
			Assert.Single(snapshot.Measurements, item => item.Name == "dataflow.total").Calls);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("move.l\td0,-(a7)", assembly);
		Assert.DoesNotContain("move.l\t(a7),d0", assembly);
		Assert.Contains("addq.l\t#3,d0", assembly);
	}

	[Fact]
	public void EmptySnapshotKeepsUnknownResultsWithoutRunningPropagation()
	{
		using var session = M68kOptimizationProfiler.Start();
		var flow = M68kInstructionDataflow.Analyze(new M68kAssembler());
		Assert.Empty(flow.Instructions);
		Assert.False(flow.TryGetFacts(0, out _));
		Assert.False(flow.GetDataValueBefore(0, 0).IsKnown);
		Assert.Equal(M68kAddressAlias.Unknown, flow.GetAddressAliasBefore(0, 7));
		Assert.False(flow.TryGetKnownZeroTest(default, out _));
		Assert.DoesNotContain(session.GetSnapshot().Measurements,
			item => item.Name is "dataflow.value-ranges" or "dataflow.condition-provenance");
	}

	private static M68kAssembler CreateConstantTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); assembler.EmitLong(42); // MOVE.L #42,D0
		assembler.EmitWord(0x2C4F); // MOVEA.L A7,A6 permits an effect-override mutation.
		assembler.Mark("test");
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x4E75);
		assembler.Mark("end");
		return assembler;
	}

	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;
}
