/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int StringBuilderDefaultNegativeSignContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = new CultureInfo("");
			var info = new NumberFormatInfo();
			culture.NumberFormat = info;
			CultureInfo.CurrentCulture = culture;
			var builder = new StringBuilder(64);
			for (var signIndex = 0; signIndex < 4; signIndex++)
			{
				var sign = signIndex == 0 ? "-" : signIndex == 1 ? "" : signIndex == 2 ? "Ω\0\uD800" : "long-negative-sign:";
				info.NegativeSign = sign;
				for (var index = 0; index < 6; index++)
				{
					var value = index == 0 ? int.MinValue : index == 1 ? -10 : index == 2 ? -1 : index == 3 ? 0 : index == 4 ? 1 : int.MaxValue;
					var expected = index == 0 ? sign + "2147483648" : index == 1 ? sign + "10" : index == 2 ? sign + "1" : index == 3 ? "0" : index == 4 ? "1" : "2147483647";
					builder.Clear().Insert(0, value);
					var snapshot = builder.ToString();
					GC.Collect();
					if (snapshot != expected || builder.ToString() != expected) return 100 + signIndex * 6 + index;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int StringBuilderBenchmarkBaselineEntry() => 42;

	public static int StringBuilderBenchmarkPresizedTextEntry()
	{
		var builder = new StringBuilder(32);
		for (var i = 0; i < 8; i++) builder.Append("part");
		return builder.ToString() == "partpartpartpartpartpartpartpart" ? 42 : 1;
	}

	public static int StringBuilderBenchmarkGrowingTextEntry()
	{
		var builder = new StringBuilder(1);
		for (var i = 0; i < 8; i++) builder.Append("part");
		return builder.ToString() == "partpartpartpartpartpartpartpart" ? 42 : 2;
	}

	public static int StringBuilderBenchmarkIntegerHandlerEntry()
	{
		var builder = new StringBuilder(32);
		for (var i = 0; i < 8; i++) builder.Append(CultureInfo.InvariantCulture, $"[{i}]");
		return builder.ToString() == "[0][1][2][3][4][5][6][7]" ? 42 : 3;
	}

	public static int StringBuilderBenchmarkIntegerFormatEntry()
	{
		var builder = new StringBuilder(32);
		for (var i = 0; i < 8; i++) builder.AppendFormat(CultureInfo.InvariantCulture, "[{0}]", i);
		return builder.ToString() == "[0][1][2][3][4][5][6][7]" ? 42 : 4;
	}
}
