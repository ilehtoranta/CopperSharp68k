/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	private bool GeneratedSizeEligible(int start, int end) => _cpu == M68kCpuTarget.M68000 &&
		_assembler.GeneratedCodeSizeRanges.Any(range =>
			_buffer.Labels.TryGetValue(range.StartLabel, out var first) &&
			_buffer.Labels.TryGetValue(range.EndLabel, out var last) && start >= first && end <= last);

}
