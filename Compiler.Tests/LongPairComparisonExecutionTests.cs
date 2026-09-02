/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class LongPairComparisonExecutionTests
{
	private const uint LoadAddress = 0x10000;
	private const uint StackAddress = 0x80000;
	private const uint ReturnSentinel = 0x1000;
	private const uint AllocatorAddress = 0x2800;
	private static readonly string FixtureName = typeof(LongPairComparisonFixtures).FullName!;

	public static TheoryData<bool, M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode>
		ComparisonCases
	{
		get
		{
			var cases = new TheoryData<bool, M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode>();
			foreach (var unsigned in new[] { false, true })
			foreach (var cpu in CompilerExecutionTests.CpuTargets)
			foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
				cases.Add(unsigned, (M68kCpuTarget)cpu[0], (M68kCpuModel)cpu[1], mode);
			return cases;
		}
	}

	public static TheoryData<M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode>
		TimeSpanCases
	{
		get
		{
			var cases = new TheoryData<M68kCpuTarget, M68kCpuModel, M68kPeepholeOptimizationMode>();
			foreach (var cpu in CompilerExecutionTests.CpuTargets)
			foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
				cases.Add((M68kCpuTarget)cpu[0], (M68kCpuModel)cpu[1], mode);
			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ComparisonCases))]
	public void MaterializedLongPairComparisonsKeepOperandOrderAndCallerAbi(
		bool unsigned, M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		var entry = unsigned ? nameof(LongPairComparisonFixtures.UnsignedEntry) : nameof(LongPairComparisonFixtures.SignedEntry);
		var scenario = unsigned ? nameof(LongPairComparisonFixtures.UnsignedScenario) : nameof(LongPairComparisonFixtures.SignedScenario);
		var compilation = Compile(entry, target, mode);
		foreach (var pair in ComparisonPairs())
		foreach (var stackRemainder in new uint[] { 0, 2 })
		foreach (var directCallee in new[] { false, true })
		{
			var words = Execute(compilation, scenario, model, stackRemainder, directCallee,
				pair.Left, pair.Right, outputBytes: 52);
			var expected = ExpectedOperators(pair.Left, pair.Right, unsigned);
			for (var operation = 0; operation < expected.Length; operation++)
				Assert.True(expected[operation] == words[operation],
					$"{pair.Name}/{(unsigned ? "unsigned" : "signed")}/{target}/{mode}/SP+{stackRemainder}/direct={directCallee}, operator {operation}: expected {expected[operation]}, got {words[operation]}.");
			Assert.Equal(LongPairComparisonFixtures.Before, words[6]);
			Assert.Equal(LongPairComparisonFixtures.Middle, words[7]);
			Assert.Equal(LongPairComparisonFixtures.After, words[8]);
			Assert.Equal((uint)(pair.Left >> 32), words[9]);
			Assert.Equal((uint)pair.Left, words[10]);
			Assert.Equal((uint)(pair.Right >> 32), words[11]);
			Assert.Equal((uint)pair.Right, words[12]);
		}
	}

	[Theory]
	[MemberData(nameof(TimeSpanCases))]
	public void PinnedTimeSpanConstructionAndFromTicksPreserveBothWordsWithoutComparingTimeSpans(
		M68kCpuTarget target, M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.Create();
		var compilation = Compile(nameof(LongPairComparisonFixtures.TimeSpanEntry), target, mode, pack.ManifestPath);
		foreach (var ticks in new long[] { 0, 1, -1, 937_840_050_000, 937_840_050_001, 0x100000000, long.MinValue, long.MaxValue })
		foreach (var stackRemainder in new uint[] { 0, 2 })
		foreach (var directCallee in new[] { false, true })
		{
			var bits = unchecked((ulong)ticks);
			var words = Execute(compilation, nameof(LongPairComparisonFixtures.TimeSpanScenario), model,
				stackRemainder, directCallee, bits, 0, outputBytes: 28);
			// Check the representation against host-supplied words. Comparing two
			// TimeSpans could hide a constructor defect behind a comparison defect.
			Assert.Equal((uint)(bits >> 32), words[0]);
			Assert.Equal((uint)bits, words[1]);
			Assert.Equal((uint)(bits >> 32), words[2]);
			Assert.Equal((uint)bits, words[3]);
			Assert.Equal(LongPairComparisonFixtures.Before, words[4]);
			Assert.Equal(LongPairComparisonFixtures.Middle, words[5]);
			Assert.Equal(LongPairComparisonFixtures.After, words[6]);
		}
	}

	private static IEnumerable<(string Name, ulong Left, ulong Right)> ComparisonPairs()
	{
		foreach (var pair in new (string Name, ulong Left, ulong Right)[]
		{
			("equal high words", 0x000000da5b9f7f50, 0x000000da5b9f7f51),
			("different high words", 0x000000da5b9f7f50, 0x000000db5b9f7f50),
			("low sign bit", 0x000000017fffffff, 0x0000000180000000),
			("signed extrema", 0x7fffffffffffffff, 0x8000000000000000),
			("all ones and zero", ulong.MaxValue, 0),
			("negative adjacent values", 0xffffffff00000000, 0xffffffff00000001),
			("negative low sign bit", 0xffffffff80000000, 0xffffffff7fffffff),
			("low carry", 0x00000000ffffffff, 0x0000000100000000)
		})
		{
			yield return pair;
			yield return (pair.Name + " reversed", pair.Right, pair.Left);
		}
		foreach (var value in new ulong[] { 0, ulong.MaxValue, 0x000000da5b9f7f50, 0x8000000000000000 })
			yield return ("equal values", value, value);
	}

	private static uint[] ExpectedOperators(ulong left, ulong right, bool unsigned)
	{
		var signedLeft = unchecked((long)left);
		var signedRight = unchecked((long)right);
		return
		[
			Word(left == right), Word(left != right),
			Word(unsigned ? left < right : signedLeft < signedRight),
			Word(unsigned ? left <= right : signedLeft <= signedRight),
			Word(unsigned ? left > right : signedLeft > signedRight),
			Word(unsigned ? left >= right : signedLeft >= signedRight)
		];
	}

	private static uint Word(bool value) => value ? 1u : 0;

	private static M68kCompilationResult Compile(string entry, M68kCpuTarget cpu,
		M68kPeepholeOptimizationMode mode, string? manifestPath = null) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(LongPairComparisonFixtures).Assembly.Location,
			EntryPoint = $"{FixtureName}::{entry}",
			Cpu = cpu,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = manifestPath is null ? M68kRuntimeProfile.Freestanding : M68kRuntimeProfile.Application,
			MemoryManagement = manifestPath is null ? M68kMemoryManagement.None : null,
			ExceptionMode = manifestPath is null ? M68kExceptionMode.Yolo : M68kExceptionMode.Full,
			PeepholeOptimization = mode,
			IncludedExportNames = [],
			Imports = new Dictionary<string, uint> { [M68kRuntimeImports.Allocate] = AllocatorAddress },
			FrameworkImplementationPack = manifestPath is null ? null : new M68kFrameworkImplementationPackOptions(manifestPath)
		});

	private static uint[] Execute(M68kCompilationResult compilation, string scenario, M68kCpuModel model,
		uint stackRemainder, bool directCallee, ulong left, ulong right, int outputBytes)
	{
		var bus = new TestBus(0x100000);
		var stack = StackAddress + stackRemainder;
		var stackBottom = StackAddress - 8192;
		bus.Memory.AsSpan((int)stackBottom - 16, (int)(stack - stackBottom) + 148).Fill(0xa5);
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in compilation.Relocations)
		{
			var address = LoadAddress + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		var heap = 0x60000u;
		bus.RegisterGateway(AllocatorAddress, state =>
		{
			var size = state.D[0];
			state.D[0] = heap;
			heap += (size + 3) & ~3u;
		});
		var input = (int)LongPairComparisonFixtures.InputAddress;
		var output = (int)LongPairComparisonFixtures.OutputAddress;
		bus.Memory.AsSpan(input - 16, 48).Fill(0x6d);
		bus.Memory.AsSpan(output - 16, outputBytes + 32).Fill(0xa7);
		BinaryPrimitives.WriteUInt64BigEndian(bus.Memory.AsSpan(input, 8), left);
		BinaryPrimitives.WriteUInt64BigEndian(bus.Memory.AsSpan(input + 8, 8), right);
		var inputBefore = bus.Memory.AsSpan(input - 16, 48).ToArray();
		var codeBefore = bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray();
		bus.WriteLong(stack, ReturnSentinel);
		var entry = directCallee
			? Assert.Single(compilation.Symbols, symbol => symbol.Name == $"{FixtureName}::{scenario}").Address
			: compilation.EntryPoint;
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + entry, stack);
		for (var register = 2; register <= 7; register++) cpu.State.D[register] = 0xd2000000u + (uint)register;
		for (var register = 2; register <= 6; register++) cpu.State.A[register] = 0x00090000u + (uint)register * 256;
		if (directCallee)
		{
			cpu.State.D[0] = LongPairComparisonFixtures.Before;
			cpu.State.A[0] = LongPairComparisonFixtures.InputAddress;
			cpu.State.D[1] = LongPairComparisonFixtures.Middle;
			cpu.State.A[1] = LongPairComparisonFixtures.After;
		}
		for (var step = 0; step < 20_000 && cpu.State.ProgramCounter != ReturnSentinel; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"{scenario}/{model}: halted at {cpu.State.ProgramCounter:X8}.");
		}
		Assert.Equal(ReturnSentinel, cpu.State.ProgramCounter);
		Assert.Equal(42u, cpu.State.D[0]);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		if (directCallee)
		{
			for (var register = 2; register <= 7; register++) Assert.Equal(0xd2000000u + (uint)register, cpu.State.D[register]);
			for (var register = 2; register <= 6; register++) Assert.Equal(0x00090000u + (uint)register * 256, cpu.State.A[register]);
		}
		Assert.Equal(inputBefore, bus.Memory.AsSpan(input - 16, 48).ToArray());
		Assert.Equal(codeBefore, bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray());
		Assert.All(bus.Memory.AsSpan((int)stackBottom - 16, 16).ToArray(), value => Assert.Equal((byte)0xa5, value));
		Assert.All(bus.Memory.AsSpan((int)stack + 4, 128).ToArray(), value => Assert.Equal((byte)0xa5, value));
		Assert.All(bus.Memory.AsSpan(output - 16, 16).ToArray(), value => Assert.Equal((byte)0xa7, value));
		Assert.All(bus.Memory.AsSpan(output + outputBytes, 16).ToArray(), value => Assert.Equal((byte)0xa7, value));
		return Enumerable.Range(0, outputBytes / 4).Select(index => bus.ReadLong((uint)output + (uint)index * 4)).ToArray();
	}
}

