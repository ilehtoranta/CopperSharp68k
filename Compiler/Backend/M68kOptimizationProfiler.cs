/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Diagnostics;

namespace CopperSharp.Compiler.Backend;

/// <summary>Opt-in, calling-thread diagnostics; timings never affect optimization.</summary>
internal static class M68kOptimizationProfiler
{
	[ThreadStatic]
	private static Session? _current;

	internal sealed record Measurement(string Name, long Calls,
		double InclusiveSeconds, double ExclusiveSeconds, long AllocatedBytes);

	internal sealed record Snapshot(IReadOnlyList<Measurement> Measurements,
		IReadOnlyDictionary<string, long> Counters);

	internal static Session Start()
	{
		if (_current is not null)
			throw new InvalidOperationException("An optimizer profile is already active on this thread.");
		return _current = new Session();
	}

	internal static Scope Measure(string name) => _current is { } session
		? session.Enter(name) : default;

	internal static void Count(string name, long value = 1)
	{
		if (_current is { } session)
			session.Counters[name] = session.Counters.GetValueOrDefault(name) + value;
	}

	internal sealed class Session : IDisposable
	{
		private readonly int _thread = Environment.CurrentManagedThreadId;
		private readonly Dictionary<string, Totals> _totals = new(StringComparer.Ordinal);
		private readonly List<Frame> _stack = [];
		internal Dictionary<string, long> Counters { get; } = new(StringComparer.Ordinal);

		internal Scope Enter(string name)
		{
			if (!_totals.TryGetValue(name, out var totals))
				_totals.Add(name, totals = new Totals());
			_stack.Add(new(totals, Stopwatch.GetTimestamp(), GC.GetAllocatedBytesForCurrentThread(), 0));
			return new(this, _stack.Count);
		}

		internal void Leave(int depth)
		{
			if (Environment.CurrentManagedThreadId != _thread || depth != _stack.Count)
				throw new InvalidOperationException("Optimizer profile scopes must close in order on their originating thread.");
			var frame = _stack[^1];
			var elapsed = Stopwatch.GetTimestamp() - frame.Start;
			frame.Totals.Calls++;
			frame.Totals.InclusiveTicks += elapsed;
			frame.Totals.ExclusiveTicks += elapsed - frame.ChildTicks;
			frame.Totals.AllocatedBytes += GC.GetAllocatedBytesForCurrentThread() - frame.Allocated;
			_stack.RemoveAt(_stack.Count - 1);
			if (_stack.Count != 0)
				_stack[^1] = _stack[^1] with { ChildTicks = _stack[^1].ChildTicks + elapsed };
		}

		internal Snapshot GetSnapshot()
		{
			if (_stack.Count != 0)
				throw new InvalidOperationException("Close measured scopes before reading an optimizer profile.");
			return new(_totals.Select(item => new Measurement(item.Key, item.Value.Calls,
				(double)item.Value.InclusiveTicks / Stopwatch.Frequency,
				(double)item.Value.ExclusiveTicks / Stopwatch.Frequency,
				item.Value.AllocatedBytes)).OrderByDescending(item => item.ExclusiveSeconds).ToArray(),
				new Dictionary<string, long>(Counters, StringComparer.Ordinal));
		}

		public void Dispose()
		{
			if (Environment.CurrentManagedThreadId != _thread || _current != this || _stack.Count != 0)
				throw new InvalidOperationException("Close measured scopes before ending their optimizer profile on its originating thread.");
			_current = null;
		}

		private sealed class Totals
		{
			internal long Calls, InclusiveTicks, ExclusiveTicks, AllocatedBytes;
		}

		private readonly record struct Frame(Totals Totals, long Start, long Allocated, long ChildTicks);
	}

	internal readonly struct Scope(Session? session, int depth) : IDisposable
	{
		public void Dispose() => session?.Leave(depth);
	}
}
