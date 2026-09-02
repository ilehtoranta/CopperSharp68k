/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

namespace CopperSharp.Compiler.Backend;

internal readonly record struct M68kCallGraphLayoutEdge(
	int Source,
	int Target,
	int Weight = 1);

internal static class M68kCallGraphLayout
{
	public static IReadOnlyList<int> Plan(
		IReadOnlyList<int> estimatedSizes,
		IEnumerable<M68kCallGraphLayoutEdge> edges,
		int clusterCapacity)
	{
		ArgumentNullException.ThrowIfNull(estimatedSizes);
		ArgumentNullException.ThrowIfNull(edges);
		if (clusterCapacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(clusterCapacity));
		if (estimatedSizes.Any(static size => size <= 0))
			throw new ArgumentOutOfRangeException(nameof(estimatedSizes));

		var graph = Enumerable.Range(0, estimatedSizes.Count)
			.Select(static _ => new Dictionary<int, int>())
			.ToArray();
		foreach (var edge in edges)
		{
			if (edge.Source < 0 || edge.Source >= graph.Length ||
				edge.Target < 0 || edge.Target >= graph.Length)
				throw new ArgumentOutOfRangeException(nameof(edges));
			if (edge.Weight <= 0)
				throw new ArgumentOutOfRangeException(nameof(edges));
			if (edge.Source == edge.Target) continue;
			AddWeight(graph[edge.Source], edge.Target, edge.Weight);
			AddWeight(graph[edge.Target], edge.Source, edge.Weight);
		}
		if (graph.Length < 2) return Enumerable.Range(0, graph.Length).ToArray();

		var degrees = graph.Select(static adjacent => adjacent.Values.Sum()).ToArray();
		var unassigned = Enumerable.Range(0, graph.Length).ToHashSet();
		var clusters = new List<List<int>>();
		while (unassigned.Count != 0)
		{
			var seed = unassigned
				.OrderByDescending(node => degrees[node])
				.ThenBy(static node => node)
				.First();
			var cluster = new List<int> { seed };
			clusters.Add(cluster);
			unassigned.Remove(seed);
			var used = estimatedSizes[seed];
			var affinity = new Dictionary<int, int>();
			AccumulateAffinity(seed);

			while (true)
			{
				var candidate = unassigned
					.Where(node =>
						used + estimatedSizes[node] <= clusterCapacity &&
						affinity.GetValueOrDefault(node) > 0)
					.OrderByDescending(node => affinity.GetValueOrDefault(node))
					.ThenByDescending(node => degrees[node])
					.ThenBy(node => estimatedSizes[node])
					.ThenBy(static node => node)
					.Cast<int?>()
					.FirstOrDefault();
				if (candidate is null) break;
				var node = candidate.Value;
				cluster.Add(node);
				unassigned.Remove(node);
				used = checked(used + estimatedSizes[node]);
				AccumulateAffinity(node);
			}

			void AccumulateAffinity(int node)
			{
				foreach (var (neighbor, weight) in graph[node])
				{
					if (unassigned.Contains(neighbor))
						affinity[neighbor] = checked(
							affinity.GetValueOrDefault(neighbor) + weight);
				}
			}
		}

		var clusterOf = new int[graph.Length];
		for (var index = 0; index < clusters.Count; index++)
		{
			foreach (var node in clusters[index]) clusterOf[node] = index;
		}
		var clusterEdges = new Dictionary<(int Left, int Right), int>();
		for (var source = 0; source < graph.Length; source++)
		{
			foreach (var (target, weight) in graph[source])
			{
				var left = clusterOf[source];
				var right = clusterOf[target];
				if (left >= right) continue;
				clusterEdges[(left, right)] = checked(
					clusterEdges.GetValueOrDefault((left, right)) + weight);
			}
		}
		var clusterOrder = ChainOrder(
			Enumerable.Range(0, clusters.Count),
			clusterEdges.Select(static item =>
				new M68kCallGraphLayoutEdge(item.Key.Left, item.Key.Right, item.Value)));
		var result = new List<int>(graph.Length);
		foreach (var clusterIndex in clusterOrder)
		{
			var members = clusters[clusterIndex];
			var memberSet = members.ToHashSet();
			var memberEdges = new List<M68kCallGraphLayoutEdge>();
			foreach (var source in members)
			{
				foreach (var (target, weight) in graph[source])
				{
					if (source < target && memberSet.Contains(target))
						memberEdges.Add(new(source, target, weight));
				}
			}
			result.AddRange(ChainOrder(members, memberEdges));
		}
		if (result.Count != graph.Length || result.Distinct().Count() != graph.Length)
			throw new InvalidOperationException(
				"Call-graph layout did not preserve every method exactly once.");
		return result;
	}

	private static IReadOnlyList<int> ChainOrder(
		IEnumerable<int> nodes,
		IEnumerable<M68kCallGraphLayoutEdge> edges)
	{
		var nodeArray = nodes.Distinct().Order().ToArray();
		var chains = nodeArray.ToDictionary(
			static node => node,
			static node => new List<int> { node });
		var owner = nodeArray.ToDictionary(static node => node, static node => node);
		foreach (var edge in edges
			.OrderByDescending(static edge => edge.Weight)
			.ThenBy(static edge => Math.Min(edge.Source, edge.Target))
			.ThenBy(static edge => Math.Max(edge.Source, edge.Target)))
		{
			if (!owner.TryGetValue(edge.Source, out var leftOwner) ||
				!owner.TryGetValue(edge.Target, out var rightOwner) ||
				leftOwner == rightOwner)
				continue;
			var left = chains[leftOwner];
			var right = chains[rightOwner];
			if (edge.Source != left[0] && edge.Source != left[^1] ||
				edge.Target != right[0] && edge.Target != right[^1])
				continue;
			if (left[^1] != edge.Source) left.Reverse();
			if (right[0] != edge.Target) right.Reverse();
			left.AddRange(right);
			chains.Remove(rightOwner);
			foreach (var node in right) owner[node] = leftOwner;
		}
		return chains.Values
			.OrderByDescending(static chain => chain.Count)
			.ThenBy(static chain => chain.Min())
			.SelectMany(static chain => chain)
			.ToArray();
	}

	private static void AddWeight(
		IDictionary<int, int> graph,
		int target,
		int weight) =>
		graph[target] = checked((graph.TryGetValue(target, out var current) ? current : 0) + weight);
}
