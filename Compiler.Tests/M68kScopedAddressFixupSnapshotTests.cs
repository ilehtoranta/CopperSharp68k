/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections;
using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kScopedAddressFixupSnapshotTests
{
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;
	private static Lazy<HashSet<int>> Global(IReadOnlySet<int> snapshot) =>
		(Lazy<HashSet<int>>)snapshot.GetType().GetField("_all",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(snapshot)!;

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void CompleteSetContractPreservesDuplicatesExtremesAndEnumerationOrder(bool scoped)
	{
		var assembler = Create(scoped);
		var values = new[] { 99, 4, 6, 4, 17, 18, -1, int.MinValue, int.MaxValue, 0, 12 };
		foreach (var offset in values) Buffer(assembler).Addresses.Add(new(offset, "target", offset % 2 == 0));
		var snapshot = assembler.AddressFixupOffsets;
		var expected = new HashSet<int>(values);
		foreach (var offset in values.Concat(new[] { 5, 13, 19, 100 }))
			Assert.Equal(expected.Contains(offset), snapshot.Contains(offset));
		Assert.Equal(expected.Count, snapshot.Count);
		Assert.Equal(expected.ToArray(), snapshot.ToArray());
		Assert.Equal(expected.ToArray(), ((IEnumerable)snapshot).Cast<int>().ToArray());
		foreach (var other in new[] { Array.Empty<int>(), values, new[] { 4, 99 }, new[] { 101 }, values.Concat(new[] { 101 }).ToArray() })
		{
			Assert.Equal(expected.SetEquals(other), snapshot.SetEquals(other));
			Assert.Equal(expected.IsSubsetOf(other), snapshot.IsSubsetOf(other));
			Assert.Equal(expected.IsSupersetOf(other), snapshot.IsSupersetOf(other));
			Assert.Equal(expected.IsProperSubsetOf(other), snapshot.IsProperSubsetOf(other));
			Assert.Equal(expected.IsProperSupersetOf(other), snapshot.IsProperSupersetOf(other));
			Assert.Equal(expected.Overlaps(other), snapshot.Overlaps(other));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SameCountReplacementAndRebasePreserveColdAndWarmSnapshots(bool globalFirst)
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		foreach (var offset in new[] { 4, 6, 24, 99 }) buffer.Addresses.Add(new(offset, "target", false));
		var old = assembler.AddressFixupOffsets;
		Assert.True(old.Contains(4));
		if (globalFirst) Assert.True(old.Contains(99));
		buffer.Addresses[3] = buffer.Addresses[3] with { Offset = 101 };
		buffer.InsertBytes(0, 2);
		// Fresh snapshots must see direct same-count edits within the same scope.
		Assert.True(assembler.AddressFixupOffsets.SetEquals(new[] { 6, 8, 26, 103 }));
		Assert.True(old.Contains(99)); Assert.False(old.Contains(101));
		Assert.Equal(new[] { 4, 6, 24, 99 }, old.ToArray());
		assembler.ClearAnalysisScope();
		Assert.True(assembler.AddressFixupOffsets.SetEquals(new[] { 6, 8, 26, 103 }));
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	[InlineData(4)]
	[InlineData(5)]
	[InlineData(6)]
	[InlineData(7)]
	[InlineData(8)]
	public void EveryGlobalOperationUsesOwnedOffsetsWhenFirstQueriedAfterMutation(int operation)
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		buffer.Addresses.Add(new(4, "local", false)); buffer.Addresses.Add(new(98, "callee", true, 7));
		var snapshot = assembler.AddressFixupOffsets;
		Assert.True(snapshot.Contains(4)); Assert.False(Global(snapshot).IsValueCreated);
		buffer.Addresses.Clear(); buffer.Addresses.Add(new(100, "replacement", false));
		switch (operation)
		{
			case 0: Assert.True(snapshot.Contains(98)); Assert.False(snapshot.Contains(100)); break;
			case 1: Assert.Equal(2, snapshot.Count); break;
			case 2: Assert.Equal(new[] { 4, 98 }, snapshot.ToArray()); break;
			case 3: Assert.True(snapshot.SetEquals(new[] { 98, 4, 4 })); break;
			case 4: Assert.True(snapshot.IsSubsetOf(new[] { 4, 98 })); break;
			case 5: Assert.True(snapshot.IsSupersetOf(new[] { 98 })); break;
			case 6: Assert.True(snapshot.IsProperSubsetOf(new[] { 4, 98, 100 })); break;
			case 7: Assert.True(snapshot.IsProperSupersetOf(new[] { 98 })); break;
			case 8: Assert.True(snapshot.Overlaps(new[] { 98 })); break;
		}
		Assert.True(Global(snapshot).IsValueCreated);
	}

	[Fact]
	public void ScopedMembershipAndOperandLookaheadKeepGlobalLookupDeferred()
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		foreach (var offset in new[] { 4, 12, 17, 18, 99 }) buffer.Addresses.Add(new(offset, "target", false));
		var snapshot = assembler.AddressFixupOffsets; // Scope [4,12), operand lookahead through 17.
		foreach (var offset in new[] { 4, 12, 17 }) Assert.True(snapshot.Contains(offset));
		Assert.False(snapshot.Contains(5)); Assert.False(Global(snapshot).IsValueCreated);
		Assert.True(snapshot.Contains(18)); Assert.True(Global(snapshot).IsValueCreated);
		Assert.True(snapshot.Contains(99));
	}

	[Fact]
	public void OperandLookaheadDoesNotOverflowAtMaximumOffset()
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		buffer.Labels["begin"] = int.MaxValue - 4; buffer.Labels["end"] = int.MaxValue;
		buffer.Addresses.Add(new(int.MaxValue, "far", false));
		var snapshot = assembler.AddressFixupOffsets;
		Assert.True(snapshot.Contains(int.MaxValue));
		Assert.False(snapshot.Contains(int.MaxValue - 1));
		Assert.False(Global(snapshot).IsValueCreated);
		Assert.False(snapshot.Contains(int.MinValue)); Assert.True(Global(snapshot).IsValueCreated);
	}

	[Fact]
	public void ActiveRoundUsesDataBoundaryForFastLookupWhileKeepingGlobalMembership()
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		assembler.MarkDataStart(); buffer.Labels["end"] = 256;
		buffer.Addresses.Add(new(133, "lookahead", false)); buffer.Addresses.Add(new(134, "data", false));
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var snapshot = assembler.AddressFixupOffsets;
			Assert.True(snapshot.Contains(133)); Assert.False(Global(snapshot).IsValueCreated);
			Assert.True(snapshot.Contains(134)); Assert.True(Global(snapshot).IsValueCreated);
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Fact]
	public void MovingScopeLabelsAndDeletingBytesDoNotChangeEarlierSnapshot()
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		buffer.Addresses.Add(new(6, "local", false)); buffer.Addresses.Add(new(98, "far", false));
		var old = assembler.AddressFixupOffsets;
		buffer.Labels["begin"] = 96; buffer.Labels["end"] = 104;
		var moved = assembler.AddressFixupOffsets;
		Assert.True(moved.Contains(98)); Assert.False(Global(moved).IsValueCreated);
		buffer.RemoveBytes(0, 2);
		Assert.True(assembler.AddressFixupOffsets.SetEquals(new[] { 4, 96 }));
		Assert.True(old.SetEquals(new[] { 6, 98 }));
		Assert.True(moved.SetEquals(new[] { 6, 98 }));
	}

	[Fact]
	public void EmptyScopeStillExposesFarOffsetsAndEmptySnapshotsRemainEmpty()
	{
		var assembler = Create(); var buffer = Buffer(assembler);
		buffer.Labels["end"] = 4;
		var empty = assembler.AddressFixupOffsets;
		buffer.Addresses.Add(new(98, "far", false));
		var snapshot = assembler.AddressFixupOffsets;
		Assert.False(snapshot.Contains(4)); Assert.False(Global(snapshot).IsValueCreated);
		Assert.True(snapshot.Contains(98)); Assert.Single(snapshot);
		Assert.Empty(empty); Assert.True(empty.SetEquals(Array.Empty<int>()));
	}

	[Fact]
	public void TruncatedImmediateKeepsSymbolicClassificationPastScopeEnd()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x203C); assembler.EmitWord(0x4E75);
		Buffer(assembler).Labels.Add("end", 2);
		Buffer(assembler).Addresses.Add(new(2, "target", false));
		assembler.SetAnalysisScope(null, "end", ["end"]);
		var snapshot = assembler.AddressFixupOffsets;
		// Remove decoder metadata and extend the scope before deferred analysis.
		// The old operand membership must still protect the truncated MOVE.
		Buffer(assembler).Addresses.Clear(); Buffer(assembler).Labels["end"] = 4;
		var flow = M68kInstructionDataflow.Analyze(assembler, snapshot);
		Assert.False(flow.Instructions[0].IsDecoded);
		Assert.False(Global(snapshot).IsValueCreated);
		Assert.True(snapshot.Contains(2));
		Assert.False(flow.GetDataValueBefore(2, 0).IsExact(out _));
		Assert.True(M68kInstructionDataflow.Analyze(assembler).GetDataValueBefore(2, 0).IsExact(out _));
	}

	[Fact]
	public void SmallScopeCopiesGlobalOffsetsWithoutAllocatingGlobalHashTable()
	{
		var assembler = Create();
		for (var i = 0; i < 4096; i++) Buffer(assembler).Addresses.Add(new(1000 + i * 4, "far", false));
		for (var i = 0; i < 3; i++) GC.KeepAlive(assembler.AddressFixupOffsets);
		var before = GC.GetAllocatedBytesForCurrentThread();
		var snapshot = assembler.AddressFixupOffsets;
		var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
		Assert.True(allocated < 4096 * 8, $"Compact snapshot allocated {allocated} bytes.");
		Assert.False(snapshot.Contains(4)); Assert.False(Global(snapshot).IsValueCreated);
		Assert.Equal(4096, snapshot.Count);
	}

	[Theory]
	[InlineData(0x10000u, false)]
	[InlineData(0x10000u, true)]
	[InlineData(0x30000u, false)]
	[InlineData(0x30000u, true)]
	public void ScopedSymbolicOperandsKeepCompiledMc68000ValuesConditionsAndStack(uint origin, bool rebase)
	{
		var assembler = new M68kAssembler(); assembler.Mark("code");
		assembler.EmitWord(0x203C); assembler.EmitAddress("target", external: true); // MOVE.L #target,D0
		assembler.EmitWord(0x4A82); // TST.L D2
		assembler.EmitWord(0x0C82); assembler.EmitAddress("target", external: true); // CMPI.L #target,D2
		assembler.EmitWord(0x57C1); assembler.EmitWord(0x4E75); // SEQ D1; RTS
		assembler.Mark("code:end"); assembler.MarkDataStart();
		for (var i = 0; i < 4096; i++) assembler.EmitAddress("payload");
		assembler.Mark("payload"); assembler.EmitLong(0);
		assembler.SetAnalysisScope("code", "code:end", ["code", "code:end"]);
		var buffer = Buffer(assembler);
		if (rebase) { buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71); }
		var entry = origin + (rebase ? 2u : 0u);
		// An external symbol stays fixed when legitimate instruction removal
		// rebases the image, so before/after execution compares the same value.
		const uint target = 0x50000;
		var symbols = new Dictionary<string, uint> { ["target"] = target };
		var before = assembler.Link(origin, symbols).Bytes;
		new M68kConditionCodeOptimizer(assembler, buffer).RunThen(
			new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []));
		var after = assembler.Link(origin, symbols).Bytes;
		Assert.Equal(2, buffer.Addresses.Count(address => address.External && address.Target == "target"));
		Assert.Contains(assembler.GetInstructionStream(), instruction => instruction.Opcode == 0x203C);
		Assert.Contains(assembler.GetInstructionStream(), instruction => instruction.Opcode == 0x0C82);
		foreach (var value in new[] { 0u, 1u, target, 0x80000000u, 0xFFFFFFFFu })
		foreach (var flags in new ushort[] { 0, 0x1F })
		{
			var expected = Execute(before, origin, entry, value, flags);
			Assert.Equal(target, expected.D0);
			Assert.Equal(value == target ? 0x123456FFu : 0x12345600u, expected.D1);
			Assert.Equal(expected, Execute(after, origin, entry, value, flags));
		}
	}

	private static M68kAssembler Create(bool scoped = true)
	{
		var assembler = new M68kAssembler(); var buffer = Buffer(assembler);
		buffer.Bytes.AddRange(new byte[128]); buffer.Labels.Add("begin", 4); buffer.Labels.Add("end", 12);
		if (scoped) assembler.SetAnalysisScope("begin", "end", ["begin", "end"]);
		return assembler;
	}

	private static (uint D0, uint D1, uint D2, uint Sp, ushort Flags) Execute(
		byte[] code, uint origin, uint entry, uint value, ushort flags)
	{
		const uint stack = 0x80000, done = 0x70000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)origin)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(entry, stack); cpu.State.D[1] = 0x12345678; cpu.State.D[2] = value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 16 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.D[2], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
