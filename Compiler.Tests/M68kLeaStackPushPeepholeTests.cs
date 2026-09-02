/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kLeaStackPushPeepholeTests
{
	[Fact]
	public void ReplacesDeadLeaStackTransportWithPea()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x41EF); // LEA 12(A7),A0
		assembler.EmitWord(12);
		assembler.EmitWord(0x2F08); // MOVE.L A0,-(A7)
		assembler.EmitWord(0x2040); // MOVEA.L D0,A0; transport is dead
		assembler.EmitWord(0x4E75); // RTS

		assembler.OptimizeForM68000();

		var assembly = assembler.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("pea\t12(a7)", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("lea\t12(a7),a0", assembly, StringComparison.Ordinal);
		Assert.DoesNotContain("move.l\ta0,-(a7)", assembly, StringComparison.Ordinal);
	}

	[Fact]
	public void KeepsLeaStackTransportWhenAddressOrConditionsRemainLive()
	{
		var addressLive = new M68kAssembler();
		addressLive.EmitWord(0x41EF); // LEA 12(A7),A0
		addressLive.EmitWord(12);
		addressLive.EmitWord(0x2F08); // MOVE.L A0,-(A7)
		addressLive.EmitWord(0x2008); // MOVE.L A0,D0
		addressLive.EmitWord(0x4E75); // RTS
		addressLive.OptimizeForM68000();
		var addressAssembly = addressLive.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("lea\t12(a7),a0", addressAssembly, StringComparison.Ordinal);
		Assert.Contains("move.l\ta0,-(a7)", addressAssembly, StringComparison.Ordinal);

		var conditionsLive = new M68kAssembler();
		conditionsLive.EmitWord(0x41EF); // LEA 12(A7),A0
		conditionsLive.EmitWord(12);
		conditionsLive.EmitWord(0x2F08); // MOVE.L A0,-(A7), defines Z/N
		conditionsLive.EmitBranch(M68kCondition.Equal, "done");
		conditionsLive.EmitWord(0x2040); // MOVEA.L D0,A0
		conditionsLive.Mark("done");
		conditionsLive.EmitWord(0x4E75); // RTS
		conditionsLive.OptimizeForM68000();
		var conditionsAssembly = conditionsLive.RenderAssembly(M68kCpuTarget.M68000);
		Assert.Contains("lea\t12(a7),a0", conditionsAssembly,
			StringComparison.Ordinal);
		Assert.Contains("move.l\ta0,-(a7)", conditionsAssembly,
			StringComparison.Ordinal);
		Assert.DoesNotContain("pea\t12(a7)", conditionsAssembly,
			StringComparison.Ordinal);
	}
}
