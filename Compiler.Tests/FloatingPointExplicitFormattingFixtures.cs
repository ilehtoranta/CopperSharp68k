/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static string FloatingExplicitFormat(int scenario) => scenario switch
	{
		0 => "F", 1 => "f0", 2 => "F3", 3 => "N", 4 => "n0", 5 => "N3",
		6 => "C", 7 => "c0", 8 => "C3", 9 => "P", 10 => "p0", 11 => "P3",
		12 => "E", 13 => "e2", 14 => "E0", 15 => "G", 16 => "g3", 17 => "G1",
		18 => "R", 19 => "r8", 20 => "F2", 21 => "F0", 22 or 23 => "F1",
		24 => "G9", 25 => "G17", 26 => "R", 27 => "G5", 28 => "F2", 29 => "P0",
		30 => "000.00", 31 or 32 => "000.00;(000.00);'zero'", 33 => "#,##0.00",
		34 => "0.0E+00", 35 => "0.0e-00", 36 => "0.0%", 37 => "0.0\u2030",
		38 => "0,", 39 => "'x'0'\u03A9'", 40 => "0\\;\\#\\%", 41 => "0\0ignored",
		42 => "#", 43 => "0.00;-0.00;'zero'", _ => "0.00"
	};

	public static double FloatingExplicitDouble(int scenario) => scenario switch
	{
		20 or 21 => 2.5, 22 or 45 => 1.25, 23 => 1.75, 24 or 25 => 0.1,
		26 => double.Epsilon, 27 => double.MaxValue, 28 or 29 or 43 => -0d,
		30 or 31 => -12.5, 32 or 42 => 0, 36 or 37 => -0.125,
		38 => 1234500, 39 or 40 or 41 => 12.5, 44 => 0.125, _ => -1234.5
	};

	public static float FloatingExplicitSingle(int scenario) => scenario switch
	{
		26 => float.Epsilon, 27 => float.MaxValue, _ => (float)FloatingExplicitDouble(scenario)
	};

	public static NumberFormatInfo FloatingExplicitInfo(bool custom) => new()
	{
		NegativeSign = custom ? "minus" : "-", PositiveSign = custom ? "p" : "+",
		NumberDecimalSeparator = custom ? ":" : ".", NumberGroupSeparator = custom ? "_" : ",",
		NumberGroupSizes = [3], NumberDecimalDigits = 2,
		CurrencySymbol = custom ? "cur" : "\u00A4", CurrencyDecimalSeparator = custom ? ":" : ".",
		CurrencyGroupSeparator = custom ? "_" : ",", CurrencyGroupSizes = [3], CurrencyDecimalDigits = 2,
		CurrencyNegativePattern = 0, CurrencyPositivePattern = 0,
		PercentSymbol = custom ? "pct" : "%", PerMilleSymbol = custom ? "pm" : "\u2030",
		PercentDecimalSeparator = custom ? ":" : ".", PercentGroupSeparator = custom ? "_" : ",",
		PercentGroupSizes = [3], PercentDecimalDigits = 2, PercentNegativePattern = 0, PercentPositivePattern = 0
	};

	public static string FloatingExplicitExpected(int scenario, bool wide, bool custom)
	{
		var expected = scenario switch
		{
			0 => "-1234.50", 1 => "-1234", 2 => "-1234.500", 3 => "-1,234.50", 4 => "-1,234", 5 => "-1,234.500",
			6 => "(\u00A41,234.50)", 7 => "(\u00A41,234)", 8 => "(\u00A41,234.500)",
			9 => "-123,450.00 %", 10 => "-123,450 %", 11 => "-123,450.000 %",
			12 => "-1.234500E+003", 13 => "-1.23e+003", 14 => "-1E+003", 16 => "-1.23e+03", 17 => "-1E+03",
			15 or 18 or 19 => "-1234.5", 20 => "2.50", 21 => "2", 22 => "1.2", 23 => "1.8",
			24 => wide ? "0.1" : "0.100000001", 25 => wide ? "0.10000000000000001" : "0.10000000149011612",
			26 => wide ? "5E-324" : "1E-45", 27 => wide ? "1.7977E+308" : "3.4028E+38", 28 => "-0.00", 29 => "-0 %",
			30 => "-012.50", 31 => "(012.50)", 32 or 43 => "zero", 33 => "-1,234.50",
			34 => "-1.2E+03", 35 => "-1.2e03", 36 => "-12.5%", 37 => "-125.0\u2030",
			38 => "1235", 39 => "x13\u03A9", 40 => "13;#%", 41 => "13", 42 => "", 44 => "0.13", _ => "1.25"
		};
		if (!custom) return expected;
		// Replace only provider-controlled punctuation, preserving quoted and escaped literals.
		var builder = new StringBuilder(64);
		for (var index = 0; index < expected.Length; index++)
		{
			var character = expected[index];
			if (character == '-') builder.Append("minus");
			else if (character == '+') builder.Append('p');
			else if (character == '.') builder.Append(':');
			else if (character == ',') builder.Append('_');
			else if (character == '\u00A4') builder.Append("cur");
			else if (character == '\u2030') builder.Append("pm");
			else if (character == '%' && scenario != 40) builder.Append("pct");
			else builder.Append(character);
		}
		return builder.ToString();
	}

	public static int CoreLibFloatingPointStandardFormatsEntry() => RunFloatingExplicitFormats(0, 30);
	public static int CoreLibFloatingPointCustomFormatsEntry() => RunFloatingExplicitFormats(30, 46);

	public static int CoreLibStringBuilderFloatingExplicitFormatsEntry()
	{
		for (var scenario = 0; scenario < 46; scenario++)
		for (var custom = 0; custom < 2; custom++)
		for (var wide = 0; wide < 2; wide++)
		for (var roomy = 0; roomy < 2; roomy++)
		{
			var info = FloatingExplicitInfo(custom != 0);
			var format = new string(FloatingExplicitFormat(scenario).AsSpan());
			var expected = FloatingExplicitExpected(scenario, wide != 0, custom != 0);
			var single = FloatingExplicitSingle(scenario); var value = FloatingExplicitDouble(scenario);
			object boxed = wide == 0 ? (object)single : value;
			var builder = new StringBuilder(roomy == 0 ? 1 : 80).Append("seed");
			System.GC.Collect();
			builder.AppendFormat(info, "[{0:" + format + "}]", boxed);
			if (builder.ToString() != "seed[" + expected + "]") return 4000 + scenario * 10 + wide;
			var snapshot = builder.ToString();
			builder.Clear().Append("seed");
			var alignment = roomy == 0 ? 24 : -24;
			var padding = new StringBuilder(24).Append(' ', expected.Length < 24 ? 24 - expected.Length : 0).ToString();
			var aligned = roomy == 0 ? padding + expected : expected + padding;
			var handler = new StringBuilder.AppendInterpolatedStringHandler(2, 1, builder, info);
			handler.AppendLiteral("[");
			if (wide == 0) handler.AppendFormatted(single, alignment, format); else handler.AppendFormatted(value, alignment, format);
			handler.AppendLiteral("]"); builder.Append(ref handler);
			if (builder.ToString() != "seed[" + aligned + "]") return 5000 + scenario * 10 + wide;
			builder.Clear().Append("seed");
			var parsed = CompositeFormat.Parse("[{0:" + format + "}]");
			if (wide == 0) builder.AppendFormat(info, parsed, single); else builder.AppendFormat(info, parsed, value);
			if (builder.ToString() != "seed[" + expected + "]") return 6000 + scenario * 10 + wide;
			builder.Clear().Append("seed").AppendFormat(info, parsed, [boxed]);
			System.GC.Collect();
			if (builder.ToString() != "seed[" + expected + "]" || snapshot != "seed[" + expected + "]") return 7000 + scenario * 10 + wide;
			builder.Clear().Append("reuse");
			if (builder.ToString() != "reuse") return 8000 + scenario;
		}
		return 42;
	}

	public static int CoreLibFloatingBoxedFormatProbeEntry()
	{
		object single = -12.5f;
		var builder = new StringBuilder(80);
		builder.AppendFormat(NumberFormatInfo.InvariantInfo, "{0:F2}", single);
		if (builder.ToString() != "-12.50") return 1;
		object value = -12.5d;
		builder.Clear().AppendFormat(NumberFormatInfo.InvariantInfo, "{0:F2}", value);
		return builder.ToString() == "-12.50" ? 42 : 2;
	}

	public static int CoreLibFloatingHandlerFormatProbeEntry()
	{
		var builder = new StringBuilder(80);
		var handler = new StringBuilder.AppendInterpolatedStringHandler(1, 2, builder, NumberFormatInfo.InvariantInfo);
		handler.AppendFormatted(-12.5f, 12, "F2");
		handler.AppendLiteral("|");
		handler.AppendFormatted(-12.5d, -12, "F2");
		builder.Append(ref handler);
		return builder.ToString() == "      -12.50|-12.50      " ? 42 : 1;
	}

	public static int CoreLibFloatingParsedFormatProbeEntry()
	{
		var builder = new StringBuilder(80);
		var parsed = CompositeFormat.Parse("[{0:F2}]");
		builder.AppendFormat(NumberFormatInfo.InvariantInfo, parsed, -12.5f);
		if (builder.ToString() != "[-12.50]") return 1;
		builder.Clear().AppendFormat(NumberFormatInfo.InvariantInfo, parsed, -12.5d);
		if (builder.ToString() != "[-12.50]") return 2;
		object single = -12.5f;
		builder.Clear().AppendFormat(NumberFormatInfo.InvariantInfo, parsed, [single]);
		if (builder.ToString() != "[-12.50]") return 3;
		object value = -12.5d;
		builder.Clear().AppendFormat(NumberFormatInfo.InvariantInfo, parsed, [value]);
		return builder.ToString() == "[-12.50]" ? 42 : 4;
	}

	private static int RunFloatingExplicitFormats(int start, int end)
	{
		for (var scenario = start; scenario < end; scenario++)
		for (var custom = 0; custom < 2; custom++)
		for (var wide = 0; wide < 2; wide++)
		{
			var info = FloatingExplicitInfo(custom != 0);
			var format = new string(FloatingExplicitFormat(scenario).AsSpan());
			var expected = FloatingExplicitExpected(scenario, wide != 0, custom != 0);
			var single = FloatingExplicitSingle(scenario); var value = FloatingExplicitDouble(scenario);
			var text = wide == 0 ? single.ToString(format, info) : value.ToString(format, info);
			System.GC.Collect();
			if (text != expected) return 1000 + scenario * 10 + custom * 2 + wide;
			for (var boundary = 0; boundary < 4; boundary++)
			{
				var size = boundary == 0 ? 0 : boundary == 1 && expected.Length != 0 ? expected.Length - 1 : expected.Length + boundary - 2;
				if (size < 0) size = 0;
				var storage = new char[size + 2];
				for (var index = 0; index < storage.Length; index++) storage[index] = '#';
				var success = wide == 0 ? single.TryFormat(storage.AsSpan(1, size), out var written, format.AsSpan(), info) :
					value.TryFormat(storage.AsSpan(1, size), out written, format.AsSpan(), info);
				System.GC.Collect();
				if (success != (size >= expected.Length) || written != (success ? expected.Length : 0)) return 2000 + scenario * 10 + boundary;
				for (var index = 0; index < storage.Length; index++)
					if (storage[index] != (success && index > 0 && index <= written ? expected[index - 1] : '#')) return 3000 + scenario * 10 + boundary;
			}
		}
		return 42;
	}
}
