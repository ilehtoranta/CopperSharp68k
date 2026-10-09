/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibStringBuilderFloatingObjectsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			for (var custom = 0; custom < 2; custom++)
			{
				var culture = new CultureInfo("");
				if (custom != 0)
				{
					culture.NumberFormat.NegativeSign = "minus";
					culture.NumberFormat.PositiveSign = "plus";
					culture.NumberFormat.NumberDecimalSeparator = ":";
					culture.NumberFormat.NaNSymbol = "unknown";
					culture.NumberFormat.PositiveInfinitySymbol = "forever";
					culture.NumberFormat.NegativeInfinitySymbol = "below";
				}
				CultureInfo.CurrentCulture = culture;
				for (var scenario = 0; scenario < 8; scenario++)
				for (var precision = 0; precision < 2; precision++)
				{
					object value;
					string expected;
					if (precision == 0)
					{
						value = scenario switch { 0 => -0f, 1 => 0.1f, 2 => -123.5f, 3 => float.Epsilon,
							4 => float.MaxValue, 5 => float.PositiveInfinity, 6 => float.NegativeInfinity, _ => float.NaN };
						expected = scenario switch { 0 => "-0", 1 => "0.1", 2 => "-123.5", 3 => "1E-45",
							4 => "3.4028235E+38", 5 => "Infinity", 6 => "-Infinity", _ => "NaN" };
					}
					else
					{
						value = scenario switch { 0 => -0d, 1 => 0.1d, 2 => -123.5d, 3 => double.Epsilon,
							4 => double.MaxValue, 5 => double.PositiveInfinity, 6 => double.NegativeInfinity, _ => double.NaN };
						expected = scenario switch { 0 => "-0", 1 => "0.1", 2 => "-123.5", 3 => "5E-324",
							4 => "1.7976931348623157E+308", 5 => "Infinity", 6 => "-Infinity", _ => "NaN" };
					}
					if (custom != 0)
					{
						if (scenario >= 5) expected = scenario == 5 ? "forever" : scenario == 6 ? "below" : "unknown";
						else
						{
							var text = new StringBuilder();
							foreach (var character in expected)
								if (character == '-') text.Append("minus");
								else if (character == '+') text.Append("plus");
								else text.Append(character == '.' ? ':' : character);
							expected = text.ToString();
						}
					}
					object?[] values = [value, null, value];
					for (var roomy = 0; roomy < 2; roomy++)
					{
						var builder = new StringBuilder(roomy == 0 ? 1 : 128).Append('A');
						if (!ReferenceEquals(builder, builder.Append(value)) || builder.ToString() != "A" + expected) return 100 + scenario;
						var snapshot = builder.ToString();
						builder.Clear().Append("tail");
						if (!ReferenceEquals(builder, builder.Insert(0, value)) || builder.ToString() != expected + "tail") return 200 + scenario;
						builder.Clear().AppendJoin('|', values);
						if (builder.ToString() != expected + "||" + expected) return 300 + scenario;
						builder.Clear().AppendJoin("::", new ReadOnlySpan<object?>(values));
						System.GC.Collect();
						if (builder.ToString() != expected + "::::" + expected || snapshot != "A" + expected) return 400 + scenario;
						builder.Clear().Append("reuse");
						if (builder.ToString() != "reuse") return 500 + scenario;
					}
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
