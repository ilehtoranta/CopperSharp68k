using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ApplicationCallArgumentAllocationTests
{
	[Fact]
	public void FinalUseOfNonCoalescingScalarArgumentCanReuseItsFixedRegister()
	{
		var function = CreateStagedCall(sourceRemainsLive: false);
		function.OptimizeCallArguments = false;
		var baseline = Allocate(function);
		function.OptimizeCallArguments = true;
		var optimized = Allocate(function);
		Assert.Equal(M68kRegister.D0, baseline.Registers[0].Register);
		Assert.Equal(M68kRegister.D1, baseline.Registers[1].Register);
		Assert.Equal(M68kRegister.D1, optimized.Registers[0].Register);
		Assert.Equal(optimized.Registers[0], optimized.Registers[1]);
	}

	[Fact]
	public void NonCoalescingArgumentKeepsALiveSourceSeparate()
	{
		var function = CreateStagedCall(sourceRemainsLive: true);
		function.OptimizeCallArguments = true;
		var allocation = Allocate(function);
		Assert.NotEqual(allocation.Registers[0], allocation.Registers[1]);
	}

	[Fact]
	public void CallArgumentCanTakeARegisterFreedByOneFlexibleNeighbor()
	{
		var function = new M68kMachineFunction("reassign-call-argument", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var source = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var blocker = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress);
		var argument = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.From(M68kRegister.D1),
			precoloredRegister: M68kRegister.D1);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
			0, definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
			1, definitions: [blocker.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
			2, uses: [blocker.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy,
			3, uses: [source.Id], definitions: [argument.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call,
			4, uses: [argument.Id]));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		graph.AddEdge(source.Id, blocker.Id);
		graph.AddForbidden(blocker.Id, M68kRegisterSet.From(M68kRegister.D0));
		graph.AddCopyPreference(source.Id, argument.Id);

		function.OptimizeCallArguments = false;
		var baseline = Allocate(function, graph);
		function.OptimizeCallArguments = true;
		var optimized = Allocate(function, graph);
		Assert.Equal(M68kRegister.D0, baseline.Registers[source.Id].Register);
		Assert.Equal(M68kRegister.D1, baseline.Registers[blocker.Id].Register);
		Assert.Equal(M68kRegister.D1, optimized.Registers[source.Id].Register);
		Assert.Equal(M68kRegister.A0, optimized.Registers[blocker.Id].Register);
		Assert.Equal(optimized.Registers[source.Id], optimized.Registers[argument.Id]);
	}

	private static M68kMachineFunction CreateStagedCall(bool sourceRemainsLive)
	{
		var function = new M68kMachineFunction("staged-argument", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var source = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var argument = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.From(M68kRegister.D1),
			precoloredRegister: M68kRegister.D1);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
			0, definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy,
			1, uses: [source.Id], definitions: [argument.Id],
			allowCopyCoalescing: false));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call,
			2, uses: [argument.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			3, uses: sourceRemainsLive ? [source.Id] : []));
		return function;
	}

	private static M68kAllocationResult Allocate(M68kMachineFunction function)
	{
		var liveness = M68kLivenessAnalysis.Analyze(function);
		var graph = M68kInterferenceBuilder.Build(function, liveness);
		return Allocate(function, graph, liveness);
	}

	private static M68kAllocationResult Allocate(
		M68kMachineFunction function,
		M68kInterferenceGraph graph,
		M68kLivenessInfo? liveness = null)
	{
		liveness ??= M68kLivenessAnalysis.Analyze(function);
		var allocation = M68kGraphColoringAllocator.Allocate(function, graph);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation,
			M68kLivenessAnalysis.AnalyzeInstructions(function, liveness));
		return allocation;
	}
}
