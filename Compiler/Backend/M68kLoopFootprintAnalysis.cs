/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal sealed record M68kNaturalLoop(
	int HeaderBlockId,
	IReadOnlySet<int> BlockIds);

internal sealed record M68kLoopBlockLayout(
	string StartLabel,
	string EndLabel);

internal sealed record M68kLoopLayout(
	string Method,
	int HeaderIlOffset,
	string HeaderLabel,
	IReadOnlyList<M68kLoopBlockLayout> Blocks);

internal static class M68kLoopFootprintAnalysis
{
	private const int InstructionCacheBytes = 256;
	private const int InstructionCacheLineBytes = 4;
	private const int InstructionCacheLines =
		InstructionCacheBytes / InstructionCacheLineBytes;

	internal static IReadOnlyList<M68kNaturalLoop> Discover(
		M68kMachineFunction function)
	{
		var blocks = function.Blocks.ToDictionary(static block => block.Id);
		var dominators = M68kControlFlowAnalysis.ComputeDominators(function);
		var loopsByHeader = new Dictionary<int, HashSet<int>>();
		foreach (var source in function.Blocks)
		{
			foreach (var headerId in source.Successors)
			{
				if (!dominators[source.Id].Contains(headerId))
				{
					continue;
				}

				var naturalLoop = new HashSet<int> { headerId, source.Id };
				var work = new Stack<int>();
				if (source.Id != headerId)
				{
					work.Push(source.Id);
				}
				while (work.TryPop(out var blockId))
				{
					foreach (var predecessor in blocks[blockId].Predecessors)
					{
						if (naturalLoop.Add(predecessor))
						{
							work.Push(predecessor);
						}
					}
				}

				if (!loopsByHeader.TryGetValue(headerId, out var combinedLoop))
				{
					combinedLoop = new HashSet<int>();
					loopsByHeader.Add(headerId, combinedLoop);
				}
				combinedLoop.UnionWith(naturalLoop);
			}
		}

		return loopsByHeader
			.OrderBy(static item => item.Key)
			.Select(static item => new M68kNaturalLoop(item.Key, item.Value))
			.ToArray();
	}

	internal static IReadOnlyList<M68kLoopFootprint> Measure(
		IReadOnlyList<M68kLoopLayout> layouts,
		IReadOnlyDictionary<string, int> labels,
		IReadOnlyDictionary<string, int> analysisAnchors,
		uint origin)
	{
		var result = new List<M68kLoopFootprint>(layouts.Count);
		foreach (var layout in layouts)
		{
			var ranges = layout.Blocks
				.Select(block => (
					Start: labels[block.StartLabel],
					End: analysisAnchors[block.EndLabel]))
				.Where(static range => range.End > range.Start)
				.ToArray();
			if (ranges.Length == 0)
			{
				continue;
			}

			var cacheLines = new HashSet<long>();
			foreach (var range in ranges)
			{
				var absoluteStart = checked((long)origin + range.Start);
				var absoluteEnd = checked((long)origin + range.End);
				var firstLine = absoluteStart / InstructionCacheLineBytes;
				var lastLine = (absoluteEnd - 1) / InstructionCacheLineBytes;
				for (var line = firstLine; line <= lastLine; line++)
				{
					cacheLines.Add(line);
				}
			}

			var cacheIndexes = cacheLines
				.Select(static line => line % InstructionCacheLines)
				.ToHashSet();
			var spanStart = ranges.Min(static range => range.Start);
			var spanEnd = ranges.Max(static range => range.End);
			result.Add(new M68kLoopFootprint(
				layout.Method,
				layout.HeaderIlOffset,
				checked(origin + (uint)labels[layout.HeaderLabel]),
				ranges.Sum(static range => range.End - range.Start),
				spanEnd - spanStart,
				cacheLines.Count,
				cacheLines.Count <= InstructionCacheLines &&
				cacheIndexes.Count == cacheLines.Count));
		}

		return result
			.OrderBy(static loop => loop.HeaderAddress)
			.ThenBy(static loop => loop.Method, StringComparer.Ordinal)
			.ToArray();
	}
}
