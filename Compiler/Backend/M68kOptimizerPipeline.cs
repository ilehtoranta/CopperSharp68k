/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal interface IM68kOptimizerPass
{
	void Run();
}

internal sealed class M68kOptimizerPipeline
{
	private readonly IReadOnlyList<IM68kOptimizerPass> _passes;

	public M68kOptimizerPipeline(
		M68kAssembler assembler,
		M68kAssemblyBuffer buffer,
		M68kCpuTarget cpu)
	{
		_passes = new[]
		{
			new M68kPeepholeOptimizer(assembler, buffer, cpu)
		};
	}

	public void Run()
	{
		foreach (var pass in _passes)
		{
			pass.Run();
		}
	}
}
