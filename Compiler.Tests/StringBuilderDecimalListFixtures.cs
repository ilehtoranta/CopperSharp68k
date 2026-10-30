/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static object StableStringListIdentity() => new List<string>();
	public static object StableInt32ListIdentity() => new List<int>();
	public static object StableObjectListIdentity() => new List<object>();
	public static object StableDecimalListIdentity() => new List<decimal>();
	public static object StableSingleListIdentity() => new List<float>();
	public static object StableDoubleListIdentity() => new List<double>();
	public static int StringBuilderStableDecimalListGrowthEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var list = new List<decimal>(1);
			for (var index = 0; index < 17; index++)
			{
				list.Add(new decimal(index, index + 1, index + 2, true, 28));
				GC.Collect();
			}
			var iterator = list.GetEnumerator();
			Span<int> bits = stackalloc int[4];
			for (var index = 0; index < 17; index++)
			{
				GC.Collect();
				if (!iterator.MoveNext()) return 1;
				if (decimal.GetBits(iterator.Current, bits) != 4 || bits[0] != index || bits[1] != index + 1 ||
					bits[2] != index + 2 || bits[3] != unchecked((int)0x801C0000)) return 2;
			}
			if (iterator.MoveNext()) return 3;
			iterator.Dispose();
			var builder = new StringBuilder(1).AppendJoin('|', list);
			GC.Collect();
			var snapshot = builder.ToString();
			if (snapshot.Length == 0) return 4;
			list.Clear(); list.Add(new decimal(1234500, 0, 0, true, 4));
			list.Add(new decimal(0, 0, 0, true, 3));
			builder.Clear().AppendJoin("::", list);
			GC.Collect();
			if (builder.ToString() != "-123.4500::0.000" || snapshot.Length == 0) return 5;
			iterator = list.GetEnumerator();
			if (!iterator.MoveNext()) return 6;
			list[0] = new decimal(7);
			try { iterator.MoveNext(); return 7; } catch (InvalidOperationException) { }
			iterator.Dispose();
			builder.Clear().AppendJoin('|', list);
			GC.Collect();
			return builder.ToString() == "7|0.000" ? 42 : 8;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
