/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kOptimizationProfilerTests
{
	[Fact]
	public void NestedMeasurementsSeparateInclusiveAndExclusiveTime()
	{
		using var session = M68kOptimizationProfiler.Start();
		using (M68kOptimizationProfiler.Measure("outer"))
		{
			using (M68kOptimizationProfiler.Measure("inner"))
			{
				M68kOptimizationProfiler.Count("visits", 7);
			}
		}
		var snapshot = session.GetSnapshot();
		var outer = Assert.Single(snapshot.Measurements, item => item.Name == "outer");
		var inner = Assert.Single(snapshot.Measurements, item => item.Name == "inner");
		Assert.Equal(1, outer.Calls);
		Assert.Equal(1, inner.Calls);
		Assert.Equal(outer.InclusiveSeconds, outer.ExclusiveSeconds + inner.InclusiveSeconds, 10);
		Assert.Equal(inner.InclusiveSeconds, inner.ExclusiveSeconds);
		Assert.Equal(7, snapshot.Counters["visits"]);
	}

	[Fact]
	public void ProfilingPreservesOptimizedBytesAndDoesNotLeakToAnotherThread()
	{
		static M68kAssembler Create()
		{
			var assembler = new M68kAssembler();
			assembler.EmitWord(0x207C); // MOVEA.L #0,A0
			assembler.EmitLong(0);
			assembler.EmitWord(0x4E75); // RTS
			return assembler;
		}
		var ordinary = Create();
		ordinary.OptimizeForM68000();
		var profiled = Create();
		using (var session = M68kOptimizationProfiler.Start())
		{
			var other = new Thread(() => M68kOptimizationProfiler.Count("other-thread"));
			other.Start();
			other.Join();
			profiled.OptimizeForM68000();
			var snapshot = session.GetSnapshot();
			Assert.DoesNotContain("other-thread", snapshot.Counters.Keys);
			Assert.Contains(snapshot.Measurements, item => item.Name == "dataflow.total");
			Assert.True(snapshot.Counters["dataflow.instructions"] > 0);
		}
		Assert.Equal(ordinary.GetInstructionStream(), profiled.GetInstructionStream());
		// A fresh session must contain neither the preceding profile nor work
		// performed while profiling is disabled.
		M68kOptimizationProfiler.Count("disabled");
		using var next = M68kOptimizationProfiler.Start();
		Assert.Empty(next.GetSnapshot().Measurements);
		Assert.Empty(next.GetSnapshot().Counters);
	}
}
