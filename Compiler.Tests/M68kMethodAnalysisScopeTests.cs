/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMethodAnalysisScopeTests
{
	private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
	private static readonly FieldInfo BufferField = typeof(M68kAssembler).GetField("_buffer", Private)!;
	private sealed record Scope(string? Start, string End, string[] Names);

	public static IEnumerable<object[]> Boundaries()
	{
		yield return [Array.Empty<int>()];
		yield return [new[] { 0 }];
		yield return [new[] { 4 }];
		yield return [new[] { 0, 0, 0, 0 }];
		yield return [new[] { 4, 4, 4, 8 }];
		yield return [new[] { -20, -10, 0, 0, 6 }];
		yield return [new[] { 12, 4, 16, 8, 20 }];
		yield return [new[] { 10, 8, 6 }];
		yield return [new[] { int.MinValue, -1, 0, int.MaxValue, int.MaxValue }];
	}

	[Theory]
	[MemberData(nameof(Boundaries))]
	public void ClosedRangesAndDictionaryOrderMatchTheOriginalScan(int[] positions)
	{
		var assembler = new M68kAssembler();
		var buffer = Buffer(assembler);
		var ends = positions.Select((_, i) => "method:" + i + ":end").ToArray();
		buffer.Labels.Add("last-in-offset-order", int.MaxValue);
		for (var i = 0; i < positions.Length; i++)
		{
			buffer.Labels.Add(ends[i], positions[i]);
			buffer.Labels.Add("alias:" + i, positions[i]);
			if (positions[i] != int.MinValue) buffer.Labels.Add("before:" + i, positions[i] - 1);
			if (positions[i] != int.MaxValue) buffer.Labels.Add("after:" + i, positions[i] + 1);
		}
		buffer.Labels.Add("negative", int.MinValue);
		buffer.Labels.Add("zero", 0);
		buffer.Labels.Remove("last-in-offset-order");
		buffer.Labels.Add("reused-slot", 0);
		var labels = buffer.Labels.ToArray();
		AssertScopes(Original(assembler, ends), Build(assembler, ends));
		Assert.Equal(labels, buffer.Labels.ToArray());
	}

	[Fact]
	public void SharedBoundaryAliasesBelongToEveryCoincidentScopeInDictionaryOrder()
	{
		var assembler = new M68kAssembler(); var labels = Buffer(assembler).Labels;
		labels.Add("alias:z", 4); labels.Add("first:end", 4); labels.Add("alias:a", 4);
		labels.Add("empty:one:end", 4); labels.Add("empty:two:end", 4); labels.Add("last:end", 8);
		var scopes = Build(assembler, ["first:end", "empty:one:end", "empty:two:end", "last:end"]);
		var boundaryNames = labels.Where(label => label.Value == 4).Select(label => label.Key).ToArray();
		Assert.Equal(4, scopes.Length);
		foreach (var scope in scopes)
			Assert.Equal(boundaryNames, scope.Names.Where(name => labels[name] == 4));
		Assert.Equal(boundaryNames, scopes[1].Names);
		Assert.Equal(boundaryNames, scopes[2].Names);
	}

	[Theory]
	[InlineData(68000, false)]
	[InlineData(68000, true)]
	[InlineData(68020, false)]
	[InlineData(68020, true)]
	public void SeededScopesMatchOriginalAfterSlotReuseAndRebasing(int seed, bool unordered)
	{
		var random = new Random(seed);
		for (var sample = 0; sample < 100; sample++)
		{
			var assembler = new M68kAssembler(); var map = Buffer(assembler).Labels;
			var positions = Enumerable.Range(0, random.Next(0, 48)).Select(_ => random.Next(-20, 100)).ToArray();
			if (!unordered) Array.Sort(positions);
			var ends = positions.Select((_, i) => "method:" + i + ":end").ToArray();
			for (var i = 0; i < 300; i++) map.Add("label:" + i, random.Next(-30, 120));
			for (var i = 0; i < ends.Length; i++) map.Add(ends[i], positions[i]);
			for (var i = 0; i < 40; i++) { map.Remove("label:" + i); map.Add("replacement:" + i, random.Next(-30, 120)); }
			AssertScopes(Original(assembler, ends), Build(assembler, ends));
			map.InsertBytes(10, 6, true); map.RemoveBytes(2, 12, false);
			if (ends.Length > 1) map[ends[1]] = -25; // Layout may change the original ordering.
			AssertScopes(Original(assembler, ends), Build(assembler, ends));
		}
	}

	[Fact]
	public void CapturedNamesKeepTheirOrderWhileScopeBoundsResolveCurrentLabels()
	{
		var assembler = new M68kAssembler(); var buffer = Buffer(assembler);
		buffer.Labels.Add("first:end", 4); buffer.Labels.Add("second:end", 12);
		buffer.Labels.Add("entry", 4); buffer.Labels.Add("interior", 8);
		var scope = Build(assembler, ["first:end", "second:end"])[1];
		var names = scope.Names.ToArray();
		buffer.Labels.InsertBytes(0, 6, true);
		buffer.Labels["interior"] = 30;
		assembler.SetAnalysisScope(scope.Start, scope.End, scope.Names);
		try
		{
			Assert.Equal((10, 18), assembler.GetAnalysisRange());
			Assert.Equal(names, assembler.AnalysisLabelNames);
			Assert.Equal(30, assembler.Labels["interior"]);
		}
		finally { assembler.ClearAnalysisScope(); }
	}

	[Fact]
	public void RepeatedEndNamesRetainDistinctZeroWidthScopes()
	{
		var assembler = new M68kAssembler(); Buffer(assembler).Labels.Add("same:end", 4);
		string[] ends = ["same:end", "same:end", "same:end"];
		AssertScopes(Original(assembler, ends), Build(assembler, ends));
		Assert.Equal(3, Build(assembler, ends).Length);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public void SharedAndEmptyMethodScopesPreserveMc68000Execution(bool second)
	{
		var assembler = new M68kAssembler();
		assembler.Mark("first"); EmitMethod(2, 1, 7, "first:zero");
		assembler.Mark("first:end"); assembler.Mark("empty:end"); assembler.Mark("second");
		EmitMethod(3, 2, 9, "second:zero"); assembler.Mark("second:end");
		assembler.MarkDataStart(); assembler.EmitLong(0x207C0000); assembler.EmitLong(0x00004E75);
		const uint load = 0x10000;
		var before = assembler.Link(load, new Dictionary<string, uint>()).Bytes;
		var name = second ? "second" : "first";
		var oldEntry = assembler.Labels[name];
		assembler.OptimizeForM68000();
		var statistics = assembler.PeepholeOptimizationStatistics;
		Assert.Equal(3, statistics.MethodRanges); Assert.True(statistics.Converged);
		Assert.False(assembler.HasAnalysisScope);
		var after = assembler.Link(load, new Dictionary<string, uint>()).Bytes;
		Assert.Equal(before[^8..], after[^8..]);
		foreach (var origin in new uint[] { load, 0x30000 })
		foreach (var value in new uint[] { 0, 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF })
		{
			var original = Execute(before, origin, oldEntry, value);
			var optimized = Execute(after, origin, assembler.Labels[name], value);
			Assert.Equal(original, optimized);
			Assert.Equal(value == 0 ? (second ? 9u : 7u) : unchecked(value + (second ? 2u : 1u)), optimized.Result);
		}

		void EmitMethod(int source, int increment, int zero, string target)
		{
			assembler.EmitWord((ushort)(0x2000 | source)); assembler.EmitWord(0x4A80);
			assembler.EmitBranch(M68kCondition.Equal, target);
			assembler.EmitWord((ushort)(0x5080 | increment << 9)); assembler.EmitWord(0x4E75);
			assembler.Mark(target); assembler.EmitWord((ushort)(0x7000 | zero)); assembler.EmitWord(0x4E75);
		}
	}

	private static M68kAssemblyBuffer Buffer(M68kAssembler assembler) => (M68kAssemblyBuffer)BufferField.GetValue(assembler)!;
	private static Scope[] Build(M68kAssembler assembler, string[] ends)
	{
		var pipeline = new M68kOptimizerPipeline(assembler, Buffer(assembler), M68kCpuTarget.M68000, M68kClrPolicy.Auto);
		var scopes = (Array)typeof(M68kOptimizerPipeline).GetMethod("BuildMethodAnalysisScopes", Private)!.Invoke(pipeline, [ends])!;
		return scopes.Cast<object>().Select(scope =>
		{
			var type = scope.GetType();
			return new Scope((string?)type.GetProperty("StartLabel")!.GetValue(scope),
				(string)type.GetProperty("EndLabel")!.GetValue(scope)!,
				((IReadOnlyList<string>)type.GetProperty("LabelNames")!.GetValue(scope)!).ToArray());
		}).ToArray();
	}
	private static Scope[] Original(M68kAssembler assembler, string[] ends) => ends.Select((end, index) =>
	{
		var start = index == 0 ? null : ends[index - 1];
		var begin = start is null ? 0 : assembler.Labels[start];
		var finish = assembler.Labels[end];
		return new Scope(start, end, assembler.Labels.Where(label => label.Value >= begin && label.Value <= finish)
			.Select(label => label.Key).ToArray());
	}).ToArray();
	private static void AssertScopes(Scope[] expected, Scope[] actual)
	{
		Assert.Equal(expected.Length, actual.Length);
		for (var i = 0; i < expected.Length; i++)
		{
			Assert.Equal(expected[i].Start, actual[i].Start); Assert.Equal(expected[i].End, actual[i].End);
			Assert.Equal(expected[i].Names, actual[i].Names);
		}
	}
	private static (uint Result, uint Stack) Execute(byte[] code, uint load, int entry, uint value)
	{
		const uint stack = 0x80000, done = 0x50000;
		var bus = new TestBus(); code.CopyTo(bus.Memory.AsSpan((int)load)); bus.WriteLong(stack, done);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load + (uint)entry, stack); cpu.State.D[2] = value; cpu.State.D[3] = value;
		for (var i = 0; i < 32 && cpu.State.ProgramCounter != done; i++) cpu.ExecuteInstruction();
		Assert.Equal(done, cpu.State.ProgramCounter);
		return (cpu.State.D[0], cpu.State.A[7]);
	}
}
