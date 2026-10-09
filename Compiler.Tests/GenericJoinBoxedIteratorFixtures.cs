/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class StructGenericJoinSource<T>(T[] values, GenericJoinState state) : IEnumerable<T>
	{
		public IEnumerator<T> GetEnumerator()
		{
			GC.Collect(); state.Creations++;
			if (state.Fault == 1) throw state.Failure;
			return state.Fault == 8 ? null! : new StructGenericJoinIterator<T>(values, state);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private struct StructGenericJoinIterator<T> : IEnumerator<T>
	{
		private readonly T[] _values;
		private readonly GenericJoinState _state;
		private int _index;
		public StructGenericJoinIterator(T[] values, GenericJoinState state) { _values = values; _state = state; _index = -1; }
		public T Current
		{
			get { GC.Collect(); _state.Currents++; if (_state.Fault == 3 || _state.Fault == 5 && _index == 1) throw _state.Failure; return _values[_index]; }
		}
		object? IEnumerator.Current => Current;
		public bool MoveNext()
		{
			GC.Collect(); _state.Moves++; _index++;
			if (_state.Fault == 2 || (_state.Fault == 4 || _state.Fault == 7) && _index == 1) throw _state.Failure;
			return _state.Fault != 9 && _index < _values.Length;
		}
		public void Dispose() { GC.Collect(); _state.Disposals++; if (_state.Fault == 6) throw _state.Failure; if (_state.Fault == 7) throw _state.DisposalFailure; }
		public void Reset() => throw new NotSupportedException();
	}
	private static int CheckStructGenericJoin<T>(T first, T second, string firstText, string secondText)
	{
		var values = new T[2]; values[0] = first; values[1] = second;
		for (var fault = 0; fault < 10; fault++)
		{
			var state = new GenericJoinState(fault);
			var builder = new StringBuilder(1).Append("seed"); var snapshot = builder.ToString();
			Exception? caught = null;
			try { builder.AppendJoin('|', new StructGenericJoinSource<T>(values, state)); }
			catch (Exception exception) { caught = exception; }
			if (fault is 0 or 9 ? caught != null : fault == 8 ? caught is not NullReferenceException :
				!ReferenceEquals(caught, fault == 7 ? state.DisposalFailure : state.Failure)) return 100 + fault;
			var complete = "seed" + firstText + "|" + secondText;
			var expected = fault is 0 or 6 ? complete : fault is 4 or 7 ? "seed" + firstText : fault == 5 ? "seed" + firstText + "|" : "seed";
			GC.Collect();
			if (builder.ToString() != expected || snapshot != "seed") return 200 + fault;
			var moves = fault is 1 or 8 ? 0 : fault is 2 or 3 or 9 ? 1 : fault is 4 or 5 or 7 ? 2 : 3;
			var currents = fault is 1 or 2 or 8 or 9 ? 0 : fault is 3 or 4 or 7 ? 1 : 2;
			if (state.Creations != 1 || state.Moves != moves || state.Currents != currents || state.Disposals != (fault is 1 or 8 ? 0 : 1)) return 300 + fault;
			builder.Clear(); var retry = new GenericJoinState(); builder.AppendJoin("|", new StructGenericJoinSource<T>(values, retry));
			if ("seed" + builder.ToString() != complete || retry.Moves != 3 || retry.Currents != 2 || retry.Disposals != 1) return 400 + fault;
		}
		return 42;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerator<JoinInt64> CreateOwnedStructGenericJoin(GenericJoinState state)
	{
		var values = new JoinInt64[2]; values[0] = JoinInt64.Named; values[1] = JoinInt64.First | JoinInt64.Second;
		return new StructGenericJoinSource<JoinInt64>(values, state).GetEnumerator();
	}
	public static int CoreLibGenericJoinBoxedIteratorContractsEntry()
	{
		var result = CheckStructGenericJoin(JoinSByte.Named, (JoinSByte)127, "Named", "127"); if (result != 42) return result;
		result = CheckStructGenericJoin(JoinInt64.Named, JoinInt64.First | JoinInt64.Second, "Named", "First, Second"); if (result != 42) return 1000 + result;
		result = CheckStructGenericJoin<ulong>(0, ulong.MaxValue, "0", "18446744073709551615"); if (result != 42) return 2000 + result;
		var state = new GenericJoinState(); var iterator = CreateOwnedStructGenericJoin(state); GC.Collect();
		if (!iterator.MoveNext() || iterator.Current != JoinInt64.Named) return 3000;
		GC.Collect();
		if (!iterator.MoveNext() || iterator.Current != (JoinInt64.First | JoinInt64.Second)) return 3001;
		if (iterator.MoveNext()) return 3002;
		iterator.Dispose();
		return state.Creations == 1 && state.Moves == 3 && state.Currents == 2 && state.Disposals == 1 ? 42 : 3003;
	}
}
