/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibGenericNullableIntJoinsEntry()
	{
		if (CheckGenericJoin<int?>(null, null, "[|]") != 42) return 1;
		var mixed = CheckGenericJoin<int?>(null, int.MinValue, "[|-2147483648]");
		if (mixed != 42) return 200 + mixed;
		if (CheckGenericJoin<int?>(int.MaxValue, null, "[2147483647|]") != 42) return 3;
		if (CheckGenericJoin<int?>(0, -7, "[0|-7]") != 42) return 4;
		if (CheckGenericJoin<uint?>(null, null, "[|]") != 42) return 5;
		if (CheckGenericJoin<uint?>(null, uint.MaxValue, "[|4294967295]") != 42) return 6;
		if (CheckGenericJoin<uint?>(uint.MaxValue, null, "[4294967295|]") != 42) return 7;
		if (CheckGenericJoin<uint?>(0, 2147483648u, "[0|2147483648]") != 42) return 8;
		return 42;
	}
	public static int CoreLibGenericNullableCallbackEntry()
	{
		if (GenericJoinCallbacks<int?>(null, int.MinValue, "", "-2147483648") != 42) return 1;
		if (GenericJoinCallbacks<int?>(int.MaxValue, null, "2147483647", "") != 42) return 2;
		if (GenericJoinCallbacks<uint?>(null, uint.MaxValue, "", "4294967295") != 42) return 3;
		if (GenericJoinCallbacks<uint?>(uint.MaxValue, null, "4294967295", "") != 42) return 4;
		if (CheckStructGenericJoin<int?>(null, int.MinValue, "", "-2147483648") != 42) return 5;
		if (CheckStructGenericJoin<int?>(int.MaxValue, null, "2147483647", "") != 42) return 6;
		if (CheckStructGenericJoin<uint?>(null, uint.MaxValue, "", "4294967295") != 42) return 7;
		return CheckStructGenericJoin<uint?>(uint.MaxValue, null, "4294967295", "");
	}
	public static int CoreLibGenericNullableOwnershipEntry()
	{
		var state = new GenericJoinState();
		var iterator = CreateOwnedGenericJoin<int?>(31, null, state); GC.Collect();
		if (!iterator.MoveNext()) return 1;
		var first = iterator.Current; GC.Collect();
		if (!first.HasValue || first.Value != 31) return 2;
		if (!iterator.MoveNext() || iterator.Current.HasValue || iterator.MoveNext()) return 3;
		iterator.Dispose(); iterator.Dispose(); GC.Collect();
		if (iterator.Current.HasValue || iterator.MoveNext() || !first.HasValue || first.Value != 31) return 4;
		object? box = first; object? empty = (int?)null; GC.Collect();
		if (box is not int number || number != 31 || empty != null) return 5;
		if (state.Creations != 1 || state.Moves != 3 || state.Currents != 2 || state.Disposals != 1) return 6;
		var values = new uint?[2]; values[0] = uint.MaxValue;
		IEnumerator<uint?> array = ShadowGenericJoinEnumeration.GetEnumerator(values);
		if (!array.MoveNext()) return 7;
		var held = array.Current; values[0] = null; GC.Collect();
		var retained = array.Current;
		if (!held.HasValue || held.Value != uint.MaxValue || !retained.HasValue || retained.Value != uint.MaxValue) return 8;
		if (!array.MoveNext() || array.Current.HasValue || array.MoveNext()) return 9;
		array.Dispose(); GC.Collect();
		return !array.Current.HasValue && held.Value == uint.MaxValue ? 42 : 10;
	}
	private static int NullableJoinLeadingEmptyCapacity<T>(T present) where T : struct
	{
		var values = new T?[2]; values[1] = present;
		for (var boxed = 0; boxed < 2; boxed++)
		for (var character = 0; character < 2; character++)
		{
			var state = new GenericJoinState();
			IEnumerable<T?> source = boxed == 0 ? new GenericJoinSource<T?>(values, state) : new StructGenericJoinSource<T?>(values, state);
			var builder = new StringBuilder(1, 4).Append("seed"); var snapshot = builder.ToString();
			try { if (character == 0) builder.AppendJoin("|", source); else builder.AppendJoin('|', source); return 1; }
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount" || error.ActualValue != null) return 2; }
			if (builder.ToString() != "seed" || builder.Capacity != 4 || snapshot != "seed") return 3;
			if (state.Creations != 1 || state.Moves != 2 || state.Currents != 1 || state.Disposals != 1) return 4;
			builder.Clear(); var retry = new GenericJoinState();
			builder.AppendJoin('|', new GenericJoinSource<T?>(new T?[0], retry)); GC.Collect();
			if (builder.Length != 0 || retry.Moves != 1 || retry.Currents != 0 || retry.Disposals != 1 || snapshot != "seed") return 5;
		}
		return 42;
	}
	public static int CoreLibGenericNullableCapacityEntry()
	{
		if (BooleanCharJoinCapacity<int?>(int.MinValue, null) != 42) return 1;
		if (BooleanCharJoinCapacity<uint?>(uint.MaxValue, null) != 42) return 2;
		var signed = NullableJoinLeadingEmptyCapacity(int.MinValue); if (signed != 42) return 100 + signed;
		var unsigned = NullableJoinLeadingEmptyCapacity(uint.MaxValue); return unsigned == 42 ? 42 : 200 + unsigned;
	}
	public static int CoreLibGenericNullableAllocationEntry()
	{
		var signed = BooleanCharJoinAllocation<int?>(null, int.MinValue, "", "-2147483648"); if (signed != 42) return signed;
		var unsigned = BooleanCharJoinAllocation<uint?>(uint.MaxValue, null, "4294967295", ""); if (unsigned != 42) return unsigned;
		int? absent = default; uint? unsignedAbsent = default;
		// Empty nullable boxing must succeed with allocation disabled.
		SetStringBuilderAllocationFailure(1);
		try { object? first = absent; object? second = unsignedAbsent; if (first != null || second != null) return 600; }
		finally { SetStringBuilderAllocationFailure(0); }
		int? present = -7; var threw = false;
		SetStringBuilderAllocationFailure(1);
		try { object? box = present; if (box is not int number || number != -7) return 601; }
		catch (OutOfMemoryException) { threw = true; }
		finally { SetStringBuilderAllocationFailure(0); }
		if (!threw || !present.HasValue || present.Value != -7) return 602;
		object? retry = present; GC.Collect();
		return retry is int value && value == -7 ? 42 : 603;
	}
}
