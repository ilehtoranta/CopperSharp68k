/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int EnumInterfaceBranchEntry() => EnumInterfaceBranch(DayOfWeek.Friday);
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static int EnumInterfaceBranch<T>(T value)
	{
		if (value is IFormattable) return 17;
		return 29;
	}
	[Flags] private enum HandlerAccess : uint { None, Read = 1, Write = 2, Execute = 4 }
	private enum HandlerLongCode : long { Minimum = long.MinValue, Maximum = long.MaxValue }
	public static int CoreLibStringBuilderInterpolatedEnumsEntry()
	{
		var builder = new StringBuilder(1);
		var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 3, builder);
		handler.AppendLiteral("[");
		handler.AppendFormatted(DayOfWeek.Friday);
		handler.AppendLiteral("|");
		handler.AppendFormatted(HandlerAccess.Read | HandlerAccess.Write, -5, "F");
		handler.AppendLiteral("|");
		handler.AppendFormatted((HandlerLongCode)(-1L), 18, "x");
		handler.AppendLiteral("]");
		System.GC.Collect();
		if (builder.ToString() != "[Friday|Read, Write|  FFFFFFFFFFFFFFFF]") return 1;
		try { handler.AppendFormatted(DayOfWeek.Friday, "ZZ"); return 2; } catch (FormatException) { }
		handler.AppendLiteral("!");
		return builder.ToString() == "[Friday|Read, Write|  FFFFFFFFFFFFFFFF]!" ? 42 : 3;
	}
}
