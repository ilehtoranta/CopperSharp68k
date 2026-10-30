/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Reflection;
using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kWordBranchWorklistTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly IReadOnlyDictionary<string, uint> Imports = new Dictionary<string, uint> { ["external"] = 0xF00000 };

	[Fact]
	public void PendingIndexesKeepFixupOrderAndExcludeCompletedAndIneligibleBranches()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "missing");
		assembler.EmitDbra(0, "missing");
		assembler.EmitBsr("missing");
		assembler.EmitBranch(M68kCondition.True, "missing");
		assembler.EmitBranch(M68kCondition.NotEqual, "far");
		var buffer = Buffer(assembler);
		buffer.WriteWord(0, 0x6702);
		buffer.Branches[3] = buffer.Branches[3] with { CanRelaxToShort = false };
		buffer.Labels.Add("far", 1000);
		buffer.Branches.Reverse(); // Fixup order need not be physical order.
		var worklist = new M68kAssembler.WordBranchWorklist(assembler);
		Assert.Equal(new[] { 0, 2 }, worklist.GetIndexes());
		buffer.WriteWord(buffer.Branches[2].OpcodeOffset, 0x6102);
		worklist.Shortened();
		Assert.Equal(new[] { 0 }, worklist.GetIndexes());
		Assert.Equal(5, buffer.Branches.Count); // Linking still needs short fixups.
	}

	[Fact]
	public void AppendedCallsFollowExistingFixupsEvenAtAnEarlierPhysicalOffset()
	{
		var assembler = new M68kAssembler();
		assembler.EmitJsr("callee", external: false);
		assembler.EmitBranch(M68kCondition.Equal, "callee");
		assembler.EmitWord(0x4E71);
		assembler.Mark("callee"); assembler.EmitWord(0x4E75);
		var buffer = Buffer(assembler);
		var worklist = new M68kAssembler.WordBranchWorklist(assembler);
		Assert.True(Step(assembler, "TryRelaxLocalAbsoluteControlTransfer", worklist));
		Assert.Equal(new[] { 0, 1 }, worklist.GetIndexes());
		Assert.True(buffer.Branches[0].OpcodeOffset > buffer.Branches[1].OpcodeOffset);
		Assert.True(Step(assembler, "TryRelaxShortBranches", worklist));
		Assert.Empty(worklist.GetIndexes());
		Assert.Equal(2, buffer.Branches.Count);
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	public void ExplicitRemovalAdjustsAllLaterPendingIndexes(int removed)
	{
		var assembler = FiveBranches();
		var buffer = Buffer(assembler);
		var worklist = new M68kAssembler.WordBranchWorklist(assembler);
		var offset = buffer.Branches[removed].OpcodeOffset;
		buffer.Branches.RemoveAt(removed); worklist.Removed(removed);
		buffer.RemoveBytes(offset, 4);
		Assert.Equal(new[] { 0, 2, 4 }.Where(i => i != removed).Select(i => i > removed ? i - 1 : i), worklist.GetIndexes());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CompactionRebuildsIndexesIncludingWhenAnAppendRestoresTheOldCount(bool append)
	{
		var assembler = FiveBranches();
		var buffer = Buffer(assembler);
		var worklist = new M68kAssembler.WordBranchWorklist(assembler);
		buffer.RemoveByteRanges([(4, 4)]);
		if (append) { assembler.EmitBsr("missing"); worklist.Appended(); }
		Assert.Equal(append ? new[] { 0, 1, 3, 4 } : new[] { 0, 1, 3 }, worklist.GetIndexes());
	}

	[Fact]
	public void OffsetRebasesRetainIndexesAndUseCurrentSourceOffsets()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E71); assembler.EmitBsr("callee");
		assembler.EmitWord(0x4E71); assembler.Mark("callee"); assembler.EmitWord(0x4E75);
		var buffer = Buffer(assembler);
		var worklist = new M68kAssembler.WordBranchWorklist(assembler);
		buffer.InsertBytes(0, 4); buffer.RemoveBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		Assert.Equal(new[] { 0 }, worklist.GetIndexes());
		Assert.True(Step(assembler, "TryRelaxShortBranches", worklist));
		Assert.Equal(0x6102, buffer.ReadWord(4));
		Assert.Empty(worklist.GetIndexes());
	}

	[Fact]
	public void PendingOutOfRangeBranchIsRetriedAfterLocalCallsShrink()
	{
		var assembler = new M68kAssembler();
		assembler.EmitBranch(M68kCondition.Equal, "callee");
		for (var i = 0; i < 24; i++) assembler.EmitJsr("callee", external: false);
		assembler.EmitWord(0x4E71); assembler.Mark("callee"); assembler.EmitWord(0x4E75);
		var linked = assembler.Link(0, Imports);
		Assert.NotEqual(0, linked.Bytes[1]);
		Assert.Equal(0x67, linked.Bytes[0]);
		Assert.All(Buffer(assembler).Branches, branch => Assert.NotEqual(0, linked.Bytes[branch.OpcodeOffset + 1]));
	}

	[Fact]
	public void ConvertedJumpsRemainWordBranchesWhileConvertedCallsCanShorten()
	{
		var assembler = new M68kAssembler();
		assembler.EmitJmp("callee", external: false); assembler.EmitJsr("callee", external: false);
		assembler.EmitWord(0x4E75); assembler.Mark("callee"); assembler.EmitWord(0x4E75);
		var linked = assembler.Link(0, Imports);
		Assert.Equal(0x6000, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		Assert.Equal(0x6102, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(4)));
	}

	[Fact]
	public void UndefinedTargetCanBeDefinedAfterAFailedLinkAndRelaxedOnRetry()
	{
		var assembler = new M68kAssembler(); assembler.EmitBsr("late"); assembler.EmitWord(0x4E75);
		Assert.Throws<InvalidOperationException>(() => assembler.Link(0, Imports));
		assembler.Mark("late"); assembler.EmitWord(0x4E75);
		Assert.Equal(0x6102, BinaryPrimitives.ReadUInt16BigEndian(assembler.Link(0, Imports).Bytes));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void ZeroShortDisplacementRetainsWordCallAndItsMc68000ReturnAddress(bool absolute)
	{
		var assembler = new M68kAssembler();
		if (absolute) assembler.EmitJsr("next", external: false);
		else assembler.EmitBsr("next");
		assembler.Mark("next"); assembler.EmitWord(0x4E75);
		var linked = assembler.Link(0x10000, Imports);
		Assert.Equal(new byte[] { 0x61, 0, 0, 2, 0x4E, 0x75 }, linked.Bytes);
		var bus = new TestBus(); linked.Bytes.CopyTo(bus.Memory.AsSpan(0x10000));
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(0x10000, 0x80000); cpu.ExecuteInstruction();
		Assert.Equal(0x10004u, cpu.State.ProgramCounter);
		Assert.Equal(0x7FFFCu, cpu.State.A[7]);
		Assert.Equal(0x10004u, bus.ReadLong(0x7FFFC));
	}

	[Fact]
	public void LaterInvocationsObserveSameCountTargetAndEligibilityChanges()
	{
		var assembler = new M68kAssembler(); assembler.EmitBsr("far"); assembler.EmitWord(0x4E75);
		assembler.Mark("near"); assembler.EmitWord(0x4E75);
		for (var i = 0; i < 80; i++) assembler.EmitWord(0x4E71);
		assembler.Mark("far"); assembler.EmitWord(0x4E75);
		assembler.RelaxFinalLayout();
		var buffer = Buffer(assembler);
		buffer.Branches[0] = buffer.Branches[0] with { Target = "near", CanRelaxToShort = false };
		assembler.RelaxFinalLayout(); Assert.Equal(0x6100, buffer.ReadWord(0));
		buffer.Branches[0] = buffer.Branches[0] with { CanRelaxToShort = true };
		assembler.RelaxFinalLayout(); Assert.Equal(0x6102, buffer.ReadWord(0));
	}

	[Theory]
	[InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
	[InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
	[InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
	public void OrderedWorklistMatchesFullScanScheduleForMixedLayouts(int seed)
	{
		var reference = MixedLayout(seed); var candidate = MixedLayout(seed);
		FullScanFinalLayout(reference); candidate.RelaxFinalLayout();
		Assert.Equal(Snapshot(reference), Snapshot(candidate));
		var expected = reference.Link(0x10000, Imports);
		var actual = candidate.Link(0x10000, Imports);
		Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
		Assert.Equal(reference.RenderAssembly(M68kCpuTarget.M68000), candidate.RenderAssembly(M68kCpuTarget.M68000));
		Assert.Equal(Snapshot(reference), Snapshot(candidate));
	}

	[Theory]
	[InlineData(false, false)] [InlineData(false, true)]
	[InlineData(true, false)] [InlineData(true, true)]
	public void RelaxedCallsAndBranchesKeepMc68000RegistersFlagsAndStack(bool aligned, bool takeBranch)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); // TST.L D0
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitJsr("callee", external: false); assembler.MarkReturnAddress("resume");
		assembler.EmitWord(0x5281); assembler.EmitBranch(M68kCondition.True, "done"); // ADDQ.L #1,D1
		assembler.Mark("zero"); assembler.EmitWord(0x72FF);
		assembler.Mark("done"); assembler.EmitWord(0x4E75);
		assembler.Mark("callee"); assembler.EmitWord(0x7229); assembler.EmitWord(0x4E75);
		if (aligned) assembler.RequestLongAlignment("callee");
		// Patch the original absolute/word forms without running relaxation.
		var original = Buffer(assembler).Bytes.ToArray();
		foreach (var branch in Buffer(assembler).Branches)
			BinaryPrimitives.WriteInt16BigEndian(original.AsSpan(branch.OpcodeOffset + 2), (short)(Buffer(assembler).Labels[branch.Target] - branch.OpcodeOffset - 2));
		foreach (var address in Buffer(assembler).Addresses)
			BinaryPrimitives.WriteUInt32BigEndian(original.AsSpan(address.Offset), 0x10000u + (uint)Buffer(assembler).Labels[address.Target]);
		var linked = assembler.Link(0x10000, Imports);
		Assert.True(linked.Bytes.Length < original.Length);
		var expected = Execute(original, takeBranch ? 0u : 1u);
		Assert.Equal(expected, Execute(linked.Bytes, takeBranch ? 0u : 1u));
		Assert.Equal(takeBranch ? uint.MaxValue : 42u, expected.D1);
		Assert.Equal(0x80004u, expected.Sp);
	}

	private static M68kAssembler FiveBranches()
	{
		var assembler = new M68kAssembler();
		for (var i = 0; i < 5; i++) assembler.EmitBsr("missing");
		var buffer = Buffer(assembler);
		foreach (var i in new[] { 1, 3 }) buffer.Branches[i] = buffer.Branches[i] with { CanRelaxToShort = false };
		return assembler;
	}

	private static M68kAssembler MixedLayout(int seed)
	{
		var random = new Random(seed); var assembler = new M68kAssembler();
		for (var i = 0; i < 64; i++)
		{
			assembler.Mark("block:" + i);
			if (i % 11 == seed % 11) assembler.RequestLongAlignment("block:" + i);
			Buffer(assembler).AnalysisAnchors.Add("anchor:" + i, assembler.Offset);
			Buffer(assembler).InstructionEffectOverrides.Add(assembler.Offset,
				default(M68kInstructionEffects) with { UsesData = (ushort)(1 << (i % 8)), StackDelta = i });
			var target = "block:" + random.Next(64);
			switch (random.Next(8))
			{
				case 0: assembler.EmitBranch(M68kCondition.Equal, target); break;
				case 1: assembler.EmitBsr(target); break;
				case 2: assembler.EmitJsr(target, external: false); assembler.MarkReturnAddress("return:" + i); break;
				case 3: assembler.EmitJmp(target, external: false); break;
				case 4: assembler.EmitDbra(0, target); break;
				case 5: assembler.EmitWord(0x41F9); assembler.EmitAddress("rodata"); break;
				case 6: assembler.EmitBranch(M68kCondition.NotEqual, "block:" + (i + 1)); break;
				default: assembler.EmitWord(0x41FA); assembler.EmitPcRelativeWord("rodata"); break;
			}
			for (var n = random.Next(3); n > 0; n--) assembler.EmitWord(0x4E71);
		}
		assembler.Mark("block:64"); assembler.EmitWord(0x4E75);
		assembler.EmitJsr("external", external: true);
		assembler.MarkDataStart(); assembler.Mark("rodata"); assembler.EmitAddress("block:0");
		assembler.MarkWritableDataStart(); assembler.EmitAddress("block:32");
		assembler.MarkBssStart(); assembler.EmitLong(0);
		if (seed % 2 != 0) Buffer(assembler).Branches.Reverse();
		return assembler;
	}

	private static void FullScanFinalLayout(M68kAssembler assembler)
	{
		assembler.ApplyRequestedAlignments();
		while (Step(assembler, "TryRelaxShortBranches", (object?)null) ||
			Step(assembler, "TryRelaxLocalAbsoluteControlTransfer", (object?)null) ||
			Step(assembler, "TryRemoveBranchToNextInstruction", (object?)null) ||
			Step(assembler, "TryRelaxLocalAbsoluteLoad")) assembler.ApplyRequestedAlignments();
	}
	private static bool Step(M68kAssembler assembler, string name, params object?[] arguments)
	{
		var method = typeof(M68kAssembler).GetMethod(name, PrivateInstance)!;
		var defaults = method.GetParameters().Skip(arguments.Length).Select(parameter => parameter.DefaultValue);
		return (bool)method.Invoke(assembler, arguments.Concat(defaults).ToArray())!;
	}
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!.GetValue(assembler)!;
	private static string Snapshot(M68kAssembler assembler)
	{
		var buffer = Buffer(assembler);
		return JsonSerializer.Serialize(new { Bytes = buffer.Bytes.ToArray(), Labels = buffer.Labels.ToArray(), Anchors = buffer.AnalysisAnchors.ToArray(),
			buffer.Branches, buffer.Addresses, buffer.PcRelative, buffer.InstructionEffectOverrides,
			buffer.DataStartOffset, buffer.WritableDataStartOffset, buffer.BssStartOffset });
	}
	private static (uint D0, uint D1, uint Sp, ushort Flags) Execute(byte[] code, uint input)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[0] = input;
		for (var i = 0; i < 64 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
