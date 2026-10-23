using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class PlatformBaseRematerializationTests
{
	[Fact]
	public void AdjacentFixedCopiesStillLoadDirectlyIntoBaseRegister()
	{
		var (function, block, source, _, _) = CreateLoadAndCopy();
		var second = FixedBase(function);
		block.Instructions.Insert(2, function.CreateInstruction(
			M68kMachineOperation.Copy, 1,
			uses: [source], definitions: [second]));

		M68kCallAbiLowering.FinalizeLogicalCalls(function);

		Assert.Equal(2, block.Instructions.Count(instruction =>
			instruction.Operation == M68kMachineOperation.PlatformBaseLoad));
		Assert.DoesNotContain(block.Instructions, instruction =>
			instruction.Operation == M68kMachineOperation.Copy);
		Assert.DoesNotContain(source, function.Values.Keys);
	}

	[Theory]
	[InlineData("slot-write")]
	[InlineData("unknown-call")]
	[InlineData("may-throw")]
	[InlineData("safepoint")]
	[InlineData("clobber")]
	[InlineData("volatile-read")]
	public void BarrierKeepsOriginalPlatformBaseRead(string barrier)
	{
		var (function, block, source, load, copy) = CreateLoadAndCopy();
		var crossed = barrier switch
		{
			"slot-write" => function.CreateInstruction(
				M68kMachineOperation.PlatformBaseStore, 1,
				memoryEffect: M68kMachineMemoryEffect.Write,
				immediate: 0,
				platformBaseConvention: load.PlatformBaseConvention),
			"unknown-call" => function.CreateInstruction(
				M68kMachineOperation.Call, 1,
				clobbers: M68kRegisterSet.From(M68kRegister.A6),
				memoryEffect: M68kMachineMemoryEffect.Read |
					M68kMachineMemoryEffect.Write,
				isSafepoint: true, mayThrow: true),
			"may-throw" => function.CreateInstruction(
				M68kMachineOperation.Other, 1, mayThrow: true),
			"safepoint" => function.CreateInstruction(
				M68kMachineOperation.Other, 1, isSafepoint: true),
			"clobber" => function.CreateInstruction(
				M68kMachineOperation.Other, 1,
				clobbers: M68kRegisterSet.From(M68kRegister.A5)),
			"volatile-read" => function.CreateInstruction(
				M68kMachineOperation.Other, 1,
				memoryEffect: M68kMachineMemoryEffect.Read |
					M68kMachineMemoryEffect.Volatile),
			_ => throw new ArgumentOutOfRangeException(nameof(barrier))
		};
		block.Instructions.Insert(1, crossed);

		M68kCallAbiLowering.FinalizeLogicalCalls(function);

		Assert.Contains(block.Instructions, instruction => instruction.Id == load.Id);
		Assert.Contains(block.Instructions, instruction => instruction.Id == copy.Id);
		Assert.Contains(source, function.Values.Keys);
	}

	[Fact]
	public void CrossBlockCopyKeepsOriginalPlatformBaseRead()
	{
		var (function, entry, source, load, copy) = CreateLoadAndCopy();
		entry.Instructions.Remove(copy);
		entry.Instructions.RemoveAt(entry.Instructions.Count - 1);
		entry.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Branch, 1));
		var successor = new M68kMachineBlock(1, 1);
		function.Blocks.Add(successor);
		function.AddEdge(entry, successor);
		successor.Instructions.Add(copy);
		successor.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return, 2));

		M68kCallAbiLowering.FinalizeLogicalCalls(function);

		Assert.Contains(entry.Instructions, instruction => instruction.Id == load.Id);
		Assert.Contains(successor.Instructions, instruction => instruction.Id == copy.Id);
		Assert.Contains(source, function.Values.Keys);
	}

	[Fact]
	public void CachedPointerDefinitionKeepsOriginalCacheSnapshot()
	{
		var (function, block, source, load, copy) = CreateLoadAndCopy();
		block.Instructions[0] = load with
		{
			MemoryEffect = M68kMachineMemoryEffect.None,
			ExactMemoryAccesses = [],
			PlatformBaseConvention = new M68kExternalCallConvention(
				"cached.library", M68kExternalBaseSource.CachedPointer,
				M68kRegister.A6, -30,
				CacheRegister: M68kRegister.A5, SourceAddress: 4)
		};
		function.ReservedRegisters = function.ReservedRegisters.Add(M68kRegister.A5);
		var cacheDefinition = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.A5),
			precoloredRegister: M68kRegister.A5);
		block.Instructions.Insert(1, function.CreateInstruction(
			M68kMachineOperation.Other, 1,
			definitions: [cacheDefinition.Id]));

		M68kCallAbiLowering.FinalizeLogicalCalls(function);

		Assert.Contains(block.Instructions, instruction => instruction.Id == load.Id);
		Assert.Contains(block.Instructions, instruction => instruction.Id == copy.Id);
		Assert.Contains(source, function.Values.Keys);
	}

	[Fact]
	public void DistinctSlotsRemainDistinctDirectLoads()
	{
		var function = new M68kMachineFunction("distinct-platform-bases", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		foreach (var (identity, slot) in new[]
		{
			("dos.library", "_DOSLibraryBase"),
			("exec.library", "_ExecBase")
		})
		{
			var convention = Convention(identity, slot);
			var source = function.CreateValue(CilStackValueKind.Int32,
				M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress);
			var destination = FixedBase(function);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.PlatformBaseLoad, 0,
				definitions: [source.Id],
				memoryEffect: M68kMachineMemoryEffect.Read,
				exactMemoryAccesses:
				[
					new M68kExactMemoryAccess(
						M68kMemoryModel.LibraryBaseObject(slot),
						M68kExactMemoryAccessKind.Read, source.Id)
				],
				platformBaseConvention: convention));
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy, 0,
				uses: [source.Id], definitions: [destination]));
		}
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return, 1));

		M68kCallAbiLowering.FinalizeLogicalCalls(function);

		Assert.Equal(new[] { "dos.library", "exec.library" },
			block.Instructions.Where(instruction =>
				instruction.Operation == M68kMachineOperation.PlatformBaseLoad)
				.Select(instruction => instruction.PlatformBaseConvention!.Identity));
	}

	private static (M68kMachineFunction Function, M68kMachineBlock Block,
		int Source, M68kMachineInstruction Load, M68kMachineInstruction Copy)
		CreateLoadAndCopy()
	{
		var function = new M68kMachineFunction("platform-base-rematerialization", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var convention = Convention("dos.library", "_DOSLibraryBase");
		var source = function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress);
		var destination = FixedBase(function);
		var load = function.CreateInstruction(M68kMachineOperation.PlatformBaseLoad, 0,
			definitions: [source.Id],
			memoryEffect: M68kMachineMemoryEffect.Read,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(
					M68kMemoryModel.LibraryBaseObject("_DOSLibraryBase"),
					M68kExactMemoryAccessKind.Read, source.Id)
			],
			platformBaseConvention: convention);
		var copy = function.CreateInstruction(M68kMachineOperation.Copy, 1,
			uses: [source.Id], definitions: [destination]);
		block.Instructions.Add(load);
		block.Instructions.Add(copy);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return, 2));
		return (function, block, source.Id, load, copy);
	}

	private static int FixedBase(M68kMachineFunction function) =>
		function.CreateValue(CilStackValueKind.Int32,
			M68kMachineValueWidth.Long,
			M68kRegisterSet.From(M68kRegister.A6),
			precoloredRegister: M68kRegister.A6).Id;

	private static M68kExternalCallConvention Convention(string identity,
		string slot) => new(identity, M68kExternalBaseSource.WritableSlot,
		M68kRegister.A6, -30, SlotSymbol: slot);
}
