/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text.Json;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Fact]
	public void StringBuilderDefaultNegativeSignsMatchCoreLib() => Assert.Equal(42, CompilerFixtures.StringBuilderDefaultNegativeSignContractsEntry());

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderDefaultNegativeSignsRetainCultureAndIntegerExtrema(M68kCpuTarget target, M68kCpuModel model) =>
		RunStringBuilderValueFormatting(target, model, "StringBuilderDefaultNegativeSignContractsEntry");

	private static readonly string[] StringBuilderBenchmarkEntries = [
		"StringBuilderBenchmarkBaselineEntry", "StringBuilderBenchmarkPresizedTextEntry",
		"StringBuilderBenchmarkGrowingTextEntry", "StringBuilderBenchmarkIntegerHandlerEntry",
		"StringBuilderBenchmarkIntegerFormatEntry"
	];

	[Fact]
	public void StringBuilderBenchmarksMatchCoreLib()
	{
		foreach (var entry in StringBuilderBenchmarkEntries)
			Assert.Equal(42, (int)typeof(CompilerFixtures).GetMethod(entry)!.Invoke(null, null)!);
	}

	// Cold-start measurements with the real managed pool and on-failure GC.
	// Initial regression ceilings allow 15% growth, rounded to KiB/100 cycles;
	// allocation counts remain exact. These are not application-specific budgets.
	private readonly record struct StringBuilderCostBudget(int ImageBytes, int LoadedBytes, int Allocations,
		long Mc68000Cycles, long Mc68020Cycles, long Mc68040Cycles);

	private static StringBuilderCostBudget StringBuilderBudget(string entry, M68kPeepholeOptimizationMode mode) => (entry, mode) switch {
		("StringBuilderBenchmarkBaselineEntry", M68kPeepholeOptimizationMode.FixedPoint) => new(5_120, 3_072, 0, 800, 200, 100),
		("StringBuilderBenchmarkBaselineEntry", M68kPeepholeOptimizationMode.Disabled) => new(5_120, 3_072, 0, 900, 200, 100),
		("StringBuilderBenchmarkPresizedTextEntry", M68kPeepholeOptimizationMode.FixedPoint) => new(47_104, 35_840, 3, 25_000, 5_600, 2_600),
		("StringBuilderBenchmarkPresizedTextEntry", M68kPeepholeOptimizationMode.Disabled) => new(49_152, 36_864, 3, 27_200, 6_000, 2_700),
		("StringBuilderBenchmarkGrowingTextEntry", M68kPeepholeOptimizationMode.FixedPoint) => new(47_104, 35_840, 11, 53_900, 12_000, 5_000),
		("StringBuilderBenchmarkGrowingTextEntry", M68kPeepholeOptimizationMode.Disabled) => new(49_152, 36_864, 11, 58_300, 12_700, 5_400),
		("StringBuilderBenchmarkIntegerHandlerEntry", M68kPeepholeOptimizationMode.FixedPoint) => new(190_464, 151_552, 36, 152_400, 31_300, 12_600),
		("StringBuilderBenchmarkIntegerHandlerEntry", M68kPeepholeOptimizationMode.Disabled) => new(203_776, 163_840, 36, 164_900, 33_400, 13_600),
		("StringBuilderBenchmarkIntegerFormatEntry", M68kPeepholeOptimizationMode.FixedPoint) => new(215_040, 167_936, 12, 168_100, 34_200, 12_600),
		("StringBuilderBenchmarkIntegerFormatEntry", M68kPeepholeOptimizationMode.Disabled) => new(228_352, 182_272, 12, 180_900, 36_200, 13_600),
		_ => throw new ArgumentOutOfRangeException(nameof(entry))
	};

	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void StringBuilderRepresentativeCostsAndTrimming(M68kCpuTarget target, M68kCpuModel model)
	{
		using var pack = FrameworkImplementationPackTests.CoreLibPack.CreatePinned();
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		foreach (var entry in StringBuilderBenchmarkEntries)
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly, EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::" + entry,
				ManagedAssemblyPaths = [typeof(CopperSharp.Runtime.AmigaPal.EnvironmentPal).Assembly.Location],
				IncludedExportNames = [], Cpu = target, OutputFormat = M68kOutputFormat.Hunk,
				ExceptionMode = M68kExceptionMode.Full, PeepholeOptimization = mode,
				MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.OnAllocationFailure,
				Heap = new M68kHeapOptions { StartAddress = 0x0010_0000, Size = 0x8000 },
				FrameworkImplementationPack = new M68kFrameworkImplementationPackOptions(pack.ManifestPath)
			});
			Assert.True(HunkLoadAddress + result.Code.Length < 0x0010_0000);
			Assert.Contains("sha256=" + pack.Sha256, result.Map, StringComparison.Ordinal);
			Assert.DoesNotContain(result.Symbols, symbol =>
				symbol.Name.StartsWith("CopperSharp.Runtime.ShadowDecimal", StringComparison.Ordinal) ||
				symbol.Name.StartsWith("CopperSharp.Runtime.ShadowFloating", StringComparison.Ordinal) ||
				symbol.Name.StartsWith("System.Number::Dragon4", StringComparison.Ordinal) ||
				symbol.Name.StartsWith("System.Number::Grisu3", StringComparison.Ordinal) ||
				symbol.Name.StartsWith("System.Number+BigInteger::", StringComparison.Ordinal));
			if (entry.EndsWith("BaselineEntry", StringComparison.Ordinal))
				Assert.DoesNotContain(result.Symbols, symbol => symbol.Name.StartsWith("System.Text.StringBuilder::", StringComparison.Ordinal));
			if (entry is "StringBuilderBenchmarkPresizedTextEntry" or "StringBuilderBenchmarkGrowingTextEntry")
				Assert.DoesNotContain(result.Symbols, symbol =>
					symbol.Name.StartsWith("CopperSharp.Runtime.ShadowGroupedIntegerFormatting::", StringComparison.Ordinal) ||
					symbol.Name.StartsWith("CopperSharp.Runtime.ShadowScientificIntegerFormatting::", StringComparison.Ordinal) ||
					symbol.Name == "CopperSharp.Runtime.ShadowStandardIntegerFormatting::Format" ||
					symbol.Name == "CopperSharp.Runtime.ShadowStandardIntegerFormatting::TryFormat");
			var allocate = result.Symbols.Where(symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::Allocate")
				.Select(symbol => (uint?)(HunkLoadAddress + symbol.Address)).SingleOrDefault();
			var allocations = 0;
			long cycles = 0;
			var actual = Execute(CreateHunkBus(result), model, HunkLoadAddress + result.EntryPoint,
				beforeInstruction: (cpu, _) => { if (allocate == cpu.State.ProgramCounter) allocations++; },
				afterReturn: state => cycles = state.Cycles, initialStackPointer: 0x0020_0000, maxInstructions: 20_000_000);
			Assert.True(actual == 42, $"{entry} returned {actual}; {target}, {mode}.");
			var budget = StringBuilderBudget(entry, mode);
			var cycleBudget = target == M68kCpuTarget.M68000 ? budget.Mc68000Cycles : target == M68kCpuTarget.M68020 ? budget.Mc68020Cycles : budget.Mc68040Cycles;
			Assert.True(result.Image.Length <= budget.ImageBytes, $"{entry} image grew to {result.Image.Length} bytes; budget {budget.ImageBytes}.");
			Assert.True(result.Code.Length <= budget.LoadedBytes, $"{entry} loaded image grew to {result.Code.Length} bytes; budget {budget.LoadedBytes}.");
			Assert.True(cycles <= cycleBudget, $"{entry} grew to {cycles} cycles; budget {cycleBudget}; {target}, {mode}.");
			Assert.Equal(budget.Allocations, allocations);
			var artifact = Path.Combine(AppContext.BaseDirectory, $"stringbuilder-benchmark-{entry}-{target}-{mode}");
			File.WriteAllText(artifact + ".map", result.Map);
			File.WriteAllText(artifact + ".json", JsonSerializer.Serialize(new {
				Entry = entry, Cpu = target.ToString(), Emulator = model.ToString(), Optimization = mode.ToString(),
				ImageBytes = result.Image.Length, LoadedBytes = result.Code.Length, Symbols = result.Symbols.Count,
				Cycles = cycles, Allocations = allocations, Result = actual,
				Collection = "OnAllocationFailure", HeapBytes = 0x8000,
				CoreLibSha256 = pack.Sha256, ImplementationPackVersion = "10.0.9",
				FixtureAssemblySha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(FixtureAssembly)))
			}, new JsonSerializerOptions { WriteIndented = true }));
		}
	}
}
