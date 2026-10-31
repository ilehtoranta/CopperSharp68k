using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kCompilationWorkReuseTests
{
	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(128)]
	public void ReturnChainsShareScratchButKeepIndependentVisitsAndMethodBounds(int methods)
	{
		var assembler = new M68kAssembler();
		if (methods != 0)
		{
			assembler.Mark("method:prefix:end");
			assembler.Mark("method:prefix-alias:end");
		}
		assembler.EmitBranch(M68kCondition.True, "tail");
		assembler.EmitWord(0x4E71);
		assembler.EmitBranch(M68kCondition.True, "tail");
		assembler.EmitWord(0x4E71);
		assembler.Mark("tail");
		assembler.EmitWord(0x4E75);
		if (methods != 0) assembler.Mark("method:current:end");
		for (var index = 0; index < methods; index++)
		{
			assembler.EmitWord(0x4E75);
			assembler.Mark($"method:later-{index}:end");
		}
		var buffer = Buffer(assembler);
		var pass = new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		pass.RunLayoutCleanup();
		Assert.Empty(buffer.Branches);
		Assert.Equal(1, pass.RewriteCount); // Both branches must use the same original snapshot.
		Assert.Equal(new ushort[] { 0x4E75, 0x4E71, 0x4E75, 0x4E71, 0x4E75 },
			assembler.GetExecutableInstructionStream().Take(5).Select(instruction => instruction.Opcode));
	}

	[Fact]
	public void ReturnAtTheNextMethodBoundaryIsNotInlined()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.True, "next");
		assembler.EmitWord(0x4E71);
		assembler.Mark("method:first:end");
		assembler.Mark("next");
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:next:end");
		var buffer = Buffer(assembler);
		var before = buffer.Bytes.ToArray();
		var pass = new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		pass.RunLayoutCleanup();
		Assert.False(pass.Changed);
		Assert.Equal(before, buffer.Bytes);
	}

	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void LayoutRebuildsLabelsAndInstructionsAfterSameCountEdits(bool scoped)
	{
		var assembler = new M68kAssembler();
		var buffer = Buffer(assembler);
		assembler.Mark("method:reuse");
		assembler.EmitBranch(M68kCondition.True, "method:reuse:target");
		assembler.EmitWord(0x4E71);
		assembler.Mark("method:reuse:target");
		assembler.EmitWord(0x4E71);
		assembler.EmitWord(0x4E75);
		assembler.Mark("method:reuse:end");
		buffer.Labels.Add("method:reuse:interior", 2);
		if (scoped) assembler.SetAnalysisScope("method:reuse", "method:reuse:end", buffer.Labels.Keys.ToArray());
		var optimizer = new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		optimizer.RunLayoutCleanup();
		Assert.False(optimizer.Changed);
		Assert.Equal(10, assembler.Offset);
		buffer.Labels["method:reuse:interior"] = 10;
		buffer.WriteWord(6, 0x4E75);
		optimizer.RunLayoutCleanup();
		Assert.True(optimizer.Changed);
		Assert.Empty(buffer.Branches);
		Assert.Equal(new ushort[] { 0x4E75, 0x4E71, 0x4E75, 0x4E75 },
			assembler.GetExecutableInstructionStream().Select(instruction => instruction.Opcode));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void LivenessRetainsPhiEdgeFactsAcrossBackEdgesAndIndependentAnalyses(bool reverseBlocks)
	{
		var function = new M68kMachineFunction("reused-liveness", 0);
		var blocks = Enumerable.Range(0, 4).Select(id => new M68kMachineBlock(id, id * 10)).ToArray();
		function.Blocks.AddRange(blocks);
		function.AddEdge(blocks[0], blocks[1]);
		function.AddEdge(blocks[1], blocks[2]);
		function.AddEdge(blocks[2], blocks[1]);
		function.AddEdge(blocks[1], blocks[3]);
		int Value() => function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data).Id;
		var initial = Value(); var carried = Value(); var phi = Value(); var next = Value();
		blocks[0].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 0, definitions: [initial, carried]));
		blocks[0].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Branch, 1));
		blocks[1].Phis.Add(new(phi, new Dictionary<int, int> { [0] = initial, [2] = next }));
		blocks[1].Instructions.Add(function.CreateInstruction(M68kMachineOperation.ConditionalBranch, 10, uses: [phi]));
		blocks[2].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Add, 20, uses: [phi, carried], definitions: [next]));
		blocks[2].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Branch, 21));
		blocks[3].Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 30, uses: [phi]));
		if (reverseBlocks) function.Blocks.Reverse();
		using var session = M68kOptimizationProfiler.Start();
		var flow = M68kLivenessAnalysis.Analyze(function);
		Assert.True(session.GetSnapshot().Counters["allocation.liveness-rounds"] > 1);
		Assert.Empty(flow.LiveIn[0]);
		Assert.True(flow.LiveOut[0].SetEquals([initial, carried]));
		Assert.True(flow.LiveIn[1].SetEquals([carried]));
		Assert.True(flow.LiveOut[1].SetEquals([phi, carried]));
		Assert.True(flow.LiveIn[2].SetEquals([phi, carried]));
		Assert.True(flow.LiveOut[2].SetEquals([carried, next]));
		Assert.True(flow.LiveIn[3].SetEquals([phi]));
		Assert.Empty(flow.LiveOut[3]);
		var second = M68kLivenessAnalysis.Analyze(function);
		second.LiveIn[1].Clear();
		flow.LiveIn[2].Clear();
		Assert.True(flow.LiveIn[1].SetEquals([carried]));
		Assert.True(flow.LiveOut[1].SetEquals([phi, carried]));
		Assert.True(second.LiveIn[2].SetEquals([phi, carried]));
	}

	[Theory]
	[InlineData(false, false, M68kRegister.D3)]
	[InlineData(true, false, M68kRegister.D2)]
	[InlineData(true, true, M68kRegister.D3)]
	public void CopyPreferencesPreserveLegacyOrderWeightsAndStableTies(bool weighted, bool tied, M68kRegister expected)
	{
		var function = new M68kMachineFunction("indexed-copy-preferences", 0) { OptimizeRegisterCopies = weighted };
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var first = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.Data, precoloredRegister: M68kRegister.D3);
		var second = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.Data, precoloredRegister: M68kRegister.D2);
		var targets = Enumerable.Range(0, 64).Select(_ => function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Byte, M68kRegisterSet.Data)).ToArray();
		foreach (var value in targets.Prepend(second).Prepend(first))
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 0, definitions: [value.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 1, uses: [targets[^1].Id]));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		foreach (var target in targets)
		{
			// Different widths prevent coalescing, leaving real preferences to resolve.
			graph.AddCopyPreference(first.Id, target.Id);
			for (var count = 0; count < (tied ? 1 : 4); count++) graph.AddCopyPreference(second.Id, target.Id);
		}
		using var session = M68kOptimizationProfiler.Start();
		var allocation = M68kGraphColoringAllocator.Allocate(function, graph);
		Assert.Empty(allocation.SpilledValues);
		Assert.All(targets, target => Assert.Equal(expected, allocation.Registers[target.Id].Register));
		Assert.Equal(1, session.GetSnapshot().Counters["allocation.copy-preference-index-builds"]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}
}
