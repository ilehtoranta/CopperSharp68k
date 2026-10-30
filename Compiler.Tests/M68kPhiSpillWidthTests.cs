/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kPhiSpillWidthTests
{
	[Theory]
	[InlineData((int)CilStackValueKind.UnsignedByte, (int)M68kMachineValueWidth.Byte)]
	[InlineData((int)CilStackValueKind.SignedByte, (int)M68kMachineValueWidth.Byte)]
	[InlineData((int)CilStackValueKind.UnsignedWord, (int)M68kMachineValueWidth.Word)]
	[InlineData((int)CilStackValueKind.SignedWord, (int)M68kMachineValueWidth.Word)]
	public void SpilledLongPhiExtendsNarrowIncomingEdgeBeforeStoring(
		int sourceKindId,
		int sourceWidthId)
	{
		var sourceKind = (CilStackValueKind)sourceKindId;
		var sourceWidth = (M68kMachineValueWidth)sourceWidthId;
		var function = new M68kMachineFunction("mixed-width-phi", 0);
		var narrow = AddBlock(function, 0);
		var priorLong = AddBlock(function, 1);
		var join = AddBlock(function, 2);
		Connect(narrow, priorLong);
		Connect(narrow, join); // The narrow incoming edge must be split.
		Connect(priorLong, join);
		var narrowValue = function.CreateValue(sourceKind, sourceWidth, M68kRegisterSet.Data);
		var sentinel = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var merged = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		narrow.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 0, definitions: [narrowValue.Id]));
		narrow.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ConditionalBranch, 0));
		priorLong.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 1, definitions: [sentinel.Id]));
		priorLong.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Branch, 1));
		join.Phis.Add(new M68kMachinePhi(merged.Id, new Dictionary<int, int>
		{
			[narrow.Id] = narrowValue.Id,
			[priorLong.Id] = sentinel.Id
		}));
		join.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 2, uses: [merged.Id]));
		var layout = new M68kSpillLayout(
			new Dictionary<int, M68kSpillSlot> { [merged.Id] = new(0, 0, 4, false) },
			new HashSet<int>(),
			4);

		M68kSpillRewriter.Rewrite(function, layout);

		var split = Assert.Single(function.Blocks.Where(block => block.Id > join.Id));
		Assert.Equal([narrow.Id], split.Predecessors);
		Assert.Equal([join.Id], split.Successors);
		var conversion = Assert.Single(split.Instructions.Where(instruction =>
			instruction.Operation == M68kMachineOperation.Convert));
		Assert.Equal([narrowValue.Id], conversion.Uses);
		Assert.Equal(sourceKind switch
		{
			CilStackValueKind.SignedByte => OpCodes.Conv_I1,
			CilStackValueKind.SignedWord => OpCodes.Conv_I2,
			CilStackValueKind.UnsignedWord => OpCodes.Conv_U2,
			_ => OpCodes.Conv_U1
		}, conversion.SourceInstruction!.OpCode);
		Assert.Equal(M68kMachineValueWidth.Long, function.Values[conversion.Definitions.Single()].Width);
		var narrowStore = Assert.Single(split.Instructions.Where(instruction =>
			instruction.Operation == M68kMachineOperation.SpillStore));
		Assert.Equal(conversion.Definitions.Single(), narrowStore.Uses.Single());
		Assert.Equal(M68kMachineValueWidth.Long, function.Values[narrowStore.Uses.Single()].Width);
		var priorStore = Assert.Single(priorLong.Instructions.Where(instruction =>
			instruction.Operation == M68kMachineOperation.SpillStore));
		Assert.Equal(sentinel.Id, priorStore.Uses.Single());
		Assert.Equal(narrowStore.SpillSlotIndex, priorStore.SpillSlotIndex);
		M68kMachineIrVerifier.Verify(function);
	}

	[Fact]
	public void SpilledBytePhiTruncatesLongIncomingEdgeBeforeStoring()
	{
		var function = new M68kMachineFunction("narrow-phi", 0);
		var first = AddBlock(function, 0);
		var second = AddBlock(function, 1);
		var join = AddBlock(function, 2);
		Connect(first, second);
		Connect(first, join);
		Connect(second, join);
		var wide = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		var narrow = function.CreateValue(CilStackValueKind.UnsignedByte, M68kMachineValueWidth.Byte, M68kRegisterSet.Data);
		var merged = function.CreateValue(CilStackValueKind.UnsignedByte, M68kMachineValueWidth.Byte, M68kRegisterSet.Data);
		first.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 0, definitions: [wide.Id]));
		first.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ConditionalBranch, 0));
		second.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 1, definitions: [narrow.Id]));
		second.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Branch, 1));
		join.Phis.Add(new M68kMachinePhi(merged.Id, new Dictionary<int, int>
		{
			[first.Id] = wide.Id,
			[second.Id] = narrow.Id
		}));
		join.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 2, uses: [merged.Id]));
		var layout = new M68kSpillLayout(
			new Dictionary<int, M68kSpillSlot> { [merged.Id] = new(0, 0, 1, false) },
			new HashSet<int>(),
			4);

		M68kSpillRewriter.Rewrite(function, layout);

		var split = Assert.Single(function.Blocks.Where(block => block.Id > join.Id));
		var conversion = Assert.Single(split.Instructions.Where(instruction =>
			instruction.Operation == M68kMachineOperation.Convert));
		Assert.Equal(OpCodes.Conv_U1, conversion.SourceInstruction!.OpCode);
		var store = Assert.Single(split.Instructions.Where(instruction =>
			instruction.Operation == M68kMachineOperation.SpillStore));
		Assert.Equal(M68kMachineValueWidth.Byte, function.Values[store.Uses.Single()].Width);
		Assert.Equal(conversion.Definitions.Single(), store.Uses.Single());
		M68kMachineIrVerifier.Verify(function);
	}

	private static M68kMachineBlock AddBlock(M68kMachineFunction function, int id)
	{
		var block = new M68kMachineBlock(id, id);
		function.Blocks.Add(block);
		return block;
	}

	private static void Connect(M68kMachineBlock source, M68kMachineBlock target)
	{
		source.Successors.Add(target.Id);
		target.Predecessors.Add(source.Id);
	}
}