public static class LongPairComparisonFixtures
{
	public const uint InputAddress = 0x40000;
	public const uint OutputAddress = 0x50000;
	public const uint Before = 0x13579bdf;
	public const uint Middle = 0x2468ace0;
	public const uint After = 0x89abcdef;

	public static uint SignedEntry() => SignedScenario(Before, APTR.FromPointer(InputAddress), Middle, After);
	public static uint UnsignedEntry() => UnsignedScenario(Before, APTR.FromPointer(InputAddress), Middle, After);
	public static uint TimeSpanEntry() => TimeSpanScenario(Before, APTR.FromPointer(InputAddress), Middle, After);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedScenario(uint before, APTR input, uint middle, uint after)
	{
		var leftHigh = APTR.ReadUInt32(input, 0);
		var leftLow = APTR.ReadUInt32(input, 4);
		var rightHigh = APTR.ReadUInt32(input, 8);
		var rightLow = APTR.ReadUInt32(input, 12);
		var left = M68kRuntime.CombineInt64(leftHigh, leftLow);
		var right = M68kRuntime.CombineInt64(rightHigh, rightLow);
		var output = APTR.FromPointer(OutputAddress);
		APTR.WriteUInt32(output, 0, SignedEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 4, SignedNotEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 8, SignedLess(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 12, SignedLessOrEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 16, SignedGreater(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 20, SignedGreaterOrEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 24, before);
		APTR.WriteUInt32(output, 28, middle);
		APTR.WriteUInt32(output, 32, after);
		APTR.WriteUInt32(output, 36, leftHigh);
		APTR.WriteUInt32(output, 40, leftLow);
		APTR.WriteUInt32(output, 44, rightHigh);
		APTR.WriteUInt32(output, 48, rightLow);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnsignedScenario(uint before, APTR input, uint middle, uint after)
	{
		var leftHigh = APTR.ReadUInt32(input, 0);
		var leftLow = APTR.ReadUInt32(input, 4);
		var rightHigh = APTR.ReadUInt32(input, 8);
		var rightLow = APTR.ReadUInt32(input, 12);
		var left = unchecked((ulong)M68kRuntime.CombineInt64(leftHigh, leftLow));
		var right = unchecked((ulong)M68kRuntime.CombineInt64(rightHigh, rightLow));
		var output = APTR.FromPointer(OutputAddress);
		APTR.WriteUInt32(output, 0, UnsignedEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 4, UnsignedNotEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 8, UnsignedLess(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 12, UnsignedLessOrEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 16, UnsignedGreater(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 20, UnsignedGreaterOrEqual(left, right) ? 1u : 0);
		APTR.WriteUInt32(output, 24, before);
		APTR.WriteUInt32(output, 28, middle);
		APTR.WriteUInt32(output, 32, after);
		APTR.WriteUInt32(output, 36, leftHigh);
		APTR.WriteUInt32(output, 40, leftLow);
		APTR.WriteUInt32(output, 44, rightHigh);
		APTR.WriteUInt32(output, 48, rightLow);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TimeSpanScenario(uint before, APTR input, uint middle, uint after)
	{
		var ticks = M68kRuntime.CombineInt64(APTR.ReadUInt32(input, 0), APTR.ReadUInt32(input, 4));
		var constructed = new TimeSpan(ticks);
		var fromTicks = TimeSpan.FromTicks(ticks);
		var constructedLow = M68kRuntime.SplitInt64(constructed.Ticks, out var constructedHigh);
		var fromTicksLow = M68kRuntime.SplitInt64(fromTicks.Ticks, out var fromTicksHigh);
		var output = APTR.FromPointer(OutputAddress);
		APTR.WriteUInt32(output, 0, constructedHigh);
		APTR.WriteUInt32(output, 4, constructedLow);
		APTR.WriteUInt32(output, 8, fromTicksHigh);
		APTR.WriteUInt32(output, 12, fromTicksLow);
		APTR.WriteUInt32(output, 16, before);
		APTR.WriteUInt32(output, 20, middle);
		APTR.WriteUInt32(output, 24, after);
		return 42;
	}

	// Returning Boolean values keeps these as materialized Compare operations.
	// NoInlining also makes both the register and stack long-argument ABI real.
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool SignedEqual(long left, long right) => left == right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool SignedNotEqual(long left, long right) => left != right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool SignedLess(long left, long right) => left < right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool SignedLessOrEqual(long left, long right) => left <= right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool SignedGreater(long left, long right) => left > right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool SignedGreaterOrEqual(long left, long right) => left >= right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool UnsignedEqual(ulong left, ulong right) => left == right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool UnsignedNotEqual(ulong left, ulong right) => left != right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool UnsignedLess(ulong left, ulong right) => left < right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool UnsignedLessOrEqual(ulong left, ulong right) => left <= right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool UnsignedGreater(ulong left, ulong right) => left > right;
	[MethodImpl(MethodImplOptions.NoInlining)] public static bool UnsignedGreaterOrEqual(ulong left, ulong right) => left >= right;
}
