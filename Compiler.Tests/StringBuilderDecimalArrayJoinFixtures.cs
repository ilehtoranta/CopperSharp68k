/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static StringBuilder AppendStableDecimalArrayJoin(StringBuilder builder, IEnumerable<decimal> values)
		=> builder.AppendJoin('|', values);

	public static int StringBuilderStableDecimalArrayJoinEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var culture = new CultureInfo("");
			culture.NumberFormat = new NumberFormatInfo { NegativeSign = "minus", NumberDecimalSeparator = ":" };
			CultureInfo.CurrentCulture = culture;
			var values = new decimal[4];
			values[0] = new decimal(0, 0, 0, true, 3);
			values[1] = new decimal(1234500, 0, 0, true, 4);
			values[2] = new decimal(1, 0, 0, false, 28);
			values[3] = new decimal(-1, -1, -1, false, 0);
			for (var spare = 0; spare < 2; spare++)
			{
				var builder = new StringBuilder(spare == 0 ? 1 : 512).Append("seed");
				if (AppendStableDecimalArrayJoin(builder, values) != builder) return 1;
				GC.Collect();
				var snapshot = builder.ToString();
				if (snapshot != "seed0:000|minus123:4500|0:0000000000000000000000000001|79228162514264337593543950335") return 2;
				builder.Clear().AppendJoin("\0Ω\ud800", (IEnumerable<decimal>)values);
				GC.Collect();
				if (builder.ToString() != "0:000\0Ω\ud800minus123:4500\0Ω\ud8000:0000000000000000000000000001\0Ω\ud80079228162514264337593543950335") return 3;
				builder.Clear().Append("seed");
				if (builder.AppendJoin('|', (IEnumerable<decimal>)new decimal[0]) != builder) return 4;
				try { builder.AppendJoin('|', (IEnumerable<decimal>)null!); return 5; }
				catch (ArgumentNullException exception) { if (exception.ParamName != "values") return 6; }
				try { builder.AppendJoin("|", (IEnumerable<decimal>)null!); return 7; }
				catch (ArgumentNullException exception) { if (exception.ParamName != "values") return 8; }
				GC.Collect();
				if (builder.ToString() != "seed" || snapshot != "seed0:000|minus123:4500|0:0000000000000000000000000001|79228162514264337593543950335") return 9;
				builder.Clear().AppendJoin((string?)null, (IEnumerable<decimal>)new decimal[] { new decimal(7) });
				GC.Collect();
				if (builder.ToString() != "7") return 10;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
