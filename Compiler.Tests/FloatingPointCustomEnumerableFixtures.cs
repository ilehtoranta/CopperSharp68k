/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Collections;
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static CopperSharp.Runtime.ShadowSingleJoinEnumerator CreateOwnedCustomSingleAdapter()
	{
		var values = new float[2]; values[0] = -0f; values[1] = float.Epsilon;
		return CopperSharp.Runtime.ShadowSingleJoinEnumeration.GetEnumerator(new CustomSingleJoinSource(values));
	}

	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static CopperSharp.Runtime.ShadowDoubleJoinEnumerator CreateOwnedCustomDoubleAdapter()
	{
		var values = new double[2]; values[0] = -0d; values[1] = double.Epsilon;
		return CopperSharp.Runtime.ShadowDoubleJoinEnumeration.GetEnumerator(new CustomDoubleJoinSource(values));
	}

	public static int CoreLibFloatingCustomAdapterOwnershipEntry()
	{
		var single = CreateOwnedCustomSingleAdapter();
		var wide = CreateOwnedCustomDoubleAdapter();
		System.GC.Collect();
		if (!single.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current) != 0x80000000u) return 1;
		if (!wide.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current) != 0x8000000000000000UL) return 2;
		System.GC.Collect();
		if (!single.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<float, uint>(single.Current) != 1) return 3;
		if (!wide.MoveNext() || System.Runtime.CompilerServices.Unsafe.BitCast<double, ulong>(wide.Current) != 1) return 4;
		if (single.MoveNext() || wide.MoveNext()) return 5;
		single.Dispose(); wide.Dispose();
		System.GC.Collect();
		return single.Current == 0 && wide.Current == 0 && !single.MoveNext() && !wide.MoveNext() ? 42 : 6;
	}

	private sealed class CustomSingleJoinSource : IEnumerable<float>, IEnumerator<float>
	{
		private readonly float[] _values;
		private int _index = -1;
		public int Creations, Disposals;
		public CustomSingleJoinSource(float[] values) => _values = values;
		public IEnumerator<float> GetEnumerator() { Creations++; _index = -1; return this; }
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		public float Current => _values[_index];
		object IEnumerator.Current => Current;
		public bool MoveNext() { _index++; System.GC.Collect(); return _index < _values.Length; }
		public void Dispose() { Disposals++; System.GC.Collect(); }
		public void Reset() => throw new NotSupportedException();
	}

	private sealed class CustomDoubleJoinSource : IEnumerable<double>, IEnumerator<double>
	{
		private readonly double[] _values;
		private int _index = -1;
		public int Creations, Disposals;
		public CustomDoubleJoinSource(double[] values) => _values = values;
		public IEnumerator<double> GetEnumerator() { Creations++; _index = -1; return this; }
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
		public double Current => _values[_index];
		object IEnumerator.Current => Current;
		public bool MoveNext() { _index++; System.GC.Collect(); return _index < _values.Length; }
		public void Dispose() { Disposals++; System.GC.Collect(); }
		public void Reset() => throw new NotSupportedException();
	}

	public static int CoreLibStringBuilderCustomFloatingEnumerableEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			var singles = new float[3]; singles[1] = -123.5f; singles[2] = float.NaN;
			var doubles = new double[3]; doubles[1] = -123.5d; doubles[2] = double.NaN;
			for (var capacity = 0; capacity < 2; capacity++)
			for (var separator = 0; separator < 2; separator++)
			{
				var singleSource = new CustomSingleJoinSource(singles);
				var doubleSource = new CustomDoubleJoinSource(doubles);
				var builder = new StringBuilder(capacity == 0 ? 1 : 64).Append("seed");
				var snapshot = builder.ToString();
				try
				{
					if (separator == 0) builder.AppendJoin('|', singleSource);
					else builder.AppendJoin("::", singleSource);
				}
				catch (NotSupportedException) { return 100; }
				var expected = separator == 0 ? "seed0|-123.5|NaN" : "seed0::-123.5::NaN";
				System.GC.Collect();
				if (builder.ToString() != expected || snapshot != "seed" || singleSource.Creations != 1 || singleSource.Disposals != 1) return 200;
				builder.Clear().Append("seed");
				try
				{
					if (separator == 0) builder.AppendJoin('|', doubleSource);
					else builder.AppendJoin("::", doubleSource);
				}
				catch (NotSupportedException) { return 300; }
				System.GC.Collect();
				if (builder.ToString() != expected || snapshot != "seed" || doubleSource.Creations != 1 || doubleSource.Disposals != 1) return 400;
				builder.Clear().Append("tail");
				if (builder.ToString() != "tail" || snapshot != "seed") return 500;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
