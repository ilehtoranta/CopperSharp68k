/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class WordArithmeticInstructionTests
{
	[Theory]
	[InlineData(0xC6FC, "mulu.w", true)]
	[InlineData(0xC7FC, "muls.w", true)]
	[InlineData(0x86FC, "divu.w", false)]
	[InlineData(0x87FC, "divs.w", false)]
	public void ImmediateWordArithmeticKeepsItsExtensionAndDataRegisterEffects(
		int opcode, string mnemonic, bool removable)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord((ushort)opcode); // arithmetic #$7A12,D3
		assembler.EmitWord(0x7A12); // Also a valid MOVEQ opcode if split incorrectly.
		assembler.EmitWord(0x4E75);

		var instructions = assembler.GetInstructionStream();
		Assert.Equal(2, instructions.Count);
		Assert.Equal(4, instructions[1].Offset);
		Assert.All(instructions, instruction => Assert.True(instruction.IsDecoded));
		Assert.Contains(mnemonic + "\t", assembler.RenderAssembly(M68kCpuTarget.M68000));
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(flow.TryGetFacts(0, out var arithmetic));
		Assert.Equal(1 << 3, arithmetic.Effects.UsesData);
		Assert.Equal(1 << 3, arithmetic.Effects.DefinesData);
		Assert.Equal(0, arithmetic.Effects.UsesAddress);
		Assert.Equal(0, arithmetic.Effects.DefinesAddress);
		Assert.Equal(M68kConditionCodeSet.Negative | M68kConditionCodeSet.Zero |
			M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry,
			arithmetic.Effects.WritesConditions);
		Assert.Equal(M68kConditionCodeSet.None, arithmetic.Effects.ReadsConditions);
		Assert.Equal(M68kMemorySet.None, arithmetic.Effects.ReadsMemory);
		Assert.Equal(removable, arithmetic.Effects.CanRemoveWhenOutputsDead);
	}

	[Theory]
	[InlineData(0xC6DF)]
	[InlineData(0xC7DF)]
	[InlineData(0x86DF)]
	[InlineData(0x87DF)]
	public void WordArithmeticStackSourceConsumesTwoBytesAndIsNotRemovable(int opcode)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord((ushort)opcode); // arithmetic (A7)+,D3
		assembler.EmitWord(0x4E75);
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(flow.TryGetFacts(0, out var arithmetic));
		Assert.Equal(2, arithmetic.Effects.StackDelta);
		Assert.Equal(1 << 7, arithmetic.Effects.UsesAddress);
		Assert.Equal(1 << 7, arithmetic.Effects.DefinesAddress);
		Assert.Equal(1 << 3, arithmetic.Effects.UsesData);
		Assert.Equal(1 << 3, arithmetic.Effects.DefinesData);
		Assert.Equal(M68kMemorySet.Stack, arithmetic.Effects.ReadsMemory);
		Assert.False(arithmetic.Effects.CanRemoveWhenOutputsDead);
	}

	[Theory]
	[InlineData(0xC6FB, 0x2004, 1 << 2, 0)]
	[InlineData(0xC7FB, 0xC804, 0, 1 << 4)]
	[InlineData(0x86FB, 0x2004, 1 << 2, 0)]
	[InlineData(0x87FB, 0xC804, 0, 1 << 4)]
	public void PcIndexedWordArithmeticKeepsItsIndexLive(int opcode, int extension,
		int indexData, int indexAddress)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord((ushort)opcode);
		assembler.EmitWord((ushort)extension);
		assembler.EmitWord(0x4E75);
		var instructions = assembler.GetInstructionStream();
		Assert.Equal(2, instructions.Count);
		Assert.True(instructions[0].IsDecoded);
		Assert.Equal(4, instructions[1].Offset);
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(flow.TryGetFacts(0, out var arithmetic));
		Assert.Equal((1 << 3) | indexData, arithmetic.Effects.UsesData);
		Assert.Equal(indexAddress, arithmetic.Effects.UsesAddress);
		Assert.Equal(1 << 3, arithmetic.Effects.DefinesData);
		Assert.Equal(0, arithmetic.Effects.DefinesAddress);
		Assert.Equal(M68kMemorySet.Indirect, arithmetic.Effects.ReadsMemory);
		Assert.False(arithmetic.Effects.CanRemoveWhenOutputsDead);
	}
}
