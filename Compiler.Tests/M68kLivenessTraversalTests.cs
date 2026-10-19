using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kLivenessTraversalTests
{
	[Theory]
	[InlineData(0)] // Terminal: no successors.
	[InlineData(1)] // Fallthrough.
	[InlineData(2)] // Join, with different live resources on each successor.
	[InlineData(3)] // Back edge requiring repeated propagation.
	[InlineData(4)] // Conditional target is also the fallthrough successor.
	public void RepeatedLivenessTraversalDoesNotAllocateAfterWarmup(int shape)
	{
		var assembler = CreateGraph(shape);
		using var session = M68kOptimizationProfiler.Start();
		M68kInstructionDataflow.Analyze(assembler);
		M68kInstructionDataflow.Analyze(assembler);
		var before = session.GetSnapshot().Measurements.Single(item => item.Name == "dataflow.liveness");
		for (var repeat = 0; repeat < 10; repeat++)
		{
			var flow = M68kInstructionDataflow.Analyze(assembler);
			Assert.All(flow.Instructions, instruction => Assert.True(instruction.IsDecoded));
		}
		var after = session.GetSnapshot().Measurements.Single(item => item.Name == "dataflow.liveness");
		Assert.Equal(10, after.Calls - before.Calls);
		Assert.Equal(before.AllocatedBytes, after.AllocatedBytes);
	}

	[Fact]
	public void JoinCombinesDataAddressConditionAndMemoryLivenessFromBothSuccessors()
	{
		var flow = M68kInstructionDataflow.Analyze(CreateGraph(2));
		Assert.True(flow.TryGetFacts(2, out var branch));
		Assert.Equal((1 << 1) | (1 << 2), branch.LiveDataAfter & ((1 << 1) | (1 << 2)));
		Assert.NotEqual(0, branch.LiveAddressAfter & 1);
		Assert.NotEqual(M68kConditionCodeSet.None, branch.LiveConditionsAfter & M68kConditionCodeSet.Carry);
		Assert.NotEqual(M68kMemorySet.None, branch.LiveMemoryAfter & M68kMemorySet.Indirect);
	}

	[Fact]
	public void BackEdgeReceivesFactsFromLaterFixedPointRounds()
	{
		using var session = M68kOptimizationProfiler.Start();
		var flow = M68kInstructionDataflow.Analyze(CreateGraph(3));
		var backEdge = flow.Instructions.Single(instruction => instruction.Kind == M68kInstructionKind.UnconditionalBranch);
		Assert.True(flow.TryGetFacts(backEdge.Offset, out var facts));
		Assert.NotEqual(0, facts.LiveAddressAfter & 1);
		Assert.NotEqual(M68kMemorySet.None, facts.LiveMemoryAfter & M68kMemorySet.Indirect);
		Assert.True(session.GetSnapshot().Counters["dataflow.liveness-rounds"] > 1);
	}

	private static M68kAssembler CreateGraph(int shape)
	{
		var assembler = new M68kAssembler();
		switch (shape)
		{
			case 1:
				assembler.EmitWord(0x7001); // MOVEQ #1,D0
				assembler.EmitWord(0x5280); // ADDQ.L #1,D0
				break;
			case 2:
				assembler.EmitWord(0x4A82); // TST.L D2
				assembler.EmitBranch(M68kCondition.Equal, "right");
				assembler.EmitWord(0x2001); // MOVE.L D1,D0
				assembler.EmitBranch(M68kCondition.True, "join");
				assembler.Mark("right");
				assembler.EmitBranch(M68kCondition.CarrySet, "join");
				assembler.EmitWord(0x2010); // MOVE.L (A0),D0
				assembler.Mark("join");
				assembler.EmitWord(0xD082); // ADD.L D2,D0
				break;
			case 3:
				assembler.Mark("loop");
				assembler.EmitWord(0x2001); // MOVE.L D1,D0
				assembler.EmitBranch(M68kCondition.Equal, "exit");
				assembler.EmitWord(0x2210); // MOVE.L (A0),D1
				assembler.EmitBranch(M68kCondition.True, "loop");
				assembler.Mark("exit");
				break;
			case 4:
				assembler.EmitBranch(M68kCondition.Equal, "next");
				assembler.Mark("next");
				assembler.EmitWord(0x2001); // MOVE.L D1,D0
				break;
		}
		assembler.EmitWord(0x4E75); // RTS
		return assembler;
	}
}
