/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Collections;
using System.Runtime.CompilerServices;
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class Int32JoinCallbackState(int fault)
	{
		public readonly int Fault = fault;
		public readonly InvalidOperationException Failure = new("int enumeration");
		public readonly InvalidOperationException DisposalFailure = new("int disposal");
		public int Creations, Moves, Currents, Disposals;
	}
	private sealed class CustomInt32JoinSource(Int32JoinCallbackState state, int[] values) : IEnumerable<int>
	{
		public IEnumerator<int> GetEnumerator()
		{
			GC.Collect(); state.Creations++;
			if (state.Fault == 1) throw state.Failure;
			return state.Fault == 8 ? null! : new CustomInt32JoinIterator(state, values);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private sealed class CustomInt32JoinIterator(Int32JoinCallbackState state, int[] values) : IEnumerator<int>
	{
		private int _index = -1;
		public int Current
		{
			get
			{
				GC.Collect(); state.Currents++;
				if (state.Fault == 3 || state.Fault == 5 && _index == 1) throw state.Failure;
				return values[_index];
			}
		}
		object IEnumerator.Current => Current;
		public bool MoveNext()
		{
			GC.Collect(); state.Moves++; _index++;
			if (state.Fault == 2 || (state.Fault == 4 || state.Fault == 7) && _index == 1) throw state.Failure;
			return state.Fault != 9 && _index < values.Length;
		}
		public void Dispose()
		{
			GC.Collect(); state.Disposals++;
			if (state.Fault == 6) throw state.Failure;
			if (state.Fault == 7) throw state.DisposalFailure;
		}
		public void Reset() => throw new NotSupportedException();
	}
	private sealed class CustomInt32AdapterSource(CustomInt32JoinSource source) : IEnumerable<int>
	{
		public IEnumerator<int> GetEnumerator() => ShadowInt32JoinEnumeration.GetEnumerator(source);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private static int[] CreateCustomInt32JoinValues()
	{
		// Explicit stores avoid the unrelated RuntimeHelpers.InitializeArray boundary.
		var values = new int[4];
		values[0] = int.MinValue; values[1] = 0; values[2] = -17; values[3] = int.MaxValue;
		return values;
	}
	private static void AppendCustomInt32Join(StringBuilder builder, IEnumerable<int> source, bool text)
	{
		if (text) builder.AppendJoin("::", source); else builder.AppendJoin('|', source);
	}
	public static int CoreLibInt32CustomCallbackContractsEntry() => Int32CustomCallbackContracts(false);
	public static int ManagedInt32CustomCallbackContractsEntry() => Int32CustomCallbackContracts(true);
	private static int Int32CustomCallbackContracts(bool adapter)
	{
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 0; capacity < 2; capacity++)
		for (var fault = 0; fault < 10; fault++)
		{
			var state = new Int32JoinCallbackState(fault);
			var source = new CustomInt32JoinSource(state, CreateCustomInt32JoinValues());
			IEnumerable<int> enumeration = adapter ? new CustomInt32AdapterSource(source) : source;
			var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
			var snapshot = builder.ToString();
			Exception? caught = null;
			try { AppendCustomInt32Join(builder, enumeration, separator != 0); }
			catch (Exception exception) { caught = exception; }
			if (fault is 0 or 9 ? caught != null : fault == 8 ? caught is not NullReferenceException :
				!ReferenceEquals(caught, fault == 7 ? state.DisposalFailure : state.Failure)) return 100 + fault;
			var delimiter = separator == 0 ? "|" : "::";
			var complete = separator == 0 ? "seed-2147483648|0|-17|2147483647" : "seed-2147483648::0::-17::2147483647";
			var expected = fault is 0 or 6 ? complete : fault is 4 or 7 ? "seed-2147483648" :
				fault == 5 ? "seed-2147483648" + delimiter : "seed";
			GC.Collect();
			if (builder.ToString() != expected || snapshot != "seed") return 200 + fault;
			var moves = fault is 1 or 8 ? 0 : fault is 2 or 3 or 9 ? 1 : fault is 4 or 5 or 7 ? 2 : 5;
			var currents = fault is 1 or 2 or 8 or 9 ? 0 : fault is 3 or 4 or 7 ? 1 : fault == 5 ? 2 : 4;
			if (state.Creations != 1 || state.Moves != moves || state.Currents != currents ||
				state.Disposals != (fault is 1 or 8 ? 0 : 1)) return 300 + fault;
			builder.Clear().Append("seed");
			var retry = new Int32JoinCallbackState(0);
			AppendCustomInt32Join(builder, new CustomInt32JoinSource(retry, CreateCustomInt32JoinValues()), separator != 0);
			if (builder.ToString() != complete || retry.Disposals != 1 || snapshot != "seed") return 400 + fault;
			builder.Clear().Append("tail");
			if (builder.ToString() != "tail") return 500 + fault;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ShadowInt32JoinEnumerator CreateOwnedCustomInt32Adapter(Int32JoinCallbackState state)
	{
		var values = new int[2];
		values[0] = int.MinValue;
		values[1] = int.MaxValue;
		return ShadowInt32JoinEnumeration.GetEnumerator(new CustomInt32JoinSource(state, values));
	}
	public static int CoreLibInt32CustomIteratorOwnershipEntry()
	{
		var state = new Int32JoinCallbackState(0);
		var adapter = CreateOwnedCustomInt32Adapter(state);
		GC.Collect();
		if (!adapter.MoveNext() || adapter.Current != int.MinValue) return 1;
		if (!adapter.MoveNext() || adapter.Current != int.MaxValue) return 2;
		if (adapter.MoveNext()) return 3;
		adapter.Dispose(); adapter.Dispose();
		GC.Collect();
		if (adapter.MoveNext() || adapter.Current != 0) return 4;
		return state.Creations == 1 && state.Moves == 3 && state.Currents == 2 && state.Disposals == 1 ? 42 : 5;
	}
	public static int CoreLibInt32AdapterAllocationCleanupEntry()
	{
		var values = new int[0];
		for (var mode = 0; mode < 2; mode++)
		for (var failAt = 1; failAt <= 3; failAt++)
		{
			var state = new Int32JoinCallbackState(mode == 0 ? 0 : 6);
			var source = new CustomInt32JoinSource(state, values);
			ShadowInt32JoinEnumerator? adapter = null;
			Exception? caught = null;
			SetStringBuilderAllocationFailure(failAt);
			try { adapter = ShadowInt32JoinEnumeration.GetEnumerator(source); }
			catch (Exception exception) { caught = exception; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (failAt == 3 ? caught != null : failAt == 2 && mode != 0 ? !ReferenceEquals(caught, state.Failure) : caught is not OutOfMemoryException) return 100 + failAt;
			if (state.Creations != 1 || state.Moves != 0 || state.Currents != 0 || state.Disposals != (failAt == 2 ? 1 : 0)) return 200 + failAt;
			if (failAt != 3) continue;
			caught = null;
			try { adapter!.Dispose(); } catch (Exception exception) { caught = exception; }
			if (mode == 0 ? caught != null : !ReferenceEquals(caught, state.Failure)) return 300;
			adapter!.Dispose();
			if (adapter.Current != 0 || adapter.MoveNext() || state.Disposals != 1) return 400;
		}
		return 42;
	}
	public static int CoreLibInt32CustomEnumerableAllocationEntry()
	{
		var values = CreateCustomInt32JoinValues();
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 0; capacity < 2; capacity++)
		{
			var expected = separator == 0 ? "seed-2147483648|0|-17|2147483647" : "seed-2147483648::0::-17::2147483647";
			var succeeded = false;
			for (var failAt = 1; failAt <= 32; failAt++)
			{
				var state = new Int32JoinCallbackState(0);
				var source = new CustomInt32JoinSource(state, values);
				var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
				var snapshot = builder.ToString();
				var threw = false;
				SetStringBuilderAllocationFailure(failAt);
				try { AppendCustomInt32Join(builder, source, separator != 0); }
				catch (OutOfMemoryException) { threw = true; }
				finally { SetStringBuilderAllocationFailure(0); }
				var failed = builder.ToString();
				if (state.Creations != 1 || state.Disposals != (failAt == 1 ? 0 : 1) || snapshot != "seed") return 100 + failAt;
				if (threw ? !expected.StartsWith(failed, StringComparison.Ordinal) || failed.Length < 4 : failed != expected) return 200 + failAt;
				if (failAt <= 2 && (failed != "seed" || state.Moves != 0 || state.Currents != 0)) return 300 + failAt;
				builder.Clear().Append("seed");
				var retry = new Int32JoinCallbackState(0);
				AppendCustomInt32Join(builder, new CustomInt32JoinSource(retry, values), separator != 0);
				if (builder.ToString() != expected || retry.Disposals != 1 || snapshot != "seed") return 400 + failAt;
				if (!threw) { succeeded = true; break; }
			}
			if (!succeeded) return 500;
		}
		return 42;
	}
}
