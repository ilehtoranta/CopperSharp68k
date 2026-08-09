/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kInstructionDataflowTests
{
	[Fact]
	public void CallClobbersButDoesNotConsumeConditionCodes()
	{
		var assembler = new M68kAssembler();
		assembler.EmitJsr("helper", external: true);

		var instruction = Assert.Single(assembler.GetInstructionStream());
		var effects = M68kInstructionDataflow.GetEffects(instruction);
		Assert.Equal(M68kConditionCodeSet.None, effects.ReadsConditions);
		Assert.Equal(M68kConditionCodeSet.All, effects.WritesConditions);
	}

	[Fact]
	public void RelocatableImmediateIsNotTreatedAsLiteralZero()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #symbol,D0
		assembler.EmitAddress("symbol");
		assembler.EmitWord(0x4E71); // NOP

		var dataflow = M68kInstructionDataflow.Analyze(assembler);

		Assert.False(dataflow.GetDataValueBefore(6, 0).IsExact(out _));
	}

	[Fact]
	public void RelocatableStackStoreDoesNotReuseZeroRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x2F7C); // MOVE.L #symbol,8(A7)
		assembler.EmitAddress("symbol");
		assembler.EmitWord(8);
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("symbol");
		assembler.EmitWord(0);

		assembler.OptimizeForM68000();

		Assert.Contains(
			assembler.GetInstructionStream(),
			static instruction => instruction.Opcode == 0x2F7C && instruction.Length == 8);
		Assert.DoesNotContain(
			assembler.GetInstructionStream(),
			static instruction => instruction.Opcode == 0x2F40);
	}

	[Fact]
	public void ClassifiesExtLongAsRegisterUnaryRatherThanMovem()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x48C3); // EXT.L D3

		var instruction = Assert.Single(assembler.GetInstructionStream());
		var effects = M68kInstructionDataflow.GetEffects(instruction);
		Assert.Equal(1 << 3, effects.UsesData);
		Assert.Equal(1 << 3, effects.DefinesData);
		Assert.Equal(M68kMemorySet.None, effects.ReadsMemory);
		Assert.Equal(M68kMemorySet.None, effects.WritesMemory);
	}

	[Fact]
	public void KeepsMoveQuickThatFeedsExtLong()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x709C); // MOVEQ #-100,D0
		assembler.EmitWord(0x48C0); // EXT.L D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#-100,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("ext.l\td0", assembly, StringComparison.Ordinal);
	}

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
	public void ForwardsStackFrameLeaIntoAddressRegisterLoad()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x41EF); // LEA 16(A7),A0
		assembler.EmitWord(16);
		assembler.EmitWord(0x2050); // MOVEA.L (A0),A0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("movea.l	16(a7),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("lea	16(a7),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l	(a0),a0", assembly, StringComparison.Ordinal);
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
	public void FoldsDataRegisterSwapMovesIntoExg()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x2403); // MOVE.L D3,D2
		assembler.EmitWord(0x2600); // MOVE.L D0,D3
		assembler.EmitWord(0x2002); // D0 is overwritten before return
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("exg\td2,d3", assembly, StringComparison.Ordinal);
		Assert.Equal(1, assembly.Split("move.l\td2,d0", StringSplitOptions.None).Length - 1);
		Assert.DoesNotContain("move.l\td3,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d3", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersAndTracksDataAddressExchangeForAddressBackedAddition()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xC188); // EXG D0,A0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0xC188); // EXG D0,A0
		assembler.EmitWord(0x4E75); // RTS observes both return registers

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(2, assembly.Split("exg\td0,a0", StringSplitOptions.None).Length - 1);
		Assert.Contains("adda.l\td0,a0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersAndTracksDirectMemoryAddToDataRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xD0A9); // ADD.L 12(A1),D0
		assembler.EmitWord(12);
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var flow = M68kInstructionDataflow.Analyze(assembler);

		Assert.Contains("add.l\t12(a1),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$D0A9", assembly, StringComparison.Ordinal);
		Assert.True(flow.TryGetFacts(0, out var facts));
		Assert.True((facts.Effects.UsesData & 1) != 0);
		Assert.True((facts.Effects.DefinesData & 1) != 0);
		Assert.True((facts.Effects.UsesAddress & 2) != 0);
		Assert.Equal(M68kMemorySet.Indirect, facts.Effects.ReadsMemory);
	}

	[Fact]
	public void ForwardsMemoryLoadIntoLongArithmetic()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0
		assembler.EmitWord(12);
		assembler.EmitWord(0xD480); // ADD.L D0,D2
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("add.l\t12(a0),d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t12(a0),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ForwardsMemoryLoadIntoLongCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0
		assembler.EmitWord(12);
		assembler.EmitWord(0xB480); // CMP.L D0,D2
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("cmp.l\t12(a0),d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t12(a0),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmp.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersForwardedAbsoluteMemoryArithmeticWithItsSymbol()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2039); // MOVE.L absolute.L,D0
		assembler.EmitAddress("value");
		assembler.EmitWord(0xD480); // ADD.L D0,D2
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("value");
		assembler.EmitLong(0);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("add.l\tC68K_value,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\tC68K_value,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMemoryLoadWhenTemporaryRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2028); // MOVE.L 12(A0),D0
		assembler.EmitWord(12);
		assembler.EmitWord(0xD480); // ADD.L D0,D2
		assembler.EmitWord(0x4E75); // RTS returns D0

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("move.l\t12(a0),d0", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersAndTracksScaledIndexedOperandsAndArbitraryLeaRegisters()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2232); // MOVE.L 12(A2,D3.L*4),D1
		assembler.EmitWord(0x3C0C);
		assembler.EmitWord(0x47F4); // LEA 12(A4,D2.L*2),A3
		assembler.EmitWord(0x2A0C);
		assembler.EmitWord(0x4E75);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		var flow = M68kInstructionDataflow.Analyze(assembler);

		Assert.Contains(
			"move.l\t12(a2,d3.l*4),d1",
			assembly,
			StringComparison.Ordinal);
		Assert.Contains(
			"lea\t12(a4,d2.l*2),a3",
			assembly,
			StringComparison.Ordinal);
		Assert.True(flow.TryGetFacts(0, out var move));
		Assert.True((move.Effects.UsesAddress & (1 << 2)) != 0);
		Assert.True((move.Effects.UsesData & (1 << 3)) != 0);
		Assert.Equal(M68kMemorySet.Indirect, move.Effects.ReadsMemory);
		Assert.True(flow.TryGetFacts(4, out var lea));
		Assert.True((lea.Effects.UsesAddress & (1 << 4)) != 0);
		Assert.True((lea.Effects.UsesData & (1 << 2)) != 0);
		Assert.True((lea.Effects.DefinesAddress & (1 << 3)) != 0);
		Assert.Equal(M68kMemorySet.None, lea.Effects.ReadsMemory);
	}

	[Fact]
	public void RendersPreviouslyUnsupportedExecutableInstructions()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xB1FC); // CMPA.L #0,A0
		assembler.EmitLong(0);
		assembler.EmitWord(0x43EE); // LEA 12(A6),A1
		assembler.EmitWord(12);
		assembler.EmitWord(0x4CE9); // MOVEM.L 16(A1),D2-D7/A2-A5
		assembler.EmitWord(0x3CFC);
		assembler.EmitWord(16);
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("cmpa.l\t#$00000000,a0", assembly, StringComparison.Ordinal);
		Assert.Contains("lea\t12(a6),a1", assembly, StringComparison.Ordinal);
		Assert.Contains("movem.l\t16(a1),d2-d7/a2-a5", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$B1FC", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$43EE", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$4CE9", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$3CFC", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsAddressCompareAgainstZeroToWordSize()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xB1FC); // CMPA.L #0,A0
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("cmpa.w\t#0,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpa.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesAddressCompareWhenMoveAlreadyEstablishedFlags()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x2041); // MOVEA.L D1,A0
		assembler.EmitWord(0xB1FC); // CMPA.L #0,A0
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("cmpa.l", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpa.w", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\td1,a0", assembly, StringComparison.Ordinal);
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

		var clearAssembler = new M68kAssembler();
		clearAssembler.EmitWord(0x42A7); // CLR.L -(A7)
		clearAssembler.EmitWord(0x201F); // MOVE.L (A7)+,D0
		clearAssembler.EmitWord(0x4E75); // RTS

		clearAssembler.OptimizeForM68000();

		var clearAssembly = clearAssembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("clr.l\td0", clearAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("clr.l\t-(a7)", clearAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7)+,d0", clearAssembly, StringComparison.Ordinal);

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
	public void RemovesRepeatedZeroReloadAcrossMemoryStores()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
		assembler.EmitWord(0x7000); // MOVEQ #0,D0, still known zero
		assembler.EmitWord(0x2140); // MOVE.L D0,4(A0)
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
		Assert.Contains("move.l\td0,(a0)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,4(a0)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRepeatedNonZeroMoveQuickWhenValueIsStillKnown()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7010); // MOVEQ #16,D0
		assembler.EmitWord(0xD880); // ADD.L D0,D4; D0 remains 16
		assembler.EmitWord(0x2604); // MOVE.L D4,D3; D0 still remains 16
		assembler.EmitWord(0x7010); // Redundant MOVEQ #16,D0
		assembler.EmitWord(0x9480); // SUB.L D0,D2
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("moveq\t#16,d0", StringSplitOptions.None).Length - 1);
		Assert.Contains("add.l\td0,d4", assembly, StringComparison.Ordinal);
		Assert.Contains("sub.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void BypassesLabeledTerminalReloadWhenFallthroughValueIsAlreadyInD0()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "other");
		assembler.EmitWord(0x7014); // MOVEQ #20,D0
		assembler.EmitWord(0x2F40); // MOVE.L D0,20(A7)
		assembler.EmitWord(20);
		assembler.Mark("return");
		assembler.EmitWord(0x202F); // MOVE.L 20(A7),D0
		assembler.EmitWord(20);
		assembler.EmitWord(0xDEFC); // ADDA.W #52,A7
		assembler.EmitWord(52);
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("other");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x2F40); // MOVE.L D0,20(A7)
		assembler.EmitWord(20);
		assembler.EmitBranch(M68kCondition.True, "return");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain(
			"moveq\t#20,d0\r\n\tmove.l\td0,20(a7)",
			assembly,
			StringComparison.Ordinal);
		Assert.Contains("moveq\t#20,d0\r\n\tbra.", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t20(a7),d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,20(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ForwardsStackReloadIntoAbiReturnRegisterBeforeRts()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F41); // MOVE.L D1,16(A7)
		assembler.EmitWord(16);
		assembler.EmitWord(0x202F); // MOVE.L 16(A7),D0
		assembler.EmitWord(16);
		assembler.EmitWord(0xDEFC); // ADDA.W #20,A7
		assembler.EmitWord(20);
		assembler.EmitWord(0x4E75); // RTS observes D0

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t16(a7),d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsZeroReloadWhenItsConditionCodesAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x2080); // MOVE.L D0,(A0)
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(2, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void KeepsZeroReloadAfterCallClobbersReturnRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0: call argument
		assembler.EmitWord(0x4EAE); // JSR -36(A6): returns an unknown value in D0
		assembler.EmitWord(unchecked((ushort)-36));
		assembler.EmitWord(0x7000); // MOVEQ #0,D0: comparison operand
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("jsr\t-36(a6)", assembly, StringComparison.Ordinal);
		Assert.Equal(2, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RemovesStackReloadWhenStoreAlreadyPublishedSameValueAndFlags()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F40); // MOVE.L D0,16(A7)
		assembler.EmitWord(16);
		assembler.EmitWord(0x202F); // MOVE.L 16(A7),D0
		assembler.EmitWord(16);
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,16(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t16(a7),d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ForwardsLiveStackReloadFromStoredRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F40); // MOVE.L D0,16(A7)
		assembler.EmitWord(16);
		assembler.EmitWord(0x222F); // MOVE.L 16(A7),D1
		assembler.EmitWord(16);
		assembler.EmitWord(0x70FF); // MOVEQ #-1,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.NotEqual, "different");
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("different");
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t16(a7),d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesDeadStackStoreImmediatelyOverwrittenByClear()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F48); // MOVE.L A0,4(A7)
		assembler.EmitWord(4);
		assembler.EmitWord(0x42AF); // CLR.L 4(A7)
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("move.l\ta0,4(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("clr.l\t4(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ZeroRegisterStackPushClrRewriteIsCpuSpecific()
	{
		var m68000 = CreateAssembler();
		m68000.OptimizeForCpu(M68kCpuTarget.M68000);
		var m68000Assembly = m68000.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d1", m68000Assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,-(a7)", m68000Assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("clr.l\t-(a7)", m68000Assembly, StringComparison.Ordinal);

		var m68020 = CreateAssembler();
		m68020.OptimizeForCpu(M68kCpuTarget.M68020);
		var m68020Assembly = m68020.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("clr.l\t-(a7)", m68020Assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#0,d1", m68020Assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td1,-(a7)", m68020Assembly, StringComparison.Ordinal);

		static M68kAssembler CreateAssembler()
		{
			var assembler = new M68kAssembler();
			assembler.EmitWord(0x7200); // MOVEQ #0,D1
			assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
			assembler.EmitWord(0x7201); // MOVEQ #1,D1
			assembler.EmitWord(0x2001); // MOVE.L D1,D0
			assembler.EmitWord(0x4E75); // RTS
			return assembler;
		}
	}

	[Fact]
	public void AnalysisAnchorsTrackRewritesWithoutBlockingThem()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
		assembler.MarkAnalysisAnchor("block:end");
		assembler.EmitWord(0x7201); // MOVEQ #1,D1
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("clr.l\t-(a7)", assembly, StringComparison.Ordinal);
		Assert.Equal(2, linked.AnalysisAnchors["block:end"]);
	}

	[Theory]
	[InlineData(224, true)]
	[InlineData(222, false)]
	[InlineData(290, false)]
	public void Mc68020UsesCompactImmediateStackPushOnlyNearCacheLimit(
		int loopBytes,
		bool expectCompact)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("loop");
		assembler.EmitWord(0x2F3C); // MOVE.L #42,-(A7)
		assembler.EmitLong(42);
		assembler.EmitWord(0x4A80); // TST.L D0; overwrite conditions
		assembler.EmitBranch(M68kCondition.NotEqual, "loop:exit");
		for (var bytes = 12; bytes < loopBytes; bytes += 2)
		{
			assembler.EmitWord(0x4E71); // NOP
		}
		assembler.MarkAnalysisAnchor("loop:end");
		assembler.Mark("loop:exit");
		assembler.EmitWord(0x4E75); // RTS; establish exit liveness
		var layouts = new[]
		{
			new M68kLoopLayout(
				"size-first",
				0,
				"loop",
				new[] { new M68kLoopBlockLayout("loop", "loop:end") })
		};
		var sizeFirstLoops = M68kLoopFootprintAnalysis.SelectSizeFirstLayouts(
			layouts,
			assembler.Labels,
			assembler.AnalysisAnchors);

		assembler.OptimizeForCpu(
			M68kCpuTarget.M68020,
			sizeFirstLoops: sizeFirstLoops);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(
			expectCompact ? (ushort)0x4878 : (ushort)0x2F3C,
			BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
	}

	[Fact]
	public void Mc68020KeepsImmediateStackPushWhenItsFlagsAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("loop");
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x2F3C); // MOVE.L #42,-(A7); flags feed BEQ
		assembler.EmitLong(42);
		assembler.EmitBranch(M68kCondition.Equal, "loop:exit");
		for (var bytes = 12; bytes < 224; bytes += 2)
		{
			assembler.EmitWord(0x4E71); // NOP
		}
		assembler.MarkAnalysisAnchor("loop:end");
		assembler.Mark("loop:exit");
		assembler.EmitWord(0x4E75); // RTS
		var layouts = new[]
		{
			new M68kLoopLayout(
				"size-first-flags",
				0,
				"loop",
				new[] { new M68kLoopBlockLayout("loop", "loop:end") })
		};
		var sizeFirstLoops = M68kLoopFootprintAnalysis.SelectSizeFirstLayouts(
			layouts,
			assembler.Labels,
			assembler.AnalysisAnchors);

		assembler.OptimizeForCpu(
			M68kCpuTarget.M68020,
			sizeFirstLoops: sizeFirstLoops);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(
			0x2F3C,
			BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
	}

	[Theory]
	[InlineData(224, true)]
	[InlineData(222, false)]
	[InlineData(290, false)]
	public void Mc68020SynthesizesSwapConstantOnlyNearCacheLimit(
		int loopBytes,
		bool expectSynthesis)
	{
		var assembler = CreateConstantLoop(0x00010000, loopBytes, flagsAreLive: false);
		var sizeFirstLoops = SelectSizeFirstLoop(assembler);

		assembler.OptimizeForCpu(
			M68kCpuTarget.M68020,
			sizeFirstLoops: sizeFirstLoops);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(
			expectSynthesis ? (ushort)0x7001 : (ushort)0x203C,
			BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		if (expectSynthesis)
		{
			Assert.Equal(
				0x4840,
				BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
		}
	}

	[Fact]
	public void Mc68020SynthesizesRotateConstantWithCycleCostedAlternative()
	{
		var assembler = CreateConstantLoop(0x01000000, 224, flagsAreLive: false);
		var sizeFirstLoops = SelectSizeFirstLoop(assembler);

		assembler.OptimizeForCpu(
			M68kCpuTarget.M68020,
			sizeFirstLoops: sizeFirstLoops);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(0x7001, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(0xE098, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
		Assert.Contains(
			"ror.l\t#8,d0",
			assembler.RenderAssembly(M68kCpuTarget.M68020),
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68040)]
	public void OtherCpusDoNotUseMc68020SizeFirstConstantSynthesis(M68kCpuTarget cpu)
	{
		var assembler = CreateConstantLoop(0x00010000, 224, flagsAreLive: false);
		var sizeFirstLoops = SelectSizeFirstLoop(assembler);

		assembler.OptimizeForCpu(cpu, sizeFirstLoops: sizeFirstLoops);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(0x203C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
	}

	[Fact]
	public void RotateConstantSynthesisPreservesLiveCarrySemantics()
	{
		// MOVE clears C, but MOVEQ #1 / ROR.L #1 leaves C set.
		var assembler = CreateConstantLoop(0x80000000, 224, flagsAreLive: true);
		var sizeFirstLoops = SelectSizeFirstLoop(assembler);

		assembler.OptimizeForCpu(
			M68kCpuTarget.M68020,
			sizeFirstLoops: sizeFirstLoops);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(0x203C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
	}

	private static M68kAssembler CreateConstantLoop(
		uint value,
		int loopBytes,
		bool flagsAreLive)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("constant-loop");
		assembler.EmitWord(0x203C); // MOVE.L #value,D0
		assembler.EmitLong(value);
		if (!flagsAreLive)
		{
			assembler.EmitWord(0x4A81); // TST.L D1; overwrite conditions
		}
		assembler.EmitBranch(
			flagsAreLive ? M68kCondition.CarrySet : M68kCondition.NotEqual,
			"constant-loop:exit");
		var emittedBytes = flagsAreLive ? 10 : 12;
		for (var bytes = emittedBytes; bytes < loopBytes; bytes += 2)
		{
			assembler.EmitWord(0x4E71); // NOP
		}
		assembler.MarkAnalysisAnchor("constant-loop:end");
		assembler.Mark("constant-loop:exit");
		assembler.EmitWord(0x4E75); // RTS; D0 is live as the return value
		return assembler;
	}

	private static IReadOnlyList<M68kLoopLayout> SelectSizeFirstLoop(
		M68kAssembler assembler)
	{
		var layouts = new[]
		{
			new M68kLoopLayout(
				"constant-size-first",
				0,
				"constant-loop",
				new[]
				{
					new M68kLoopBlockLayout("constant-loop", "constant-loop:end")
				})
		};
		return M68kLoopFootprintAnalysis.SelectSizeFirstLayouts(
			layouts,
			assembler.Labels,
			assembler.AnalysisAnchors);
	}

	[Fact]
	public void ForwardsRegisterStoresAndFrameLoadsAcrossTheEvaluationStack()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2F5F); // MOVE.L (A7)+,40(A7)
		assembler.EmitWord(40);
		assembler.EmitWord(0x2F2F); // MOVE.L 44(A7),-(A7)
		assembler.EmitWord(44);
		assembler.EmitWord(0x221F); // MOVE.L (A7)+,D1
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,40(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t44(a7),d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7)+", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesDeadRegisterForMemoryToMemoryEvaluationStackTransfer()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F2F); // MOVE.L 44(A7),-(A7)
		assembler.EmitWord(44);
		assembler.EmitWord(0x2F5F); // MOVE.L (A7)+,40(A7)
		assembler.EmitWord(40);
		assembler.EmitWord(0x7200); // D1 is dead during the transfer.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t44(a7),d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,40(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t44(a7),-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7)+,40(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRepeatedRuntimeFrameClear()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x42AD); // CLR.L 12(A5)
		assembler.EmitWord(12);
		assembler.EmitWord(0x42AD); // CLR.L 12(A5)
		assembler.EmitWord(12);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			1,
			assembly.Split("clr.l\t12(a5)", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RemovesRuntimeFrameClearAlreadyPerformedThroughStackPointer()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x42AF); // CLR.L 12(A7)
		assembler.EmitWord(12);
		assembler.EmitWord(0x42AF); // CLR.L 16(A7)
		assembler.EmitWord(16);
		assembler.EmitWord(0x2A4F); // MOVEA.L A7,A5
		assembler.EmitWord(0x42AD); // CLR.L 12(A5)
		assembler.EmitWord(12);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("clr.l\t12(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\ta7,a5", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("clr.l\t12(a5)", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	public void HoistsRequiredZeroAcrossOwnedStackClears(M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x42AF); // CLR.L 48(A7)
		assembler.EmitWord(48);
		assembler.EmitWord(0x42AF); // CLR.L 44(A7)
		assembler.EmitWord(44);
		assembler.EmitWord(0x204A); // MOVEA.L A2,A0
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitJsr("raise", external: true);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains(
			"moveq\t#0,d0\r\n" +
			"\tmove.l\td0,48(a7)\r\n" +
			"\tmove.l\td0,44(a7)\r\n" +
			"\tmovea.l\ta2,a0",
			assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("clr.l\t48(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("clr.l\t44(a7)", assembly, StringComparison.Ordinal);
		Assert.Equal(1, assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void ReusesKnownZeroRegisterForFollowingFrameStores()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4280); // CLR.L D0
		assembler.EmitWord(0x1F40); // MOVE.B D0,27(A7)
		assembler.EmitWord(27);
		assembler.EmitWord(0x2F7C); // MOVE.L #0,28(A7)
		assembler.EmitLong(0);
		assembler.EmitWord(28);
		assembler.EmitWord(0x2F7C); // MOVE.L #0,32(A7)
		assembler.EmitLong(0);
		assembler.EmitWord(32);
		assembler.EmitWord(0x2B7C); // MOVE.L #0,36(A5)
		assembler.EmitLong(0);
		assembler.EmitWord(36);
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("clr.l\td0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\td0,27(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,28(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,32(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,36(a5)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t#$00000000", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void StopsReusingZeroRegisterAfterRedefinition()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4280); // CLR.L D0
		assembler.EmitWord(0x2F7C); // MOVE.L #0,8(A7)
		assembler.EmitLong(0);
		assembler.EmitWord(8);
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x2F7C); // MOVE.L #0,12(A7)
		assembler.EmitLong(0);
		assembler.EmitWord(12);
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,8(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t#$00000000,12(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsSmallAddressImmediateOnM68000()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F07); // MOVE.L D7,-(A7)
		assembler.EmitWord(0x227C); // MOVEA.L #7,A1
		assembler.EmitLong(7);
		assembler.EmitWord(0xD1C9); // ADDA.L A1,A0
		assembler.EmitWord(0x2E1F); // MOVE.L (A7)+,D7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x327C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
		Assert.DoesNotContain("movea.l\t#$00000007,a1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void AdjustsKnownAddressConstantWithQuickArithmetic()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x247C); // MOVEA.L #40,A2
		assembler.EmitLong(40);
		assembler.EmitWord(0x247C); // MOVEA.L #47,A2
		assembler.EmitLong(47);
		assembler.EmitWord(0x4E92); // JSR (A2)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("addq.l\t#7,a2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\t#$0000002F,a2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReusesKnownAddressRegisterForAddressImmediate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x247C); // MOVEA.L #300,A2
		assembler.EmitLong(300);
		assembler.EmitWord(0x227C); // MOVEA.L #300,A1
		assembler.EmitLong(300);
		assembler.EmitWord(0x4E91); // JSR (A1)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("movea.l\ta2,a1", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.w\t", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("#$0000012C", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsSmallAddressImmediateDirectOnM68020()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x227C); // MOVEA.L #7,A1
		assembler.EmitLong(7);
		assembler.EmitWord(0x4E91); // JSR (A1)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x327C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.DoesNotContain("movea.l\t#$00000007,a1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void DoesNotTreatRelocatableAddressesAsLiteralZero()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x247C); // MOVEA.L #first,A2
		assembler.EmitAddress("first");
		assembler.EmitWord(0x227C); // MOVEA.L #second,A1
		assembler.EmitAddress("second");
		assembler.EmitWord(0x4E91); // JSR (A1)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			2,
			assembly.Split("movea.l\t#", StringSplitOptions.None).Length - 1);
		Assert.Contains("first", assembly, StringComparison.Ordinal);
		Assert.Contains("second", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta2,a1", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(-32769, false)]
	[InlineData(-32768, true)]
	[InlineData(32767, true)]
	[InlineData(32768, false)]
	public void NarrowsSignedWordAddressImmediatesAcrossInstructionFamilies(
		int value,
		bool expectNarrow)
	{
		AssertOpcode(0x227C, 0x327C); // MOVEA.L/W #imm,A1
		AssertOpcode(0xB3FC, 0xB2FC); // CMPA.L/W #imm,A1
		AssertOpcode(0xD3FC, 0xD2FC); // ADDA.L/W #imm,A1
		AssertOpcode(0x93FC, 0x92FC); // SUBA.L/W #imm,A1

		void AssertOpcode(ushort longOpcode, ushort wordOpcode)
		{
			var assembler = new M68kAssembler();
			assembler.EmitWord(longOpcode);
			assembler.EmitLong(unchecked((uint)value));
			if ((longOpcode & 0xF1FF) == 0xB1FC)
			{
				assembler.EmitBranch(M68kCondition.Equal, "used");
				assembler.EmitWord(0x4E75); // unequal return
			}
			else
			{
				assembler.EmitWord(0x4ED1); // JMP (A1)
			}
			assembler.Mark("used");
			assembler.EmitWord(0x4E75); // RTS

			assembler.OptimizeForCpu(M68kCpuTarget.M68020);
			var linked = assembler.Link(0, new Dictionary<string, uint>());

			Assert.Equal(
				expectNarrow ? wordOpcode : longOpcode,
				BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		}
	}

	[Theory]
	[InlineData(0x227C)] // MOVEA.L #target,A1
	[InlineData(0xB3FC)] // CMPA.L #target,A1
	[InlineData(0xD3FC)] // ADDA.L #target,A1
	[InlineData(0x93FC)] // SUBA.L #target,A1
	[InlineData(0x2F3C)] // MOVE.L #target,-(A7)
	public void DoesNotNarrowRelocatableSignedWordImmediates(int opcodeValue)
	{
		var opcode = (ushort)opcodeValue;
		var assembler = new M68kAssembler();
		assembler.EmitWord(opcode);
		assembler.EmitAddress("target");
		if ((opcode & 0xF1FF) == 0xB1FC)
		{
			assembler.EmitBranch(M68kCondition.Equal, "target");
			assembler.EmitWord(0x4E75); // unequal return
		}
		else if (opcode == 0x2F3C)
		{
			assembler.EmitJsr("consume", external: false);
		}
		else
		{
			assembler.EmitWord(0x4ED1); // JMP (A1)
		}
		assembler.Mark("target");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("consume");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(opcode, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
	}

	[Theory]
	[InlineData(0, 300, true)]
	[InlineData(-1, -300, true)]
	[InlineData(0, -300, false)]
	public void NarrowsDataImmediateWhenExistingUpperWordMatches(
		int prior,
		int value,
		bool expectNarrow)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord((ushort)(0x7000 | (byte)prior)); // MOVEQ #prior,D0
		assembler.EmitWord(0x203C); // MOVE.L #value,D0
		assembler.EmitLong(unchecked((uint)value));
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Equal(
			expectNarrow,
			assembly.Contains("move.w\t#", StringComparison.Ordinal));
		Assert.Equal(
			!expectNarrow,
			assembly.Contains("move.l\t#", StringComparison.Ordinal));
	}

	[Fact]
	public void CanonicalizesZeroDisplacementEffectiveAddresses()
	{
		AssertCanonicalized(
			new ushort[] { 0x2011, 0x4E75 },
			0x2029, 0x0000, 0x4E75); // MOVE.L 0(A1),D0
		AssertCanonicalized(
			new ushort[] { 0x2280, 0x4E75 },
			0x2340, 0x0000, 0x4E75); // MOVE.L D0,0(A1)
		AssertCanonicalized(
			new ushort[] { 0x2491, 0x4E75 },
			0x2569, 0x0000, 0x0000, 0x4E75); // MOVE.L 0(A1),0(A2)
		AssertCanonicalized(
			new ushort[] { 0x22BC, 0x1234, 0x5678, 0x4E75 },
			0x237C, 0x1234, 0x5678, 0x0000, 0x4E75); // MOVE.L #imm,0(A1)
		AssertCanonicalized(
			new ushort[] { 0x45D1, 0x4ED2 },
			0x45E9, 0x0000, 0x4ED2); // LEA 0(A1),A2
		AssertCanonicalized(
			new ushort[] { 0x4291, 0x4E75 },
			0x42A9, 0x0000, 0x4E75); // CLR.L 0(A1)
		AssertCanonicalized(
			new ushort[] { 0x5291, 0x4E75 },
			0x52A9, 0x0000, 0x4E75); // ADDQ.L #1,0(A1)
		AssertCanonicalized(
			new ushort[] { 0x4E91, 0x7001, 0x4E75 },
			0x4EA9, 0x0000, 0x7001, 0x4E75); // JSR 0(A1)
		AssertCanonicalized(
			new ushort[] { 0x48D1, 0x0003, 0x4E75 },
			0x48E9, 0x0003, 0x0000, 0x4E75); // MOVEM.L D0-D1,0(A1)
		AssertCanonicalized(
			new ushort[] { 0xF217, 0x5400, 0x4E75 },
			0xF22F, 0x5400, 0x0000, 0x4E75); // FMOVE.D 0(A7),FP0

		static void AssertCanonicalized(
			IReadOnlyList<ushort> expected,
			params ushort[] emitted)
		{
			var assembler = new M68kAssembler();
			foreach (var word in emitted)
			{
				assembler.EmitWord(word);
			}

			assembler.OptimizeForCpu(M68kCpuTarget.M68020);
			var linked = assembler.Link(0, new Dictionary<string, uint>());
			var actual = Enumerable.Range(0, linked.Bytes.Length / 2)
				.Select(index => BinaryPrimitives.ReadUInt16BigEndian(
					linked.Bytes.AsSpan(index * 2)))
				.ToArray();

			Assert.Equal(expected, actual);
		}
	}

	[Fact]
	public void KeepsNonZeroDisplacementEffectiveAddress()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2340); // MOVE.L D0,2(A1)
		assembler.EmitWord(2);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Equal(0x2340, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(2, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
	}

	[Fact]
	public void ReusesKnownDataConstantForAddressImmediate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x76FC); // MOVEQ #-4,D3
		assembler.EmitWord(0x227C); // MOVEA.L #-4,A1
		assembler.EmitLong(unchecked((uint)-4));
		assembler.EmitWord(0x4E91); // JSR (A1)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("movea.l\td3,a1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\t#$FFFFFFFC,a1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void NarrowsSmallAddressImmediateWhenConditionCodesAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x227C); // MOVEA.L #7,A1
		assembler.EmitLong(7);
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7201); // MOVEQ #1,D1
		assembler.Mark("equal");
		assembler.EmitWord(0x4E91); // JSR (A1)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var linked = assembler.Link(0, new Dictionary<string, uint>());
		Assert.Equal(0x327C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(2)));
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, true)]
	[InlineData(M68kCpuTarget.M68020, true)]
	[InlineData(M68kCpuTarget.M68040, false)]
	[InlineData(M68kCpuTarget.M68060, false)]
	public void MaterializesBoundaryDataConstantAccordingToCpuTiming(
		M68kCpuTarget cpu,
		bool expectQuickSequence)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x243C); // MOVE.L #135,D2
		assembler.EmitLong(135);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		if (expectQuickSequence)
		{
			Assert.Contains(
				"moveq\t#127,d2\r\n\taddq.w\t#8,d2",
				assembly,
				StringComparison.Ordinal);
			Assert.DoesNotContain("move.l\t#$00000087,d2", assembly, StringComparison.Ordinal);
		}
		else
		{
			Assert.Contains("move.l\t#$00000087,d2", assembly, StringComparison.Ordinal);
			Assert.DoesNotContain("moveq\t#127,d2", assembly, StringComparison.Ordinal);
		}
	}

	[Fact]
	public void RendersM68060AssemblerDirective()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68060);

		Assert.StartsWith("\tmc68060", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void MaterializesNegativeBoundaryDataConstantWithSubtractQuick()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x243C); // MOVE.L #-136,D2
		assembler.EmitLong(unchecked((uint)-136));
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains(
			"moveq\t#-128,d2\r\n\tsubq.w\t#8,d2",
			assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void AdjustsKnownDataRegisterConstantWithQuickArithmetic(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7478); // MOVEQ #120,D2
		assembler.EmitWord(0x243C); // MOVE.L #128,D2
		assembler.EmitLong(128);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("addq.w\t#8,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t#$00000080,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesDataImmediateWhenExtendConditionIsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x243C); // MOVE.L #135,D2
		assembler.EmitLong(135);
		assembler.EmitWord(0xD181); // ADDX.L D1,D0
		assembler.EmitWord(0x4E75); // RTS
		var dataflow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(dataflow.TryGetFacts(0, out var facts));
		Assert.NotEqual(
			M68kConditionCodeSet.None,
			facts.LiveConditionsAfter & M68kConditionCodeSet.Extend);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#$00000087,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void DoesNotTreatRelocatableDataImmediateAsLiteralZero()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7400); // MOVEQ #0,D2
		assembler.EmitWord(0x243C); // MOVE.L #symbol,D2
		assembler.EmitAddress("symbol");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#", assembly, StringComparison.Ordinal);
		Assert.Contains("symbol", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesZeroRegisterStackPushWhenRegisterRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,-(a7)", assembly, StringComparison.Ordinal);
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
	public void DistributesMoveQuickAcrossBranchAndUsesMoveFlags()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4A81); // TST.L D1
		assembler.EmitBranch(M68kCondition.LessThan, "negative");
		assembler.EmitWord(0x102F); // MOVE.B 8(A7),D0
		assembler.EmitWord(8);
		assembler.EmitBranch(M68kCondition.True, "done");
		assembler.Mark("negative");
		assembler.EmitWord(0x102F); // MOVE.B 8(A7),D0
		assembler.EmitWord(8);
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td1", assembly, StringComparison.Ordinal);
		Assert.Contains("blt", assembly, StringComparison.Ordinal);
		Assert.Equal(
			2,
			assembly.Split("moveq\t#0,d0", StringSplitOptions.None).Length - 1);
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
	public void RemovesImmediatelyDiscardedStackPush()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
		Assert.Contains("rts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesDiscardedPushBeforeFlagSettingFramePush()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x2F2F); // MOVE.L 52(A7),-(A7)
		assembler.EmitWord(52);
		assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		assembler.EmitBranch(M68kCondition.NotEqual, "done");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain(
			"move.l\td0,-(a7)\r\n\taddq.l\t#4,a7",
			assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDiscardedStackPushWhenItsFlagsAreConsumed()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
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
	public void ForwardsImmediateStackTemporaryToDataRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F3C); // MOVE.L #$12345678,-(A7)
		assembler.EmitLong(0x12345678);
		assembler.EmitWord(0x2217); // MOVE.L (A7),D1
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#$12345678,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ForwardsImmediateStackTemporaryAcrossRebasedFrameClear()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F3C); // MOVE.L #$12345678,-(A7)
		assembler.EmitLong(0x12345678);
		assembler.EmitWord(0x42AF); // CLR.L 8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#$12345678,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("clr.l\t4(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RewritesStackPreservationToFinalSlots()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2E80); // MOVE.L D0,(A7)
		assembler.EmitWord(0x7002); // MOVEQ #2,D0
		assembler.EmitWord(0x2F6F); // MOVE.L (A7),4(A7)
		assembler.EmitWord(0);
		assembler.EmitWord(4);
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x4E75); // RTS
		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,4(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#2,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td1,4(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),4(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesDirectFinalStoresForM68040StackPreservation()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2E80); // MOVE.L D0,(A7)
		assembler.EmitWord(0x7002); // MOVEQ #2,D0
		assembler.EmitWord(0x2F6F); // MOVE.L (A7),4(A7)
		assembler.EmitWord(0);
		assembler.EmitWord(4);
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68040);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68040);
		Assert.Contains("move.l\td0,4(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td1,4(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),4(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesScratchRegisterForM68060StackPreservation()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2E80); // MOVE.L D0,(A7)
		assembler.EmitWord(0x7002); // MOVEQ #2,D0
		assembler.EmitWord(0x2F6F); // MOVE.L (A7),4(A7)
		assembler.EmitWord(0);
		assembler.EmitWord(4);
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68060);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68060);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,4(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,4(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsAddressRegisterRelativeTailReturnToJump()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4EAE); // JSR -954(A6)
		assembler.EmitWord(unchecked((ushort)-954));
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("jmp\t-954(a6)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("jsr\t-954(a6)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("\trts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsAddressRegisterRelativeTailReturnWhenReturnIsLabelled()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4EAE); // JSR -954(A6)
		assembler.EmitWord(unchecked((ushort)-954));
		assembler.Mark("return");
		assembler.EmitWord(0x4E75); // RTS
		assembler.EmitBranch(M68kCondition.True, "return");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("jsr\t-954(a6)", assembly, StringComparison.Ordinal);
		Assert.Contains("return:", assembly, StringComparison.Ordinal);
		Assert.Contains("\trts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void AssemblyUsesReferencedMethodEntryWhenItAliasesPreviousMethodEnd()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBsr("method:z_next");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:a_previous:end");
		assembler.Mark("method:z_next");
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("bsr.s\tC68K_method_003Az_next", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain(
			"bsr.s\tC68K_method_003Aa_previous_003Aend",
			assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsRegisterSpillWhenExpressionReadsStackPointer()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		assembler.EmitWord(0x200F); // MOVE.L A7,D0
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\ta7,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsRegisterSpillWhenExpressionReadsMoveFlags()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2017); // MOVE.L (A7),D0
		assembler.EmitWord(0x56C1); // SNE D1
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("sne\td1", assembly, StringComparison.Ordinal);
		Assert.Contains("addq.l\t#4,a7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsDeadAddressRegisterRoundTripIntoDataMove()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		assembler.EmitWord(0x2E08); // MOVE.L A0,D7
		assembler.EmitWord(0x2041); // MOVEA.L D1,A0; A0 is dead after the round trip
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\td0,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\ta0,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsMoveaMemoryLoadAndMoveIntoDirectDataLoad()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x226E); // MOVEA.L 20(A6),A1
		assembler.EmitWord(20);
		assembler.EmitWord(0x2009); // MOVE.L A1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t20(a6),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\t20(a6),a1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\ta1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsMoveaMemoryLoadAndStoreIntoDirectMemoryTransfer()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2057); // MOVEA.L (A7),A0
		assembler.EmitWord(0x2F48); // MOVE.L A0,36(A7)
		assembler.EmitWord(36);
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0; kills the temporary
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t(a7),36(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\t(a7),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\ta0,36(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveaMemoryTransferWhenDestinationUsesLoadedAddress()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2057); // MOVEA.L (A7),A0
		assembler.EmitWord(0x2148); // MOVE.L A0,4(A0)
		assembler.EmitWord(4);
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0; kills the temporary
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\t(a7),a0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\ta0,4(a0)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a7),4(a0)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesCopiedAddressRegisterDirectlyAsMemoryBase()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x204B); // MOVEA.L A3,A0
		assembler.EmitWord(0x2228); // MOVE.L 12(A0),D1
		assembler.EmitWord(12);
		assembler.EmitWord(0x4E75);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t12(a3),d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta3,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t12(a0),d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsLeaIntoDirectMemoryLoad()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x41EF); // LEA 12(A7),A0
		assembler.EmitWord(12);
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t12(a7),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("lea\t12(a7),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t(a0),d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsLeaWhenItsAddressRegisterRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x41EF); // LEA 12(A7),A0
		assembler.EmitWord(12);
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0; keeps A0 live and overwrites D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("lea\t12(a7),a0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t(a0),d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsMoveQuickIntoAddQuickWhenTemporaryIsDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7004); // MOVEQ #4,D0
		assembler.EmitWord(0xD480); // ADD.L D0,D2
		assembler.EmitWord(0x7001); // D0 is overwritten before return
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.l\t#4,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#4,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsMoveQuickIntoAddQuickBeforeFullRegisterOverwrite()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("block"); // The replacement remains at this block entry.
		assembler.EmitWord(0x7004); // MOVEQ #4,D0
		assembler.EmitWord(0xD480); // ADD.L D0,D2
		assembler.EmitWord(0x2003); // MOVE.L D3,D0 overwrites the temporary
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.l\t#4,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#4,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsMoveQuickIntoAddQuickWhenOverwriteFeedsLaterArithmetic()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7004); // MOVEQ #4,D0
		assembler.EmitWord(0xD480); // ADD.L D0,D2
		assembler.EmitWord(0x2003); // MOVE.L D3,D0 overwrites the temporary
		assembler.EmitWord(0xE288); // LSR.L #1,D0
		assembler.EmitWord(0x2600); // MOVE.L D0,D3
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.l\t#4,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveQuickWhenItsRegisterRemainsLiveAfterAdd()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7204); // MOVEQ #4,D1
		assembler.EmitWord(0xD481); // ADD.L D1,D2
		assembler.EmitWord(0x2801); // MOVE.L D1,D4; keeps D1 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#4,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td1,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l\t#4,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void DoesNotFoldAddWhenMoveQuickAndAddShareTheDestination()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7404); // MOVEQ #4,D2
		assembler.EmitWord(0xD482); // ADD.L D2,D2
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("addq.l\t#4,d2", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#4,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsMoveQuickIntoSubQuickWhenTemporaryIsDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7004); // MOVEQ #4,D0
		assembler.EmitWord(0x9880); // SUB.L D0,D4
		assembler.EmitWord(0x7001); // D0 is overwritten before return
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#4,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#4,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("sub.l\td0,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void EncodesEightAsTheSubQuickMaximum()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7008); // MOVEQ #8,D0
		assembler.EmitWord(0x9880); // SUB.L D0,D4
		assembler.EmitWord(0x7001); // D0 is overwritten before return
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#8,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveQuickWhenItsRegisterRemainsLiveAfterSub()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7204); // MOVEQ #4,D1
		assembler.EmitWord(0x9481); // SUB.L D1,D2
		assembler.EmitWord(0x2801); // MOVE.L D1,D4; keeps D1 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#4,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("sub.l\td1,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l\t#4,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void DoesNotFoldSubWhenMoveQuickAndSubShareTheDestination()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7404); // MOVEQ #4,D2
		assembler.EmitWord(0x9482); // SUB.L D2,D2
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("subq.l\t#4,d2", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#4,d2", assembly, StringComparison.Ordinal);
		Assert.Contains("sub.l\td2,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesAdjacentDuplicateAndImmediateMasks()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$000000FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x0280); // ANDI.L #$000000FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("andi.l\t#$000000FF,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void KeepsNonRedundantAndImmediateMasks()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$000000F0,D0
		assembler.EmitLong(0x000000F0);
		assembler.EmitWord(0x0280); // ANDI.L #$0000000F,D0
		assembler.EmitLong(0x0000000F);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("andi.l\t#$000000F0,d0", StringSplitOptions.None).Length - 1);
		Assert.Equal(1, assembly.Split("andi.l\t#$0000000F,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RemovesRepeatedMaskAcrossUsesOfUntouchedRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xDE44); // ADD.W D4,D7; D4 is only read
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD284); // ADD.L D4,D1; replaces mask flags
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			1,
			assembly.Split("andi.l\t#$000000FF,d4", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RemovesRepeatedMaskAtTargetWhenEveryPathIsAlreadyMasked()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitBranch(M68kCondition.Equal, "masked");
		assembler.EmitWord(0x2200); // MOVE.L D0,D1; leaves D4 untouched
		assembler.Mark("masked");
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD284); // ADD.L D4,D1; replaces mask flags
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			1,
			assembly.Split("andi.l\t#$000000FF,d4", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void KeepsTargetMaskWhenAnIncomingPathIsNotMasked()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "needs-mask");
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4 on only one path
		assembler.EmitLong(0x000000FF);
		assembler.Mark("needs-mask");
		assembler.EmitWord(0x0284); // Required for the unmasked incoming path
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD284); // ADD.L D4,D1; replaces mask flags
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$000000FF,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRepeatedMaskAcrossLoopThatLeavesRegisterUntouched()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x7601); // MOVEQ #1,D3
		assembler.Mark("loop");
		assembler.EmitWord(0xD280); // ADD.L D0,D1; leaves D4 untouched
		assembler.EmitDbra(3, "loop");
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x700A); // MOVEQ #10,D0; replaces mask flags
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			1,
			assembly.Split("andi.l\t#$000000FF,d4", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RemovesWordMaskWhenLoopOperationsPreserveZeroUpperWord()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7E00); // MOVEQ #0,D7
		assembler.EmitWord(0x7601); // MOVEQ #1,D3
		assembler.Mark("loop");
		assembler.EmitWord(0xEB5F); // ROL.W #5,D7
		assembler.EmitWord(0xDE44); // ADD.W D4,D7
		assembler.EmitWord(0x0287); // ANDI.L #$FFFF,D7
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitDbra(3, "loop");
		assembler.EmitWord(0xD087); // ADD.L D7,D0; observes zero extension
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("rol.w\t#5,d7", assembly, StringComparison.Ordinal);
		Assert.Contains("add.w\td4,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ByteStoreDoesNotDiscardUnrelatedZeroUpperWord()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7E00); // MOVEQ #0,D7
		assembler.EmitWord(0x1F41); // MOVE.B D1,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0xEB5F); // ROL.W #5,D7
		assembler.EmitWord(0xDE44); // ADD.W D4,D7
		assembler.EmitWord(0x0287); // ANDI.L #$FFFF,D7
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xD087); // ADD.L D7,D0; observes zero extension
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.b\td1,8(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReplacesDelayedByteLoadMaskWithZeroInitialization()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x162F); // MOVE.B 8(A7),D3
		assembler.EmitWord(8);
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x0283); // ANDI.L #$FF,D3
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x2083); // MOVE.L D3,(A0)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d3", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\t8(a7),d3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$000000FF,d3", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReplacesDelayedWordMoveMaskWithZeroInitialization()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3806); // MOVE.W D6,D4
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x0284); // ANDI.L #$FFFF,D4
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0x2084); // MOVE.L D4,(A0)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d4", assembly, StringComparison.Ordinal);
		Assert.Contains("move.w\td6,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ForwardsAdjacentDataRegisterCopyChain()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2004); // MOVE.L D4,D0
		assembler.EmitWord(0x2C00); // MOVE.L D0,D6
		assembler.EmitWord(0x2086); // MOVE.L D6,(A0)
		assembler.EmitWord(0x7000); // MOVEQ #0,D0; kills the temporary
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td4,d6", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td4,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d6", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReplacesDeadAddressRegisterNullCheckWithDataTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2600); // MOVE.L D0,D3
		assembler.EmitWord(0x2043); // MOVEA.L D3,A0
		assembler.EmitWord(0xB0FC); // CMPA.W #0,A0
		assembler.EmitWord(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "done");
		assembler.EmitWord(0x2281); // MOVE.L D1,(A1); keeps the branch meaningful
		assembler.Mark("done");
		assembler.EmitWord(0x2003); // MOVE.L D3,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("bne", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\td3,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpa.w\t#0,a0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReusesSourceFlagsForLiveAddressRegisterNullCheck()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2800); // MOVE.L D0,D4
		assembler.EmitWord(0x2244); // MOVEA.L D4,A1
		assembler.EmitWord(0xB2FC); // CMPA.W #0,A1
		assembler.EmitWord(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "nonnull");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonnull");
		assembler.EmitWord(0x2011); // MOVE.L (A1),D0; keeps A1 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\td4,a1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpa.w\t#0,a1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesDeadLabeledInstructionInControlFlow()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A82); // TST.L D2
		assembler.EmitBranch(M68kCondition.Equal, "block");
		assembler.EmitWord(0x2083); // MOVE.L D3,(A0)
		assembler.Mark("block");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0; dead
		assembler.EmitWord(0x1281); // MOVE.B D1,(A1); replaces flags
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("moveq\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("beq.s", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsWordMaskWhenUpperWordIsUnknown()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0xEB5F); // ROL.W #5,D7
		assembler.EmitWord(0xDE44); // ADD.W D4,D7
		assembler.EmitWord(0x0287); // ANDI.L #$FFFF,D7
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xD287); // ADD.L D7,D1; observes zero extension
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$0000FFFF,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesZeroUpperWordInCalleeSavedRegisterAcrossCall()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7E00); // MOVEQ #0,D7
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0xEB5F); // ROL.W #5,D7
		assembler.EmitWord(0xDE44); // ADD.W D4,D7
		assembler.EmitWord(0x0287); // ANDI.L #$FFFF,D7
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xD087); // ADD.L D7,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void InvalidatesCallerSavedRangeAcrossCall()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitJsr("helper", external: true);
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0 remains required
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xD280); // ADD.L D0,D1
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$0000FFFF,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesSubsumedAndImmediateMask()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$000000FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x0280); // ANDI.L #$0000007F,D0
		assembler.EmitLong(0x0000007F);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$000000FF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000007F,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesSubsumedOriImmediateMask()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0080); // ORI.L #$000000FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x0080); // ORI.L #$0000007F,D0
		assembler.EmitLong(0x0000007F);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("ori.l\t#$000000FF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("ori.l\t#$0000007F,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsNonRedundantOriImmediateMasks()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0080); // ORI.L #$F0F0F0F0,D0
		assembler.EmitLong(0xF0F0F0F0);
		assembler.EmitWord(0x0080); // ORI.L #$0F0F0F0F,D0
		assembler.EmitLong(0x0F0F0F0F);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("ori.l\t#$F0F0F0F0,d0", StringSplitOptions.None).Length - 1);
		Assert.Equal(1, assembly.Split("ori.l\t#$0F0F0F0F,d0", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void RemovesDataRegisterRoundTrip()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0x2007); // MOVE.L D7,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("move.l\td0,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td7,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("\trts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReplacesDataRegisterRoundTripWithTestWhenOnlyZeroFlagsAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0x2007); // MOVE.L D7,D0
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7201); // Keep the branch target beyond the fallthrough
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("tst.l\td0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td7,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesCopyBackWhileKeepingLivePreservationRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2400); // MOVE.L D0,D2
		assembler.EmitWord(0x2002); // MOVE.L D2,D0: D0 already has this value
		assembler.EmitWord(0xD282); // ADD.L D2,D1: keep D2 live
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td2,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDataPreservationRegisterLiveOnBranchTarget()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2400); // MOVE.L D0,D2
		assembler.EmitWord(0x2002); // MOVE.L D2,D0: D0 already has this value
		assembler.EmitBranch(M68kCondition.Equal, "uses_preserved");
		assembler.EmitWord(0x7400); // MOVEQ #0,D2 on fallthrough
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("uses_preserved");
		assembler.EmitWord(0xD282); // ADD.L D2,D1
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td2,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesAddressCopyBackWhileKeepingLivePreservationRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2448); // MOVEA.L A0,A2
		assembler.EmitWord(0x204A); // MOVEA.L A2,A0: A0 is unchanged
		assembler.EmitWord(0x2012); // MOVE.L (A2),D0: keep A2 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("movea.l\ta0,a2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta2,a0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t(a0),d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsAddressPreservationRegisterLiveOnBranchTarget()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitWord(0x2448); // MOVEA.L A0,A2
		assembler.EmitWord(0x204A); // MOVEA.L A2,A0: A0 is unchanged
		assembler.EmitBranch(M68kCondition.Equal, "uses_preserved");
		assembler.EmitWord(0x2449); // MOVEA.L A1,A2 on fallthrough
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("uses_preserved");
		assembler.EmitWord(0x2012); // MOVE.L (A2),D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\ta0,a2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta2,a0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t(a2),d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRedundantAddressRegisterReloadAcrossMemoryLoad()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2044); // MOVEA.L D4,A0
		assembler.EmitWord(0x2628); // MOVE.L 8(A0),D3
		assembler.EmitWord(8);
		assembler.EmitWord(0x2044); // MOVEA.L D4,A0: both registers are unchanged
		assembler.EmitWord(0x2428); // MOVE.L 12(A0),D2
		assembler.EmitWord(12);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			1,
			assembly.Split(
				"movea.l\td4,a0",
				StringSplitOptions.None).Length - 1);
		Assert.Contains("move.l\t8(a0),d3", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t12(a0),d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsAddressRegisterReloadWhenItsSourceChanges()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2044); // MOVEA.L D4,A0
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x7801); // MOVEQ #1,D4
		assembler.EmitWord(0x2044); // MOVEA.L D4,A0: D4 now has another value
		assembler.EmitWord(0x2210); // MOVE.L (A0),D1
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			2,
			assembly.Split(
				"movea.l\td4,a0",
				StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void ForwardsAbsoluteMemoryLoadDirectlyToCallAddressRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2039); // MOVE.L absolute.L,D0
		assembler.EmitAddress("dos-base");
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x2C7A); // MOVEA.L exec-base(PC),A6
		assembler.EmitPcRelativeWord("exec-base");
		assembler.EmitWord(0x2257); // MOVEA.L (A7),A1
		assembler.EmitWord(0x4EAE); // JSR -414(A6)
		assembler.EmitWord(unchecked((ushort)-414));
		assembler.EmitWord(0x4E75); // RTS
		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\tC68K_dos_002Dbase,a1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\tdos-base,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\t(a7),a1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RelaxesLocalAbsoluteLoadsToPcRelativeWithoutChangingDestinationRegisters()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2639); // MOVE.L value,D3
		assembler.EmitAddress("value");
		assembler.EmitWord(0x2A79); // MOVEA.L address,A5
		assembler.EmitAddress("address");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("value");
		assembler.EmitLong(0x1234_5678);
		assembler.Mark("address");
		assembler.EmitLong(0x9ABC_DEF0);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		var linked = assembler.Link(0, new Dictionary<string, uint>());

		Assert.Contains("move.l\tC68K_value(pc),d3", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\tC68K_address(pc),a5", assembly, StringComparison.Ordinal);
		Assert.Equal(0x263A, (linked.Bytes[0] << 8) | linked.Bytes[1]);
		Assert.Equal(0x2A7A, (linked.Bytes[4] << 8) | linked.Bytes[5]);
		Assert.Empty(linked.Relocations);
	}

	[Fact]
	public void RendersPcRelativeLeaWithItsSymbol()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x47FA); // LEA requester-text(PC),A3
		assembler.EmitPcRelativeWord("requester-text");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("requester-text");
		assembler.EmitByte(0);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains(
			"lea\tC68K_requester_002Dtext(pc),a3",
			assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$47FA", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ForwardsMoveQuickDirectlyToItsDataMoveDestination()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7014); // MOVEQ #20,D0
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0x7000); // D0 is overwritten before return
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#20,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#20,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void LaysOutTerminalFailureBlockAfterHotFallthrough()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "failure");
		assembler.EmitWord(0x7201); // MOVEQ #1,D1; hot path
		assembler.EmitBranch(M68kCondition.True, "join");
		assembler.Mark("failure");
		assembler.EmitWord(0x7014); // MOVEQ #20,D0; cold path
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.Mark("join");
		assembler.EmitWord(0x2007); // MOVE.L D7,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("next");
		assembler.EmitWord(0x7202); // Keep a suffix block after the reordered method
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("bra.w", assembly, StringComparison.Ordinal);
		Assert.Contains("beq.s", assembly, StringComparison.Ordinal);
		Assert.Contains("failure:", assembly, StringComparison.Ordinal);
		Assert.Equal(3, assembly.Split("rts", StringSplitOptions.None).Length - 1);
	}

	[Fact]
	public void ReplacesCompareAgainstMoveqZeroWithTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0xB480); // CMP.L D0,D2
		assembler.EmitWord(0x57C2); // SEQ D2
		assembler.EmitWord(0x7001); // D0 is overwritten before return
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("tst.l\td2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void MovesBlockEntryLabelWhenZeroComparePairIsAlwaysEnteredTogether()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "entry");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("entry");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0xB480); // CMP.L D0,D2
		assembler.EmitBranch(M68kCondition.Higher, "done");
		assembler.EmitWord(0x7001); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x7001); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("tst.l\td2", assembly, StringComparison.Ordinal);
		Assert.Contains("entry:", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveqWhenCompareHasASeparateIncomingEdge()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "entry");
		assembler.EmitBranch(M68kCondition.NotEqual, "compare");
		assembler.Mark("entry");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.Mark("compare");
		assembler.EmitWord(0xB480); // CMP.L D0,D2
		assembler.EmitBranch(M68kCondition.Higher, "done");
		assembler.EmitWord(0x7001); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("done");
		assembler.EmitWord(0x7001); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("cmp.l\td0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveqZeroWhenItsRegisterRemainsLiveAfterCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0xB680); // CMP.L D0,D3
		assembler.EmitWord(0x57C3); // SEQ D3
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("tst.l\td3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmp.l\td0,d3", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsNonZeroMoveqCompareIntoDestructiveQuickCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmp.l\td0,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesCompactRegisterCompareForM68020CopiedCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x7200); // D1 is dead after the compare
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x7200); // D1 is dead after the compare
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("moveq\t#3,d1\r\n\tcmp.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveQuickRegisterCompareOnM68020()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("moveq\t#3,d0\r\n\tcmp.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l\t#$00000003,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesDeadRegisterForSmallImmediateCompareOnM68020()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0C80); // CMPI.L #3,D0
		assembler.EmitLong(3);
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("moveq\t#3,d2\r\n\tcmp.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsSmallImmediateCompareOnM68020WhenNoRegisterIsDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0C80); // CMPI.L #3,D0
		assembler.EmitLong(3);
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x4E75); // RTS keeps ABI result/preserved registers live
		assembler.Mark("equal");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#3", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void UsesDestructiveQuickCompareWhenDestinationAndExtendAreDead(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0C80); // CMPI.L #8,D0
		assembler.EmitLong(8);
		assembler.EmitBranch(M68kCondition.Higher, "higher");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("higher");
		assembler.EmitWord(0x7001); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("subq.l\t#8,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsSmallImmediateCompareWhenDestinationRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0C80); // CMPI.L #3,D0
		assembler.EmitLong(3);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7200); // Keep a distinct fallthrough path
		assembler.EmitWord(0x4E75); // D0 is the live return value
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // D0 is the live return value

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDestructiveCompareOutsideQuickImmediateRange()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0C80); // CMPI.L #9,D0
		assembler.EmitLong(9);
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75);
		assembler.Mark("equal");
		assembler.EmitWord(0x7001); // D0 is dead after the compare
		assembler.EmitWord(0x4E75);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("cmpi.l\t#$00000009,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsSmallImmediateCompareWhenExtendRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0C80); // CMPI.L #3,D0; does not alter X
		assembler.EmitLong(3);
		assembler.EmitWord(0xD581); // ADDX.L D1,D2 consumes X
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesSubQuickForNegativeAddImmediateWhenCarryAndExtendAreDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x700A); // MOVEQ #10,D0
		assembler.EmitWord(0x0680); // ADDI.L #-3,D0
		assembler.EmitLong(unchecked((uint)-3));
		assembler.EmitWord(0x4E75); // D0 remains live

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.l", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData((int)M68kCondition.CarrySet)]
	[InlineData((int)M68kCondition.CarryClear)]
	public void KeepsNegativeAddImmediateWhenCarryRemainsLive(
		int condition)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0680); // ADDI.L #-1,D0
		assembler.EmitLong(uint.MaxValue);
		assembler.EmitBranch((M68kCondition)condition, "done");
		assembler.EmitWord(0x4E75);
		assembler.Mark("done");
		assembler.EmitWord(0x4E75);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.l\t#$FFFFFFFF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l\t#1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsNegativeAddImmediateWhenExtendRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0680); // ADDI.L #-1,D0
		assembler.EmitLong(uint.MaxValue);
		assembler.EmitWord(0xD581); // ADDX.L D1,D2 consumes X
		assembler.EmitWord(0x4E75);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.l\t#$FFFFFFFF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l\t#1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsNegativeAddOutsideQuickImmediateRange()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0680); // ADDI.L #-9,D0
		assembler.EmitLong(unchecked((uint)-9));
		assembler.EmitWord(0x4E75); // D0 remains live

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addi.l\t#$FFFFFFF7,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesDestructiveQuickCompareForM68040CopiedCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68040);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68040);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#3,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#3,d0\r\n\tcmp.l\td0,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesDeadScratchForM68060CopiedCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7400); // D2 is dead after the compare
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68060);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68060);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#3,d2\r\n\tcmp.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void UsesDestructiveQuickCompareWhenRegisterCompareProfileHasNoScratch(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#3,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsCompareDestinationWhenMoveQuickHasIncomingEdge()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "constant");
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.Mark("constant");
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitWord(0x7000); // D0 is dead after the compare
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("cmpi.l\t#$00000003,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l\t#$00000003,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void CollapsesDeadCopyAndConstantCompareToOneDestructiveQuickCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2200); // MOVE.L D0,D1
		assembler.EmitWord(0x7003); // MOVEQ #3,D0
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.Equal, "equal");
		assembler.EmitWord(0x7000); // D0 dead
		assembler.EmitWord(0x7200); // D1 dead
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("equal");
		assembler.EmitWord(0x7000); // D0 dead
		assembler.EmitWord(0x7200); // D1 dead
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmpi.l", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmp.l\td0,d1", assembly, StringComparison.Ordinal);
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
		Assert.Contains("beq.s", assembly, StringComparison.Ordinal);
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
	public void RendersMoveByteEffectiveAddresses()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x1F00); // MOVE.B D0,-(A7)
		assembler.EmitWord(0x101F); // MOVE.B (A7)+,D0
		assembler.EmitWord(0x1F40); // MOVE.B D0,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("move.b\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\t(a7)+,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\td0,8(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$1F00", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$101F", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$1F40", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesByteRegisterStackRoundTrip()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x1F00); // MOVE.B D0,-(A7)
		assembler.EmitWord(0x101F); // MOVE.B (A7)+,D0
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7201); // Keep the branch target beyond the fallthrough
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("tst.b\td0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\t(a7)+,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesByteMaskAndTestBeforeFrameStore()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xD001); // ADD.B D1,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x4A00); // TST.B D0
		assembler.EmitWord(0x1F40); // MOVE.B D0,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("add.b\td1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\td0,8(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$000000FF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.b\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void MaterializesZeroExtendedByteAtCopyBeforeLowByteUses()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2801); // MOVE.L D1,D4
		assembler.EmitWord(0x2206); // MOVE.L D6,D1; also replaces copy flags
		assembler.Mark("byte-use");
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD92F); // ADD.B D4,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0xEF5F); // ROL.W #7,D7; unrelated
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xDE44); // ADD.W D4,D7
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d4", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\td1,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td1,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$000000FF,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void MaterializesZeroExtendedWordAtAdjacentRegisterCopy()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xD280); // ADD.L D0,D1; observes zero extension
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.w\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersByteQuickArithmeticMnemonics()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x5600); // ADDQ.B #3,D0
		assembler.EmitWord(0x5301); // SUBQ.B #1,D1

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.b\t#3,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("subq.b\t#1,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsWordMaskWhenLongSignConditionIsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitBranch(M68kCondition.Plus, "nonnegative");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonnegative");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$0000FFFF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.w\td2,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMaskedCopyWhenDestinationHasInterveningLongUse()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2801); // MOVE.L D1,D4
		assembler.EmitWord(0x2206); // MOVE.L D6,D1; replaces copy flags
		assembler.EmitWord(0xD084); // ADD.L D4,D0; observes all bits
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD284); // ADD.L D4,D1
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td1,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRepeatedByteMasksAroundNormalizedAddAndStore()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0281); // ANDI.L #$FF,D1
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x0281); // ANDI.L #$FF,D1
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD001); // ADD.B D1,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x1F40); // MOVE.B D0,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0x2002); // MOVE.L D2,D0; result is dead
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("andi.l\t#$000000FF", assembly, StringComparison.Ordinal);
		Assert.Contains("add.b\td1,8(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsFinalByteNormalizationWhenResultRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0281); // ANDI.L #$FF,D1
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD001); // ADD.B D1,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x1F40); // MOVE.B D0,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0xD480); // ADD.L D0,D2; observes zero extension
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			1,
			assembly.Split("andi.l\t#$000000FF,d0", StringSplitOptions.None).Length - 1);
		Assert.DoesNotContain("andi.l\t#$000000FF,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("add.b\td1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesByteNormalizationBeforeNormalizedWordReplacement()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x1F40); // MOVE.B D0,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0x3002); // MOVE.W D2,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xD480); // ADD.L D0,D2; keeps normalized word live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("andi.l\t#$000000FF,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.w\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsNormalizedWordRotateAndAddIntoWordOperations()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xEB88); // LSL.L #5,D0
		assembler.EmitWord(0x2A00); // MOVE.L D0,D5
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xE080); // ASR.L #8,D0
		assembler.EmitWord(0xE680); // ASR.L #3,D0
		assembler.EmitWord(0x8085); // OR.L D5,D0
		assembler.EmitWord(0x0284); // ANDI.L #$FF,D4
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0xD084); // ADD.L D4,D0
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0x0287); // ANDI.L #$FFFF,D7
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0x7000); // MOVEQ #0,D0; rotate temporary is dead
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5; saved left half is dead
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("rol.w\t#5,d7", assembly, StringComparison.Ordinal);
		Assert.Contains("andi.l\t#$000000FF,d4", assembly, StringComparison.Ordinal);
		Assert.Contains("add.w\td4,d7", assembly, StringComparison.Ordinal);
		Assert.Contains("andi.l\t#$0000FFFF,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.w\td7,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d5", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsNormalizedWordRotateAndAddWithoutRedundantByteMask()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xEB88); // LSL.L #5,D0
		assembler.EmitWord(0x2A00); // MOVE.L D0,D5
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0x0280); // ANDI.L #$FFFF,D0
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0xE080); // ASR.L #8,D0
		assembler.EmitWord(0xE680); // ASR.L #3,D0
		assembler.EmitWord(0x8085); // OR.L D5,D0
		assembler.EmitWord(0xD084); // ADD.L D4,D0
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0x0287); // ANDI.L #$FFFF,D7
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("rol.w\t#5,d7", assembly, StringComparison.Ordinal);
		Assert.Contains("add.w\td4,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$000000FF,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.w\td7,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsDeferredWordRotateAndAddIntoSourceRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0xEB48); // LSL.W #5,D0
		assembler.EmitWord(0x3A00); // MOVE.W D0,D5
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0xE048); // LSR.W #8,D0
		assembler.EmitWord(0xE648); // LSR.W #3,D0
		assembler.EmitWord(0x8045); // OR.W D5,D0
		assembler.EmitWord(0xD044); // ADD.W D4,D0
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("rol.w\t#5,d7", assembly, StringComparison.Ordinal);
		Assert.Contains("add.w\td4,d7", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.w\td7,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsDeferredWordRotateAndAddIntoDistinctResultRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0xEB48); // LSL.W #5,D0
		assembler.EmitWord(0x3A00); // MOVE.W D0,D5
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0xE048); // LSR.W #8,D0
		assembler.EmitWord(0xE648); // LSR.W #3,D0
		assembler.EmitWord(0x8045); // OR.W D5,D0
		assembler.EmitWord(0xD044); // ADD.W D4,D0
		assembler.EmitWord(0x2C00); // MOVE.L D0,D6
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td7,d6", assembly, StringComparison.Ordinal);
		Assert.Contains("rol.w\t#5,d6", assembly, StringComparison.Ordinal);
		Assert.Contains("add.w\td4,d6", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.w\td7,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d6", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDeferredWordRotateWhenCopyBackConditionsAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0xEB48); // LSL.W #5,D0
		assembler.EmitWord(0x3A00); // MOVE.W D0,D5
		assembler.EmitWord(0x3007); // MOVE.W D7,D0
		assembler.EmitWord(0xE048); // LSR.W #8,D0
		assembler.EmitWord(0xE648); // LSR.W #3,D0
		assembler.EmitWord(0x8045); // OR.W D5,D0
		assembler.EmitWord(0xD044); // ADD.W D4,D0
		assembler.EmitWord(0x2E00); // MOVE.L D0,D7
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("rol.w\t#5,d7", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,d7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsDeadByteAddResultIntoFrameStore()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xD001); // ADD.B D1,D0
		assembler.EmitWord(0x1F40); // MOVE.B D0,8(A7)
		assembler.EmitWord(8);
		assembler.EmitWord(0x2002); // MOVE.L D2,D0; result is dead after the store
		assembler.EmitWord(0x4E75); // RTS
		var dataflow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(dataflow.TryGetFacts(dataflow.Instructions[1].Offset, out var storeFacts));
		Assert.True(
			(storeFacts.LiveDataAfter & 1) == 0 &&
			(storeFacts.LiveConditionsAfter &
				(M68kConditionCodeSet.Overflow | M68kConditionCodeSet.Carry)) == 0,
			$"live data={storeFacts.LiveDataAfter}, conditions={storeFacts.LiveConditionsAfter}");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("add.b\td1,8(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.b\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\td0,8(a7)", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0x2F00, 0x205F)] // MOVE.L D0,-(A7); MOVEA.L (A7)+,A0
	[InlineData(0x2F17, 0x205F)] // MOVE.L (A7),-(A7); MOVEA.L (A7)+,A0
	public void KeepsAddressRegisterStackRoundTripWhenMoveFlagsAreLive(
		ushort push,
		ushort pop)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(push);
		assembler.EmitWord(pop);
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7201);
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\t(a7)+,a0", assembly, StringComparison.Ordinal);
		Assert.Contains("beq.s", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsStackShuffleWhenRestoredRegisterFlagsAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
		assembler.EmitWord(0x241F); // MOVE.L (A7)+,D2
		assembler.EmitWord(0x201F); // MOVE.L (A7)+,D0
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7401);
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\t(a7)+,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("beq.s", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesZeroExtendedByteRegisterStackRoundTrip()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x1F00); // MOVE.B D0,-(A7)
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0x121F); // MOVE.B (A7)+,D1
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7401); // Keep the branch target beyond the fallthrough
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\td0,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\t(a7)+,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RewritesByteStackPreservationUsingTheNextFreeDataRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x102F); // MOVE.B 27(A7),D0
		assembler.EmitWord(27);
		assembler.EmitWord(0x1F00); // MOVE.B D0,-(A7)
		assembler.EmitWord(0x102F); // MOVE.B 53(A7),D0
		assembler.EmitWord(53);
		assembler.EmitWord(0x1200); // MOVE.B D0,D1
		assembler.EmitWord(0x101F); // MOVE.B (A7)+,D0
		assembler.EmitWord(0xD001); // ADD.B D1,D0
		assembler.EmitWord(0x1F40); // MOVE.B D0,27(A7)
		assembler.EmitWord(27);
		assembler.EmitWord(0x102F); // MOVE.B 55(A7),D0
		assembler.EmitWord(55);
		assembler.EmitWord(0x1200); // MOVE.B D0,D1 overwrites the temporary.
		assembler.EmitWord(0x7000); // D0 is dead after the frame store.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.b\t27(a7),d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.b\t51(a7),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.b\t(a7)+,d0", assembly, StringComparison.Ordinal);
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
	public void UsesSwapAndClearWordForFlagDeadLongShiftBySixteenOnM68000()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xE188); // LSL.L #8,D0
		assembler.EmitWord(0xE188); // LSL.L #8,D0
		assembler.EmitWord(0x2200); // MOVE.L D0,D1 keeps the value live.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("swap\td0", assembly, StringComparison.Ordinal);
		Assert.Contains("clr.w\td0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("lsl.l\t#8,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsLongShiftBySixteenWhenItsConditionCodesAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xE188); // LSL.L #8,D0
		assembler.EmitWord(0xE188); // LSL.L #8,D0
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7201); // Preserve a distinct fallthrough.
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(
			2,
			assembly.Split("lsl.l\t#8,d0", StringSplitOptions.None).Length - 1);
		Assert.DoesNotContain("swap\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void GroupsDescendingDataRegisterPushesIntoOrderedMovemVector()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F04); // MOVE.L D4,-(A7)
		assembler.EmitWord(0x2F02); // MOVE.L D2,-(A7)
		assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movem.l\td1-d2/d4,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td4,-(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsRegisterPushesWhenMovemWouldChangeArgumentOrder()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
		assembler.EmitWord(0x2F02); // MOVE.L D2,-(A7)
		assembler.EmitWord(0x2F04); // MOVE.L D4,-(A7)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("movem.l", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,-(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsOrderedRegisterPushesWhenFinalMoveFlagsAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2F04); // MOVE.L D4,-(A7)
		assembler.EmitWord(0x2F02); // MOVE.L D2,-(A7)
		assembler.EmitWord(0x2F01); // MOVE.L D1,-(A7)
		assembler.EmitBranch(M68kCondition.Equal, "done");
		assembler.EmitWord(0x7001);
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("movem.l", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,-(a7)", assembly, StringComparison.Ordinal);
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

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	public void UsesMoveQuickScratchRegisterForSignedByteAndMask(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$FFFFFF80,D0
		assembler.EmitLong(0xFFFF_FF80u);
		assembler.EmitWord(0x7200); // MOVEQ #0,D1 makes D1 dead at the AND.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);
		var assembly = assembler.RenderAssembly(cpu);

		Assert.Contains("moveq\t#-128,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("and.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void MoveQuickAndMaskPreservesLiveLogicalConditionCodes()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$FFFFFF80,D0
		assembler.EmitLong(0xFFFF_FF80u);
		assembler.EmitWord(0x57C2); // SEQ D2 consumes Z from ANDI.
		assembler.EmitWord(0x7200); // MOVEQ #0,D1 makes D1 dead at the AND.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("moveq\t#-128,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("and.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("seq\td2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UsesMoveQuickScratchRegisterForPositiveSignedByteAndMask()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0283); // ANDI.L #$0000007F,D3
		assembler.EmitLong(0x0000_007Fu);
		assembler.EmitWord(0x7200); // MOVEQ #0,D1 makes D1 dead at the AND.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("moveq\t#127,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("and.l\td1,d3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsImmediateAndMaskWhenNoScratchRegisterIsDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$FFFFFF80,D0
		assembler.EmitLong(0xFFFF_FF80u);
		assembler.EmitWord(0x4E75); // RTS keeps every ABI-visible register live.

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("andi.w\t#$ff80,d0", assembly, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("moveq\t#-128", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void Mc68040KeepsFasterImmediateAndMask()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$FFFFFF80,D0
		assembler.EmitLong(0xFFFF_FF80u);
		assembler.EmitWord(0x57C2); // SEQ D2 keeps Z live.
		assembler.EmitWord(0x7200); // MOVEQ #0,D1 makes D1 available.
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68040);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68040);

		Assert.Contains("andi.l\t#$ffffff80,d0", assembly, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("moveq\t#-128", assembly, StringComparison.Ordinal);
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
	public void UsesSubQuickWhenNegativeAddWouldCancelThroughCarry()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0x0680); // ADDI.L #$FFFFFFFF,D0
		assembler.EmitLong(0xFFFF_FFFF);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addi.", assembly, StringComparison.Ordinal);
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
	public void FoldsZeroExtendedWordIncrementIntoWordAdd()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("increment");
		assembler.EmitWord(0x0281); // ANDI.L #$FFFF,D1
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x2080); // MOVE.L D0,(A0); keeps the result live
		assembler.EmitWord(0x7200); // MOVEQ #0,D1; kills the masked source
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("C68K_increment:", assembly, StringComparison.Ordinal);
		Assert.Contains("moveq\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("add.w\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("andi.l\t#$0000FFFF,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsWordIncrementNormalizationWhenSourceRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0281); // ANDI.L #$FFFF,D1
		assembler.EmitLong(0x0000FFFF);
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x2281); // MOVE.L D1,(A1); observes normalization
		assembler.EmitWord(0x2080); // MOVE.L D0,(A0); keeps the result live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$0000FFFF,d1", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td1,d0", assembly, StringComparison.Ordinal);
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

	[Theory]
	[InlineData(0x2E80, "move.l\td0,-(a7)")]
	[InlineData(0x2E88, "move.l\ta0,-(a7)")]
	public void FoldsStackAllocationIntoRegisterPush(
		ushort storeOpcode,
		string expected)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("entry");
		assembler.EmitWord(0x598F); // SUBQ.L #4,A7
		assembler.EmitWord(storeOpcode); // MOVE.L D0/A0,(A7)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains(expected, assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("subq.l\t#4,a7", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsStackAllocationBeforeStackPointerStore()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x598F); // SUBQ.L #4,A7
		assembler.EmitWord(0x2E8F); // MOVE.L A7,(A7)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#4,a7", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\ta7,(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsStackAllocationWhenStoreIsLabelled()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x598F); // SUBQ.L #4,A7
		assembler.Mark("store");
		assembler.EmitWord(0x2E88); // MOVE.L A0,(A7)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("subq.l\t#4,a7", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\ta0,(a7)", assembly, StringComparison.Ordinal);
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

	[Fact]
	public void RendersCompilerGeneratedArithmeticAndBitInstructionFamilies()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x9681); // SUB.L D1,D3
		assembler.EmitWord(0xC081); // AND.L D1,D0
		assembler.EmitWord(0x8081); // OR.L D1,D0
		assembler.EmitWord(0xB380); // EOR.L D1,D0
		assembler.EmitWord(0x4A82); // TST.L D2
		assembler.EmitWord(0xE280); // ASR.L #1,D0
		assembler.EmitWord(0xE288); // LSR.L #1,D0
		assembler.EmitWord(0xE388); // LSL.L #1,D0
		assembler.EmitWord(0x08C5); // BSET #0,D5
		assembler.EmitWord(0);
		assembler.EmitWord(0x09C2); // BSET D4,D2
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("sub.l\td1,d3", assembly, StringComparison.Ordinal);
		Assert.Contains("and.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("or.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("eor.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("tst.l\td2", assembly, StringComparison.Ordinal);
		Assert.Contains("asr.l\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("lsr.l\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("lsl.l\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("bset\t#0,d5", assembly, StringComparison.Ordinal);
		Assert.Contains("bset\td4,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$0000", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void UsesImmediateBitOperationsForSingleHighBitLogicalImmediates(
		M68kCpuTarget cpu)
	{
		var set = new M68kAssembler();
		set.EmitWord(0x0080); // ORI.L #$80000000,D0
		set.EmitLong(0x80000000);
		set.EmitWord(0x4E75); // RTS

		set.OptimizeForCpu(cpu);
		var setAssembly = set.RenderAssembly(cpu);

		Assert.Contains("bset\t#31,d0", setAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("ori.l", setAssembly, StringComparison.Ordinal);

		var change = new M68kAssembler();
		change.EmitWord(0x0A83); // EORI.L #$80000000,D3
		change.EmitLong(0x80000000);
		change.EmitWord(0x4E75); // RTS

		change.OptimizeForCpu(cpu);
		var changeAssembly = change.RenderAssembly(cpu);

		Assert.Contains("bchg\t#31,d3", changeAssembly, StringComparison.Ordinal);
		Assert.DoesNotContain("eori.l", changeAssembly, StringComparison.Ordinal);
	}

	[Fact]
	public void Mc68000KeepsFasterWordImmediateForSingleLowBit()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0080); // ORI.L #1,D0
		assembler.EmitLong(1);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("ori.w\t#$0001,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("bset", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void Mc68020UsesEqualCostBitOperationForSingleLowBit()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0A82); // EORI.L #1,D2
		assembler.EmitLong(1);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("bchg\t#0,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("eori", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsSingleBitLogicalImmediateWhenItsConditionCodesAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0080); // ORI.L #$80000000,D0
		assembler.EmitLong(0x80000000);
		assembler.EmitBranch(M68kCondition.Equal, "single-bit-live:exit");
		assembler.EmitWord(0x4E71); // NOP
		assembler.Mark("single-bit-live:exit");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("ori.l\t#$80000000,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("bset", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMultiBitLogicalImmediate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0A80); // EORI.L #$C0000000,D0
		assembler.EmitLong(0xC0000000);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);
		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("eori.l\t#$C0000000,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("bchg", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RendersM68020LongMultiplyAndDivideWithoutLeakingExtensions()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4C01); // MULS.L D1,D0
		assembler.EmitWord(0x0800);
		assembler.EmitWord(0x4C41); // DIVS.L D1,D2:D0
		assembler.EmitWord(0x0802);
		assembler.EmitWord(0x4C41); // DIVU.L D1,D2:D0
		assembler.EmitWord(0x0002);
		assembler.EmitWord(0x4E75); // RTS

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);

		Assert.Contains("muls.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("divs.l\td1,d2:d0", assembly, StringComparison.Ordinal);
		Assert.Contains("divu.l\td1,d2:d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$0800", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$0802", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("dc.w\t$0002", assembly, StringComparison.Ordinal);
		Assert.Contains("\trts", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReusesMoveConditionCodesAcrossAddressOnlyInstruction()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x2441); // MOVEA.L D1,A2
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t(a0),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\td1,a2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsDataMoveIntoAddressMoveWhenTemporaryIsDead()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2005); // MOVE.L D5,D0
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0; keeps A0 live and overwrites D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\td5,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td5,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\td0,a0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDataMoveWhenItsConditionCodesRemainLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2005); // MOVE.L D5,D0
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0; keeps A0 live and overwrites D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x2210); // MOVE.L (A0),D1; keeps A0 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\td5,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\td0,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\td5,a0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsPartialWidthDataMoveBeforeAddressConversion()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x3005); // MOVE.W D5,D0; upper word remains unchanged
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		assembler.EmitWord(0x2210); // MOVE.L (A0),D1; keeps A0 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.w\td5,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\td0,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.w\td5,a0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsDataTemporaryWhenItRemainsLiveAsIndexedAddressInput()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2012); // MOVE.L (A2),D0
		assembler.EmitWord(0x2240); // MOVEA.L D0,A1
		assembler.EmitWord(0x2633); // MOVE.L 12(A3,D0.L*4),D3
		assembler.EmitWord(0x0C0C);
		assembler.EmitWord(0x2211); // MOVE.L (A1),D1; keeps A1 live
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(M68kCpuTarget.M68020);

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68020);
		Assert.Contains("move.l\t(a2),d0", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\td0,a1", assembly, StringComparison.Ordinal);
		Assert.Contains(
			"move.l\t12(a3,d0.l*4),d3",
			assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsTestAfterUnrelatedConditionCodeWrite()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x7401); // MOVEQ #1,D2
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#1,d2", assembly, StringComparison.Ordinal);
		Assert.Contains("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRepeatedCompareAcrossAddressOnlyInstruction()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xB081); // CMP.L D1,D0
		assembler.EmitWord(0x2441); // MOVEA.L D1,A2
		assembler.EmitWord(0xB081); // CMP.L D1,D0
		assembler.EmitBranch(M68kCondition.NotEqual, "different");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("different");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("cmp.l\td1,d0").Length - 1);
		Assert.DoesNotContain("movea.l\td1,a2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsNullCheckAfterDominatingNonNullAssertion()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xB5FC); // CMPA.L #0,A2
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "first_nonnull");
		assembler.EmitWord(0x4AFC); // ILLEGAL: non-returning null failure
		assembler.Mark("first_nonnull");
		assembler.EmitWord(0x2742); // MOVE.L D3,8(A2); overwrites condition codes
		assembler.EmitWord(8);
		assembler.EmitWord(0xB5FC); // CMPA.L #0,A2
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "second_nonnull");
		assembler.EmitWord(0x4AFC); // ILLEGAL: unreachable repeated null failure
		assembler.Mark("second_nonnull");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("cmpa.w\t#0,a2").Length - 1);
		Assert.Contains("bne.s\tC68K_first_nonnull", assembly, StringComparison.Ordinal);
		Assert.Contains("bra.s\tC68K_second_nonnull", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsNullCheckAcrossNonReturningExceptionCall()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xB5FC); // CMPA.L #0,A2
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "first_nonnull");
		assembler.EmitJsr("__c68k_exception_raise", external: false);
		assembler.Mark("first_nonnull");
		assembler.EmitWord(0x2742); // MOVE.L D3,8(A2)
		assembler.EmitWord(8);
		assembler.EmitWord(0xB5FC); // CMPA.L #0,A2
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "second_nonnull");
		assembler.EmitJsr("__c68k_exception_raise", external: false);
		assembler.Mark("second_nonnull");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(1, assembly.Split("cmpa.w\t#0,a2").Length - 1);
		Assert.Contains("bra.s\tC68K_second_nonnull", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsNullCheckWhenASeparateIncomingPathCanBypassAssertion()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "alternate");
		assembler.EmitWord(0xB5FC); // CMPA.L #0,A2
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "first_nonnull");
		assembler.EmitWord(0x4AFC); // ILLEGAL: non-returning null failure
		assembler.Mark("first_nonnull");
		assembler.EmitWord(0x2742); // MOVE.L D3,8(A2)
		assembler.EmitWord(8);
		assembler.EmitBranch(M68kCondition.True, "join");
		assembler.Mark("alternate");
		assembler.EmitWord(0x2742); // MOVE.L D3,8(A2)
		assembler.EmitWord(8);
		assembler.Mark("join");
		assembler.EmitWord(0x2742); // MOVE.L D3,12(A2)
		assembler.EmitWord(12);
		assembler.EmitWord(0xB5FC); // CMPA.L #0,A2
		assembler.EmitLong(0);
		assembler.EmitBranch(M68kCondition.NotEqual, "second_nonnull");
		assembler.EmitWord(0x4AFC); // ILLEGAL: null failure
		assembler.Mark("second_nonnull");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Equal(2, assembly.Split("cmpa.w\t#0,a2").Length - 1);
		Assert.Contains("bne.s\tC68K_second_nonnull", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void ReusesArithmeticZeroFlagAcrossAddressOnlyInstruction()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x5280); // ADDQ.L #1,D0
		assembler.EmitWord(0x2441); // MOVEA.L D1,A2
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.l\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsTestWhenBranchNeedsDifferentlyDefinedOverflowFlag()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x5280); // ADDQ.L #1,D0
		assembler.EmitWord(0x2441); // MOVEA.L D1,A2
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.GreaterThan, "positive");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("positive");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesUnchangedNegativeFlagAcrossBitOperation()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x09C2); // BSET D4,D2 (writes only Z)
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Plus, "positive");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("positive");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
		Assert.Contains("bset\td4,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RewritesSingleBitMaskBranchToBitTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0xC082); // AND.L D2,D0
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("btst\t#0,d2", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("and.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RewritesCopiedHigherBitMaskAndUnsignedBranchToBitTest()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7004); // MOVEQ #4,D0
		assembler.EmitWord(0x2203); // MOVE.L D3,D1
		assembler.EmitWord(0xC280); // AND.L D0,D1
		assembler.EmitBranch(M68kCondition.LowerOrSame, "clear");
		assembler.EmitWord(0x2003); // MOVE.L D3,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("clear");
		assembler.EmitWord(0x2003); // MOVE.L D3,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("btst\t#2,d3", assembly, StringComparison.Ordinal);
		Assert.Contains("beq.s\tC68K_clear", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#4,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("and.l\td0,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsSingleBitMaskWhenOtherFlagsRemainLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0xC082); // AND.L D2,D0
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Minus, "negative");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("negative");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("and.l\td2,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("btst\t#0,d2", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void DoesNotTreatDivisionFlagsAsTestOfPackedResult()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x80C1); // DIVU.W D1,D0
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRedundantTestAfterLogicalImmediate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x0280); // ANDI.L #$000000FF,D0
		assembler.EmitLong(0x000000FF);
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("andi.l\t#$000000FF,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRedundantTestAfterMove()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2010); // MOVE.L (A0),D0
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.NotEqual, "nonzero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("nonzero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t(a0),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRedundantTestAfterLogicalRegisterOperation()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xC081); // AND.L D1,D0
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("and.l\td1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovesRedundantTestAfterMemoryLogicalAnd()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0xC090); // AND.L (A0),D0
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("zero");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);

		Assert.Contains("and.l\t(a0),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("tst.l\td0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FusesSymbolicQuickReadModifyWriteWithoutRegisterDependence()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x223A); // MOVE.L value(PC),D1
		assembler.EmitPcRelativeWord("value");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x23C0); // MOVE.L D0,value
		assembler.EmitAddress("value");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0; end both temporary live ranges
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0x4E75); // RTS
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.l\t#1,C68K_value", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\tC68K_value(pc)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FusesDirectQuickSymbolicReadModifyWrite()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x223A); // MOVE.L value(PC),D1
		assembler.EmitPcRelativeWord("value");
		assembler.EmitWord(0x5281); // ADDQ.L #1,D1
		assembler.EmitWord(0x23C1); // MOVE.L D1,value
		assembler.EmitAddress("value");
		assembler.EmitWord(0x7200); // MOVEQ #0,D1; end the temporary live range
		assembler.EmitWord(0x4E75); // RTS
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("addq.l\t#1,C68K_value", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\tC68K_value(pc)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("addq.l\t#1,d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void UpdatesOriginalRegisterWithoutArithmeticCopyRoundTrip()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7010); // MOVEQ #16,D0
		assembler.EmitWord(0x2604); // MOVE.L D4,D3
		assembler.EmitWord(0xD680); // ADD.L D0,D3
		assembler.EmitWord(0x2803); // MOVE.L D3,D4
		assembler.EmitWord(0x7600); // D3 is dead after the update
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#16,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td0,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td4,d3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td3,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsSubtractionCopyUpdateGenerically()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2604); // MOVE.L D4,D3
		assembler.EmitWord(0x9681); // SUB.L D1,D3
		assembler.EmitWord(0x2803); // MOVE.L D3,D4
		assembler.EmitWord(0x7600); // D3 is dead after the update
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("sub.l\td1,d4", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td4,d3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td3,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesLiveTemporaryAfterDirectArithmeticUpdate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2604); // MOVE.L D4,D3
		assembler.EmitWord(0xD681); // ADD.L D1,D3
		assembler.EmitWord(0x2803); // MOVE.L D3,D4
		assembler.EmitWord(0x2003); // MOVE.L D3,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("add.l\td1,d4", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td4,d3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td1,d3", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td3,d4", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FusesDynamicFrameReadModifyWrite()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x222D); // MOVE.L 8(A5),D1
		assembler.EmitWord(8);
		assembler.EmitWord(0xD282); // ADD.L D2,D1
		assembler.EmitWord(0x2B41); // MOVE.L D1,8(A5)
		assembler.EmitWord(8);
		assembler.EmitWord(0x7200); // MOVEQ #0,D1; end the temporary live range
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("add.l\td2,8(a5)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t8(a5),d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void PreservesLiveLoadedValueWhileFusingMemoryUpdate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x223A); // MOVE.L value(PC),D1
		assembler.EmitPcRelativeWord("value");
		assembler.EmitWord(0x7001); // MOVEQ #1,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x23C0); // MOVE.L D0,value
		assembler.EmitAddress("value");
		assembler.EmitWord(0x7000); // D0 is dead; D1 remains observable at return
		assembler.EmitWord(0x4E75); // RTS
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\tC68K_value(pc),d1", assembly, StringComparison.Ordinal);
		Assert.Contains("addq.l\t#1,C68K_value", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#1,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td1,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsNonQuickDeltaRegisterForFusedMemoryUpdate()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x223A); // MOVE.L value(PC),D1
		assembler.EmitPcRelativeWord("value");
		assembler.EmitWord(0x7009); // MOVEQ #9,D0
		assembler.EmitWord(0xD081); // ADD.L D1,D0
		assembler.EmitWord(0x23C0); // MOVE.L D0,value
		assembler.EmitAddress("value");
		assembler.EmitWord(0x7000); // End both temporary live ranges
		assembler.EmitWord(0x7200);
		assembler.EmitWord(0x4E75); // RTS
		assembler.MarkDataStart();
		assembler.Mark("value");
		assembler.EmitLong(0);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#9,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("add.l\td0,C68K_value", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\tC68K_value(pc),d1", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsIndirectReadModifyWriteForPotentialMmio()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2210); // MOVE.L (A0),D1
		assembler.EmitWord(0xD282); // ADD.L D2,D1
		assembler.EmitWord(0x2081); // MOVE.L D1,(A0)
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t(a0),d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,(a0)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("add.l\td2,(a0)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsReadModifyWriteWhenResultRegisterRemainsLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x222D); // MOVE.L 8(A5),D1
		assembler.EmitWord(8);
		assembler.EmitWord(0xD282); // ADD.L D2,D1
		assembler.EmitWord(0x2B41); // MOVE.L D1,8(A5)
		assembler.EmitWord(8);
		assembler.EmitWord(0x2001); // MOVE.L D1,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t8(a5),d1", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td1,8(a5)", assembly, StringComparison.Ordinal);
	}
}
