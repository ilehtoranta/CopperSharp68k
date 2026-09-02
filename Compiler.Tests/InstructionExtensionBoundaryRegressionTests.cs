/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class InstructionExtensionBoundaryRegressionTests
{
	public static TheoryData<string, M68kCpuTarget, M68kCpuModel> Cases
	{
		get
		{
			var result = new TheoryData<string, M68kCpuTarget, M68kCpuModel>();
			foreach (var (target, model) in new[] {
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040),
				// Match the production test matrix: 68060 integer output uses the 68040 emulator.
				(M68kCpuTarget.M68060, M68kCpuModel.M68040) })
			{
				if (target != M68kCpuTarget.M68020)
				{
					result.Add("TryFoldMoveQuickIntoCompareImmediate", target, model);
					result.Add("TryFoldCopyMoveQuickCompare", target, model);
				}
				result.Add("TryNarrowZeroExtendedRegisterAnd", target, model);
				result.Add("TryBypassTerminalStackReloadOnFallthrough", target, model);
			}
			return result;
		}
	}

	[Theory]
	[MemberData(nameof(Cases))]
	public void GrowingAnInstructionOrAddingAFallthroughBranchPreservesOtherPredecessors(
		string rewrite, M68kCpuTarget target, M68kCpuModel model)
	{
		// Execute an independent original fixture; linking must not mutate the
		// candidate's unrelaxed instruction lengths before invoking the rewrite.
		VerifyExecution(CreateFixture(rewrite).Assembler, rewrite, model);
		var fixture = CreateFixture(rewrite);
		var flow = M68kInstructionDataflow.Analyze(fixture.Assembler);
		var optimizer = new M68kPeepholeOptimizer(fixture.Assembler, fixture.Buffer,
			target, M68kClrPolicy.Auto, []);
		var method = typeof(M68kPeepholeOptimizer).GetMethod(rewrite,
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		Assert.True(method.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer)(flow),
			$"The fixture must exercise {rewrite}.");

		var following = fixture.Buffer.Labels["following"];
		var expectedOpcode = rewrite switch
		{
			"TryNarrowZeroExtendedRegisterAnd" => 0x5280, // ADDQ.L #1,D0
			"TryBypassTerminalStackReloadOnFallthrough" => 0x202f, // MOVE.L d16(A7),D0
			_ => 0x6700 // BEQ.W
		};
		Assert.Equal(expectedOpcode, fixture.Buffer.ReadWord(following));
		Assert.Equal(following, fixture.Buffer.Labels["following-alias"]);
		Assert.Equal(following, fixture.Buffer.AnalysisAnchors["preceding-block-end"]);
		Assert.Contains(fixture.Assembler.GetInstructionStream(),
			instruction => instruction.Offset == following && instruction.IsDecoded);
		Assert.Equal(fixture.Buffer.Labels["method:boundary:end"],
			fixture.Buffer.DataStartOffset);
		VerifyExecution(fixture.Assembler, rewrite, model);
	}

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) CreateFixture(string rewrite)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("method:boundary");
		assembler.EmitWord(0x2f02); // MOVE.L D2,-(A7)
		var reload = rewrite == "TryBypassTerminalStackReloadOnFallthrough";
		var wordMask = rewrite == "TryNarrowZeroExtendedRegisterAnd";
		if (reload)
		{
			assembler.EmitWord(0x518f); // SUBQ.L #8,A7
			assembler.EmitWord(0x2f7c); // MOVE.L #saved,4(A7)
			assembler.EmitLong(0x11223344);
			assembler.EmitWord(4);
		}
		assembler.EmitWord(0x4a87); // TST.L D7
		assembler.EmitBranch(M68kCondition.NotEqual, "following");
		if (reload)
		{
			assembler.EmitWord(0x2f40); // MOVE.L D0,4(A7)
			assembler.EmitWord(4);
		}
		else if (wordMask)
		{
			assembler.EmitWord(0x7000); // MOVEQ #0,D0
			assembler.EmitWord(0x3006); // MOVE.W D6,D0
			assembler.EmitWord(0x223c); // MOVE.L #$7fff,D1
			assembler.EmitLong(0x7fff);
			assembler.EmitWord(0xc081); // AND.L D1,D0
		}
		else if (rewrite == "TryFoldCopyMoveQuickCompare")
		{
			assembler.EmitWord(0x2400); // MOVE.L D0,D2
			assembler.EmitWord(0x7207); // MOVEQ #7,D1
			assembler.EmitWord(0xb481); // CMP.L D1,D2
		}
		else
		{
			assembler.EmitWord(0x7207); // MOVEQ #7,D1
			assembler.EmitWord(0xb081); // CMP.L D1,D0
		}
		assembler.MarkAnalysisAnchor("preceding-block-end");
		assembler.Mark("following");
		assembler.Mark("following-alias");
		if (reload)
		{
			assembler.EmitWord(0x202f); // MOVE.L 4(A7),D0
			assembler.EmitWord(4);
			assembler.EmitWord(0x508f); // ADDQ.L #8,A7
		}
		else if (wordMask)
		{
			assembler.EmitWord(0x5280); // ADDQ.L #1,D0
		}
		else
		{
			assembler.EmitBranch(M68kCondition.Equal, "equal");
			assembler.EmitWord(0x70ff); // MOVEQ #-1,D0
			assembler.EmitBranch(M68kCondition.True, "exit");
			assembler.Mark("equal");
			assembler.EmitWord(0x702a); // MOVEQ #42,D0
		}
		assembler.Mark("exit");
		assembler.EmitWord(0x7200); // The compare/mask temporary is dead at the return boundary.
		assembler.EmitWord(0x241f); // MOVE.L (A7)+,D2
		assembler.EmitWord(0x4e75);
		assembler.Mark("method:boundary:end");
		assembler.MarkDataStart();
		var field = typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!;
		return (assembler, (M68kAssemblyBuffer)field.GetValue(assembler)!);
	}

	private static void VerifyExecution(M68kAssembler assembler, string rewrite, M68kCpuModel model)
	{
		var image = assembler.Link(0, new Dictionary<string, uint>());
		var starts = assembler.GetInstructionStream().Select(instruction => instruction.Offset).ToHashSet();
		foreach (var takeOtherPredecessor in new uint[] { 0, 1 })
		foreach (var stackRemainder in new uint[] { 0, 2 })
		{
			const uint load = 0x10000;
			const uint sentinel = 0x1000;
			var stack = 0x80000u + stackRemainder;
			var bus = new TestBus(0x100000);
			bus.Memory.AsSpan((int)stack - 256, 388).Fill(0xa5);
			image.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
			bus.WriteLong(stack, sentinel);
			using var cpu = M68kCoreFactory.Default.Create(model, bus);
			cpu.Reset(load, stack);
			cpu.State.D[0] = 7;
			cpu.State.D[2] = 0x13579bdf;
			cpu.State.D[6] = 0x82fa;
			cpu.State.D[7] = takeOtherPredecessor;
			var returnedThroughCaller = false;
			for (var step = 0; step < 100 && cpu.State.ProgramCounter != sentinel; step++)
			{
				var pc = cpu.State.ProgramCounter;
				Assert.True(pc >= load && pc < load + image.Bytes.Length);
				Assert.Contains((int)(pc - load), starts);
				if (bus.ReadWord(pc) == 0x4e75 && bus.ReadLong(cpu.State.A[7]) == sentinel)
				{
					Assert.Equal(stack, cpu.State.A[7]);
					returnedThroughCaller = true;
				}
				cpu.ExecuteInstruction();
				Assert.False(cpu.State.Halted);
			}
			var expected = rewrite switch
			{
				"TryNarrowZeroExtendedRegisterAnd" => takeOtherPredecessor == 0 ? 0x2fbu : 8u,
				"TryBypassTerminalStackReloadOnFallthrough" => takeOtherPredecessor == 0 ? 7u : 0x11223344u,
				_ => takeOtherPredecessor == 0 ? 42u : uint.MaxValue
			};
			Assert.True(returnedThroughCaller);
			Assert.Equal(expected, cpu.State.D[0]);
			Assert.Equal(sentinel, cpu.State.ProgramCounter);
			Assert.Equal(stack + 4, cpu.State.A[7]);
			Assert.Equal(0x13579bdfu, cpu.State.D[2]);
			Assert.Equal(0x82fau, cpu.State.D[6]);
			Assert.Equal(takeOtherPredecessor, cpu.State.D[7]);
			Assert.All(bus.Memory.AsSpan((int)stack - 256, 128).ToArray(), value => Assert.Equal((byte)0xa5, value));
			Assert.All(bus.Memory.AsSpan((int)stack + 4, 128).ToArray(), value => Assert.Equal((byte)0xa5, value));
		}
	}
}
