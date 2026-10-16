/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kFixupIndexTests
{
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;

	[Theory]
	[InlineData(0, 0x6000, 4)]
	[InlineData(1, 0x4EB9, 6)]
	[InlineData(2, 0x203A, 4)]
	public void RepeatedDecodeObservesFixupRemovalReplacementAndDuplicateRecovery(int kind, int opcode, int length)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord((ushort)opcode); // Intentionally truncated symbolic operand.
		var buffer = Buffer(assembler);
		buffer.Labels.Add("target", 32);
		void Add()
		{
			if (kind == 0) buffer.Branches.Add(new(0, "target"));
			else if (kind == 1) buffer.Addresses.Add(new(2, "target", false));
			else buffer.PcRelative.Add(new(2, "target"));
		}
		void Clear()
		{
			buffer.Branches.Clear();
			buffer.Addresses.Clear();
			buffer.PcRelative.Clear();
		}
		Add();
		var first = Assert.Single(assembler.GetInstructionStream());
		Assert.True(first.IsDecoded);
		Assert.Equal(length, first.Length);
		Clear();
		var missing = Assert.Single(assembler.GetInstructionStream());
		Assert.False(missing.IsDecoded);
		Assert.Equal(2, missing.Length);
		Add();
		Assert.Equal(first, Assert.Single(assembler.GetInstructionStream()));
		Add();
		Assert.Throws<ArgumentException>(() => assembler.GetInstructionStream());
		Clear();
		Add();
		Assert.Equal(first, Assert.Single(assembler.GetInstructionStream()));
	}

	[Fact]
	public void RepeatedDecodeObservesSameCountRetargetingAndShiftedFixupOffsets()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x6000); assembler.EmitWord(0);
		assembler.EmitWord(0x4EB9); assembler.EmitLong(0);
		assembler.EmitWord(0x203A); assembler.EmitWord(0);
		assembler.EmitWord(0x4E75); assembler.EmitWord(0x4E75);
		var buffer = Buffer(assembler);
		buffer.Labels.Add("first", 14);
		buffer.Labels.Add("second", 16);
		buffer.Branches.Add(new(0, "first"));
		buffer.Addresses.Add(new(6, "first", false));
		buffer.PcRelative.Add(new(12, "first"));
		var snapshot = assembler.GetInstructionStream();
		buffer.Branches[0] = new(0, "second");
		buffer.Addresses[0] = new(6, "second", true);
		buffer.PcRelative[0] = new(12, "second");
		var retargeted = assembler.GetInstructionStream();
		Assert.Equal(16, retargeted[0].TargetOffset);
		Assert.True(retargeted[1].ExternalTarget);
		Assert.Equal(14, snapshot[0].TargetOffset);
		Assert.False(snapshot[1].ExternalTarget);
		AssertMatchesFreshDecode();
		buffer.InsertBytes(0, 2);
		buffer.WriteWord(0, 0x4E71);
		AssertMatchesFreshDecode();
		buffer.RemoveBytes(0, 2);
		AssertMatchesFreshDecode();
		buffer.RemoveByteRanges([(0, 4), (14, 2)]);
		AssertMatchesFreshDecode();

		void AssertMatchesFreshDecode()
		{
			var fresh = new M68kAssembler();
			var copy = Buffer(fresh);
			copy.Bytes.AddRange(buffer.Bytes);
			foreach (var label in buffer.Labels) copy.Labels.Add(label.Key, label.Value);
			copy.Branches.AddRange(buffer.Branches);
			copy.Addresses.AddRange(buffer.Addresses);
			copy.PcRelative.AddRange(buffer.PcRelative);
			Assert.Equal(fresh.GetInstructionStream(), assembler.GetInstructionStream());
		}
	}

	[Fact]
	public void RepeatedIndexingDoesNotAllocateAfterStorageIsWarmed()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E75);
		var buffer = Buffer(assembler);
		for (var index = 0; index < 64; index++)
		{
			buffer.Branches.Add(new(100 + index * 8, "target"));
			buffer.Addresses.Add(new(102 + index * 8, "target", false));
			buffer.PcRelative.Add(new(104 + index * 8, "target"));
		}
		using var session = M68kOptimizationProfiler.Start();
		assembler.GetInstructionStream();
		// Initialize both the storage-build and storage-reuse profiler counters.
		assembler.GetInstructionStream();
		var before = Assert.Single(session.GetSnapshot().Measurements,
			item => item.Name == "instructions.fixup-index");
		for (var index = 0; index < 20; index++) assembler.GetInstructionStream();
		var after = Assert.Single(session.GetSnapshot().Measurements,
			item => item.Name == "instructions.fixup-index");
		Assert.Equal(before.AllocatedBytes, after.AllocatedBytes);
		Assert.Equal(60, after.Calls - before.Calls);
	}
}
