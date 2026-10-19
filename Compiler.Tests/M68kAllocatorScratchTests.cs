using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kAllocatorScratchTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CoalescingRecomputesNeighborsAfterMergesAndFixedColorRejections(bool reverseColors)
	{
		var function = new M68kMachineFunction("coalescing-scratch-chain", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var firstColor = reverseColors ? M68kRegister.D0 : M68kRegister.D1;
		var secondColor = reverseColors ? M68kRegister.D1 : M68kRegister.D0;
		var values = Enumerable.Range(0, 5).Select(index => function.CreateValue(
			CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D0, M68kRegister.D1, M68kRegister.D2),
			precoloredRegister: index == 2 ? firstColor : index == 4 ? secondColor : null)).ToArray();
		foreach (var value in values)
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Constant, 0, definitions: [value.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 1));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		graph.AddEdge(values[0].Id, values[4].Id);
		graph.AddEdge(values[0].Id, values[3].Id);
		for (var index = 0; index < 4; index++) graph.AddCopyPreference(values[index].Id, values[index + 1].Id);
		for (var repeat = 0; repeat < 3; repeat++)
		{
			var allocation = M68kGraphColoringAllocator.Allocate(function, graph);
			Assert.Empty(allocation.SpilledValues);
			Assert.All(values.Take(3), value => Assert.Equal(firstColor, allocation.Registers[value.Id].Register));
			Assert.All(values.Skip(3), value => Assert.Equal(secondColor, allocation.Registers[value.Id].Register));
			Assert.Equal([values[4].Id, values[3].Id], graph.Neighbors(values[0].Id));
			Assert.Equal(4, graph.CopyPreferences.Count);
		}
	}

	[Fact]
	public void DenseCoalescingChecksReuseBoundedScratchStorage()
	{
		var function = new M68kMachineFunction("dense-coalescing-scratch", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		const int count = 128;
		var values = Enumerable.Range(0, count).Select(_ => function.CreateValue(
			CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data)).ToArray();
		foreach (var value in values)
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Constant, 0, definitions: [value.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 1));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		for (var index = 0; index < count; index++)
		{
			for (var distance = 1; distance <= 8; distance++)
				graph.AddEdge(values[index].Id, values[(index + distance) % count].Id);
			graph.AddCopyPreference(values[index].Id, values[(index + 32) % count].Id);
		}
		using var session = M68kOptimizationProfiler.Start();
		var expected = M68kGraphColoringAllocator.Allocate(function, graph);
		var before = session.GetSnapshot().Measurements.Single(item => item.Name == "allocation.coalescing-candidate");
		var actual = M68kGraphColoringAllocator.Allocate(function, graph);
		var after = session.GetSnapshot().Measurements.Single(item => item.Name == "allocation.coalescing-candidate");
		Assert.Equal(count, after.Calls - before.Calls);
		Assert.InRange(after.AllocatedBytes - before.AllocatedBytes, 0, 512 * 1024);
		Assert.Equal(expected.Registers.OrderBy(item => item.Key), actual.Registers.OrderBy(item => item.Key));
		Assert.True(expected.SpilledValues.SetEquals(actual.SpilledValues));
		Assert.All(values, value => Assert.Equal(16, graph.Neighbors(value.Id).Count));
	}
}
