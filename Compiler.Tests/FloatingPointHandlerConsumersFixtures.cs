/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static string FloatingHandlerConsumerExpected(int route, int direction)
	{
		var text = direction == 0 ? "seedB     -123.50" : "seedB-123.50     ";
		return (route & 3) >= 2 ? text + Environment.NewLine : text;
	}

	private static StringBuilder ApplyFloatingHandlerConsumer(StringBuilder builder, bool wide, object box, int route, int direction)
	{
		var consumer = route & 3;
		var info = NumberFormatInfo.InvariantInfo;
		var handler = (consumer & 1) == 0 ? new StringBuilder.AppendInterpolatedStringHandler(1, 1, builder) :
			new StringBuilder.AppendInterpolatedStringHandler(1, 1, builder, info);
		handler.AppendLiteral("B");
		var alignment = direction == 0 ? 12 : -12;
		if (route >= 4) handler.AppendFormatted(box, alignment, "F2");
		else if (wide) handler.AppendFormatted(-123.5d, alignment, "F2");
		else handler.AppendFormatted(-123.5f, alignment, "F2");
		return consumer == 0 ? builder.Append(ref handler) : consumer == 1 ? builder.Append(info, ref handler) :
			consumer == 2 ? builder.AppendLine(ref handler) : builder.AppendLine(info, ref handler);
	}

	public static int CoreLibFloatingHandlerConsumersOracleEntry()
	{
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			{
				object box = precision == 0 ? (object)(-123.5f) : -123.5d;
				for (var route = 0; route < 8; route++)
				for (var direction = 0; direction < 2; direction++)
				for (var capacity = 0; capacity < 2; capacity++)
				{
					var builder = new StringBuilder(capacity == 0 ? 5 : 64).Append("seed");
					var snapshot = builder.ToString();
					if (!ReferenceEquals(builder, ApplyFloatingHandlerConsumer(builder, precision != 0, box, route, direction))) return 100 + route;
					System.GC.Collect();
					if (builder.ToString() != FloatingHandlerConsumerExpected(route, direction) || snapshot != "seed") return 200 + route;
					builder.Clear().Append("tail");
					System.GC.Collect();
					if (builder.ToString() != "tail" || snapshot != "seed") return 300 + route;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}

	public static int CoreLibFloatingHandlerConsumerAllocationContractsEntry()
	{
		var selectedCase = GetFloatingAllocationCase();
		if (selectedCase < 0 || selectedCase >= 64) return 800;
		var previous = CultureInfo.CurrentCulture;
		CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
		try
		{
			for (var precision = 0; precision < 2; precision++)
			{
				object box = precision == 0 ? (object)(-123.5f) : -123.5d;
				for (var route = 0; route < 8; route++)
				for (var direction = 0; direction < 2; direction++)
				for (var capacity = 0; capacity < 2; capacity++)
				{
					if (((precision * 8 + route) * 2 + direction) * 2 + capacity != selectedCase) continue;
					var expected = FloatingHandlerConsumerExpected(route, direction);
					var warm = new StringBuilder(capacity == 0 ? 5 : 64).Append("seed");
					if (!ReferenceEquals(warm, ApplyFloatingHandlerConsumer(warm, precision != 0, box, route, direction)) || warm.ToString() != expected) return 100 + route;
					var completed = false;
					for (var failAt = 1; failAt <= 16; failAt++)
					{
						var builder = new StringBuilder(capacity == 0 ? 5 : 64).Append("seed");
						var snapshot = builder.ToString();
						var threw = false;
						SetStringBuilderAllocationFailure(failAt);
						try { if (!ReferenceEquals(builder, ApplyFloatingHandlerConsumer(builder, precision != 0, box, route, direction))) return 200 + route; }
						catch (OutOfMemoryException) { threw = true; }
						finally { SetStringBuilderAllocationFailure(0); }
						var failed = builder.ToString();
						if (snapshot != "seed" || failed.Length < 4 || failed.Length > expected.Length || !threw && failed != expected) return 300 + route;
						for (var index = 0; index < failed.Length; index++) if (failed[index] != expected[index]) return 400 + route;
						builder.Length = 4;
						if (!ReferenceEquals(builder, ApplyFloatingHandlerConsumer(builder, precision != 0, box, route, direction)) || builder.ToString() != expected) return 500 + route;
						builder.Clear().Append("tail");
						if (builder.ToString() != "tail" || snapshot != "seed") return 600 + route;
						if (!threw) { completed = true; break; }
					}
					if (!completed) return 700 + route;
				}
			}
			return 42;
		}
		finally { CultureInfo.CurrentCulture = previous; }
	}
}
