/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMachineOptimizerTests
{
	[Fact]
	public void FrameFieldByrefCallRequiresLiveCallerFrame()
	{
		using var module = new CompilationModule(
			typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.CompilerFixtures::TailFrameAliasEntry");
		var function = CilMachineIrBuilder.Build(method, module);
		M68kCallAbiLowering.FinalizeLogicalCalls(function);
		var allocated = M68kRegisterAllocatorPipeline.Run(
			function,
			allowUntrackedManagedByrefs: true);
		var call = Assert.Single(allocated.Function.Blocks
			.SelectMany(static block => block.Instructions)
			.Where(static instruction =>
				instruction.Operation == M68kMachineOperation.Call));

		Assert.True(call.RequiresLiveCallerFrame);
	}

	[Fact]
	public void DirectMultiwordBranchReturnsReuseOneHiddenBuffer()
	{
		using var module = new CompilationModule(
			typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.CompilerFixtures::DirectMultiwordBranchReturn");
		var function = CilMachineIrBuilder.Build(method, module);

		var home = Assert.Single(function.ReusableAggregateReturnHomes);
		Assert.Equal(8, home.Key.Size);
		Assert.Equal(8, function.LocalHomes[home.Value].Size);
	}

	[Fact]
	public void AggressiveGuestReadMachineBodyUsesGuestMemoryIntrinsic()
	{
		using var module = new CompilationModule(
			typeof(CompilerFixtures).Assembly.Location);
		var method = module.ResolveEntryPoint(
			"CopperSharp.Compiler.Tests.CompilerFixtures::AggressiveGuestRead");
		var function = CilMachineIrBuilder.Build(method, module);
		var call = Assert.Single(function.Blocks.SelectMany(static block => block.Instructions)
			.Where(static instruction => instruction.Operation == M68kMachineOperation.Call));
		var source = call.Origin!.SourceInstruction!;
		var callee = module.ResolveMethodToken((int)source.Operand!,
			call.Origin.SourceMethod, source.Offset);
		var cached = module.ResolveMethodToken((int)source.Operand!,
			call.Origin.SourceMethod, source.Offset);

		Assert.Same(callee, cached);
		Assert.Equal("intrinsic:aptr-read-uint32", callee.ImportName);
		Assert.Null(callee.Definition);
		Assert.NotNull(call.LogicalCall);
		Assert.True(call.MayThrow);
		Assert.True(call.IsSafepoint);
	}

	[Fact]
	public void OriginRetainsSourceMethodAcrossInlineSiteChain()
	{
		var sourceInstruction = new CilInstruction(0, OpCodes.Ldc_I4_1, null, 1);
		var sourceMethod = CreateMethod("Source.Module", "Source::Value", [sourceInstruction]);
		var callerMethod = CreateMethod("Caller.Module", "Caller::Value", [
			new CilInstruction(5, OpCodes.Call, 0x06000001, 10)]);
		var function = new M68kMachineFunction(sourceMethod.DisplayName, 0, sourceMethod);
		var block = AddBlock(function, 0, 0);
		var value = CreateLong(function);
		var instruction = function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [value.Id],
			sourceInstruction: sourceInstruction,
			constantValue: M68kMachineConstant.Int32(1));
		block.Instructions.Add(instruction with
		{
			Origin = instruction.Origin!.AtInlineSite(
				callerMethod,
				callerMethod.Instructions[0])
		});
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			1,
			uses: [value.Id]));

		M68kMachineIrVerifier.Verify(function);

		var origin = block.Instructions[0].Origin!;
		Assert.Equal("Source.Module", origin.SourceMethodIdentity.ModuleName);
		Assert.Equal("Caller.Module", Assert.Single(origin.InlineSites).Caller.ModuleName);
	}

	[Fact]
	public void VerifierRejectsMissingOriginForLoweredFunction()
	{
		var source = new CilInstruction(0, OpCodes.Ret, null, 1);
		var method = CreateMethod("Origin.Module", "Origin::Missing", [source]);
		var function = new M68kMachineFunction(method.DisplayName, 0, method);
		var block = AddBlock(function, 0, 0);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			0,
			sourceInstruction: source) with { Origin = null });

		var exception = Assert.Throws<InvalidOperationException>(() =>
			M68kMachineIrVerifier.Verify(function));

		Assert.Contains("has no source origin", exception.Message);
	}

	[Fact]
	public void TypedExceptionEdgeDoesNotBecomeAPhiPredecessor()
	{
		var function = new M68kMachineFunction("typed-eh", 0);
		var body = AddBlock(function, 0, 0);
		var handler = AddBlock(function, 1, 10);
		handler.IsExceptionEntry = true;
		body.ActiveExceptionRegionIds.Add(0);
		function.ExceptionRegions.Add(new M68kMachineExceptionRegion(
			0,
			new CilExceptionRegion(
				ExceptionRegionKind.Catch,
				0,
				10,
				10,
				10,
				default,
				-1),
			handler.Id,
			[body.Id],
			[handler.Id],
			null));
		function.AddEdge(
			body,
			handler,
			M68kMachineEdgeKind.ExceptionDispatch,
			0);
		body.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Throw,
			0,
			mayThrow: true));
		handler.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			10));

		M68kMachineIrVerifier.Verify(function);

		Assert.Empty(handler.Predecessors);
		var edge = Assert.Single(body.SuccessorEdges);
		Assert.Equal(M68kMachineEdgeKind.ExceptionDispatch, edge.Kind);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ModuleOptimizerHonorsNoInliningForSafeScalarCall(bool noInlining)
	{
		var callerMethod = CreateMethod(
			"Inline.Module",
			"Inline::Caller",
			[new CilInstruction(0, OpCodes.Call, 0x06000002, 5)],
			methodRow: 1);
		var targetMethod = CreateMethod(
			"Inline.Module",
			"Inline::Target",
			[new CilInstruction(0, OpCodes.Ldc_I4_7, null, 1),
			 new CilInstruction(1, OpCodes.Ret, null, 2)],
			methodRow: 2,
			implAttributes: noInlining ? MethodImplAttributes.NoInlining : 0);
		var caller = new M68kMachineFunction(callerMethod.DisplayName, 0, callerMethod);
		var callerBlock = AddBlock(caller, 0, 0);
		var fixedResult = CreateLong(caller);
		var logicalResult = CreateLong(caller);
		var callSource = callerMethod.Instructions[0];
		var origin = caller.OriginAt(0, callSource)!;
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Call,
			0,
			definitions: [fixedResult.Id],
			clobbers: M68kRegisterSet.Data,
			memoryEffect: M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: callSource,
			logicalCall: new M68kMachineLogicalCall(
				M68kMachineCallDispatchKind.Direct,
				[targetMethod.Identity],
				[],
				[logicalResult.Id],
				false,
				origin)));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Copy,
			0,
			uses: [fixedResult.Id],
			definitions: [logicalResult.Id]));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Return,
			5,
			uses: [logicalResult.Id]));

		var target = new M68kMachineFunction(targetMethod.DisplayName, 0, targetMethod);
		var targetBlock = AddBlock(target, 0, 0);
		var constant = CreateLong(target);
		targetBlock.Instructions.Add(target.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [constant.Id],
			sourceInstruction: targetMethod.Instructions[0],
			constantValue: M68kMachineConstant.Int32(7)));
		targetBlock.Instructions.Add(target.CreateInstruction(
			M68kMachineOperation.Return,
			1,
			uses: [constant.Id],
			sourceInstruction: targetMethod.Instructions[1]));

		using var module = new CompilationModule(
			typeof(M68kMachineOptimizerTests).Assembly.Location);
		var statistics = M68kMachineModuleOptimizer.Run(
			[callerMethod, targetMethod],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[callerMethod.Identity] = caller,
				[targetMethod.Identity] = target
			},
			module,
			M68kCpuTarget.M68000);

		Assert.Equal(noInlining ? 0 : 1, statistics.InlinedCalls);
		if (noInlining)
		{
			Assert.Contains(callerBlock.Instructions, static instruction =>
				instruction.Operation == M68kMachineOperation.Call);
			return;
		}
		Assert.DoesNotContain(callerBlock.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.Call);
		var returnValue = Assert.Single(callerBlock.Instructions[^1].Uses);
		var folded = Assert.Single(callerBlock.Instructions.Where(instruction =>
			instruction.Definitions.Contains(returnValue)));
		Assert.Equal(M68kMachineConstant.Int32(7), folded.ConstantValue);
		Assert.Equal(targetMethod.DisplayName, folded.Origin!.SourceMethod.DisplayName);
		Assert.Single(folded.Origin.InlineSites);
	}

	[Fact]
	public void ScalarInliningPreservesAbiPrefixCopyUsedAfterCall()
	{
		var callSource = new CilInstruction(0, OpCodes.Call, 0x06000002, 1);
		var callerMethod = CreateMethod("Inline.Prefix", "Inline::Caller",
			[callSource], methodRow: 1);
		var targetMethod = CreateMethod("Inline.Prefix", "Inline::Target",
			[new CilInstruction(0, OpCodes.Ret, null, 1)], methodRow: 2);
		var caller = new M68kMachineFunction(callerMethod.DisplayName, 0,
			callerMethod);
		var callerBlock = AddBlock(caller, 0, 0);
		var logicalArgument = CreateLong(caller);
		var abiArgument = CreateLong(caller);
		var returned = CreateLong(caller);
		var origin = caller.OriginAt(0, callSource)!;
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Argument, 0,
			definitions: [logicalArgument.Id], argumentIndex: 0));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Copy, 0,
			uses: [logicalArgument.Id], definitions: [abiArgument.Id],
			sourceInstruction: callSource));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Call, 0,
			uses: [abiArgument.Id],
			clobbers: M68kRegisterSet.Data,
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Write,
			isSafepoint: true, mayThrow: true, sourceInstruction: callSource,
			logicalCall: new M68kMachineLogicalCall(
				M68kMachineCallDispatchKind.Direct,
				[targetMethod.Identity], [logicalArgument.Id], [], false, origin)));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Copy, 1,
			uses: [abiArgument.Id], definitions: [returned.Id]));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Return, 2, uses: [returned.Id]));

		var target = new M68kMachineFunction(targetMethod.DisplayName, 0,
			targetMethod);
		var targetBlock = AddBlock(target, 0, 0);
		var targetArgument = CreateLong(target);
		targetBlock.Instructions.Add(target.CreateInstruction(
			M68kMachineOperation.Argument, 0,
			definitions: [targetArgument.Id], argumentIndex: 0));
		targetBlock.Instructions.Add(target.CreateInstruction(
			M68kMachineOperation.Return, 0,
			sourceInstruction: targetMethod.Instructions[0]));

		using var module = new CompilationModule(
			typeof(M68kMachineOptimizerTests).Assembly.Location);
		var statistics = M68kMachineModuleOptimizer.Run(
			[callerMethod, targetMethod],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[callerMethod.Identity] = caller,
				[targetMethod.Identity] = target
			},
			module,
			M68kCpuTarget.M68000);

		Assert.Equal(0, statistics.InlinedCalls);
		Assert.Contains(callerBlock.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.Call);
		M68kMachineIrVerifier.Verify(caller);
	}

	[Fact]
	public void ScalarInliningDefinesFixedResultReusedAfterSuffixCopy()
	{
		var callSource = new CilInstruction(0, OpCodes.Call, 0x06000002, 1);
		var callerMethod = CreateMethod("Inline.Result", "Inline::Caller",
			[callSource], methodRow: 1);
		var targetMethod = CreateMethod("Inline.Result", "Inline::Target",
			[new CilInstruction(0, OpCodes.Ldc_I4_7, null, 1),
			 new CilInstruction(1, OpCodes.Ret, null, 2)], methodRow: 2);
		var caller = new M68kMachineFunction(callerMethod.DisplayName, 0,
			callerMethod);
		var callerBlock = AddBlock(caller, 0, 0);
		var fixedResult = CreateLong(caller);
		var logicalResult = CreateLong(caller);
		var reusedResult = CreateLong(caller);
		var origin = caller.OriginAt(0, callSource)!;
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Call, 0,
			definitions: [fixedResult.Id], clobbers: M68kRegisterSet.Data,
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Write,
			isSafepoint: true, mayThrow: true, sourceInstruction: callSource,
			logicalCall: new M68kMachineLogicalCall(
				M68kMachineCallDispatchKind.Direct, [targetMethod.Identity], [],
				[logicalResult.Id], false, origin)));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Copy, 0,
			uses: [fixedResult.Id], definitions: [logicalResult.Id]));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Copy, 1,
			uses: [fixedResult.Id], definitions: [reusedResult.Id]));
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Return, 2, uses: [reusedResult.Id]));

		var target = new M68kMachineFunction(targetMethod.DisplayName, 0,
			targetMethod);
		var targetBlock = AddBlock(target, 0, 0);
		var constant = CreateLong(target);
		targetBlock.Instructions.Add(target.CreateInstruction(
			M68kMachineOperation.Constant, 0, definitions: [constant.Id],
			sourceInstruction: targetMethod.Instructions[0],
			constantValue: M68kMachineConstant.Int32(7)));
		targetBlock.Instructions.Add(target.CreateInstruction(
			M68kMachineOperation.Return, 1, uses: [constant.Id],
			sourceInstruction: targetMethod.Instructions[1]));

		using var module = new CompilationModule(
			typeof(M68kMachineOptimizerTests).Assembly.Location);
		var statistics = M68kMachineModuleOptimizer.Run(
			[callerMethod, targetMethod],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[callerMethod.Identity] = caller,
				[targetMethod.Identity] = target
			}, module, M68kCpuTarget.M68000);

		Assert.Equal(0, statistics.InlinedCalls);
		Assert.Contains(callerBlock.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.Call);
		M68kMachineIrVerifier.Verify(caller);
	}

	[Fact]
	public void FoldsIntegralExpressionAndRemovesItsDeadOperands()
	{
		var function = new M68kMachineFunction("constant-fold", 0);
		var block = AddBlock(function, 0, 0);
		var left = CreateLong(function);
		var right = CreateLong(function);
		var sum = CreateLong(function);
		AddConstant(function, block, left.Id, 20, 0);
		AddConstant(function, block, right.Id, 22, 1);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			2,
			uses: [left.Id, right.Id],
			definitions: [sum.Id],
			sourceInstruction: new CilInstruction(2, OpCodes.Add, null, 3)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [sum.Id]));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		var folded = Assert.Single(block.Instructions.Where(instruction =>
			instruction.Definitions.Contains(sum.Id)));
		Assert.Equal(M68kMachineOperation.Constant, folded.Operation);
		Assert.Equal(M68kMachineConstant.Int32(42), folded.ConstantValue);
		Assert.DoesNotContain(left.Id, function.Values.Keys);
		Assert.DoesNotContain(right.Id, function.Values.Keys);
		Assert.True(statistics.ConstantsFolded > 0);
	}

	[Fact]
	public void ConstantBranchPrunesOnlyTheUntakenNormalPath()
	{
		var function = new M68kMachineFunction("constant-branch", 0);
		var entry = AddBlock(function, 0, 0);
		var taken = AddBlock(function, 1, 10);
		var discarded = AddBlock(function, 2, 20);
		Connect(entry, taken);
		Connect(entry, discarded);
		var condition = function.CreateValue(
			CilStackValueKind.BooleanByte,
			M68kMachineValueWidth.Byte,
			M68kRegisterSet.Data);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [condition.Id],
			sourceInstruction: new CilInstruction(0, OpCodes.Ldc_I4_1, null, 1),
			constantValue: M68kMachineConstant.Boolean(true)));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			1,
			uses: [condition.Id],
			sourceInstruction: new CilInstruction(1, OpCodes.Brtrue, 10, 2)));
		taken.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			10));
		discarded.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			20));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Equal([0], function.Blocks.Select(static block => block.Id));
		Assert.Empty(entry.Successors);
		Assert.Equal(M68kMachineOperation.Return, entry.Instructions[^1].Operation);
		Assert.Equal(1, statistics.BranchesFolded);
		Assert.Equal(2, statistics.BlocksRemoved);
	}

	[Fact]
	public void ConstantSwitchRepairsEdgesAndPrunesOtherCases()
	{
		var function = new M68kMachineFunction("constant-switch", 0);
		var entry = AddBlock(function, 0, 0);
		var first = AddBlock(function, 1, 10);
		var selected = AddBlock(function, 2, 20);
		var fallback = AddBlock(function, 3, 30);
		Connect(entry, first);
		Connect(entry, selected);
		Connect(entry, fallback);
		var selector = CreateLong(function);
		AddConstant(function, entry, selector.Id, 1, 0);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Switch,
			1,
			uses: [selector.Id],
			sourceInstruction: new CilInstruction(
				1,
				OpCodes.Switch,
				new[] { 10, 20 },
				30)));
		foreach (var block in new[] { first, selected, fallback })
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Return,
				block.StartIlOffset));
		}

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Equal([0], function.Blocks.Select(static block => block.Id));
		Assert.Empty(entry.Successors);
		Assert.Equal(M68kMachineOperation.Return, entry.Instructions[^1].Operation);
		Assert.Equal(1, statistics.BranchesFolded);
		Assert.Equal(3, statistics.BlocksRemoved);
	}

	[Fact]
	public void EmptyBlockThreadingRepairsSuccessorPhiInputs()
	{
		var function = new M68kMachineFunction("empty-thread", 0);
		var entry = AddBlock(function, 0, 0);
		var left = AddBlock(function, 1, 10);
		var right = AddBlock(function, 2, 20);
		var empty = AddBlock(function, 3, 30);
		var exit = AddBlock(function, 4, 40);
		Connect(entry, left);
		Connect(entry, right);
		Connect(left, empty);
		Connect(right, empty);
		Connect(empty, exit);
		var condition = CreateLong(function);
		var value = CreateLong(function);
		var merged = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [condition.Id],
			argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [value.Id],
			argumentIndex: 1));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			1,
			uses: [condition.Id],
			sourceInstruction: new CilInstruction(1, OpCodes.Brtrue, 10, 2)));
		left.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			10,
			sourceInstruction: new CilInstruction(10, OpCodes.Br, 30, 11)));
		right.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			20,
			sourceInstruction: new CilInstruction(20, OpCodes.Br, 30, 21)));
		empty.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			30,
			sourceInstruction: new CilInstruction(30, OpCodes.Br, 40, 31)));
		exit.Phis.Add(new M68kMachinePhi(
			merged.Id,
			new Dictionary<int, int> { [empty.Id] = value.Id }));
		exit.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			40,
			uses: [merged.Id]));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.DoesNotContain(function.Blocks, block => block.Id == empty.Id);
		Assert.True(statistics.BlocksRemoved > 0);
		Assert.Equal([exit.Id], left.Successors);
		Assert.Equal([exit.Id], right.Successors);
		Assert.Equal([value.Id], exit.Instructions[^1].Uses);
	}

	[Fact]
	public void LinearBlockMergeRemovesTrivialPhiAndBranch()
	{
		var function = new M68kMachineFunction("linear-merge", 0);
		var entry = AddBlock(function, 0, 0);
		var exit = AddBlock(function, 1, 10);
		Connect(entry, exit);
		var argument = CreateLong(function);
		var merged = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [argument.Id],
			argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			1,
			sourceInstruction: new CilInstruction(1, OpCodes.Br, 10, 2)));
		exit.Phis.Add(new M68kMachinePhi(
			merged.Id,
			new Dictionary<int, int> { [entry.Id] = argument.Id }));
		exit.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			10,
			uses: [merged.Id]));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Single(function.Blocks);
		Assert.True(statistics.BlocksRemoved > 0);
		Assert.Empty(entry.Phis);
		Assert.DoesNotContain(entry.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.Branch);
		Assert.Equal([argument.Id], entry.Instructions[^1].Uses);
	}

	[Fact]
	public void DoesNotFoldThrowingConstantDivision()
	{
		var function = new M68kMachineFunction("division-fault", 0);
		var block = AddBlock(function, 0, 0);
		var dividend = CreateLong(function);
		var divisor = CreateLong(function);
		var quotient = CreateLong(function);
		AddConstant(function, block, dividend.Id, 1, 0);
		AddConstant(function, block, divisor.Id, 0, 1);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Divide,
			2,
			uses: [dividend.Id, divisor.Id],
			definitions: [quotient.Id],
			mayThrow: true,
			sourceInstruction: new CilInstruction(2, OpCodes.Div, null, 3)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [quotient.Id]));

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Contains(block.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.Divide && instruction.MayThrow);
	}

	[Fact]
	public void DoesNotFoldCheckedUnsignedOverflow()
	{
		var function = new M68kMachineFunction("checked-overflow", 0);
		var block = AddBlock(function, 0, 0);
		var left = CreateLong(function);
		var right = CreateLong(function);
		var sum = CreateLong(function);
		AddConstant(function, block, left.Id, -1, 0);
		AddConstant(function, block, right.Id, 1, 1);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			2,
			uses: [left.Id, right.Id],
			definitions: [sum.Id],
			mayThrow: true,
			sourceInstruction: new CilInstruction(2, OpCodes.Add_Ovf_Un, null, 3)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [sum.Id]));

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Contains(block.Instructions, static instruction =>
			instruction.SourceInstruction?.OpCode == OpCodes.Add_Ovf_Un &&
			instruction.MayThrow);
	}

	[Fact]
	public void EliminatesDominatedCommonExpression()
	{
		var function = new M68kMachineFunction("gvn", 0);
		var block = AddBlock(function, 0, 0);
		var left = CreateLong(function);
		var right = CreateLong(function);
		var first = CreateLong(function);
		var duplicate = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [left.Id],
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [right.Id],
			argumentIndex: 1));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			1,
			uses: [left.Id, right.Id],
			definitions: [first.Id],
			sourceInstruction: new CilInstruction(1, OpCodes.Add, null, 2)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			2,
			uses: [left.Id, right.Id],
			definitions: [duplicate.Id],
			sourceInstruction: new CilInstruction(2, OpCodes.Add, null, 3)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [duplicate.Id]));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Single(block.Instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Add));
		Assert.Equal([first.Id], block.Instructions[^1].Uses);
		Assert.Equal(1, statistics.CommonExpressionsRemoved);
	}

	[Fact]
	public void DeadCodeKeepsThrowSafepointAndVolatileBoundaries()
	{
		var function = new M68kMachineFunction("effect-boundaries", 0);
		var block = AddBlock(function, 0, 0);
		var throwing = CreateLong(function);
		var safepoint = CreateLong(function);
		var volatileRead = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [throwing.Id],
			mayThrow: true));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			1,
			definitions: [safepoint.Id],
			isSafepoint: true));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Load,
			2,
			definitions: [volatileRead.Id],
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Volatile));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3));

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Contains(block.Instructions, instruction =>
			instruction.Definitions.Contains(throwing.Id));
		Assert.Contains(block.Instructions, instruction =>
			instruction.Definitions.Contains(safepoint.Id));
		Assert.Contains(block.Instructions, instruction =>
			instruction.Definitions.Contains(volatileRead.Id));
	}

	[Fact]
	public void ExactFrameStoreForwardsIntoLoad()
	{
		var function = new M68kMachineFunction("frame-forward", 0);
		var block = AddBlock(function, 0, 0);
		var source = CreateLong(function);
		var loaded = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [source.Id],
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalStore,
			1,
			uses: [source.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalLoad,
			2,
			definitions: [loaded.Id],
			memoryEffect: M68kMachineMemoryEffect.Read,
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [loaded.Id]));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.True(statistics.LoadsForwarded > 0);
		Assert.DoesNotContain(block.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.LocalLoad);
		Assert.Equal([source.Id], block.Instructions[^1].Uses);
	}

	[Fact]
	public void SafepointPreventsOverwrittenFrameStoreRemoval()
	{
		var function = new M68kMachineFunction("frame-barrier", 0);
		var block = AddBlock(function, 0, 0);
		var first = CreateLong(function);
		var second = CreateLong(function);
		foreach (var (value, argument) in new[] { (first, 0), (second, 1) })
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Argument,
				0,
				definitions: [value.Id],
				argumentIndex: argument));
		}
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalStore,
			1,
			uses: [first.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			2,
			isSafepoint: true));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalStore,
			3,
			uses: [second.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			4));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Equal(0, statistics.StoresRemoved);
		Assert.Equal(2, block.Instructions.Count(static instruction =>
			instruction.Operation == M68kMachineOperation.LocalStore));
	}

	[Fact]
	public void MemoryPromotionForwardsFrameValueAcrossBlocks()
	{
		var function = new M68kMachineFunction("memory-cross-block", 0);
		var entry = AddBlock(function, 0, 0);
		var exit = AddBlock(function, 1, 10);
		Connect(entry, exit);
		function.LocalHomes[0] = new M68kFrameHome(0, 4, false);
		var memory = FrameMemory(0, 4);
		var source = CreateLong(function);
		var loaded = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [source.Id],
			argumentIndex: 0));
		entry.Instructions.Add(FrameStore(function, memory, source.Id, 1));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			2));
		exit.Instructions.Add(FrameLoad(function, memory, loaded.Id, 10));
		exit.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			11,
			uses: [loaded.Id]));

		var statistics = RunPromotion(function);

		Assert.Equal(1, statistics.LoadsForwarded);
		Assert.Equal(1, statistics.StoresRemoved);
		Assert.DoesNotContain(function.Blocks.SelectMany(static block =>
			block.Instructions), static instruction => instruction.Operation is
				M68kMachineOperation.LocalLoad or
				M68kMachineOperation.LocalStore);
		var alias = Assert.Single(exit.Instructions.Where(instruction =>
			instruction.Definitions.Contains(loaded.Id)));
		Assert.Equal(M68kMachineOperation.Copy, alias.Operation);
		Assert.Equal([source.Id], alias.Uses);
	}

	[Fact]
	public void MemoryPromotionBuildsPhiAtControlFlowJoin()
	{
		var function = new M68kMachineFunction("memory-join", 0);
		var entry = AddBlock(function, 0, 0);
		var left = AddBlock(function, 1, 10);
		var right = AddBlock(function, 2, 20);
		var join = AddBlock(function, 3, 30);
		Connect(entry, left);
		Connect(entry, right);
		Connect(left, join);
		Connect(right, join);
		function.LocalHomes[0] = new M68kFrameHome(0, 4, false);
		var memory = FrameMemory(0, 4);
		var condition = CreateLong(function);
		var leftValue = CreateLong(function);
		var rightValue = CreateLong(function);
		var loaded = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [condition.Id],
			argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			1,
			uses: [condition.Id]));
		AddConstant(function, left, leftValue.Id, 11, 10);
		left.Instructions.Add(FrameStore(function, memory, leftValue.Id, 11));
		left.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			12));
		AddConstant(function, right, rightValue.Id, 22, 20);
		right.Instructions.Add(FrameStore(function, memory, rightValue.Id, 21));
		right.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			22));
		join.Instructions.Add(FrameLoad(function, memory, loaded.Id, 30));
		join.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			31,
			uses: [loaded.Id]));

		var statistics = RunPromotion(function);

		var phi = Assert.Single(join.Phis);
		Assert.Equal(2, phi.Inputs.Count);
		Assert.Equal(leftValue.Id, phi.Inputs[left.Id]);
		Assert.Equal(rightValue.Id, phi.Inputs[right.Id]);
		var alias = Assert.Single(join.Instructions.Where(instruction =>
			instruction.Definitions.Contains(loaded.Id)));
		Assert.Equal(M68kMachineOperation.Copy, alias.Operation);
		Assert.Equal([phi.Definition], alias.Uses);
		Assert.Equal(1, statistics.PhisInserted);
		Assert.Equal(2, statistics.StoresRemoved);
	}

	[Fact]
	public void MemoryPromotionBuildsLoopCarriedPhi()
	{
		var function = new M68kMachineFunction("memory-loop", 0);
		var entry = AddBlock(function, 0, 0);
		var header = AddBlock(function, 1, 10);
		var body = AddBlock(function, 2, 20);
		var exit = AddBlock(function, 3, 30);
		Connect(entry, header);
		Connect(header, body);
		Connect(header, exit);
		Connect(body, header);
		function.LocalHomes[0] = new M68kFrameHome(0, 4, false);
		var memory = FrameMemory(0, 4);
		var initial = CreateLong(function);
		var condition = CreateLong(function);
		var current = CreateLong(function);
		var one = CreateLong(function);
		var next = CreateLong(function);
		var result = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [initial.Id],
			argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [condition.Id],
			argumentIndex: 1));
		entry.Instructions.Add(FrameStore(function, memory, initial.Id, 1));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			2));
		header.Instructions.Add(FrameLoad(function, memory, current.Id, 10));
		header.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			11,
			uses: [condition.Id]));
		AddConstant(function, body, one.Id, 1, 20);
		body.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			21,
			uses: [current.Id, one.Id],
			definitions: [next.Id]));
		body.Instructions.Add(FrameStore(function, memory, next.Id, 22));
		body.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			23));
		exit.Instructions.Add(FrameLoad(function, memory, result.Id, 30));
		exit.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			31,
			uses: [result.Id]));

		var statistics = RunPromotion(function);

		var phi = Assert.Single(header.Phis);
		Assert.Equal(initial.Id, phi.Inputs[entry.Id]);
		Assert.Equal(next.Id, phi.Inputs[body.Id]);
		Assert.Equal([phi.Definition], header.Instructions[0].Uses);
		Assert.Equal(1, statistics.PhisInserted);
		Assert.Equal(2, statistics.LoadsForwarded);
	}

	[Fact]
	public void MemoryPromotionUsesLatestOverwritingStore()
	{
		var function = new M68kMachineFunction("memory-overwrite", 0);
		var block = AddBlock(function, 0, 0);
		function.LocalHomes[0] = new M68kFrameHome(0, 4, false);
		var memory = FrameMemory(0, 4);
		var first = CreateLong(function);
		var second = CreateLong(function);
		var loaded = CreateLong(function);
		AddConstant(function, block, first.Id, 1, 0);
		AddConstant(function, block, second.Id, 2, 1);
		block.Instructions.Add(FrameStore(function, memory, first.Id, 2));
		block.Instructions.Add(FrameStore(function, memory, second.Id, 3));
		block.Instructions.Add(FrameLoad(function, memory, loaded.Id, 4));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			5,
			uses: [loaded.Id]));

		var statistics = RunPromotion(function);

		Assert.Equal(2, statistics.StoresRemoved);
		Assert.Equal(1, statistics.LoadsForwarded);
		var alias = Assert.Single(block.Instructions.Where(instruction =>
			instruction.Definitions.Contains(loaded.Id)));
		Assert.Equal(M68kMachineOperation.Copy, alias.Operation);
		Assert.Equal([second.Id], alias.Uses);
	}

	[Fact]
	public void MemoryPromotionPreservesByteTruncationAndZeroExtension()
	{
		var function = new M68kMachineFunction("memory-narrow", 0);
		var block = AddBlock(function, 0, 0);
		function.LocalHomes[0] = new M68kFrameHome(0, 1, false);
		var memory = FrameMemory(0, 1);
		var source = CreateLong(function);
		var loaded = CreateLong(function);
		AddConstant(function, block, source.Id, 0x1ff, 0);
		block.Instructions.Add(FrameStore(function, memory, source.Id, 1));
		block.Instructions.Add(FrameLoad(
			function,
			memory,
			loaded.Id,
			2,
			new CilInstruction(2, OpCodes.Ldind_U1, null, 3)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [loaded.Id]));

		var statistics = RunPromotion(function);

		Assert.Equal(1, statistics.NormalizationsInserted);
		var conversions = block.Instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.Convert).ToArray();
		Assert.Equal(2, conversions.Length);
		Assert.All(conversions, static conversion =>
			Assert.Equal(OpCodes.Conv_U1, conversion.SourceInstruction!.OpCode));
	}

	[Fact]
	public void MemoryPromotionRejectsAddressTakenAndVolatileLocations()
	{
		var function = new M68kMachineFunction("memory-address-volatile", 0);
		var block = AddBlock(function, 0, 0);
		function.LocalHomes[0] = new M68kFrameHome(0, 4, false);
		function.LocalHomes[1] = new M68kFrameHome(1, 4, false);
		var addressMemory = FrameMemory(0, 4);
		var volatileMemory = FrameMemory(1, 4);
		var address = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		var value = CreateLong(function);
		var loaded = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalAddress,
			0,
			definitions: [address.Id],
			argumentIndex: 0,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					addressMemory,
					M68kExactMemoryAccessKind.Address,
					address.Id)
			]));
		AddConstant(function, block, value.Id, 7, 1);
		block.Instructions.Add(FrameStore(
			function,
			volatileMemory,
			value.Id,
			2,
			M68kMachineMemoryEffect.Write |
				M68kMachineMemoryEffect.Volatile,
			localIndex: 1));
		block.Instructions.Add(FrameLoad(
			function,
			volatileMemory,
			loaded.Id,
			3,
			sourceInstruction: null,
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Volatile,
			localIndex: 1));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			4,
			uses: [loaded.Id]));

		var statistics = RunPromotion(function);

		Assert.Equal(0, statistics.PromotedLocations);
		Assert.Contains(block.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.LocalAddress);
		Assert.Contains(block.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.LocalLoad);
	}

	[Fact]
	public void MemoryPromotionLeavesExceptionalObserversMaterialized()
	{
		var function = new M68kMachineFunction("memory-exception", 0)
		{
			HasExceptionHandlers = true
		};
		var block = AddBlock(function, 0, 0);
		function.LocalHomes[0] = new M68kFrameHome(0, 4, false);
		var memory = FrameMemory(0, 4);
		var value = CreateLong(function);
		var loaded = CreateLong(function);
		AddConstant(function, block, value.Id, 42, 0);
		block.Instructions.Add(FrameStore(function, memory, value.Id, 1));
		block.Instructions.Add(FrameLoad(function, memory, loaded.Id, 2));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [loaded.Id]));

		var statistics = RunPromotion(function);

		Assert.False(statistics.Changed);
		Assert.Contains(block.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.LocalStore);
		Assert.Contains(block.Instructions, static instruction =>
			instruction.Operation == M68kMachineOperation.LocalLoad);
	}

	[Fact]
	public void PromotedManagedFieldKeepsValueAndOwnerAliveAtSafepoint()
	{
		var function = new M68kMachineFunction("memory-gc-keepalive", 0);
		var block = AddBlock(function, 0, 0);
		var owner = CreateReference(function);
		var child = CreateReference(function);
		var loaded = CreateReference(function);
		var memory = new M68kMemoryObject(
			M68kMemoryObjectKind.ObjectField,
			"object:test",
			IsManagedRoot: true,
			OwnerValueId: owner.Id,
			Offset: 8,
			Size: 4);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [owner.Id],
			argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [child.Id],
			argumentIndex: 1));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Store,
			1,
			uses: [owner.Id, child.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					memory,
					M68kExactMemoryAccessKind.Write,
					child.Id)
			]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			2,
			isSafepoint: true));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Load,
			3,
			uses: [owner.Id],
			definitions: [loaded.Id],
			memoryEffect: M68kMachineMemoryEffect.Read,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					memory,
					M68kExactMemoryAccessKind.Read,
					loaded.Id)
			]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			4,
			uses: [loaded.Id]));
		var owners = new Dictionary<int, M68kHeapOwnerFacts>
		{
			[owner.Id] = new(
				owner.Id,
				IsArray: false,
				IsPromotable: true,
				HasFinalizer: false,
				ConstructorMayWrite: false,
				ConstantLength: null,
				ElementSize: 0,
				StorageIdentity: "object:test")
		};

		var statistics = RunPromotion(function, owners);

		var keepAlive = Assert.Single(block.Instructions.Where(static instruction =>
			instruction.Operation == M68kMachineOperation.GcKeepAlive));
		Assert.Equal([child.Id, owner.Id], keepAlive.Uses);
		Assert.Equal(1, statistics.KeepAlivesInserted);
		Assert.DoesNotContain(block.Instructions, static instruction =>
			instruction.Operation is M68kMachineOperation.Load or
				M68kMachineOperation.Store);
	}

	[Fact]
	public void MethodMemorySummariesPropagateReadWriteAndCaptureThroughCalls()
	{
		var readMethod = CreateMethod(
			"Summary.Module",
			"Summary::Read",
			[new CilInstruction(0, OpCodes.Ldfld, null, 1)],
			methodRow: 1);
		var writeMethod = CreateMethod(
			"Summary.Module",
			"Summary::Write",
			[new CilInstruction(0, OpCodes.Stfld, null, 1)],
			methodRow: 2);
		var captureMethod = CreateMethod(
			"Summary.Module",
			"Summary::Capture",
			[new CilInstruction(0, OpCodes.Stfld, null, 1)],
			methodRow: 3);
		var callSources = new[]
		{
			new CilInstruction(0, OpCodes.Call, 0x06000001, 1),
			new CilInstruction(1, OpCodes.Call, 0x06000002, 2),
			new CilInstruction(2, OpCodes.Call, 0x06000003, 3)
		};
		var callerMethod = CreateMethod(
			"Summary.Module",
			"Summary::Caller",
			callSources,
			methodRow: 4);

		var read = new M68kMachineFunction(readMethod.DisplayName, 0, readMethod);
		var readBlock = AddBlock(read, 0, 0);
		var readOwner = CreateReference(read);
		var readResult = CreateLong(read);
		readBlock.Instructions.Add(read.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [readOwner.Id],
			argumentIndex: 0));
		readBlock.Instructions.Add(read.CreateInstruction(
			M68kMachineOperation.Load,
			0,
			uses: [readOwner.Id],
			definitions: [readResult.Id],
			memoryEffect: M68kMachineMemoryEffect.Read,
			sourceInstruction: readMethod.Instructions[0],
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					SummaryField(readOwner.Id, "read"),
					M68kExactMemoryAccessKind.Read,
					readResult.Id)
			]));
		readBlock.Instructions.Add(read.CreateInstruction(
			M68kMachineOperation.Return,
			1,
			uses: [readResult.Id]));

		var write = new M68kMachineFunction(writeMethod.DisplayName, 0, writeMethod);
		var writeBlock = AddBlock(write, 0, 0);
		var writeOwner = CreateReference(write);
		var writeValue = CreateLong(write);
		AddArgument(write, writeBlock, writeOwner.Id, 0);
		AddArgument(write, writeBlock, writeValue.Id, 1);
		writeBlock.Instructions.Add(write.CreateInstruction(
			M68kMachineOperation.Store,
			0,
			uses: [writeOwner.Id, writeValue.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			sourceInstruction: writeMethod.Instructions[0],
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					SummaryField(writeOwner.Id, "write"),
					M68kExactMemoryAccessKind.Write,
					writeValue.Id)
			]));
		writeBlock.Instructions.Add(write.CreateInstruction(
			M68kMachineOperation.Return,
			1));

		var capture = new M68kMachineFunction(
			captureMethod.DisplayName,
			0,
			captureMethod);
		var captureBlock = AddBlock(capture, 0, 0);
		var destination = CreateReference(capture);
		var captured = CreateReference(capture);
		AddArgument(capture, captureBlock, destination.Id, 0);
		AddArgument(capture, captureBlock, captured.Id, 1);
		captureBlock.Instructions.Add(capture.CreateInstruction(
			M68kMachineOperation.Store,
			0,
			uses: [destination.Id, captured.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			sourceInstruction: captureMethod.Instructions[0],
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					SummaryField(destination.Id, "capture", isRoot: true),
					M68kExactMemoryAccessKind.Write,
					captured.Id)
			]));
		captureBlock.Instructions.Add(capture.CreateInstruction(
			M68kMachineOperation.Return,
			1));

		var caller = new M68kMachineFunction(
			callerMethod.DisplayName,
			0,
			callerMethod);
		var callerBlock = AddBlock(caller, 0, 0);
		var value = CreateReference(caller);
		var captureDestination = CreateReference(caller);
		var scalar = CreateLong(caller);
		AddArgument(caller, callerBlock, value.Id, 0);
		AddArgument(caller, callerBlock, captureDestination.Id, 1);
		AddArgument(caller, callerBlock, scalar.Id, 2);
		AddSummaryCall(caller, callerBlock, callSources[0], readMethod, [value.Id]);
		AddSummaryCall(
			caller,
			callerBlock,
			callSources[1],
			writeMethod,
			[value.Id, scalar.Id]);
		AddSummaryCall(
			caller,
			callerBlock,
			callSources[2],
			captureMethod,
			[captureDestination.Id, value.Id]);
		callerBlock.Instructions.Add(caller.CreateInstruction(
			M68kMachineOperation.Return,
			3));

		using var module = new CompilationModule(
			typeof(M68kMachineOptimizerTests).Assembly.Location);
		var summaries = M68kMethodMemorySummaryAnalyzer.Compute(
			[readMethod, writeMethod, captureMethod, callerMethod],
			new Dictionary<CilMethodIdentity, M68kMachineFunction>
			{
				[readMethod.Identity] = read,
				[writeMethod.Identity] = write,
				[captureMethod.Identity] = capture,
				[callerMethod.Identity] = caller
			},
			module);

		Assert.Equal(
			M68kParameterMemoryEffect.Read |
			M68kParameterMemoryEffect.Write |
			M68kParameterMemoryEffect.Capture,
			summaries[callerMethod.Identity].EffectForParameter(0));
		Assert.Equal(
			M68kParameterMemoryEffect.Write,
			summaries[callerMethod.Identity].EffectForParameter(1));
		Assert.Equal(
			M68kParameterMemoryEffect.Capture,
			summaries[callerMethod.Identity].EffectForParameter(2));
	}

	[Fact]
	public void LicmHoistsPureInvariantFromNaturalLoop()
	{
		var function = new M68kMachineFunction("licm", 0);
		var preheader = AddBlock(function, 0, 0);
		var header = AddBlock(function, 1, 10);
		var body = AddBlock(function, 2, 20);
		var exit = AddBlock(function, 3, 30);
		Connect(preheader, header);
		Connect(header, body);
		Connect(header, exit);
		Connect(body, header);
		var left = CreateLong(function);
		var right = CreateLong(function);
		var condition = CreateLong(function);
		var invariant = CreateLong(function);
		foreach (var (value, argument) in new[]
			{
				(left, 0),
				(right, 1),
				(condition, 2)
			})
		{
			preheader.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Argument,
				0,
				definitions: [value.Id],
				argumentIndex: argument));
		}
		preheader.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			1));
		header.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			10,
			uses: [condition.Id],
			sourceInstruction: new CilInstruction(10, OpCodes.Brtrue, 20, 11)));
		body.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			20,
			uses: [left.Id, right.Id],
			definitions: [invariant.Id],
			sourceInstruction: new CilInstruction(20, OpCodes.Add, null, 21)));
		body.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.LocalStore,
			21,
			uses: [invariant.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			argumentIndex: 0));
		body.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			22));
		exit.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			30));

		var statistics = M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Equal(1, statistics.LoopInstructionsHoisted);
		Assert.Contains(preheader.Instructions, instruction =>
			instruction.Definitions.Contains(invariant.Id));
		Assert.DoesNotContain(body.Instructions, instruction =>
			instruction.Definitions.Contains(invariant.Id));
	}

	private static void AddConstant(
		M68kMachineFunction function,
		M68kMachineBlock block,
		int definition,
		int value,
		int offset) =>
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			offset,
			definitions: [definition],
			sourceInstruction: new CilInstruction(
				offset,
				OpCodes.Ldc_I4,
				value,
				offset + 1),
			constantValue: M68kMachineConstant.Int32(value)));

	private static M68kMachineValue CreateLong(M68kMachineFunction function) =>
		function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Data);

	private static M68kMachineValue CreateReference(M68kMachineFunction function) =>
		function.CreateValue(
			CilStackValueKind.Reference,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress,
			isGcReference: true);

	private static M68kMemoryObject SummaryField(
		int owner,
		string identity,
		bool isRoot = false) =>
		new(
			M68kMemoryObjectKind.ObjectField,
			$"summary:{identity}",
			isRoot,
			owner,
			Offset: 8,
			Size: 4);

	private static void AddArgument(
		M68kMachineFunction function,
		M68kMachineBlock block,
		int definition,
		int index) =>
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument,
			0,
			definitions: [definition],
			argumentIndex: index));

	private static void AddSummaryCall(
		M68kMachineFunction function,
		M68kMachineBlock block,
		CilInstruction source,
		CilMethod target,
		IReadOnlyList<int> arguments) =>
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			source.Offset,
			uses: arguments,
			memoryEffect: M68kMachineMemoryEffect.Read |
				M68kMachineMemoryEffect.Write,
			isSafepoint: true,
			mayThrow: true,
			sourceInstruction: source,
			logicalCall: new M68kMachineLogicalCall(
				M68kMachineCallDispatchKind.Direct,
				[target.Identity],
				arguments.ToImmutableArray(),
				[],
				false,
				function.OriginAt(source.Offset, source)!)));

	private static M68kMemoryObject FrameMemory(int index, int size) =>
		new(
			M68kMemoryObjectKind.FrameSlot,
			index.ToString(System.Globalization.CultureInfo.InvariantCulture),
			Offset: 0,
			Size: size);

	private static M68kMachineInstruction FrameStore(
		M68kMachineFunction function,
		M68kMemoryObject memory,
		int value,
		int offset,
		M68kMachineMemoryEffect memoryEffect = M68kMachineMemoryEffect.Write,
		int localIndex = 0) =>
		function.CreateInstruction(
			M68kMachineOperation.LocalStore,
			offset,
			uses: [value],
			memoryEffect: memoryEffect,
			argumentIndex: localIndex,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					memory,
					M68kExactMemoryAccessKind.Write,
					value)
			]);

	private static M68kMachineInstruction FrameLoad(
		M68kMachineFunction function,
		M68kMemoryObject memory,
		int value,
		int offset,
		CilInstruction? sourceInstruction = null,
		M68kMachineMemoryEffect memoryEffect = M68kMachineMemoryEffect.Read,
		int localIndex = 0) =>
		function.CreateInstruction(
			M68kMachineOperation.LocalLoad,
			offset,
			definitions: [value],
			memoryEffect: memoryEffect,
			sourceInstruction: sourceInstruction,
			argumentIndex: localIndex,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					memory,
					M68kExactMemoryAccessKind.Read,
					value)
			]);

	private static M68kMemoryPromotionStatistics RunPromotion(
		M68kMachineFunction function,
		IReadOnlyDictionary<int, M68kHeapOwnerFacts>? owners = null) =>
		M68kMemoryPromotionPass.Run(
			function,
			new M68kMemoryPromotionContext(
				null!,
				null!,
				new Dictionary<CilMethodIdentity, M68kMethodMemorySummary>(),
				new HashSet<M68kMemoryObject>(),
				owners ?? new Dictionary<int, M68kHeapOwnerFacts>(),
				function));

	private static M68kMachineBlock AddBlock(
		M68kMachineFunction function,
		int id,
		int ilOffset)
	{
		var block = new M68kMachineBlock(id, ilOffset);
		function.Blocks.Add(block);
		return block;
	}

	private static void Connect(M68kMachineBlock from, M68kMachineBlock to)
	{
		from.Successors.Add(to.Id);
		to.Predecessors.Add(from.Id);
	}

	private static CilMethod CreateMethod(
		string moduleName,
		string displayName,
		IReadOnlyList<CilInstruction> instructions,
		int methodRow = 1,
		MethodImplAttributes implAttributes = 0) =>
		new(
			MetadataTokens.MethodDefinitionHandle(methodRow),
			default,
			displayName,
			displayName.Split("::")[^1],
			default,
			[],
			instructions,
			[],
			false,
			null,
			null,
			null,
			moduleName,
			ImplAttributes: implAttributes);
}
