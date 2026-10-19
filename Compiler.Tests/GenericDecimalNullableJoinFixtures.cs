/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibDecimalNullableTransportEntry()
	{
		var values = new decimal?[4];
		values[0] = new decimal(0x12345678, 0x13579bdf, 0x2468ace0, true, 7);
		values[1] = new decimal(-1, -1, -1, false, 0);
		values[2] = new decimal(0, 0, 0, true, 28);
		values[3] = new decimal(1234500, 0, 0, false, 4);
		for (var index = 0; index < values.Length; index++)
		{
			var expected = decimal.GetBits(values[index]!.Value);
			var copy = values[index]; values[index] = null; GC.Collect();
			if (!copy.HasValue || values[index].HasValue) return 100 + index;
			if (!SameNullableDecimalBits(copy.Value, expected) || !SameNullableDecimalBits(copy.GetValueOrDefault(), expected) ||
				!SameNullableDecimalBits(copy.GetValueOrDefault(new decimal(1)), expected)) return 200 + index;
			object? boxed = copy; GC.Collect();
			if (boxed is not decimal unboxed || !SameNullableDecimalBits(unboxed, expected)) return 300 + index;
		}
		decimal? empty = null;
		var fallback = new decimal(0x12345678, 0x13579bdf, 0x2468ace0, true, 7);
		if (!SameNullableDecimalBits(empty.GetValueOrDefault(), decimal.GetBits(new decimal(0))) ||
			!SameNullableDecimalBits(empty.GetValueOrDefault(fallback), decimal.GetBits(fallback))) return 1;
		object? emptyBox = empty;
		return emptyBox == null ? 42 : 2;
	}

	private static bool SameNullableDecimalBits(decimal value, int[] expected)
	{
		var actual = decimal.GetBits(value);
		return actual[0] == expected[0] && actual[1] == expected[1] && actual[2] == expected[2] && actual[3] == expected[3];
	}

	public static int CoreLibDecimalNullableReuseEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var value = new decimal(1234500, 0, 0, false, 4);
			var expected = decimal.GetBits(value);
			var values = new decimal?[2]; values[1] = value;
			var builder = new System.Text.StringBuilder(1).Append('[').AppendJoin("|", values).Append(']'); GC.Collect();
			var snapshot = builder.ToString();
			if (snapshot != "[|123.4500]") return 1;
			if (!values[1].HasValue || !SameNullableDecimalBits(values[1]!.Value, expected)) return 2;
			builder.Clear().AppendJoin('|', values); GC.Collect();
			if (!values[1].HasValue || !SameNullableDecimalBits(values[1]!.Value, expected)) return 3;
			var text = builder.ToString();
			if (text.Length != 9) return 100 + text.Length;
			var expectedText = "|123.4500";
			for (var index = 0; index < text.Length; index++)
				if (text[index] != expectedText[index]) return 10000 + index * 1000 + text[index];
			return snapshot == "[|123.4500]" ? 42 : 4;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericDecimalNullableJoinsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var result = CheckSmallNullableJoin(new decimal(1234500, 0, 0, false, 4), new decimal(0, 0, 0, true, 4), "123.4500", "0.0000");
			if (result != 42) return result;
			return CheckSmallNullableJoin(new decimal(-1, -1, -1, false, 0), new decimal(-1, -1, -1, true, 0), "79228162514264337593543950335", "-79228162514264337593543950335");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericDecimalNullableCallbacksEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var value = new decimal(1234500, 0, 0, true, 4);
			if (GenericJoinCallbacks<decimal?>(value, null, "-123.4500", "") != 42) return 1;
			return CheckStructGenericJoin<decimal?>(null, value, "", "-123.4500");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericDecimalNullableOwnershipEntry()
	{
		for (var index = 0; index < 4; index++)
		{
			var value = index == 0 ? new decimal(0x12345678, 0x13579bdf, 0x2468ace0, true, 7) :
				index == 1 ? new decimal(-1, -1, -1, false, 0) :
				index == 2 ? new decimal(0, 0, 0, true, 28) : new decimal(1234500, 0, 0, false, 4);
			var expected = decimal.GetBits(value);
			var values = new decimal?[2]; values[0] = value;
			IEnumerator<decimal?> iterator = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(values);
			if (!iterator.MoveNext()) return 100 + index;
			var held = iterator.Current; values[0] = null; GC.Collect();
			if (!held.HasValue || !iterator.Current.HasValue || !SameNullableDecimalBits(held.Value, expected) ||
				!SameNullableDecimalBits(iterator.Current.GetValueOrDefault(), expected)) return 200 + index;
			object? box = held; GC.Collect();
			if (box is not decimal payload || !SameNullableDecimalBits(payload, expected)) return 300 + index;
			iterator.Dispose(); iterator.Dispose(); GC.Collect();
			if (iterator.Current.HasValue || iterator.MoveNext() || !SameNullableDecimalBits(held.GetValueOrDefault(), expected)) return 400 + index;
		}
		return 42;
	}

	public static int CoreLibGenericDecimalNullableCapacityEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			if (BooleanCharJoinCapacity<decimal?>(new decimal(1234500, 0, 0, true, 4), null) != 42) return 1;
			return NullableJoinLeadingEmptyCapacity(new decimal(-1, -1, -1, false, 0));
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericDecimalNullableAllocationEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			if (BooleanCharJoinAllocation<decimal?>(null, new decimal(1234500, 0, 0, true, 4), "", "-123.4500") != 42) return 1;
			return BooleanCharJoinAllocation<decimal?>(new decimal(0, 0, 0, true, 28), null, "0.0000000000000000000000000000", "");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericDecimalNullableCultureEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = new CultureInfo("");
			culture.NumberFormat.NegativeSign = "n"; culture.NumberFormat.NumberDecimalSeparator = ":";
			CultureInfo.CurrentCulture = culture; culture = null!; GC.Collect();
			var value = new decimal(1234500, 0, 0, true, 4);
			if (CheckGenericJoin<decimal?>(value, null, "[n123:4500|]") != 42) return 1;
			if (CheckGenericJoin<decimal?>(null, value, "[|n123:4500]") != 42) return 2;
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; GC.Collect();
			return CheckGenericJoin<decimal?>(value, null, "[-123.4500|]");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
