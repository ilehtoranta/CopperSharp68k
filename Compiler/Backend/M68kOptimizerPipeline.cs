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
	private readonly IReadOnlyList<IM68kOptimizerPass> _passes;

	public M68kOptimizerPipeline(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer,
		M68kCpuTarget cpu,
		M68kClrPolicy clrPolicy)
	{
		_passes = new IM68kOptimizerPass[]
		{
			new M68kConditionCodeOptimizer(assembler, buffer),
			new M68kPeepholeOptimizer(assembler, buffer, cpu, clrPolicy)
		};
	}

	public void Run()
	{
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
