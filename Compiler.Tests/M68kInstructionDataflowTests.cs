/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kInstructionDataflowTests
{
	[Fact]
	public void ReplacesStackArgumentShuffleWithDirectLoads()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("entry");
		assembler.EmitWord(0x2F17);
		assembler.EmitWord(0x2F2F);
		assembler.EmitWord(8);
		assembler.EmitWord(0x201F);
		assembler.EmitWord(0x205F);
		assembler.EmitWord(0x4E75);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("entry:", assembly);
		Assert.DoesNotContain("move.l\t(a7),-(a7)", assembly);
		Assert.DoesNotContain("move.l\t8(a7),-(a7)", assembly);
		Assert.DoesNotContain("move.l\t(a7)+,d0", assembly);
		Assert.DoesNotContain("movea.l\t(a7)+,a0", assembly);
		Assert.Contains("move.l\t4(a7),d0", assembly);
		Assert.Contains("movea.l\t(a7),a0", assembly);
	}

	[Fact]
	public void RendersMoveLongDataRegisterToAddressDisplacement()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2140); // MOVE.L D0,4(A0)
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("move.l\td0,4(a0)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$2140", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$0004", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesZeroAndAddressStackRoundTrips()
	{
		var zeroAssembler = new M68kAssembler();
		zeroAssembler.EmitWord(0x7000); // MOVEQ #0,D0
		zeroAssembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		zeroAssembler.EmitWord(0x201F); // MOVE.L (A7)+,D0
		zeroAssembler.EmitWord(0x4E75); // RTS

		zeroAssembler.OptimizeForM68000();

		var zeroAssembly = zeroAssembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("clr.l\td0", zeroAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,-(a7)", zeroAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7)+,d0", zeroAssembly, StringComparison.Ordinal);

		var addressAssembler = new M68kAssembler();
		addressAssembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		addressAssembler.EmitWord(0x4850); // PEA (A0)
		addressAssembler.EmitWord(0x2F17); // MOVE.L (A7),-(A7)
		addressAssembler.EmitWord(0x201F); // MOVE.L (A7)+,D0
		addressAssembler.Mark("after");
		addressAssembler.EmitWord(0x4E75); // RTS

		addressAssembler.OptimizeForM68000();

		var addressAssembly = addressAssembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\ta0,-(a7)", addressAssembly, StringComparison.Ordinal);
		Assert.Contains("move.l\ta0,d0", addressAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("pea\t(a0)", addressAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),-(a7)", addressAssembly, StringComparison.Ordinal);

		var duplicateAssembler = new M68kAssembler();
		duplicateAssembler.EmitWord(0x2F17); // MOVE.L (A7),-(A7)
		duplicateAssembler.EmitWord(0x201F); // MOVE.L (A7)+,D0
		duplicateAssembler.EmitWord(0x4E75); // RTS

		duplicateAssembler.OptimizeForM68000();

		var duplicateAssembly = duplicateAssembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t(a7),d0", duplicateAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),-(a7)", duplicateAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7)+,d0", duplicateAssembly, StringComparison.Ordinal);
	}

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
	public void RemovesRedundantRegisterSpillAroundAnExpression()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("helper");
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		assembler.EmitWord(0x5680); // ADDQ.L #3,D0
		assembler.EmitWord(0x0240); // ANDI.W #$FFFC,D0
		assembler.EmitWord(0xFFFC);
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
		Assert.Contains("addq.l\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("andi.w\t#$FFFC,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReplacesCompareAgainstMoveqZeroWithTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0xB081); // CMP.L D1,D0
		assembler.EmitWord(0x57C0); // SEQ D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("moveq\t#0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesDeadRegisterMoveAndArithmeticTransitively()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7201); // MOVEQ #1,D1
		assembler.EmitWord(0x0681); // ADDI.L #2,D1
		assembler.EmitLong(2);
		assembler.EmitWord(0x7209); // MOVEQ #9,D1 kills the dead chain
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("moveq\t#1,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#9,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("rts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesDeadRegisterToRegisterMove()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0 return value
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7209); // MOVEQ #9,D1 kills the move result
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UndecodedInstructionKeepsItsExtensionWord()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xF140); // Unrecognized opcode with an extension word
		assembler.EmitWord(8);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("dc.w\t$F140", assembly, StringComparison.Ordinal);
		Assert.Contains("dc.w\t$0008", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovingDeadSymbolMoveAlsoRemovesItsRelocation()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x227C); // MOVEA.L #value,A1
		assembler.EmitAddress("value");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("value");
		assembler.EmitLong(0x1234_5678);

		assembler.OptimizeForM68000();
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Empty(linked.Relocations);
		Assert.Equal(6, linked.Bytes.Length);
	}

	[Fact]
	public void KeepsReturnValueAndPreservedRegistersLiveAtReturn()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0 return value
		assembler.EmitWord(0x7402); // MOVEQ #2,D2 preserved register
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#2,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDeadDestinationInstructionWhenConditionCodesAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7201); // MOVEQ #1,D1
		assembler.EmitWord(0x0681); // ADDI.L #2,D1
		assembler.EmitLong(2);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7007); // MOVEQ #7,D0 on fallthrough
		assembler.Mark("done");
		assembler.EmitWord(0x7209); // MOVEQ #9,D1 kills the arithmetic result
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.l\t#$00000002,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("beq.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMemoryReadWhenDestinationRegisterIsDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2250); // MOVEA.L (A0),A1
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\t(a0),a1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersMoveLongDataRegisterToAddressPostincrement()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x20C1); // MOVE.L D1,(A0)+
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("move.l\td1,(a0)+", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$20C1", assembly, StringComparison.Ordinal);
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
		assembler.EmitWord(0x7001); // MOVEQ #1,D0 on the fallthrough path
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

	[Theory]
	[InlineData(0x41E8, 4, "addq.l\t#4,a0")]
	[InlineData(0x47EB, -8, "subq.l\t#8,a3")]
	public void ReplacesSmallSameRegisterLeaWithQuick(
		ushort opcode,
		short displacement,
		string expected)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(opcode);
		assembler.EmitWord(unchecked((ushort)displacement));
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains(expected, assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("lea\t", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void MergesAddressQuickAdjustmentsIntoLea()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x5088); // ADDQ.L #8,A0
		assembler.EmitWord(0x5088); // ADDQ.L #8,A0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("lea\t16(a0),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesCancellingAddressAdjustments()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x588C); // ADDQ.L #4,A4
		assembler.EmitWord(0x598C); // SUBQ.L #4,A4
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("\trts", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void DoesNotMergeAddressAdjustmentsAcrossLabel()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x5088); // ADDQ.L #8,A0
		assembler.Mark("boundary");
		assembler.EmitWord(0x5088); // ADDQ.L #8,A0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(2, assembly.Split("addq.l\t#8,a0").Length - 1);
		Assert.DoesNotContain("lea\t16(a0),a0", assembly, StringComparison.Ordinal);
	}
}
