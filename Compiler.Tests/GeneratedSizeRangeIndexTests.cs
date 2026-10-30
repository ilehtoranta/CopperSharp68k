/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Reflection;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class GeneratedSizeRangeIndexTests
{
	[Fact]
	public void IndexedLookupMatchesLinearSearchForOverlapsBoundariesAndMissingLabels()
	{
		var assembler = new M68kAssembler();
		var buffer = new M68kAssemblyBuffer();
		var random = new Random(21642);
		var ranges = new List<(string StartLabel, string EndLabel)>();
		for (var i = 0; i < 100; i++)
		{
			var start = $"start{i}";
			var end = $"end{i}";
			buffer.Labels[start] = random.Next(0, 1000);
			if (i % 7 != 0) buffer.Labels[end] = random.Next(0, 1000);
			ranges.Add((start, end));
		}
		assembler.GeneratedCodeSizeRanges = ranges;
		var optimizer = new M68kPeepholeOptimizer(assembler, buffer,
			M68kCpuTarget.M68000, M68kClrPolicy.Always, []);
		var eligible = typeof(M68kPeepholeOptimizer).GetMethod("GeneratedSizeEligible",
			BindingFlags.NonPublic | BindingFlags.Instance)!;
		for (var start = 0; start <= 1000; start += 5)
		for (var end = start; end <= Math.Min(start + 25, 1000); end += 5)
		{
			var expected = ranges.Any(range =>
				buffer.Labels.TryGetValue(range.StartLabel, out var first) &&
				buffer.Labels.TryGetValue(range.EndLabel, out var last) &&
				start >= first && end <= last);
			Assert.Equal(expected, (bool)eligible.Invoke(optimizer, [start, end])!);
		}
	}
}
