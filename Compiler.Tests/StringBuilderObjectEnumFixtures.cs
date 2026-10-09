/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static object CreateObjectEnum(int kind, bool unnamed) => kind switch
	{
		0 => unnamed ? (object)(JoinSByte)(-127) : JoinSByte.Named,
		1 => unnamed ? (object)(JoinByte)0 : JoinByte.Named,
		2 => unnamed ? (object)(JoinInt16)(-32767) : JoinInt16.Named,
		3 => unnamed ? (object)(JoinUInt16)0 : JoinUInt16.Named,
		4 => unnamed ? (object)(JoinInt32)int.MaxValue : JoinInt32.Named,
		5 => unnamed ? (object)(JoinUInt32)0 : JoinUInt32.Named,
		6 => unnamed ? (object)(JoinInt64.First | JoinInt64.Second) : JoinInt64.Named,
		7 => unnamed ? (object)(JoinUInt64.First | JoinUInt64.Second) : JoinUInt64.Named,
		_ => unnamed ? (object)(DayOfWeek)7 : DayOfWeek.Monday
	};
	private static string ObjectEnumName(int kind, bool unnamed) => unnamed ? kind switch
	{
		0 => "-127", 1 or 3 or 5 => "0", 2 => "-32767", 4 => "2147483647", 6 or 7 => "First, Second", _ => "7"
	} : kind == 8 ? "Monday" : "Named";
	private static void AppendObjectEnumRoute(StringBuilder builder, object value, int route)
	{
		if (route == 0) { builder.Append(value); return; }
		if (route == 1) { builder.Insert(1, value); return; }
		var parts = new object?[3]; parts[0] = null; parts[1] = value; parts[2] = "tail";
		if (route == 2) builder.AppendJoin("|", parts);
		else if (route == 3) builder.AppendJoin('|', parts);
		else if (route == 4) builder.AppendJoin("|", new ReadOnlySpan<object?>(parts));
		else if (route == 5) builder.AppendJoin('|', new ReadOnlySpan<object?>(parts));
		else if (route == 6) builder.AppendJoin("|", (IEnumerable<object?>)parts);
		else if (route == 7) builder.AppendJoin('|', (IEnumerable<object?>)parts);
		else { var list = new List<object?>(3); list.Add(null); list.Add(value); list.Add("tail"); if (route == 8) builder.AppendJoin("|", list); else builder.AppendJoin('|', list); }
	}
	public static int CoreLibStringBuilderObjectEnumsEntry()
	{
		for (var kind = 0; kind < 9; kind++)
		for (var unnamed = 0; unnamed < 2; unnamed++)
		for (var capacity = 1; capacity <= 64; capacity += 63)
		for (var route = 0; route < 10; route++)
		{
			var value = CreateObjectEnum(kind, unnamed != 0); GC.Collect();
			var name = ObjectEnumName(kind, unnamed != 0);
			var builder = new StringBuilder(capacity).Append('[');
			AppendObjectEnumRoute(builder, value, route); builder.Append(']');
			var expected = route < 2 ? string.Concat(string.Concat("[", name), "]") : string.Concat(string.Concat("[|", name), "|tail]");
			var snapshot = builder.ToString(); GC.Collect();
			if (snapshot != expected) return 100 + kind * 10 + route;
			builder.Clear().Append('['); AppendObjectEnumRoute(builder, value, route); builder.Append(']');
			if (builder.ToString() != expected || snapshot != expected) return 200 + kind * 10 + route;
		}
		return 42;
	}
	[System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<object?> CreateOwnedObjectEnums()
	{
		var values = new object?[4]; values[0] = (JoinSByte)(-127); values[1] = JoinInt64.Named;
		values[2] = JoinUInt64.First | JoinUInt64.Second; values[3] = DayOfWeek.Monday;
		return new ReadOnlySpan<object?>(values, 0, 4);
	}
	public static int CoreLibStringBuilderObjectEnumOwnershipEntry()
	{
		var view = CreateOwnedObjectEnums(); GC.Collect();
		var builder = new StringBuilder(1); builder.AppendJoin('|', view); GC.Collect();
		var snapshot = builder.ToString();
		if (snapshot != "-127|Named|First, Second|Monday") return 1;
		builder.Clear().AppendJoin("::", view); GC.Collect();
		return builder.ToString() == "-127::Named::First, Second::Monday" && snapshot == "-127|Named|First, Second|Monday" ? 42 : 2;
	}
}
