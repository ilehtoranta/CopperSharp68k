/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Collections;
using System.Numerics;

namespace CopperSharp.Compiler.Backend;

internal static class M68kNonNullFactsAnalysis
{
	// All facts name SSA values from one immutable function. Dense indices
	// keep top-state copies and predecessor intersections proportional to
	// machine words rather than hash-table entries. No bit storage escapes.
	private sealed class Facts(int[] values, Dictionary<int, int> indices, bool full = false) : ISet<int>
	{
		private readonly ulong[] _bits = CreateBits(values.Length, full);
		public int Count { get; private set; } = full ? values.Length : 0;
		public bool IsReadOnly => false;
		private static ulong[] CreateBits(int count, bool full)
		{
			var bits = new ulong[(count + 63) / 64];
			if (full && bits.Length != 0) { bits.AsSpan().Fill(ulong.MaxValue); bits[^1] >>= bits.Length * 64 - count; }
			return bits;
		}
		internal void CopyFrom(Facts other) { other._bits.CopyTo(_bits, 0); Count = other.Count; }
		internal bool SetEquals(Facts other) => _bits.AsSpan().SequenceEqual(other._bits);
		internal void IntersectWith(Facts other)
		{
			if (Count == 0 || other.Count == values.Length) return;
			if (other.Count == 0) { Clear(); return; }
			if (Count == values.Length) { CopyFrom(other); return; }
			var count = 0;
			for (var word = 0; word < _bits.Length; word++)
			{
				_bits[word] &= other._bits[word];
				count += BitOperations.PopCount(_bits[word]);
			}
			Count = count;
		}
		public bool Add(int value)
		{
			var index = indices[value];
			var mask = 1UL << (index & 63);
			if ((_bits[index / 64] & mask) != 0) return false;
			_bits[index / 64] |= mask; Count++; return true;
		}
		void ICollection<int>.Add(int value) => Add(value);
		public bool Contains(int value) => indices.TryGetValue(value, out var index) && (_bits[index / 64] & (1UL << (index & 63))) != 0;
		public bool Remove(int value)
		{
			if (!indices.TryGetValue(value, out var index)) return false;
			var mask = 1UL << (index & 63);
			if ((_bits[index / 64] & mask) == 0) return false;
			_bits[index / 64] &= ~mask; Count--; return true;
		}
		public void Clear() { _bits.AsSpan().Clear(); Count = 0; }
		public void UnionWith(IEnumerable<int> other) { foreach (var value in other) Add(value); }
		public void IntersectWith(IEnumerable<int> other)
		{
			if (other is Facts facts && ReferenceEquals(indices, facts.Indices)) { IntersectWith(facts); return; }
			var retained = other.ToHashSet();
			foreach (var value in this) if (!retained.Contains(value)) Remove(value);
		}
		private Dictionary<int, int> Indices => indices;
		public void ExceptWith(IEnumerable<int> other) { foreach (var value in other) Remove(value); }
		public void SymmetricExceptWith(IEnumerable<int> other) { foreach (var value in other.ToHashSet()) if (!Remove(value)) Add(value); }
		public bool IsSubsetOf(IEnumerable<int> other) => this.ToHashSet().IsSubsetOf(other);
		public bool IsSupersetOf(IEnumerable<int> other) => other.All(Contains);
		public bool IsProperSubsetOf(IEnumerable<int> other) => this.ToHashSet().IsProperSubsetOf(other);
		public bool IsProperSupersetOf(IEnumerable<int> other) => this.ToHashSet().IsProperSupersetOf(other);
		public bool Overlaps(IEnumerable<int> other) => other.Any(Contains);
		public bool SetEquals(IEnumerable<int> other) => other is Facts facts && ReferenceEquals(indices, facts.Indices)
			? SetEquals(facts) : this.ToHashSet().SetEquals(other);
		public void CopyTo(int[] array, int arrayIndex) { foreach (var value in this) array[arrayIndex++] = value; }
		public IEnumerator<int> GetEnumerator()
		{
			for (var word = 0; word < _bits.Length; word++)
			{
				var remaining = _bits[word];
				while (remaining != 0)
				{
					yield return values[word * 64 + BitOperations.TrailingZeroCount(remaining)];
					remaining &= remaining - 1;
				}
			}
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	internal static IReadOnlyDictionary<int, HashSet<int>> Compute(
		M68kMachineFunction function,
		Action<M68kMachineInstruction, ISet<int>> transfer,
		Action<M68kMachineBlock, int, ISet<int>> transferEdge)
	{
		using var profile = M68kOptimizationProfiler.Measure("emission.non-null-facts");
		// The initial top state is read-only. A changed entry/edge gets its own
		// scratch set; a discarded top reference must never become scratch.
		var values = function.Values.Keys.ToArray();
		var indices = values.Select((id, index) => (id, index)).ToDictionary(p => p.id, p => p.index);
		Facts Empty() => new(values, indices);
		var universe = new Facts(values, indices, full: true);
		var entries = function.Blocks.ToDictionary(static block => block.Id,
			block => IsRoot(block) ? Empty() : universe);
		var edgeFacts = function.Blocks
			.SelectMany(block => block.Successors.Select(successor => (block.Id, successor)))
			.ToDictionary(static edge => edge, _ => universe);
		var incoming = Empty();
		var outgoing = Empty();
		var edge = Empty();
		var changed = true;
		while (changed)
		{
			changed = false;
			foreach (var block in function.Blocks)
			{
				if (IsRoot(block)) incoming.Clear();
				else
				{
					// Meet from the smallest edge instead of repopulating the full
					// value universe at every visit. Keep the universe intersection
					// explicit, so the fixed-point equation is unchanged.
					var seed = universe;
					foreach (var predecessor in block.Predecessors)
						if (edgeFacts[(predecessor, block.Id)].Count < seed.Count) seed = edgeFacts[(predecessor, block.Id)];
					incoming.CopyFrom(seed);
					incoming.IntersectWith(universe);
					foreach (var predecessor in block.Predecessors)
						incoming.IntersectWith(edgeFacts[(predecessor, block.Id)]);
					foreach (var phi in block.Phis)
						if (block.Predecessors.All(predecessor =>
							phi.Inputs.TryGetValue(predecessor, out var input) &&
							edgeFacts[(predecessor, block.Id)].Contains(input)))
							incoming.Add(phi.Definition);
				}

				// Capture the new state before exchanging its storage. Transferring
				// from the displaced old entry would use facts from the last round.
				var currentIncoming = incoming;
				var previous = entries[block.Id];
				if (!incoming.SetEquals(previous))
				{
					entries[block.Id] = incoming;
					incoming = ReferenceEquals(previous, universe) ? Empty() : previous;
					changed = true;
				}
				outgoing.CopyFrom(currentIncoming);
				foreach (var instruction in block.Instructions) transfer(instruction, outgoing);
				foreach (var successor in block.Successors)
				{
					edge.CopyFrom(outgoing);
					transferEdge(block, successor, edge);
					var key = (block.Id, successor);
					previous = edgeFacts[key];
					if (!edge.SetEquals(previous))
					{
						edgeFacts[key] = edge;
						edge = ReferenceEquals(previous, universe) ? Empty() : previous;
						changed = true;
					}
				}
			}
		}
		// Materialize separate mutable sets only after convergence.
		return entries.ToDictionary(p => p.Key, p => p.Value.ToHashSet());

		bool IsRoot(M68kMachineBlock block) => block.Id == function.EntryBlockId ||
			block.IsExceptionEntry || block.Predecessors.Count == 0;
	}
}
