/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Runtime.CompilerServices;
using CopperSharp.Compiler;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static NumberFormatInfo PercentInfo(int style, int pattern) => new()
	{
		NegativeSign = new string("\u2212\0\u03A9".AsSpan()), PercentSymbol = new string("%#\0\uD800".AsSpan()),
		PercentDecimalSeparator = new string("\u03A9\0\uD83D\uDE00".AsSpan()), PercentGroupSeparator = new string("\0\uD800".AsSpan()),
		PercentDecimalDigits = 4, PercentNegativePattern = pattern, PercentPositivePattern = pattern % 4,
		PercentGroupSizes = style switch { 0 => [3], 1 => [3, 2], 2 => [3, 2, 0], 3 => [1, 0], 4 => [], 5 => [0], 6 => [1], _ => [9] },
		NumberDecimalSeparator = "wrong", NumberGroupSeparator = "wrong", NumberGroupSizes = [1], NumberDecimalDigits = 7, NumberNegativePattern = 4,
		CurrencyDecimalSeparator = "wrong", CurrencyGroupSeparator = "wrong", CurrencyGroupSizes = [1], CurrencyDecimalDigits = 9, CurrencySymbol = "wrong", CurrencyNegativePattern = 16
	};

	private static string PercentFormat(int scenario) => scenario == 0 ? "P" : scenario == 1 ? "p0" : scenario == 2 ? "P3" : "p65";
	private static string PercentComposite(int scenario) => scenario == 0 ? "{0:P}" : scenario == 1 ? "{0:p0}" : scenario == 2 ? "{0:P3}" : "{0:p65}";

	private static string PercentMagnitude(int width, bool custom) => width switch
	{
		0 => "12,800", 1 => "25,500", 2 => custom ? "32,76,800" : "3,276,800", 3 => custom ? "65,53,500" : "6,553,500",
		4 => custom ? "2,14,74,83,64,800" : "214,748,364,800", 5 => custom ? "4,29,49,67,29,500" : "429,496,729,500",
		6 => custom ? "92,23,37,20,36,85,47,75,80,800" : "922,337,203,685,477,580,800",
		_ => custom ? "1,84,46,74,40,73,70,95,51,61,500" : "1,844,674,407,370,955,161,500"
	};

	private static string PercentContractMagnitude(int style, bool unsigned) => style switch
	{
		0 => unsigned ? "1,844,674,407,370,955,161,500" : "922,337,203,685,477,580,800",
		1 => unsigned ? "1,84,46,74,40,73,70,95,51,61,500" : "92,23,37,20,36,85,47,75,80,800",
		2 => unsigned ? "18446744073709551,61,500" : "9223372036854775,80,800",
		3 => unsigned ? "184467440737095516150,0" : "92233720368547758080,0",
		4 or 5 => unsigned ? "1844674407370955161500" : "922337203685477580800",
		6 => unsigned ? "1,8,4,4,6,7,4,4,0,7,3,7,0,9,5,5,1,6,1,5,0,0" : "9,2,2,3,3,7,2,0,3,6,8,5,4,7,7,5,8,0,8,0,0",
		_ => unsigned ? "1844,674407370,955161500" : "922,337203685,477580800"
	};

	// Group locations come from literal scaled magnitude oracles. These
	// prefix/suffix decorations are checked against official host formatting.
	private static string PercentExpected(string magnitude, bool negative, bool custom, int pattern, int precision)
	{
		var prefix = negative ? pattern switch
		{
			0 or 1 => "-", 2 => "-%", 3 => "%-", 4 => "%", 5 or 6 or 8 or 11 => "", 7 => "-% ", 9 => "% ", _ => "% -"
		} : pattern switch { 0 or 1 => "", 2 => "%", _ => "% " };
		var suffix = negative ? pattern switch
		{
			0 => " %", 1 => "%", 2 or 3 or 7 or 10 => "", 4 or 9 => "-", 5 => "-%", 6 => "%-", 8 => " %-", _ => "- %"
		} : pattern switch { 0 => " %", 1 => "%", _ => "" };
		var text = new System.Text.StringBuilder(256);
		for (var part = 0; part < 3; part++)
		{
			if (part == 1)
			{
				for (var index = 0; index < magnitude.Length; index++)
					if (magnitude[index] == ',') text.Append(custom ? "\0\uD800" : ","); else text.Append(magnitude[index]);
				if (precision != 0) text.Append(custom ? "\u03A9\0\uD83D\uDE00" : ".").Append('0', precision);
			}
			else
			{
				var decoration = part == 0 ? prefix : suffix;
				for (var index = 0; index < decoration.Length; index++)
					if (decoration[index] == '%') text.Append(custom ? "%#\0\uD800" : "%");
					else if (decoration[index] == '-') text.Append(custom ? "\u2212\0\u03A9" : "-");
					else text.Append(decoration[index]);
			}
		}
		return text.ToString(0, text.Length);
	}

	private sealed class CoreLibPercentProvider : IFormatProvider
	{
		public NumberFormatInfo? Info;
		public int Mode, NumberQueries, CustomQueries;
		public InvalidOperationException? Error;
		public object? GetFormat(Type? formatType)
		{
			M68kRuntime.Collect();
			if (formatType == typeof(ICustomFormatter)) { CustomQueries++; return null; }
			if (formatType != typeof(NumberFormatInfo)) throw new InvalidOperationException("type query");
			NumberQueries++;
			if (Mode == 1) return null;
			if (Mode == 2) return "wrong";
			if (Mode == 3) throw Error!;
			if (Mode == 4)
			{
				Info!.NegativeSign = NumberQueries == 1 ? "first" : "second";
				Info.PercentSymbol = NumberQueries == 1 ? "one" : "two";
				Info.PercentDecimalSeparator = NumberQueries == 1 ? ":" : "::";
				Info.PercentGroupSeparator = NumberQueries == 1 ? "a" : "b";
				Info.PercentGroupSizes = NumberQueries == 1 ? [3] : [2];
				Info.PercentNegativePattern = NumberQueries == 1 ? 0 : 11;
				Info.PercentPositivePattern = NumberQueries == 1 ? 0 : 3;
				Info.PercentDecimalDigits = NumberQueries == 1 ? 1 : 3;
			}
			return Info;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibPercentSmokeEntry()
	{
		Span<char> storage = stackalloc char[64];
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12345, -1, "P3", null, storage, out var count)) return 1;
		if (new string(storage.Slice(0, count)) != "-1,234,500.000 %") return 2;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(-12345, -1, "P3", null) != "-1,234,500.000 %") return 3;
		var info = new NumberFormatInfo { PercentGroupSizes = [2], PercentNegativePattern = 11, PercentSymbol = "ratio" };
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12345, -1, "P3", info, storage, out count)) return 4;
		if (new string(storage.Slice(0, count)) != "1,23,45,00.000- ratio") return 5;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(-12345, -1, "P3", info) != "1,23,45,00.000- ratio") return 6;
		if (!CopperSharp.Runtime.ShadowGroupedIntegerFormatting.TryFormatPercent(0, 12345, false, info, 3, storage, out count)) return 7;
		return new string(storage.Slice(0, count)) == "1,23,45,00.000 ratio" ? 42 : 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderPercentIntegersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var supplied = new CoreLibPercentProvider { Info = PercentInfo(1, 10) };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? supplied.Info : supplied;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				var custom = providerCase != 0;
				var expected = PercentExpected(PercentMagnitude(width, custom), (width & 1) == 0, custom,
					custom ? (width & 1) == 0 ? 10 : 2 : 0, GroupedPrecision(formatCase, custom));
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 256);
				supplied.NumberQueries = 0; supplied.CustomQueries = 0; M68kRuntime.Collect();
				if (builder.AppendFormat(provider, PercentComposite(formatCase), StandardIntegerValue(width * 4)) != builder) return 1;
				var snapshot = builder.ToString(0, builder.Length);
				if (snapshot != expected || providerCase == 2 && (supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1)) return 2;
				builder.Clear().Append("changed"); M68kRuntime.Collect(); if (snapshot != expected) return 3;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibPercentIntegerSpanHelpersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var supplied = new CoreLibPercentProvider { Info = PercentInfo(1, 10) };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? supplied.Info : supplied;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			{
				var custom = providerCase != 0;
				var expected = PercentExpected(PercentMagnitude(width, custom), (width & 1) == 0, custom,
					custom ? (width & 1) == 0 ? 10 : 2 : 0, GroupedPrecision(formatCase, custom));
				var value = StandardIntegerValue(width * 4);
				for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				{
					var before = supplied.NumberQueries;
					if (!CheckGroupedSpan(value, PercentFormat(formatCase), expected, provider, sizeCase)) return 1;
					if (providerCase == 2 && (supplied.NumberQueries != before + 1 || supplied.CustomQueries != 0)) return 2;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibPercentProviderContractsEntry()
	{
		for (var pattern = 0; pattern < 12; pattern++)
		for (var valueCase = 0; valueCase < 3; valueCase++)
		{
			var style = pattern % 8;
			var supplied = new CoreLibPercentProvider { Info = PercentInfo(style, pattern) };
			var magnitude = valueCase == 0 ? "0" : PercentContractMagnitude(style, valueCase == 2);
			var expected = PercentExpected(magnitude, valueCase == 1, true, valueCase == 1 ? pattern : pattern % 4, 3);
			object value = valueCase == 0 ? 0 : valueCase == 1 ? (object)long.MinValue : ulong.MaxValue;
			for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				if (!CheckGroupedSpan(value, "P3", expected, supplied, sizeCase)) return 1;
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				supplied.NumberQueries = 0; supplied.CustomQueries = 0;
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 256).AppendFormat(supplied, "{0:P3}", value);
				var snapshot = builder.ToString(0, builder.Length);
				if (snapshot != expected || supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1) return 2;
				supplied.Info!.PercentGroupSizes = [1]; supplied.Info.PercentSymbol = "changed";
				builder.Clear(); M68kRuntime.Collect(); if (snapshot != expected) return 3;
				supplied.Info = PercentInfo(style, pattern);
			}
		}
		var state = new NumberFormatInfo { NegativeSign = "", PercentSymbol = "", PercentGroupSeparator = "", PercentDecimalSeparator = "::", PercentDecimalDigits = 0 };
		var output = new System.Text.StringBuilder(256).AppendFormat(state, "[{0,8:P}][{1,-8:p3}][{2:P0}]", -12, 0u, 1000);
		if (output.ToString(0, output.Length) != "[   1200 ][0::000  ][100000 ]") return 4;
		output.Clear(); state.NegativeSign = "minus"; state.PercentSymbol = "ratio"; state.PercentGroupSeparator = "_"; state.PercentDecimalDigits = 3; state.PercentNegativePattern = 6;
		output.AppendFormat(state, "{0:P}|{1:P\0ignored}|{2:p000000000003}", -1000, -1000, 1000u);
		if (output.ToString(0, output.Length) != "100_000::000ratiominus|100_000ratiominus|100_000::000 ratio") return 5;
		for (var mode = 1; mode <= 2; mode++)
		{
			var provider = new CoreLibPercentProvider { Mode = mode };
			output.Clear().AppendFormat(provider, "{0:P}/{1:P0}/{2:p3}", -1000, 0u, ulong.MaxValue);
			if (output.ToString(0, output.Length) != "-100,000.00 %/0 %/1,844,674,407,370,955,161,500.000 %" || provider.NumberQueries != 3 || provider.CustomQueries != 1) return 6;
		}
		var changing = new CoreLibPercentProvider { Mode = 4, Info = new NumberFormatInfo() };
		output = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:P}", -12345);
		if (output.ToString(0, output.Length) != "1b23b45b00::000second two" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 7;
		changing.NumberQueries = 0; changing.CustomQueries = 0;
		output = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:P}", 12345);
		if (output.ToString(0, output.Length) != "two 1b23b45b00::000" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 8;
		var throwing = new CoreLibPercentProvider { Mode = 3, Error = new InvalidOperationException("provider") };
		output = new System.Text.StringBuilder(256).Append("seed");
		try { output.AppendFormat(throwing, "pre{0:P0}post", 0u); return 9; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || output.ToString(0, output.Length) != "seedpre") return 10; }
		throwing.NumberQueries = 0;
		try { output.AppendFormat(throwing, "{0:P1000000000}", 12u); return 11; } catch (FormatException) { }
		if (throwing.NumberQueries != 0) return 12;
		var written = 37;
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(0, "P0", throwing, Span<char>.Empty, out written); return 13; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || written != 37) return 14; }
		int[] sizes = [3, 2, 0]; state.PercentGroupSizes = sizes; sizes[0] = 1;
		var copy = state.PercentGroupSizes; if (copy[0] != 3) return 15; copy[0] = 1; if (state.PercentGroupSizes[0] != 3) return 16;
		try { state.PercentGroupSizes = [0, 3]; return 17; } catch (ArgumentException error) { if (error.ParamName != "value") return 18; }
		try { state.PercentGroupSizes = [10]; return 19; } catch (ArgumentException) { }
		try { state.PercentGroupSizes = null!; return 20; } catch (ArgumentNullException error) { if (error.ParamName != "value") return 21; }
		try { state.PercentNegativePattern = 12; return 22; } catch (ArgumentOutOfRangeException) { }
		try { state.PercentPositivePattern = 4; return 23; } catch (ArgumentOutOfRangeException) { }
		try { state.PercentDecimalDigits = 100; return 24; } catch (ArgumentOutOfRangeException) { }
		try { state.PercentSymbol = null!; return 25; } catch (ArgumentNullException) { }
		try { state.PercentDecimalSeparator = ""; return 26; } catch (ArgumentException) { }
		return state.PercentNegativePattern == 6 && state.PercentPositivePattern == 0 && state.PercentDecimalDigits == 3 && state.PercentSymbol == "ratio" && state.PercentGroupSizes[0] == 3 ? 42 : 27;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibPercentIntegerAllocationContractsEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		for (var width = 0; width < 8; width++)
		{
			var supplied = new CoreLibPercentProvider { Info = PercentInfo(1, 10) };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? supplied.Info : supplied;
			var custom = providerCase != 0; var formatCase = width % 4;
			var value = StandardIntegerValue(width * 4); var format = PercentFormat(formatCase);
			var expected = PercentExpected(PercentMagnitude(width, custom), (width & 1) == 0, custom,
				custom ? (width & 1) == 0 ? 10 : 2 : 0, GroupedPrecision(formatCase, custom));
			var storage = new char[expected.Length + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
			SetStringBuilderAllocationFailure(1);
			var shortSuccess = TryStandardInteger(value, format, new Span<char>(storage, 1, expected.Length - 1), out var shortWritten, provider);
			SetStringBuilderAllocationFailure(0); if (shortSuccess || shortWritten != 0) return 1;
			for (var index = 0; index < storage.Length; index++) if (storage[index] != '#') return 2;
			SetStringBuilderAllocationFailure(1);
			var success = TryStandardInteger(value, format, new Span<char>(storage, 1, expected.Length), out var written, provider);
			SetStringBuilderAllocationFailure(0); if (!success || written != expected.Length || storage[0] != '#' || storage[storage.Length - 1] != '#') return 3;
			for (var index = 0; index < written; index++) if (storage[index + 1] != expected[index]) return 4;
			SetStringBuilderAllocationFailure(2); var text = FormatStandardInteger(value, format, provider); SetStringBuilderAllocationFailure(0);
			if (text != expected || providerCase == 2 && supplied.NumberQueries != 3) return 5;
		}
		var guard = new char[1]; SetStringBuilderAllocationFailure(1);
		var huge = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64(ulong.MaxValue, "P999999999", null, guard, out var count);
		SetStringBuilderAllocationFailure(0); if (huge || count != 0) return 6;
		for (var pattern = 0; pattern < 12; pattern++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var style = pattern % 8; var supplied = new CoreLibPercentProvider { Info = PercentInfo(style, pattern) };
			object value = long.MinValue;
			var expected = PercentExpected(PercentContractMagnitude(style, false), true, true, pattern, 0);
			var builder = new System.Text.StringBuilder(256).Append("seed");
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(supplied, "pre{0:P999}post", value); SetStringBuilderAllocationFailure(0); return 7; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre" || supplied.NumberQueries != failAt || supplied.CustomQueries != 1) return 8;
			builder.Clear().AppendFormat(supplied, "{0:P0}", value); if (builder.ToString(0, builder.Length) != expected) return 9;
		}
		var state = PercentInfo(1, 1); int[] input = [1, 0];
		SetStringBuilderAllocationFailure(1); try { state.PercentGroupSizes = input; SetStringBuilderAllocationFailure(0); return 10; } catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (state.PercentGroupSizes[0] != 3 || input[0] != 1) return 11;
		SetStringBuilderAllocationFailure(2); state.PercentGroupSizes = input; SetStringBuilderAllocationFailure(0); input[0] = 9;
		if (state.PercentGroupSizes[0] != 1) return 12;
		SetStringBuilderAllocationFailure(1); try { var failedCopy = state.PercentGroupSizes; SetStringBuilderAllocationFailure(0); return 13; } catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		SetStringBuilderAllocationFailure(2); var copy = state.PercentGroupSizes; SetStringBuilderAllocationFailure(0); copy[0] = 9;
		return state.PercentGroupSizes[0] == 1 ? 42 : 14;
	}
}
