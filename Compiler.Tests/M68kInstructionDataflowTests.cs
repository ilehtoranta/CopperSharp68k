/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kInstructionDataflowTests
{
	[Fact]
	public void TracksConditionCodeLivenessAcrossAConditionalBranch()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		var flow = M68kInstructionDataflow.Analyze(assembler);

		Assert.True(flow.TryGetFacts(0, out var facts));
		Assert.False(facts.ConditionsAreDeadAfter);
		Assert.True((facts.LiveConditionsAfter & M68kConditionCodeSet.Zero) != 0);
	}

	[Fact]
	public void TracksDeadConditionCodesAndStackPointerDelta()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		var flow = M68kInstructionDataflow.Analyze(assembler);

		Assert.True(flow.TryGetFacts(0, out var testFacts));
		Assert.True(testFacts.ConditionsAreDeadAfter);
		Assert.True(flow.TryGetFacts(2, out var stackFacts));
		Assert.Equal(0, stackFacts.StackDeltaBefore);
		Assert.Equal(4, stackFacts.StackDeltaAfter);
	}

	[Fact]
	public void TracksRegisterAndStackMemoryEffects()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1
		assembler.EmitWord(0x4E75); // RTS

		var flow = M68kInstructionDataflow.Analyze(assembler);

		Assert.True(flow.TryGetFacts(0, out var pushFacts));
		Assert.True((pushFacts.Effects.UsesData & 1) != 0);
		Assert.True((pushFacts.Effects.DefinesAddress & 0x80) != 0);
		Assert.Equal(M68kMemorySet.Stack, pushFacts.Effects.WritesMemory);
		Assert.Equal(-4, pushFacts.Effects.StackDelta);

		Assert.True(flow.TryGetFacts(2, out var popFacts));
		Assert.True((popFacts.Effects.DefinesData & 2) != 0);
		Assert.Equal(M68kMemorySet.Stack, popFacts.Effects.ReadsMemory);
		Assert.Equal(4, popFacts.Effects.StackDelta);
	}

	[Fact]
	public void PeepholePipelineRemovesDeadTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("tst.l", assembly, StringComparison.Ordinal);
		Assert.Contains("rts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsLogicalImmediateOnlyWhenNegativeAndZeroFlagsAreDead()
	{
		var deadFlags = new M68kAssembler();
		deadFlags.EmitWord(0x0280); // ANDI.L #$FFFFFFFC,D0
		deadFlags.EmitLong(0xFFFF_FFFCu);
		deadFlags.EmitWord(0x4E75); // RTS
		deadFlags.OptimizeForM68000();

		var deadAssembly = deadFlags.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.w", deadAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l", deadAssembly, StringComparison.Ordinal);

		var liveFlags = new M68kAssembler();
		liveFlags.EmitWord(0x0280); // ANDI.L #$FFFFFFFC,D0
		liveFlags.EmitLong(0xFFFF_FFFCu);
		liveFlags.EmitBranch(M68kCondition.Equal, "done");
		liveFlags.EmitWord(0x7001); // MOVEQ #1,D0 on the fallthrough path
		liveFlags.Mark("done");
		liveFlags.EmitWord(0x4E75); // RTS
		var liveFacts = M68kInstructionDataflow.Analyze(liveFlags);
		Assert.True(liveFacts.TryGetFacts(0, out var liveImmediateFacts));
		Assert.True((liveImmediateFacts.LiveConditionsAfter & M68kConditionCodeSet.Zero) != 0);
		liveFlags.OptimizeForM68000();

		var liveAssembly = liveFlags.RenderAssembly(M68kCpuTarget.M68000);
		Assert.True(liveAssembly.Contains("andi.l", StringComparison.Ordinal), liveAssembly);
		Assert.DoesNotContain("andi.w", liveAssembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsOriImmediateWhenNegativeAndZeroFlagsAreDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0080); // ORI.L #$00001234,D0
		assembler.EmitLong(0x0000_1234u);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("ori.w", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("ori.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsAddImmediateWhenRangeProvesUpperWordIsPreserved()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		assembler.EmitWord(0x0680); // ADDI.L #20,D0
		assembler.EmitLong(20);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.w\t#$0014,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsLongAddWhenLowWordMayCarryIntoUpperWord()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #$0000FFFF,D0
		assembler.EmitLong(0x0000_FFFF);
		assembler.EmitWord(0x0680); // ADDI.L #1,D0
		assembler.EmitLong(1);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.l\t#$00000001,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsLongAddWhenConditionCodesAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		assembler.EmitWord(0x0680); // ADDI.L #20,D0
		assembler.EmitLong(20);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7201); // MOVEQ #1,D1 on the fallthrough path
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.l\t#$00000014,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsAddAcrossJoinedRangesWhenNoLowWordCarryIsPossible()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A82); // TST.L D2
		assembler.EmitBranch(M68kCondition.Equal, "alternate");
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		assembler.EmitBranch(M68kCondition.True, "join");
		assembler.Mark("alternate");
		assembler.EmitWord(0x7014); // MOVEQ #20,D0
		assembler.Mark("join");
		var addOffset = assembler.Offset;
		assembler.EmitWord(0x0680); // ADDI.L #5,D0
		assembler.EmitLong(5);
		assembler.EmitWord(0x4E75); // RTS

		var flow = M68kInstructionDataflow.Analyze(assembler);
		var range = flow.GetDataValueBefore(addOffset, 0);
		Assert.True(range.IsKnown);
		Assert.Equal(10u, range.Minimum);
		Assert.Equal(20u, range.Maximum);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.w\t#$0005,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsAddWhenNegativeUpperWordIsCancelledByGuaranteedCarry()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x0680); // ADDI.L #$FFFFFFFF,D0
		assembler.EmitLong(0xFFFF_FFFF);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.w\t#$FFFF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesStackAliasValuesToNarrowRegisterAdd()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2217); // MOVE.L (A7),D1
		assembler.EmitWord(0x7014); // MOVEQ #20,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.Equal(M68kAddressAlias.Stack(-4), flow.GetAddressAliasBefore(8, 7));
		Assert.True(flow.GetDataValueBefore(8, 1).IsExact(out var source));
		Assert.Equal(10u, source);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("add.w\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UnknownIndirectWriteInvalidatesAliasedStackValue()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2082); // MOVE.L D2,(A0)
		assembler.EmitWord(0x2217); // MOVE.L (A7),D1
		assembler.EmitWord(0x7014); // MOVEQ #20,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("add.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.w\td1,d0", assembly, StringComparison.Ordinal);
	}
}
