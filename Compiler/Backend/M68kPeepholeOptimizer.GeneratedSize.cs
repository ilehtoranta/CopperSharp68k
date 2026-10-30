/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
namespace CopperSharp.Compiler.Backend;

internal sealed partial class M68kPeepholeOptimizer
{
	// Labels move when bytes are removed, so rebuild once per analysis round.
	// Prefix maxima preserve the original Any() semantics even for overlapping ranges.
	private (int Start, int MaxEnd)[]? _generatedSizeIntervals;

	private bool GeneratedSizeEligible(int start, int end)
	{
		if (_cpu != M68kCpuTarget.M68000) return false;
		if (_generatedSizeIntervals is null)
		{
			var intervals = new List<(int Start, int MaxEnd)>();
			foreach (var range in _assembler.GeneratedCodeSizeRanges)
				if (_buffer.Labels.TryGetValue(range.StartLabel, out var first) &&
					_buffer.Labels.TryGetValue(range.EndLabel, out var last))
					intervals.Add((first, last));
			intervals.Sort(static (a, b) => a.Start.CompareTo(b.Start));
			var maxEnd = int.MinValue;
			for (var i = 0; i < intervals.Count; i++)
			{
				maxEnd = Math.Max(maxEnd, intervals[i].MaxEnd);
				intervals[i] = (intervals[i].Start, maxEnd);
			}
			_generatedSizeIntervals = intervals.ToArray();
		}
		var low = 0;
		var high = _generatedSizeIntervals.Length;
		while (low < high)
		{
			var middle = low + (high - low) / 2;
			if (_generatedSizeIntervals[middle].Start <= start) low = middle + 1;
			else high = middle;
		}
		return low > 0 && end <= _generatedSizeIntervals[low - 1].MaxEnd;
	}

}
