/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kScopedFixupTableTests
{
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;

	[Theory]
	[InlineData(0x203C, 2)] // Immediate address.
	[InlineData(0x23EF, 4)] // Frame source, absolute destination.
	[InlineData(0x23F9, 6)] // Absolute source and destination.
	[InlineData(0x23FC, 6)] // Immediate source, absolute destination.
	public void OperandFixupsBeyondRangeEndPreserveTheWholeInstruction(int opcode, int operand)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E71);
		var start = assembler.Offset;
		assembler.EmitWord((ushort)opcode);
		while (assembler.Offset < start + operand) assembler.EmitWord(0);
		assembler.EmitAddress("target", addend: 2);
		assembler.EmitWord(0x4E75); assembler.Mark("target");
		var expected = assembler.GetExecutableInstructionStream().Single(i => i.Offset == start);
		Assert.True(expected.IsDecoded);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			for (var end = start + 1; end <= start + operand; end++)
				Assert.Equal(expected, Assert.Single(assembler.GetExecutableInstructionRange(start, end)));
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Fact]
	public void PcRelativeOperandBeyondRangeEndRetainsInstructionRecognition()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x203A);
		assembler.EmitPcRelativeWord("target"); assembler.Mark("target"); assembler.EmitWord(0x4E75);
		var expected = assembler.GetExecutableInstructionStream()[0];
		Assert.True(expected.IsDecoded); Assert.Equal(4, expected.Length);
		assembler.BeginInstructionAnalysisRound();
		try { Assert.Equal(expected, Assert.Single(assembler.GetExecutableInstructionRange(0, 1))); }
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void CallerAndCalleeRangesKeepTheirOwnRelocations(bool reverse, bool external)
	{
		var assembler = new M68kAssembler();
		assembler.EmitCall("method:callee"); assembler.EmitWord(0x4E75); assembler.Mark("caller:end");
		assembler.Mark("method:callee");
		assembler.EmitWord(0x4EB9); assembler.EmitAddress("__c68k_exception_raise", external);
		assembler.EmitWord(0x203A); assembler.EmitPcRelativeWord("target");
		assembler.EmitBranch(M68kCondition.NotEqual, "target");
		assembler.Mark("target"); assembler.EmitWord(0x4E75); assembler.Mark("callee:end");
		assembler.Mark("__c68k_exception_raise"); assembler.EmitWord(0x4AFC);
		assembler.MarkDataStart(); assembler.EmitWord(0x4E71);
		var all = assembler.GetExecutableInstructionStream();
		var boundary = assembler.Labels["caller:end"];
		var end = assembler.Labels["callee:end"];
		assembler.SetAnalysisScope(null, "caller:end", ["caller:end"]);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			foreach (var callee in reverse ? new[] { true, false } : new[] { false, true })
			{
				var range = assembler.GetExecutableInstructionRange(callee ? boundary : 0, callee ? end : boundary);
				Assert.Equal(all.Where(i => i.Offset >= (callee ? boundary : 0) && i.Offset < (callee ? end : boundary)), range);
				Assert.Same(range, assembler.GetExecutableInstructionRange(callee ? boundary : 0, callee ? end : boundary));
			}
			var call = assembler.GetExecutableInstructionRange(boundary, end)[0];
			Assert.Equal(external, call.ExternalTarget);
			Assert.Equal(!external, call.IsNonReturning);
			Assert.Equal((0, boundary), assembler.GetAnalysisRange());
			Assert.Equal(all.Where(i => i.Offset < boundary), assembler.GetInstructionStream());
		}
		finally { assembler.EndInstructionAnalysisRound(); assembler.ClearAnalysisScope(); }
	}

	[Fact]
	public void SmallRangesDoNotAllocateTablesForUnrelatedMethods()
	{
		var assembler = new M68kAssembler(); assembler.Mark("entry");
		assembler.EmitWord(0x7001); assembler.EmitWord(0x4E75); assembler.Mark("entry:end");
		for (var index = 0; index < 4096; index++)
		{
			assembler.EmitBranch(M68kCondition.NotEqual, "entry");
			assembler.EmitWord(0x4EB9); assembler.EmitAddress("entry");
			assembler.EmitWord(0x203A); assembler.EmitPcRelativeWord("entry");
		}
		for (var warmup = 0; warmup < 3; warmup++) Query();
		var start = GC.GetAllocatedBytesForCurrentThread();
		var instructions = Query();
		var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
		Assert.Equal(new ushort[] { 0x7001, 0x4E75 }, instructions.Select(i => i.Opcode));
		Assert.True(allocated < 32768, $"Small range allocated {allocated} bytes for unrelated fixups.");

		IReadOnlyList<M68kEmittedInstruction> Query()
		{
			assembler.BeginInstructionAnalysisRound();
			try { return assembler.GetExecutableInstructionRange(0, 4); }
			finally { assembler.EndInstructionAnalysisRound(); }
		}
	}

	[Fact]
	public void NewRoundReadsSameCountRetargetingAndRebasedOperandOffsets()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x4EB9); assembler.EmitAddress("first");
		assembler.Mark("first"); assembler.EmitWord(0x4E75);
		assembler.Mark("second"); assembler.EmitWord(0x4AFC);
		var buffer = Buffer(assembler);
		assembler.BeginInstructionAnalysisRound();
		Assert.Equal(6, assembler.GetExecutableInstructionRange(0, 6)[0].TargetOffset);
		assembler.EndInstructionAnalysisRound();
		buffer.Addresses[0] = buffer.Addresses[0] with { Target = "second" };
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		assembler.BeginInstructionAnalysisRound();
		try { Assert.Equal(10, assembler.GetExecutableInstructionRange(2, 8)[0].TargetOffset); }
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Fact]
	public void AddressOffsetSnapshotsArePresizedDistinctAndFresh()
	{
		var assembler = new M68kAssembler(); assembler.Mark("target");
		for (var index = 0; index < 4096; index++) assembler.EmitAddress("target");
		var buffer = Buffer(assembler);
		// Membership is by operand offset, including unresolved/external entries.
		buffer.Addresses.Add(new(0, "unresolved", true, 17));
		for (var warmup = 0; warmup < 3; warmup++) GC.KeepAlive(assembler.AddressFixupOffsets);
		var start = GC.GetAllocatedBytesForCurrentThread();
		var before = assembler.AddressFixupOffsets;
		var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
		Assert.Equal(4096, before.Count);
		Assert.True(allocated < 110000, $"Offset set allocated {allocated} bytes despite a known list count.");
		buffer.Addresses[1] = buffer.Addresses[1] with { Offset = 16384 };
		var after = assembler.AddressFixupOffsets;
		Assert.Contains(4, before); Assert.DoesNotContain(16384, before);
		Assert.DoesNotContain(4, after); Assert.Contains(16384, after);
	}
}
