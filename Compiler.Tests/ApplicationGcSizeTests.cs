/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	public void ApplicationGcCompactLookupPreservesSignedBoundariesAndFirstDuplicate(M68kPeepholeOptimizationMode mode)
	{
		var result = M68kCompiler.Compile(CompactGcRequest(
			typeof(CompilerFixtures).FullName + "::CollectorFrameAnchorRetainsCallerArrayEntry") with {
			PeepholeOptimization = mode
		});
		Assert.Contains("pass=CompactApplicationGcSites", result.Map);
		var lookup = result.Symbols.Single(symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::FindRootSiteCompact").Address;
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
		{
			var bus = LoadApplicationSizeHunk(result.Image, load, 0x40000);
			var emittedTable = load + result.Symbols.Single(symbol => symbol.Name == "__c68k_method_table").Address;
			var count = bus.ReadLong(emittedTable);
			Assert.True(count > 1);
			for (uint index = 1; index < count; index++)
				Assert.True((short)bus.ReadWord(emittedTable + 4 + (index - 1) * 12) <=
					(short)bus.ReadWord(emittedTable + 4 + index * 12));
			const uint table = 0x70000;
			foreach (uint stride in new uint[] { 10, 12 })
			foreach (var keys in new short[][] { [], [short.MinValue], [short.MinValue, -100, -100, -1, 0, 1, short.MaxValue] })
			{
				bus.WriteLong(table, (uint)keys.Length);
				for (var index = 0; index < keys.Length; index++)
					bus.WriteWord(table + 4 + (uint)index * stride, unchecked((ushort)keys[index]));
				foreach (int key in new[] { -32769, -32768, -32767, -101, -100, -99, -1, 0, 1, 2, 32766, 32767, 32768 })
				foreach (int alias in new[] { -65536, 0, 65536 })
				{
					var index = Array.FindIndex(keys, value => value == key + alias);
					var expected = index < 0 ? 0u : table + 4 + (uint)index * stride;
					Assert.Equal(expected, Execute(bus, model, load + lookup, maxInstructions: 10_000, initialize: state => {
						state.D[0] = table;
						state.D[1] = unchecked(table + (uint)(key + alias));
						state.A[0] = stride;
					}));
				}
			}
		}
	}

	[Fact]
	public void ApplicationGcCompactLargeDisplacementsRetryWithLegacyReaders()
	{
		var request = CompactGcRequest(typeof(ApplicationMetadataFixtures).FullName + "::LargeTable");
		var wide = M68kCompiler.Compile(request with {
			CodeSizeOptimizations = new() { CompactApplicationGcSites = false }
		});
		var automatic = M68kCompiler.Compile(request);
		Assert.True(wide.Code.Length > short.MaxValue);
		Assert.Equal(wide.Image, automatic.Image);
		Assert.DoesNotContain("pass=CompactApplicationGcSites", automatic.Map);
		Assert.DoesNotContain(automatic.Symbols, symbol => symbol.Name.EndsWith("Compact", StringComparison.Ordinal));
		foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
		foreach (uint load in new uint[] { 0x10000, 0x20000 })
			Assert.Equal(42u, Execute(LoadApplicationSizeHunk(automatic.Image, load, 0x40000),
				model, load + automatic.EntryPoint, maxInstructions: 5_000_000));
	}

	[Theory]
	[InlineData("FailureEntry", 42u)]
	[InlineData("OnceOnlyEntry", 83u)]
	[InlineData("RecursiveEntry", 42u)]
	public void ApplicationGcCompactRetainsTypeInitializerCleanup(string entry, uint expected)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		foreach (var compact in new[] { false, true })
		{
			var result = M68kCompiler.Compile(CompactGcRequest(
				typeof(TypeInitializationRuntimeFixtures).FullName + "::" + entry) with {
				PeepholeOptimization = mode,
				CodeSizeOptimizations = new() { CompactApplicationGcSites = compact }
			});
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
				Assert.Equal(expected, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000),
					model, load + result.EntryPoint, maxInstructions: 5_000_000));
		}
	}

	private static M68kCompilationRequest CompactGcRequest(string entry) => new() {
		AssemblyPath = FixtureAssembly, EntryPoint = entry,
		Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
		RuntimeProfile = M68kRuntimeProfile.Application, ExceptionMode = M68kExceptionMode.Full,
		MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
		GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
		Heap = new() { StartAddress = 0x60000, Size = 0x8000 },
		IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }, CodeSizeOptimizations = new()
	};

	[Theory]
	[InlineData("PoolCollectTracesCallerFrameEntry")]
	[InlineData("CollectorFrameAnchorRetainsCallerArrayEntry")]
	public void ApplicationGcCompactRetainsRootsWithoutManagedExceptions(string entry)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = M68kCompiler.Compile(CompactGcRequest(typeof(CompilerFixtures).FullName + "::" + entry) with {
				ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = mode
			});
			Assert.Contains("pass=CompactApplicationGcSites", result.Map);
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
				Assert.Equal(42u, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000),
					model, load + result.EntryPoint, maxInstructions: 5_000_000));
		}
	}

	[Theory]
	[InlineData("FinalizesUnreachableEntry", 1u)]
	[InlineData("PreservesFinalizerGraphEntry", 42u)]
	[InlineData("NestedCollectionEntry", 2u)]
	[InlineData("ThrowingFinalizerDoesNotStopDrainEntry", 2u)]
	public void ApplicationGcCompactPreservesFinalizerDrain(string entry, uint expected)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		foreach (var compact in new[] { false, true })
		{
			var result = M68kCompiler.Compile(CompactGcRequest(typeof(FinalizerExecutionFixtures).FullName + "::" + entry) with {
				PeepholeOptimization = mode,
				CodeSizeOptimizations = new() { CompactApplicationGcSites = compact }
			});
			foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
			foreach (uint load in new uint[] { 0x10000, 0x20000 })
				Assert.Equal(expected, Execute(LoadApplicationSizeHunk(result.Image, load, 0x40000),
					model, load + result.EntryPoint, maxInstructions: 5_000_000));
		}
	}
}
