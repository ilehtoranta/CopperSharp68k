/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection.Emit;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kComparisonPromotionTests
{
	[Theory]
	[InlineData(false, 128)]
	[InlineData(true, 32768)]
	public void MatchingUnsignedDomainUsesUnsignedFlagsWithoutWidening(bool word, int threshold)
	{
		var (function, block, left, right) = Create(word ? CilStackValueKind.UnsignedWord : CilStackValueKind.UnsignedByte,
			word ? M68kMachineValueWidth.Word : M68kMachineValueWidth.Byte, threshold);
		var compare = function.CreateInstruction(M68kMachineOperation.Compare, 0, uses: [left, right], sourceInstruction: new(0, OpCodes.Cgt, null, 0));
		block.Instructions.Add(compare);
		M68kComparisonPromotion.Run(function);
		Assert.Equal(OpCodes.Cgt_Un, block.Instructions.Last().SourceInstruction!.OpCode);
		Assert.DoesNotContain(block.Instructions, i => i.Operation == M68kMachineOperation.Convert);
		Assert.Equal(left, block.Instructions.Last().Uses[0]);
	}

	[Theory]
	[InlineData(-1)]
	[InlineData(256)]
	[InlineData(65536)]
	public void WiderThresholdCannotBeTruncatedToTheLeftByte(int threshold)
	{
		var (function, block, left, right) = Create(CilStackValueKind.UnsignedByte, M68kMachineValueWidth.Byte, threshold);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Compare, 0, uses: [left, right], sourceInstruction: new(0, OpCodes.Ceq, null, 0)));
		M68kComparisonPromotion.Run(function);
		var conversion = Assert.Single(block.Instructions.Where(i => i.Operation == M68kMachineOperation.Convert));
		Assert.Equal(OpCodes.Conv_U1, conversion.SourceInstruction!.OpCode);
		Assert.Equal(M68kMachineValueWidth.Long, function.Values[conversion.Definitions[0]].Width);
		Assert.Equal(conversion.Definitions[0], block.Instructions.Last().Uses[0]);
		Assert.Equal(right, block.Instructions.Last().Uses[1]);
	}

	[Fact]
	public void NarrowBranchConditionAndProducerAgreeAboutSignedness()
	{
		var (function, block, left, right) = Create(CilStackValueKind.UnsignedByte, M68kMachineValueWidth.Byte, 128);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ConditionalBranch, 0, uses: [left, right],
			sourceInstruction: new(0, OpCodes.Brtrue, 0, 0), branchCondition:
			new(M68kMachineConditionSourceKind.Compare, M68kCondition.GreaterOrEqual, new(0, OpCodes.Clt, null, 0))));
		M68kComparisonPromotion.Run(function);
		var branch = block.Instructions.Last().BranchCondition!;
		Assert.Equal(M68kCondition.CarryClear, branch.Condition);
		Assert.Equal(OpCodes.Clt_Un, branch.ProducerInstruction!.OpCode);
	}

	[Fact]
	public void SignedSourceIsSignExtendedWhenUnsignedDomainDoesNotFit()
	{
		var (function, block, left, right) = Create(CilStackValueKind.SignedByte, M68kMachineValueWidth.Byte, 128);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.ConditionalBranch, 0, uses: [left, right], sourceInstruction: new(0, OpCodes.Blt, 0, 0)));
		M68kComparisonPromotion.Run(function);
		Assert.Equal(OpCodes.Conv_I1, Assert.Single(block.Instructions.Where(i => i.Operation == M68kMachineOperation.Convert)).SourceInstruction!.OpCode);
		Assert.Equal(OpCodes.Blt, block.Instructions.Last().SourceInstruction!.OpCode);
	}

	private static (M68kMachineFunction Function, M68kMachineBlock Block, int Left, int Right) Create(CilStackValueKind kind, M68kMachineValueWidth width, int threshold)
	{
		var function = new M68kMachineFunction("promoted-comparison", 0);
		var block = new M68kMachineBlock(0, 0);
		function.Blocks.Add(block);
		var left = function.CreateValue(kind, width, M68kRegisterSet.Data);
		var right = function.CreateValue(CilStackValueKind.Int32, M68kMachineValueWidth.Long, M68kRegisterSet.Data);
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument, 0, definitions: [left.Id], argumentIndex: 0));
		block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Constant, 0, definitions: [right.Id], constantValue: M68kMachineConstant.Int32(threshold)));
		return (function, block, left.Id, right.Id);
	}
}
