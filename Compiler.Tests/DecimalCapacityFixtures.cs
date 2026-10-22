/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static StringBuilder AppendDecimalCapacityCase(StringBuilder builder, int form,
		CompositeFormat parsed, CompositeFormat right, CompositeFormat left)
	{
		const decimal value = -123.5000m;
		object box = value;
		if (form == 0) return builder.Append(value);
		if (form == 1) return builder.Append(box);
		if (form == 2) return builder.Insert(0, value);
		if (form == 3) return builder.Insert(0, box);
		if (form == 4) return builder.AppendFormat(CultureInfo.InvariantCulture, "B{0:F2}C", box);
		if (form is >= 5 and < 8 || form >= 20)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("B");
			var formatting = form < 20 ? form - 5 : (form - 20) % 3;
			if (formatting == 2) handler.AppendFormatted(box, -16, "F2");
			else handler.AppendFormatted(value, formatting == 1 ? 16 : 0, "F2");
			handler.AppendLiteral("C");
			var consuming = form < 20 ? 0 : 1 + (form - 20) / 3;
			if (consuming == 0) return builder.Append(ref handler);
			if (consuming == 1) return builder.Append(CultureInfo.InvariantCulture, ref handler);
			if (consuming == 2) return builder.AppendLine(ref handler);
			return builder.AppendLine(CultureInfo.InvariantCulture, ref handler);
		}
		if (form == 8) return builder.AppendFormat<decimal>(CultureInfo.InvariantCulture, parsed, value);
		if (form == 9) return builder.AppendFormat<decimal, int>(CultureInfo.InvariantCulture, parsed, value, 7);
		if (form == 10) return builder.AppendFormat<decimal, int, int>(CultureInfo.InvariantCulture, parsed, value, 7, 9);
		if (form == 11) return builder.AppendFormat(CultureInfo.InvariantCulture, parsed, new object?[] { box });
		if (form == 12) return builder.AppendFormat(CultureInfo.InvariantCulture, parsed, new ReadOnlySpan<object?>(new object?[] { box }));
		if (form == 13) return builder.AppendJoin('|', new object?[] { null, box });
		if (form == 14) return builder.AppendJoin("::", new ReadOnlySpan<object?>(new object?[] { null, box }));
		if (form == 15)
		{
			var values = new decimal[2]; values[1] = value; return builder.AppendJoin<decimal>('|', values);
		}
		if (form == 16)
		{
			var values = new List<decimal>(2); values.Add(0m); values.Add(value); return builder.AppendJoin<decimal>("::", values);
		}
		if (form == 17) return builder.AppendJoin<object?>("::", new List<object?> { null, box });
		var aligned = form == 18 ? right : left;
		return builder.AppendFormat<decimal>(CultureInfo.InvariantCulture, aligned, value);
	}

	public static int CoreLibDecimalPublicConstantsEntry()
	{
		Span<int> bits = stackalloc int[4];
		decimal.GetBits(decimal.Zero, bits);
		if (bits[0] != 0 || bits[1] != 0 || bits[2] != 0 || bits[3] != 0) return 1;
		decimal.GetBits(decimal.One, bits);
		if (bits[0] != 1 || bits[1] != 0 || bits[2] != 0 || bits[3] != 0) return 2;
		decimal.GetBits(decimal.MinusOne, bits);
		if (bits[0] != 1 || bits[1] != 0 || bits[2] != 0 || bits[3] != unchecked((int)0x80000000)) return 3;
		decimal.GetBits(decimal.MaxValue, bits);
		if (bits[0] != -1 || bits[1] != -1 || bits[2] != -1 || bits[3] != 0) return 4;
		decimal.GetBits(decimal.MinValue, bits);
		if (bits[0] != -1 || bits[1] != -1 || bits[2] != -1 || bits[3] != unchecked((int)0x80000000)) return 5;
		GC.Collect();
		return new StringBuilder().Append(decimal.Zero).Append('|').Append(decimal.One).ToString() == "0|1" ? 42 : 6;
	}

	public static int CoreLibStringBuilderDecimalCapacityEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var parsed = CompositeFormat.Parse("B{0:F2}C");
			var right = CompositeFormat.Parse("B{0,16:F2}C");
			var left = CompositeFormat.Parse("B{0,-16:F2}C");
			for (var roomy = 0; roomy < 2; roomy++)
			for (var form = 0; form < 29; form++)
			{
				var builder = new StringBuilder(roomy == 0 ? 1 : 4, 4).Append('A');
				var original = builder.ToString();
				try { AppendDecimalCapacityCase(builder, form, parsed, right, left); return 100 + form; }
				catch (ArgumentOutOfRangeException error)
				{
					if (form is 2 or 3 || error.ParamName != (form is 6 or 18 or 21 or 24 or 27 ? "repeatCount" : "valueCount") || error.ActualValue != null) return 200 + form;
				}
				catch (OutOfMemoryException) { if (form is not (2 or 3)) return 300 + form; }
				var expected = form < 4 ? "A" : form == 13 ? "A|" : form is 14 or 17 ? "A::" : form == 15 ? "A0|" : form == 16 ? "A0::" : "AB";
				var snapshot = builder.ToString();
				System.GC.Collect();
				if (snapshot != expected || builder.ToString() != expected || original != "A" || builder.Length != expected.Length || builder.MaxCapacity != 4) return 400 + form;
				if (!ReferenceEquals(builder, builder.Clear())) return 500 + form;
				builder.Append('Z');
				builder.Append(0m);
				System.GC.Collect();
				if (builder.ToString() != "Z0" || snapshot != expected || original != "A") return 600 + form;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
