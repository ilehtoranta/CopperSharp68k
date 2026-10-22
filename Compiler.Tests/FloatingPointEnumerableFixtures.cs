/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibFloatingListPrefixCopyContractsEntry()
	{
		var singles = new List<float>();
		var doubles = new List<double>();
		var objects = new List<object>();
		for (var index = 0; index < 9; index++)
		{
			singles.Add(System.Runtime.CompilerServices.Unsafe.BitCast<uint, float>(FloatingArraySingleBits(index & 7)));
			doubles.Add(System.Runtime.CompilerServices.Unsafe.BitCast<ulong, double>(FloatingArrayDoubleBits(index & 7)));
			var owner = new byte[4]; owner[0] = (byte)(index + 1); objects.Add(owner);
			System.GC.Collect();
		}
		singles.Capacity = 25;
		doubles.Capacity = 25;
		objects.Capacity = 25;
		var singleCopy = singles.ToArray();
		var doubleCopy = doubles.ToArray();
		var objectCopy = objects.ToArray();
		System.GC.Collect();
		if (singleCopy.Length != 9 || doubleCopy.Length != 9 || objectCopy.Length != 9) return 1;
		for (var index = 0; index < 9; index++)
		{
			if (ReadFloatingArraySingle(singleCopy, index) != FloatingArraySingleBits(index & 7) ||
				ReadFloatingArrayDouble(doubleCopy, index) != FloatingArrayDoubleBits(index & 7)) return 100 + index;
			if (((byte[])objectCopy[index])[0] != index + 1) return 200 + index;
		}
		try { singles.Capacity = 8; return 2; }
		catch (System.ArgumentOutOfRangeException) { }
		try { doubles.Capacity = -1; return 3; }
		catch (System.ArgumentOutOfRangeException) { }
		return singles.Count == 9 && doubles.Count == 9 && objects.Count == 9 ? 42 : 4;
	}

	public static int CoreLibFloatingJoinEnumeratorContractsEntry()
	{
		var singleList = new List<float>();
		singleList.Add(float.Epsilon);
		var single = CopperSharp.Runtime.ShadowSingleJoinEnumeration.GetEnumerator(singleList);
		if (!single.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current) != 1u) return 1;
		singleList.Add(-0f);
		try { single.MoveNext(); return 2; }
		catch (System.InvalidOperationException) { }
		single.Dispose();
		System.GC.Collect();
		if (single.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current) != 0u) return 3;
		var doubleList = new List<double>();
		doubleList.Add(double.Epsilon);
		var wide = CopperSharp.Runtime.ShadowDoubleJoinEnumeration.GetEnumerator(doubleList);
		if (!wide.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current) != 1ul) return 4;
		doubleList.Add(-0d);
		try { wide.MoveNext(); return 5; }
		catch (System.InvalidOperationException) { }
		wide.Dispose();
		System.GC.Collect();
		if (wide.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current) != 0ul) return 6;
		var singles = new float[1]; singles[0] = -0f;
		var doubles = new double[1]; doubles[0] = -0d;
		single = CopperSharp.Runtime.ShadowSingleJoinEnumeration.GetEnumerator(singles);
		wide = CopperSharp.Runtime.ShadowDoubleJoinEnumeration.GetEnumerator(doubles);
		System.GC.Collect();
		if (!single.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current) != 0x80000000u) return 7;
		if (!wide.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current) != 0x8000000000000000ul) return 8;
		if (single.MoveNext() || wide.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current) != 0u ||
			System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current) != 0ul) return 9;
		return 42;
	}

	public static int CoreLibStringBuilderFloatingEnumerableEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var singles = new float[5];
			var doubles = new double[5];
			var singleList = new List<float>();
			var doubleList = new List<double>();
			for (var index = 0; index < singles.Length; index++)
			{
				singles[index] = index switch { 0 => -0f, 1 => float.Epsilon, 2 => -123.5f, 3 => float.PositiveInfinity, _ => float.NaN };
				doubles[index] = index switch { 0 => -0d, 1 => double.Epsilon, 2 => -123.5d, 3 => double.NegativeInfinity, _ => double.NaN };
				singleList.Add(singles[index]);
				doubleList.Add(doubles[index]);
			}
			object?[] objects = [singles[0], null, doubles[1], singles[2], doubles[3], singles[4]];
			var objectList = new List<object?>();
			for (var index = 0; index < objects.Length; index++) objectList.Add(objects[index]);
			const string singleText = "-0|1E-45|-123.5|Infinity|NaN";
			const string doubleText = "-0|5E-324|-123.5|-Infinity|NaN";
			const string objectText = "-0||5E-324|-123.5|-Infinity|NaN";
			for (var roomy = 0; roomy < 2; roomy++)
			{
				var builder = new StringBuilder(roomy == 0 ? 1 : 160);
				if (!ReferenceEquals(builder, builder.AppendJoin<float>('|', singles)) || builder.ToString() != singleText) return 1;
				var snapshot = builder.ToString();
				builder.Clear().AppendJoin<double>("|", doubles);
				if (builder.ToString() != doubleText) return 2;
				builder.Clear().AppendJoin<float>("|", singleList);
				if (builder.ToString() != singleText) return 3;
				builder.Clear().AppendJoin<double>('|', doubleList);
				if (builder.ToString() != doubleText) return 4;
				builder.Clear().AppendJoin<object?>("|", objects);
				if (builder.ToString() != objectText) return 5;
				builder.Clear().AppendJoin<object?>('|', objectList);
				System.GC.Collect();
				if (builder.ToString() != objectText || snapshot != singleText) return 6;
				builder.Clear().Append("reuse");
				if (builder.ToString() != "reuse") return 7;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
