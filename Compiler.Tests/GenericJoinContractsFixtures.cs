/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Runtime.CompilerServices;
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static void AppendGenericJoin<T>(StringBuilder builder, IEnumerable<T> source, bool text)
	{
		if (text) builder.AppendJoin("::", source); else builder.AppendJoin('|', source);
	}
	private static int GenericJoinCallbacks<T>(T first, T second, string firstText, string secondText)
	{
		T[] values = new T[2]; values[0] = first; values[1] = second;
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 1; capacity <= 192; capacity += 191)
		for (var fault = 0; fault < 10; fault++)
		{
			var state = new GenericJoinState(fault);
			var builder = new StringBuilder(capacity).Append("seed");
			var snapshot = builder.ToString();
			Exception? caught = null;
			try { AppendGenericJoin(builder, new GenericJoinSource<T>(values, state), separator != 0); }
			catch (Exception exception) { caught = exception; }
			if (fault is 0 or 9 ? caught != null : fault == 8 ? caught is not NullReferenceException :
				!ReferenceEquals(caught, fault == 7 ? state.DisposalFailure : state.Failure)) return 100 + fault;
			var delimiter = separator == 0 ? "|" : "::";
			var complete = "seed" + firstText + delimiter + secondText;
			var expected = fault is 0 or 6 ? complete : fault is 4 or 7 ? "seed" + firstText : fault == 5 ? "seed" + firstText + delimiter : "seed";
			GC.Collect();
			if (builder.ToString() != expected || snapshot != "seed") return 200 + fault;
			var moves = fault is 1 or 8 ? 0 : fault is 2 or 3 or 9 ? 1 : fault is 4 or 5 or 7 ? 2 : 3;
			var currents = fault is 1 or 2 or 8 or 9 ? 0 : fault is 3 or 4 or 7 ? 1 : 2;
			if (state.Creations != 1 || state.Moves != moves || state.Currents != currents || state.Disposals != (fault is 1 or 8 ? 0 : 1)) return 300 + fault;
			var retry = new GenericJoinState(); builder.Clear().Append("seed");
			AppendGenericJoin(builder, new GenericJoinSource<T>(values, retry), separator != 0);
			if (builder.ToString() != complete || retry.Disposals != 1 || snapshot != "seed") return 400 + fault;
		}
		return 42;
	}
	public static int CoreLibGenericJoinCallbackContractsEntry()
	{
		if (GenericJoinCallbacks<byte>(0, 255, "0", "255") != 42) return 1;
		if (GenericJoinCallbacks<long>(long.MinValue, long.MaxValue, "-9223372036854775808", "9223372036854775807") != 42) return 2;
		if (GenericJoinCallbacks(JoinSByte.Named, (JoinSByte)127, "Named", "127") != 42) return 3;
		return GenericJoinCallbacks(JoinInt64.Named, JoinInt64.First | JoinInt64.Second, "Named", "First, Second");
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerator<T> CreateOwnedGenericJoin<T>(T first, T second, GenericJoinState state)
	{
		var values = new T[2]; values[0] = first; values[1] = second;
		return ShadowGenericJoinEnumeration.GetEnumerator(new GenericJoinSource<T>(values, state));
	}
	private static int GenericJoinOwnership<T>(T first, T second, string expected)
	{
		var state = new GenericJoinState(); var adapter = CreateOwnedGenericJoin(first, second, state); GC.Collect();
		var builder = new StringBuilder();
		if (!adapter.MoveNext()) return 1;
		builder.AppendFormat("{0}", adapter.Current);
		GC.Collect();
		if (!adapter.MoveNext()) return 2;
		builder.Append('|').AppendFormat("{0}", adapter.Current);
		if (adapter.MoveNext() || builder.ToString() != expected) return 3;
		adapter.Dispose(); adapter.Dispose(); GC.Collect();
		if (adapter.MoveNext()) return 4;
		builder.Clear().AppendFormat("{0}", adapter.Current);
		if (builder.ToString() != "0") return 6;
		return state.Creations == 1 && state.Moves == 3 && state.Currents == 2 && state.Disposals == 1 ? 42 : 5;
	}
	public static int CoreLibGenericJoinIteratorOwnershipEntry()
	{
		if (GenericJoinOwnership<long>(long.MinValue, long.MaxValue, "-9223372036854775808|9223372036854775807") != 42) return 1;
		return GenericJoinOwnership(JoinInt64.Named, JoinInt64.First | JoinInt64.Second, "Named|First, Second");
	}
	public static int CoreLibGenericJoinAdapterAllocationCleanupEntry()
	{
		var values = new JoinInt64[0];
		for (var mode = 0; mode < 2; mode++)
		for (var failAt = 1; failAt <= 3; failAt++)
		{
			var state = new GenericJoinState(mode == 0 ? 0 : 6);
			var source = new GenericJoinSource<JoinInt64>(values, state);
			IEnumerator<JoinInt64>? adapter = null; Exception? caught = null;
			SetStringBuilderAllocationFailure(failAt);
			try { adapter = ShadowGenericJoinEnumeration.GetEnumerator(source); }
			catch (Exception exception) { caught = exception; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (failAt == 3 ? caught != null : failAt == 2 && mode != 0 ? !ReferenceEquals(caught, state.Failure) : caught is not OutOfMemoryException) return 100 + failAt;
			if (state.Creations != 1 || state.Moves != 0 || state.Currents != 0 || state.Disposals != (failAt == 2 ? 1 : 0)) return 200 + failAt;
			if (failAt != 3) continue;
			caught = null; try { adapter!.Dispose(); } catch (Exception exception) { caught = exception; }
			if (mode == 0 ? caught != null : !ReferenceEquals(caught, state.Failure)) return 300;
			adapter!.Dispose();
			if (adapter.Current != 0 || adapter.MoveNext() || state.Disposals != 1) return 400;
		}
		return 42;
	}
	public static int CoreLibGenericJoinEnumerableAllocationEntry()
	{
		var values = new JoinInt64[2]; values[0] = JoinInt64.Named; values[1] = JoinInt64.First | JoinInt64.Second;
		for (var separator = 0; separator < 2; separator++)
		for (var capacity = 1; capacity <= 192; capacity += 191)
		{
			var expected = separator == 0 ? "seedNamed|First, Second" : "seedNamed::First, Second";
			var succeeded = false;
			for (var failAt = 1; failAt <= 64; failAt++)
			{
				var state = new GenericJoinState(); var source = new GenericJoinSource<JoinInt64>(values, state);
				var builder = new StringBuilder(capacity).Append("seed"); var snapshot = builder.ToString();
				var threw = false; SetStringBuilderAllocationFailure(failAt);
				try { AppendGenericJoin(builder, source, separator != 0); } catch (OutOfMemoryException) { threw = true; }
				finally { SetStringBuilderAllocationFailure(0); }
				var failed = builder.ToString();
				if (state.Creations != 1 || state.Disposals != (failAt == 1 ? 0 : 1) || snapshot != "seed") return 100 + failAt;
				if (threw ? !expected.StartsWith(failed, StringComparison.Ordinal) || failed.Length < 4 : failed != expected) return 200 + failAt;
				if (failAt <= 2 && (failed != "seed" || state.Moves != 0 || state.Currents != 0)) return 300 + failAt;
				builder.Clear().Append("seed"); var retry = new GenericJoinState();
				AppendGenericJoin(builder, new GenericJoinSource<JoinInt64>(values, retry), separator != 0);
				if (builder.ToString() != expected || retry.Disposals != 1 || snapshot != "seed") return 400 + failAt;
				if (!threw) { succeeded = true; break; }
			}
			if (!succeeded) return 500;
		}
		return 42;
	}
}
