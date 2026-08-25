/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kExecSlopPeepholeTests
{
	private const uint CodeAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RemovesAdjacentDeadLongImmediateBeforeFullOverwrite(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #300,D0
		assembler.EmitLong(300);
		assembler.EmitWord(0x2028); // MOVE.L 300(A0),D0
		assembler.EmitWord(300);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.DoesNotContain("#$0000012C,d0", assembly,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("move.l\t300(a0),d0", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsLongImmediateBeforePartialRegisterOverwrite()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #$12345678,D0
		assembler.EmitLong(0x1234_5678);
		assembler.EmitWord(0x1010); // MOVE.B (A0),D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#$12345678,d0", assembly,
			StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void KeepsLongImmediateWhenOverwriteReadsRegister()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #300,D0
		assembler.EmitLong(300);
		assembler.EmitWord(0xD080); // ADD.L D0,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("#$0000012C,d0", assembly,
			StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RemovesZeroWhenOnlyLowByteIsObservedBeforeFullOverwrite(
		M68kCpuTarget cpu)
	{
		var assembler = CreateByteOnlyExecutionFixture();

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.DoesNotContain("moveq\t#0,d3", assembly,
			StringComparison.Ordinal);
		Assert.Contains("move.b\t(a0),d3", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsZeroWhenCanonicalUnsignedByteIsObserved()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7600); // MOVEQ #0,D3
		assembler.EmitWord(0x1610); // MOVE.B (A0),D3
		assembler.EmitWord(0x2003); // MOVE.L D3,D0; observes upper bytes
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d3", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsZeroWhenItsConditionCodesAreObservedBeforeByteDefinition()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7600); // MOVEQ #0,D3
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x1610); // MOVE.B (A0),D3
		assembler.Mark("zero");
		assembler.EmitWord(0x2003); // MOVE.L D3,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d3", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RemovesProductionByteStoreZeroWhenFullOverwriteFollows(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7600); // MOVEQ #0,D3
		assembler.EmitWord(0x1628); // MOVE.B 14(A0),D3
		assembler.EmitWord(14);
		assembler.EmitWord(0x7002); // MOVEQ #2,D0
		assembler.EmitWord(0x8680); // OR.L D0,D3
		assembler.EmitWord(0x7200); // MOVEQ #0,D1
		assembler.EmitWord(0x1203); // MOVE.B D3,D1
		assembler.EmitWord(0x1141); // MOVE.B D1,14(A0)
		assembler.EmitWord(14);
		assembler.EmitWord(0x223C); // MOVE.L #$4EF9,D1; full overwrite
		assembler.EmitLong(0x0000_4EF9);
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.DoesNotContain("moveq\t#0,d1", assembly,
			StringComparison.Ordinal);
		Assert.Contains("move.b\td3,d1", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RemovesByteZeroAcrossUnreferencedInternalAnchor(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7600); // MOVEQ #0,D3
		assembler.EmitWord(0x1610); // MOVE.B (A0),D3
		assembler.EmitWord(0x1683); // MOVE.B D3,(A3)
		assembler.Mark("diagnostic:byte-anchor");
		assembler.EmitWord(0x260A); // MOVE.L A2,D3; full overwrite
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.DoesNotContain("moveq\t#0,d3", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void FoldsBaseIndexAcrossIndependentRegisterSetup(M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		assembler.EmitWord(0x2004); // MOVE.L D4,D0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0x2081); // MOVE.L D1,(A0)
		assembler.EmitWord(0x204C); // MOVEA.L A4,A0; kill temporary
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("move.l\td4,d0\r\n\tmove.l\td1,(a5,d0.l)", assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta5,a0", assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("adda.l\td0,a0", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void FoldsBaseIndexWhenTemporaryDiesAfterKnownLongMultiplyBarrier(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0x2410); // MOVE.L (A0),D2
		assembler.EmitWord(0x4C01); // MULS.L D1,D0
		assembler.EmitWord(0x0800);
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0; full overwrite
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("move.l\td2,d0\r\n\tmove.l\t(a5,d0.l),d2", assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("adda.l\td0,a0", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void FoldsBaseIndexAcrossUnreferencedInternalAnchor(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0x2410); // MOVE.L (A0),D2
		assembler.Mark("diagnostic:address-anchor");
		assembler.EmitWord(0x4C01); // MULS.L D1,D0
		assembler.EmitWord(0x0800);
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0; full overwrite
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("move.l\t(a5,d0.l),d2", assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("adda.l\td0,a0", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void KeepsBaseIndexAcrossReferencedControlFlowMerge(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.NotEqual, "merge");
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0x2410); // MOVE.L (A0),D2
		assembler.Mark("merge");
		assembler.EmitWord(0x4C01); // MULS.L D1,D0
		assembler.EmitWord(0x0800);
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0; full overwrite
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("movea.l\ta5,a0", assembly,
			StringComparison.Ordinal);
		Assert.Contains("adda.l\td0,a0", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsBaseIndexWhenInterveningInstructionCanFault()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		assembler.EmitWord(0x2012); // MOVE.L (A2),D0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0x2081); // MOVE.L D1,(A0)
		assembler.EmitWord(0x204C); // MOVEA.L A4,A0; kill temporary
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("movea.l\ta5,a0", assembly, StringComparison.Ordinal);
		Assert.Contains("adda.l\td0,a0", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void KeepsSizeAndCycleNeutralMoveQuickStackTransport(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x702A); // MOVEQ #42,D0
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitWord(0x7000); // MOVEQ #0,D0; kill value and flags
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("moveq\t#42,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("pea\t$002A.w", assembly,
			StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void RematerializesCalleeSavedLongTransportAcrossDirectInternalCall()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:caller");
		assembler.EmitWord(0x2A3C); // MOVE.L #$12345678,D5
		assembler.EmitLong(0x1234_5678);
		assembler.EmitWord(0x2F05); // MOVE.L D5,-(A7)
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5; explicit kill
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:caller:end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:callee:end");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#$12345678,-(a7)", assembly,
			StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("#$12345678,d5", assembly,
			StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("move.l\td5,-(a7)", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMoveQuickTransportWhenPushConditionsAreObserved()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x702A); // MOVEQ #42,D0
		assembler.EmitWord(0x2F00); // MOVE.L D0,-(A7)
		assembler.EmitBranch(M68kCondition.Equal, "observed");
		assembler.EmitWord(0x7001); // kill D0 on fallthrough
		assembler.EmitBranch(M68kCondition.True, "done");
		assembler.Mark("observed");
		assembler.EmitWord(0x7002); // kill D0 on branch target
		assembler.Mark("done");
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#42,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.l\td0,-(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("pea\t$002A.w", assembly,
			StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RetargetsMemoryLoadWhenDirectCalleeKillsTemporaryBeforeUse(
		M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:caller");
		assembler.EmitWord(0x202A); // MOVE.L 36(A2),D0
		assembler.EmitWord(36);
		assembler.EmitWord(0x2400); // MOVE.L D0,D2
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:caller:end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0; kill incoming D0
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:callee:end");

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("move.l\t36(a2),d2", assembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\td0,d2", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMemoryLoadCopyWhenDirectCalleeReadsIncomingTemporary()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:caller");
		assembler.EmitWord(0x202A); // MOVE.L 36(A2),D0
		assembler.EmitWord(36);
		assembler.EmitWord(0x2400); // MOVE.L D0,D2
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:caller:end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x2200); // MOVE.L D0,D1; observe incoming D0
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:callee:end");

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t36(a2),d0", assembly,
			StringComparison.Ordinal);
		Assert.Contains("move.l\td0,d2", assembly,
			StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsMemoryLoadCopyAcrossIndirectCall()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x202A); // MOVE.L 36(A2),D0
		assembler.EmitWord(36);
		assembler.EmitWord(0x2400); // MOVE.L D0,D2
		assembler.EmitWord(0x4E90); // JSR (A0); target is opaque
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t36(a2),d0", assembly,
			StringComparison.Ordinal);
		Assert.Contains("move.l\td0,d2", assembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void ExecSlopRewritesMatchDisabledExecutionAndConditionCodes(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var disabledAssembler = CreateCombinedExecutionFixture();
		disabledAssembler.OptimizeForCpu(
			target,
			peepholeOptimization: M68kPeepholeOptimizationMode.Disabled);
		var disabled = disabledAssembler.Link(
			CodeAddress,
			new Dictionary<string, uint>());

		var optimizedAssembler = CreateCombinedExecutionFixture();
		optimizedAssembler.OptimizeForCpu(target);
		var optimizedAssembly = optimizedAssembler.RenderAssembly(target);
		var optimized = optimizedAssembler.Link(
			CodeAddress,
			new Dictionary<string, uint>());

		var expected = ExecuteCombinedFixture(disabled, model);
		var actual = ExecuteCombinedFixture(optimized, model);

		Assert.Equal(expected.Result, actual.Result);
		Assert.Equal(expected.Stored, actual.Stored);
		Assert.Equal(expected.Conditions, actual.Conditions);
		Assert.Equal(expected.Stack, actual.Stack);
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(actual.Cycles <= expected.Cycles,
				$"Optimized MC68000 path regressed from {expected.Cycles} to " +
				$"{actual.Cycles} cycles.");
		}
		Assert.DoesNotContain("#$0000012C,d0", optimizedAssembly,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("move.l\t300(a0),d2", optimizedAssembly,
			StringComparison.Ordinal);
		Assert.Contains("move.l\t#$12345678,-(a7)", optimizedAssembly,
			StringComparison.OrdinalIgnoreCase);
		Assert.Contains("move.l\td0,(a5,d1.l)", optimizedAssembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void ByteOnlyRewriteMatchesSeededUpperBitsStackAndFinalConditions(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var disabledAssembler = CreateByteOnlyExecutionFixture();
		disabledAssembler.OptimizeForCpu(
			target,
			peepholeOptimization: M68kPeepholeOptimizationMode.Disabled);
		var disabled = disabledAssembler.Link(
			CodeAddress,
			new Dictionary<string, uint>());

		var optimizedAssembler = CreateByteOnlyExecutionFixture();
		optimizedAssembler.OptimizeForCpu(target);
		var optimizedAssembly = optimizedAssembler.RenderAssembly(target);
		var optimized = optimizedAssembler.Link(
			CodeAddress,
			new Dictionary<string, uint>());

		var expected = ExecuteByteOnlyFixture(disabled, model);
		var actual = ExecuteByteOnlyFixture(optimized, model);

		Assert.Equal(expected.Result, actual.Result);
		Assert.Equal(expected.Stored, actual.Stored);
		Assert.Equal(expected.Conditions, actual.Conditions);
		Assert.Equal(expected.Stack, actual.Stack);
		if (target == M68kCpuTarget.M68000)
		{
			Assert.True(actual.Cycles <= expected.Cycles,
				$"Optimized MC68000 path regressed from {expected.Cycles} to " +
				$"{actual.Cycles} cycles.");
		}
		Assert.DoesNotContain("moveq\t#0,d3", optimizedAssembly,
			StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
	public void BaseIndexAcrossLongMultiplyMatchesDisabledExecutionAndConditions(
		M68kCpuTarget target,
		M68kCpuModel model)
	{
		var disabledAssembler = CreateLongMultiplyAddressFixture();
		disabledAssembler.OptimizeForCpu(
			target,
			peepholeOptimization: M68kPeepholeOptimizationMode.Disabled);
		var disabled = disabledAssembler.Link(
			CodeAddress,
			new Dictionary<string, uint>());

		var optimizedAssembler = CreateLongMultiplyAddressFixture();
		optimizedAssembler.OptimizeForCpu(target);
		var optimizedAssembly = optimizedAssembler.RenderAssembly(target);
		var optimized = optimizedAssembler.Link(
			CodeAddress,
			new Dictionary<string, uint>());

		var expected = ExecuteLongMultiplyAddressFixture(disabled, model);
		var actual = ExecuteLongMultiplyAddressFixture(optimized, model);

		Assert.Equal(expected.Result, actual.Result);
		Assert.Equal(expected.Stored, actual.Stored);
		Assert.Equal(expected.Conditions, actual.Conditions);
		Assert.Equal(expected.Stack, actual.Stack);
		Assert.Contains("move.l\t(a5,d0.l),d2", optimizedAssembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("adda.l\td0,a0", optimizedAssembly,
			StringComparison.Ordinal);
	}

	private static M68kAssembler CreateByteOnlyExecutionFixture()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:byte-only");
		assembler.EmitWord(0x7600); // MOVEQ #0,D3
		assembler.EmitWord(0x1610); // MOVE.B (A0),D3
		assembler.EmitWord(0xD602); // ADD.B D2,D3
		assembler.EmitWord(0x1683); // MOVE.B D3,(A3)
		assembler.EmitWord(0x260A); // MOVE.L A2,D3; full overwrite
		assembler.EmitWord(0x2003); // MOVE.L D3,D0
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:byte-only:end");
		return assembler;
	}

	private static M68kAssembler CreateLongMultiplyAddressFixture()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:long-multiply-address");
		assembler.EmitWord(0x204D); // MOVEA.L A5,A0
		assembler.EmitWord(0x2002); // MOVE.L D2,D0
		assembler.EmitWord(0xD1C0); // ADDA.L D0,A0
		assembler.EmitWord(0x2410); // MOVE.L (A0),D2
		assembler.EmitWord(0x4C01); // MULS.L D1,D0
		assembler.EmitWord(0x0800);
		assembler.EmitWord(0x204C); // MOVEA.L A4,A0; full overwrite
		assembler.EmitWord(0x2682); // MOVE.L D2,(A3)
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:long-multiply-address:end");
		return assembler;
	}

	private static M68kAssembler CreateCombinedExecutionFixture()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:caller");
		assembler.EmitWord(0x203C); // MOVE.L #300,D0; dead offset materialization
		assembler.EmitLong(300);
		assembler.EmitWord(0x2028); // MOVE.L 300(A0),D0
		assembler.EmitWord(300);
		assembler.EmitWord(0x2400); // MOVE.L D0,D2
		assembler.EmitWord(0x2A3C); // MOVE.L #$12345678,D5
		assembler.EmitLong(0x1234_5678);
		assembler.EmitWord(0x2F05); // MOVE.L D5,-(A7)
		assembler.EmitCall("method:callee");
		assembler.EmitWord(0x588F); // ADDQ.L #4,A7
		assembler.EmitWord(0xD082); // ADD.L D2,D0
		assembler.EmitWord(0x224D); // MOVEA.L A5,A1
		assembler.EmitWord(0x2203); // MOVE.L D3,D1; independent index setup
		assembler.EmitWord(0xD3C1); // ADDA.L D1,A1
		assembler.EmitWord(0x2280); // MOVE.L D0,(A1)
		assembler.EmitWord(0x224C); // MOVEA.L A4,A1; kill address temporary
		assembler.EmitWord(0x7A00); // MOVEQ #0,D5; kill stack transport
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:caller:end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x202F); // MOVE.L 4(A7),D0; kill incoming D0
		assembler.EmitWord(4);
		assembler.EmitWord(0x4E75); // RTS
		assembler.Mark("method:callee:end");
		return assembler;
	}

	private static (
		uint Result,
		uint Stored,
		ushort Conditions,
		uint Stack,
		long Cycles) ExecuteCombinedFixture(
		LinkedCode linked,
		M68kCpuModel model)
	{
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)CodeAddress));
		bus.WriteLong(StackPointer, ReturnSentinel);
		bus.WriteLong(0x0000_3000 + 300, 5);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(
			CodeAddress + (uint)linked.Labels["method:caller"],
			StackPointer);
		cpu.State.A[0] = 0x0000_3000;
		cpu.State.A[4] = 0x0000_5000;
		cpu.State.A[5] = 0x0000_4000;
		cpu.State.D[3] = 8;

		for (var instruction = 0; instruction < 100; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				return (
					cpu.State.D[0],
					bus.ReadLong(0x0000_4008),
					(ushort)(cpu.State.StatusRegister & 0x001F),
					cpu.State.A[7],
					cpu.State.Cycles);
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}

		throw new Xunit.Sdk.XunitException(
			$"{model} did not return from the Exec slop fixture.");
	}

	private static (
		uint Result,
		byte Stored,
		ushort Conditions,
		uint Stack,
		long Cycles) ExecuteByteOnlyFixture(
		LinkedCode linked,
		M68kCpuModel model)
	{
		const uint sourceAddress = 0x0000_3000;
		const uint storeAddress = 0x0000_4000;
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)CodeAddress));
		bus.WriteLong(StackPointer, ReturnSentinel);
		bus.Memory[(int)sourceAddress] = 0x7F;
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(
			CodeAddress + (uint)linked.Labels["method:byte-only"],
			StackPointer);
		cpu.State.A[0] = sourceAddress;
		cpu.State.A[2] = 0xF000_0001;
		cpu.State.A[3] = storeAddress;
		cpu.State.D[2] = 2;
		cpu.State.D[3] = 0xA5A5_A5A5;

		for (var instruction = 0; instruction < 32; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				return (
					cpu.State.D[0],
					bus.Memory[(int)storeAddress],
					(ushort)(cpu.State.StatusRegister & 0x001F),
					cpu.State.A[7],
					cpu.State.Cycles);
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}

		throw new Xunit.Sdk.XunitException(
			$"{model} did not return from the byte-only Exec slop fixture.");
	}

	private static (
		uint Result,
		uint Stored,
		ushort Conditions,
		uint Stack) ExecuteLongMultiplyAddressFixture(
		LinkedCode linked,
		M68kCpuModel model)
	{
		const uint sourceBase = 0x0000_4000;
		const uint storeAddress = 0x0000_5000;
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)CodeAddress));
		bus.WriteLong(StackPointer, ReturnSentinel);
		bus.WriteLong(sourceBase + 8, 5);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(
			CodeAddress + (uint)linked.Labels["method:long-multiply-address"],
			StackPointer);
		cpu.State.A[3] = storeAddress;
		cpu.State.A[4] = 0x0000_6000;
		cpu.State.A[5] = sourceBase;
		cpu.State.D[1] = 3;
		cpu.State.D[2] = 8;

		for (var instruction = 0; instruction < 32; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				return (
					cpu.State.D[0],
					bus.ReadLong(storeAddress),
					(ushort)(cpu.State.StatusRegister & 0x001F),
					cpu.State.A[7]);
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}

		throw new Xunit.Sdk.XunitException(
			$"{model} did not return from the long-multiply address fixture.");
	}
}
