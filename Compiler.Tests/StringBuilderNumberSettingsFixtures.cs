/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int StringBuilderStableNumberSettingsFallbackEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var characters = new char[303];
			for (var index = 0; index < 300; index++) characters[index] = '!';
			characters[300] = '\0'; characters[301] = '\u03a9'; characters[302] = '\ud800';
			var sign = new string(characters.AsSpan());
			var culture = new CultureInfo("");
			culture.NumberFormat = new NumberFormatInfo { NegativeSign = sign, NumberDecimalSeparator = ":" };
			CultureInfo.CurrentCulture = culture;
			string[] digits = ["128", "32768", "2147483648", "9223372036854775808", "123:4500"];
			for (var kind = 0; kind < digits.Length; kind++)
			for (var roomy = 0; roomy < 2; roomy++)
			for (var operation = 0; operation < 2; operation++)
			{
				var builder = new StringBuilder(roomy == 0 ? 1 : 1024).Append('[');
				if (operation == 0)
				{
					if (kind == 0) builder.Append(sbyte.MinValue);
					else if (kind == 1) builder.Append(short.MinValue);
					else if (kind == 2) builder.Append(int.MinValue);
					else if (kind == 3) builder.Append(long.MinValue);
					else builder.Append(new decimal(1234500, 0, 0, true, 4));
				}
				else
				{
					if (kind == 0) builder.Insert(0, sbyte.MinValue);
					else if (kind == 1) builder.Insert(0, short.MinValue);
					else if (kind == 2) builder.Insert(0, int.MinValue);
					else if (kind == 3) builder.Insert(0, long.MinValue);
					else builder.Insert(0, new decimal(1234500, 0, 0, true, 4));
				}
				var expected = string.Concat(sign, digits[kind]);
				expected = operation == 0 ? string.Concat("[", expected) : string.Concat(expected, "[");
				var snapshot = builder.ToString(); GC.Collect(); builder.Clear().Append("reuse"); GC.Collect();
				if (snapshot != expected || builder.ToString() != "reuse") return 1 + kind;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int StringBuilderStableNumberSettingsValidationEntry()
	{
		var info = new NumberFormatInfo();
		for (var property = 0; property < 14; property++)
		{
			var text = new string("custom\0\u03a9\ud800".AsSpan());
			SetNumberText(info, property, text);
			GC.Collect();
			if (GetNumberText(info, property) != text) return 1;
			try { SetNumberText(info, property, null!); return 2; }
			catch (ArgumentNullException error) { if (error.ParamName != "value") return 3; }
			if (GetNumberText(info, property) != text) return 4;
			if (property is 2 or 4 or 7)
			{
				try { SetNumberText(info, property, ""); return 5; }
				catch (ArgumentException error) { if (error.ParamName != "value") return 6; }
				if (GetNumberText(info, property) != text) return 7;
			}
			else { SetNumberText(info, property, ""); if (GetNumberText(info, property) != "") return 8; }
			try { SetNumberText(NumberFormatInfo.InvariantInfo, property, "changed"); return 9; }
			catch (InvalidOperationException) { }
			SetNumberText(info, property, text);
			if (GetNumberText(info, property) != text) return 10;
		}
		int[] limits = [99, 4, 99, 16, 3, 99, 11, 3];
		for (var property = 0; property < limits.Length; property++)
		{
			SetNumberInteger(info, property, limits[property]);
			if (GetNumberInteger(info, property) != limits[property]) return 11;
			for (var boundary = 0; boundary < 2; boundary++)
			{
				var invalid = boundary == 0 ? -1 : limits[property] + 1;
				try { SetNumberInteger(info, property, invalid); return 12; }
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value" || error.ActualValue is not int value || value != invalid) return 13; }
				GC.Collect();
				if (GetNumberInteger(info, property) != limits[property]) return 14;
			}
			try { SetNumberInteger(NumberFormatInfo.InvariantInfo, property, 0); return 15; }
			catch (InvalidOperationException) { }
			SetNumberInteger(info, property, 0);
			if (GetNumberInteger(info, property) != 0) return 16;
		}
		for (var property = 0; property < 3; property++)
		{
			int[] source = [3, 2, 0];
			SetNumberGroups(info, property, source); source[0] = 7;
			var copy = GetNumberGroups(info, property); copy[0] = 8;
			GC.Collect();
			if (GetNumberGroups(info, property)[0] != 3) return 17;
			for (var invalid = 0; invalid < 4; invalid++)
			{
				try { SetNumberGroups(info, property, invalid == 0 ? null! : invalid == 1 ? [-1] : invalid == 2 ? [10] : [0, 1]); return 18; }
				catch (ArgumentException error) { if (error.ParamName != "value" || (error is ArgumentNullException) != (invalid == 0)) return 19; }
				if (GetNumberGroups(info, property)[0] != 3) return 20;
			}
			try { SetNumberGroups(NumberFormatInfo.InvariantInfo, property, [3]); return 21; }
			catch (InvalidOperationException) { }
			SetNumberGroups(info, property, []);
			if (GetNumberGroups(info, property).Length != 0) return 22;
			SetNumberGroups(info, property, [1, 0]);
			if (GetNumberGroups(info, property)[1] != 0) return 23;
		}
		info.NegativeSign = "minus"; info.NumberNegativePattern = 1; info.NumberDecimalSeparator = ":"; info.NumberGroupSeparator = "_"; info.NumberGroupSizes = [1];
		var builder = new StringBuilder(1);
		builder.AppendFormat(info, "{0:N1}", new decimal(1234500, 0, 0, true, 4));
		GC.Collect();
		return builder.ToString() == "minus1_2_3:5" ? 42 : 24;
	}

	private static void SetNumberText(NumberFormatInfo info, int property, string value)
	{
		switch (property) {
		case 0: info.NegativeSign = value; break; case 1: info.PositiveSign = value; break;
		case 2: info.NumberDecimalSeparator = value; break; case 3: info.NumberGroupSeparator = value; break;
		case 4: info.CurrencyDecimalSeparator = value; break; case 5: info.CurrencyGroupSeparator = value; break;
		case 6: info.CurrencySymbol = value; break; case 7: info.PercentDecimalSeparator = value; break;
		case 8: info.PercentGroupSeparator = value; break; case 9: info.PercentSymbol = value; break;
		case 10: info.PerMilleSymbol = value; break; case 11: info.NaNSymbol = value; break;
		case 12: info.NegativeInfinitySymbol = value; break; default: info.PositiveInfinitySymbol = value; break;
		}
	}
	private static string GetNumberText(NumberFormatInfo info, int property) => property switch {
		0 => info.NegativeSign, 1 => info.PositiveSign, 2 => info.NumberDecimalSeparator, 3 => info.NumberGroupSeparator,
		4 => info.CurrencyDecimalSeparator, 5 => info.CurrencyGroupSeparator, 6 => info.CurrencySymbol,
		7 => info.PercentDecimalSeparator, 8 => info.PercentGroupSeparator, 9 => info.PercentSymbol, 10 => info.PerMilleSymbol,
		11 => info.NaNSymbol, 12 => info.NegativeInfinitySymbol, _ => info.PositiveInfinitySymbol
	};
	private static void SetNumberInteger(NumberFormatInfo info, int property, int value)
	{
		switch (property) {
		case 0: info.NumberDecimalDigits = value; break; case 1: info.NumberNegativePattern = value; break;
		case 2: info.CurrencyDecimalDigits = value; break; case 3: info.CurrencyNegativePattern = value; break;
		case 4: info.CurrencyPositivePattern = value; break; case 5: info.PercentDecimalDigits = value; break;
		case 6: info.PercentNegativePattern = value; break; default: info.PercentPositivePattern = value; break;
		}
	}
	private static int GetNumberInteger(NumberFormatInfo info, int property) => property switch {
		0 => info.NumberDecimalDigits, 1 => info.NumberNegativePattern, 2 => info.CurrencyDecimalDigits, 3 => info.CurrencyNegativePattern,
		4 => info.CurrencyPositivePattern, 5 => info.PercentDecimalDigits, 6 => info.PercentNegativePattern, _ => info.PercentPositivePattern
	};
	private static void SetNumberGroups(NumberFormatInfo info, int property, int[] value)
	{
		if (property == 0) info.NumberGroupSizes = value;
		else if (property == 1) info.CurrencyGroupSizes = value;
		else info.PercentGroupSizes = value;
	}
	private static int[] GetNumberGroups(NumberFormatInfo info, int property) => property == 0 ? info.NumberGroupSizes : property == 1 ? info.CurrencyGroupSizes : info.PercentGroupSizes;
}
