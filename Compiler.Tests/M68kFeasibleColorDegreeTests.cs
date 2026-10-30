using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kFeasibleColorDegreeTests
{
	[Theory]
	[InlineData(false, "byte")]
	[InlineData(true, "byte")]
	[InlineData(false, "word")]
	[InlineData(true, "word")]
	[InlineData(false, "long")]
	[InlineData(true, "long")]
	[InlineData(false, "address")]
	[InlineData(true, "address")]
	public void ForbiddenColorsDoNotCreateFalsePressureUnderOptInPolicy(bool enabled, string shape)
	{
		var function = new M68kMachineFunction("feasible-color-degree", 0)
		{
			SimplifyLoopPhis = enabled
		};
		var colors = shape == "address"
			? new[] { M68kRegister.A2, M68kRegister.A3, M68kRegister.A4 }
			: new[] { M68kRegister.D2, M68kRegister.D3, M68kRegister.D4 };
		var width = shape == "byte" ? M68kMachineValueWidth.Byte :
			shape == "word" ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Long;
		var values = Enumerable.Range(0, 4).Select(_ => function.CreateValue(
			CilStackValueKind.Int32, width, M68kRegisterSet.From(colors), spillWeight: 1)).ToArray();
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		foreach (var value in values)
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
				block.Instructions.Count, definitions: [value.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			block.Instructions.Count));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		graph.AddForbidden(values[0].Id, M68kRegisterSet.From(colors[1], colors[2]));
		graph.AddForbidden(values[1].Id, M68kRegisterSet.From(colors[2]));
		graph.AddForbidden(values[3].Id, M68kRegisterSet.From(colors[0], colors[2]));
		foreach (var (left, right) in new[] { (0,1), (0,2), (0,3), (1,2), (2,3) })
			graph.AddEdge(values[left].Id, values[right].Id);
		var result = M68kGraphColoringAllocator.Allocate(function, graph);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, result);
		if (enabled)
		{
			Assert.Empty(result.SpilledValues);
			Assert.Equal(colors[0], result.Registers[values[0].Id].Register);
			Assert.Equal(colors[1], result.Registers[values[1].Id].Register);
			Assert.Equal(colors[2], result.Registers[values[2].Id].Register);
			Assert.Equal(colors[1], result.Registers[values[3].Id].Register);
		}
		else
			Assert.Equal(values[0].Id, Assert.Single(result.SpilledValues));
		for (var repeat = 0; repeat < 3; repeat++)
		{
			var again = M68kGraphColoringAllocator.Allocate(function, graph);
			Assert.Equal(result.SpilledValues.Order(), again.SpilledValues.Order());
			Assert.Equal(result.Registers.OrderBy(p => p.Key), again.Registers.OrderBy(p => p.Key));
		}
	}
}
