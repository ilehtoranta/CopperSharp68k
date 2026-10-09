/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class NativePointerProvenanceTests
{
	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NativePointerCastKeepsTheManagedOwnerAliveWhileOnlyRawAddressIsLive(bool signed)
	{
		var function = new M68kMachineFunction("owned-native-pointer", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var owner = function.CreateValue(CilStackValueKind.Reference, M68kMachineValueWidth.Long, M68kRegisterSet.Address, isGcReference: true);
		var address = function.CreateValue(CilStackValueKind.ManagedPointer, M68kMachineValueWidth.Long, M68kRegisterSet.Address);
		var native = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.DataOrAddress);
		var restored = function.CreateValue(CilStackValueKind.ManagedPointer, M68kMachineValueWidth.Long, M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument, 0, definitions: [owner.Id], argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ArrayAddress, 1, uses: [owner.Id], definitions: [address.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Convert, 2, uses: [address.Id], definitions: [native.Id],
			sourceInstruction: new CilInstruction(2, signed ? OpCodes.Conv_I : OpCodes.Conv_U, null, 3)));
		var safepoint = function.CreateInstruction(M68kMachineOperation.Call, 3, isSafepoint: true);
		block.Instructions.Add(safepoint);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy, 4, uses: [native.Id], definitions: [restored.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 5, uses: [restored.Id]));
		var provenance = M68kByrefProvenanceAnalyzer.Analyze(function, true, out _);
		Assert.Equal(owner.Id, provenance[native.Id].OwnerValue);
		Assert.Equal(provenance[address.Id], provenance[restored.Id]);
		M68kByrefOwnerRooting.Insert(function, rejectManagedByrefReturn: false);
		var keepAlive = Assert.Single(block.Instructions, instruction => instruction.Operation == M68kMachineOperation.ByrefOwnerKeepAlive);
		Assert.Equal(new[] { owner.Id }, keepAlive.Uses);
		Assert.True(block.Instructions.IndexOf(keepAlive) > block.Instructions.IndexOf(safepoint));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void OrdinaryIntegerArgumentCannotAcquireCallerBorrowedPointerProvenance(bool borrowed)
	{
		var function = new M68kMachineFunction("untracked-integer-pointer", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var integer = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var pointer = function.CreateValue(CilStackValueKind.ManagedPointer, M68kMachineValueWidth.Long, M68kRegisterSet.Address);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument, 0, definitions: [integer.Id], argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy, 1, uses: [integer.Id], definitions: [pointer.Id]));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call, 2, uses: [pointer.Id], isSafepoint: true));
		var provenance = M68kByrefProvenanceAnalyzer.Analyze(function, borrowed, out _);
		Assert.Equal(M68kByrefProvenanceKind.Unknown, provenance[pointer.Id].Kind);
		Assert.Throws<M68kCompilationException>(() => M68kByrefOwnerRooting.Insert(function, allowCallerBorrowedByrefs: borrowed));
	}
}
