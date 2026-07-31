using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kRegisterAllocationTests
{
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

	private static void Connect(M68kMachineBlock from, M68kMachineBlock to)
	{
		from.Successors.Add(to.Id);
		to.Predecessors.Add(from.Id);
	}
}
