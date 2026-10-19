/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDominatorAnalysisTests
{
	[Theory]
	[InlineData(1, false)]
	[InlineData(1, true)]
	[InlineData(2, false)]
	[InlineData(2, true)]
	[InlineData(3, false)]
	[InlineData(3, true)]
	public void DominatorsMatchRemovalReachabilityAcrossCyclesAndExceptionRoots(int rootCount, bool reverseOrder)
	{
		for (var seed = 0; seed < 32; seed++)
		{
			var random = new Random(seed);
			var function = new M68kMachineFunction("dominator-oracle", 13);
			var blocks = Enumerable.Range(0, 12 + seed % 17).Select(index => new M68kMachineBlock(13 + index * 7, index)).ToArray();
			function.Blocks.AddRange(reverseOrder ? blocks.Reverse() : blocks);
			for (var root = 1; root < rootCount; root++) blocks[root * 4].IsExceptionEntry = true;
			for (var index = 1; index < blocks.Length; index++) Connect(blocks[index - 1], blocks[index], M68kMachineEdgeKind.Normal);
			for (var index = 0; index < blocks.Length * 3; index++)
				Connect(blocks[random.Next(blocks.Length)], blocks[random.Next(blocks.Length)], (M68kMachineEdgeKind)random.Next(4));
			var roots = blocks.Where(block => block.Id == function.EntryBlockId || block.IsExceptionEntry).Select(block => block.Id).ToArray();
			var edges = blocks.ToDictionary(block => block.Id, block => block.ControlFlowSuccessors.ToArray());
			var actual = M68kControlFlowAnalysis.ComputeDominators(function);
			foreach (var target in blocks)
			{
				var expected = new HashSet<int>();
				foreach (var removed in blocks)
				{
					// A node dominates the target exactly when every root-to-target
					// path requires it. Removal tests this without set intersections.
					var visited = new HashSet<int>();
					var pending = new Stack<int>(roots.Where(root => root != removed.Id));
					while (pending.TryPop(out var id))
					{
						if (id == removed.Id || !visited.Add(id)) continue;
						foreach (var next in edges[id]) pending.Push(next);
					}
					if (!visited.Contains(target.Id)) expected.Add(removed.Id);
				}
				Assert.True(expected.SetEquals(actual[target.Id]), $"Seed {seed}, target {target.Id}, roots {rootCount}, reverse {reverseOrder}.");
			}
			Assert.Equal(blocks.Length, actual.Values.Distinct(ReferenceEqualityComparer.Instance).Count());
		}
	}

	[Fact]
	public void UnreachableBlocksRetainTheirDiagnostic()
	{
		var function = new M68kMachineFunction("unreachable-dominator", 13);
		function.Blocks.Add(new M68kMachineBlock(13, 0));
		function.Blocks.Add(new M68kMachineBlock(44, 1));
		var exception = Assert.Throws<InvalidOperationException>(() => M68kControlFlowAnalysis.ComputeDominators(function));
		Assert.Contains("unreachable-dominator", exception.Message);
		Assert.Contains("unreachable blocks 44", exception.Message);
	}

	private static void Connect(M68kMachineBlock source, M68kMachineBlock target, M68kMachineEdgeKind kind)
	{
		if (kind == M68kMachineEdgeKind.Normal)
		{
			if (!source.Successors.Contains(target.Id)) source.Successors.Add(target.Id);
			if (!target.Predecessors.Contains(source.Id)) target.Predecessors.Add(source.Id);
		}
		else
		{
			var edge = new M68kMachineEdge(source.Id, target.Id, kind);
			source.SuccessorEdges.Add(edge);
			target.PredecessorEdges.Add(edge);
		}
	}
}
