/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibStringBuilderObjectEnumAllocationEntry()
	{
		for (var kind = 0; kind < 2; kind++)
		for (var route = 0; route < 10; route++)
		for (var capacity = 1; capacity <= 64; capacity += 63)
		{
			var value = CreateObjectEnum(kind == 0 ? 0 : 6, true);
			var name = kind == 0 ? "-127" : "First, Second";
			var warm = new StringBuilder(64).Append('['); AppendObjectEnumRoute(warm, value, route);
			var expected = route < 2 ? "[" + name : string.Concat(string.Concat("[|", name), "|tail");
			var succeeded = false;
			for (var failAt = 1; failAt <= 64; failAt++)
			{
				var builder = new StringBuilder(capacity).Append('['); var snapshot = builder.ToString();
				var threw = false; SetStringBuilderAllocationFailure(failAt);
				try { AppendObjectEnumRoute(builder, value, route); }
				catch (OutOfMemoryException) { threw = true; }
				finally { SetStringBuilderAllocationFailure(0); }
				var actual = builder.ToString();
				if (snapshot != "[" || (threw ? !expected.StartsWith(actual, StringComparison.Ordinal) || actual.Length < 1 : actual != expected)) return 100 + route;
				builder.Clear().Append('['); AppendObjectEnumRoute(builder, value, route);
				if (builder.ToString() != expected || snapshot != "[") return 200 + route;
				if (!threw) { succeeded = true; break; }
			}
			if (!succeeded) return 300 + route;
		}
		return 42;
	}
	public static int CoreLibStringBuilderObjectEnumCapacityEntry()
	{
		for (var kind = 0; kind < 2; kind++)
		{
			var value = CreateObjectEnum(kind == 0 ? 0 : 6, true);
			for (var insert = 0; insert < 2; insert++)
			{
				var builder = new StringBuilder(4, 4).Append("seed");
				try { if (insert == 0) builder.Append(value); else builder.Insert(2, value); return 1; }
				catch (ArgumentOutOfRangeException error) { if (insert != 0 || error.ParamName != "valueCount") return 2; }
				catch (OutOfMemoryException) { if (insert == 0) return 3; }
				if (builder.ToString() != "seed" || builder.Capacity != 4) return 4;
				builder.Clear().Append("ok"); if (builder.ToString() != "ok") return 5;
			}
			for (var character = 0; character < 2; character++)
			for (var span = 0; span < 2; span++)
			{
				var parts = new object?[3]; parts[0] = "A"; parts[1] = value; parts[2] = "tail";
				var builder = new StringBuilder(6, 6).Append("seed");
				try
				{
					if (span == 0) { if (character == 0) builder.AppendJoin("|", parts); else builder.AppendJoin('|', parts); }
					else { if (character == 0) builder.AppendJoin("|", new ReadOnlySpan<object?>(parts)); else builder.AppendJoin('|', new ReadOnlySpan<object?>(parts)); }
					return 6;
				}
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 7; }
				if (builder.ToString() != "seedA|" || builder.Capacity != 6) return 8;
				builder.Clear().Append("ok"); if (builder.ToString() != "ok") return 9;
			}
		}
		return 42;
	}
}
