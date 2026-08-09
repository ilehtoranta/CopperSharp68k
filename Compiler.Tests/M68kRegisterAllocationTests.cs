using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kRegisterAllocationTests
{
	[Fact]
	public void LoopFootprintDetectsInstructionCacheIndexConflicts()
	{
		var layouts = new[]
		{
			new M68kLoopLayout(
				"cache-conflict",
				12,
				"header",
				new[]
				{
					new M68kLoopBlockLayout("header", "first-end"),
					new M68kLoopBlockLayout("second", "second-end")
				})
		};
		var labels = new Dictionary<string, int>
		{
			["header"] = 0,
			["second"] = 256
		};
		var anchors = new Dictionary<string, int>
		{
			["first-end"] = 128,
			["second-end"] = 384
		};

		var footprint = Assert.Single(
			M68kLoopFootprintAnalysis.Measure(layouts, labels, anchors, 0x1000));

		Assert.Equal(0x1000u, footprint.HeaderAddress);
		Assert.Equal(256, footprint.InstructionBytes);
		Assert.Equal(384, footprint.SpanBytes);
		Assert.Equal(64, footprint.CacheLineCount);
		Assert.False(footprint.FitsIn256ByteInstructionCache);
	}

	[Fact]
	public void BlockLayoutUsesOriginalFalseSuccessorAsDiamondFallthrough()
	{
		var function = new M68kMachineFunction("layout-diamond", 0);
		var entry = AddBlock(function, 0, 0);
		var taken = AddBlock(function, 1, 10);
		var fallthrough = AddBlock(function, 2, 20);
		var join = AddBlock(function, 3, 30);
		Connect(entry, taken);
		Connect(entry, fallthrough);
		Connect(taken, join);
		Connect(fallthrough, join);

		var layout = CreateIdentityLayout(function);

		Assert.Equal(entry.Id, layout.BlockIds[0]);
		Assert.Equal(fallthrough.Id, layout.BlockIds[1]);
		Assert.Equal(layout.BlockIds, CreateIdentityLayout(function).BlockIds);
	}

	[Fact]
	public void BlockLayoutKeepsLoopBodyAheadOfExit()
	{
		var function = new M68kMachineFunction("layout-loop", 0);
		var entry = AddBlock(function, 0, 0);
		var header = AddBlock(function, 1, 10);
		var exit = AddBlock(function, 2, 20);
		var body = AddBlock(function, 3, 30);
		Connect(entry, header);
		Connect(header, exit);
		Connect(header, body);
		Connect(body, header);
		M68kControlFlowAnalysis.ComputeLoopDepths(function);

		var layout = CreateIdentityLayout(function);
		var order = layout.BlockIds.ToList();

		Assert.True(
			order.IndexOf(body.Id) < order.IndexOf(exit.Id));
	}

	[Fact]
	public void BlockLayoutSinksTransitiveColdFailureChain()
	{
		var function = new M68kMachineFunction("layout-cold-chain", 0);
		var entry = AddBlock(function, 0, 0);
		var hot = AddBlock(function, 1, 10);
		var coldForwarder = AddBlock(function, 2, 20);
		var coldThrow = AddBlock(function, 3, 30);
		Connect(entry, hot);
		Connect(entry, coldForwarder);
		Connect(coldForwarder, coldThrow);
		coldForwarder.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			20));
		coldThrow.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Throw,
			30));

		var layout = CreateIdentityLayout(function);

		Assert.Equal(entry.Id, layout.BlockIds[0]);
		Assert.Equal(hot.Id, layout.BlockIds[1]);
		Assert.True(
			layout.BlockIds.ToList().IndexOf(coldForwarder.Id) >
			layout.BlockIds.ToList().IndexOf(hot.Id));
	}

	[Fact]
	public void BlockLayoutPlacesConditionalHeaderBeforeReciprocalLoopBody()
	{
		var function = new M68kMachineFunction("layout-loop-fallthrough", 0);
		var entry = AddBlock(function, 0, 0);
		var header = AddBlock(function, 1, 10);
		var body = AddBlock(function, 2, 20);
		var exit = AddBlock(function, 3, 30);
		Connect(entry, header);
		Connect(header, body);
		Connect(header, exit);
		Connect(body, header);
		M68kControlFlowAnalysis.ComputeLoopDepths(function);

		var layout = CreateIdentityLayout(function);
		var order = layout.BlockIds.ToList();

		var headerIndex = order.IndexOf(header.Id);
		Assert.True(headerIndex < order.IndexOf(body.Id));
		Assert.Contains(order[headerIndex + 1], new[] { body.Id, exit.Id });
	}

	[Fact]
	public void BlockLayoutPlacesSelectedCriticalEdgeBlockAfterConditional()
	{
		var function = new M68kMachineFunction("layout-critical-edge", 0);
		var entry = AddBlock(function, 0, 0);
		var alternate = AddBlock(function, 1, 10);
		var join = AddBlock(function, 2, 20);
		var split = AddBlock(function, 3, 20);
		Connect(entry, alternate);
		Connect(entry, split);
		Connect(alternate, join);
		Connect(split, join);
		split.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			20));

		var layout = CreateIdentityLayout(function);
		var order = layout.BlockIds.ToList();

		var entryIndex = order.IndexOf(entry.Id);
		Assert.Equal(split.Id, layout.BlockIds[entryIndex + 1]);
	}

	[Fact]
	public void BlockLayoutCanSelectTrueEdgeBlockAsFallthrough()
	{
		var function = new M68kMachineFunction("layout-true-edge", 0);
		var entry = AddBlock(function, 0, 0);
		var falseBlock = AddBlock(function, 1, 10);
		var join = AddBlock(function, 2, 20);
		var trueEdge = AddBlock(function, 3, 20);
		Connect(entry, trueEdge);
		Connect(entry, falseBlock);
		Connect(trueEdge, join);
		trueEdge.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			20));

		var layout = CreateIdentityLayout(function);

		Assert.Equal([entry.Id, trueEdge.Id], layout.BlockIds.Take(2));
	}

	[Fact]
	public void BlockLayoutKeepsEntryAndExceptionBlocksAsChainRoots()
	{
		var function = new M68kMachineFunction("layout-roots", 0);
		var entry = AddBlock(function, 0, 0);
		var normal = AddBlock(function, 1, 10);
		var handler = AddBlock(function, 2, 20);
		handler.IsExceptionEntry = true;
		Connect(entry, normal);
		Connect(normal, handler);

		var layout = CreateIdentityLayout(function);

		Assert.Equal(entry.Id, layout.BlockIds[0]);
		Assert.Equal(handler.Id, layout.BlockIds[^1]);
	}

	[Fact]
	public void FinalDestinationsCollapseForwardingChainsAndPreserveAliases()
	{
		var function = new M68kMachineFunction("final-destinations-chain", 0);
		var entry = AddBlock(function, 0, 0);
		var first = AddBlock(function, 1, 10);
		var second = AddBlock(function, 2, 20);
		var merge = AddBlock(function, 3, 30);
		var target = AddBlock(function, 4, 40);
		Connect(entry, first);
		Connect(entry, second);
		Connect(first, merge);
		Connect(second, merge);
		Connect(merge, target);
		AddPlainBranch(function, first);
		AddPlainBranch(function, second);
		AddPlainBranch(function, merge);

		var plan = M68kFinalDestinationPlan.Create(
			function,
			CreateEmptyParallelCopies());

		Assert.Equal(target.Id, plan.Resolve(first.Id));
		Assert.Equal(target.Id, plan.Resolve(second.Id));
		Assert.Equal(target.Id, plan.Resolve(merge.Id));
		Assert.Equal([entry.Id, target.Id], plan.EmittedBlockIds);
		Assert.Equal([first.Id, second.Id, merge.Id], plan.AliasesByDestination[target.Id]);
		var repeated = M68kFinalDestinationPlan.Create(
			function,
			CreateEmptyParallelCopies());
		Assert.Equal(
			plan.FinalDestinationByBlock.OrderBy(static item => item.Key),
			repeated.FinalDestinationByBlock.OrderBy(static item => item.Key));
	}

	[Fact]
	public void FinalDestinationsRetainCyclesAndCollapseTheirAcyclicPrefix()
	{
		var function = new M68kMachineFunction("final-destinations-cycle", 0);
		var entry = AddBlock(function, 0, 0);
		var prefix = AddBlock(function, 1, 10);
		var firstCycle = AddBlock(function, 2, 20);
		var secondCycle = AddBlock(function, 3, 30);
		Connect(entry, prefix);
		Connect(prefix, firstCycle);
		Connect(firstCycle, secondCycle);
		Connect(secondCycle, firstCycle);
		AddPlainBranch(function, prefix);
		AddPlainBranch(function, firstCycle);
		AddPlainBranch(function, secondCycle);

		var plan = M68kFinalDestinationPlan.Create(
			function,
			CreateEmptyParallelCopies());

		Assert.Equal(firstCycle.Id, plan.Resolve(prefix.Id));
		Assert.Equal(firstCycle.Id, plan.Resolve(firstCycle.Id));
		Assert.Equal(secondCycle.Id, plan.Resolve(secondCycle.Id));
		Assert.Equal([entry.Id, firstCycle.Id, secondCycle.Id], plan.EmittedBlockIds);
		Assert.Equal([prefix.Id], plan.AliasesByDestination[firstCycle.Id]);
	}

	[Fact]
	public void FinalDestinationsRetainSemanticForwarders()
	{
		var function = new M68kMachineFunction("final-destinations-effects", 0);
		var entry = AddBlock(function, 0, 0);
		var exceptionEntry = AddBlock(function, 1, 10);
		var phiBlock = AddBlock(function, 2, 20);
		var copiedEdge = AddBlock(function, 3, 30);
		var leave = AddBlock(function, 4, 40);
		var effectful = AddBlock(function, 5, 50);
		var target = AddBlock(function, 6, 60);
		exceptionEntry.IsExceptionEntry = true;
		foreach (var block in new[]
			{
				exceptionEntry,
				phiBlock,
				copiedEdge,
				leave,
				effectful
			})
		{
			Connect(entry, block);
			Connect(block, target);
		}
		var source = CreateLong(function);
		var result = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		phiBlock.Phis.Add(new M68kMachinePhi(
			result.Id,
			new Dictionary<int, int> { [entry.Id] = source.Id }));
		AddPlainBranch(function, exceptionEntry);
		AddPlainBranch(function, phiBlock);
		AddPlainBranch(function, copiedEdge);
		leave.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			leave.StartIlOffset,
			sourceInstruction: new CilInstruction(
				leave.StartIlOffset,
				OpCodes.Leave,
				target.StartIlOffset,
				leave.StartIlOffset + 5)));
		effectful.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			effectful.StartIlOffset,
			memoryEffect: M68kMachineMemoryEffect.Write));
		var edgeCopies = new Dictionary<
			(int From, int To),
			IReadOnlyList<M68kParallelCopy>>
		{
			[(entry.Id, copiedEdge.Id)] =
			[
				new M68kParallelCopy(
					M68kStorageLocation.Register(M68kRegister.D0),
					M68kStorageLocation.Register(M68kRegister.D1))
			]
		};

		var plan = M68kFinalDestinationPlan.Create(
			function,
			new M68kParallelCopyPlan(edgeCopies, NeedsTemporarySlot: false));

		foreach (var block in new[]
			{
				entry,
				exceptionEntry,
				phiBlock,
				copiedEdge,
				leave,
				effectful
			})
		{
			Assert.Equal(block.Id, plan.Resolve(block.Id));
		}
	}

	[Fact]
	public void BlockLayoutUsesFinalDestinationsAndOmitsForwarders()
	{
		var function = new M68kMachineFunction("layout-final-destinations", 0);
		var entry = AddBlock(function, 0, 0);
		var forwarding = AddBlock(function, 1, 10);
		var target = AddBlock(function, 2, 20);
		Connect(entry, forwarding);
		Connect(forwarding, target);
		AddPlainBranch(function, forwarding);
		var finalDestinations = M68kFinalDestinationPlan.Create(
			function,
			CreateEmptyParallelCopies());

		var layout = M68kBlockLayoutPlan.Create(function, finalDestinations);

		Assert.Equal([entry.Id, target.Id], layout.BlockIds);
	}

	[Fact]
	public void VerifierRejectsByteValueInAddressRegisterClass()
	{
		var function = new M68kMachineFunction("invalid-byte", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var value = function.CreateValue(
			CilStackValueKind.UnsignedByte,
			M68kMachineValueWidth.Byte,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [value.Id]));

		var exception = Assert.Throws<InvalidOperationException>(
			() => M68kMachineIrVerifier.Verify(function));

		Assert.Contains("allows an address register", exception.Message);
	}

	[Fact]
	public void LivenessUsesPhiInputOnlyOnItsIncomingEdge()
	{
		var function = new M68kMachineFunction("diamond", 0);
		var entry = AddBlock(function, 0, 0);
		var left = AddBlock(function, 1, 10);
		var right = AddBlock(function, 2, 20);
		var join = AddBlock(function, 3, 30);
		Connect(entry, left);
		Connect(entry, right);
		Connect(left, join);
		Connect(right, join);

		var condition = CreateLong(function);
		var leftValue = CreateLong(function);
		var rightValue = CreateLong(function);
		var result = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [condition.Id]));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			1,
			uses: [condition.Id]));
		left.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			10,
			definitions: [leftValue.Id]));
		right.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			20,
			definitions: [rightValue.Id]));
		join.Phis.Add(new M68kMachinePhi(
			result.Id,
			new Dictionary<int, int>
			{
				[left.Id] = leftValue.Id,
				[right.Id] = rightValue.Id
			}));
		join.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			30,
			uses: [result.Id]));

		var liveness = M68kLivenessAnalysis.Analyze(function);

		Assert.Contains(leftValue.Id, liveness.LiveOut[left.Id]);
		Assert.DoesNotContain(rightValue.Id, liveness.LiveOut[left.Id]);
		Assert.Contains(rightValue.Id, liveness.LiveOut[right.Id]);
		Assert.DoesNotContain(leftValue.Id, liveness.LiveOut[right.Id]);
		Assert.Empty(liveness.LiveIn[join.Id]);
	}

	[Fact]
	public void AllocatorSeparatesInterferingValues()
	{
		var function = new M68kMachineFunction("interference", 0);
		var block = AddBlock(function, 0, 0);
		var left = CreateLong(function);
		var right = CreateLong(function);
		var result = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [left.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			1,
			definitions: [right.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			2,
			uses: [left.Id, right.Id],
			definitions: [result.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [result.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.NotEqual(
			allocation.Registers[left.Id].Register,
			allocation.Registers[right.Id].Register);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AllocatorCoalescesNonInterferingCopies()
	{
		var function = new M68kMachineFunction("copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = CreateLong(function);
		var copy = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [copy.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			2,
			uses: [copy.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(
			allocation.Registers[source.Id],
			allocation.Registers[copy.Id]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AllocationVerifierRejectsClobberedPendingOutgoingArgument()
	{
		var function = new M68kMachineFunction("pending-outgoing-argument", 0);
		var block = AddBlock(function, 0, 0);
		var terminal = CreateLong(function);
		var lateAddress = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [terminal.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Address,
			1,
			definitions: [lateAddress.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.OutgoingArgumentPush,
			2,
			uses: [terminal.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			argumentIndex: 4));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.OutgoingArgumentPush,
			3,
			uses: [lateAddress.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			argumentIndex: 4));

		var liveness = M68kLivenessAnalysis.Analyze(function);
		var graph = M68kInterferenceBuilder.Build(function, liveness);
		// Simulate an independently corrupted interference graph. The verifier
		// must derive simultaneous liveness from the instruction stream itself.
		graph.AddCoalescableCopy(terminal.Id, lateAddress.Id);
		graph.FinalizeCoalescableCopies();
		var allocation = new M68kAllocationResult(
			new Dictionary<int, M68kAllocatedLocation>
			{
				[terminal.Id] = new(M68kRegister.D0, IsPair: false),
				[lateAddress.Id] = new(M68kRegister.D0, IsPair: false)
			},
			new HashSet<int>());

		var exception = Assert.Throws<InvalidOperationException>(() =>
			M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation));
		Assert.Contains("simultaneously live", exception.Message, StringComparison.Ordinal);
	}

	[Fact]
	public void AddressConstraintCoalescesAnExistingAddressValue()
	{
		var function = new M68kMachineFunction("address-copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		var constrained = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [constrained.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			2,
			uses: [constrained.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(allocation.Registers[source.Id], allocation.Registers[constrained.Id]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AddressConstraintCoalescesAFlexiblePointerIntoItsAddressRegister()
	{
		var function = new M68kMachineFunction("flexible-address-copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress);
		var constrained = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [constrained.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			2,
			uses: [constrained.Id, source.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.InRange(
			allocation.Registers[source.Id].Register,
			M68kRegister.A0,
			M68kRegister.A6);
		Assert.Equal(
			allocation.Registers[source.Id],
			allocation.Registers[constrained.Id]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AddressConstraintPropagatesThroughFlexibleCopyChain()
	{
		var function = new M68kMachineFunction("transitive-address-copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress);
		var localLoad = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress);
		var constrained = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [localLoad.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			2,
			uses: [localLoad.Id],
			definitions: [constrained.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			3,
			uses: [constrained.Id, localLoad.Id, source.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.InRange(
			allocation.Registers[source.Id].Register,
			M68kRegister.A0,
			M68kRegister.A6);
		Assert.Equal(
			allocation.Registers[source.Id],
			allocation.Registers[localLoad.Id]);
		Assert.Equal(
			allocation.Registers[source.Id],
			allocation.Registers[constrained.Id]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AddressConstraintMovesADataRegisterPointerToAnAddressRegister()
	{
		var function = new M68kMachineFunction("data-address-copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D2),
			precoloredRegister: M68kRegister.D2);
		var constrained = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [constrained.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			2,
			uses: [constrained.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(M68kRegister.D2, allocation.Registers[source.Id].Register);
		Assert.InRange(
			allocation.Registers[constrained.Id].Register,
			M68kRegister.A0,
			M68kRegister.A6);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void NonCoalescingCopySeparatesALiveSourceButReusesADeadSource()
	{
		static (M68kAllocationResult Allocation, M68kInterferenceGraph Graph, int Source, int Copy)
			Build(bool sourceRemainsLive)
		{
			var function = new M68kMachineFunction("destructive-copy", 0);
			var block = AddBlock(function, 0, 0);
			var source = CreateLong(function);
			var copy = CreateLong(function);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Constant,
				0,
				definitions: [source.Id]));
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy,
				1,
				uses: [source.Id],
				definitions: [copy.Id],
				allowCopyCoalescing: false));
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Other,
				2,
				uses: sourceRemainsLive ? [copy.Id, source.Id] : [copy.Id]));
			var allocation = Allocate(function, out var graph);
			return (allocation, graph, source.Id, copy.Id);
		}

		var live = Build(sourceRemainsLive: true);
		var dead = Build(sourceRemainsLive: false);

		Assert.NotEqual(
			live.Allocation.Registers[live.Source],
			live.Allocation.Registers[live.Copy]);
		Assert.Equal(
			dead.Allocation.Registers[dead.Source],
			dead.Allocation.Registers[dead.Copy]);
	}

	[Fact]
	public void CopyIntoFixedAbiRegisterCoalescesWhenSafe()
	{
		var function = new M68kMachineFunction("fixed-copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = CreateLong(function);
		var fixedArgument = function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D0),
			precoloredRegister: M68kRegister.D0);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [fixedArgument.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			2,
			uses: [fixedArgument.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(M68kRegister.D0, allocation.Registers[source.Id].Register);
		Assert.Equal(
			allocation.Registers[source.Id],
			allocation.Registers[fixedArgument.Id]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void TransparentRawCopyCoalescesIntoFixedDataAbiRegister()
	{
		var function = new M68kMachineFunction("transparent-fixed-copy", 0);
		var block = AddBlock(function, 0, 0);
		var source = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress);
		var raw = function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Data);
		var fixedArgument = function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D1),
			precoloredRegister: M68kRegister.D1);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [raw.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			2,
			uses: [raw.Id],
			definitions: [fixedArgument.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			3,
			uses: [fixedArgument.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(M68kRegister.D1, allocation.Registers[source.Id].Register);
		Assert.Equal(allocation.Registers[source.Id], allocation.Registers[raw.Id]);
		Assert.Equal(
			allocation.Registers[source.Id],
			allocation.Registers[fixedArgument.Id]);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void FlagDeadLongCounterAdmitsAddressRegisterAllocation()
	{
		var function = new M68kMachineFunction("address-counter", 0);
		var block = AddBlock(function, 0, 0);
		var incoming = function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.D0),
			precoloredRegister: M68kRegister.D0);
		var counter = CreateLong(function);
		var one = CreateLong(function);
		var updated = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			0,
			uses: [incoming.Id],
			definitions: [counter.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			1,
			definitions: [one.Id],
			sourceInstruction: new CilInstruction(1, OpCodes.Ldc_I4_1, null, 2)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			2,
			uses: [counter.Id, one.Id],
			definitions: [updated.Id],
			sourceInstruction: new CilInstruction(2, OpCodes.Add, null, 3)));

		M68kAddressRegisterEligibility.Apply(function);

		Assert.Equal(M68kRegisterSet.DataOrAddress, function.Values[counter.Id].AllowedRegisters);
		Assert.Equal(M68kRegisterSet.DataOrAddress, function.Values[updated.Id].AllowedRegisters);
		Assert.Equal(M68kRegisterSet.Data, function.Values[one.Id].AllowedRegisters);
	}

	[Fact]
	public void CheckedLongCounterRemainsInDataRegistersForOverflowFlags()
	{
		var function = new M68kMachineFunction("checked-address-counter", 0);
		var block = AddBlock(function, 0, 0);
		var counter = CreateLong(function);
		var one = CreateLong(function);
		var updated = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			0,
			uses: [counter.Id, one.Id],
			definitions: [updated.Id],
			sourceInstruction: new CilInstruction(0, OpCodes.Add_Ovf, null, 1)));

		M68kAddressRegisterEligibility.Apply(function);

		Assert.Equal(M68kRegisterSet.Data, function.Values[counter.Id].AllowedRegisters);
		Assert.Equal(M68kRegisterSet.Data, function.Values[updated.Id].AllowedRegisters);
	}

	[Fact]
	public void FixedA5CallTemporaryDoesNotAllocateOrdinaryValueToRuntimeFrameRegister()
	{
		var function = new M68kMachineFunction("fixed-a5", 0)
		{
			ReservedRegisters = M68kRegisterSet.From(M68kRegister.A5)
		};
		var block = AddBlock(function, 0, 0);
		var source = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Address);
		var fixedArgument = function.CreateValue(
			CilStackValueKind.ManagedPointer,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.A5),
			precoloredRegister: M68kRegister.A5);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [source.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy,
			1,
			uses: [source.Id],
			definitions: [fixedArgument.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			2,
			uses: [fixedArgument.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(M68kRegister.A5, allocation.Registers[fixedArgument.Id].Register);
		Assert.NotEqual(M68kRegister.A5, allocation.Registers[source.Id].Register);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void ValueLiveAcrossCallAvoidsCallerSavedRegisters()
	{
		var function = new M68kMachineFunction("call-clobber", 0);
		var block = AddBlock(function, 0, 0);
		var value = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [value.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			1,
			clobbers: M68kRegisterSet.From(
				M68kRegister.D0,
				M68kRegister.D1,
				M68kRegister.A0,
				M68kRegister.A1)));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			2,
			uses: [value.Id]));

		var allocation = Allocate(function, out var graph);

		Assert.Equal(M68kRegister.D2, allocation.Registers[value.Id].Register);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void RegisterPairOccupiesBothDataRegisters()
	{
		var function = new M68kMachineFunction("pair", 0);
		var block = AddBlock(function, 0, 0);
		var pair = function.CreateValue(
			CilStackValueKind.Int64,
			M68kMachineValueWidth.LongPair,
			M68kRegisterSet.DataPairStarts);
		var scalar = CreateLong(function);
		var result = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [pair.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			1,
			definitions: [scalar.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Add,
			2,
			uses: [pair.Id, scalar.Id],
			definitions: [result.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [result.Id]));

		var allocation = Allocate(function, out var graph);
		var pairLocation = allocation.Registers[pair.Id];
		var scalarLocation = allocation.Registers[scalar.Id];

		Assert.False(
			pairLocation.OccupiedRegisters.Overlaps(
				scalarLocation.OccupiedRegisters));
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AllocatorSpillsWhenDataRegisterPressureExceedsEight()
	{
		var function = new M68kMachineFunction("pressure", 0);
		var block = AddBlock(function, 0, 0);
		var values = Enumerable.Range(0, 9)
			.Select(_ => CreateLong(function))
			.ToArray();
		foreach (var value in values)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Constant,
				block.Instructions.Count,
				definitions: [value.Id]));
		}
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			20,
			uses: values.Select(static value => value.Id)));

		var allocation = Allocate(function, out var graph);

		Assert.Single(allocation.SpilledValues);
		M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation);
	}

	[Fact]
	public void AllocationPipelineRewritesSpillsAndConverges()
	{
		var function = new M68kMachineFunction("rewrite-pressure", 0);
		var block = AddBlock(function, 0, 0);
		var values = Enumerable.Range(0, 9)
			.Select(_ => CreateLong(function))
			.ToArray();
		foreach (var value in values)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Other,
				block.Instructions.Count,
				definitions: [value.Id]));
		}
		foreach (var value in values)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Other,
				block.Instructions.Count,
				uses: [value.Id]));
		}
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			block.Instructions.Count));

		var allocated = M68kRegisterAllocatorPipeline.Run(function);
		var instructions = function.Blocks
			.SelectMany(static item => item.Instructions)
			.ToArray();

		Assert.Empty(allocated.Allocation.SpilledValues);
		Assert.True(allocated.Statistics.AllocationIterations > 1);
		Assert.True(allocated.Statistics.SpilledValues > 0);
		Assert.Contains(
			instructions,
			static instruction =>
				instruction.Operation == M68kMachineOperation.SpillLoad);
		Assert.Contains(
			instructions,
			static instruction =>
				instruction.Operation == M68kMachineOperation.SpillStore);
		Assert.All(
			instructions.Where(static instruction =>
				instruction.Operation is
					M68kMachineOperation.SpillLoad or
					M68kMachineOperation.SpillStore),
			static instruction => Assert.NotNull(instruction.SpillSlotIndex));
		M68kMachineIrVerifier.Verify(function);
	}

	[Fact]
	public void AllocationPipelineRematerializesConstantsWithoutSpillStorage()
	{
		var function = new M68kMachineFunction("rematerialize-pressure", 0);
		var block = AddBlock(function, 0, 0);
		var values = Enumerable.Range(0, 9)
			.Select(_ => CreateLong(function))
			.ToArray();
		foreach (var value in values)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Constant,
				block.Instructions.Count,
				definitions: [value.Id]));
		}
		foreach (var value in values)
		{
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Other,
				block.Instructions.Count,
				uses: [value.Id]));
		}
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			block.Instructions.Count));

		var allocated = M68kRegisterAllocatorPipeline.Run(function);

		Assert.True(allocated.Statistics.RematerializedValues > 0);
		Assert.DoesNotContain(
			function.Blocks.SelectMany(static item => item.Instructions),
			static instruction =>
				instruction.Operation is
					M68kMachineOperation.SpillLoad or
					M68kMachineOperation.SpillStore);
		Assert.Equal(0, allocated.Spills.FrameBytes);
	}

	[Fact]
	public void SpilledPhiDefinitionSplitsCriticalIncomingEdge()
	{
		var function = new M68kMachineFunction("critical-phi", 0);
		var entry = AddBlock(function, 0, 0);
		var alternate = AddBlock(function, 1, 10);
		var join = AddBlock(function, 2, 20);
		Connect(entry, alternate);
		Connect(entry, join);
		Connect(alternate, join);
		var entryValue = CreateLong(function);
		var alternateValue = CreateLong(function);
		var result = CreateLong(function);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [entryValue.Id]));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.ConditionalBranch,
			1));
		alternate.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			10,
			definitions: [alternateValue.Id]));
		alternate.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			11));
		join.Phis.Add(new M68kMachinePhi(
			result.Id,
			new Dictionary<int, int>
			{
				[entry.Id] = entryValue.Id,
				[alternate.Id] = alternateValue.Id
			}));
		join.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			20,
			uses: [result.Id]));
		var layout = new M68kSpillLayout(
			new Dictionary<int, M68kSpillSlot>
			{
				[result.Id] = new(0, 0, 4, false)
			},
			new HashSet<int>(),
			4);

		M68kSpillRewriter.Rewrite(function, layout);

		var split = Assert.Single(function.Blocks.Where(static block =>
			block.Id > 2));
		Assert.Equal([entry.Id], split.Predecessors);
		Assert.Equal([join.Id], split.Successors);
		Assert.Contains(
			split.Instructions,
			static instruction =>
				instruction.Operation == M68kMachineOperation.SpillStore);
		Assert.Empty(join.Phis);
		Assert.Contains(split.Id, join.Predecessors);
		Assert.DoesNotContain(entry.Id, join.Predecessors);
		M68kMachineIrVerifier.Verify(function);
	}

	[Fact]
	public void SpillSlotsReuseStorageOnlyForNonInterferingValues()
	{
		var function = new M68kMachineFunction("spill-slots", 0);
		var block = AddBlock(function, 0, 0);
		var first = CreateLong(function);
		var second = CreateLong(function);
		var third = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			0,
			definitions: [first.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			1,
			uses: [first.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			2,
			definitions: [second.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Constant,
			3,
			definitions: [third.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			4,
			uses: [second.Id, third.Id]));
		var liveness = M68kLivenessAnalysis.Analyze(function);
		var graph = M68kInterferenceBuilder.Build(function, liveness);

		var layout = M68kSpillSlotAllocator.Allocate(
			function,
			graph,
			new HashSet<int> { first.Id, second.Id, third.Id });

		Assert.Equal(layout.Slots[first.Id], layout.Slots[second.Id]);
		Assert.NotEqual(layout.Slots[second.Id], layout.Slots[third.Id]);
	}

	[Fact]
	public void GcAndScalarSpillsNeverShareAFrameSlot()
	{
		var function = new M68kMachineFunction("gc-spills", 0);
		var block = AddBlock(function, 0, 0);
		var scalar = CreateLong(function);
		var reference = function.CreateValue(
			CilStackValueKind.Reference,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress,
			isGcReference: true);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [scalar.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			1,
			uses: [scalar.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			2,
			definitions: [reference.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			3,
			uses: [reference.Id]));
		var liveness = M68kLivenessAnalysis.Analyze(function);
		var graph = M68kInterferenceBuilder.Build(function, liveness);

		var layout = M68kSpillSlotAllocator.Allocate(
			function,
			graph,
			new HashSet<int> { scalar.Id, reference.Id });

		Assert.NotEqual(layout.Slots[scalar.Id], layout.Slots[reference.Id]);
		Assert.False(layout.Slots[scalar.Id].IsGcRoot);
		Assert.True(layout.Slots[reference.Id].IsGcRoot);
	}

	[Fact]
	public void ParallelCopyResolverBreaksRegisterCycles()
	{
		var d0 = M68kStorageLocation.Register(M68kRegister.D0);
		var d1 = M68kStorageLocation.Register(M68kRegister.D1);
		var temporary = M68kStorageLocation.Temporary();

		var resolved = M68kParallelCopyResolver.Resolve(
			[
				new M68kParallelCopy(d0, d1),
				new M68kParallelCopy(d1, d0)
			],
			temporary);

		Assert.Equal(
			[
				new M68kParallelCopy(temporary, d1),
				new M68kParallelCopy(d1, d0),
				new M68kParallelCopy(d0, temporary)
			],
			resolved);
	}

	[Fact]
	public void PhiCopyPlannerUsesFreeRegisterForSwapCycle()
	{
		var function = new M68kMachineFunction("phi-swap", 0);
		var predecessor = AddBlock(function, 0, 0);
		var target = AddBlock(function, 1, 10);
		Connect(predecessor, target);
		var sourceD0 = CreateFixedLong(function, M68kRegister.D0);
		var sourceD1 = CreateFixedLong(function, M68kRegister.D1);
		var resultD1 = CreateFixedLong(function, M68kRegister.D1);
		var resultD0 = CreateFixedLong(function, M68kRegister.D0);
		predecessor.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [sourceD0.Id]));
		predecessor.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			1,
			definitions: [sourceD1.Id]));
		predecessor.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			2));
		target.Phis.Add(new M68kMachinePhi(
			resultD1.Id,
			new Dictionary<int, int> { [predecessor.Id] = sourceD0.Id }));
		target.Phis.Add(new M68kMachinePhi(
			resultD0.Id,
			new Dictionary<int, int> { [predecessor.Id] = sourceD1.Id }));
		target.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			10,
			uses: [resultD0.Id, resultD1.Id]));
		var liveness = M68kLivenessAnalysis.Analyze(function);
		var graph = M68kInterferenceBuilder.Build(function, liveness);
		var allocation = M68kGraphColoringAllocator.Allocate(function, graph);

		var plan = M68kParallelCopyPlanner.Create(
			function,
			liveness,
			allocation);
		var copies = Assert.Single(plan.EdgeCopies).Value;

		Assert.Equal(3, copies.Count);
		Assert.False(plan.NeedsTemporarySlot);
		Assert.Contains(
			copies,
			static copy =>
				copy.Destination ==
					M68kStorageLocation.Register(M68kRegister.D2));
	}

	[Fact]
	public void SafepointRootsReuseSlotsWhenReferencesAreNeverLiveTogether()
	{
		var function = new M68kMachineFunction("root-reuse", 0);
		var block = AddBlock(function, 0, 0);
		var first = CreateReference(function);
		var second = CreateReference(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [first.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			1,
			uses: [first.Id],
			isSafepoint: true));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			2,
			definitions: [second.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			3,
			uses: [second.Id],
			isSafepoint: true));
		var blockLiveness = M68kLivenessAnalysis.Analyze(function);
		var instructionLiveness = M68kLivenessAnalysis.AnalyzeInstructions(
			function,
			blockLiveness);

		var plan = M68kSafepointPlanner.Create(function, instructionLiveness);

		Assert.Equal(
			plan.RootSlotByValue[first.Id],
			plan.RootSlotByValue[second.Id]);
		Assert.Equal(1, plan.RootSlotCount);
	}

	[Fact]
	public void SafepointRootsSeparateSimultaneouslyLiveReferences()
	{
		var function = new M68kMachineFunction("root-pressure", 0);
		var block = AddBlock(function, 0, 0);
		var first = CreateReference(function);
		var second = CreateReference(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [first.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			1,
			definitions: [second.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Call,
			2,
			uses: [first.Id, second.Id],
			isSafepoint: true));
		var blockLiveness = M68kLivenessAnalysis.Analyze(function);
		var instructionLiveness = M68kLivenessAnalysis.AnalyzeInstructions(
			function,
			blockLiveness);

		var plan = M68kSafepointPlanner.Create(function, instructionLiveness);

		Assert.NotEqual(
			plan.RootSlotByValue[first.Id],
			plan.RootSlotByValue[second.Id]);
		Assert.Equal(2, plan.RootSlotCount);
	}

	[Fact]
	public void RootSynchronizerStoresLiveRegisterReferenceAndClearsItAfterDeath()
	{
		var function = new M68kMachineFunction("root-sync", 0);
		var block = AddBlock(function, 0, 0);
		var reference = CreateReference(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [reference.Id]));
		var call = function.CreateInstruction(
			M68kMachineOperation.Call,
			1,
			uses: [reference.Id],
			isSafepoint: true);
		block.Instructions.Add(call);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			2));

		var allocated = M68kRegisterAllocatorPipeline.Run(function);
		var instructions = block.Instructions;
		var callIndex = instructions.FindIndex(item => item.Id == call.Id);

		Assert.Equal(
			M68kMachineOperation.RootStore,
			instructions[callIndex - 1].Operation);
		Assert.Equal(
			M68kMachineOperation.RootClear,
			instructions[callIndex + 1].Operation);
		Assert.Equal(
			instructions[callIndex - 1].SpillSlotIndex,
			instructions[callIndex + 1].SpillSlotIndex);
		Assert.Equal(1, allocated.Safepoints.RootSlotCount);
		Assert.Single(allocated.Frame.RootOffsets);
		Assert.Equal(4, allocated.Frame.FrameBytes);
	}

	[Fact]
	public void SafepointTracksGcReferenceHeldInSpillSlot()
	{
		var function = new M68kMachineFunction("spilled-root", 0);
		var block = AddBlock(function, 0, 0);
		var reference = CreateReference(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Other,
			0,
			definitions: [reference.Id]));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.SpillStore,
			1,
			uses: [reference.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			spillSlotIndex: 3));
		var call = function.CreateInstruction(
			M68kMachineOperation.Call,
			2,
			isSafepoint: true);
		block.Instructions.Add(call);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.SpillClear,
			3,
			memoryEffect: M68kMachineMemoryEffect.Write,
			spillSlotIndex: 3));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return,
			4));
		function.GcSpillSlots.Add(3);
		var blockLiveness = M68kLivenessAnalysis.Analyze(function);
		var instructionLiveness = M68kLivenessAnalysis.AnalyzeInstructions(
			function,
			blockLiveness);

		var plan = M68kSafepointPlanner.Create(
			function,
			instructionLiveness,
			firstRootSlot: 4);

		Assert.Equal(
			new HashSet<int> { 3 },
			Assert.Single(plan.Safepoints).LiveSpillRootSlots);
		Assert.Empty(plan.RootSlotByValue);
	}

	private static M68kAllocationResult Allocate(
		M68kMachineFunction function,
		out M68kInterferenceGraph graph)
	{
		var liveness = M68kLivenessAnalysis.Analyze(function);
		graph = M68kInterferenceBuilder.Build(function, liveness);
		return M68kGraphColoringAllocator.Allocate(function, graph);
	}

	private static M68kMachineValue CreateLong(M68kMachineFunction function) =>
		function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.Data);

	private static M68kMachineValue CreateReference(
		M68kMachineFunction function) =>
		function.CreateValue(
			CilStackValueKind.Reference,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress,
			isGcReference: true);

	private static M68kMachineValue CreateFixedLong(
		M68kMachineFunction function,
		M68kRegister register) =>
		function.CreateValue(
			CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(register),
			precoloredRegister: register);

	private static M68kMachineBlock AddBlock(
		M68kMachineFunction function,
		int id,
		int ilOffset)
	{
		var block = new M68kMachineBlock(id, ilOffset);
		function.Blocks.Add(block);
		return block;
	}

	private static void AddPlainBranch(
		M68kMachineFunction function,
		M68kMachineBlock block) =>
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch,
			block.StartIlOffset));

	private static M68kParallelCopyPlan CreateEmptyParallelCopies() =>
		new(
			new Dictionary<
				(int From, int To),
				IReadOnlyList<M68kParallelCopy>>(),
			NeedsTemporarySlot: false);

	private static M68kBlockLayoutPlan CreateIdentityLayout(
		M68kMachineFunction function)
	{
		var blockIds = function.Blocks.Select(static block => block.Id).ToArray();
		var finalDestinations = new M68kFinalDestinationPlan(
			blockIds.ToDictionary(static blockId => blockId),
			blockIds,
			new Dictionary<int, IReadOnlyList<int>>());
		return M68kBlockLayoutPlan.Create(function, finalDestinations);
	}

	private static void Connect(M68kMachineBlock from, M68kMachineBlock to)
	{
		from.Successors.Add(to.Id);
		to.Predecessors.Add(from.Id);
	}
}
