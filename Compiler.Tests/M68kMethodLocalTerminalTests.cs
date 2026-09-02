/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMethodLocalTerminalTests
{
	[Theory]
	[InlineData(M68kCpuModel.M68000, false)]
	[InlineData(M68kCpuModel.M68000, true)]
	[InlineData(M68kCpuModel.M68020, false)]
	[InlineData(M68kCpuModel.M68020, true)]
	[InlineData(M68kCpuModel.M68040, false)]
	[InlineData(M68kCpuModel.M68040, true)]
	public void SharedExitsPreserveRegistersFlagsFrameAndAllPaths(M68kCpuModel model, bool invert)
	{
		var original = Create(invert);
		var optimized = Create(invert);
		var statistics = Merge(optimized);
		Assert.Equal(2, statistics.Groups);
		Assert.Equal(4, statistics.MergedCopies);
		Assert.Equal(invert ? 4 : 0, statistics.InvertedBranches);
		Assert.Equal(invert ? 0 : 4, statistics.Trampolines);
		Assert.True(statistics.NetBytesSaved > 0);
		Assert.Equal(original.Buffer.Bytes.Count - statistics.NetBytesSaved, optimized.Buffer.Bytes.Count);
		foreach (var branch in optimized.Buffer.Branches.Where(branch => branch.Target.StartsWith("__m68k_local_epilogue_", StringComparison.Ordinal)))
		{
			var owner = optimized.Assembler.MethodLocalTerminalRanges.Single(range =>
				branch.OpcodeOffset >= optimized.Buffer.Labels[range.StartLabel] &&
				branch.OpcodeOffset < optimized.Buffer.Labels[range.EndLabel]);
			Assert.InRange(optimized.Buffer.Labels[branch.Target],
				optimized.Buffer.Labels[owner.StartLabel], optimized.Buffer.Labels[owner.EndLabel] - 1);
		}
		foreach (var name in new[] { "first", "second" })
		foreach (var value in new uint[] { 0, 1, 2, 3, 0x80000000, 0xffffffff })
		foreach (var residue in new uint[] { 0, 2 })
		foreach (var flags in new ushort[] { 0, 31 })
		{
			var before = Execute(original, model, name, value, residue, flags);
			var after = Execute(optimized, model, name, value, residue, flags);
			Assert.Equal(value <= 2 ? 0u : 42u, after.Data[0]);
			Assert.Equal(before.Data, after.Data);
			Assert.Equal(before.Addresses, after.Addresses);
			Assert.Equal(before.Flags, after.Flags);
			if (value > 2) Assert.True(after.Cycles <= before.Cycles,
				$"Success path became slower: {before.Cycles} -> {after.Cycles}.");
			if (model == M68kCpuModel.M68000)
				Assert.True(after.Cycles <= before.Cycles + 10,
					$"An exit adds more than one word BRA: {before.Cycles} -> {after.Cycles}.");
		}
	}

	[Fact]
	public void EntryAndEndAnchorsAndSuffixMetadataRemainValid()
	{
		var fixture = Create(invert: true, suffix: true);
		var buffer = fixture.Buffer;
		var before = buffer.Bytes.ToArray();
		var suffixStart = buffer.Labels["suffix"];
		var data = buffer.DataStartOffset!.Value;
		var writable = buffer.WritableDataStartOffset!.Value;
		var bss = buffer.BssStartOffset!.Value;
		var address = buffer.Addresses.ToArray();
		var pc = buffer.PcRelative.ToArray();
		var effects = buffer.InstructionEffectOverrides.ToArray();
		var statistics = Merge(fixture);
		var delta = statistics.NetBytesSaved;
		Assert.True(delta > 0);
		Assert.Equal(before[suffixStart..], buffer.Bytes.Skip(buffer.Labels["suffix"]));
		Assert.Equal(suffixStart - delta, buffer.Labels["suffix"]);
		Assert.Equal(data - delta, buffer.DataStartOffset);
		Assert.Equal(writable - delta, buffer.WritableDataStartOffset);
		Assert.Equal(bss - delta, buffer.BssStartOffset);
		Assert.Equal(address.Select(item => item with { Offset = item.Offset - delta }), buffer.Addresses);
		Assert.Equal(pc.Select(item => item with { DisplacementOffset = item.DisplacementOffset - delta }), buffer.PcRelative);
		Assert.Equal(effects.Select(item => new KeyValuePair<int, M68kInstructionEffects>(item.Key - delta, item.Value)), buffer.InstructionEffectOverrides);
		foreach (var name in new[] { "first", "second" })
		{
			Assert.Equal(buffer.Labels[name + "_error2"], buffer.Labels[name + "_error0"]);
			Assert.Equal(buffer.Labels[name + "_error0"], buffer.AnalysisAnchors[name + "_error0_start"]);
			Assert.Equal(buffer.Labels[name + "_continue0"], buffer.AnalysisAnchors[name + "_error0_end"]);
			Assert.Equal(buffer.Labels[name + ":end"], buffer.AnalysisAnchors[name + "_method_end"]);
		}
	}

	[Theory]
	[InlineData("interior-anchor")]
	[InlineData("interior-label")]
	[InlineData("effects")]
	[InlineData("address")]
	[InlineData("pc-relative")]
	[InlineData("address-taken-entry")]
	[InlineData("alignment")]
	[InlineData("raw-pc-relative")]
	[InlineData("opaque")]
	[InlineData("no-ranges")]
	public void UnprovenMetadataAndPositionDependentCodeRetainTheirBytes(string reason)
	{
		var fixture = Create(invert: true, count: 2, methods: 1,
			rawPcRelative: reason == "raw-pc-relative", opaque: reason == "opaque");
		var buffer = fixture.Buffer;
		var start = buffer.Labels["first_error0"];
		switch (reason)
		{
			case "interior-anchor": buffer.AnalysisAnchors.Add("protected", start + 2); break;
			case "interior-label": buffer.Labels.Add("protected", start + 2); break;
			case "effects":
				var instruction = fixture.Assembler.GetInstructionStream(start).First();
				buffer.InstructionEffectOverrides.Add(start, M68kInstructionDataflow.GetEffects(instruction)); break;
			case "address": buffer.Addresses.Add(new AddressFixup(start + 2, "external", true)); break;
			case "pc-relative": buffer.PcRelative.Add(new PcRelativeFixup(start + 2, "first")); break;
			case "address-taken-entry": fixture.Assembler.EmitAddress("first_error0"); break;
			case "alignment": fixture.Assembler.RequestLongAlignment("first_error0"); break;
			case "no-ranges": fixture.Assembler.MethodLocalTerminalRanges = []; break;
		}
		var before = Snapshot(buffer);
		Assert.Equal(0, Merge(fixture).MergedCopies);
		Assert.Equal(before, Snapshot(buffer));
	}

	[Fact]
	public void IdenticalExitsInDifferentMethodsAreNotOneLocalGroup()
	{
		var fixture = Create(invert: true, count: 1);
		var before = Snapshot(fixture.Buffer);
		Assert.Equal(0, Merge(fixture).MergedCopies);
		Assert.Equal(before, Snapshot(fixture.Buffer));
	}

	[Fact]
	public void DefaultMergerStillLeavesThisFarGlobalPoolAlone()
	{
		var fixture = Create(invert: false);
		var before = Snapshot(fixture.Buffer);
		Assert.Equal(0, new M68kTerminalEpilogueMerger(fixture.Assembler, fixture.Buffer).Run().MergedCopies);
		Assert.Equal(before, Snapshot(fixture.Buffer));
	}

	[Fact]
	public void RegionalReuseDoesNotPreemptCheaperMethodLocalBranches()
	{
		var localOnly = Create(invert: false);
		var localStatistics = new M68kTerminalEpilogueMerger(
			localOnly.Assembler, localOnly.Buffer, enableMethodLocalReuse: true).Run();
		var expectedLocalLabels = localOnly.Buffer.Labels.Keys.Count(label =>
			label.StartsWith("__m68k_local_epilogue_", StringComparison.Ordinal));
		Assert.True(expectedLocalLabels > 0);

		var combined = Create(invert: false);
		var combinedStatistics = new M68kTerminalEpilogueMerger(
			combined.Assembler,
			combined.Buffer,
			enableMethodLocalReuse: true,
			enableRegionalReuse: true).Run();
		var actualLocalLabels = combined.Buffer.Labels.Keys.Count(label =>
			label.StartsWith("__m68k_local_epilogue_", StringComparison.Ordinal));

		Assert.Equal(expectedLocalLabels, actualLocalLabels);
		Assert.True(combinedStatistics.NetBytesSaved >= localStatistics.NetBytesSaved);
	}

	private sealed record Fixture(M68kAssembler Assembler, M68kAssemblyBuffer Buffer);
	private static Fixture Create(bool invert, int count = 3, int methods = 2,
		bool suffix = false, bool rawPcRelative = false, bool opaque = false)
	{
		var assembler = new M68kAssembler();
		var ranges = new List<(string StartLabel, string EndLabel)>();
		foreach (var name in new[] { "first", "second" }.Take(methods))
		{
			assembler.Mark(name);
			assembler.EmitWord(0x48e7); assembler.EmitWord(0x3f3e); // Save D2-D7/A2-A6.
			assembler.EmitWord(0x4fef); assembler.EmitWord(unchecked((ushort)-20));
			assembler.EmitWord(0x243c); assembler.EmitLong(0x13579bdf); // Clobber D2 and A2.
			assembler.EmitWord(0x247c); assembler.EmitLong(0x12345678);
			for (var index = 0; index < count; index++)
			{
				assembler.EmitWord(0x0c80); assembler.EmitLong((uint)index);
				assembler.EmitBranch(invert && index < count - 1 ? M68kCondition.NotEqual : M68kCondition.Equal,
					name + (invert && index < count - 1 ? "_continue" : "_error") + index);
				if (invert && index < count - 1)
				{
					EmitError(index);
					assembler.Mark(name + "_continue" + index);
					assembler.MarkAnalysisAnchor(name + "_error" + index + "_end");
				}
			}
			assembler.Mark(name + "_success");
			EmitTail(42);
			for (var index = invert ? count - 1 : 0; index < count; index++) EmitError(index);
			assembler.Mark(name + ":end");
			assembler.MarkAnalysisAnchor(name + "_method_end");
			ranges.Add((name, name + ":end"));
			void EmitError(int index)
			{
				assembler.Mark(name + "_error" + index);
				assembler.MarkAnalysisAnchor(name + "_error" + index + "_start");
				if (rawPcRelative) { assembler.EmitWord(0x41fa); assembler.EmitWord(0); }
				if (opaque) assembler.EmitWord(0x4e70); // RESET cannot be an ordinary effect-free move.
				EmitTail(0);
			}
		}
		assembler.MethodLocalTerminalRanges = ranges;
		assembler.Mark("unowned-padding");
		for (var index = 0; index < 20000; index++) assembler.EmitWord(0x4e71);
		var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
			.GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(assembler)!;
		if (suffix)
		{
			assembler.Mark("suffix");
			assembler.EmitBsr("first"); assembler.EmitJsr("external", external: true);
			assembler.EmitWord(0x41fa); assembler.EmitPcRelativeWord("second"); assembler.EmitWord(0x4e75);
			var firstSuffix = assembler.GetInstructionStream(buffer.Labels["suffix"]).First();
			buffer.InstructionEffectOverrides.Add(firstSuffix.Offset, M68kInstructionDataflow.GetEffects(firstSuffix));
			assembler.MarkDataStart(); assembler.EmitAddress("second");
			assembler.MarkWritableDataStart(); assembler.EmitLong(0x2468ace0);
			assembler.MarkBssStart(); assembler.EmitLong(0);
		}
		return new(assembler, buffer);
		void EmitTail(byte value)
		{
			assembler.EmitWord((ushort)(0x7000 | value));
			assembler.EmitWord(0x4fef); assembler.EmitWord(20);
			assembler.EmitWord(0x4cdf); assembler.EmitWord(0x7cfc);
			assembler.EmitWord(0x4e75);
		}
	}

	private static M68kTerminalEpilogueMerger.Statistics Merge(Fixture fixture) =>
		new M68kTerminalEpilogueMerger(fixture.Assembler, fixture.Buffer, true).Run();
	private sealed record Observation(uint[] Data, uint[] Addresses, ushort Flags, long Cycles);
	private static Observation Execute(Fixture fixture, M68kCpuModel model, string entry,
		uint value, uint residue, ushort flags)
	{
		const uint load = 0x10000, sentinel = 0x1000;
		var stack = 0x80000u + residue;
		var linked = fixture.Assembler.Link(load, new Dictionary<string, uint>());
		var bus = new TestBus(0x100000);
		linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
		bus.WriteLong(stack, sentinel);
		bus.WriteLong(stack + 4, 0xa55a3cc3);
		bus.WriteLong(stack - 68, 0x11223344);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(load + (uint)linked.Labels[entry], stack);
		for (var index = 1; index < 8; index++) cpu.State.D[index] = 0xdada0000u + (uint)index;
		for (var index = 0; index < 7; index++) cpu.State.A[index] = 0xa0a00000u + (uint)index;
		cpu.State.D[0] = value;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var step = 0; step < 100 && cpu.State.ProgramCounter != sentinel; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		Assert.Equal(0xa55a3cc3u, bus.ReadLong(stack + 4));
		Assert.Equal(0x11223344u, bus.ReadLong(stack - 68));
		for (var index = 2; index < 8; index++) Assert.Equal(0xdada0000u + (uint)index, cpu.State.D[index]);
		for (var index = 2; index < 7; index++) Assert.Equal(0xa0a00000u + (uint)index, cpu.State.A[index]);
		Assert.Equal(linked.Bytes, bus.Memory.AsSpan((int)load, linked.Bytes.Length).ToArray());
		return new(cpu.State.D.ToArray(), cpu.State.A.ToArray(), (ushort)(cpu.State.StatusRegister & 31), cpu.State.Cycles);
	}
	private static string Snapshot(M68kAssemblyBuffer buffer) => JsonSerializer.Serialize(new
	{
		Bytes = buffer.Bytes.ToArray(), Labels = buffer.Labels.ToArray(), Anchors = buffer.AnalysisAnchors.ToArray(),
		Branches = buffer.Branches.ToArray(), Addresses = buffer.Addresses.ToArray(), PcRelative = buffer.PcRelative.ToArray(),
		Effects = buffer.InstructionEffectOverrides.ToArray(), buffer.DataStartOffset, buffer.WritableDataStartOffset, buffer.BssStartOffset
	});
}
