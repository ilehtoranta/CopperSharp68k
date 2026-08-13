/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal interface IM68kOptimizerPass
{
	bool Changed { get; }

	void Run();
}

internal sealed class M68kOptimizerPipeline
{
	private const int BoundedPeepholeRewriteBudget = 8;
	private readonly IReadOnlyList<IM68kOptimizerPass> _passes;
	private readonly M68kPeepholeOptimizationMode _peepholeOptimization;

	public M68kOptimizerPipeline(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer,
		M68kCpuTarget cpu,
		M68kClrPolicy clrPolicy,
		IReadOnlyList<M68kLoopLayout>? sizeFirstLoops = null,
		M68kPeepholeOptimizationMode peepholeOptimization =
			M68kPeepholeOptimizationMode.FixedPoint)
	{
		_peepholeOptimization = peepholeOptimization;
		_passes = new IM68kOptimizerPass[]
		{
			new M68kConditionCodeOptimizer(assembler, buffer),
			new M68kPeepholeOptimizer(
				assembler,
				buffer,
				cpu,
				clrPolicy,
				sizeFirstLoops ?? Array.Empty<M68kLoopLayout>(),
				peepholeOptimization == M68kPeepholeOptimizationMode.Bounded
					? BoundedPeepholeRewriteBudget
					: int.MaxValue)
		};
	}

	public void Run()
	{
		if (_peepholeOptimization == M68kPeepholeOptimizationMode.Bounded)
		{
			foreach (var pass in _passes)
			{
				pass.Run();
			}
			return;
		}

		bool changed;
		do
		{
			foreach (var pass in _passes)
			{
				pass.Run();
			}
			changed = _passes.Any(static pass => pass.Changed);
		}
		while (changed);
	}
}
