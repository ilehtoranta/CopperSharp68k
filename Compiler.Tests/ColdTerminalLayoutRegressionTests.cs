/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class ColdTerminalLayoutRegressionTests
{
	[Theory]
	[InlineData(2)]
	[InlineData(4)]
	[InlineData(14)]
	public void DuplicatingTerminalTailPreservesSectionsSuffixBytesAndFixups(int tailLength)
	{
		var fixture = CreateLayout(tailLength);
		var buffer = fixture.Buffer;
		var original = buffer.Bytes.ToArray();
		var tail = original[16..(16 + tailLength)];
		var delta = tailLength - 4;
		var originalData = buffer.DataStartOffset!.Value;
		var originalWritable = buffer.WritableDataStartOffset!.Value;
		var originalBss = buffer.BssStartOffset!.Value;

		Assert.True(Layout(fixture));

		Assert.Equal(original[..8].Concat(tail).Concat(original[12..]), buffer.Bytes);
		Assert.Equal(originalData + delta, buffer.DataStartOffset);
		Assert.Equal(originalWritable + delta, buffer.WritableDataStartOffset);
		Assert.Equal(originalBss + delta, buffer.BssStartOffset);
		Assert.Equal(original[originalData..], buffer.Bytes.Skip(buffer.DataStartOffset!.Value));
		Assert.Equal(8, buffer.Labels["branch-site"]);
		Assert.Equal(8, buffer.Labels["tail"]);
		Assert.Equal(8, buffer.Labels["tail-alias"]);
		Assert.Equal(8 + tailLength, buffer.Labels["cold"]);
		Assert.Equal(10 + tailLength, buffer.Labels["cold-middle"]);
		Assert.Equal(12 + 2 * tailLength, buffer.Labels["method:layout:end"]);
		Assert.Equal(original.Length + delta, buffer.Labels["image-end"]);
		Assert.Equal(new BranchFixup[] {
			new(2, "cold"), new(12 + 2 * tailLength, "entry")
		}, buffer.Branches);
		Assert.Equal(new AddressFixup[] {
			new(18 + 2 * tailLength, "external", true)
		}, buffer.Addresses);
		Assert.Equal(new PcRelativeFixup[] {
			new(24 + 2 * tailLength, "entry")
		}, buffer.PcRelative);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(4)]
	[InlineData(14)]
	public void EndAnchorsFollowTheirBlocksAtSharedFailureAndTailBoundaries(int tailLength)
	{
		var fixture = CreateLayout(tailLength);
		var buffer = fixture.Buffer;
		buffer.AnalysisAnchors.Add("prefix-end", 8);
		buffer.AnalysisAnchors.Add("success-end", 12);
		buffer.AnalysisAnchors.Add("failure-middle-end", 14);
		buffer.AnalysisAnchors.Add("failure-end", 16);
		buffer.AnalysisAnchors.Add("tail-end", 16 + tailLength);
		buffer.AnalysisAnchors.Add("suffix-end", 32 + tailLength);
		buffer.AnalysisAnchors.Add("image-end", 44 + tailLength);

		Assert.True(Layout(fixture));

		Assert.Equal(8, buffer.AnalysisAnchors["prefix-end"]);
		Assert.Equal(8 + tailLength, buffer.AnalysisAnchors["success-end"]);
		Assert.Equal(10 + tailLength, buffer.AnalysisAnchors["failure-middle-end"]);
		Assert.Equal(12 + tailLength, buffer.AnalysisAnchors["failure-end"]);
		Assert.Equal(8 + tailLength, buffer.AnalysisAnchors["tail-end"]);
		Assert.Equal(28 + 2 * tailLength, buffer.AnalysisAnchors["suffix-end"]);
		Assert.Equal(40 + 2 * tailLength, buffer.AnalysisAnchors["image-end"]);
		Assert.Equal(4, buffer.AnalysisAnchors["failure-end"] - buffer.Labels["cold"]);
		Assert.Equal(tailLength, buffer.AnalysisAnchors["tail-end"] - buffer.Labels["tail"]);
	}

	[Theory]
	[InlineData(2)]
	[InlineData(4)]
	[InlineData(14)]
	public void BothTailCopiesKeepEffectsAndTheRemovedBranchLosesItsOverride(int tailLength)
	{
		var fixture = CreateLayout(tailLength);
		var buffer = fixture.Buffer;
		buffer.InstructionEffectOverrides.Add(0, Effect(1));
		buffer.InstructionEffectOverrides.Add(8, Effect(2));
		buffer.InstructionEffectOverrides.Add(12, Effect(3));
		buffer.InstructionEffectOverrides.Add(14, Effect(4));
		for (var offset = 0; offset < tailLength; offset += 2)
			buffer.InstructionEffectOverrides.Add(16 + offset, Effect((ushort)(10 + offset)));
		buffer.InstructionEffectOverrides.Add(16 + tailLength, Effect(50));

		Assert.True(Layout(fixture));

		var expected = new Dictionary<int, M68kInstructionEffects>
		{
			[0] = Effect(1),
			[8 + tailLength] = Effect(3),
			[10 + tailLength] = Effect(4),
			[12 + 2 * tailLength] = Effect(50)
		};
		for (var offset = 0; offset < tailLength; offset += 2)
		{
			expected.Add(8 + offset, Effect((ushort)(10 + offset)));
			expected.Add(12 + tailLength + offset, Effect((ushort)(10 + offset)));
		}
		Assert.Equal(expected.OrderBy(item => item.Key),
			buffer.InstructionEffectOverrides.OrderBy(item => item.Key));
		Assert.DoesNotContain(Effect(2), buffer.InstructionEffectOverrides.Values);
	}

	[Theory]
	[InlineData("branch", 12)]
	[InlineData("branch", 16)]
	[InlineData("address", 12)]
	[InlineData("address", 16)]
	[InlineData("pc-relative", 12)]
	[InlineData("pc-relative", 16)]
	public void RelocationsInsideFailureOrTailStillPreventDuplication(string kind, int offset)
	{
		var fixture = CreateLayout(14);
		var buffer = fixture.Buffer;
		switch (kind)
		{
			case "branch": buffer.Branches.Add(new(offset, "entry")); break;
			case "address": buffer.Addresses.Add(new(offset, "external", true)); break;
			case "pc-relative": buffer.PcRelative.Add(new(offset, "entry")); break;
		}
		var bytes = buffer.Bytes.ToArray();
		var labels = buffer.Labels.ToArray();
		var branches = buffer.Branches.ToArray();
		var addresses = buffer.Addresses.ToArray();
		var pcRelative = buffer.PcRelative.ToArray();

		Assert.False(Layout(fixture));

		Assert.Equal(bytes, buffer.Bytes);
		Assert.Equal(labels, buffer.Labels);
		Assert.Equal(branches, buffer.Branches);
		Assert.Equal(addresses, buffer.Addresses);
		Assert.Equal(pcRelative, buffer.PcRelative);
	}

	public static TheoryData<M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode> NativeCases
	{
		get
		{
			var result = new TheoryData<M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode>();
			foreach (var cpu in CompilerExecutionTests.CpuTargets)
			foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
				result.Add((M68kCpuTarget)cpu[0], (M68kCpuModel)cpu[1], mode);
			return result;
		}
	}

	[Theory]
	[MemberData(nameof(NativeCases))]
	public void NullableListWithCollectOnEveryAllocationReturnsThroughTheRealCallerFrame(
		M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		Assert.Equal(42, CompilerFixtures.ListNullableIntEqualityEntry());
		var compilation = M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(CompilerFixtures).Assembly.Location,
			EntryPoint = $"{typeof(CompilerFixtures).FullName}::ListNullableIntEqualityEntry",
			Cpu = target,
			PeepholeOptimization = mode,
			MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
			GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
			Heap = new M68kHeapOptions { StartAddress = 0x4000, Size = 0x6000 }
		});
		foreach (var stackRemainder in new uint[] { 0, 2 })
		{
			const uint load = 0x10000;
			const uint sentinel = 0x1000;
			var stack = 0x80000u + stackRemainder;
			var bus = new TestBus(0x100000);
			bus.Memory.AsSpan(0x70000, 0x10100).Fill(0xa5);
			compilation.Code.CopyTo(bus.Memory.AsSpan((int)load));
			foreach (var relocation in compilation.Relocations)
			{
				var address = load + (uint)relocation.Offset;
				bus.WriteLong(address, bus.ReadLong(address) + load);
			}
			bus.WriteLong(stack, sentinel);
			using var cpu = M68kCoreFactory.Default.Create(model, bus);
			cpu.Reset(load + compilation.EntryPoint, stack);
			var returnedThroughCaller = false;
			for (var step = 0; step < 200_000 && cpu.State.ProgramCounter != sentinel; step++)
			{
				var pc = cpu.State.ProgramCounter;
				Assert.True(pc >= load && pc + 2 <= load + compilation.Code.Length && (pc & 1) == 0,
					$"{target}/{mode}/SP+{stackRemainder}: escaped the linked image at ${pc:X8}.");
				Assert.InRange(cpu.State.A[7], 0x70010u, stack);
				if (bus.ReadWord(pc) == 0x4e75 && bus.ReadLong(cpu.State.A[7]) == sentinel)
				{
					Assert.Equal(stack, cpu.State.A[7]);
					returnedThroughCaller = true;
				}
				cpu.ExecuteInstruction();
				Assert.False(cpu.State.Halted);
			}
			Assert.True(returnedThroughCaller, "Execution must use the seeded caller return slot.");
			Assert.Equal(sentinel, cpu.State.ProgramCounter);
			Assert.Equal(stack + 4, cpu.State.A[7]);
			Assert.Equal(42u, cpu.State.D[0]);
			Assert.All(bus.Memory.AsSpan(0x70000, 16).ToArray(), value => Assert.Equal((byte)0xa5, value));
			Assert.All(bus.Memory.AsSpan((int)stack + 4, 128).ToArray(), value => Assert.Equal((byte)0xa5, value));
		}
	}

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) CreateLayout(int tailLength)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("entry");
		assembler.EmitWord(0x4a80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "cold");
		assembler.EmitWord(0x722a); // MOVEQ #42,D1
		assembler.Mark("branch-site");
		assembler.EmitBranch(M68kCondition.True, "tail");
		assembler.Mark("cold");
		assembler.EmitWord(0x72ff); // MOVEQ #-1,D1
		assembler.Mark("cold-middle");
		assembler.EmitWord(0x7407); // MOVEQ #7,D2
		assembler.Mark("tail");
		assembler.Mark("tail-alias");
		for (var offset = 0; offset < tailLength - 2; offset += 2)
			assembler.EmitWord(0x4e71); // NOP
		assembler.EmitWord(0x4e75); // RTS
		assembler.Mark("method:layout:end");
		assembler.EmitBsr("entry");
		assembler.EmitJsr("external", external: true);
		assembler.EmitWord(0x41fa); // LEA entry(PC),A0
		assembler.EmitPcRelativeWord("entry");
		assembler.EmitWord(0x4e75);
		assembler.MarkDataStart();
		assembler.EmitLong(0x4e754e75); // Deliberately resembles instructions.
		assembler.MarkWritableDataStart();
		assembler.EmitLong(0x13579bdf);
		assembler.MarkBssStart();
		assembler.EmitLong(0);
		assembler.Mark("image-end");
		var field = typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!;
		return (assembler, (M68kAssemblyBuffer)field.GetValue(assembler)!);
	}

	private static bool Layout((M68kAssembler Assembler, M68kAssemblyBuffer Buffer) fixture)
	{
		var optimizer = new M68kPeepholeOptimizer(fixture.Assembler, fixture.Buffer,
			M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
		var method = typeof(M68kPeepholeOptimizer).GetMethod("TryLayoutColdTerminalBranch",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		return method.CreateDelegate<Func<bool>>(optimizer)();
	}

	private static M68kInstructionEffects Effect(ushort identity) =>
		new(identity, 0, 0, 0, M68kConditionCodeSet.None,
			M68kConditionCodeSet.None, M68kMemorySet.None,
			M68kMemorySet.None, 0, false, true);
}
