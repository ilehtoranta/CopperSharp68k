/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibSmallNullableEnumTransportEntry()
	{
		JoinSByte? value = JoinSByte.Named;
		if (!value.HasValue || value.Value != JoinSByte.Named) return 1;
		if (value.ToString() != "Named") return 2;
		object? box = value; GC.Collect();
		if (box is not JoinSByte scalar || scalar != JoinSByte.Named) return 3;
		var values = new JoinSByte?[2]; values[0] = value;
		var builder = new System.Text.StringBuilder(192).AppendJoin('|', values);
		return builder.ToString() == "Named|" ? 42 : 4;
	}
	private static int CheckSmallNullableJoin<T>(T first, T second, string firstText, string secondText) where T : struct
	{
		var empty = CheckGenericJoin<T?>(null, null, "[|]"); if (empty != 42) return 100 + empty;
		var leading = CheckGenericJoin<T?>(null, first, "[|" + firstText + "]"); if (leading != 42) return 200 + leading;
		var trailing = CheckGenericJoin<T?>(second, null, "[" + secondText + "|]"); if (trailing != 42) return 300 + trailing;
		var prefix = "[" + firstText + "|"; var suffix = secondText + "]";
		var present = CheckGenericJoin<T?>(first, second, prefix + suffix);
		return present == 42 ? 42 : 400 + present;
	}
	public static int CoreLibGenericSmallNullablePrimitiveJoinsEntry()
	{
		var result = CheckSmallNullableJoin(false, true, "False", "True"); if (result != 42) return result;
		result = CheckSmallNullableJoin('\0', '\uD800', "\0", "\uD800"); if (result != 42) return 1000 + result;
		result = CheckSmallNullableJoin(sbyte.MinValue, sbyte.MaxValue, "-128", "127"); if (result != 42) return 2000 + result;
		result = CheckSmallNullableJoin((byte)0, byte.MaxValue, "0", "255"); if (result != 42) return 3000 + result;
		result = CheckSmallNullableJoin(short.MinValue, short.MaxValue, "-32768", "32767"); if (result != 42) return 4000 + result;
		result = CheckSmallNullableJoin((ushort)0, ushort.MaxValue, "0", "65535");
		return result == 42 ? 42 : 5000 + result;
	}
	public static int CoreLibGenericSmallNullableEnumJoinsEntry()
	{
		var result = CheckSmallNullableJoin(JoinSByte.Named, (JoinSByte)127, "Named", "127"); if (result != 42) return result;
		result = CheckSmallNullableJoin(JoinByte.Named, (JoinByte)0, "Named", "0"); if (result != 42) return 1000 + result;
		result = CheckSmallNullableJoin(JoinInt16.Named, (JoinInt16)32767, "Named", "32767"); if (result != 42) return 2000 + result;
		result = CheckSmallNullableJoin(JoinUInt16.Named, (JoinUInt16)0, "Named", "0"); if (result != 42) return 3000 + result;
		result = CheckSmallNullableJoin(JoinInt32.Named, (JoinInt32)int.MaxValue, "Named", "2147483647"); if (result != 42) return 4000 + result;
		result = CheckSmallNullableJoin(JoinUInt32.Named, (JoinUInt32)0, "Named", "0");
		return result == 42 ? 42 : 5000 + result;
	}
	public static int CoreLibGenericSmallNullableCallbacksEntry()
	{
		if (GenericJoinCallbacks<bool?>(false, null, "False", "") != 42) return 1;
		if (GenericJoinCallbacks<char?>(null, '\uD800', "", "\uD800") != 42) return 2;
		if (GenericJoinCallbacks<sbyte?>(sbyte.MinValue, null, "-128", "") != 42) return 3;
		if (GenericJoinCallbacks<JoinUInt16?>(null, JoinUInt16.Named, "", "Named") != 42) return 4;
		if (CheckStructGenericJoin<bool?>(false, null, "False", "") != 42) return 5;
		if (CheckStructGenericJoin<char?>(null, '\uD800', "", "\uD800") != 42) return 6;
		if (CheckStructGenericJoin<sbyte?>(sbyte.MinValue, null, "-128", "") != 42) return 7;
		return CheckStructGenericJoin<JoinUInt16?>(null, JoinUInt16.Named, "", "Named");
	}
	private static int CheckSmallNullableOwnership<T>(T first, string text) where T : struct
	{
		var values = new T?[2]; values[0] = first;
		IEnumerator<T?> iterator = ShadowGenericJoinEnumeration.GetEnumerator(values);
		if (!iterator.MoveNext()) return 1;
		var held = iterator.Current; values[0] = null; GC.Collect();
		var current = iterator.Current;
		if (!held.HasValue || !current.HasValue || held.Value.ToString() != text || current.Value.ToString() != text) return 2;
		object? box = held; object? empty = (T?)null; GC.Collect();
		if (box is not T payload || payload.ToString() != text || empty != null) return 3;
		iterator.Dispose(); iterator.Dispose(); GC.Collect();
		if (iterator.Current.HasValue || iterator.MoveNext() || !held.HasValue || held.Value.ToString() != text) return 4;
		return 42;
	}
	public static int CoreLibGenericSmallNullableOwnershipEntry()
	{
		var result = CheckSmallNullableOwnership(false, "False"); if (result != 42) return result;
		result = CheckSmallNullableOwnership('\0', "\0"); if (result != 42) return 100 + result;
		result = CheckSmallNullableOwnership(sbyte.MinValue, "-128"); if (result != 42) return 200 + result;
		result = CheckSmallNullableOwnership(JoinByte.Named, "Named"); return result == 42 ? 42 : 300 + result;
	}
	public static int CoreLibGenericSmallNullableCapacityEntry()
	{
		if (BooleanCharJoinCapacity<bool?>(false, null) != 42) return 1;
		if (BooleanCharJoinCapacity<char?>('\uFFFF', null) != 42) return 2;
		if (BooleanCharJoinCapacity<JoinUInt16?>(JoinUInt16.Named, null) != 42) return 3;
		if (NullableJoinLeadingEmptyCapacity(false) != 42) return 4;
		if (NullableJoinLeadingEmptyCapacity('\uFFFF') != 42) return 5;
		return NullableJoinLeadingEmptyCapacity(JoinUInt16.Named);
	}
	public static int CoreLibGenericSmallNullableAllocationEntry()
	{
		var result = BooleanCharJoinAllocation<char?>(null, '\0', "", "\0"); if (result != 42) return result;
		return BooleanCharJoinAllocation<JoinByte?>(JoinByte.Named, null, "Named", "");
	}
}
