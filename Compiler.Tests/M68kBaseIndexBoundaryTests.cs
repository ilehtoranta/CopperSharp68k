/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kBaseIndexBoundaryTests
{
	[Theory]
	[InlineData(1, 0)]
	[InlineData(2, 0)]
	[InlineData(4, 0)]
	[InlineData(1, 1)]
	[InlineData(2, 1)]
	[InlineData(4, 1)]
	[InlineData(4, 2)]
	[InlineData(4, 3)]
	public void GrowingMemoryAccessKeepsFollowingEntriesAndEndAnchorsAtAnInstructionBoundary(
		int width, int accessKind)
	{
		var fixture = CreateFixture(width, accessKind, includeSuffix: true);
		var buffer = fixture.Buffer;
		var original = buffer.Bytes.ToArray();
		var oldEnd = buffer.Labels["after-access"];
		var oldData = buffer.DataStartOffset!.Value;
		var oldWritable = buffer.WritableDataStartOffset!.Value;
		var oldBss = buffer.BssStartOffset!.Value;
		var originalBranches = buffer.Branches.ToArray();
		var originalAddresses = buffer.Addresses.ToArray();
		var originalPcRelative = buffer.PcRelative.ToArray();
		var originalEffects = buffer.InstructionEffectOverrides.ToArray();

		Assert.True(Fold(fixture, M68kCpuTarget.M68000));

		// Replacing MOVEA+ADDA with one extension saves two bytes. The following
		// entry still denotes MOVEQ, never the indexed access's extension word.
		Assert.Equal(original.Length - 2, buffer.Bytes.Count);
		Assert.Equal(oldEnd - 2, buffer.Labels["after-access"]);
		Assert.Equal(oldEnd - 2, buffer.Labels["after-access-alias"]);
		Assert.Equal(oldEnd - 2, buffer.AnalysisAnchors["access-end"]);
		Assert.Equal(0x7400, buffer.ReadWord(buffer.Labels["after-access"]));
		Assert.Equal(oldData - 2, buffer.DataStartOffset);
		Assert.Equal(oldWritable - 2, buffer.WritableDataStartOffset);
		Assert.Equal(oldBss - 2, buffer.BssStartOffset);
		Assert.Equal(original[oldEnd..], buffer.Bytes.Skip(oldEnd - 2));
		Assert.Equal(originalBranches.Select(branch => branch.OpcodeOffset >= oldEnd
			? branch with { OpcodeOffset = branch.OpcodeOffset - 2 } : branch), buffer.Branches);
		Assert.Equal(originalAddresses.Select(address =>
			address with { Offset = address.Offset - 2 }), buffer.Addresses);
		Assert.Equal(originalPcRelative.Select(reference =>
			reference with { DisplacementOffset = reference.DisplacementOffset - 2 }), buffer.PcRelative);
		Assert.Equal(originalEffects.Select(effect =>
			new KeyValuePair<int, M68kInstructionEffects>(effect.Key - 2, effect.Value)),
			buffer.InstructionEffectOverrides);
		var instructionStarts = fixture.Assembler.GetInstructionStream()
			.Select(instruction => instruction.Offset).ToHashSet();
		Assert.Contains(buffer.Labels["after-access"], instructionStarts);
	}

	[Fact]
	public void DefaultInsertionKeepsEntryAndEndAnchorsOnInsertedPrefix()
	{
		var buffer = new M68kAssemblyBuffer();
		buffer.Bytes.AddRange([0x4e, 0x71, 0x70, 0x2a, 0x4e, 0x75]);
		buffer.Labels.Add("entry", 2);
		buffer.AnalysisAnchors.Add("previous-end", 2);
		buffer.Labels.Add("return", 4);

		buffer.InsertBytes(2, 4);

		Assert.Equal(2, buffer.Labels["entry"]);
		Assert.Equal(2, buffer.AnalysisAnchors["previous-end"]);
		Assert.Equal(8, buffer.Labels["return"]);
		Assert.Equal(0x702a, buffer.ReadWord(6));
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void TakenAndFallthroughPathsPreserveMemoryAndStackAfterIndexedAccessGrowth(
		M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var stackResidue in new uint[] { 0, 2 })
		foreach (var taken in new[] { false, true })
		{
			var disabled = CreateFixture(1, 0, includeSuffix: false);
			var optimized = CreateFixture(1, 0, includeSuffix: false);
			Assert.True(Fold(optimized, target));
			var expected = Execute(disabled.Assembler, model, stackResidue, taken);
			var actual = Execute(optimized.Assembler, model, stackResidue, taken);
			Assert.Equal(42u, expected.Result);
			Assert.Equal(0u, expected.StoredIndex);
			Assert.Equal(taken ? (byte)0xa7 : (byte)0x5a, expected.StoredCharacter);
			Assert.Equal(expected, actual);
		}
	}

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void FullOptimizationPreservesBothEntriesToTheSharedContinuation(
		M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var taken in new[] { false, true })
		{
			var disabled = CreateFixture(1, 0, includeSuffix: false);
			var optimized = CreateFixture(1, 0, includeSuffix: false);
			optimized.Assembler.OptimizeForCpu(target, clrPolicy: M68kClrPolicy.Always);
			Assert.Equal(Execute(disabled.Assembler, model, 2, taken),
				Execute(optimized.Assembler, model, 2, taken));
		}
	}

	public static TheoryData<M68kCpuTarget, M68kCpuModel> CpuTargets => new()
	{
		{ M68kCpuTarget.M68000, M68kCpuModel.M68000 },
		{ M68kCpuTarget.M68020, M68kCpuModel.M68020 },
		{ M68kCpuTarget.M68040, M68kCpuModel.M68040 },
		{ M68kCpuTarget.M68060, M68kCpuModel.M68040 }
	};

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) CreateFixture(
		int width, int accessKind, bool includeSuffix)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("entry");
		assembler.EmitWord(0x4a86); // TST.L D6
		assembler.EmitBranch(M68kCondition.Equal, "after-access");
		assembler.EmitWord(0x204a); // MOVEA.L A2,A0
		assembler.EmitWord(0xd1c0); // ADDA.L D0,A0
		var size = width == 1 ? 0x1000 : width == 2 ? 0x3000 : 0x2000;
		assembler.EmitWord((ushort)(accessKind switch
		{
			0 => size | 0x0081, // MOVE.[BWL] D1,(A0)
			1 => size | 0x0610, // MOVE.[BWL] (A0),D3
			2 => size | 0x0097, // MOVE.L (A7),(A0)
			_ => size | 0x00af // MOVE.L 4(A7),(A0)
		}));
		if (accessKind == 3) assembler.EmitWord(4);
		assembler.Mark("after-access");
		assembler.Mark("after-access-alias");
		assembler.MarkAnalysisAnchor("access-end");
		assembler.EmitWord(0x7400); // MOVEQ #0,D2; same reversal-index reset as RawDoFmt
		assembler.EmitWord(0x204c); // MOVEA.L A4,A0; temporary is dead
		assembler.EmitWord(0x2682); // MOVE.L D2,(A3); observe the shared continuation
		assembler.EmitWord(0x702a); // MOVEQ #42,D0
		assembler.EmitWord(0x4e75); // RTS
		if (includeSuffix)
		{
			assembler.EmitBsr("entry");
			assembler.EmitJsr("external", external: true);
			assembler.EmitWord(0x41fa); // LEA after-access(PC),A0
			assembler.EmitPcRelativeWord("after-access");
			assembler.EmitWord(0x4e75);
			assembler.MarkDataStart();
			assembler.EmitAddress("after-access");
			assembler.MarkWritableDataStart();
			assembler.EmitLong(0x13579bdf);
			assembler.MarkBssStart();
			assembler.EmitLong(0);
		}
		var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(assembler)!;
		if (includeSuffix)
		{
			foreach (var instruction in assembler.GetInstructionStream()
				.Where(instruction => instruction.Offset == buffer.Labels["after-access"] ||
					instruction.Offset == buffer.Labels["after-access"] + 2))
				buffer.InstructionEffectOverrides.Add(instruction.Offset,
					M68kInstructionDataflow.GetEffects(instruction));
		}
		return (assembler, buffer);
	}

	private static bool Fold(
		(M68kAssembler Assembler, M68kAssemblyBuffer Buffer) fixture, M68kCpuTarget target)
	{
		var optimizer = new M68kPeepholeOptimizer(fixture.Assembler, fixture.Buffer,
			target, M68kClrPolicy.Always, []);
		var method = typeof(M68kPeepholeOptimizer).GetMethod("TryFoldBaseIndexMemoryAccess",
			BindingFlags.Instance | BindingFlags.NonPublic)!;
		return method.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer)(
			M68kInstructionDataflow.Analyze(fixture.Assembler));
	}

	private static (uint Result, uint StoredIndex, byte StoredCharacter, uint Stack) Execute(
		M68kAssembler assembler, M68kCpuModel model, uint stackResidue, bool taken)
	{
		const uint code = 0x10000, characterBase = 0x4000, indexAddress = 0x5000;
		const uint returnSentinel = 0x1000;
		var stack = 0x80000 + stackResidue;
		var linked = assembler.Link(code, new Dictionary<string, uint>());
		var bus = new TestBus();
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)code));
		bus.Memory[(int)characterBase + 4] = 0xa7;
		bus.WriteLong(indexAddress, 0xdeadbeef);
		bus.WriteLong(stack, returnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(code, stack);
		cpu.State.D[0] = 4;
		cpu.State.D[1] = 0x5a;
		cpu.State.D[2] = 52; // stale final digit when the reset is skipped
		cpu.State.D[6] = taken ? 0u : 1u;
		cpu.State.A[2] = characterBase;
		cpu.State.A[3] = indexAddress;
		cpu.State.A[4] = 0x6000;
		for (var instruction = 0; instruction < 64 &&
			cpu.State.ProgramCounter != returnSentinel; instruction++)
		{
			Assert.InRange(cpu.State.ProgramCounter, code, code + (uint)linked.Bytes.Length - 2);
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(returnSentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		return (cpu.State.D[0], bus.ReadLong(indexAddress),
			bus.Memory[(int)characterBase + 4], cpu.State.A[7]);
	}
}
