/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMethodEndLabelIndexTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!;
	private delegate bool ReadsIncoming(int target, int register,
		ref IReadOnlyList<M68kEmittedInstruction>? instructions, Dictionary<(int, int), bool> proofs);

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void NamesUseTheExactOrdinalSuffixBeforeAndAfterInitialization(bool initializeEarly)
	{
		var map = new M68kOffsetMap();
		if (initializeEarly) Assert.Empty(map.MethodEndLabelNames);
		foreach (var name in new[] { "", ":end", "method:end", "METHOD:end", "method:End", "weekend", "method:end:IL_0000", "method:end\0" })
			map.Add(name, 0);
		Assert.Equal(new[] { ":end", "METHOD:end", "method:end" }, map.MethodEndLabelNames.Order(StringComparer.Ordinal));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void EveryDictionaryMutationPathMaintainsNames(bool initializeEarly)
	{
		var map = new M68kOffsetMap();
		if (initializeEarly) Assert.Empty(map.MethodEndLabelNames);
		map.Add("first:end", 2);
		Assert.True(map.TryAdd("second:end", 4));
		((IDictionary<string, int>)map)["third:end"] = 6;
		((ICollection<KeyValuePair<string, int>>)map).Add(new("fourth:end", 8));
		map.Add("ordinary", 10);
		AssertNames(map, "first:end", "second:end", "third:end", "fourth:end");
		map[new string("first:end".ToCharArray())] = 12;
		Assert.Equal(12, map["first:end"]);
		Assert.False(map.TryAdd("first:end", 99));
		Assert.Throws<ArgumentException>(() => map.Add("first:end", 99));
		Assert.False(map.Remove(new KeyValuePair<string, int>("first:end", 2)));
		Assert.False(map.Remove("missing:end"));
		Assert.True(map.Remove(new KeyValuePair<string, int>("first:end", 12)));
		Assert.True(map.Remove("second:end"));
		AssertNames(map, "third:end", "fourth:end");
		map.Clear();
		Assert.Empty(map.MethodEndLabelNames);
		map["after-clear:end"] = 42;
		AssertNames(map, "after-clear:end");
		map.Clear(); map.Clear();
		Assert.Empty(map.MethodEndLabelNames);
	}

	[Fact]
	public void SameCountReplacementAndDictionarySlotReuseDoNotKeepStaleNames()
	{
		var map = new M68kOffsetMap { ["old:end"] = 4, ["ordinary"] = 8 };
		AssertNames(map, "old:end");
		map.Remove("old:end"); map.Add("replacement", 4);
		Assert.Empty(map.MethodEndLabelNames);
		map.Remove("ordinary"); map.Add("new:end", 8);
		AssertNames(map, "new:end");
		Assert.Equal(2, map.Count);
	}

	[Fact]
	public void WarmNameQueriesSurvivePositionEditsWithoutRebuildingGlobalSets()
	{
		var map = new M68kOffsetMap();
		for (var i = 0; i < 16384; i++) map.Add(i % 4 == 0 ? $"method{i}:end" : $"label{i}", i * 4);
		Assert.Equal(4096, map.MethodEndLabelNames.Count);
		for (var i = 0; i < 3; i++) GC.KeepAlive(map.MethodEndLabelNames);
		var start = GC.GetAllocatedBytesForCurrentThread();
		for (var i = 0; i < 32; i++)
		{
			map.InsertBytes(0, 2, shiftBoundary: true);
			GC.KeepAlive(map.MethodEndLabelNames);
			map.RemoveBytes(0, 2, clampInterior: false);
			GC.KeepAlive(map.MethodEndLabelNames);
		}
		var allocated = GC.GetAllocatedBytesForCurrentThread() - start;
		Assert.True(allocated < 32768, $"Repeated name queries and rebases allocated {allocated} bytes.");
		Assert.Equal(4096, map.MethodEndLabelNames.Count);
	}

	[Theory]
	[InlineData(13)]
	[InlineData(7621)]
	public void MixedNameAndPositionEditsMatchTheOriginalGlobalScan(int seed)
	{
		var map = new M68kOffsetMap();
		var flat = new Dictionary<string, int>(StringComparer.Ordinal);
		var random = new Random(seed);
		for (var i = 0; i < 128; i++) Add(i, random.Next(256));
		for (var step = 128; step < 1128; step++)
		{
			var offset = random.Next(256);
			var count = random.Next(9);
			switch (random.Next(6))
			{
				case 0: Add(step, offset); break;
				case 1:
					if (flat.Count == 0) break;
					var removed = flat.Keys.ElementAt(random.Next(flat.Count));
					map.Remove(removed); flat.Remove(removed); break;
				case 2:
					if (flat.Count == 0) break;
					var replaced = flat.Keys.ElementAt(random.Next(flat.Count));
					map[replaced] = flat[replaced] = offset; break;
				case 3:
					map.InsertBytes(offset, count, shiftBoundary: true);
					foreach (var item in flat) if (item.Value >= offset) flat[item.Key] += count;
					break;
				case 4:
					map.RemoveBytes(offset, count, clampInterior: false);
					foreach (var item in flat) if (item.Value >= offset + count) flat[item.Key] -= count;
					break;
				case 5:
					map.MoveOffsets(offset, offset + count, 20);
					foreach (var item in flat) if (item.Value >= offset && item.Value < offset + count) flat[item.Key] = 20;
					break;
			}
			if (step % 17 == 0) Check();
		}
		Check();
		void Add(int i, int offset) { var name = i % 4 == 0 ? $"method{i}:end" : $"label{i}"; map.Add(name, offset); flat.Add(name, offset); }
		void Check()
		{
			Assert.Equal(flat.ToArray(), map.ToArray());
			Assert.Equal(flat.Keys.Where(IsEnd).Order(StringComparer.Ordinal), map.MethodEndLabelNames.Order(StringComparer.Ordinal));
			Assert.Equal(flat.Where(item => IsEnd(item.Key)).Select(item => item.Value).Distinct().Order(),
				map.MethodEndLabelNames.Select(name => map[name]).Distinct().Order());
		}
	}

	[Fact]
	public void BoundaryBuilderResolvesCurrentGlobalOffsetsAliasesAndExtremes()
	{
		var assembler = new M68kAssembler();
		var buffer = Buffer(assembler);
		buffer.Labels.Add("first:end", 4); buffer.Labels.Add("alias:end", 4);
		buffer.Labels.Add("zero:end", 0); buffer.Labels.Add("negative:end", -4);
		buffer.Labels.Add("minimum:end", int.MinValue); buffer.Labels.Add("maximum:end", int.MaxValue);
		buffer.Labels.Add("ignored:End", 12);
		assembler.SetAnalysisScope(null, "first:end", ["first:end"]);
		var build = typeof(M68kPeepholeOptimizer).GetMethod("BuildControlFlowMethodEnds", PrivateInstance)!
			.CreateDelegate<Func<int[]>>(Optimizer(assembler));
		Assert.Equal(new[] { int.MinValue, -4, 0, 4, int.MaxValue }, build());
		buffer.Labels["first:end"] = 8;
		buffer.Labels.Remove("negative:end"); buffer.Labels.Add("helper:end", 10);
		Assert.Equal(new[] { int.MinValue, 0, 4, 8, 10, int.MaxValue }, build());
		buffer.Labels.Clear(); buffer.Labels.Add("replacement:end", 6);
		Assert.Equal(new[] { 6 }, build());
	}

	[Fact]
	public void EachRoundResolvesRebasedMethodEndOffsets()
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E71); assembler.EmitWord(0x4E75); assembler.Mark("first:end");
		assembler.EmitWord(0x4E75); assembler.Mark("second:end"); assembler.Mark("empty:end");
		var buffer = Buffer(assembler);
		var optimizer = Optimizer(assembler);
		var build = typeof(M68kPeepholeOptimizer).GetMethod("BuildControlFlowMethodEnds", PrivateInstance)!
			.CreateDelegate<Func<int[]>>(optimizer);
		Assert.Equal(new[] { 4, 6 }, build());
		buffer.RemoveBytes(0, 2);
		Assert.Equal(new[] { 2, 4 }, build());
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		Assert.Equal(new[] { 4, 6 }, build());
		buffer.Labels.MoveOffsetsAt(6, 4);
		Assert.Equal(new[] { 4 }, build());
	}

	[Theory]
	[InlineData(2, false)]
	[InlineData(4, true)]
	[InlineData(6, true)]
	[InlineData(8, true)]
	[InlineData(10, true)]
	public void GlobalCalleeProofStillRejectsOperandAndSharedBoundaryEntries(int target, bool readsIncoming)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E75); assembler.Mark("caller:end"); assembler.Mark("empty:end");
		assembler.EmitWord(0x203C); assembler.EmitLong(0x4E757000); assembler.EmitWord(0x4E75);
		assembler.Mark("callee:end"); assembler.Mark("alias:end");
		assembler.SetAnalysisScope(null, "caller:end", ["caller:end"]);
		var reads = typeof(M68kPeepholeOptimizer).GetMethod("CallTargetReadsDataRegisterBeforeOverwrite", PrivateInstance)!
			.CreateDelegate<ReadsIncoming>(Optimizer(assembler));
		IReadOnlyList<M68kEmittedInstruction>? instructions = null;
		Assert.Equal(readsIncoming, reads(target, 0, ref instructions, new()));
		if (instructions is not null) Assert.DoesNotContain(instructions, instruction => instruction.Offset is 4 or 6);
		Assert.Equal((0, 2), assembler.GetAnalysisRange());
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void GlobalCalleeFoldingPreservesCompiledMc68000Behavior(bool scoped, bool observesConditions)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2008); assembler.EmitWord(0x2C40); // MOVE.L A0,D0; MOVEA.L D0,A6
		assembler.EmitCall("method:callee"); assembler.EmitWord(0x2001); assembler.EmitWord(0x4E75);
		assembler.Mark("caller:end"); assembler.Mark("empty:end"); assembler.Mark("method:callee");
		if (observesConditions) assembler.EmitWord(0x57C1); // SEQ D1 observes the copied address's Z.
		assembler.EmitWord(0x7005); assembler.EmitWord(0x4E75); assembler.Mark("method:callee:end"); assembler.Mark("alias:end");
		// Keep baseline layout identical: patch only the absolute JSR operand.
		var buffer = Buffer(assembler);
		var before = buffer.Bytes.ToArray();
		PatchCall(before, buffer.Labels["method:callee"]);
		if (scoped) assembler.SetAnalysisScope(null, "caller:end", ["caller:end"]);
		var optimizer = Optimizer(assembler);
		var fold = typeof(M68kPeepholeOptimizer).GetMethod("TryFoldDataToAddressRegisterMove", PrivateInstance)!
			.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer);
		Assert.Equal(!observesConditions, fold(M68kInstructionDataflow.Analyze(assembler)));
		var after = buffer.Bytes.ToArray();
		PatchCall(after, buffer.Labels["method:callee"]);
		Assert.Equal(before.Length - (observesConditions ? 0 : 2), after.Length);
		foreach (var address in new uint[] { 0, 1, 0x80000000, 0xFFFFFFFF })
		foreach (var flags in new ushort[] { 0, 0x1F })
			Assert.Equal(Execute(before, address, flags), Execute(after, address, flags));

		void PatchCall(byte[] code, int target)
		{
			var offset = Assert.Single(buffer.Addresses).Offset;
			System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(code.AsSpan(offset, 4), 0x10000u + (uint)target);
		}
	}

	private static bool IsEnd(string name) => name.EndsWith(":end", StringComparison.Ordinal);
	private static void AssertNames(M68kOffsetMap map, params string[] expected) =>
		Assert.Equal(expected.Order(StringComparer.Ordinal), map.MethodEndLabelNames.Order(StringComparer.Ordinal));
	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) => (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
	private static M68kPeepholeOptimizer Optimizer(M68kAssembler assembler) =>
		new(assembler, Buffer(assembler), M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);

	private static (uint D0, uint D1, uint A6, uint Sp, ushort Flags) Execute(byte[] code, uint address, ushort flags)
	{
		const uint load = 0x10000, stack = 0x80000, done = 0x20000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load, stack); cpu.State.A[0] = address; cpu.State.D[1] = 0x12345678;
		cpu.State.StatusRegister = (ushort)(0x2000 | flags);
		for (var i = 0; i < 32 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.D[1], cpu.State.A[6], cpu.State.A[7], (ushort)(cpu.State.StatusRegister & 0x1F));
	}
}
