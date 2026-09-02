using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameInefficiencyRegressionTests
{
	[Fact]
	public void ExplicitAggregateInitializationOverwritesEveryPrivateLongword()
	{
		var (function, entry) = Function();
		function.LocalHomes.Add(0, new M68kFrameHome(0, 88, false));
		var address = Address(function);
		var copy = Address(function);
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalAddress,
			0, definitions: [address], argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy,
			1, uses: [address], definitions: [copy]));
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.AggregateIndirectInitialize, 2, uses: [copy], memorySize: 88));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call, 3,
			uses: [address], memoryEffect: M68kMachineMemoryEffect.Read));

		var overwritten = M68kFrameInitializationAnalysis.FindEntryOverwrites(
			function, instruction => instruction.MemorySize);

		Assert.Equal(Enumerable.Range(0, 22).Select(index => (0, index * 4)),
			overwritten.OrderBy(lane => lane.Offset));
	}

	[Theory]
	[InlineData((int)M68kMachineOperation.LocalLoad)]
	[InlineData((int)M68kMachineOperation.Call)]
	[InlineData((int)M68kMachineOperation.ConditionalBranch)]
	public void InitializationIsRetainedAfterAnObservationOrControlFlowBoundary(
		int boundary)
	{
		var (function, entry) = Function();
		function.LocalHomes.Add(0, new M68kFrameHome(0, 8, false));
		entry.Instructions.Add(function.CreateInstruction((M68kMachineOperation)boundary, 0, argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore,
			1, argumentIndex: 0, memorySize: 8));

		Assert.Empty(M68kFrameInitializationAnalysis.FindEntryOverwrites(
			function, instruction => instruction.MemorySize));
	}

	[Fact]
	public void SelfCopyCannotEliminateTheSourceInitialization()
	{
		var (function, entry) = Function();
		function.LocalHomes.Add(0, new M68kFrameHome(0, 8, false));
		var address = Address(function);
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalAddress,
			0, definitions: [address], argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore,
			1, uses: [address], argumentIndex: 0, memorySize: 8));

		Assert.Empty(M68kFrameInitializationAnalysis.FindEntryOverwrites(
			function, instruction => instruction.MemorySize));
	}

	[Fact]
	public void PartialStoresOnlyEliminateFullyOverwrittenLongwords()
	{
		var (function, entry) = Function();
		function.LocalHomes.Add(0, new M68kFrameHome(0, 12, false));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore,
			0, argumentIndex: 0, memorySize: 2));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore,
			1, argumentIndex: 0, memoryOffset: 4, memorySize: 4));

		Assert.Equal(new[] { (0, 4) }, M68kFrameInitializationAnalysis.FindEntryOverwrites(
			function, instruction => instruction.MemorySize));
	}

	[Theory]
	[InlineData(true, false, false)]
	[InlineData(false, true, false)]
	[InlineData(false, false, true)]
	public void GcEhAndVolatileInitializationRemain(bool gc, bool eh, bool isVolatile)
	{
		var (function, entry) = Function();
		function.HasExceptionHandlers = eh;
		function.LocalHomes.Add(0, new M68kFrameHome(0, 4, gc));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore,
			0, argumentIndex: 0, memorySize: 4,
			memoryEffect: isVolatile ? M68kMachineMemoryEffect.Volatile : M68kMachineMemoryEffect.Write));

		Assert.Empty(M68kFrameInitializationAnalysis.FindEntryOverwrites(
			function, instruction => instruction.MemorySize));
	}

	[Fact]
	public void ReadOnlyArgumentHomeBecomesOneIncomingSsaValueAcrossBlocks()
	{
		var (function, entry) = Function();
		var next = new M68kMachineBlock(1, 10);
		function.Blocks.Add(next);
		function.AddEdge(entry, next, M68kMachineEdgeKind.Normal);
		function.ArgumentHomes.Add(3, new M68kFrameHome(3, 4, false));
		foreach (var block in function.Blocks)
		{
			var loaded = Scalar(function);
			block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ArgumentLoad,
				block.StartIlOffset, definitions: [loaded], argumentIndex: 3,
				memoryEffect: M68kMachineMemoryEffect.Read));
		}

		M68kArgumentHomeOptimizer.Run(function);

		Assert.Empty(function.ArgumentHomes);
		var incoming = Assert.Single(entry.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Argument);
		Assert.Equal(3, incoming.ArgumentIndex);
		Assert.All(function.Blocks.SelectMany(block => block.Instructions).Where(instruction =>
			instruction.Operation != M68kMachineOperation.Argument), instruction =>
		{
			Assert.Equal(M68kMachineOperation.Copy, instruction.Operation);
			Assert.Equal(incoming.Definitions[0], Assert.Single(instruction.Uses));
			Assert.Equal(M68kMachineMemoryEffect.None, instruction.MemoryEffect);
		});
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RegisterHomePromotionUsesOneCanonicalEntryCopyEvenForALateRead(bool existingArgument)
	{
		var (function, entry) = Function();
		function.ArgumentHomes.Add(0, new M68kFrameHome(0, 4, false));
		int? priorCopy = null;
		if (existingArgument)
		{
			var incoming = function.CreateValue(CilStackValueKind.Int32,
				M68kMachineValueWidth.Long, M68kRegisterSet.From(M68kRegister.D1),
				precoloredRegister: M68kRegister.D1).Id;
			priorCopy = Scalar(function);
			entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument,
				0, definitions: [incoming], argumentIndex: 0));
			entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy,
				0, uses: [incoming], definitions: [priorCopy.Value]));
		}
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ArgumentLoad,
			77, definitions: [Scalar(function)], argumentIndex: 0));

		M68kArgumentHomeOptimizer.Run(function, [M68kRegister.D1]);

		var argument = Assert.Single(entry.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Argument);
		var input = argument.Definitions[0];
		Assert.Equal(M68kRegister.D1, function.Values[input].PrecoloredRegister);
		var copy = Assert.Single(entry.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Copy && instruction.Uses.Contains(input));
		Assert.Equal(entry.StartIlOffset, copy.IlOffset);
		var later = Assert.Single(entry.Instructions, instruction => instruction.IlOffset == 77);
		Assert.Equal(copy.Definitions[0], Assert.Single(later.Uses));
		if (priorCopy is not null) Assert.Equal(priorCopy.Value, copy.Definitions[0]);
		Assert.Empty(function.ArgumentHomes);
	}

	[Theory]
	[InlineData((int)M68kMachineOperation.ArgumentAddress)]
	[InlineData((int)M68kMachineOperation.ArgumentStore)]
	public void AddressExposedOrWrittenArgumentHomesStayAuthoritative(int operation)
	{
		var (function, entry) = Function();
		function.ArgumentHomes.Add(0, new M68kFrameHome(0, 4, false));
		entry.Instructions.Add(function.CreateInstruction((M68kMachineOperation)operation, 0, argumentIndex: 0));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ArgumentLoad,
			1, definitions: [Scalar(function)], argumentIndex: 0));

		M68kArgumentHomeOptimizer.Run(function);

		Assert.Single(function.ArgumentHomes);
		Assert.Equal(M68kMachineOperation.ArgumentLoad, entry.Instructions[1].Operation);
	}

	[Fact]
	public void ExactEscapesPreventPromotionEvenWithoutAnArgumentAddressInstruction()
	{
		var (function, entry) = Function();
		function.ArgumentHomes.Add(0, new M68kFrameHome(0, 4, false));
		entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call, 0,
			exactMemoryAccesses: [new M68kExactMemoryAccess(
				M68kMemoryModel.FrameObject(M68kMemoryObjectKind.ArgumentHome, 0),
				M68kExactMemoryAccessKind.Escape)]));

		M68kArgumentHomeOptimizer.Run(function);

		Assert.Single(function.ArgumentHomes);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void FrameStoreReloadStoreChainPreservesMemoryRegistersAndAllConditionCodes(
		M68kCpuTarget target, M68kCpuModel model)
	{
		var original = StoreReloadChain();
		var optimized = StoreReloadChain();
		optimized.OptimizeForCpu(target);
		var before = original.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		var after = optimized.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		Assert.Equal(18, before.Length);
		Assert.Equal(8, after.Length);
		foreach (var value in new uint[] { 0, 1, 0x7fff_ffff, 0x8000_0000, 0xffff_ffff })
		foreach (var flags in new ushort[] { 0, 0x1f })
		{
			Assert.Equal(ExecuteNative(before, model, value, flags),
				ExecuteNative(after, model, value, flags));
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, false)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, true)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, false)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, true)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, false)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, true)]
	public void HiddenIlLabelsAfterCallDoNotRetainRedundantFrameStores(
		M68kCpuTarget target, M68kCpuModel model, bool forwardedReload)
	{
		var original = LabeledStoreChain(forwardedReload);
		var optimized = LabeledStoreChain(forwardedReload);
		optimized.OptimizeForCpu(target);
		var before = original.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		var after = optimized.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		var stores = optimized.GetInstructionStream().Where(instruction =>
			instruction.Opcode is 0x2f42 or 0x2f48).ToArray();
		Assert.Single(stores);
		Assert.True(after.Length <= before.Length - (forwardedReload ? 8 : 10));
		foreach (var value in new uint[] { 0, 1, 0x7fff_ffff, 0x8000_0000, 0xffff_ffff })
		foreach (var flags in new ushort[] { 0, 0x1f })
			Assert.Equal(ExecuteNative(before, model, value, flags),
				ExecuteNative(after, model, value, flags));
	}

	[Theory]
	[InlineData("method:other")]
	[InlineData("method:frame:BB0010")]
	[InlineData("generated:unwind-site")]
	[InlineData("method:frame:IL_invalid")]
	public void IndependentEntryAndUnwindLabelsKeepDuplicateFrameStores(string label)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2f42);
		assembler.EmitWord(12);
		assembler.Mark(label);
		assembler.EmitWord(0x2f42);
		assembler.EmitWord(12);
		assembler.EmitWord(0x4e75);
		assembler.OptimizeForCpu(M68kCpuTarget.M68000);
		Assert.Equal(2, assembler.GetInstructionStream().Count(instruction => instruction.Opcode == 0x2f42));
	}

	[Fact]
	public void CodePointerAddendAcrossFrameTransfersKeepsOriginalDistances()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:frame");
		EmitStoreReloadChain(assembler);
		assembler.EmitWord(0x4e75);
		assembler.MarkDataStart();
		assembler.EmitAddress("method:frame", addend: 16); // The RTS after the entire chain.
		var original = assembler.Link(0, new Dictionary<string, uint>()).Bytes;
		assembler.OptimizeForCpu(M68kCpuTarget.M68000);
		Assert.Equal(original, assembler.Link(0, new Dictionary<string, uint>()).Bytes);
	}

	[Fact]
	public void DuplicateStoresToAnUnknownBaseAreNotCombined()
	{
		var assembler = new M68kAssembler();
		for (var index = 0; index < 2; index++)
		{
			assembler.EmitWord(0x2542); // MOVE.L D2,12(A2): may be MMIO.
			assembler.EmitWord(12);
		}
		assembler.EmitWord(0x4E75);
		assembler.OptimizeForCpu(M68kCpuTarget.M68000);

		Assert.Equal(2, assembler.GetInstructionStream().Count(instruction => instruction.Opcode == 0x2542));
	}

	[Fact]
	public void FrameTransferPatternsInTrailingDataAreUntouched()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E75);
		assembler.MarkDataStart();
		EmitStoreReloadChain(assembler);
		var original = assembler.Link(0, new Dictionary<string, uint>()).Bytes;
		assembler.OptimizeForCpu(M68kCpuTarget.M68000);

		Assert.Equal(original, assembler.Link(0, new Dictionary<string, uint>()).Bytes);
	}

	[Fact]
	public void BranchTargetReloadIsNotForwardedFromAnOptionalStore()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "reload");
		assembler.EmitWord(0x2F42);
		assembler.EmitWord(12);
		assembler.Mark("reload");
		assembler.EmitWord(0x206F);
		assembler.EmitWord(12);
		assembler.EmitWord(0x4E75);
		assembler.OptimizeForCpu(M68kCpuTarget.M68000);

		Assert.Contains(assembler.GetInstructionStream(), instruction => instruction.Opcode == 0x206F);
	}

	[Fact]
	public void ExplicitEffectOverridesPreventFrameTransferCleanup()
	{
		var assembler = StoreReloadChain();
		foreach (var instruction in assembler.GetInstructionStream().Where(instruction =>
			instruction.Opcode != 0x4e75))
		{
			assembler.SetInstructionEffects(instruction.Offset,
				M68kInstructionDataflow.GetEffects(instruction) with
				{
					IsBarrier = true,
					CanRemoveWhenOutputsDead = false
				});
		}
		assembler.OptimizeForCpu(M68kCpuTarget.M68000);

		Assert.Contains(assembler.GetInstructionStream(), instruction => instruction.Opcode == 0x206f);
	}

	private static (M68kMachineFunction, M68kMachineBlock) Function()
	{
		var function = new M68kMachineFunction("frame-test", 0);
		var entry = new M68kMachineBlock(0, 0);
		function.Blocks.Add(entry);
		return (function, entry);
	}

	private static int Scalar(M68kMachineFunction function) => function.CreateValue(
		CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data).Id;
	private static int Address(M68kMachineFunction function) => function.CreateValue(
		CilStackValueKind.ManagedPointer, M68kMachineValueWidth.Long, M68kRegisterSet.Address).Id;

	private static M68kAssembler StoreReloadChain()
	{
		var assembler = new M68kAssembler();
		EmitStoreReloadChain(assembler);
		assembler.EmitWord(0x4E75);
		return assembler;
	}

	private static M68kAssembler LabeledStoreChain(bool forwardedReload)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:frame");
		assembler.EmitJsr("method:frame-callee", external: false);
		var operations = new ushort[] { 0x2f42, forwardedReload ? (ushort)0x2042 : (ushort)0x206f, 0x2f48, 0x2f48 };
		for (var index = 0; index < operations.Length; index++)
		{
			assembler.Mark($"method:frame:IL_{index:X4}");
			assembler.EmitWord(operations[index]);
			if (operations[index] != 0x2042) assembler.EmitWord(12);
		}
		assembler.EmitWord(0x4e75);
		assembler.Mark("method:frame:end");
		assembler.Mark("method:frame-callee");
		assembler.EmitWord(0x4e75);
		assembler.Mark("method:frame-callee:end");
		return assembler;
	}

	private static void EmitStoreReloadChain(M68kAssembler assembler)
	{
		foreach (var opcode in new ushort[] { 0x2F42, 0x206F, 0x2F48, 0x2F48 })
		{
			assembler.EmitWord(opcode);
			assembler.EmitWord(12);
		}
	}

	private static (uint Address, uint Data, uint Stack, uint Memory, ushort Flags)
		ExecuteNative(byte[] code, M68kCpuModel model, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000;
		var bus = new TestBus();
		code.CopyTo(bus.Memory.AsSpan((int)load));
		bus.WriteLong(stack, sentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(load, stack);
		cpu.State.D[2] = value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var step = 0; step < 20 && cpu.State.ProgramCounter != sentinel; step++)
			cpu.ExecuteInstruction();
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		return (cpu.State.A[0], cpu.State.D[2], cpu.State.A[7], bus.ReadLong(stack + 12),
			(ushort)(cpu.State.StatusRegister & 0x1f));
	}
}
