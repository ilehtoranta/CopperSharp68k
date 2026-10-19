/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kTerminalLayoutShapeTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!;

	[Theory]
	[InlineData("no conditional")]
	[InlineData("short conditional")]
	[InlineData("raw conditional")]
	[InlineData("allocated target")]
	[InlineData("unresolved failure")]
	[InlineData("return in success")]
	[InlineData("short closing branch")]
	[InlineData("raw closing branch")]
	[InlineData("unresolved tail")]
	public void ImpossibleShapesAvoidGlobalLookupAllocationAndLeaveMetadataUnchanged(string shape)
	{
		var assembler = new M68kAssembler();
		if (shape is "no conditional" or "short conditional" or "raw conditional")
		{
			if (shape == "short conditional") assembler.EmitWord(0x6602);
			if (shape == "raw conditional") { assembler.EmitWord(0x6600); assembler.EmitWord(2); }
			assembler.EmitWord(0x4E71); assembler.EmitWord(0x4E75);
			assembler.Mark("method:end");
		}
		else
		{
			EmitLayout(assembler, "first:");
			var buffer = Buffer(assembler);
			switch (shape)
			{
				case "allocated target":
					buffer.Labels.Add("allocated_003ABB:failure", 12);
					buffer.Branches[0] = buffer.Branches[0] with { Target = "allocated_003ABB:failure" }; break;
				case "unresolved failure": buffer.Branches[0] = buffer.Branches[0] with { Target = "later:failure" }; break;
				case "return in success": buffer.WriteWord(8, 0x4E75); buffer.WriteWord(10, 0x4E71); buffer.Branches.RemoveAt(1); break;
				case "short closing branch":
					buffer.RemoveBytes(10, 2); buffer.WriteWord(8, 0x6006);
					break;
				case "raw closing branch": buffer.Branches.RemoveAt(1); buffer.WriteWord(10, 6); break;
				case "unresolved tail": buffer.Branches[1] = buffer.Branches[1] with { Target = "later:tail" }; break;
			}
		}
		AddGlobalLabels(assembler);
		var storage = Buffer(assembler);
		var bytes = storage.Bytes.ToArray(); var labels = storage.Labels.ToArray();
		var branches = storage.Branches.ToArray(); var addresses = storage.Addresses.ToArray();
		var pcRelative = storage.PcRelative.ToArray(); var anchors = storage.AnalysisAnchors.ToArray();
		var effects = storage.InstructionEffectOverrides.ToArray(); var dataStart = storage.DataStartOffset;
		var layout = Layout(assembler);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			for (var i = 0; i < 3; i++) Assert.False(layout());
			var start = GC.GetAllocatedBytesForCurrentThread();
			var changed = layout();
			var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
			Assert.False(changed);
			Assert.True(allocated < 32768, $"Impossible shape {shape} allocated {allocated} bytes.");
		}
		finally { assembler.EndInstructionAnalysisRound(); }
		Assert.Equal(bytes, storage.Bytes); Assert.Equal(labels, storage.Labels.ToArray());
		Assert.Equal(branches, storage.Branches); Assert.Equal(addresses, storage.Addresses);
		Assert.Equal(pcRelative, storage.PcRelative); Assert.Equal(anchors, storage.AnalysisAnchors.ToArray());
		Assert.Equal(effects, storage.InstructionEffectOverrides.ToArray()); Assert.Equal(dataStart, storage.DataStartOffset);
	}

	[Theory]
	[InlineData("branch")]
	[InlineData("address")]
	[InlineData("pc-relative")]
	[InlineData("method boundary")]
	[InlineData("nonterminal tail")]
	public void PlausibleShapesStillBuildGlobalFactsAndRetainTheirGuards(string guard)
	{
		var assembler = new M68kAssembler();
		EmitLayout(assembler, "first:", tailKind: guard);
		var buffer = Buffer(assembler);
		if (guard == "branch")
		{
			buffer.WriteWord(12, 0x6000); buffer.WriteWord(14, 0);
			buffer.Branches.Add(new(12, "first:tail"));
		}
		if (guard == "method boundary") buffer.Labels.Add("split:end", 18);
		AddGlobalLabels(assembler);
		var bytes = buffer.Bytes.ToArray(); var labels = buffer.Labels.ToArray();
		var branches = buffer.Branches.ToArray(); var addresses = buffer.Addresses.ToArray(); var pcRelative = buffer.PcRelative.ToArray();
		var layout = Layout(assembler);
		assembler.BeginInstructionAnalysisRound();
		try
		{
			for (var i = 0; i < 3; i++) Assert.False(layout());
			var start = GC.GetAllocatedBytesForCurrentThread();
			Assert.False(layout());
			var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
			Assert.True(allocated >= 32768, $"Plausible candidate {guard} skipped global lookup construction.");
		}
		finally { assembler.EndInstructionAnalysisRound(); }
		Assert.Equal(bytes, buffer.Bytes); Assert.Equal(labels, buffer.Labels.ToArray());
		Assert.Equal(branches, buffer.Branches); Assert.Equal(addresses, buffer.Addresses); Assert.Equal(pcRelative, buffer.PcRelative);
	}

	[Fact]
	public void RejectedCandidatesShareOneLookupWithinTheUnchangedInvocation()
	{
		var assembler = new M68kAssembler();
		EmitLayout(assembler, "first:", tailKind: "nonterminal tail");
		EmitLayout(assembler, "second:", tailKind: "nonterminal tail");
		AddGlobalLabels(assembler);
		var optimizer = Optimizer(assembler);
		var layout = Layout(optimizer);
		var lookup = typeof(M68kPeepholeOptimizer).GetMethod("CreateTerminalBlockLookup", PrivateInstance)!;
		assembler.BeginInstructionAnalysisRound();
		try
		{
			for (var i = 0; i < 3; i++) { Assert.False(layout()); GC.KeepAlive(lookup.Invoke(optimizer, null)); }
			var start = GC.GetAllocatedBytesForCurrentThread();
			GC.KeepAlive(lookup.Invoke(optimizer, null));
			var oneLookup = GC.GetAllocatedBytesForCurrentThread() - start;
			start = GC.GetAllocatedBytesForCurrentThread();
			Assert.False(layout());
			var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
			Assert.InRange(allocated, 32768, oneLookup + 32768);
		}
		finally { assembler.EndInstructionAnalysisRound(); }
	}

	[Theory]
	[InlineData("valid")]
	[InlineData("shape rejection")]
	[InlineData("lookup rejection")]
	public void CandidateTraversalKeepsTheSameFirstSuccessfulRewrite(string first)
	{
		var assembler = new M68kAssembler();
		var prefix = first == "shape rejection" ? "first_003ABB:" : "first:";
		EmitLayout(assembler, prefix, tailKind: first == "lookup rejection" ? "nonterminal tail" : "normal");
		EmitLayout(assembler, "second:");
		var buffer = Buffer(assembler);
		Assert.True(Layout(assembler)());
		Assert.Equal(first == "valid" ? 8 : 16, buffer.Labels[prefix + "tail"]);
		Assert.Equal(first == "valid" ? 36 : 28, buffer.Labels["second:tail"]);
	}

	[Fact]
	public void LaterInvocationsRebuildFactsAfterMetadataAndCodeChanges()
	{
		var assembler = new M68kAssembler();
		EmitLayout(assembler, "first:", tailKind: "nonterminal tail");
		var buffer = Buffer(assembler);
		var layout = Layout(assembler);
		Assert.False(layout());
		buffer.WriteWord(18, 0x4E75);
		buffer.Labels.Add("new-boundary:end", 18);
		Assert.False(layout());
		buffer.Labels.Remove("new-boundary:end");
		buffer.WriteWord(12, 0x6000); buffer.WriteWord(14, 0);
		buffer.Branches.Add(new(12, "first:tail"));
		Assert.False(layout());
		buffer.Branches.RemoveAt(2); buffer.WriteWord(12, 0x72FF); buffer.WriteWord(14, 0x7407);
		Assert.True(layout());
	}

	[Theory]
	[InlineData(2, false)]
	[InlineData(4, false)]
	[InlineData(14, false)]
	[InlineData(2, true)]
	[InlineData(4, true)]
	[InlineData(14, true)]
	public void EligibleLayoutsKeepCompiledMc68000Behavior(int tailLength, bool scoped)
	{
		var assembler = new M68kAssembler();
		EmitLayout(assembler, "first:", tailLength: tailLength);
		var buffer = Buffer(assembler);
		var before = PatchedCode(buffer);
		if (scoped) assembler.SetAnalysisScope(null, "first:method:end", buffer.Labels.Keys.ToArray());
		Assert.True(Layout(assembler)());
		var after = PatchedCode(buffer);
		Assert.Equal(before.Length + tailLength - 4, after.Length);
		foreach (var value in new uint[] { 0, 1, 0x80000000, 0xFFFFFFFF })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(before, value, flags), Execute(after, value, flags));
	}

	private static void EmitLayout(M68kAssembler assembler, string prefix, int tailLength = 4, string tailKind = "normal")
	{
		assembler.Mark(prefix + "entry"); assembler.EmitWord(0x4A80);
		assembler.EmitBranch(M68kCondition.Equal, prefix + "cold");
		assembler.EmitWord(0x722A); assembler.EmitBranch(M68kCondition.True, prefix + "tail");
		assembler.Mark(prefix + "cold"); assembler.EmitWord(0x72FF); assembler.EmitWord(0x7407);
		assembler.Mark(prefix + "tail");
		if (tailKind == "address") { assembler.EmitWord(0x207C); assembler.EmitAddress(prefix + "entry"); }
		else if (tailKind == "pc-relative") { assembler.EmitWord(0x41FA); assembler.EmitPcRelativeWord(prefix + "entry"); }
		else for (var i = 0; i < tailLength - 2; i += 2) assembler.EmitWord(0x4E71);
		assembler.EmitWord((ushort)(tailKind == "nonterminal tail" ? 0x4E71 : 0x4E75));
		assembler.Mark(prefix + "method:end");
	}

	private static void AddGlobalLabels(M68kAssembler assembler)
	{
		assembler.MarkDataStart(); assembler.EmitLong(0);
		for (var i = 0; i < 16384; i++) assembler.Mark("data-alias:" + i);
	}
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) => (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
	private static M68kPeepholeOptimizer Optimizer(M68kAssembler assembler) =>
		new(assembler, Buffer(assembler), M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
	private static Func<bool> Layout(M68kAssembler assembler) => Layout(Optimizer(assembler));
	private static Func<bool> Layout(M68kPeepholeOptimizer optimizer) =>
		typeof(M68kPeepholeOptimizer).GetMethod("TryLayoutColdTerminalBranch", PrivateInstance)!.CreateDelegate<Func<bool>>(optimizer);
	private static byte[] PatchedCode(M68kAssemblyBuffer buffer)
	{
		var bytes = buffer.Bytes.ToArray();
		foreach (var branch in buffer.Branches)
		{
			Assert.Equal(0, buffer.ReadWord(branch.OpcodeOffset) & 0xFF);
			BinaryPrimitives.WriteInt16BigEndian(bytes.AsSpan(branch.OpcodeOffset + 2, 2),
				checked((short)(buffer.Labels[branch.Target] - branch.OpcodeOffset - 2)));
		}
		return bytes;
	}
	private static (uint D0, uint D1, uint D2, uint Sp, ushort Flags) Execute(byte[] code, uint value, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.D[0] = value; cpu.State.D[2] = 0x89ABCDEF;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 32 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.D[2], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
