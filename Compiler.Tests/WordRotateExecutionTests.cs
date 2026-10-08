/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void DeferredWordRotatePreservesHighWordsAcrossBothSuccessors(
		M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var resultIsSource in new[] { false, true })
		foreach (var combineInSavedLeft in new[] { false, true })
		foreach (var takeBranch in new[] { false, true })
		foreach (var sourceWord in new uint[] { 0, 0x8123, 0xFFFF })
		{
			var resultRegister = resultIsSource ? 3 : 5;
			var combined = combineInSavedLeft ? 1 : 0;
			var expectedUpper = combineInSavedLeft ? 0xBBBB0000u : 0xCCCC0000u;
			var expectedWord = ((sourceWord << 5) | (sourceWord >> 11)) + 0xFE;
			foreach (var optimize in new[] { false, true })
			{
				var assembler = new M68kAssembler();
				assembler.EmitWord(0x3003); // MOVE.W D3,D0
				assembler.EmitWord(0xEB48); // LSL.W #5,D0
				assembler.EmitWord(0x3200); // MOVE.W D0,D1
				assembler.EmitWord(0x3003); // MOVE.W D3,D0
				assembler.EmitWord(0xE048); // LSR.W #8,D0
				assembler.EmitWord(0xE648); // LSR.W #3,D0
				assembler.EmitWord((ushort)(combineInSavedLeft ? 0x8240 : 0x8041));
				assembler.EmitWord((ushort)(0xD044 | (combined << 9)));
				assembler.EmitWord((ushort)(0x2000 | (resultRegister << 9) | combined));
				assembler.EmitWord(0x5286); // ADDQ.L #1,D6 kills all checksum flags, including X.
				assembler.EmitBranch(M68kCondition.NotEqual, "taken");
				OverwriteScratchWords(assembler, 0x1234, 0x5678);
				assembler.EmitBranch(M68kCondition.True, "done");
				assembler.Mark("taken");
				OverwriteScratchWords(assembler, 0x9ABC, 0xDEF0);
				assembler.Mark("done");
				assembler.EmitWord(0x4E75);
				if (optimize)
				{
					assembler.OptimizeForCpu(target, peepholeOptimization: M68kPeepholeOptimizationMode.FixedPoint);
					Assert.Contains("rol.w", assembler.RenderAssembly(target), StringComparison.Ordinal);
				}
				var bus = new TestBus();
				assembler.Link(HunkLoadAddress, new Dictionary<string, uint>()).Bytes
					.CopyTo(bus.Memory.AsSpan((int)HunkLoadAddress));
				Execute(bus, model, HunkLoadAddress, initialize: state =>
				{
					state.D[0] = 0xCCCCABCD;
					state.D[1] = 0xBBBB4321;
					state.D[3] = 0xAAAA0000 | sourceWord;
					state.D[4] = 0xFE;
					state.D[6] = takeBranch ? 0u : uint.MaxValue;
				}, afterReturn: state =>
				{
					Assert.Equal(expectedUpper | (expectedWord & 0xFFFF), state.D[resultRegister]);
					Assert.Equal(0xCCCC0000u | (takeBranch ? 0x9ABCu : 0x1234u), state.D[0]);
					Assert.Equal(0xBBBB0000u | (takeBranch ? 0xDEF0u : 0x5678u), state.D[1]);
				});
			}
		}
	}

	private static void OverwriteScratchWords(M68kAssembler assembler, ushort first, ushort second)
	{
		assembler.EmitWord(0x303C); // MOVE.W #first,D0
		assembler.EmitWord(first);
		assembler.EmitWord(0x323C); // MOVE.W #second,D1
		assembler.EmitWord(second);
	}
}
