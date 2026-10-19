/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDataflowFixupSnapshotTests
{
	private static readonly FieldInfo BufferField = typeof(M68kAssembler)
		.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!;

	[Fact]
	public void SuppliedRoundSnapshotAvoidsAnotherGlobalAllocation()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E71); assembler.EmitWord(0x4E75);
		assembler.Mark("code:end"); assembler.MarkDataStart();
		for (var i = 0; i < 4096; i++) assembler.EmitAddress("target");
		assembler.Mark("target");
		assembler.SetAnalysisScope(null, "code:end", ["code:end"]);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var snapshot = assembler.AddressFixupOffsets;
			for (var i = 0; i < 3; i++) M68kInstructionDataflow.Analyze(assembler, snapshot);
			var allocated = Allocated(() => GC.KeepAlive(M68kInstructionDataflow.Analyze(assembler, snapshot)));
			Assert.True(allocated < 32768, $"Analysis with supplied offsets allocated {allocated} bytes.");
			Assert.Equal(4096, snapshot.Count);
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void SuppliedSnapshotPreservesBothAnalyses(bool valueSymbol, bool conditionSymbol)
	{
		var (assembler, buffer) = Create(valueSymbol, conditionSymbol);
		var bytes = buffer.Bytes.ToArray(); var addresses = buffer.Addresses.ToArray();
		var snapshot = assembler.AddressFixupOffsets;
		var expected = M68kInstructionDataflow.Analyze(assembler);
		var actual = M68kInstructionDataflow.Analyze(assembler, snapshot);
		Assert.Equal(expected.Instructions, actual.Instructions);
		foreach (var instruction in expected.Instructions)
		{
			Assert.True(expected.TryGetFacts(instruction.Offset, out var expectedFacts));
			Assert.True(actual.TryGetFacts(instruction.Offset, out var actualFacts));
			Assert.Equal(expectedFacts, actualFacts);
			for (var register = 0; register < 8; register++)
			{
				Assert.Equal(expected.GetDataValueBefore(instruction.Offset, register), actual.GetDataValueBefore(instruction.Offset, register));
				Assert.Equal(expected.GetAddressAliasBefore(instruction.Offset, register), actual.GetAddressAliasBefore(instruction.Offset, register));
			}
			Assert.Equal(expected.IsConditionInstructionRedundant(instruction, M68kConditionCodeSet.All),
				actual.IsConditionInstructionRedundant(instruction, M68kConditionCodeSet.All));
		}
		AssertFacts(actual, valueSymbol, conditionSymbol);
		Assert.Equal(bytes, buffer.Bytes);
		Assert.Equal(addresses, buffer.Addresses);
		Assert.Equal(addresses.Select(address => address.Offset).Order(), snapshot.Order());
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SuppliedSnapshotDoesNotLeakIntoLaterDefaultCalls(bool inRound)
	{
		var (assembler, buffer) = Create(true, true);
		if (inRound) assembler.BeginInstructionAnalysisRound();
		try
		{
			var snapshot = assembler.AddressFixupOffsets;
			AssertFacts(M68kInstructionDataflow.Analyze(assembler, snapshot), true, true);
			buffer.Addresses[0] = buffer.Addresses[0] with { Offset = 18 };
			buffer.Addresses[1] = buffer.Addresses[1] with { Offset = 22 };
			AssertFacts(assembler, false, false);
			AssertFacts(M68kInstructionDataflow.Analyze(assembler, assembler.AddressFixupOffsets), false, false);
			Assert.Equal(new[] { 2, 10 }, snapshot.Order());
		}
		finally { if (inRound) assembler.EndInstructionAnalysisRound(); }
	}

	[Fact]
	public void SmallScopeAllocatesAtMostOneGlobalFixupSnapshot()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E71); assembler.EmitWord(0x4E75);
		assembler.Mark("code:end"); assembler.MarkDataStart();
		for (var i = 0; i < 4096; i++) assembler.EmitAddress("target");
		assembler.Mark("target");
		assembler.SetAnalysisScope(null, "code:end", ["code:end"]);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			for (var i = 0; i < 3; i++) M68kInstructionDataflow.Analyze(assembler);
			var oneSnapshot = Allocated(() => GC.KeepAlive(assembler.AddressFixupOffsets));
			var analysis = Allocated(() => GC.KeepAlive(M68kInstructionDataflow.Analyze(assembler)));
			Assert.True(analysis <= oneSnapshot + 32768,
				$"Analysis allocated {analysis} bytes; one snapshot allocated {oneSnapshot} bytes.");
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void BothAnalysesDistinguishLiteralAndRelocatableOperands(bool valueSymbol, bool conditionSymbol)
	{
		var (assembler, buffer) = Create(valueSymbol, conditionSymbol);
		var bytes = buffer.Bytes.ToArray(); var addresses = buffer.Addresses.ToArray();
		AssertFacts(assembler, valueSymbol, conditionSymbol);
		Assert.Equal(bytes, buffer.Bytes);
		Assert.Equal(addresses, buffer.Addresses);
	}

	[Theory]
	[InlineData("unresolved", false, 0)]
	[InlineData("external", true, 0)]
	[InlineData("target", false, 4)]
	[InlineData("target", false, -4)]
	public void SymbolicClassificationDependsOnOffsetRatherThanTargetResolution(
		string target, bool external, int addend)
	{
		var (assembler, buffer) = Create(true, true);
		for (var i = 0; i < buffer.Addresses.Count; i++)
			buffer.Addresses[i] = buffer.Addresses[i] with { Target = target, External = external, Addend = addend };
		AssertFacts(assembler, true, true);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void EveryCallReadsFreshSameCountFixups(bool inRound)
	{
		var (assembler, buffer) = Create(true, true);
		if (inRound) assembler.BeginInstructionAnalysisRound();
		try
		{
			AssertFacts(assembler, true, true);
			buffer.Addresses[0] = buffer.Addresses[0] with { Offset = 18 };
			buffer.Addresses[1] = buffer.Addresses[1] with { Offset = 22 };
			AssertFacts(assembler, false, false);
			buffer.Addresses[0] = buffer.Addresses[0] with { Offset = 2 };
			buffer.Addresses[1] = buffer.Addresses[1] with { Offset = 10 };
			AssertFacts(assembler, true, true);
		}
		finally { if (inRound) assembler.EndInstructionAnalysisRound(); }
	}

	[Fact]
	public void LaterRoundsSeeRebasedOperandOffsets()
	{
		var (assembler, buffer) = Create(true, true);
		assembler.BeginInstructionAnalysisRound();
		AssertFacts(assembler, true, true);
		assembler.EndInstructionAnalysisRound();
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			AssertFacts(assembler, true, true, 2);
			Assert.Equal(new[] { 4, 12 }, assembler.AddressFixupOffsets.Order());
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void RelocatedOperandsKeepCompiledMc68000Behavior(bool scoped)
	{
		var (assembler, buffer) = Create(true, true);
		if (!scoped) assembler.ClearAnalysisScope();
		// Keep trailing data outside the pass in whole-image analysis too.
		var before = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		new M68kConditionCodeOptimizer(assembler, buffer).Run();
		Assert.Contains(buffer.Addresses, address => address.Offset == 2);
		Assert.Contains(buffer.Addresses, address => address.Offset == 10);
		Assert.Contains(assembler.GetInstructionStream(), instruction => instruction.Opcode == 0x0C82);
		var after = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		Assert.Equal(before, after);
		foreach (var value in new uint[] { 0, 1, 0x1001A, 0x80000000, 0xFFFFFFFF })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(before, value, flags), Execute(after, value, flags));
	}

	[Fact]
	public void EmptyScopeReturnsEmptyAnalysesWithoutReadingGlobalFixups()
	{
		var assembler = new M68kAssembler();
		assembler.Mark("empty:end"); assembler.MarkDataStart();
		for (var i = 0; i < 4096; i++) assembler.EmitAddress("empty:end");
		assembler.SetAnalysisScope(null, "empty:end", ["empty:end"]);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			for (var i = 0; i < 3; i++) M68kInstructionDataflow.Analyze(assembler);
			var allocated = Allocated(() => Assert.Empty(M68kInstructionDataflow.Analyze(assembler).Instructions));
			Assert.True(allocated < 32768, $"Empty analysis allocated {allocated} bytes.");
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	private static void AssertFacts(M68kAssembler assembler, bool valueSymbol, bool conditionSymbol, int shift = 0)
		=> AssertFacts(M68kInstructionDataflow.Analyze(assembler), valueSymbol, conditionSymbol, shift);

	private static void AssertFacts(M68kInstructionDataflow dataflow, bool valueSymbol, bool conditionSymbol, int shift = 0)
	{
		Assert.Equal(!valueSymbol, dataflow.GetDataValueBefore(6 + shift, 0).IsExact(out var value));
		if (!valueSymbol) Assert.Equal(0u, value);
		var comparison = dataflow.Instructions.Single(instruction => instruction.Offset == 8 + shift);
		Assert.Equal(!conditionSymbol, dataflow.IsConditionInstructionRedundant(comparison, M68kConditionCodeSet.Zero));
	}

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer) Create(bool valueSymbol, bool conditionSymbol)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x203C); assembler.EmitLong(0); // MOVE.L #literal/symbol,D0
		assembler.EmitWord(0x4A82); // TST.L D2 establishes the literal-zero comparison.
		assembler.EmitWord(0x0C82); assembler.EmitLong(0); // CMPI.L #literal/symbol,D2
		assembler.EmitWord(0x4E71); assembler.EmitWord(0x4E75);
		assembler.Mark("code:end"); assembler.MarkDataStart();
		assembler.EmitLong(0); assembler.EmitLong(0);
		assembler.Mark("target"); assembler.EmitLong(0);
		var buffer = (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
		buffer.Addresses.Add(new(valueSymbol ? 2 : 18, "target", false));
		buffer.Addresses.Add(new(conditionSymbol ? 10 : 22, "target", false));
		assembler.SetAnalysisScope(null, "code:end", ["code:end"]);
		return (assembler, buffer);
	}

	private static long Allocated(Action action)
	{
		var start = GC.GetAllocatedBytesForCurrentThread(); action();
		return GC.GetAllocatedBytesForCurrentThread() - start;
	}

	private static (uint D0, uint D2, uint Sp, ushort Flags) Execute(byte[] code, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[2] = value; cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 16 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[2], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
