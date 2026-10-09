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
	private static NumberFormatInfo CurrencyInfo(int style, int pattern) => new()
	{
		NegativeSign = new string("\u2212\0\u03A9".AsSpan()), CurrencySymbol = new string("$#\0\uD800".AsSpan()),
		CurrencyDecimalSeparator = new string("\u03A9\0\uD83D\uDE00".AsSpan()), CurrencyGroupSeparator = new string("\0\uD800".AsSpan()),
		CurrencyDecimalDigits = 4, CurrencyNegativePattern = pattern, CurrencyPositivePattern = pattern % 4,
		CurrencyGroupSizes = style switch { 0 => [3], 1 => [3, 2], 2 => [3, 2, 0], 3 => [1, 0], 4 => [], 5 => [0], 6 => [1], _ => [9] },
		NumberDecimalSeparator = "wrong", NumberGroupSeparator = "wrong", NumberGroupSizes = [1], NumberDecimalDigits = 7, NumberNegativePattern = 4
	};

	private static string CurrencyFormat(int scenario) => scenario == 0 ? "C" : scenario == 1 ? "c0" : scenario == 2 ? "C3" : "c65";
	private static string CurrencyComposite(int scenario) => scenario == 0 ? "{0:C}" : scenario == 1 ? "{0:c0}" : scenario == 2 ? "{0:C3}" : "{0:c65}";

	// Group locations come from the existing literal magnitude oracles. These
	// prefix/suffix decorations are checked against official host formatting.
	private static string CurrencyExpected(string magnitude, bool negative, bool custom, int pattern, int precision)
	{
		var prefix = negative ? pattern switch
		{
			0 => "($", 1 => "-$", 2 => "$-", 3 => "$", 4 => "(", 5 => "-", 6 or 7 or 10 or 13 => "",
			8 => "-", 9 => "-$ ", 11 => "$ ", 12 => "$ -", 14 => "($ ", 15 => "(", _ => "$- "
		} : pattern switch { 0 => "$", 1 or 3 => "", _ => "$ " };
		var suffix = negative ? pattern switch
		{
			0 or 14 => ")", 1 or 2 or 9 or 12 or 16 => "", 3 or 11 => "-", 4 => "$)", 5 => "$", 6 => "-$",
			7 => "$-", 8 => " $", 10 => " $-", 13 => "- $", _ => " $)"
		} : pattern switch { 0 or 2 => "", 1 => "$", _ => " $" };
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
					if (decoration[index] == '$') text.Append(custom ? "$#\0\uD800" : "\u00A4");
					else if (decoration[index] == '-') text.Append(custom ? "\u2212\0\u03A9" : "-");
					else text.Append(decoration[index]);
			}
		}
		return text.ToString(0, text.Length);
	}

	private sealed class CoreLibCurrencyProvider : IFormatProvider
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
				Info.CurrencySymbol = NumberQueries == 1 ? "one" : "two";
				Info.CurrencyDecimalSeparator = NumberQueries == 1 ? ":" : "::";
				Info.CurrencyGroupSeparator = NumberQueries == 1 ? "a" : "b";
				Info.CurrencyGroupSizes = NumberQueries == 1 ? [3] : [2];
				Info.CurrencyNegativePattern = NumberQueries == 1 ? 0 : 13;
				Info.CurrencyPositivePattern = NumberQueries == 1 ? 0 : 3;
				Info.CurrencyDecimalDigits = NumberQueries == 1 ? 1 : 3;
			}
			return Info;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibCurrencySmokeEntry()
	{
		Span<char> storage = stackalloc char[64];
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12345, -1, "C3", null, storage, out var count)) return 1;
		if (new string(storage.Slice(0, count)) != "(\u00A412,345.000)") return 2;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(-12345, -1, "C3", null) != "(\u00A412,345.000)") return 3;
		var info = new NumberFormatInfo { CurrencyGroupSizes = [2], CurrencyNegativePattern = 16, CurrencySymbol = "coin" };
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12345, -1, "C3", info, storage, out count)) return 4;
		if (new string(storage.Slice(0, count)) != "coin- 1,23,45.000") return 5;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(-12345, -1, "C3", info) != "coin- 1,23,45.000") return 6;
		if (!CopperSharp.Runtime.ShadowGroupedIntegerFormatting.TryFormatCurrency(0, 12345, false, info, 3, storage, out count)) return 7;
		return new string(storage.Slice(0, count)) == "coin1,23,45.000" ? 42 : 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCurrencyIntegersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var supplied = new CoreLibCurrencyProvider { Info = CurrencyInfo(1, 12) };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? supplied.Info : supplied;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				var custom = providerCase != 0;
				var expected = CurrencyExpected(GroupedMagnitude(width, custom), (width & 1) == 0, custom,
					custom && (width & 1) == 0 ? 12 : 0, GroupedPrecision(formatCase, custom));
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 256);
				supplied.NumberQueries = 0; supplied.CustomQueries = 0; M68kRuntime.Collect();
				if (builder.AppendFormat(provider, CurrencyComposite(formatCase), StandardIntegerValue(width * 4)) != builder) return 1;
				var snapshot = builder.ToString(0, builder.Length);
				if (snapshot != expected || providerCase == 2 && (supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1)) return 2;
				builder.Clear().Append("changed"); M68kRuntime.Collect(); if (snapshot != expected) return 3;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibCurrencyIntegerSpanHelpersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var supplied = new CoreLibCurrencyProvider { Info = CurrencyInfo(1, 12) };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? supplied.Info : supplied;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			{
				var custom = providerCase != 0;
				var expected = CurrencyExpected(GroupedMagnitude(width, custom), (width & 1) == 0, custom,
					custom && (width & 1) == 0 ? 12 : 0, GroupedPrecision(formatCase, custom));
				var value = StandardIntegerValue(width * 4);
				for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				{
					var before = supplied.NumberQueries;
					if (!CheckGroupedSpan(value, CurrencyFormat(formatCase), expected, provider, sizeCase)) return 1;
					if (providerCase == 2 && (supplied.NumberQueries != before + 1 || supplied.CustomQueries != 0)) return 2;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibCurrencyProviderContractsEntry()
	{
		for (var pattern = 0; pattern < 17; pattern++)
		for (var valueCase = 0; valueCase < 3; valueCase++)
		{
			var style = pattern % 8;
			var supplied = new CoreLibCurrencyProvider { Info = CurrencyInfo(style, pattern) };
			var magnitude = valueCase == 0 ? "0" : GroupingContractMagnitude(style, valueCase == 2);
			var expected = CurrencyExpected(magnitude, valueCase == 1, true, valueCase == 1 ? pattern : pattern % 4, 3);
			object value = valueCase == 0 ? 0 : valueCase == 1 ? (object)long.MinValue : ulong.MaxValue;
			for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				if (!CheckGroupedSpan(value, "C3", expected, supplied, sizeCase)) return 1;
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				supplied.NumberQueries = 0; supplied.CustomQueries = 0;
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 256).AppendFormat(supplied, "{0:C3}", value);
				var snapshot = builder.ToString(0, builder.Length);
				if (snapshot != expected || supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1) return 2;
				supplied.Info!.CurrencyGroupSizes = [1]; supplied.Info.CurrencySymbol = "changed";
				builder.Clear(); M68kRuntime.Collect(); if (snapshot != expected) return 3;
				supplied.Info = CurrencyInfo(style, pattern);
			}
		}
		var state = new NumberFormatInfo { NegativeSign = "", CurrencySymbol = "", CurrencyGroupSeparator = "", CurrencyDecimalSeparator = "::", CurrencyDecimalDigits = 0 };
		var output = new System.Text.StringBuilder(256).AppendFormat(state, "[{0,8:C}][{1,-8:c3}][{2:C0}]", -12, 0u, 1000);
		if (output.ToString(0, output.Length) != "[    (12)][0::000  ][1000]") return 4;
		output.Clear(); state.NegativeSign = "minus"; state.CurrencySymbol = "coin"; state.CurrencyGroupSeparator = "_"; state.CurrencyDecimalDigits = 3; state.CurrencyNegativePattern = 6;
		output.AppendFormat(state, "{0:C}|{1:C\0ignored}|{2:c000000000003}", -1000, -1000, 1000u);
		if (output.ToString(0, output.Length) != "1_000::000minuscoin|1_000minuscoin|coin1_000::000") return 5;
		for (var mode = 1; mode <= 2; mode++)
		{
			var provider = new CoreLibCurrencyProvider { Mode = mode };
			output.Clear().AppendFormat(provider, "{0:C}/{1:C0}/{2:c3}", -1000, 0u, ulong.MaxValue);
			if (output.ToString(0, output.Length) != "(\u00A41,000.00)/\u00A40/\u00A418,446,744,073,709,551,615.000" || provider.NumberQueries != 3 || provider.CustomQueries != 1) return 6;
		}
		var changing = new CoreLibCurrencyProvider { Mode = 4, Info = new NumberFormatInfo() };
		output = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:C}", -12345);
		if (output.ToString(0, output.Length) != "1b23b45::000second two" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 7;
		changing.NumberQueries = 0; changing.CustomQueries = 0;
		output = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:C}", 12345);
		if (output.ToString(0, output.Length) != "1b23b45::000 two" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 8;
		var throwing = new CoreLibCurrencyProvider { Mode = 3, Error = new InvalidOperationException("provider") };
		output = new System.Text.StringBuilder(256).Append("seed");
		try { output.AppendFormat(throwing, "pre{0:C0}post", 0u); return 9; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || output.ToString(0, output.Length) != "seedpre") return 10; }
		throwing.NumberQueries = 0;
		try { output.AppendFormat(throwing, "{0:C1000000000}", 12u); return 11; } catch (FormatException) { }
		if (throwing.NumberQueries != 0) return 12;
		var written = 37;
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(0, "C0", throwing, Span<char>.Empty, out written); return 13; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || written != 37) return 14; }
		int[] sizes = [3, 2, 0]; state.CurrencyGroupSizes = sizes; sizes[0] = 1;
		var copy = state.CurrencyGroupSizes; if (copy[0] != 3) return 15; copy[0] = 1; if (state.CurrencyGroupSizes[0] != 3) return 16;
		try { state.CurrencyGroupSizes = [0, 3]; return 17; } catch (ArgumentException error) { if (error.ParamName != "value") return 18; }
		try { state.CurrencyGroupSizes = [10]; return 19; } catch (ArgumentException) { }
		try { state.CurrencyGroupSizes = null!; return 20; } catch (ArgumentNullException error) { if (error.ParamName != "value") return 21; }
		try { state.CurrencyNegativePattern = 17; return 22; } catch (ArgumentOutOfRangeException) { }
		try { state.CurrencyPositivePattern = 4; return 23; } catch (ArgumentOutOfRangeException) { }
		try { state.CurrencyDecimalDigits = 100; return 24; } catch (ArgumentOutOfRangeException) { }
		try { state.CurrencySymbol = null!; return 25; } catch (ArgumentNullException) { }
		try { state.CurrencyDecimalSeparator = ""; return 26; } catch (ArgumentException) { }
		return state.CurrencyNegativePattern == 6 && state.CurrencyPositivePattern == 0 && state.CurrencyDecimalDigits == 3 && state.CurrencySymbol == "coin" && state.CurrencyGroupSizes[0] == 3 ? 42 : 27;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibCurrencyIntegerAllocationContractsEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		for (var width = 0; width < 8; width++)
		{
			var supplied = new CoreLibCurrencyProvider { Info = CurrencyInfo(1, 12) };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? supplied.Info : supplied;
			var custom = providerCase != 0; var formatCase = width % 4;
			var value = StandardIntegerValue(width * 4); var format = CurrencyFormat(formatCase);
			var expected = CurrencyExpected(GroupedMagnitude(width, custom), (width & 1) == 0, custom,
				custom && (width & 1) == 0 ? 12 : 0, GroupedPrecision(formatCase, custom));
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
		var huge = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64(ulong.MaxValue, "C999999999", null, guard, out var count);
		SetStringBuilderAllocationFailure(0); if (huge || count != 0) return 6;
		for (var pattern = 0; pattern < 17; pattern++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var style = pattern % 8; var supplied = new CoreLibCurrencyProvider { Info = CurrencyInfo(style, pattern) };
			object value = long.MinValue;
			var expected = CurrencyExpected(GroupingContractMagnitude(style, false), true, true, pattern, 0);
			var builder = new System.Text.StringBuilder(256).Append("seed");
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(supplied, "pre{0:C999}post", value); SetStringBuilderAllocationFailure(0); return 7; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre" || supplied.NumberQueries != failAt || supplied.CustomQueries != 1) return 8;
			builder.Clear().AppendFormat(supplied, "{0:C0}", value); if (builder.ToString(0, builder.Length) != expected) return 9;
		}
		var state = CurrencyInfo(1, 1); int[] input = [1, 0];
		SetStringBuilderAllocationFailure(1); try { state.CurrencyGroupSizes = input; SetStringBuilderAllocationFailure(0); return 10; } catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (state.CurrencyGroupSizes[0] != 3 || input[0] != 1) return 11;
		SetStringBuilderAllocationFailure(2); state.CurrencyGroupSizes = input; SetStringBuilderAllocationFailure(0); input[0] = 9;
		if (state.CurrencyGroupSizes[0] != 1) return 12;
		SetStringBuilderAllocationFailure(1); try { var failedCopy = state.CurrencyGroupSizes; SetStringBuilderAllocationFailure(0); return 13; } catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		SetStringBuilderAllocationFailure(2); var copy = state.CurrencyGroupSizes; SetStringBuilderAllocationFailure(0); copy[0] = 9;
		return state.CurrencyGroupSizes[0] == 1 ? 42 : 14;
	}
}
