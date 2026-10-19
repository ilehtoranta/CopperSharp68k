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

public sealed class M68kRelaxationRetryBudgetTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly IReadOnlyDictionary<string, uint> Imports = new Dictionary<string, uint> { ["external"] = 0xF00000 };

	[Theory]
	[InlineData(false, 128, 1)] [InlineData(false, 129, 1)] [InlineData(false, 130, 1)]
	[InlineData(false, 131, 2)] [InlineData(false, 136, 7)]
	[InlineData(false, -129, 1)] [InlineData(false, -130, 1)] [InlineData(false, -131, 1)]
	[InlineData(false, -132, 2)] [InlineData(false, -137, 7)]
	[InlineData(true, 32768, 1)] [InlineData(true, 32769, 1)] [InlineData(true, 32770, 1)]
	[InlineData(true, 32771, 2)] [InlineData(true, 32776, 7)]
	[InlineData(true, -32769, 1)] [InlineData(true, -32770, 1)] [InlineData(true, -32771, 1)]
	[InlineData(true, -32772, 2)] [InlineData(true, -32777, 7)]
	public void RetryWakesAtTheConservativeDeletionBound(bool absolute, int displacement, int needed)
	{
		var assembler = BudgetLayout(absolute);
		var budget = Budget(assembler, absolute);
		Assert.True(budget.ShouldRetry());
		budget.Defer(displacement, 128);
		Assert.False(budget.ShouldRetry());
		if (needed > 1)
		{
			Buffer(assembler).RemoveBytes(assembler.Offset - needed + 1, needed - 1);
			Assert.False(budget.ShouldRetry());
		}
		Buffer(assembler).RemoveBytes(assembler.Offset - 1, 1);
		Assert.True(budget.ShouldRetry()); // Even unrelated deletions must wake it early enough.
	}

	[Theory]
	[InlineData(false, -128)] [InlineData(false, 0)] [InlineData(false, 127)]
	[InlineData(true, -32768)] [InlineData(true, 0)] [InlineData(true, 32767)]
	public void ZeroAndInRangeDisplacementsAreNotDeferred(bool absolute, int displacement)
	{
		var assembler = BudgetLayout(absolute); var budget = Budget(assembler, absolute);
		budget.Defer(displacement, 128);
		Assert.True(budget.ShouldRetry());
	}

	[Theory]
	[InlineData(false, -1)] [InlineData(false, 1000)]
	[InlineData(true, -1)] [InlineData(true, 1000)]
	public void OutOfImageTargetsAreNotDeferred(bool absolute, int target)
	{
		var assembler = BudgetLayout(absolute); var budget = Budget(assembler, absolute);
		budget.Defer(absolute ? 33000 : 200, target);
		Assert.True(budget.ShouldRetry());
	}

	[Theory]
	[InlineData(false, false)] [InlineData(false, true)]
	[InlineData(true, false)] [InlineData(true, true)]
	public void RequestedAlignmentDisablesDeferralEvenAfterABudgetWasRecorded(bool absolute, bool requestFirst)
	{
		var assembler = BudgetLayout(absolute);
		if (requestFirst) assembler.RequestLongAlignment("entry");
		var budget = Budget(assembler, absolute); budget.Defer(absolute ? 33000 : 200, 128);
		Assert.Equal(requestFirst, budget.ShouldRetry());
		assembler.RequestLongAlignment("entry");
		Assert.True(budget.ShouldRetry());
	}

	[Fact]
	public void AbsoluteWorklistPreservesRecordOrderAndFiltersOnlyPermanentCandidates()
	{
		var assembler = new M68kAssembler();
		assembler.EmitJsr("missing", false); assembler.EmitJmp("missing", false);
		assembler.EmitJsr("external", true);
		assembler.EmitWord(0x41F9); assembler.EmitAddress("missing");
		assembler.EmitJsr("missing", false);
		var buffer = Buffer(assembler);
		buffer.Addresses[4] = buffer.Addresses[4] with { CanRelaxToPcRelative = false };
		buffer.Addresses.Reverse();
		var worklist = new M68kAssembler.AbsoluteTransferWorklist(assembler);
		Assert.Equal(new[] { 3, 4 }, worklist.GetIndexes());
		Assert.True(worklist.ShouldRetry(3)); // Unresolved targets remain pending.
	}

	[Theory]
	[InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)]
	public void AbsoluteRemovalKeepsTheBudgetsAttachedToTheSurvivingRecords(int removed)
	{
		var assembler = new M68kAssembler();
		for (var i = 0; i < 5; i++) assembler.EmitJsr("target", false);
		for (var i = 0; i < 128; i++) assembler.EmitWord(0x4E71);
		var buffer = Buffer(assembler); var worklist = new M68kAssembler.AbsoluteTransferWorklist(assembler);
		worklist.Defer(1, 33000, 128); worklist.Defer(3, 33000, 128);
		var offset = buffer.Addresses[removed].Offset - 2;
		buffer.Addresses.RemoveAt(removed); worklist.Removed(removed); buffer.RemoveBytes(offset, 6);
		Assert.Equal(Enumerable.Range(0, 4), worklist.GetIndexes());
		foreach (var original in Enumerable.Range(0, 5).Where(i => i != removed))
			Assert.Equal(original is not 1 and not 3, worklist.ShouldRetry(original > removed ? original - 1 : original));
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public void UnexpectedCompactionRebuildsIndexesAndDiscardsBudgets(bool absolute)
	{
		var assembler = BudgetLayout(absolute);
		if (absolute) assembler.EmitJsr("target", false); else assembler.EmitBsr("target");
		var buffer = Buffer(assembler);
		if (absolute)
		{
			var worklist = new M68kAssembler.AbsoluteTransferWorklist(assembler);
			worklist.Defer(1, 33000, 128);
			buffer.RemoveBytes(0, 6);
			Assert.Equal(new[] { 0 }, worklist.GetIndexes()); Assert.True(worklist.ShouldRetry(0));
		}
		else
		{
			var worklist = new M68kAssembler.WordBranchWorklist(assembler);
			worklist.Defer(1, 200, 128);
			buffer.RemoveByteRanges([(0, 4)]);
			Assert.Equal(new[] { 0 }, worklist.GetIndexes()); Assert.True(worklist.ShouldRetry(0));
		}
	}

	[Fact]
	public void WordRemovalDiscardsBoundsAndAppendedCallsHaveNoStaleBudget()
	{
		var assembler = BudgetLayout(false); assembler.EmitBsr("target");
		var buffer = Buffer(assembler); var worklist = new M68kAssembler.WordBranchWorklist(assembler);
		worklist.Defer(1, 200, 128);
		buffer.Branches.RemoveAt(0); worklist.Removed(0); buffer.RemoveBytes(0, 4);
		Assert.True(worklist.ShouldRetry(0));
		assembler.EmitBsr("target"); worklist.Appended();
		Assert.Equal(new[] { 0, 1 }, worklist.GetIndexes()); Assert.True(worklist.ShouldRetry(1));
	}

	[Fact]
	public void AbsoluteLoadRemovalNotifiesTheTransferWorklist()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x41F9); assembler.EmitAddress("data");
		assembler.EmitJsr("data", false); assembler.EmitWord(0x4E75);
		assembler.MarkDataStart(); assembler.Mark("data"); assembler.EmitLong(0);
		var worklist = new M68kAssembler.AbsoluteTransferWorklist(assembler);
		worklist.Defer(1, 33000, 0);
		Assert.True(Step(assembler, "TryRelaxLocalAbsoluteLoad", worklist));
		Assert.Equal(new[] { 0 }, worklist.GetIndexes()); Assert.False(worklist.ShouldRetry(0));
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public void LaterInvocationsObserveSameCountMetadataChangesAndFailedLinkRetries(bool absolute)
	{
		var assembler = BudgetLayout(absolute); var buffer = Buffer(assembler);
		buffer.Labels.Add("target", 500000);
		assembler.RelaxFinalLayout();
		buffer.Labels["target"] = 128;
		if (absolute) buffer.Addresses[0] = buffer.Addresses[0] with { Target = "missing" };
		else buffer.Branches[0] = buffer.Branches[0] with { Target = "missing" };
		Assert.Throws<InvalidOperationException>(() => assembler.Link(0, Imports));
		buffer.Labels.Add("missing", 16);
		var linked = assembler.Link(0, Imports);
		Assert.Equal(absolute ? 0x610A : 0x610C, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
	}

	[Theory]
	[InlineData(0, false)] [InlineData(1, false)] [InlineData(2, false)] [InlineData(3, false)]
	[InlineData(4, false)] [InlineData(5, false)] [InlineData(6, false)] [InlineData(7, false)]
	[InlineData(8, false)] [InlineData(9, false)] [InlineData(10, false)] [InlineData(11, false)]
	[InlineData(12, false)] [InlineData(13, false)] [InlineData(14, false)] [InlineData(15, false)]
	[InlineData(16, false)] [InlineData(17, false)] [InlineData(18, false)] [InlineData(19, false)]
	[InlineData(20, false)] [InlineData(21, false)] [InlineData(22, false)] [InlineData(23, false)]
	[InlineData(0, true)] [InlineData(1, true)] [InlineData(2, true)] [InlineData(3, true)]
	[InlineData(4, true)] [InlineData(5, true)] [InlineData(6, true)] [InlineData(7, true)]
	public void MixedLayoutsKeepTheFullScanScheduleAndCompleteMetadata(int seed, bool aligned) =>
		Compare(() => MixedLayout(seed, aligned));

	[Theory]
	[InlineData(false, 32768)] [InlineData(false, 32774)] [InlineData(false, 32782)]
	[InlineData(true, 32770)] [InlineData(true, 32778)] [InlineData(true, 32786)]
	public void DeferredAbsoluteTransfersWakeAtSignedWordBoundaries(bool backward, int distance) => Compare(() =>
	{
		var assembler = new M68kAssembler();
		if (backward)
		{
			assembler.Mark("far"); assembler.EmitWord(0x4E75);
			while (assembler.Offset < distance - 50) assembler.EmitWord(0x4E71);
			assembler.Mark("near"); assembler.EmitWord(0x4E75);
			for (var i = 0; i < 8; i++) assembler.EmitJsr("near", false);
			assembler.EmitJsr("far", false); Buffer(assembler).Addresses.Reverse();
		}
		else
		{
			assembler.EmitJsr("far", false);
			for (var i = 0; i < 8; i++) assembler.EmitJsr("near", false);
			assembler.Mark("near"); assembler.EmitWord(0x4E75);
			while (assembler.Offset < distance) assembler.EmitWord(0x4E71);
			assembler.Mark("far"); assembler.EmitWord(0x4E75);
		}
		return assembler;
	});

	[Theory]
	[InlineData(0, false)] [InlineData(1, false)] [InlineData(0, true)] [InlineData(1, true)]
	public void RetainedInteriorAliasesAndClampedAnchorsKeepTheFullScanResult(int interior, bool aligned) => Compare(() =>
	{
		var assembler = new M68kAssembler();
		assembler.EmitBsr("alias"); assembler.EmitBranch(M68kCondition.Equal, "alias-copy");
		for (var i = 0; i < 24; i++) assembler.EmitJsr("near", false);
		var alias = assembler.Offset - 2 + interior;
		Buffer(assembler).Labels.Add("alias", alias); Buffer(assembler).Labels.Add("alias-copy", alias);
		Buffer(assembler).AnalysisAnchors.Add("interior-anchor", alias);
		assembler.Mark("near"); assembler.EmitWord(0x4E75);
		if (aligned) assembler.RequestLongAlignment("near");
		return assembler;
	});

	[Theory]
	[InlineData(false, false)] [InlineData(false, true)]
	[InlineData(true, false)] [InlineData(true, true)]
	public void DeferredWordBranchKeepsMc68000CallsRegistersFlagsAndStack(bool aligned, bool takeBranch)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4A80); assembler.EmitBranch(M68kCondition.Equal, "zero");
		for (var i = 0; i < 24; i++) assembler.EmitJsr("callee", false);
		assembler.EmitWord(0x722A); assembler.EmitWord(0x4E75);
		assembler.Mark("zero"); assembler.EmitWord(0x72FF); assembler.EmitWord(0x4E75);
		assembler.Mark("callee"); assembler.EmitWord(0x5282); assembler.EmitWord(0x4E75);
		if (aligned) assembler.RequestLongAlignment("callee");
		var before = OriginalCode(assembler); var linked = assembler.Link(0x10000, Imports);
		Assert.NotEqual(0, linked.Bytes[3]);
		var expected = Execute(before, takeBranch ? 0u : 1u);
		Assert.Equal(expected, Execute(linked.Bytes, takeBranch ? 0u : 1u));
		Assert.Equal(takeBranch ? uint.MaxValue : 42u, expected.D1);
		Assert.Equal(takeBranch ? 0u : 24u, expected.D2); Assert.Equal(0x80004u, expected.Sp);
	}

	[Theory]
	[InlineData(false)] [InlineData(true)]
	public void DeferredAbsoluteCallKeepsMc68000ReturnAddressesAndEffects(bool aligned)
	{
		var assembler = new M68kAssembler(); assembler.EmitJsr("far", false);
		for (var i = 0; i < 24; i++) assembler.EmitJsr("near", false);
		assembler.EmitWord(0x4E75); assembler.Mark("near"); assembler.EmitWord(0x5282); assembler.EmitWord(0x4E75);
		while (assembler.Offset < 32780) assembler.EmitWord(0x4E71);
		assembler.Mark("far"); assembler.EmitWord(0x5281); assembler.EmitWord(0x4E75);
		if (aligned) assembler.RequestLongAlignment("near");
		var before = OriginalCode(assembler); var linked = assembler.Link(0x10000, Imports);
		Assert.Equal(0x6100, BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes));
		var expected = Execute(before, 0);
		Assert.Equal(expected, Execute(linked.Bytes, 0));
		Assert.Equal(1u, expected.D1); Assert.Equal(24u, expected.D2); Assert.Equal(0x80004u, expected.Sp);
	}

	private static M68kAssembler BudgetLayout(bool absolute)
	{
		var assembler = new M68kAssembler(); assembler.Mark("entry");
		if (absolute) assembler.EmitJsr("target", false); else assembler.EmitBsr("target");
		for (var i = 0; i < 128; i++) assembler.EmitWord(0x4E71);
		return assembler;
	}
	private static (Func<bool> ShouldRetry, Action<int, int> Defer) Budget(M68kAssembler assembler, bool absolute)
	{
		if (absolute)
		{
			var worklist = new M68kAssembler.AbsoluteTransferWorklist(assembler);
			return (() => worklist.ShouldRetry(0), (displacement, target) => worklist.Defer(0, displacement, target));
		}
		var words = new M68kAssembler.WordBranchWorklist(assembler);
		return (() => words.ShouldRetry(0), (displacement, target) => words.Defer(0, displacement, target));
	}
	private static M68kAssembler MixedLayout(int seed, bool aligned)
	{
		var random = new Random(seed); var assembler = new M68kAssembler();
		for (var i = 0; i < 64; i++)
		{
			assembler.Mark("block:" + i);
			if (aligned && i % 11 == seed % 11) assembler.RequestLongAlignment("block:" + i);
			Buffer(assembler).AnalysisAnchors.Add("anchor:" + i, assembler.Offset);
			Buffer(assembler).InstructionEffectOverrides.Add(assembler.Offset,
				default(M68kInstructionEffects) with { UsesData = (ushort)(1 << (i % 8)), StackDelta = i });
			var target = "block:" + random.Next(64);
			switch (random.Next(8))
			{
				case 0: assembler.EmitBranch(M68kCondition.Equal, target); break;
				case 1: assembler.EmitBsr(target); break;
				case 2: assembler.EmitJsr(target, false); assembler.MarkReturnAddress("return:" + i); break;
				case 3: assembler.EmitJmp(target, false); break;
				case 4: assembler.EmitDbra(0, target); break;
				case 5: assembler.EmitWord(0x41F9); assembler.EmitAddress("rodata"); break;
				case 6: assembler.EmitBranch(M68kCondition.NotEqual, "block:" + (i + 1)); break;
				default: assembler.EmitWord(0x41FA); assembler.EmitPcRelativeWord("rodata"); break;
			}
			for (var n = random.Next(3); n > 0; n--) assembler.EmitWord(0x4E71);
		}
		assembler.Mark("block:64"); assembler.EmitWord(0x4E75); assembler.EmitJsr("external", true);
		assembler.MarkDataStart(); assembler.Mark("rodata"); assembler.EmitAddress("block:0");
		assembler.MarkWritableDataStart(); assembler.EmitAddress("block:32"); assembler.MarkBssStart(); assembler.EmitLong(0);
		if (seed % 2 != 0) { Buffer(assembler).Branches.Reverse(); Buffer(assembler).Addresses.Reverse(); }
		return assembler;
	}
	private static void Compare(Func<M68kAssembler> create)
	{
		var reference = create(); var candidate = create(); reference.ApplyRequestedAlignments();
		while (Step(reference, "TryRelaxShortBranches") || Step(reference, "TryRelaxLocalAbsoluteControlTransfer") ||
			Step(reference, "TryRemoveBranchToNextInstruction") || Step(reference, "TryRelaxLocalAbsoluteLoad")) reference.ApplyRequestedAlignments();
		candidate.RelaxFinalLayout(); Assert.Equal(Snapshot(reference), Snapshot(candidate));
		Assert.Equal(JsonSerializer.Serialize(reference.Link(0x10000, Imports)), JsonSerializer.Serialize(candidate.Link(0x10000, Imports)));
		Assert.Equal(reference.RenderAssembly(M68kCpuTarget.M68000), candidate.RenderAssembly(M68kCpuTarget.M68000));
	}
	private static bool Step(M68kAssembler assembler, string name, params object?[] arguments)
	{
		var method = typeof(M68kAssembler).GetMethod(name, PrivateInstance)!;
		return (bool)method.Invoke(assembler, arguments.Concat(method.GetParameters().Skip(arguments.Length).Select(p => p.DefaultValue)).ToArray())!;
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
	private static byte[] OriginalCode(M68kAssembler assembler)
	{
		var buffer = Buffer(assembler); var bytes = buffer.Bytes.ToArray();
		foreach (var branch in buffer.Branches)
			BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(branch.OpcodeOffset + 2), checked((short)(buffer.Labels[branch.Target] - branch.OpcodeOffset - 2)));
		foreach (var address in buffer.Addresses)
			BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(address.Offset), 0x10000u + (uint)buffer.Labels[address.Target]);
		return bytes;
	}
	private static (uint D0, uint D1, uint D2, uint Sp, ushort Flags) Execute(byte[] code, uint input)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[0] = input;
		for (var i = 0; i < 512 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.D[2], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
