/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class ExactMemorySubstitutionTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CopyRemovalPreservesStoreLoadPromotion(bool heapField)
	{
		var function = new M68kMachineFunction("copied-memory-identities", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var stored = CreateLong(function);
		var storedCopy = CreateLong(function);
		var loaded = CreateLong(function);
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Argument, 0,
			definitions: [stored.Id], argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Copy, 1,
			uses: [stored.Id], definitions: [storedCopy.Id]));

		var memory = M68kMemoryModel.LibraryBaseObject("_GraphicsLibraryBase");
		var convention = new M68kExternalCallConvention("graphics.library",
			M68kExternalBaseSource.WritableSlot, M68kRegister.A6, 0,
			SlotSymbol: "_GraphicsLibraryBase");
		var owners = new Dictionary<int, M68kHeapOwnerFacts>();
		var addressUses = Array.Empty<int>();
		if (heapField)
		{
			var owner = function.CreateValue(CilStackValueKind.Reference,
				M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress,
				isGcReference: true);
			var ownerCopy = function.CreateValue(CilStackValueKind.Reference,
				M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress,
				isGcReference: true);
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Argument, 2,
				definitions: [owner.Id], argumentIndex: 1));
			block.Instructions.Add(function.CreateInstruction(
				M68kMachineOperation.Copy, 3,
				uses: [owner.Id], definitions: [ownerCopy.Id]));
			memory = new M68kMemoryObject(M68kMemoryObjectKind.ObjectField,
				"copied-owner:payload", OwnerValueId: ownerCopy.Id,
				Offset: 8, Size: 4);
			owners.Add(owner.Id, new M68kHeapOwnerFacts(owner.Id,
				IsArray: false, IsPromotable: true, HasFinalizer: false,
				ConstructorMayWrite: true, ConstantLength: null,
				ElementSize: 0, StorageIdentity: "copied-owner"));
			addressUses = [ownerCopy.Id];
		}

		block.Instructions.Add(function.CreateInstruction(
			heapField ? M68kMachineOperation.Store : M68kMachineOperation.PlatformBaseStore,
			4, uses: [.. addressUses, storedCopy.Id],
			memoryEffect: M68kMachineMemoryEffect.Write,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(memory,
					M68kExactMemoryAccessKind.Write, storedCopy.Id)
			], platformBaseConvention: heapField ? null : convention));
		block.Instructions.Add(function.CreateInstruction(
			heapField ? M68kMachineOperation.Load : M68kMachineOperation.PlatformBaseLoad,
			5, uses: addressUses, definitions: [loaded.Id],
			memoryEffect: M68kMachineMemoryEffect.Read,
			exactMemoryAccesses:
			[
				new M68kExactMemoryAccess(memory,
					M68kExactMemoryAccessKind.Read, loaded.Id)
			], platformBaseConvention: heapField ? null : convention));
		block.Instructions.Add(function.CreateInstruction(
			M68kMachineOperation.Return, 6, uses: [loaded.Id]));

		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);
		var promotion = M68kMemoryPromotionPass.Run(function,
			new M68kMemoryPromotionContext(null!, null!,
				new Dictionary<CilMethodIdentity, M68kMethodMemorySummary>(),
				heapField ? new HashSet<M68kMemoryObject>() : [memory],
				owners, function));
		M68kMachineOptimizer.Run(function, M68kCpuTarget.M68000);

		Assert.Equal(1, promotion.LoadsForwarded);
		Assert.Equal(1, promotion.StoresRemoved);
		Assert.DoesNotContain(block.Instructions, static instruction =>
			instruction.MemoryEffect != M68kMachineMemoryEffect.None);
		var returned = Assert.Single(block.Instructions,
			static instruction => instruction.Operation == M68kMachineOperation.Return);
		Assert.Equal(stored.Id, Assert.Single(returned.Uses));
	}

	private static M68kMachineValue CreateLong(M68kMachineFunction function) =>
		function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long,
			M68kRegisterSet.DataOrAddress);
}
