/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibGenericFrameworkEnumJoinsEntry()
	{
		var result = CheckGenericJoin(DayOfWeek.Monday, (DayOfWeek)7, "[Monday|7]");
		return result == 42 ? CheckStructGenericJoin(DayOfWeek.Monday, (DayOfWeek)7, "Monday", "7") : result;
	}
}
