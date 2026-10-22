/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kConditionLabelIndexTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", Private)!;

	[Fact]
	public void SmallScopeDoesNotMaterializeGlobalLabelOffsets()
	{
		var (assembler, buffer, _) = Create(true, false);
		var optimizer = new M68kConditionCodeOptimizer(assembler, buffer);
		Assert.Null(Snapshot(optimizer, assembler.GetInstructionStream().Count));
		optimizer.Run();
		Assert.True(optimizer.Changed);
		Assert.DoesNotContain(assembler.GetInstructionStream(), i => i.Opcode == 0x4A80);
	}

	[Fact]
	public void LargeScopeRetainsExactGlobalSnapshot()
	{
		var (assembler, buffer, _) = Create(false, false);
		buffer.Labels.Add("negative", -1);
		buffer.Labels.Add("end-alias", assembler.Offset);
		var snapshot = Snapshot(new(assembler, buffer), 10000);
		Assert.NotNull(snapshot);
		Assert.True(snapshot.SetEquals(buffer.Labels.Values));
	}

	[Theory]
	[InlineData(false, false, "none")]
	[InlineData(true, false, "none")]
	[InlineData(false, false, "test")]
	[InlineData(true, false, "test")]
	[InlineData(false, false, "branch")]
	[InlineData(true, false, "branch")]
	[InlineData(false, true, "none")]
	[InlineData(true, true, "none")]
	[InlineData(false, true, "test")]
	[InlineData(true, true, "test")]
	[InlineData(false, true, "branch")]
	[InlineData(true, true, "branch")]
	public void GlobalAliasesProtectKnownZeroFoldsInEitherMembershipMode(
		bool indexed, bool taken, string boundary)
	{
		var (assembler, buffer, offsets) = Create(indexed, true, taken);
		if (boundary != "none")
		{
			var position = boundary == "test" ? offsets.Test : offsets.Branch;
			buffer.Labels.Add("outside:entry", position);
			buffer.Labels.Add("outside:alias", position);
		}
		var original = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		var optimizer = new M68kConditionCodeOptimizer(assembler, buffer);
		Assert.Equal(indexed, Snapshot(optimizer, assembler.GetInstructionStream().Count) is null);
		optimizer.Run();
		var tests = assembler.GetInstructionStream().Count(i => i.Opcode == 0xB5FC);
		Assert.Equal(boundary == "none" ? 1 : 2, tests);
		var optimized = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		foreach (var address in new uint[] { 0, 1, 0x7FFFFFFF, 0xFFFFFFFF })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(original, address, flags), Execute(optimized, address, flags));
		if (boundary != "none")
			Assert.Equal(buffer.Labels["outside:entry"], buffer.Labels["outside:alias"]);
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void GlobalTestAliasPreventsRedundantTestRemoval(bool indexed, bool protect)
	{
		var (assembler, buffer, offsets) = Create(indexed, false);
		if (protect)
		{
			buffer.Labels.Add("not-in-scope:entry", offsets.Test);
			buffer.Labels.Add("not-in-scope:alias", offsets.Test);
		}
		var original = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		new M68kConditionCodeOptimizer(assembler, buffer).Run();
		Assert.Equal(protect ? 1 : 0, assembler.GetInstructionStream().Count(i => i.Opcode == 0x4A80));
		var optimized = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes;
		foreach (var value in new uint[] { 0, 1, 0x80000000, 0xFFFFFFFF })
			Assert.Equal(Execute(original, value, 0x1F), Execute(optimized, value, 0x1F));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MembershipMatchesGlobalOffsetsAfterEveryMutation(bool indexed)
	{
		var assembler = new M68kAssembler();
		var buffer = (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
		for (var i = 0; i < 100; i++) buffer.Labels.Add("label:" + i, i % 17 - 8);
		var optimizer = new M68kConditionCodeOptimizer(assembler, buffer);
		var contains = typeof(M68kConditionCodeOptimizer).GetMethod("HasLabelAt", Private)!
			.CreateDelegate<Func<int, HashSet<int>?, bool>>(optimizer);
		for (var step = 0; step < 5; step++)
		{
			var expected = buffer.Labels.Values.ToHashSet();
			var snapshot = Snapshot(optimizer, indexed ? 1 : 10000);
			Assert.Equal(indexed, snapshot is null);
			for (var offset = -12; offset <= 24; offset++)
				Assert.Equal(expected.Contains(offset), contains(offset, snapshot));
			switch (step)
			{
				case 0: buffer.Labels.InsertBytes(0, 3, true); break;
				case 1: buffer.Labels.RemoveBytes(-2, 6, true); break;
				case 2: buffer.Labels.MoveOffsets(-1, 5, 18, includeEnd: true); break;
				case 3:
					buffer.Labels["label:0"] = 23;
					buffer.Labels.Remove("label:1");
					buffer.Labels.Add("replacement", -10);
					break;
			}
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void MembershipSeesRetargetingAndLazyRebasingBetweenRuns(bool indexed)
	{
		var (assembler, buffer, offsets) = Create(indexed, false);
		buffer.Labels.Add("global:entry", offsets.Test);
		var optimizer = new M68kConditionCodeOptimizer(assembler, buffer);
		optimizer.Run();
		Assert.False(optimizer.Changed);
		buffer.InsertBytes(0, 2);
		buffer.WriteWord(0, 0x4E71);
		buffer.Labels["global:entry"] = assembler.Offset;
		optimizer.Run();
		Assert.True(optimizer.Changed);
		Assert.DoesNotContain(assembler.GetInstructionStream(), i => i.Opcode == 0x4A80);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void BatchedRemovalsKeepLaterLabeledTestsAndRelocations(bool indexed)
	{
		var assembler = new M68kAssembler();
		var buffer = (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
		for (var i = 0; i < 3; i++)
		{
			assembler.EmitWord(0x2002); // MOVE.L D2,D0 establishes NZVC.
			if (i == 2) assembler.Mark("global:keep");
			assembler.EmitWord(0x4A80);
			assembler.EmitBranch(M68kCondition.NotEqual, "next:" + i);
			assembler.EmitWord(0x4E71);
			assembler.Mark("next:" + i);
			assembler.EmitWord(0x4E75); // Independent entries keep their own value facts.
		}
		assembler.Mark("method:end"); assembler.MarkDataStart();
		assembler.EmitAddress("global:keep");
		if (indexed) AddOutsideLabels(buffer);
		assembler.SetAnalysisScope(null, "method:end", ["method:end", "next:0", "next:1", "next:2"]);
		var before = buffer.DataStartOffset!.Value;
		new M68kConditionCodeOptimizer(assembler, buffer).Run();
		Assert.Equal(before - 4, buffer.DataStartOffset);
		var test = Assert.Single(assembler.GetInstructionStream(), i => i.Opcode == 0x4A80);
		Assert.Equal(buffer.Labels["global:keep"], test.Offset);
		Assert.Equal(buffer.DataStartOffset, Assert.Single(buffer.Addresses).Offset);
		Assert.Equal(3, buffer.Branches.Count);
	}

	private static HashSet<int>? Snapshot(M68kConditionCodeOptimizer optimizer, int count) =>
		typeof(M68kConditionCodeOptimizer).GetMethod("CreateLabelOffsetSnapshot", Private)!
			.CreateDelegate<Func<int, HashSet<int>?>>(optimizer)(count);

	private static (M68kAssembler Assembler, M68kAssemblyBuffer Buffer, (int Test, int Branch) Offsets)
		Create(bool indexed, bool knownZero, bool taken = true)
	{
		var assembler = new M68kAssembler();
		var buffer = (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
		if (knownZero)
		{
			assembler.EmitWord(0xB5FC); assembler.EmitLong(0); // CMPA.L #0,A2
			assembler.EmitBranch(M68kCondition.NotEqual, "body");
			assembler.EmitWord(0x70FF); assembler.EmitWord(0x4E75);
			assembler.Mark("body");
			assembler.EmitWord(0x2203); // MOVE.L D3,D1 clobbers conditions.
		}
		else assembler.EmitWord(0x2002); // MOVE.L D2,D0
		var test = assembler.Offset;
		if (knownZero) { assembler.EmitWord(0xB5FC); assembler.EmitLong(0); }
		else assembler.EmitWord(0x4A80); // TST.L D0
		var branch = assembler.Offset;
		assembler.EmitBranch(taken ? M68kCondition.NotEqual : M68kCondition.Equal, "done");
		assembler.EmitWord(0x70FE); assembler.EmitWord(0x4E75);
		assembler.Mark("done");
		assembler.EmitWord(0x7007); assembler.EmitWord(0x4E75);
		assembler.Mark("method:end"); assembler.MarkDataStart();
		if (indexed) AddOutsideLabels(buffer);
		// Deliberately exclude the global aliases supplied by individual tests.
		assembler.SetAnalysisScope(null, "method:end", ["body", "done", "method:end"]);
		return (assembler, buffer, (test, branch));
	}

	private static void AddOutsideLabels(M68kAssemblyBuffer buffer)
	{
		for (var i = 0; i < 4096; i++) buffer.Labels.Add("outside:" + i, 100000 + i * 2);
	}

	private static (uint D0, uint D1, uint D2, uint D3, uint A2, uint Sp, ushort Flags)
		Execute(byte[] code, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load));
		bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[2] = value; cpu.State.D[3] = ~value; cpu.State.A[2] = value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 32 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.D[2], cpu.State.D[3], cpu.State.A[2],
			cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
