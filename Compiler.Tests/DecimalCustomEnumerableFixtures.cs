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
	private sealed class DecimalJoinCallbackState(int fault)
	{
		public readonly int Fault = fault;
		public readonly InvalidOperationException Failure = new("decimal enumeration");
		public readonly InvalidOperationException DisposalFailure = new("decimal disposal");
		public int Creations, Moves, Currents, Disposals;
	}
	private sealed class CustomDecimalJoinSource(DecimalJoinCallbackState state, decimal[] values) : IEnumerable<decimal>
	{
		public IEnumerator<decimal> GetEnumerator()
		{
			GC.Collect(); state.Creations++;
			if (state.Fault == 1) throw state.Failure;
			return state.Fault == 8 ? null! : new CustomDecimalJoinIterator(state, values);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private sealed class CustomDecimalJoinIterator(DecimalJoinCallbackState state, decimal[] values) : IEnumerator<decimal>
	{
		private int _index = -1;
		public decimal Current
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
	private sealed class CustomDecimalAdapterSource(CustomDecimalJoinSource source) : IEnumerable<decimal>
	{
		public IEnumerator<decimal> GetEnumerator() => ShadowDecimalJoinEnumeration.GetEnumerator(source);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private static decimal[] CreateCustomDecimalJoinValues()
	{
		// Explicit Decimal construction retains scale and sign bits.
		var values = new decimal[4];
		values[0] = new decimal(1234500, 0, 0, true, 4);
		values[1] = new decimal(0, 0, 0, true, 3);
		values[2] = new decimal(1, 0, 0, false, 28);
		values[3] = new decimal(-1, -1, -1, false, 0);
		return values;
	}
	private static void AppendCustomDecimalJoin(StringBuilder builder, IEnumerable<decimal> source, bool text)
	{
		if (text) builder.AppendJoin("::", source); else builder.AppendJoin('|', source);
	}
	public static int CoreLibDecimalCustomCallbackContractsEntry() => DecimalCustomCallbackContracts(false);
	public static int ManagedDecimalCustomCallbackContractsEntry() => DecimalCustomCallbackContracts(true);
	private static int DecimalCustomCallbackContracts(bool adapter)
	{
		var previous = CultureInfo.CurrentCulture;
		try { CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; return DecimalCustomCallbackBody(adapter); }
		finally { CultureInfo.CurrentCulture = previous; }
	}
	private static int DecimalCustomCallbackBody(bool adapter)
	{
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 0; capacity < 2; capacity++)
		for (var fault = 0; fault < 10; fault++)
		{
			var state = new DecimalJoinCallbackState(fault);
			var source = new CustomDecimalJoinSource(state, CreateCustomDecimalJoinValues());
			IEnumerable<decimal> enumeration = adapter ? new CustomDecimalAdapterSource(source) : source;
			var builder = new StringBuilder(capacity == 0 ? 1 : 192).Append("seed");
			var snapshot = builder.ToString();
			Exception? caught = null;
			try { AppendCustomDecimalJoin(builder, enumeration, separator != 0); }
			catch (Exception exception) { caught = exception; }
			if (fault is 0 or 9 ? caught != null : fault == 8 ? caught is not NullReferenceException :
				!ReferenceEquals(caught, fault == 7 ? state.DisposalFailure : state.Failure)) return 100 + fault;
			var delimiter = separator == 0 ? "|" : "::";
			var complete = separator == 0 ? "seed-123.4500|0.000|0.0000000000000000000000000001|79228162514264337593543950335" : "seed-123.4500::0.000::0.0000000000000000000000000001::79228162514264337593543950335";
			var expected = fault is 0 or 6 ? complete : fault is 4 or 7 ? "seed-123.4500" :
				fault == 5 ? "seed-123.4500" + delimiter : "seed";
			GC.Collect();
			if (builder.ToString() != expected || snapshot != "seed") return 200 + fault;
			var moves = fault is 1 or 8 ? 0 : fault is 2 or 3 or 9 ? 1 : fault is 4 or 5 or 7 ? 2 : 5;
			var currents = fault is 1 or 2 or 8 or 9 ? 0 : fault is 3 or 4 or 7 ? 1 : fault == 5 ? 2 : 4;
			if (state.Creations != 1 || state.Moves != moves || state.Currents != currents ||
				state.Disposals != (fault is 1 or 8 ? 0 : 1)) return 300 + fault;
			builder.Clear().Append("seed");
			var retry = new DecimalJoinCallbackState(0);
			AppendCustomDecimalJoin(builder, new CustomDecimalJoinSource(retry, CreateCustomDecimalJoinValues()), separator != 0);
			if (builder.ToString() != complete || retry.Disposals != 1 || snapshot != "seed") return 400 + fault;
			builder.Clear().Append("tail");
			if (builder.ToString() != "tail") return 500 + fault;
		}
		return 42;
	}

	private static bool CustomDecimalBitsMatch(decimal value, int low, int middle, int high, int flags)
	{
		Span<int> bits = stackalloc int[4];
		return decimal.GetBits(value, bits) == 4 && bits[0] == low && bits[1] == middle && bits[2] == high && bits[3] == flags;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<decimal>.Enumerator CreateOwnedCoreLibDecimalListIterator()
	{
		var list = new List<decimal>(2);
		list.Add(new decimal(0, 0, 0, true, 3));
		list.Add(new decimal(-1, -1, -1, true, 28));
		return list.GetEnumerator();
	}
	public static int CoreLibDecimalListIteratorOwnershipEntry()
	{
		var iterator = CreateOwnedCoreLibDecimalListIterator();
		GC.Collect();
		if (!iterator.MoveNext() || !CustomDecimalBitsMatch(iterator.Current, 0, 0, 0, unchecked((int)0x80030000))) return 1;
		GC.Collect();
		if (!iterator.MoveNext() || !CustomDecimalBitsMatch(iterator.Current, -1, -1, -1, unchecked((int)0x801C0000))) return 2;
		if (iterator.MoveNext() || !CustomDecimalBitsMatch(iterator.Current, 0, 0, 0, 0)) return 3;
		iterator.Dispose();
		GC.Collect();
		return iterator.MoveNext() ? 4 : 42;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ShadowDecimalJoinEnumerator CreateOwnedCustomDecimalAdapter(DecimalJoinCallbackState state)
	{
		var values = new decimal[2];
		values[0] = new decimal(0, 0, 0, true, 3);
		values[1] = new decimal(-1, -1, -1, true, 28);
		return ShadowDecimalJoinEnumeration.GetEnumerator(new CustomDecimalJoinSource(state, values));
	}
	public static int CoreLibDecimalCustomIteratorOwnershipEntry()
	{
		var state = new DecimalJoinCallbackState(0);
		var adapter = CreateOwnedCustomDecimalAdapter(state);
		GC.Collect();
		if (!adapter.MoveNext() || !CustomDecimalBitsMatch(adapter.Current, 0, 0, 0, unchecked((int)0x80030000))) return 1;
		if (!adapter.MoveNext() || !CustomDecimalBitsMatch(adapter.Current, -1, -1, -1, unchecked((int)0x801C0000))) return 2;
		if (adapter.MoveNext()) return 3;
		adapter.Dispose(); adapter.Dispose();
		GC.Collect();
		if (adapter.MoveNext() || !CustomDecimalBitsMatch(adapter.Current, 0, 0, 0, 0)) return 4;
		return state.Creations == 1 && state.Moves == 3 && state.Currents == 2 && state.Disposals == 1 ? 42 : 5;
	}
	public static int CoreLibDecimalAdapterAllocationCleanupEntry()
	{
		var values = new decimal[0];
		for (var mode = 0; mode < 2; mode++)
		for (var failAt = 1; failAt <= 3; failAt++)
		{
			var state = new DecimalJoinCallbackState(mode == 0 ? 0 : 6);
			var source = new CustomDecimalJoinSource(state, values);
			ShadowDecimalJoinEnumerator? adapter = null;
			Exception? caught = null;
			SetStringBuilderAllocationFailure(failAt);
			try { adapter = ShadowDecimalJoinEnumeration.GetEnumerator(source); }
			catch (Exception exception) { caught = exception; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (failAt == 3 ? caught != null : failAt == 2 && mode != 0 ? !ReferenceEquals(caught, state.Failure) : caught is not OutOfMemoryException) return 100 + failAt;
			if (state.Creations != 1 || state.Moves != 0 || state.Currents != 0 || state.Disposals != (failAt == 2 ? 1 : 0)) return 200 + failAt;
			if (failAt != 3) continue;
			caught = null;
			try { adapter!.Dispose(); } catch (Exception exception) { caught = exception; }
			if (mode == 0 ? caught != null : !ReferenceEquals(caught, state.Failure)) return 300;
			adapter!.Dispose();
			if (!CustomDecimalBitsMatch(adapter.Current, 0, 0, 0, 0) || adapter.MoveNext() || state.Disposals != 1) return 400;
		}
		return 42;
	}
	public static int CoreLibDecimalCustomEnumerableAllocationEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try { CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; return DecimalCustomEnumerableAllocationBody(); }
		finally { CultureInfo.CurrentCulture = previous; }
	}
	private static int DecimalCustomEnumerableAllocationBody()
	{
		var values = CreateCustomDecimalJoinValues();
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 0; capacity < 2; capacity++)
		{
			var expected = separator == 0 ? "seed-123.4500|0.000|0.0000000000000000000000000001|79228162514264337593543950335" : "seed-123.4500::0.000::0.0000000000000000000000000001::79228162514264337593543950335";
			var succeeded = false;
			for (var failAt = 1; failAt <= 32; failAt++)
			{
				var state = new DecimalJoinCallbackState(0);
				var source = new CustomDecimalJoinSource(state, values);
				var builder = new StringBuilder(capacity == 0 ? 1 : 192).Append("seed");
				var snapshot = builder.ToString();
				var threw = false;
				SetStringBuilderAllocationFailure(failAt);
				try { AppendCustomDecimalJoin(builder, source, separator != 0); }
				catch (OutOfMemoryException) { threw = true; }
				finally { SetStringBuilderAllocationFailure(0); }
				var failed = builder.ToString();
				if (state.Creations != 1 || state.Disposals != (failAt == 1 ? 0 : 1) || snapshot != "seed") return 100 + failAt;
				if (threw ? !expected.StartsWith(failed, StringComparison.Ordinal) || failed.Length < 4 : failed != expected) return 200 + failAt;
				if (failAt <= 2 && (failed != "seed" || state.Moves != 0 || state.Currents != 0)) return 300 + failAt;
				builder.Clear().Append("seed");
				var retry = new DecimalJoinCallbackState(0);
				AppendCustomDecimalJoin(builder, new CustomDecimalJoinSource(retry, values), separator != 0);
				if (builder.ToString() != expected || retry.Disposals != 1 || snapshot != "seed") return 400 + failAt;
				if (!threw) { succeeded = true; break; }
			}
			if (!succeeded) return 500;
		}
		return 42;
	}
}
