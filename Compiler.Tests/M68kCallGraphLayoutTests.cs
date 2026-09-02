using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kCallGraphLayoutTests
{
	[Fact]
	public void PreservesEveryNodeExactlyOnceAndIsDeterministic()
	{
		int[] sizes = [100, 200, 300, 400, 500, 600];
		M68kCallGraphLayoutEdge[] edges =
		[
			new(0, 1, 8), new(1, 2, 7), new(3, 4, 6), new(4, 5, 5), new(2, 3, 1)
		];
		var first = M68kCallGraphLayout.Plan(sizes, edges, clusterCapacity: 900);
		var second = M68kCallGraphLayout.Plan(sizes, edges, clusterCapacity: 900);
		Assert.Equal(first, second);
		Assert.Equal(Enumerable.Range(0, sizes.Length), first.Order());
	}

	[Fact]
	public void KeepsStrongAffinityPairsAdjacentWithinBoundedClusters()
	{
		int[] sizes = [100, 100, 100, 100, 100, 100];
		M68kCallGraphLayoutEdge[] edges =
		[
			new(0, 1, 100), new(2, 3, 90), new(4, 5, 80),
			new(1, 2, 1), new(3, 4, 1)
		];
		var order = M68kCallGraphLayout.Plan(sizes, edges, clusterCapacity: 200).ToArray();
		var positions = order.Select((node, index) => (node, index))
			.ToDictionary(static item => item.node, static item => item.index);
		Assert.Equal(1, Math.Abs(positions[0] - positions[1]));
		Assert.Equal(1, Math.Abs(positions[2] - positions[3]));
		Assert.Equal(1, Math.Abs(positions[4] - positions[5]));
	}

	[Fact]
	public void RejectsInvalidInputs()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			M68kCallGraphLayout.Plan([1], [], clusterCapacity: 0));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			M68kCallGraphLayout.Plan([0], [], clusterCapacity: 1));
		Assert.Throws<ArgumentOutOfRangeException>(() =>
			M68kCallGraphLayout.Plan([1], [new(0, 2)], clusterCapacity: 1));
	}
}
