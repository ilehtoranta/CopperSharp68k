/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class DecimalContractProvider : IFormatProvider, ICustomFormatter
	{
		public object? Result, NextResult;
		public CultureInfo? NextCulture;
		public InvalidOperationException? Error;
		public int NumberQueries, CustomQueries, FormatCalls, ThrowNumberAt;
		public bool UseCustom, ReturnNull, ThrowDiscovery, ThrowFormat, Valid = true;

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
			if (format != "F2" || !ReferenceEquals(provider, this) || value is not decimal number) Valid = false;
			else
			{
				Span<int> bits = stackalloc int[4]; decimal.GetBits(number, bits);
				if (bits[0] != 1234500 || bits[1] != 0 || bits[2] != 0 || bits[3] != unchecked((int)0x80040000)) Valid = false;
			}
			if (ThrowFormat) throw Error!;
			return ReturnNull ? null! : new string("C\u03A9\0\uD800".AsSpan());
		}
	}

	private static NumberFormatInfo DecimalContractInfo(string sign, string separator) => new NumberFormatInfo {
		NegativeSign = new string(sign.AsSpan()), PositiveSign = "plus", NumberDecimalSeparator = new string(separator.AsSpan()),
		NumberGroupSeparator = "_", NumberGroupSizes = [1], CurrencySymbol = "coin", CurrencyNegativePattern = 1,
		CurrencyDecimalSeparator = ":", CurrencyGroupSeparator = "_", PercentSymbol = "pct", PercentNegativePattern = 0,
		PercentDecimalSeparator = ":", PercentGroupSeparator = "_"
	};

	private static StringBuilder AppendDecimalContract(StringBuilder builder, IFormatProvider? provider, decimal value,
		object box, CompositeFormat parsed, string source, string format, int form, int alignment = 0)
	{
		if (form <= 1)
		{
			var handler = new StringBuilder.AppendInterpolatedStringHandler(1, 1, builder, provider);
			handler.AppendLiteral("A");
			if (form == 0) handler.AppendFormatted(value, alignment, format);
			else handler.AppendFormatted(box, alignment, format);
			return builder.Append(ref handler);
		}
		if (form == 2) return builder.AppendFormat<decimal>(provider, parsed, value);
		if (form == 3) return builder.AppendFormat<decimal, int>(provider, parsed, value, 7);
		if (form == 4) return builder.AppendFormat<decimal, int, int>(provider, parsed, value, 7, 9);
		if (form == 5) return builder.AppendFormat(provider, parsed, new object?[] { box });
		if (form == 6) return builder.AppendFormat(provider, parsed, new ReadOnlySpan<object?>(new object?[] { box }));
		return builder.AppendFormat(provider, source, box);
	}

	public static int CoreLibDecimalValueProviderContractsEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		try
		{
			var value = new decimal(1234500, 0, 0, true, 4);
			string[] formats = ["G", "F3", "N1", "E2", "C1", "P1", "0000.00;[neg]0000.00;zero"];
			string[] expected = ["minus123:4500", "minus123:450", "minus1_2_3:5", "minus1:23Eplus002", "minuscoin123:5", "minus12_345:0 pct", "[neg]0123:45"];
			for (var style = 0; style < formats.Length; style++)
			{
				var provider = new DecimalContractProvider { Result = DecimalContractInfo("minus", ":") };
				var format = new string(formats[style].AsSpan());
				if (value.ToString(format, provider) != expected[style] || provider.NumberQueries != 1 || provider.CustomQueries != 0) return 100 + style;
				for (var boundary = 0; boundary < 4; boundary++)
				{
					var length = boundary == 0 ? 0 : boundary == 1 ? expected[style].Length - 1 : expected[style].Length + boundary - 2;
					var storage = new char[length + 2];
					for (var index = 0; index < storage.Length; index++) storage[index] = '#';
					var before = provider.NumberQueries;
					var success = value.TryFormat(storage.AsSpan(1, length), out var written, format, provider);
					System.GC.Collect();
					if (success != (length >= expected[style].Length) || written != (success ? expected[style].Length : 0) || provider.NumberQueries != before + 1 || provider.CustomQueries != 0 || !provider.Valid) return 200 + style;
					for (var index = 0; index < storage.Length; index++)
						if (storage[index] != (success && index > 0 && index <= written ? expected[style][index - 1] : '#')) return 300 + style;
				}
			}
			foreach (var invalid in new[] { "D", "D0", "X", "B", "Q", "G1000000000", "Q1000000000." })
			for (var operation = 0; operation < 2; operation++)
			{
				var provider = new DecimalContractProvider { Result = DecimalContractInfo("minus", ":") };
				var storage = new char[1]; storage[0] = '#'; var written = 77;
				try
				{
					if (operation == 0) _ = value.ToString(invalid, provider);
					else value.TryFormat(storage, out written, invalid, provider);
					return 4;
				}
				catch (FormatException) { }
				if (provider.NumberQueries != 1 || provider.CustomQueries != 0 || written != 77 || storage[0] != '#') return 5;
				var error = new InvalidOperationException("decimal-query"); provider.Error = error; provider.ThrowNumberAt = 2;
				try { value.TryFormat(storage, out written, invalid, provider); return 6; }
				catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 7; }
				if (provider.NumberQueries != 2 || written != 77 || storage[0] != '#') return 8;
			}
			for (var mode = 0; mode < 3; mode++)
			{
				var oldCulture = new CultureInfo("") { NumberFormat = DecimalContractInfo("old", ".") };
				var nextCulture = new CultureInfo("") { NumberFormat = DecimalContractInfo("new", ":") };
				CultureInfo.CurrentCulture = oldCulture;
				var provider = new DecimalContractProvider { Result = mode == 0 ? null : mode == 1 ? "wrong" : oldCulture.NumberFormat, NextCulture = nextCulture };
				oldCulture = null!; nextCulture = null!;
				var expectedText = mode == 2 ? "old123.45" : "new123:45";
				if (value.ToString("F2", provider) != expectedText || provider.NumberQueries != 1 || provider.CustomQueries != 0) return 9;
			}
			object box = value;
			var parsed = CompositeFormat.Parse("A{0:F2}");
			for (var form = 0; form < 8; form++)
			for (var roomy = 0; roomy < 2; roomy++)
			{
				var provider = new DecimalContractProvider { Result = DecimalContractInfo("old", "."), NextResult = DecimalContractInfo("new", ":") };
				var builder = new StringBuilder(roomy == 0 ? 5 : 80).Append("seed");
				if (!ReferenceEquals(builder, AppendDecimalContract(builder, provider, value, box, parsed, "A{0:F2}", "F2", form))) return 10;
				var snapshot = builder.ToString(); System.GC.Collect(); builder.Clear().Append("tail");
				if (snapshot != (roomy == 0 ? "seedAnew123:45" : "seedAold123.45") || provider.NumberQueries != (roomy == 0 ? 2 : 1) || provider.CustomQueries != (roomy == 0 && form < 7 ? 2 : 1) || !provider.Valid || builder.ToString() != "tail") return 1000 + form * 10 + roomy;
			}
			for (var form = 0; form < 8; form++)
			for (var mode = 0; mode < 4; mode++)
			{
				var error = new InvalidOperationException("decimal-custom");
				var provider = new DecimalContractProvider { Result = DecimalContractInfo("old", "."), UseCustom = true, ReturnNull = mode == 1, ThrowDiscovery = mode == 2, ThrowFormat = mode == 3, Error = error };
				var builder = new StringBuilder(5).Append("seed");
				try { AppendDecimalContract(builder, provider, value, box, parsed, "A{0:F2}", "F2", form); if (mode >= 2) return 11; }
				catch (InvalidOperationException actual) { if (mode < 2 || !ReferenceEquals(actual, error)) return 12; }
				var expectedText = mode == 0 ? "seedAC\u03A9\0\uD800" : mode == 1 && form == 7 ? "seedAold123.45" : mode == 2 ? "seed" : "seedA";
				if (builder.ToString() != expectedText || provider.NumberQueries != (mode == 1 && form == 7 ? 2 : 0) || provider.FormatCalls != (mode == 2 ? 0 : 1) || provider.CustomQueries != (mode == 2 || form == 7 ? 1 : 2) || !provider.Valid) return 2000 + form * 10 + mode;
			}
			for (var form = 0; form < 8; form++)
			for (var failAt = 1; failAt <= 2; failAt++)
			{
				var error = new InvalidOperationException("decimal-builder-query");
				var provider = new DecimalContractProvider { Result = DecimalContractInfo("old", "."), Error = error, ThrowNumberAt = failAt };
				var builder = new StringBuilder(5).Append("seed");
				try { AppendDecimalContract(builder, provider, value, box, parsed, "A{0:F2}", "F2", form); return 13; }
				catch (InvalidOperationException actual) { if (!ReferenceEquals(actual, error)) return 14; }
				var snapshot = builder.ToString();
				if (snapshot != "seedA" || provider.NumberQueries != failAt || provider.CustomQueries != (failAt == 1 || form == 7 ? 1 : 2) || !provider.Valid) return 3000 + form * 10 + failAt;
				provider.ThrowNumberAt = 0;
				builder.Length = 4;
				AppendDecimalContract(builder, provider, value, box, parsed, "A{0:F2}", "F2", form);
				System.GC.Collect();
				if (builder.ToString() != "seedAold123.45" || snapshot != "seedA") return 15;
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibDecimalAllocationContractsEntry()
	{
		var info = NumberFormatInfo.InvariantInfo;
		var value = new decimal(1234500, 0, 0, true, 4);
		var shortBuffer = new char[7];
		if (!CopperSharp.Runtime.ShadowDecimalFormatting.TryFormatDecimal(value, "F2", info, shortBuffer, out _)) return 1;
		SetStringBuilderAllocationFailure(1);
		var shortSuccess = CopperSharp.Runtime.ShadowDecimalFormatting.TryFormatDecimal(value, "F2", info, shortBuffer, out var shortWritten);
		SetStringBuilderAllocationFailure(0);
		if (!shortSuccess || shortWritten != 7) return 2;
		SetStringBuilderAllocationFailure(2);
		var shortText = CopperSharp.Runtime.ShadowDecimalFormatting.FormatDecimal(value, "F2", info);
		SetStringBuilderAllocationFailure(0);
		if (shortText != "-123.45") return 3;
		SetStringBuilderAllocationFailure(1);
		try { _ = CopperSharp.Runtime.ShadowDecimalFormatting.FormatDecimal(value, "F2", info); SetStringBuilderAllocationFailure(0); return 4; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (CopperSharp.Runtime.ShadowDecimalFormatting.FormatDecimal(value, "F2", info) != "-123.45") return 5;
		for (var style = 0; style < 2; style++)
		{
			var format = style == 0 ? "F80" : new StringBuilder().Append('0', 40).Append(".0000").ToString();
			var expected = style == 0 ? new StringBuilder("-123.45").Append('0', 78).ToString() :
				new StringBuilder("-").Append('0', 37).Append("123.4500").ToString();
			for (var operation = 0; operation < 3; operation++)
			{
				var reachedSuccess = false;
				for (var failAt = 1; failAt <= 16; failAt++)
				{
					var buffer = new char[expected.Length + 2];
					for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
					var written = 77; var threw = false;
					SetStringBuilderAllocationFailure(failAt);
					try
					{
						if (operation == 0)
						{
							if (!CopperSharp.Runtime.ShadowDecimalFormatting.TryFormatDecimal(value, format, info, buffer.AsSpan(1, expected.Length), out written) || written != expected.Length) { SetStringBuilderAllocationFailure(0); return 6; }
						}
						else if (operation == 1)
						{
							if (CopperSharp.Runtime.ShadowDecimalFormatting.FormatDecimal(value, format, info) != expected) { SetStringBuilderAllocationFailure(0); return 7; }
						}
						else if (CopperSharp.Runtime.ShadowDecimalFormatting.TryFormatDecimal(value, format, info, buffer.AsSpan(1, 1), out written) || written != 0) { SetStringBuilderAllocationFailure(0); return 8; }
						SetStringBuilderAllocationFailure(0);
					}
					catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); threw = true; }
					if (buffer[0] != '#' || buffer[^1] != '#') return 9;
					if (!threw && operation == 0 && buffer.AsSpan(1, written).ToString() != expected) return 20;
					if (threw || operation != 0)
					{
						if (written != (threw || operation == 1 ? 77 : 0)) return 10;
						for (var index = 0; index < buffer.Length; index++) if (buffer[index] != '#') return 11;
					}
					if (threw)
					{
						if (!CopperSharp.Runtime.ShadowDecimalFormatting.TryFormatDecimal(value, format, info, buffer.AsSpan(1, expected.Length), out written) || written != expected.Length || buffer.AsSpan(1, written).ToString() != expected) return 12;
					}
					else { reachedSuccess = true; break; }
				}
				if (!reachedSuccess) return 13;
			}
			object box = value;
			var width = expected.Length + 5;
			var source = new StringBuilder().Append("A{0,").Append(width.ToString(info))
				.Append(':').Append(format).Append('}').ToString();
			var parsed = CompositeFormat.Parse(source);
			var complete = "seedA     " + expected;
			for (var form = 0; form < 8; form++)
			for (var roomy = 0; roomy < 2; roomy++)
			{
				var reachedSuccess = false;
				for (var failAt = 1; failAt <= 24; failAt++)
				{
					var builder = new StringBuilder(roomy == 0 ? 5 : 160).Append("seed");
					var threw = false;
					SetStringBuilderAllocationFailure(failAt);
					try { AppendDecimalContract(builder, info, value, box, parsed, source, format, form, width); SetStringBuilderAllocationFailure(0); }
					catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); threw = true; }
					var snapshot = builder.ToString();
					if (snapshot.Length < 4 || snapshot.Length > complete.Length) return 14;
					for (var index = 0; index < snapshot.Length; index++) if (snapshot[index] != complete[index]) return 15;
					if (threw)
					{
						builder.Length = 4;
						AppendDecimalContract(builder, info, value, box, parsed, source, format, form, width);
						if (builder.ToString() != complete) return 16;
					}
					else if (snapshot != complete) return 17;
					builder.Clear().Append("tail");
					if (builder.ToString() != "tail") return 18;
					if (!threw) { reachedSuccess = true; break; }
				}
				if (!reachedSuccess) return 19;
			}
		}
		return 42;
	}
}
