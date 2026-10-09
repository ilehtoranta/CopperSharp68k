/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibWideNullableTransportEntry()
	{
		long? signed = long.MinValue;
		ulong? unsigned = ulong.MaxValue;
		long? empty = null;
		if (!signed.HasValue || signed.Value != long.MinValue || unsigned.Value != ulong.MaxValue) return 1;
		if (empty.HasValue || empty.GetValueOrDefault() != 0 || empty.GetValueOrDefault(0x123456789abcdefL) != 0x123456789abcdefL) return 2;
		if (signed.GetValueOrDefault(7) != long.MinValue || unsigned.GetValueOrDefault(7) != ulong.MaxValue) return 3;
		long? pattern = 0x123456789abcdefL;
		if (pattern.GetValueOrDefault() != 0x123456789abcdefL) return 301;
		var values = new long?[2]; values[0] = signed; values[1] = 0x123456789abcdefL;
		if (values[1].GetValueOrDefault() != 0x123456789abcdefL) return 302;
		var copy = values[1];
		if (!copy.HasValue) return 401;
		if (copy.GetValueOrDefault() != 0x123456789abcdefL) return 402;
		values[1] = null; GC.Collect();
		if (!copy.HasValue) return 411;
		if (copy.GetValueOrDefault() != 0x123456789abcdefL) return 412;
		if (!values[0].HasValue || values[0].GetValueOrDefault() != long.MinValue) return 43;
		if (values[1].HasValue) return 44;
		object? signedBox = signed; object? unsignedBox = unsigned; object? emptyBox = empty; GC.Collect();
		if (signedBox is not long s || s != long.MinValue || unsignedBox is not ulong u || u != ulong.MaxValue || emptyBox != null) return 5;
		JoinInt64? signedEnum = JoinInt64.Named; JoinUInt64? unsignedEnum = JoinUInt64.Named;
		object? enumBox = unsignedEnum; GC.Collect();
		if (signedEnum.Value != JoinInt64.Named || enumBox is not JoinUInt64 e || e != JoinUInt64.Named) return 6;
		return 42;
	}

	public static int CoreLibGenericWideNullableJoinsEntry()
	{
		var result = CheckSmallNullableJoin(long.MinValue, long.MaxValue, "-9223372036854775808", "9223372036854775807"); if (result != 42) return result;
		result = CheckSmallNullableJoin(0UL, ulong.MaxValue, "0", "18446744073709551615"); if (result != 42) return 1000 + result;
		result = CheckSmallNullableJoin(JoinInt64.Named, (JoinInt64)long.MaxValue, "Named", "9223372036854775807"); if (result != 42) return 2000 + result;
		result = CheckSmallNullableJoin(JoinUInt64.Named, (JoinUInt64)0, "Named", "0"); return result == 42 ? 42 : 3000 + result;
	}

	public static int CoreLibGenericWideNullableCallbacksEntry()
	{
		if (GenericJoinCallbacks<long?>(long.MinValue, null, "-9223372036854775808", "") != 42) return 1;
		if (GenericJoinCallbacks<ulong?>(null, ulong.MaxValue, "", "18446744073709551615") != 42) return 2;
		if (GenericJoinCallbacks<JoinInt64?>(JoinInt64.Named, null, "Named", "") != 42) return 3;
		if (GenericJoinCallbacks<JoinUInt64?>(null, JoinUInt64.Named, "", "Named") != 42) return 4;
		if (CheckStructGenericJoin<long?>(long.MinValue, null, "-9223372036854775808", "") != 42) return 5;
		if (CheckStructGenericJoin<ulong?>(null, ulong.MaxValue, "", "18446744073709551615") != 42) return 6;
		if (CheckStructGenericJoin<JoinInt64?>(JoinInt64.Named, null, "Named", "") != 42) return 7;
		return CheckStructGenericJoin<JoinUInt64?>(null, JoinUInt64.Named, "", "Named");
	}

	public static int CoreLibGenericWideNullableOwnershipEntry()
	{
		var result = CheckSmallNullableOwnership(0x123456789abcdefL, "81985529216486895"); if (result != 42) return result;
		result = CheckSmallNullableOwnership(ulong.MaxValue, "18446744073709551615"); if (result != 42) return 100 + result;
		result = CheckSmallNullableOwnership(JoinInt64.Named, "Named"); if (result != 42) return 200 + result;
		return CheckSmallNullableOwnership(JoinUInt64.Named, "Named");
	}

	public static int CoreLibGenericWideNullableCapacityEntry()
	{
		if (BooleanCharJoinCapacity<long?>(long.MinValue, null) != 42) return 1;
		if (BooleanCharJoinCapacity<ulong?>(ulong.MaxValue, null) != 42) return 2;
		if (BooleanCharJoinCapacity<JoinInt64?>(JoinInt64.Named, null) != 42) return 3;
		if (BooleanCharJoinCapacity<JoinUInt64?>(JoinUInt64.Named, null) != 42) return 4;
		if (NullableJoinLeadingEmptyCapacity(long.MinValue) != 42) return 5;
		if (NullableJoinLeadingEmptyCapacity(ulong.MaxValue) != 42) return 6;
		if (NullableJoinLeadingEmptyCapacity(JoinInt64.Named) != 42) return 7;
		return NullableJoinLeadingEmptyCapacity(JoinUInt64.Named);
	}

	public static int CoreLibGenericWideNullableAllocationEntry()
	{
		if (BooleanCharJoinAllocation<long?>(null, 0x123456789abcdefL, "", "81985529216486895") != 42) return 1;
		return BooleanCharJoinAllocation<JoinUInt64?>(JoinUInt64.Named, null, "Named", "");
	}
}
