/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kSingleUseTransportPeepholeTests
{
	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void FoldsLongImmediateTransportIntoCompare(M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #$00000400,D0
		assembler.EmitLong(0x0000_0400);
		assembler.EmitWord(0xB280); // CMP.L D0,D1
		assembler.EmitBranch(M68kCondition.NotEqual, "different");
		EmitReturn(assembler);
		assembler.Mark("different");
		EmitReturn(assembler);

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("cmpi.l\t#$00000400,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t#$00000400,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("cmp.l\td0,d1", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0xC280, "andi.l")]
	[InlineData(0x8280, "ori.l")]
	[InlineData(0xB181, "eori.l")]
	[InlineData(0xD280, "addi.l")]
	public void FoldsLongImmediateTransportIntoArithmetic(
		int operation,
		string mnemonic)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #$12345678,D0
		assembler.EmitLong(0x1234_5678);
		assembler.EmitWord((ushort)operation);
		EmitReturn(assembler, usesData: 0x00FE); // Preserve the D1 result.

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.True(assembly.Contains($"{mnemonic}\t#$12345678,d1", StringComparison.Ordinal), assembly);
		Assert.DoesNotContain("move.l\t#$12345678,d0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsLongImmediateTransportIntoWordCompare()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #1208,D0
		assembler.EmitLong(0x0000_04B8);
		assembler.EmitWord(0xB240); // CMP.W D0,D1
		assembler.EmitBranch(M68kCondition.NotEqual, "different");
		EmitReturn(assembler);
		assembler.Mark("different");
		EmitReturn(assembler);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("cmpi.w\t#1208,d1", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\t#", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(0x2340, 0x12345678u, "move.l\t#$12345678,12(a1)")]
	[InlineData(0x3340, 0x00005678u, "move.w\t#$5678,12(a1)")]
	[InlineData(0x1340, 0x000000E0u, "move.b\t#$E0,12(a1)")]
	public void FoldsLongImmediateTransportIntoMemoryStore(
		int store,
		uint value,
		string expected)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #value,D0
		assembler.EmitLong(value);
		assembler.EmitWord((ushort)store); // MOVE.[LBW] D0,12(A1)
		assembler.EmitWord(12);
		EmitReturn(assembler);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains(expected, assembly, StringComparison.Ordinal);
		Assert.DoesNotContain(",d0\r\n\tmove.", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsRelocatableLongImmediateForNarrowMemoryStore()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); // MOVE.L #value,D0
		assembler.EmitAddress("value");
		assembler.EmitWord(0x3340); // MOVE.W D0,12(A1)
		assembler.EmitWord(12);
		EmitReturn(assembler);
		assembler.Mark("value");
		assembler.MarkDataStart();
		assembler.EmitLong(0x1234_5678);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("move.l\t#C68K_value,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("move.w\td0,12(a1)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void FoldsDeadZeroTransportIntoAddressSubtract()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		assembler.EmitWord(0x2208); // MOVE.L A0,D1
		EmitReturn(assembler, usesData: 0x00FE);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("suba.l\ta0,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\td0,a0", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsZeroTransportWhenMoveQuickFlagsAreLive()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7000); // MOVEQ #0,D0
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x2208); // MOVE.L A0,D1
		EmitReturn(assembler, usesData: 0x00FE);
		assembler.Mark("zero");
		assembler.EmitWord(0x2208); // MOVE.L A0,D1
		EmitReturn(assembler, usesData: 0x00FE);

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("moveq\t#0,d0", assembly, StringComparison.Ordinal);
		Assert.Contains("movea.l\td0,a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("suba.l\ta0,a0", assembly, StringComparison.Ordinal);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000)]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	public void RetargetsDeadLeaTransportToFinalAddressRegister(M68kCpuTarget cpu)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x41EF); // LEA 12(A7),A0
		assembler.EmitWord(12);
		assembler.EmitWord(0x2248); // MOVEA.L A0,A1
		assembler.EmitWord(0x2011); // MOVE.L (A1),D0
		EmitReturn(assembler, usesData: 0x00FD);

		assembler.OptimizeForCpu(cpu);

		var assembly = assembler.RenderAssembly(cpu);
		Assert.Contains("move.l\t12(a7),d0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("lea\t12(a7),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("movea.l\ta0,a1", assembly, StringComparison.Ordinal);
	}

	private static void EmitReturn(
		M68kAssembler assembler,
		ushort usesData = 0x00FC,
		ushort usesAddress = 0x00FC)
	{
		var offset = assembler.Offset;
		assembler.EmitWord(0x4E75); // RTS
		assembler.SetInstructionEffects(
			offset,
			new M68kInstructionEffects(
				usesData,
				0,
				usesAddress,
				0x0080,
				M68kConditionCodeSet.None,
				M68kConditionCodeSet.None,
				M68kMemorySet.Stack,
				M68kMemorySet.None,
				4,
				true,
				false));
	}
}
