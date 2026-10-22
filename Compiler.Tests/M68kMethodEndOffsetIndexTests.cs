/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMethodEndOffsetIndexTests
{
	private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
	private delegate bool ReadsIncoming(int target, int register,
		ref IReadOnlyList<M68kEmittedInstruction>? instructions, Dictionary<(int, int), bool> proofs);
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", PrivateInstance)!;
	private static string Name(int index) => index % 5 == 0 ? $"method:{index}:end" : $"method:{index}:IL_0000";

	[Theory]
	[InlineData(101)]
	[InlineData(202)]
	[InlineData(303)]
	[InlineData(404)]
	public void IndexedEndsMatchCurrentEnumerationAfterMetadataEdits(int seed)
	{
		var random = new Random(seed);
		var map = new M68kOffsetMap();
		for (var i = 0; i < 512; i++) map.Add(Name(i), random.Next(0, 1024));
		// Build before the edits so every mutation exercises index maintenance.
		Assert.NotNull(map.MethodEndAfter(0));
		for (var step = 0; step < 600; step++)
		{
			var start = random.Next(-16, 1056);
			var length = random.Next(1, 20);
			switch (random.Next(7))
			{
				case 0: map[Name(random.Next(600))] = random.Next(-32, 1100); break;
				case 1: map.Remove(Name(random.Next(600))); break;
				case 2: map.InsertBytes(start, length, random.Next(2) == 0); break;
				case 3: map.RemoveBytes(start, length, random.Next(2) == 0); break;
				case 4:
					map.MoveOffsets(start, start + length, random.Next(-32, 1100),
						random.Next(2) == 0, random.Next(2) == 0);
					break;
				case 5: map.MoveOffsetsAt(start, random.Next(-32, 1100)); break;
				case 6:
					map.Remove(Name(random.Next(600)));
					map.TryAdd(Name(random.Next(600)), start);
					break;
			}
			var ends = map.Where(p => p.Key.EndsWith(":end", StringComparison.Ordinal))
				.Select(p => p.Value).Distinct().Order().ToArray();
			foreach (var value in new[] { int.MinValue, int.MaxValue, -1, 0, start, start + length, random.Next(-64, 1200) })
			{
				int? after = ends.Where(v => v > value).Select(v => (int?)v).FirstOrDefault();
				int? before = ends.Where(v => v <= value).Select(v => (int?)v).LastOrDefault();
				Assert.Equal(after, map.MethodEndAfter(value));
				Assert.Equal(before, map.MethodEndAtOrBefore(value));
			}
		}
		map.Clear();
		Assert.Null(map.MethodEndAfter(0)); Assert.Null(map.MethodEndAtOrBefore(0));
		map.Add("new:end", 4);
		Assert.Equal(4, map.MethodEndAfter(0)); Assert.Equal(4, map.MethodEndAtOrBefore(4));
	}

	[Fact]
	public void SharedAndEmptyBoundariesUseStrictSuccessorAndInclusivePredecessor()
	{
		var map = new M68kOffsetMap
		{
			["empty:end"] = 0, ["first:end"] = 4, ["alias:end"] = 4,
			["ordinary:END"] = 6, ["last:end"] = 8
		};
		Assert.Equal(4, map.MethodEndAfter(0));
		Assert.Equal(4, map.MethodEndAtOrBefore(4)); Assert.Equal(8, map.MethodEndAfter(4));
		map.Remove("first:end"); Assert.Equal(4, map.MethodEndAfter(0));
		map.Remove("alias:end");
		Assert.Equal(8, map.MethodEndAfter(0)); Assert.Equal(0, map.MethodEndAtOrBefore(4));
		map["last:end"] = 2;
		Assert.Equal(2, map.MethodEndAfter(0)); Assert.Equal(2, map.MethodEndAtOrBefore(4));
	}

	[Fact]
	public void ColdClearAndUnorderedRepopulationBuildFreshBoundaries()
	{
		var map = new M68kOffsetMap { ["old:end"] = 4 };
		map.Clear();
		map.Add("later:end", 12); map.Add("earlier:end", 2);
		Assert.Equal(2, map.MethodEndAfter(0));
		Assert.Equal(2, map.MethodEndAtOrBefore(4));
		Assert.Equal(12, map.MethodEndAfter(4));
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DeletionHandlesInteriorEndsOverlappingTheRebasedSuffix(bool clampInterior)
	{
		var map = new M68kOffsetMap
		{
			["a:end"] = 4, ["b:end"] = 6, ["c:end"] = 8, ["d:end"] = 10, ["e:end"] = 16
		};
		Assert.Equal(4, map.MethodEndAfter(0));
		map.RemoveBytes(4, 8, clampInterior);
		var expected = map.Values.Distinct().Order().ToArray();
		for (var offset = 0; offset < 18; offset++)
		{
			Assert.Equal(expected.Where(v => v > offset).Select(v => (int?)v).FirstOrDefault(), map.MethodEndAfter(offset));
			Assert.Equal(expected.Where(v => v <= offset).Select(v => (int?)v).LastOrDefault(), map.MethodEndAtOrBefore(offset));
		}
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void DictionaryMutationsKeepBoundariesCurrent(bool initializeEarly)
	{
		var map = new M68kOffsetMap();
		if (initializeEarly) Assert.Null(map.MethodEndAfter(0));
		map.Add("first:end", 2);
		Assert.True(map.TryAdd("second:end", 4));
		map["third:end"] = 6;
		map.Add(new KeyValuePair<string, int>("fourth:end", 8));
		Assert.Equal(2, map.MethodEndAfter(0));
		map["first:end"] = 12;
		map["first:end"] = 12;
		Assert.False(map.TryAdd("first:end", 99));
		Assert.Throws<ArgumentException>(() => map.Add("first:end", 99));
		Assert.False(map.Remove(new KeyValuePair<string, int>("first:end", 2)));
		Assert.False(map.Remove("missing:end"));
		Assert.True(map.Remove(new KeyValuePair<string, int>("first:end", 12)));
		Assert.True(map.Remove("second:end"));
		Assert.Equal(6, map.MethodEndAfter(0));
		Assert.Equal(8, map.MethodEndAtOrBefore(99));
		map.Clear(); map.Clear();
		Assert.Null(map.MethodEndAfter(int.MinValue));
		Assert.Null(map.MethodEndAtOrBefore(int.MaxValue));
		map.Add("minimum:end", int.MinValue); map.Add("maximum:end", int.MaxValue);
		Assert.Equal(int.MinValue, map.MethodEndAtOrBefore(int.MinValue));
		Assert.Equal(int.MaxValue, map.MethodEndAfter(int.MinValue));
		Assert.Null(map.MethodEndAfter(int.MaxValue));
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void RangeBoundaryOptionsAndNoOpsPreserveNeighbors(bool includeStart, bool includeEnd)
	{
		var map = new M68kOffsetMap { ["a:end"] = 4, ["b:end"] = 8, ["c:end"] = 12 };
		Assert.Equal(4, map.MethodEndAfter(0));
		map.InsertBytes(4, 2, shiftBoundary: includeStart);
		Assert.Equal(includeStart ? 6 : 4, map.MethodEndAfter(0));
		map.RemoveBytes(4, 2, clampInterior: true);
		Assert.Equal(4, map.MethodEndAfter(0));
		map.MoveOffsets(4, 8, 20, includeStart, includeEnd);
		Assert.Equal(includeStart ? (includeEnd ? 12 : 8) : 4, map.MethodEndAfter(0));
		Assert.Equal(includeStart || includeEnd ? 20 : 12, map.MethodEndAtOrBefore(99));
		var expected = map.ToArray();
		map.InsertBytes(4, 0, true); map.RemoveBytes(4, 0, true);
		map.MoveOffsets(8, 4, 0); map.MoveOffsets(4, 4, 0);
		Assert.Equal(expected, map.ToArray());
		Assert.Equal(expected.Where(p => p.Key.EndsWith(":end", StringComparison.Ordinal)).Max(p => p.Value), map.MethodEndAtOrBefore(99));
	}

	[Theory]
	[InlineData(false, false)]
	[InlineData(false, true)]
	[InlineData(true, false)]
	[InlineData(true, true)]
	public void RebasedIndexedCalleeFoldingPreservesCompiledMc68000Behavior(bool scoped, bool observesConditions)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x2008); assembler.EmitWord(0x2C40); // MOVE.L A0,D0; MOVEA.L D0,A6
		assembler.EmitCall("method:callee"); assembler.EmitWord(0x2001); assembler.EmitWord(0x4E75);
		assembler.Mark("caller:end"); assembler.Mark("empty:end"); assembler.Mark("method:callee");
		if (observesConditions) assembler.EmitWord(0x57C1); // SEQ D1 observes the copied address's Z.
		assembler.EmitWord(0x7005); assembler.EmitWord(0x4E75); assembler.Mark("method:callee:end"); assembler.Mark("alias:end");
		// Warm the index before rebasing, then verify a scoped caller can still
		// prove the global callee using the updated boundaries.
		var buffer = Buffer(assembler);
		Assert.Equal(buffer.Labels["caller:end"], buffer.Labels.MethodEndAfter(0));
		buffer.InsertBytes(0, 2); buffer.WriteWord(0, 0x4E71);
		Assert.Equal(buffer.Labels["method:callee:end"], buffer.Labels.MethodEndAfter(buffer.Labels["method:callee"]));
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

	[Theory]
	[InlineData(2, false)]
	[InlineData(4, true)]
	[InlineData(6, true)]
	[InlineData(8, true)]
	[InlineData(10, true)]
	public void RebasedCalleeProofRejectsOperandAndSharedBoundaryEntries(int target, bool readsIncoming)
	{
		var assembler = new M68kAssembler();
		assembler.EmitWord(0x4E71); // Removed after warming the method-end index.
		assembler.EmitWord(0x4E75); assembler.Mark("caller:end"); assembler.Mark("empty:end");
		assembler.EmitWord(0x203C); assembler.EmitLong(0x4E757000); assembler.EmitWord(0x4E75);
		assembler.Mark("callee:end"); assembler.Mark("alias:end");
		Assert.Equal(4, Buffer(assembler).Labels.MethodEndAfter(0));
		Buffer(assembler).RemoveBytes(0, 2);
		assembler.SetAnalysisScope(null, "caller:end", ["caller:end"]);
		var reads = typeof(M68kPeepholeOptimizer).GetMethod("CallTargetReadsDataRegisterBeforeOverwrite", PrivateInstance)!
			.CreateDelegate<ReadsIncoming>(Optimizer(assembler));
		IReadOnlyList<M68kEmittedInstruction>? instructions = null;
		Assert.Equal(readsIncoming, reads(target, 0, ref instructions, new()));
		if (instructions is not null) Assert.DoesNotContain(instructions, instruction => instruction.Offset is 4 or 6);
		Assert.Equal((0, 2), assembler.GetAnalysisRange());
	}

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
