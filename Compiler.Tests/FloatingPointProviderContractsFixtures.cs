/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class FloatingContractProvider : IFormatProvider, ICustomFormatter
	{
		public object? Result, NextResult;
		public CultureInfo? NextCulture;
		public InvalidOperationException? Error;
		public int NumberQueries, CustomQueries, FormatCalls, ThrowNumberAt;
		public bool Wide, UseCustom, ReturnNull, ThrowDiscovery, ThrowFormat, Valid = true;

		public object? GetFormat(Type? type)
		{
			if (type == typeof(ICustomFormatter))
			{
				CustomQueries++;
				System.GC.Collect();
				if (ThrowDiscovery) throw Error!;
				return UseCustom ? this : null;
			}
			if (type != typeof(NumberFormatInfo)) { Valid = false; throw new InvalidOperationException(); }
			NumberQueries++;
			var result = Result;
			if (NumberQueries == 1 && NextResult is not null) Result = NextResult;
			if (NextCulture is not null) CultureInfo.CurrentCulture = NextCulture;
			System.GC.Collect();
			var pressure = new byte[64]; pressure[0] = 19;
			if (pressure[0] != 19) Valid = false;
			if (NumberQueries == ThrowNumberAt) throw Error!;
			return result;
		}

		public string Format(string? format, object? value, IFormatProvider? provider)
		{
			FormatCalls++;
			System.GC.Collect();
			if (format != "F2" || !ReferenceEquals(provider, this)) Valid = false;
			if (Wide ? value is not double number || number != -123.5 : value is not float single || single != -123.5f) Valid = false;
			if (ThrowFormat) throw Error!;
			return ReturnNull ? null! : new string("C\u03A9\0\uD800".AsSpan());
		}
	}

	private static NumberFormatInfo FloatingContractInfo(string sign, string separator) => new NumberFormatInfo {
		NegativeSign = new string(sign.AsSpan()), PositiveSign = "plus", NumberDecimalSeparator = new string(separator.AsSpan()),
		NumberGroupSeparator = "_", NumberGroupSizes = [1], CurrencySymbol = "coin", CurrencyNegativePattern = 1,
		CurrencyDecimalSeparator = ":", CurrencyGroupSeparator = "_", PercentSymbol = "pct", PercentNegativePattern = 0,
		PercentDecimalSeparator = ":", PercentGroupSeparator = "_"
	};

	private static StringBuilder AppendFloatingContract(StringBuilder builder, IFormatProvider? provider, bool wide,
		object box, CompositeFormat parsed, string source, string format, int form, int alignment = 0)
	{
		if (form <= 1)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(1, 1, builder, provider);
			handler.AppendLiteral("A");
			if (form == 1) handler.AppendFormatted(box, alignment, format);
			else if (wide) handler.AppendFormatted(-123.5, alignment, format);
			else handler.AppendFormatted(-123.5f, alignment, format);
			return builder.Append(ref handler);
		}
		if (form == 2) return wide ? builder.AppendFormat<double>(provider, parsed, -123.5) : builder.AppendFormat<float>(provider, parsed, -123.5f);
		if (form == 3) return wide ? builder.AppendFormat<double, int>(provider, parsed, -123.5, 7) : builder.AppendFormat<float, int>(provider, parsed, -123.5f, 7);
		if (form == 4) return wide ? builder.AppendFormat<double, int, int>(provider, parsed, -123.5, 7, 9) : builder.AppendFormat<float, int, int>(provider, parsed, -123.5f, 7, 9);
		if (form == 5) return builder.AppendFormat(provider, parsed, new object?[] { box });
		if (form == 6) return builder.AppendFormat(provider, parsed, new ReadOnlySpan<object?>(new object?[] { box }));
		return builder.AppendFormat(provider, source, box);
	}

	public static string CoreLibFloatingProviderTwoArgumentGrowthEntry()
	{
		var parsed = CompositeFormat.Parse("A{0:F2}");
		var results = new StringBuilder(80);
		for (var precision = 0; precision < 2; precision++)
		{
			var provider = new FloatingContractProvider { Wide = precision != 0,
				Result = FloatingContractInfo("old", "."), NextResult = FloatingContractInfo("new", ":") };
			var builder = new StringBuilder(5).Append("seed");
			if (precision == 0) builder.AppendFormat<float, int>(provider, parsed, -123.5f, 7);
			else builder.AppendFormat<double, int>(provider, parsed, -123.5, 7);
			System.GC.Collect();
			results.Append(builder.ToString()).Append('|').Append(provider.NumberQueries).Append('|')
				.Append(provider.CustomQueries).Append(provider.Valid ? "|valid" : "|invalid");
			if (precision == 0) results.Append('\n');
		}
		return results.ToString();
	}

	public static int CoreLibFloatingValueProviderContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			{
				var wide = precision != 0;
				string[] formats = ["G", "F3", "N1", "E2", "C1", "P1", "0000.00;[neg]0000.00;zero"];
				string[] expected = ["minus123:5", "minus123:500", "minus1_2_3:5", "minus1:24Eplus002", "minuscoin123:5", "minus12_350:0 pct", "[neg]0123:50"];
				for (var style = 0; style < formats.Length; style++)
				{
					var provider = new FloatingContractProvider { Wide = wide, Result = FloatingContractInfo("minus", ":") };
					var format = new string(formats[style].AsSpan());
					if ((wide ? (-123.5).ToString(format, provider) : (-123.5f).ToString(format, provider)) != expected[style] || provider.NumberQueries != 1 || provider.CustomQueries != 0) return 100 + style;
					for (var boundary = 0; boundary < 4; boundary++)
					{
						var length = boundary == 0 ? 0 : boundary == 1 ? expected[style].Length - 1 : expected[style].Length + boundary - 2;
						var storage = new char[length + 2];
						for (var index = 0; index < storage.Length; index++) storage[index] = '#';
						var before = provider.NumberQueries;
						var written = 77; var success = (wide ? (-123.5).TryFormat(storage.AsSpan(1, length), out written, format, provider) : (-123.5f).TryFormat(storage.AsSpan(1, length), out written, format, provider));
						System.GC.Collect();
						if (success != (length >= expected[style].Length) || written != (success ? expected[style].Length : 0) || provider.NumberQueries != before + 1 || provider.CustomQueries != 0 || !provider.Valid) return 200 + style;
						for (var index = 0; index < storage.Length; index++)
							if (storage[index] != (success && index > 0 && index <= written ? expected[style][index - 1] : '#')) return 300 + style;
					}
				}
				foreach (var invalid in new[] { "D", "D0", "X", "B", "Q", "G1000000000", "Q1000000000." })
				for (var operation = 0; operation < 2; operation++)
				{
					var provider = new FloatingContractProvider { Wide = wide, Result = FloatingContractInfo("minus", ":") };
					var storage = new char[1]; storage[0] = '#'; var written = 77;
					try
					{
						if (operation == 0) _ = (wide ? (-123.5).ToString(invalid, provider) : (-123.5f).ToString(invalid, provider));
						else _ = (wide ? (-123.5).TryFormat(storage, out written, invalid, provider) : (-123.5f).TryFormat(storage, out written, invalid, provider));
						return 4;
					}
					catch (FormatException) { }
					if (provider.NumberQueries != 1 || provider.CustomQueries != 0 || written != 77 || storage[0] != '#') return 5;
					var error = new InvalidOperationException("floating-query"); provider.Error = error; provider.ThrowNumberAt = 2;
					try { _ = (wide ? (-123.5).TryFormat(storage, out written, invalid, provider) : (-123.5f).TryFormat(storage, out written, invalid, provider)); return 6; }
					catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 7; }
					if (provider.NumberQueries != 2 || written != 77 || storage[0] != '#') return 8;
				}
				for (var mode = 0; mode < 3; mode++)
				{
					var oldCulture = new CultureInfo("") { NumberFormat = FloatingContractInfo("old", ".") };
					var nextCulture = new CultureInfo("") { NumberFormat = FloatingContractInfo("new", ":") };
					CultureInfo.CurrentCulture = oldCulture;
					var provider = new FloatingContractProvider { Wide = wide, Result = mode == 0 ? null : mode == 1 ? "wrong" : oldCulture.NumberFormat, NextCulture = nextCulture };
					oldCulture = null!; nextCulture = null!;
					var expectedText = mode == 2 ? "old123.50" : "new123:50";
					if ((wide ? (-123.5).ToString("F2", provider) : (-123.5f).ToString("F2", provider)) != expectedText || provider.NumberQueries != 1 || provider.CustomQueries != 0) return 9;
				}
				object box = wide ? (object)(-123.5) : -123.5f;
				var parsed = CompositeFormat.Parse("A{0:F2}");
				for (var form = 0; form < 8; form++)
				for (var roomy = 0; roomy < 2; roomy++)
				{
					var provider = new FloatingContractProvider { Wide = wide, Result = FloatingContractInfo("old", "."), NextResult = FloatingContractInfo("new", ":") };
					var builder = new StringBuilder(roomy == 0 ? 5 : 80).Append("seed");
					if (!ReferenceEquals(builder, AppendFloatingContract(builder, provider, wide, box, parsed, "A{0:F2}", "F2", form))) return 10;
					var snapshot = builder.ToString(); System.GC.Collect(); builder.Clear().Append("tail");
					if (snapshot != (roomy == 0 ? "seedAnew123:50" : "seedAold123.50") || provider.NumberQueries != (roomy == 0 ? 2 : 1) || provider.CustomQueries != (roomy == 0 && form < 7 ? 2 : 1) || !provider.Valid || builder.ToString() != "tail") return 1000 + form * 10 + roomy;
				}
				for (var form = 0; form < 8; form++)
				for (var mode = 0; mode < 4; mode++)
				{
					var error = new InvalidOperationException("floating-custom");
					var provider = new FloatingContractProvider { Wide = wide, Result = FloatingContractInfo("old", "."), UseCustom = true, ReturnNull = mode == 1, ThrowDiscovery = mode == 2, ThrowFormat = mode == 3, Error = error };
					var builder = new StringBuilder(5).Append("seed");
					try { AppendFloatingContract(builder, provider, wide, box, parsed, "A{0:F2}", "F2", form); if (mode >= 2) return 11; }
					catch (InvalidOperationException actual) { if (mode < 2 || !ReferenceEquals(actual, error)) return 12; }
					var expectedText = mode == 0 ? "seedAC\u03A9\0\uD800" : mode == 1 && form == 7 ? "seedAold123.50" : mode == 2 ? "seed" : "seedA";
					if (builder.ToString() != expectedText || provider.NumberQueries != (mode == 1 && form == 7 ? 2 : 0) || provider.FormatCalls != (mode == 2 ? 0 : 1) || provider.CustomQueries != (mode == 2 || form == 7 ? 1 : 2) || !provider.Valid) return 2000 + form * 10 + mode;
				}
				for (var form = 0; form < 8; form++)
				for (var failAt = 1; failAt <= 2; failAt++)
				{
					var error = new InvalidOperationException("floating-builder-query");
					var provider = new FloatingContractProvider { Wide = wide, Result = FloatingContractInfo("old", "."), Error = error, ThrowNumberAt = failAt };
					var builder = new StringBuilder(5).Append("seed");
					try { AppendFloatingContract(builder, provider, wide, box, parsed, "A{0:F2}", "F2", form); return 13; }
					catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 14; }
					var snapshot = builder.ToString();
					if (snapshot != "seedA" || provider.NumberQueries != failAt || provider.CustomQueries != (failAt == 1 || form == 7 ? 1 : 2) || !provider.Valid) return 3000 + form * 10 + failAt;
					provider.ThrowNumberAt = 0;
					builder.Length = 4;
					AppendFloatingContract(builder, provider, wide, box, parsed, "A{0:F2}", "F2", form);
					System.GC.Collect();
					if (builder.ToString() != "seedAold123.50" || snapshot != "seedA") return 15;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

}
