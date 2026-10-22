/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kOptimizerSnapshotTests
{
	[Fact]
	public void ScopedAndWholeImageQueriesKeepIndependentExecutableBoundaries()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001);
		assembler.EmitWord(0x4E75);
		assembler.Mark("first:end");
		assembler.Mark("second");
		assembler.EmitWord(0x7202);
		assembler.EmitWord(0x4E75);
		assembler.Mark("second:end");
		assembler.MarkDataStart();
		assembler.EmitWord(0x4E71); // A valid opcode in data must stay excluded.
		assembler.SetAnalysisScope("first:end", "second:end",
			["first:end", "second", "second:end"]);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var scoped = assembler.GetInstructionStream();
			Assert.Equal(new[] { 4, 6 }, scoped.Select(instruction => instruction.Offset));
			Assert.Same(scoped, assembler.GetExecutableInstructionStream());
			assembler.ClearAnalysisScope();
			var whole = assembler.GetExecutableInstructionStream();
			Assert.Equal(new[] { 0, 2, 4, 6 }, whole.Select(instruction => instruction.Offset));
			Assert.Same(whole, assembler.GetExecutableInstructionStream());
			var second = assembler.GetExecutableInstructionRange(4, 8);
			Assert.Same(scoped, second);
			Assert.Equal(new[] { 0, 2 }, assembler.GetExecutableInstructionRange(0, 4)
				.Select(instruction => instruction.Offset));
			Assert.Equal(new[] { 4, 6 }, assembler.GetExecutableInstructionRange(4, 10)
				.Select(instruction => instruction.Offset));
			assembler.SetAnalysisScope("first:end", "second:end",
				["first:end", "second", "second:end"]);
			Assert.Same(scoped, assembler.GetInstructionStream());
		}
		finally { assembler.EndInstructionAnalysisRound(); assembler.ClearAnalysisScope(); }
		Assert.Equal(5, assembler.GetInstructionStream().Count);
		Assert.Equal(4, assembler.GetExecutableInstructionStream().Count);
	}

	[Fact]
	public void CalleeRangePreservesGlobalInstructionBoundariesAndCurrentScope()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001);
		assembler.EmitWord(0x4E75);
		assembler.Mark("caller:end");
		assembler.EmitWord(0x203C); // MOVE.L #$4E757000,D0: operand resembles RTS/MOVEQ.
		assembler.EmitLong(0x4E757000);
		assembler.EmitWord(0x4E75);
		assembler.Mark("callee:end");
		assembler.SetAnalysisScope(null, "caller:end", ["caller:end"]);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var whole = assembler.GetExecutableInstructionRange(0, 12);
			var callee = assembler.GetExecutableInstructionRange(4, 12);
			Assert.Equal(whole.Where(instruction => instruction.Offset >= 4), callee);
			Assert.DoesNotContain(callee, instruction => instruction.Offset is 6 or 8);
			Assert.Equal((0, 4), assembler.GetAnalysisRange());
			Assert.Equal(new[] { 0, 2 }, assembler.GetInstructionStream()
				.Select(instruction => instruction.Offset));
		}
		finally { assembler.EndInstructionAnalysisRound(); assembler.ClearAnalysisScope(); }
	}

	[Fact]
	public void EndingAnAnalysisRoundDiscardsTheSnapshotBeforeFurtherEmission()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x7001);
		assembler.BeginInstructionAnalysisRound();
		var before = assembler.GetExecutableInstructionStream();
		assembler.EndInstructionAnalysisRound();
		assembler.EmitWord(0x4E75);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var after = assembler.GetExecutableInstructionStream();
			Assert.Single(before);
			Assert.Equal(new ushort[] { 0x7001, 0x4E75 }, after.Select(instruction => instruction.Opcode));
			Assert.NotSame(before, after);
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Fact]
	public void EndingAnAnalysisRoundDiscardsFixupsBeforeFurtherEmission()
	{
		var assembler = new M68kAssembler();
		assembler.EmitCall("method:first");
		assembler.Mark("method:first");
		assembler.EmitWord(0x4E75);
		assembler.BeginInstructionAnalysisRound();
		var first = assembler.GetExecutableInstructionStream();
		Assert.Equal(6, first[0].TargetOffset);
		assembler.EndInstructionAnalysisRound();
		assembler.EmitCall("method:second");
		assembler.Mark("method:second");
		assembler.EmitWord(0x4E75);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			var second = assembler.GetExecutableInstructionRange(8, 16);
			Assert.Equal(M68kInstructionKind.Call, second[0].Kind);
			Assert.Equal(14, second[0].TargetOffset);
			Assert.Equal(6, assembler.GetExecutableInstructionRange(0, 8)[0].TargetOffset);
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData(1)]
	[InlineData(17)]
	[InlineData(257)]
	public void BatchedRemovalMatchesReverseRemovalForEveryMetadataBoundary(int count)
	{
		var sequential = MakeBuffer();
		var batched = MakeBuffer();
		var ranges = Enumerable.Range(0, count).Select(i => (Offset: i * 6 + 2, Count: 2)).ToArray();
		foreach (var range in ranges.Reverse()) sequential.RemoveBytes(range.Offset, range.Count);
		batched.RemoveByteRanges(ranges);
		Assert.Equal(sequential.Bytes, batched.Bytes);
		Assert.Equal(sequential.Labels, batched.Labels);
		Assert.Equal(sequential.AnalysisAnchors, batched.AnalysisAnchors);
		Assert.Equal(sequential.Branches, batched.Branches);
		Assert.Equal(sequential.Addresses, batched.Addresses);
		Assert.Equal(sequential.PcRelative, batched.PcRelative);
		Assert.Equal(sequential.InstructionEffectOverrides.OrderBy(item => item.Key),
			batched.InstructionEffectOverrides.OrderBy(item => item.Key));
		Assert.Equal(sequential.DataStartOffset, batched.DataStartOffset);
		Assert.Equal(sequential.WritableDataStartOffset, batched.WritableDataStartOffset);
		Assert.Equal(sequential.BssStartOffset, batched.BssStartOffset);

		M68kAssemblyBuffer MakeBuffer()
		{
			var buffer = new M68kAssemblyBuffer();
			for (var offset = 0; offset <= count * 6; offset++)
			{
				// Include aliases, boundaries and metadata inside removed ranges.
				buffer.Labels.Add("label" + offset, offset);
				buffer.AnalysisAnchors.Add("anchor" + offset, offset);
				buffer.Branches.Add(new(offset, "label0", offset % 2 == 0));
				buffer.Addresses.Add(new(offset, "label0", offset % 2 == 0, offset - 3));
				buffer.PcRelative.Add(new(offset, "label0"));
				buffer.InstructionEffectOverrides.Add(offset, new((ushort)offset, 0, 0, 0,
					M68kConditionCodeSet.None, M68kConditionCodeSet.None,
					M68kMemorySet.None, M68kMemorySet.None, 0, false, true));
				buffer.Bytes.Add((byte)offset);
			}
			buffer.MarkDataStart(); buffer.Bytes.Add(1);
			buffer.MarkWritableDataStart(); buffer.Bytes.Add(2);
			buffer.MarkBssStart(); buffer.Bytes.Add(3);
			return buffer;
		}
	}
}
