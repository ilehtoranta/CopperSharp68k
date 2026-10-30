/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kFixupTableScanTests
{
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer",
			BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;

	[Theory]
	[InlineData(0, 2)]
	[InlineData(0, 8)]
	[InlineData(1, 2)]
	[InlineData(1, 8)]
	[InlineData(2, 2)]
	[InlineData(2, 8)]
	public void DuplicateOffsetsThrowOnlyInsideTheScopedOperandWindow(int kind, int offset)
	{
		var assembler = CreateSmallScope(); var buffer = Buffer(assembler);
		Add(buffer, kind, offset); Add(buffer, kind, offset);
		// Scope ends at 2, so end+6 is the excluded operand-window boundary.
		if (offset == 2) Assert.Throws<ArgumentException>(() => assembler.GetInstructionStream());
		else Assert.Single(assembler.GetInstructionStream());
	}

	[Theory]
	[InlineData(0)]
	[InlineData(1)]
	[InlineData(2)]
	public void SameCountReplacementRebuildsCurrentScopedTables(int kind)
	{
		var assembler = CreateSmallScope(); var buffer = Buffer(assembler);
		Add(buffer, kind, 8); Add(buffer, kind, 8);
		Assert.Single(assembler.GetInstructionStream());
		for (var index = 0; index < 2; index++)
		{
			switch (kind)
			{
				case 0: buffer.Addresses[index] = buffer.Addresses[index] with { Offset = 2 }; break;
				case 1: buffer.Branches[index] = buffer.Branches[index] with { OpcodeOffset = 2 }; break;
				case 2: buffer.PcRelative[index] = buffer.PcRelative[index] with { DisplacementOffset = 2 }; break;
			}
		}
		Assert.Throws<ArgumentException>(() => assembler.GetInstructionStream());
	}

	[Theory]
	[InlineData(0x10000u, false, false)]
	[InlineData(0x10000u, false, true)]
	[InlineData(0x10000u, true, false)]
	[InlineData(0x10000u, true, true)]
	[InlineData(0x40000u, false, false)]
	[InlineData(0x40000u, false, true)]
	[InlineData(0x40000u, true, false)]
	[InlineData(0x40000u, true, true)]
	public void AllFixupKindsAndRebasedEffectsPreserveCompiledMc68000Execution(
		uint origin, bool scoped, bool removePrefix)
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x4E71); assembler.Mark("entry");
		assembler.EmitWord(0x203C); assembler.EmitAddress("external", external: true, addend: 7);
		assembler.EmitWord(0x223A); assembler.EmitPcRelativeWord("payload"); // MOVE.L payload(PC),D1
		assembler.EmitWord(0x4A82); // TST.L D2
		assembler.EmitBranch(M68kCondition.Equal, "zero");
		assembler.EmitWord(0x5280); // ADDQ.L #1,D0
		assembler.EmitBranch(M68kCondition.True, "done");
		assembler.Mark("zero"); assembler.EmitWord(0x5380); // SUBQ.L #1,D0
		assembler.Mark("done"); assembler.EmitWord(0x4E75); assembler.Mark("code:end");
		assembler.MarkDataStart(); assembler.Mark("payload"); assembler.EmitLong(0x89123456);
		// Unrelated relocations remain in all three global lists. Keep their
		// linked word displacements within MC68000 range as well.
		for (var index = 0; index < 512; index++)
		{
			assembler.EmitAddress("external", external: true, addend: -3);
			assembler.EmitBranch(M68kCondition.NotEqual, "entry");
			assembler.EmitWord(0x203A); assembler.EmitPcRelativeWord("payload");
		}
		var buffer = Buffer(assembler);
		var test = assembler.GetExecutableInstructionRange(0, buffer.Labels["code:end"])
			.Single(instruction => instruction.Opcode == 0x4A82);
		var testEffects = M68kInstructionDataflow.GetEffects(test) with { IsBarrier = true };
		// Deliberately insert overrides in descending order; Link and deletion
		// must relocate by numeric offset without losing the physical effects.
		assembler.SetInstructionEffects(test.Offset, testEffects);
		assembler.SetInstructionEffects(0, default(M68kInstructionEffects) with { IsBarrier = true });
		if (scoped) assembler.SetAnalysisScope("entry", "code:end", ["entry", "code:end"]);
		var imports = new Dictionary<string, uint> { ["external"] = 0x65000 };
		var original = assembler.Link(origin, imports).Bytes;
		var originalEntry = origin + (uint)buffer.Labels["entry"];
		if (removePrefix)
		{
			buffer.RemoveBytes(0, 2);
			Assert.Single(buffer.InstructionEffectOverrides);
		}
		var rebasedTest = assembler.GetExecutableInstructionRange(0, buffer.Labels["code:end"])
			.Single(instruction => instruction.Opcode == 0x4A82);
		Assert.Equal(testEffects, buffer.InstructionEffectOverrides[rebasedTest.Offset]);
		var flow = M68kInstructionDataflow.Analyze(assembler);
		Assert.True(flow.TryGetFacts(rebasedTest.Offset, out var facts));
		Assert.Equal(testEffects, facts.Effects);
		new M68kConditionCodeOptimizer(assembler, buffer).RunThen(
			new M68kPeepholeOptimizer(assembler, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []));
		var optimized = assembler.Link(origin, imports).Bytes;
		var optimizedEntry = origin + (uint)buffer.Labels["entry"];
		foreach (var value in new[] { 0u, 1u, 0x80000000u, 0xFFFFFFFFu })
		foreach (var flags in new ushort[] { 0, 0x1F })
		{
			var expected = Execute(original, origin, originalEntry, value, flags);
			Assert.Equal(value == 0 ? 0x65006u : 0x65008u, expected.D0);
			Assert.Equal(0x89123456u, expected.D1);
			Assert.Equal(expected, Execute(optimized, origin, optimizedEntry, value, flags));
		}
	}

	private static M68kAssembler CreateSmallScope()
	{
		var assembler = new M68kAssembler(); assembler.EmitWord(0x4E71); assembler.Mark("end");
		assembler.EmitLong(0); assembler.EmitLong(0);
		assembler.SetAnalysisScope(null, "end", ["end"]);
		return assembler;
	}

	private static void Add(M68kAssemblyBuffer buffer, int kind, int offset)
	{
		switch (kind)
		{
			case 0: buffer.Addresses.Add(new(offset, "target", true, 7)); break;
			case 1: buffer.Branches.Add(new(offset, "target")); break;
			case 2: buffer.PcRelative.Add(new(offset, "target")); break;
		}
	}

	private static (uint D0, uint D1, uint D2, uint Sp, ushort Flags) Execute(
		byte[] code, uint origin, uint entry, uint value, ushort flags)
	{
		const uint stack = 0x80000, done = 0x70000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)origin)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(entry, stack); cpu.State.D[2] = value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var index = 0; index < 32 && cpu.State.ProgramCounter != done; index++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.D[2], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
