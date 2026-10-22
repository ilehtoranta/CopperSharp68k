/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kConditionStorageTests
{
	[Fact]
	public void OppositeBranchFactsStaySeparateAndDisappearAtTheirJoin()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		var nonZeroTest = assembler.Offset;
		assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.True, "join");
		assembler.Mark("zero");
		var zeroTest = assembler.Offset;
		assembler.EmitWord(0x4A80);
		assembler.Mark("join");
		var joinedTest = assembler.Offset;
		assembler.EmitWord(0x4A80);
		assembler.EmitWord(0x4E75);

		var flow = M68kInstructionDataflow.Analyze(assembler);
		M68kEmittedInstruction At(int offset) => Assert.Single(flow.Instructions, instruction => instruction.Offset == offset);
		Assert.False(flow.TryGetKnownZeroTest(At(0), out _));
		Assert.True(flow.TryGetKnownZeroTest(At(nonZeroTest), out var nonZero));
		Assert.True(nonZero);
		Assert.True(flow.TryGetKnownZeroTest(At(zeroTest), out var alsoNonZero));
		Assert.False(alsoNonZero);
		Assert.False(flow.TryGetKnownZeroTest(At(joinedTest), out _));
	}
}
