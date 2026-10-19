/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class FloatingJoinCallbackState
	{
		public readonly int Fault;
		public readonly InvalidOperationException Failure = new("enumeration");
		public readonly InvalidOperationException DisposalFailure = new("disposal");
		public int Creations, Moves, Currents, Disposals;
		public FloatingJoinCallbackState(int fault) => Fault = fault;
	}

	private sealed class DistinctSingleJoinSource : IEnumerable<float>
	{
		private readonly FloatingJoinCallbackState _state;
		private readonly float[] _values;
		public DistinctSingleJoinSource(FloatingJoinCallbackState state, float[] values) { _state = state; _values = values; }
		public IEnumerator<float> GetEnumerator()
		{
			GC.Collect(); _state.Creations++;
			if (_state.Fault == 1) throw _state.Failure;
			return _state.Fault == 8 ? null! : new DistinctSingleJoinIterator(_state, _values);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	private sealed class DistinctSingleJoinIterator : IEnumerator<float>
	{
		private readonly FloatingJoinCallbackState _state;
		private readonly float[] _values;
		private int _index = -1;
		public DistinctSingleJoinIterator(FloatingJoinCallbackState state, float[] values) { _state = state; _values = values; }
		public float Current
		{
			get
			{
				GC.Collect(); _state.Currents++;
				if (_state.Fault == 3 || _state.Fault == 5 && _index == 1) throw _state.Failure;
				return _values[_index];
			}
		}
		object IEnumerator.Current => Current;
		public bool MoveNext()
		{
			GC.Collect(); _state.Moves++; _index++;
			if (_state.Fault == 2 || (_state.Fault == 4 || _state.Fault == 7) && _index == 1) throw _state.Failure;
			return _state.Fault != 9 && _index < _values.Length;
		}
		public void Dispose()
		{
			GC.Collect(); _state.Disposals++;
			if (_state.Fault == 6) throw _state.Failure;
			if (_state.Fault == 7) throw _state.DisposalFailure;
		}
		public void Reset() => throw new NotSupportedException();
	}

	private sealed class DistinctDoubleJoinSource : IEnumerable<double>
	{
		private readonly FloatingJoinCallbackState _state;
		private readonly double[] _values;
		public DistinctDoubleJoinSource(FloatingJoinCallbackState state, double[] values) { _state = state; _values = values; }
		public IEnumerator<double> GetEnumerator()
		{
			GC.Collect(); _state.Creations++;
			if (_state.Fault == 1) throw _state.Failure;
			return _state.Fault == 8 ? null! : new DistinctDoubleJoinIterator(_state, _values);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	private sealed class DistinctDoubleJoinIterator : IEnumerator<double>
	{
		private readonly FloatingJoinCallbackState _state;
		private readonly double[] _values;
		private int _index = -1;
		public DistinctDoubleJoinIterator(FloatingJoinCallbackState state, double[] values) { _state = state; _values = values; }
		public double Current
		{
			get
			{
				GC.Collect(); _state.Currents++;
				if (_state.Fault == 3 || _state.Fault == 5 && _index == 1) throw _state.Failure;
				return _values[_index];
			}
		}
		object IEnumerator.Current => Current;
		public bool MoveNext()
		{
			GC.Collect(); _state.Moves++; _index++;
			if (_state.Fault == 2 || (_state.Fault == 4 || _state.Fault == 7) && _index == 1) throw _state.Failure;
			return _state.Fault != 9 && _index < _values.Length;
		}
		public void Dispose()
		{
			GC.Collect(); _state.Disposals++;
			if (_state.Fault == 6) throw _state.Failure;
			if (_state.Fault == 7) throw _state.DisposalFailure;
		}
		public void Reset() => throw new NotSupportedException();
	}

	// A real CoreLib consumer exercises the transport on the host as well.
	private sealed class SingleAdapterJoinSource(DistinctSingleJoinSource source) : IEnumerable<float>
	{
		public IEnumerator<float> GetEnumerator() => ShadowSingleJoinEnumeration.GetEnumerator(source);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private sealed class DoubleAdapterJoinSource(DistinctDoubleJoinSource source) : IEnumerable<double>
	{
		public IEnumerator<double> GetEnumerator() => ShadowDoubleJoinEnumeration.GetEnumerator(source);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}

	private static void AppendFloatingCallbackJoin(StringBuilder builder, FloatingJoinCallbackState state, bool wide, bool text, bool adapter)
	{
		if (wide)
		{
			var values = new double[2]; values[0] = -1.5d; values[1] = 2.25d;
			var source = new DistinctDoubleJoinSource(state, values);
			IEnumerable<double> enumeration = adapter ? new DoubleAdapterJoinSource(source) : source;
			if (text) builder.AppendJoin("::", enumeration); else builder.AppendJoin('|', enumeration);
		}
		else
		{
			var values = new float[2]; values[0] = -1.5f; values[1] = 2.25f;
			var source = new DistinctSingleJoinSource(state, values);
			IEnumerable<float> enumeration = adapter ? new SingleAdapterJoinSource(source) : source;
			if (text) builder.AppendJoin("::", enumeration); else builder.AppendJoin('|', enumeration);
		}
	}

	public static int CoreLibFloatingCustomCallbackContractsEntry() => FloatingCustomCallbackContracts(false);
	public static int ManagedFloatingCustomCallbackContractsEntry() => FloatingCustomCallbackContracts(true);

	private static int FloatingCustomCallbackContracts(bool adapter)
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			for (var separator = 0; separator < 2; separator++)
			for (var capacity = 0; capacity < 2; capacity++)
			for (var fault = 0; fault < 10; fault++)
			{
				var state = new FloatingJoinCallbackState(fault);
				var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
				var snapshot = builder.ToString();
				Exception? caught = null;
				try { AppendFloatingCallbackJoin(builder, state, precision != 0, separator != 0, adapter); }
				catch (Exception exception) { caught = exception; }
				if (fault is 0 or 9 ? caught != null : fault == 8 ? caught is not NullReferenceException :
					!ReferenceEquals(caught, fault == 7 ? state.DisposalFailure : state.Failure)) return 100 + fault;
				var delimiter = separator == 0 ? "|" : "::";
				var expected = fault is 0 or 6 ? "seed-1.5" + delimiter + "2.25" :
					fault is 4 or 7 ? "seed-1.5" : fault == 5 ? "seed-1.5" + delimiter : "seed";
				GC.Collect();
				if (builder.ToString() != expected || snapshot != "seed") return 200 + fault;
				var moves = fault is 1 or 8 ? 0 : fault is 2 or 3 or 9 ? 1 : fault is 4 or 5 or 7 ? 2 : 3;
				var currents = fault is 1 or 2 or 8 or 9 ? 0 : fault is 3 or 4 or 7 ? 1 : 2;
				if (state.Creations != 1 || state.Moves != moves || state.Currents != currents ||
					state.Disposals != (fault is 1 or 8 ? 0 : 1)) return 300 + fault;
				builder.Clear().Append("seed");
				var retry = new FloatingJoinCallbackState(0);
				AppendFloatingCallbackJoin(builder, retry, precision != 0, separator != 0, adapter);
				if (builder.ToString() != "seed-1.5" + delimiter + "2.25" || retry.Disposals != 1 || snapshot != "seed") return 400 + fault;
				builder.Clear().Append("tail");
				if (builder.ToString() != "tail") return 500 + fault;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ShadowSingleJoinEnumerator CreateOwnedDistinctSingleAdapter(FloatingJoinCallbackState state)
	{
		var values = new float[2]; values[0] = -0f; values[1] = float.Epsilon;
		return ShadowSingleJoinEnumeration.GetEnumerator(new DistinctSingleJoinSource(state, values));
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ShadowDoubleJoinEnumerator CreateOwnedDistinctDoubleAdapter(FloatingJoinCallbackState state)
	{
		var values = new double[2]; values[0] = -0d; values[1] = double.Epsilon;
		return ShadowDoubleJoinEnumeration.GetEnumerator(new DistinctDoubleJoinSource(state, values));
	}

	public static int CoreLibFloatingDistinctIteratorOwnershipEntry()
	{
		var singleState = new FloatingJoinCallbackState(0);
		var doubleState = new FloatingJoinCallbackState(0);
		var single = CreateOwnedDistinctSingleAdapter(singleState);
		var wide = CreateOwnedDistinctDoubleAdapter(doubleState);
		GC.Collect();
		if (!single.MoveNext() || Unsafe.BitCast<float, uint>(single.Current) != 0x80000000u) return 1;
		if (!wide.MoveNext() || Unsafe.BitCast<double, ulong>(wide.Current) != 0x8000000000000000UL) return 2;
		if (!single.MoveNext() || Unsafe.BitCast<float, uint>(single.Current) != 1) return 3;
		if (!wide.MoveNext() || Unsafe.BitCast<double, ulong>(wide.Current) != 1) return 4;
		if (single.MoveNext() || wide.MoveNext()) return 5;
		single.Dispose(); wide.Dispose(); single.Dispose(); wide.Dispose();
		GC.Collect();
		if (single.Current != 0 || wide.Current != 0 || single.MoveNext() || wide.MoveNext()) return 6;
		return singleState.Creations == 1 && doubleState.Creations == 1 && singleState.Disposals == 1 && doubleState.Disposals == 1 &&
			singleState.Currents == 2 && doubleState.Currents == 2 && singleState.Moves == 3 && doubleState.Moves == 3 ? 42 : 7;
	}

	public static int CoreLibFloatingAdapterAllocationCleanupEntry()
	{
		var singles = new float[0];
		var doubles = new double[0];
		for (var precision = 0; precision < 2; precision++)
		for (var failAt = 1; failAt <= 3; failAt++)
		{
			var state = new FloatingJoinCallbackState(6);
			var singleSource = new DistinctSingleJoinSource(state, singles);
			var doubleSource = new DistinctDoubleJoinSource(state, doubles);
			ShadowSingleJoinEnumerator? single = null;
			ShadowDoubleJoinEnumerator? wide = null;
			Exception? caught = null;
			SetStringBuilderAllocationFailure(failAt);
			try
			{
				if (precision == 0) single = ShadowSingleJoinEnumeration.GetEnumerator(singleSource);
				else wide = ShadowDoubleJoinEnumeration.GetEnumerator(doubleSource);
			}
			catch (Exception exception) { caught = exception; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (failAt == 1 ? caught is not OutOfMemoryException : failAt == 2 ? !ReferenceEquals(caught, state.Failure) : caught != null) return 100 + failAt;
			if (state.Creations != 1 || state.Moves != 0 || state.Currents != 0 || state.Disposals != (failAt == 2 ? 1 : 0)) return 200 + failAt;
			if (failAt != 3) continue;
			caught = null;
			try { if (single != null) single.Dispose(); else wide!.Dispose(); }
			catch (Exception exception) { caught = exception; }
			if (!ReferenceEquals(caught, state.Failure) || state.Disposals != 1) return 300 + precision;
			if (single != null)
			{
				single.Dispose();
				if (single.MoveNext() || single.Current != 0) return 400;
			}
			else
			{
				wide!.Dispose();
				if (wide.MoveNext() || wide.Current != 0) return 401;
			}
			if (state.Disposals != 1) return 500;
		}
		return 42;
	}

	private static void AppendPreparedFloatingCallbackJoin(StringBuilder builder, DistinctSingleJoinSource single, DistinctDoubleJoinSource wide, bool precision, bool text)
	{
		if (precision) { if (text) builder.AppendJoin("::", wide); else builder.AppendJoin('|', wide); }
		else { if (text) builder.AppendJoin("::", single); else builder.AppendJoin('|', single); }
	}

	public static int CoreLibFloatingCustomEnumerableAllocationEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			var singles = new float[2]; singles[0] = -1.5f; singles[1] = 2.25f;
			var doubles = new double[2]; doubles[0] = -1.5d; doubles[1] = 2.25d;
			for (var precision = 0; precision < 2; precision++)
			for (var separator = 0; separator < 2; separator++)
			for (var capacity = 0; capacity < 2; capacity++)
			{
				var expected = separator == 0 ? "seed-1.5|2.25" : "seed-1.5::2.25";
				var warmState = new FloatingJoinCallbackState(0);
				var warm = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
				AppendPreparedFloatingCallbackJoin(warm, new DistinctSingleJoinSource(warmState, singles),
					new DistinctDoubleJoinSource(warmState, doubles), precision != 0, separator != 0);
				if (warm.ToString() != expected) return 100;
				var succeeded = false;
				for (var failAt = 1; failAt <= 16; failAt++)
				{
					var state = new FloatingJoinCallbackState(0);
					var single = new DistinctSingleJoinSource(state, singles);
					var wide = new DistinctDoubleJoinSource(state, doubles);
					var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
					var snapshot = builder.ToString();
					var threw = false;
					SetStringBuilderAllocationFailure(failAt);
					try { AppendPreparedFloatingCallbackJoin(builder, single, wide, precision != 0, separator != 0); }
					catch (OutOfMemoryException) { threw = true; }
					finally { SetStringBuilderAllocationFailure(0); }
					var failed = builder.ToString();
					if (snapshot != "seed" || state.Creations != 1 || state.Disposals != (failAt == 1 ? 0 : 1)) return 200 + failAt;
					if (threw ? !expected.StartsWith(failed, StringComparison.Ordinal) || failed.Length < 4 : failed != expected) return 300 + failAt;
					if (failAt <= 2 && (failed != "seed" || state.Moves != 0 || state.Currents != 0)) return 400 + failAt;
					builder.Clear().Append("seed");
					var retry = new FloatingJoinCallbackState(0);
					AppendPreparedFloatingCallbackJoin(builder, new DistinctSingleJoinSource(retry, singles),
						new DistinctDoubleJoinSource(retry, doubles), precision != 0, separator != 0);
					if (builder.ToString() != expected || retry.Disposals != 1 || snapshot != "seed") return 500 + failAt;
					if (!threw) { succeeded = true; break; }
				}
				if (!succeeded) return 600;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
