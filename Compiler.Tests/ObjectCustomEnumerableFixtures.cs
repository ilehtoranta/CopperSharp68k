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
	private sealed class ObjectJoinCallbackState(int fault)
	{
		public readonly int Fault = fault;
		public readonly InvalidOperationException Failure = new("object enumeration");
		public readonly InvalidOperationException DisposalFailure = new("object disposal");
		public int Creations, Moves, Currents, Disposals, Formats;
	}
	private sealed class CustomEnumeratedObjectValue(ObjectJoinCallbackState state, string text, bool first)
	{
		public override string ToString()
		{
			GC.Collect(); state.Formats++;
			if ((state.Fault == 10 || state.Fault == 12) && first || state.Fault == 11 && !first) throw state.Failure;
			return text;
		}
	}
	private sealed class CustomObjectJoinSource(ObjectJoinCallbackState state, object?[] values) : IEnumerable<object?>
	{
		public IEnumerator<object?> GetEnumerator()
		{
			GC.Collect(); state.Creations++;
			if (state.Fault == 1) throw state.Failure;
			return state.Fault == 8 ? null! : new CustomObjectJoinIterator(state, values);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private sealed class CustomObjectJoinIterator(ObjectJoinCallbackState state, object?[] values) : IEnumerator<object?>
	{
		private int _index = -1;
		public object? Current
		{
			get
			{
				GC.Collect(); state.Currents++;
				if (state.Fault == 3 || state.Fault == 5 && _index == 1) throw state.Failure;
				return values[_index];
			}
		}
		object? IEnumerator.Current => Current;
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
			if (state.Fault == 7 || state.Fault == 12) throw state.DisposalFailure;
		}
		public void Reset() => throw new NotSupportedException();
	}
	private sealed class CustomObjectAdapterSource(CustomObjectJoinSource source) : IEnumerable<object?>
	{
		public IEnumerator<object?> GetEnumerator() => ShadowObjectJoinEnumeration.GetEnumerator(source);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private static object?[] CreateCustomEnumeratedObjectValues(ObjectJoinCallbackState state) =>
		[new CustomEnumeratedObjectValue(state, "A\u03A9\0", true), null, 42, new CustomEnumeratedObjectValue(state, "Z\uD800", false)];
	private static void AppendCustomObjectJoin(StringBuilder builder, IEnumerable<object?> source, bool text)
	{
		if (text) builder.AppendJoin("::", source); else builder.AppendJoin('|', source);
	}
	public static int CoreLibObjectCustomCallbackContractsEntry() => ObjectCustomCallbackContracts(false);
	public static int ManagedObjectCustomCallbackContractsEntry() => ObjectCustomCallbackContracts(true);
	private static int ObjectCustomCallbackContracts(bool adapter)
	{
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 0; capacity < 2; capacity++)
		for (var fault = 0; fault < 13; fault++)
		{
			var state = new ObjectJoinCallbackState(fault);
			var source = new CustomObjectJoinSource(state, CreateCustomEnumeratedObjectValues(state));
			IEnumerable<object?> enumeration = adapter ? new CustomObjectAdapterSource(source) : source;
			var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
			var snapshot = builder.ToString();
			Exception? caught = null;
			try { AppendCustomObjectJoin(builder, enumeration, separator != 0); }
			catch (Exception exception) { caught = exception; }
			if (fault is 0 or 9 ? caught != null : fault == 8 ? caught is not NullReferenceException :
				!ReferenceEquals(caught, fault is 7 or 12 ? state.DisposalFailure : state.Failure)) return 100 + fault;
			var delimiter = separator == 0 ? "|" : "::";
			var complete = separator == 0 ? "seedA\u03A9\0||42|Z\uD800" : "seedA\u03A9\0::::42::Z\uD800";
			var expected = fault is 0 or 6 ? complete : fault is 4 or 7 ? "seedA\u03A9\0" :
				fault == 5 ? "seedA\u03A9\0" + delimiter : fault == 11 ?
				(separator == 0 ? "seedA\u03A9\0||42|" : "seedA\u03A9\0::::42::") : "seed";
			GC.Collect();
			if (builder.ToString() != expected || snapshot != "seed") return 200 + fault;
			var moves = fault is 1 or 8 ? 0 : fault is 2 or 3 or 9 or 10 or 12 ? 1 : fault is 4 or 5 or 7 ? 2 : fault == 11 ? 4 : 5;
			var currents = fault is 1 or 2 or 8 or 9 ? 0 : fault is 3 or 4 or 7 or 10 or 12 ? 1 : fault == 5 ? 2 : 4;
			var formats = fault is 1 or 2 or 3 or 8 or 9 ? 0 : fault is 4 or 5 or 7 or 10 or 12 ? 1 : 2;
			if (state.Creations != 1 || state.Moves != moves || state.Currents != currents ||
				state.Disposals != (fault is 1 or 8 ? 0 : 1) || state.Formats != formats) return 300 + fault;
			builder.Clear().Append("seed");
			var retry = new ObjectJoinCallbackState(0);
			AppendCustomObjectJoin(builder, new CustomObjectJoinSource(retry, CreateCustomEnumeratedObjectValues(retry)), separator != 0);
			if (builder.ToString() != complete || retry.Disposals != 1 || snapshot != "seed") return 400 + fault;
			builder.Clear().Append("tail");
			if (builder.ToString() != "tail") return 500 + fault;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ShadowObjectJoinEnumerator CreateOwnedCustomObjectAdapter(ObjectJoinCallbackState state)
	{
		var values = new object?[2];
		values[0] = new StringBuilder(1).Append("A\u03A9\0").ToString();
		values[1] = new StringBuilder(1).Append("Z\uD800").ToString();
		return ShadowObjectJoinEnumeration.GetEnumerator(new CustomObjectJoinSource(state, values));
	}
	public static int CoreLibObjectCustomIteratorOwnershipEntry()
	{
		var state = new ObjectJoinCallbackState(0);
		var adapter = CreateOwnedCustomObjectAdapter(state);
		GC.Collect();
		if (!adapter.MoveNext() || adapter.Current is not string first || first != "A\u03A9\0") return 1;
		if (!adapter.MoveNext() || adapter.Current is not string last || last != "Z\uD800") return 2;
		if (adapter.MoveNext()) return 3;
		adapter.Dispose(); adapter.Dispose();
		GC.Collect();
		if (adapter.MoveNext() || adapter.Current != null) return 4;
		return state.Creations == 1 && state.Moves == 3 && state.Currents == 2 && state.Disposals == 1 ? 42 : 5;
	}
	public static int CoreLibObjectAdapterAllocationCleanupEntry()
	{
		var values = new object?[0];
		for (var mode = 0; mode < 2; mode++)
		for (var failAt = 1; failAt <= 3; failAt++)
		{
			var state = new ObjectJoinCallbackState(mode == 0 ? 0 : 6);
			var source = new CustomObjectJoinSource(state, values);
			ShadowObjectJoinEnumerator? adapter = null;
			Exception? caught = null;
			SetStringBuilderAllocationFailure(failAt);
			try { adapter = ShadowObjectJoinEnumeration.GetEnumerator(source); }
			catch (Exception exception) { caught = exception; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (failAt == 3 ? caught != null : failAt == 2 && mode != 0 ? !ReferenceEquals(caught, state.Failure) : caught is not OutOfMemoryException) return 100 + failAt;
			if (state.Creations != 1 || state.Moves != 0 || state.Currents != 0 || state.Disposals != (failAt == 2 ? 1 : 0)) return 200 + failAt;
			if (failAt != 3) continue;
			caught = null;
			try { adapter!.Dispose(); } catch (Exception exception) { caught = exception; }
			if (mode == 0 ? caught != null : !ReferenceEquals(caught, state.Failure)) return 300;
			adapter!.Dispose();
			if (adapter.Current != null || adapter.MoveNext() || state.Disposals != 1) return 400;
		}
		return 42;
	}
	public static int CoreLibObjectCustomEnumerableAllocationEntry()
	{
		var values = CreateCustomEnumeratedObjectValues(new ObjectJoinCallbackState(0));
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 0; capacity < 2; capacity++)
		{
			var expected = separator == 0 ? "seedA\u03A9\0||42|Z\uD800" : "seedA\u03A9\0::::42::Z\uD800";
			var succeeded = false;
			for (var failAt = 1; failAt <= 12; failAt++)
			{
				var state = new ObjectJoinCallbackState(0);
				var source = new CustomObjectJoinSource(state, values);
				var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
				var snapshot = builder.ToString();
				var threw = false;
				SetStringBuilderAllocationFailure(failAt);
				try { AppendCustomObjectJoin(builder, source, separator != 0); }
				catch (OutOfMemoryException) { threw = true; }
				finally { SetStringBuilderAllocationFailure(0); }
				var failed = builder.ToString();
				if (state.Creations != 1 || state.Disposals != (failAt == 1 ? 0 : 1) || snapshot != "seed") return 100 + failAt;
				if (threw ? !expected.StartsWith(failed, StringComparison.Ordinal) || failed.Length < 4 : failed != expected) return 200 + failAt;
				if (failAt <= 2 && (failed != "seed" || state.Moves != 0 || state.Currents != 0)) return 300 + failAt;
				builder.Clear().Append("seed");
				var retry = new ObjectJoinCallbackState(0);
				AppendCustomObjectJoin(builder, new CustomObjectJoinSource(retry, values), separator != 0);
				if (builder.ToString() != expected || retry.Disposals != 1 || snapshot != "seed") return 400 + failAt;
				if (!threw) { succeeded = true; break; }
			}
			if (!succeeded) return 500;
		}
		return 42;
	}
}
