/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDeadPhiTests
{
	[Fact]
	public void RemovesAnUnusedPhiAndCopyCycleWithoutRemovingLoopControl()
	{
		var (function, loop, seed, current, next) = CreateLoop();
		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Empty(loop.Phis);
		Assert.DoesNotContain(loop.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Copy);
		Assert.Contains(loop.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.ConditionalBranch);
		Assert.DoesNotContain(seed, function.Values.Keys);
		Assert.DoesNotContain(current, function.Values.Keys);
		Assert.DoesNotContain(next, function.Values.Keys);
		Assert.True(statistics.PhisRemoved > 0);
		M68kMachineIrVerifier.Verify(function);
	}

	[Theory]
	[InlineData((int)M68kMachineOperation.Store)]
	[InlineData((int)M68kMachineOperation.Call)]
	[InlineData((int)M68kMachineOperation.Load)]
	[InlineData((int)M68kMachineOperation.GcKeepAlive)]
	public void RetainsPhiInputsNeededByObservableInstructions(int operationValue)
	{
		var operation = (M68kMachineOperation)operationValue;
		var (function, loop, seed, current, next) = CreateLoop(
			gcReference: operation == M68kMachineOperation.GcKeepAlive);
		var definition = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		loop.Instructions.Insert(1, function.CreateInstruction(operation, 1,
			uses: [current], definitions: operation is M68kMachineOperation.Load or
				M68kMachineOperation.Call ? [definition.Id] : [],
			memoryEffect: operation switch
			{
				M68kMachineOperation.Store => M68kMachineMemoryEffect.Write,
				M68kMachineOperation.Load => M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Volatile,
				M68kMachineOperation.Call => M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
				_ => M68kMachineMemoryEffect.None
			}, mayThrow: operation == M68kMachineOperation.Load,
			isSafepoint: operation == M68kMachineOperation.Call));

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Single(loop.Phis);
		Assert.Contains(seed, function.Values.Keys);
		Assert.Contains(current, function.Values.Keys);
		Assert.Contains(next, function.Values.Keys);
		Assert.Contains(loop.Instructions, instruction => instruction.Operation == operation);
		M68kMachineIrVerifier.Verify(function);
	}

	[Fact]
	public void AReturnedExpressionKeepsItsPhiDependencies()
	{
		var (function, loop, seed, current, next) = CreateLoop();
		var exit = function.Blocks.Single(block => block.Id == 2);
		var result = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		exit.Instructions.Clear();
		exit.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Negate,
			2, uses: [current], definitions: [result.Id],
			sourceInstruction: new CilInstruction(2, OpCodes.Neg, null, 3)));
		exit.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return,
			3, uses: [result.Id]));

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Single(loop.Phis);
		Assert.Contains(seed, function.Values.Keys);
		Assert.Contains(current, function.Values.Keys);
		Assert.Contains(next, function.Values.Keys);
		Assert.Contains(result.Id, function.Values.Keys);
		M68kMachineIrVerifier.Verify(function);
	}

	[Fact]
	public void RemovesDeadPhiConsumersButKeepsTheirSideEffectingProducer()
	{
		var (function, loop, seed, _, _) = CreateLoop();
		var entry = function.Blocks.Single(block => block.Id == 0);
		var seedIndex = entry.Instructions.FindIndex(instruction => instruction.Definitions.Contains(seed));
		entry.Instructions[seedIndex] = function.CreateInstruction(M68kMachineOperation.Call,
			0, definitions: [seed], memoryEffect: M68kMachineMemoryEffect.Write,
			isSafepoint: true);

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Empty(loop.Phis);
		Assert.Contains(entry.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Call && instruction.Definitions.Contains(seed));
		M68kMachineIrVerifier.Verify(function);
	}

	private static (M68kMachineFunction Function, M68kMachineBlock Loop,
		int Seed, int Current, int Next) CreateLoop(bool gcReference = false)
	{
		var function = new M68kMachineFunction("phi-cycle", 0);
		var entry = new M68kMachineBlock(0, 0);
		var loop = new M68kMachineBlock(1, 1) { LoopDepth = 1 };
		var exit = new M68kMachineBlock(2, 2);
		function.Blocks.AddRange([entry, loop, exit]);
		function.AddEdge(entry, loop);
		function.AddEdge(loop, loop);
		function.AddEdge(loop, exit);
		var condition = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var kind = gcReference ? CilStackValueKind.Reference : CilStackValueKind.Int32;
		var seed = function.CreateValue(kind,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data, isGcReference: gcReference);
		var current = function.CreateValue(kind,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data, isGcReference: gcReference);
		var next = function.CreateValue(kind,
			M68kMachineValueWidth.Long, M68kRegisterSet.Data, isGcReference: gcReference);
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument,
			0, definitions: [condition.Id], argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument,
			0, definitions: [seed.Id], argumentIndex: 1));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Branch,
			0, sourceInstruction: new CilInstruction(0, OpCodes.Br_S, 1, 1)));
		loop.Phis.Add(new M68kMachinePhi(current.Id,
			new Dictionary<int, int> { [entry.Id] = seed.Id, [loop.Id] = next.Id }));
		loop.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy,
			1, uses: [current.Id], definitions: [next.Id]));
		loop.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ConditionalBranch,
			1, uses: [condition.Id], sourceInstruction: new CilInstruction(1, OpCodes.Brtrue_S, 1, 2),
			branchCondition: new M68kMachineBranchCondition(M68kMachineConditionSourceKind.Test,
				M68kCondition.NotEqual)));
		exit.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 2));
		return (function, loop, seed.Id, current.Id, next.Id);
	}
}
