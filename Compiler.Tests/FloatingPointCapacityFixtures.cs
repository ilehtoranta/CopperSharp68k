/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static StringBuilder AppendFloatingCapacityCase(StringBuilder builder, bool wide, int form,
		CompositeFormat parsed, CompositeFormat right, CompositeFormat left)
	{
		const float single = -123.5f;
		const double value = -123.5d;
		object box = wide ? (object)value : single;
		if (form == 0) return wide ? builder.Append(value) : builder.Append(single);
		if (form == 1) return builder.Append(box);
		if (form == 2) return wide ? builder.Insert(0, value) : builder.Insert(0, single);
		if (form == 3) return builder.Insert(0, box);
		if (form == 4) return builder.AppendFormat(CultureInfo.InvariantCulture, "B{0:F2}C", box);
		if (form < 8)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, CultureInfo.InvariantCulture);
			handler.AppendLiteral("B");
			if (form == 7) handler.AppendFormatted(box, -16, "F2");
			else if (wide) handler.AppendFormatted(value, form == 6 ? 16 : 0, "F2");
			else handler.AppendFormatted(single, form == 6 ? 16 : 0, "F2");
			handler.AppendLiteral("C");
			return builder.Append(ref handler);
		}
		if (form == 8) return wide ? builder.AppendFormat<double>(CultureInfo.InvariantCulture, parsed, value) : builder.AppendFormat<float>(CultureInfo.InvariantCulture, parsed, single);
		if (form == 9) return wide ? builder.AppendFormat<double, int>(CultureInfo.InvariantCulture, parsed, value, 7) : builder.AppendFormat<float, int>(CultureInfo.InvariantCulture, parsed, single, 7);
		if (form == 10) return wide ? builder.AppendFormat<double, int, int>(CultureInfo.InvariantCulture, parsed, value, 7, 9) : builder.AppendFormat<float, int, int>(CultureInfo.InvariantCulture, parsed, single, 7, 9);
		if (form == 11) return builder.AppendFormat(CultureInfo.InvariantCulture, parsed, new object?[] { box });
		if (form == 12) return builder.AppendFormat(CultureInfo.InvariantCulture, parsed, new ReadOnlySpan<object?>(new object?[] { box }));
		if (form == 13) return builder.AppendJoin('|', new object?[] { null, box });
		if (form == 14) return builder.AppendJoin("::", new ReadOnlySpan<object?>(new object?[] { null, box }));
		if (form == 15)
		{
			if (wide) { var values = new double[2]; values[1] = value; return builder.AppendJoin<double>('|', values); }
			else { var values = new float[2]; values[1] = single; return builder.AppendJoin<float>('|', values); }
		}
		if (form == 16)
		{
			if (wide) { var values = new List<double>(); values.Add(0d); values.Add(value); return builder.AppendJoin<double>("::", values); }
			else { var values = new List<float>(); values.Add(0f); values.Add(single); return builder.AppendJoin<float>("::", values); }
		}
		if (form == 17) return builder.AppendJoin<object?>("::", new List<object?> { null, box });
		var aligned = form == 18 ? right : left;
		return wide ? builder.AppendFormat<double>(CultureInfo.InvariantCulture, aligned, value) : builder.AppendFormat<float>(CultureInfo.InvariantCulture, aligned, single);
	}

	public static int CoreLibStringBuilderFloatingCapacityEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
			var parsed = CompositeFormat.Parse("B{0:F2}C");
			var right = CompositeFormat.Parse("B{0,16:F2}C");
			var left = CompositeFormat.Parse("B{0,-16:F2}C");
			for (var precision = 0; precision < 2; precision++)
			for (var roomy = 0; roomy < 2; roomy++)
			for (var form = 0; form < 20; form++)
			{
				var builder = new StringBuilder(roomy == 0 ? 1 : 4, 4).Append('A');
				var original = builder.ToString();
				try { AppendFloatingCapacityCase(builder, precision != 0, form, parsed, right, left); return 100 + form; }
				catch (ArgumentOutOfRangeException error)
				{
					if (form is 2 or 3 || error.ParamName != (form is 6 or 18 ? "repeatCount" : "valueCount") || error.ActualValue != null) return 200 + form;
				}
				catch (OutOfMemoryException) { if (form is not (2 or 3)) return 300 + form; }
				var expected = form < 4 ? "A" : form == 13 ? "A|" : form is 14 or 17 ? "A::" : form == 15 ? "A0|" : form == 16 ? "A0::" : "AB";
				var snapshot = builder.ToString();
				System.GC.Collect();
				if (snapshot != expected || builder.ToString() != expected || original != "A" || builder.Length != expected.Length || builder.MaxCapacity != 4) return 400 + form;
				if (!ReferenceEquals(builder, builder.Clear())) return 500 + form;
				builder.Append('Z');
				if (precision == 0) builder.Append(0f); else builder.Append(0d);
				System.GC.Collect();
				if (builder.ToString() != "Z0" || snapshot != expected || original != "A") return 600 + form;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
