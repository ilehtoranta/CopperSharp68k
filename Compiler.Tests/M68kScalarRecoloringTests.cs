using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kScalarRecoloringTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RecolorsFlexibleScalarOnlyUnderOptInPolicy(bool enabled)
	{
		var (function, graph, flag, scalar) = CreatePressure(enabled);
		var result = M68kGraphColoringAllocator.Allocate(function, graph);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, result);
		if (enabled)
		{
			Assert.Empty(result.SpilledValues);
			Assert.Equal(M68kRegister.D2, result.Registers[flag].Register);
			Assert.Equal(M68kRegister.A3, result.Registers[scalar].Register);
		}
		else
		{
			Assert.Equal(flag, Assert.Single(result.SpilledValues));
			Assert.Equal(M68kRegister.D2, result.Registers[scalar].Register);
		}
	}

	[Theory]
	[InlineData("fixed")]
	[InlineData("reload")]
	[InlineData("gc")]
	[InlineData("byref")]
	[InlineData("narrow")]
	[InlineData("pair")]
	[InlineData("wide")]
	[InlineData("forbidden")]
	[InlineData("reserved")]
	[InlineData("interference")]
	[InlineData("multiple")]
	public void IneligibleOrUnavailableNeighborRemainsSpilled(string exclusion)
	{
		var (function, graph, flag, scalar) = CreatePressure(true, exclusion);
		var result = M68kGraphColoringAllocator.Allocate(function, graph);
		Assert.Contains(flag, result.SpilledValues);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, result);
	}

	[Fact]
	public void FailedRepairDoesNotChangeEarlierAssignments()
	{
		var (function, graph, _, _) = CreatePressure(true, "forbidden");
		var before = M68kGraphColoringAllocator.Allocate(function, graph);
		var (controlFunction, controlGraph, _, _) = CreatePressure(false, "forbidden");
		var control = M68kGraphColoringAllocator.Allocate(controlFunction, controlGraph);
		Assert.Equal(control.SpilledValues.Order(), before.SpilledValues.Order());
		foreach (var (value, location) in control.Registers)
		{
			Assert.Equal(location, before.Registers[value]);
		}
	}

	[Fact]
	public void RepairMovesTheScalarWithLowerLoopWeightedDemand()
	{
		var function = new M68kMachineFunction("weighted-bank-repair", 0)
		{
			SimplifyLoopPhis = true
		};
		var flag = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Byte,
			M68kRegisterSet.From(M68kRegister.D2, M68kRegister.D3), spillWeight: 1);
		var hot = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D2, M68kRegister.A2, M68kRegister.A3), spillWeight: 20);
		var cold = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D3, M68kRegister.A2, M68kRegister.A3), spillWeight: 10);
		var leaves = Enumerable.Range(0, 2).Select(_ => function.CreateValue(
			CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.A2), spillWeight: 10)).ToArray();
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		foreach (var value in function.Values.Values)
		{
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
				block.Instructions.Count, definitions: [value.Id]));
		}
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			block.Instructions.Count));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		foreach (var scalar in new[] { hot, cold })
		{
			graph.AddEdge(flag.Id, scalar.Id);
			foreach (var leaf in leaves) graph.AddEdge(scalar.Id, leaf.Id);
		}
		var result = M68kGraphColoringAllocator.Allocate(function, graph);
		Assert.Empty(result.SpilledValues);
		Assert.Equal(M68kRegister.D3, result.Registers[flag.Id].Register);
		Assert.Equal(M68kRegister.D2, result.Registers[hot.Id].Register);
		Assert.Equal(M68kRegister.A3, result.Registers[cold.Id].Register);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, result);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(true, false)]
	[InlineData(false, true)]
	[InlineData(true, true)]
	public void RepairLeavesThePreferredAddressColorForPendingNeighbor(bool copyPreference, bool forbiddenPreference)
	{
		var function = new M68kMachineFunction("pending-address-bank-repair", 0)
		{
			SimplifyLoopPhis = true
		};
		var address = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			copyPreference ? M68kRegisterSet.From(M68kRegister.D3, M68kRegister.A2, M68kRegister.A3) :
				M68kRegisterSet.From(M68kRegister.A2, M68kRegister.A3), spillWeight: 10);
		var flag = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Byte,
			M68kRegisterSet.From(M68kRegister.D2), spillWeight: 1);
		var scalar = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D2, M68kRegister.A0, M68kRegister.A2, M68kRegister.A3), spillWeight: 10);
		var leaves = Enumerable.Range(0, 3).Select(_ => function.CreateValue(
			CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.A0), spillWeight: 10)).ToArray();
		var source = copyPreference ? function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.From(M68kRegister.A2),
			precoloredRegister: M68kRegister.A2) : null;
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		foreach (var value in function.Values.Values)
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
				block.Instructions.Count, definitions: [value.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			block.Instructions.Count));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		graph.AddEdge(address.Id, scalar.Id);
		graph.AddEdge(flag.Id, scalar.Id);
		foreach (var leaf in leaves) graph.AddEdge(scalar.Id, leaf.Id);
		if (source is not null) graph.AddCopyPreference(address.Id, source.Id);
		if (forbiddenPreference) graph.AddForbidden(address.Id, M68kRegisterSet.From(M68kRegister.A2));
		var result = M68kGraphColoringAllocator.Allocate(function, graph);
		Assert.Empty(result.SpilledValues);
		Assert.Equal(M68kRegister.D2, result.Registers[flag.Id].Register);
		Assert.Equal(forbiddenPreference ? M68kRegister.A2 : M68kRegister.A3,
			result.Registers[scalar.Id].Register);
		Assert.Equal(forbiddenPreference ? copyPreference ? M68kRegister.D3 : M68kRegister.A3 : M68kRegister.A2,
			result.Registers[address.Id].Register);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, result);
	}

	[Fact]
	public void RepairIsDeterministic()
	{
		var (function, graph, _, _) = CreatePressure(true);
		var first = M68kGraphColoringAllocator.Allocate(function, graph);
		for (var i = 0; i < 5; i++)
		{
			var next = M68kGraphColoringAllocator.Allocate(function, graph);
			Assert.Empty(next.SpilledValues);
			foreach (var (value, location) in first.Registers)
			{
				Assert.Equal(location, next.Registers[value]);
			}
		}
	}

	[Fact]
	public void WordValueCanAlsoRecoverItsDataRegister()
	{
		var (function, graph, flag, scalar) = CreatePressure(true, "word-flag");
		var result = M68kGraphColoringAllocator.Allocate(function, graph);
		Assert.Empty(result.SpilledValues);
		Assert.Equal(M68kRegister.D2, result.Registers[flag].Register);
		Assert.Equal(M68kRegister.A3, result.Registers[scalar].Register);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, result);
	}

	private static (M68kMachineFunction Function, M68kInterferenceGraph Graph,
		int Flag, int Scalar) CreatePressure(bool enabled, string? exclusion = null)
	{
		var function = new M68kMachineFunction("scalar-bank-repair", 0)
		{
			SimplifyLoopPhis = enabled
		};
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var flag = function.CreateValue(CilStackValueKind.Int32,
			exclusion == "pair" ? M68kMachineValueWidth.LongPair :
			exclusion == "wide" ? M68kMachineValueWidth.Long :
			exclusion == "word-flag" ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Byte,
			M68kRegisterSet.From(M68kRegister.D2), spillWeight: 1);
		if (exclusion == "pair")
		{
			function.Values[flag.Id] = flag with { Kind = CilStackValueKind.Int64 };
		}
		var scalar = function.CreateValue(
			exclusion == "byref" ? CilStackValueKind.ManagedPointer :
			exclusion == "gc" ? CilStackValueKind.Reference : CilStackValueKind.Int32,
			exclusion == "narrow" ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Long,
			exclusion == "narrow" ? M68kRegisterSet.From(M68kRegister.D2) :
				M68kRegisterSet.From(M68kRegister.D2, M68kRegister.A2, M68kRegister.A3),
			precoloredRegister: exclusion == "fixed" ? M68kRegister.D2 : null,
			isGcReference: exclusion == "gc", spillWeight: 10,
			isSpillTemporary: exclusion == "reload");
		var leaves = Enumerable.Range(0, 2).Select(_ => function.CreateValue(
			CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.A2), spillWeight: 10)).ToArray();
		M68kMachineValue? fixedAddress = exclusion == "interference"
			? function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
				M68kRegisterSet.From(M68kRegister.A3), precoloredRegister: M68kRegister.A3)
			: null;
		M68kMachineValue? secondScalar = exclusion == "multiple"
			? function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
				M68kRegisterSet.From(M68kRegister.D2, M68kRegister.A2, M68kRegister.A3),
				spillWeight: 10)
			: null;
		// A legal copy preference must win over the usual reference/byref An
		// preference, so these exclusions actually reach the failed-color path.
		M68kMachineValue? copyRegister = exclusion is "gc" or "byref"
			? function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
				M68kRegisterSet.From(M68kRegister.D2), precoloredRegister: M68kRegister.D2)
			: null;
		foreach (var value in function.Values.Values)
		{
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other,
				block.Instructions.Count, definitions: [value.Id]));
		}
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			block.Instructions.Count));
		var graph = new M68kInterferenceGraph(function.Values.Keys);
		graph.AddEdge(flag.Id, scalar.Id);
		foreach (var leaf in leaves) graph.AddEdge(scalar.Id, leaf.Id);
		if (secondScalar is not null)
		{
			graph.AddEdge(flag.Id, secondScalar.Id);
			foreach (var leaf in leaves) graph.AddEdge(secondScalar.Id, leaf.Id);
		}
		if (fixedAddress is not null) graph.AddEdge(scalar.Id, fixedAddress.Id);
		if (copyRegister is not null) graph.AddCopyPreference(scalar.Id, copyRegister.Id);
		if (exclusion is "forbidden" or "reserved")
		{
			graph.AddForbidden(scalar.Id, M68kRegisterSet.From(M68kRegister.A3));
			if (exclusion == "reserved")
			{
				function.ReservedRegisters = M68kRegisterSet.From(M68kRegister.A3);
			}
		}
		return (function, graph, flag.Id, scalar.Id);
	}
}
