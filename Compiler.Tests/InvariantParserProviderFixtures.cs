/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Globalization;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class InvariantParserProviderFixtures
{
	public static int CachedEntry() =>
		Cached(true) == 21 && Cached(false) == 21 ? 42 : 1;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Cached(bool chooseFirst)
	{
		IFormatProvider provider;
		if (chooseFirst) provider = CultureInfo.InvariantCulture;
		else provider = CultureInfo.InvariantCulture;
		var alias = provider;
		var count = 0;
		for (var i = 0; i < 3; i++)
		{
			if (DateOnly.Parse("2024-02-29", alias).DayNumber != 738944) return 1;
			if (!DateOnly.TryParse("2000-02-29", provider, out var date) || date.DayNumber != 730178) return 2;
			if (TimeOnly.Parse("12:34:56", alias).Hour != 12) return 3;
			if (!TimeOnly.TryParse("23:59:59.9999999", provider, out var time) || time.Ticks != 863_999_999_999L) return 4;
			count += 7;
		}
		return count;
	}

	public static int OverwrittenEntry()
	{
		IFormatProvider provider = CultureInfo.InvariantCulture;
		provider = CultureInfo.CurrentCulture;
		return DateOnly.Parse("2024-02-29", provider).DayNumber;
	}

	public static int EscapingEntry()
	{
		var provider = CultureInfo.InvariantCulture;
		var date = DateOnly.Parse("2024-02-29", provider);
		Observe(provider);
		return date.DayNumber;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Observe(IFormatProvider provider) => _observed = provider;
	private static IFormatProvider? _observed;

	public static int MixedEntry() => Mixed(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Mixed(bool chooseInvariant)
	{
		var provider = chooseInvariant ? CultureInfo.InvariantCulture : CultureInfo.CurrentCulture;
		return TimeOnly.Parse("12:34", provider).Hour;
	}

	public static int NullEntry() => TimeOnly.Parse("12:34", null).Hour;

	public static int AliasedEntry()
	{
		IFormatProvider provider = CultureInfo.InvariantCulture;
		Replace(ref provider);
		return DateOnly.Parse("2024-02-29", provider).DayNumber;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Replace(ref IFormatProvider provider) => provider = CultureInfo.CurrentCulture;
}
