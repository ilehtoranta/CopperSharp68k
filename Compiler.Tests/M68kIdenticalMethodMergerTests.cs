/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kIdenticalMethodMergerTests
{
	[Theory]
	[InlineData(0u, 7u)]
	[InlineData(1u, 42u)]
	[InlineData(uint.MaxValue, 42u)]
	public void IdenticalMethodsKeepDistinctExecutableEntries(uint input, uint expected)
	{
		var fixture = CreateConditionalMethods();
		var beforeBytes = fixture.Buffer.Bytes.Count;
		var statistics = Merge(fixture);

		Assert.Equal(1, statistics.Groups);
		Assert.Equal(1, statistics.Thunks);
		Assert.Equal(statistics.GrossBytesRemoved - 6, statistics.NetBytesSaved);
		Assert.Equal(beforeBytes - statistics.NetBytesSaved, fixture.Buffer.Bytes.Count);
		Assert.NotEqual(fixture.Buffer.Labels["first"], fixture.Buffer.Labels["second"]);
		Assert.Equal(0x4EF9, fixture.Buffer.ReadWord(fixture.Buffer.Labels["second"]));
		Assert.Contains(fixture.Buffer.Addresses, fixup =>
			fixup.Target == "first" &&
			fixup.Offset == fixture.Buffer.Labels["second"] + 2);

		var linked = fixture.Assembler.Link(0x10000, new Dictionary<string, uint>());
		Assert.Equal(expected, Execute(linked, "first", input));
		Assert.Equal(expected, Execute(linked, "second", input));
	}

	[Fact]
	public void AddressTakenEntryStillPointsToItsOwnThunk()
	{
		var fixture = CreateConditionalMethods(addressTakenSecond: true);
		Assert.Equal(1, Merge(fixture).Thunks);
		var linked = fixture.Assembler.Link(0x10000, new Dictionary<string, uint>());
		var pointerOffset = fixture.Buffer.DataStartOffset!.Value;
		var pointer = ReadLong(linked.Bytes, pointerOffset);
		Assert.Equal(0x10000u + (uint)linked.Labels["second"], pointer);
		Assert.NotEqual(linked.Labels["first"], linked.Labels["second"]);
	}

	[Fact]
	public void DifferentExternalTargetsDoNotMerge()
	{
		var assembler = new M68kAssembler();
		var ranges = new List<(string StartLabel, string EndLabel)>();
		foreach (var (name, target) in new[] { ("first", "left"), ("second", "right") })
		{
			assembler.Mark(name);
			assembler.EmitJsr(target, external: true);
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + ":end");
			ranges.Add((name, name + ":end"));
		}
		assembler.IdenticalMethodRanges = ranges;
		var fixture = new Fixture(assembler, Buffer(assembler));
		var before = fixture.Buffer.Bytes.ToArray();
		Assert.Equal(0, Merge(fixture).Thunks);
		Assert.Equal(before, fixture.Buffer.Bytes);
	}

	[Theory]
	[InlineData("anchor")]
	[InlineData("effects")]
	[InlineData("alignment")]
	[InlineData("interior-reference")]
	public void PositionSensitiveOrAnalysisOwnedMethodsRetainTheirBodies(string reason)
	{
		var fixture = CreateConditionalMethods();
		var first = fixture.Buffer.Labels["first"];
		switch (reason)
		{
			case "anchor":
				fixture.Buffer.AnalysisAnchors.Add("protected", first);
				break;
			case "effects":
				var instruction = fixture.Assembler.GetInstructionStream(first).First();
				fixture.Buffer.InstructionEffectOverrides.Add(first,
					M68kInstructionDataflow.GetEffects(instruction));
				break;
			case "alignment":
				fixture.Assembler.RequestLongAlignment("first");
				break;
			case "interior-reference":
				fixture.Assembler.MarkDataStart();
				fixture.Assembler.EmitAddress("first_zero");
				break;
		}
		var before = fixture.Buffer.Bytes.ToArray();
		Assert.Equal(0, Merge(fixture).Thunks);
		Assert.Equal(before, fixture.Buffer.Bytes);
	}

	private sealed record Fixture(M68kAssembler Assembler, M68kAssemblyBuffer Buffer);

	private static Fixture CreateConditionalMethods(bool addressTakenSecond = false)
	{
		var assembler = new M68kAssembler();
		var ranges = new List<(string StartLabel, string EndLabel)>();
		foreach (var name in new[] { "first", "second" })
		{
			assembler.Mark(name);
			assembler.EmitWord(0x4A80); // TST.L D0.
			assembler.EmitBranch(M68kCondition.Equal, name + "_zero");
			assembler.EmitWord(0x702A); // MOVEQ #42,D0.
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + "_zero");
			assembler.EmitWord(0x7007); // MOVEQ #7,D0.
			assembler.EmitWord(0x4E75);
			assembler.Mark(name + ":end");
			ranges.Add((name, name + ":end"));
		}
		assembler.IdenticalMethodRanges = ranges;
		if (addressTakenSecond)
		{
			assembler.MarkDataStart();
			assembler.EmitAddress("second");
		}
		return new Fixture(assembler, Buffer(assembler));
	}

	private static M68kIdenticalMethodMerger.Statistics Merge(Fixture fixture) =>
		new M68kIdenticalMethodMerger(fixture.Assembler, fixture.Buffer).Run();

	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) =>
		(M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!
			.GetValue(assembler)!;

	private static uint Execute(LinkedCode linked, string entry, uint input)
	{
		const uint load = 0x10000;
		const uint stack = 0x80000;
		const uint sentinel = 0x1000;
		var bus = new TestBus(0x100000);
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
		bus.WriteLong(stack, sentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load + (uint)linked.Labels[entry], stack);
		cpu.State.D[0] = input;
		for (var step = 0; step < 20 && cpu.State.ProgramCounter != sentinel; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		return cpu.State.D[0];
	}

	private static uint ReadLong(byte[] bytes, int offset) =>
		((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) |
		((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
}
