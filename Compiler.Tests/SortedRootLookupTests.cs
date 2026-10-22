/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
	[Theory]
	[MemberData(nameof(CpuTargets))]
	public void SortedRootLookupPreservesBoundariesAndFirstDuplicate(M68kCpuTarget target, M68kCpuModel model)
	{
		foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
		{
			var result = M68kCompiler.Compile(new M68kCompilationRequest {
				AssemblyPath = FixtureAssembly,
				EntryPoint = "CopperSharp.Compiler.Tests.CompilerFixtures::CollectorFrameAnchorRetainsCallerArrayEntry",
				Cpu = target, OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Full,
				PeepholeOptimization = mode, MemoryManagement = M68kMemoryManagement.ManagedPoolMarkSweepGc,
				GcSweepStrategy = M68kGcSweepStrategy.EveryAllocation,
				Heap = new M68kHeapOptions { StartAddress = 0x60000, Size = 0x8000 }
			});
			var bus = CreateHunkBus(result);
			var emittedTable = HunkLoadAddress + result.Symbols.Single(symbol => symbol.Name == "__c68k_method_table").Address;
			var count = bus.ReadLong(emittedTable);
			Assert.True(count > 1);
			for (uint index = 1; index < count; index++)
				Assert.True(bus.ReadLong(emittedTable + 4 + (index - 1) * 24) <= bus.ReadLong(emittedTable + 4 + index * 24));
			var lookup = HunkLoadAddress + result.Symbols.Single(symbol => symbol.Name == "CopperSharp.Runtime.ManagedPool::FindRootSite").Address;
			const uint table = 0x70000;
			foreach (uint stride in new uint[] { 20, 24 })
			foreach (var keys in new uint[][] { [], [0x80000000], [10, 20, 20, 30, 0x80000000, uint.MaxValue] })
			{
				bus.WriteLong(table, (uint)keys.Length);
				for (var index = 0; index < keys.Length; index++) bus.WriteLong(table + 4 + (uint)index * stride, keys[index]);
				foreach (uint key in new uint[] { 0, 9, 10, 19, 20, 21, 30, 31, 0x7fffffff, 0x80000000, 0x80000001, uint.MaxValue })
				{
					var index = Array.IndexOf(keys, key);
					var expected = index < 0 ? 0u : table + 4 + (uint)index * stride;
					var actual = Execute(bus, model, lookup, maxInstructions: 10_000, initialize: state => {
						// Internal scalar ABI: table and PC in D0/D1, third word in A0.
						state.D[0] = table; state.D[1] = key; state.A[0] = stride;
					});
					Assert.Equal(expected, actual);
				}
			}
		}
	}
}
