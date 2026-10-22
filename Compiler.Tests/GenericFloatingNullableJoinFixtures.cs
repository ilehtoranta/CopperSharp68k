/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibFloatingNullableTransportEntry()
	{
		var singles = new float?[8]; var doubles = new double?[8];
		for (var index = 0; index < 8; index++)
		{
			var singleBits = FloatingArraySingleBits(index); var doubleBits = FloatingArrayDoubleBits(index);
			singles[index] = Unsafe.BitCast<uint, float>(singleBits); doubles[index] = Unsafe.BitCast<ulong, double>(doubleBits);
			var single = singles[index]; var wide = doubles[index];
			singles[index] = null; doubles[index] = null; GC.Collect();
			if (!single.HasValue || !wide.HasValue || singles[index].HasValue || doubles[index].HasValue) return 100 + index;
			if (Unsafe.BitCast<float, uint>(single.Value) != singleBits || Unsafe.BitCast<double, ulong>(wide.Value) != doubleBits) return 200 + index;
			if (Unsafe.BitCast<float, uint>(single.GetValueOrDefault()) != singleBits || Unsafe.BitCast<double, ulong>(wide.GetValueOrDefault()) != doubleBits) return 220 + index;
			if (Unsafe.BitCast<float, uint>(single.GetValueOrDefault(1.25f)) != singleBits || Unsafe.BitCast<double, ulong>(wide.GetValueOrDefault(1.25d)) != doubleBits) return 240 + index;
			object? singleBox = single; object? wideBox = wide; GC.Collect();
			if (singleBox is not float s || Unsafe.BitCast<float, uint>(s) != singleBits || wideBox is not double d || Unsafe.BitCast<double, ulong>(d) != doubleBits) return 300 + index;
		}
		float? emptySingle = null; double? emptyDouble = null;
		if (Unsafe.BitCast<float, uint>(emptySingle.GetValueOrDefault()) != 0 || Unsafe.BitCast<double, ulong>(emptyDouble.GetValueOrDefault()) != 0) return 1;
		if (Unsafe.BitCast<float, uint>(emptySingle.GetValueOrDefault(-0f)) != 0x80000000u || Unsafe.BitCast<double, ulong>(emptyDouble.GetValueOrDefault(-0d)) != 0x8000000000000000UL) return 2;
		object? emptySingleBox = emptySingle; object? emptyDoubleBox = emptyDouble;
		return emptySingleBox == null && emptyDoubleBox == null ? 42 : 3;
	}

	public static int CoreLibGenericSingleNullableJoinsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var result = CheckSmallNullableJoin(-0f, float.Epsilon, "-0", "1E-45"); if (result != 42) return result;
			result = CheckSmallNullableJoin(float.MaxValue, float.MinValue, "3.4028235E+38", "-3.4028235E+38"); if (result != 42) return 1000 + result;
			result = CheckSmallNullableJoin(float.PositiveInfinity, float.NegativeInfinity, "Infinity", "-Infinity"); if (result != 42) return 2000 + result;
			result = CheckSmallNullableJoin(Unsafe.BitCast<uint, float>(0x7fc12345u), Unsafe.BitCast<uint, float>(0xffa12345u), "NaN", "NaN"); if (result != 42) return 3000 + result;
			result = CheckSmallNullableJoin(-123.5f, 1.25f, "-123.5", "1.25"); return result == 42 ? 42 : 4000 + result;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericDoubleNullableJoinsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var result = CheckSmallNullableJoin(-0d, double.Epsilon, "-0", "5E-324"); if (result != 42) return result;
			result = CheckSmallNullableJoin(double.MaxValue, double.MinValue, "1.7976931348623157E+308", "-1.7976931348623157E+308"); if (result != 42) return 1000 + result;
			result = CheckSmallNullableJoin(double.PositiveInfinity, double.NegativeInfinity, "Infinity", "-Infinity"); if (result != 42) return 2000 + result;
			result = CheckSmallNullableJoin(Unsafe.BitCast<ulong, double>(0x7ff8123456789abcUL), Unsafe.BitCast<ulong, double>(0xfff0123456789abcUL), "NaN", "NaN"); if (result != 42) return 3000 + result;
			result = CheckSmallNullableJoin(-123.5d, 1.25d, "-123.5", "1.25"); return result == 42 ? 42 : 4000 + result;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericFloatingNullableCallbacksEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			if (GenericJoinCallbacks<float?>(-0f, null, "-0", "") != 42) return 1;
			if (GenericJoinCallbacks<double?>(null, double.Epsilon, "", "5E-324") != 42) return 2;
			if (CheckStructGenericJoin<float?>(float.NaN, null, "NaN", "") != 42) return 3;
			return CheckStructGenericJoin<double?>(null, double.NegativeInfinity, "", "-Infinity");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericFloatingNullableOwnershipEntry()
	{
		for (var index = 0; index < 8; index++)
		{
			var singles = new float?[2]; singles[0] = Unsafe.BitCast<uint, float>(FloatingArraySingleBits(index));
			IEnumerator<float?> single = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(singles);
			if (!single.MoveNext()) return 100 + index;
			var heldSingle = single.Current; singles[0] = null; GC.Collect();
			if (!heldSingle.HasValue || !single.Current.HasValue || Unsafe.BitCast<float, uint>(heldSingle.Value) != FloatingArraySingleBits(index) ||
				Unsafe.BitCast<float, uint>(single.Current.GetValueOrDefault()) != FloatingArraySingleBits(index)) return 200 + index;
			object? singleBox = heldSingle; GC.Collect();
			if (singleBox is not float s || Unsafe.BitCast<float, uint>(s) != FloatingArraySingleBits(index)) return 300 + index;
			single.Dispose(); single.Dispose(); GC.Collect();
			if (single.Current.HasValue || single.MoveNext() || Unsafe.BitCast<float, uint>(heldSingle.GetValueOrDefault()) != FloatingArraySingleBits(index)) return 400 + index;

			var doubles = new double?[2]; doubles[0] = Unsafe.BitCast<ulong, double>(FloatingArrayDoubleBits(index));
			IEnumerator<double?> wide = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(doubles);
			if (!wide.MoveNext()) return 500 + index;
			var heldWide = wide.Current; doubles[0] = null; GC.Collect();
			if (!heldWide.HasValue || !wide.Current.HasValue || Unsafe.BitCast<double, ulong>(heldWide.Value) != FloatingArrayDoubleBits(index) ||
				Unsafe.BitCast<double, ulong>(wide.Current.GetValueOrDefault()) != FloatingArrayDoubleBits(index)) return 600 + index;
			object? wideBox = heldWide; GC.Collect();
			if (wideBox is not double d || Unsafe.BitCast<double, ulong>(d) != FloatingArrayDoubleBits(index)) return 700 + index;
			wide.Dispose(); wide.Dispose(); GC.Collect();
			if (wide.Current.HasValue || wide.MoveNext() || Unsafe.BitCast<double, ulong>(heldWide.GetValueOrDefault()) != FloatingArrayDoubleBits(index)) return 800 + index;
		}
		return 42;
	}

	public static int CoreLibGenericFloatingNullableCapacityEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			if (BooleanCharJoinCapacity<float?>(-0f, null) != 42) return 1;
			if (BooleanCharJoinCapacity<double?>(double.Epsilon, null) != 42) return 2;
			if (NullableJoinLeadingEmptyCapacity(float.NaN) != 42) return 3;
			return NullableJoinLeadingEmptyCapacity(double.PositiveInfinity);
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericFloatingNullableAllocationEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			if (BooleanCharJoinAllocation<float?>(null, -0f, "", "-0") != 42) return 1;
			return BooleanCharJoinAllocation<double?>(double.Epsilon, null, "5E-324", "");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibGenericFloatingNullableCultureEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = new CultureInfo("");
			culture.NumberFormat.NegativeSign = "n";
			culture.NumberFormat.NumberDecimalSeparator = ":";
			culture.NumberFormat.NaNSymbol = "nan";
			culture.NumberFormat.PositiveInfinitySymbol = "plus";
			culture.NumberFormat.NegativeInfinitySymbol = "minus";
			CultureInfo.CurrentCulture = culture; culture = null!; GC.Collect();
			if (CheckGenericJoin<float?>(-1.25f, null, "[n1:25|]") != 42) return 1;
			if (CheckGenericJoin<double?>(null, -1.25d, "[|n1:25]") != 42) return 2;
			if (CheckGenericJoin<float?>(float.NaN, float.PositiveInfinity, "[nan|plus]") != 42) return 3;
			if (CheckGenericJoin<double?>(double.NegativeInfinity, -0d, "[minus|n0]") != 42) return 4;
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; GC.Collect();
			return CheckGenericJoin<double?>(-1.25d, null, "[-1.25|]");
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
