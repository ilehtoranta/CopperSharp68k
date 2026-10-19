/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private class DefaultClassName { public string? Text; }
	private sealed class DefaultDerivedClassName : DefaultClassName { }
	private sealed class DefaultGenericClassName<T> { public T? Value; }
	private class DefaultClassNameOwner<T> { public sealed class Nested<U> { } }
	private sealed class HiddenDefaultClassName : DefaultClassName
	{
		public new string ToString() => throw new InvalidOperationException("Hidden method must not run.");
	}
	private class DefaultClassOverride
	{
		public override string ToString() { GC.Collect(); return "override"; }
	}
	private sealed class HiddenInheritedClassOverride : DefaultClassOverride
	{
		public new string ToString() => throw new InvalidOperationException("Hidden method must not run.");
	}

	public static int CoreLibStringBuilderDefaultClassNamesEntry()
	{
		object[] values = new object[8];
		values[0] = new DefaultClassName { Text = new StringBuilder().Append("owned").Append('\u03a9').ToString() };
		values[1] = new DefaultDerivedClassName();
		values[2] = new DefaultGenericClassName<int>();
		values[3] = new DefaultGenericClassName<string> { Value = "retained" };
		values[4] = new DefaultClassNameOwner<int>.Nested<string>();
		values[5] = new HiddenDefaultClassName();
		values[6] = new UnsupportedObjectJoinValue();
		values[7] = new HiddenInheritedClassOverride();
		for (var kind = 0; kind < values.Length; kind++)
		{
			var name = kind == 0 ? "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultClassName" :
				kind == 1 ? "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultDerivedClassName" :
				kind == 2 ? "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericClassName`1[System.Int32]" :
				kind == 3 ? "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultGenericClassName`1[System.String]" :
				kind == 4 ? "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultClassNameOwner`1+Nested`1[System.Int32,System.String]" :
				kind == 5 ? "CopperSharp.Compiler.Tests.CompilerFixtures+HiddenDefaultClassName" :
				kind == 6 ? "CopperSharp.Compiler.Tests.CompilerFixtures+UnsupportedObjectJoinValue" : "override";
			for (var capacity = 1; capacity <= 192; capacity += 191)
			for (var route = 0; route < 10; route++)
			{
				GC.Collect();
				var value = values[kind];
				var builder = new StringBuilder(capacity).Append('[');
				object?[] parts = new object?[3]; parts[0] = null; parts[1] = value; parts[2] = "tail";
				if (route == 0) builder.Append(value);
				else if (route == 1) builder.Insert(1, value);
				else if (route == 2) builder.AppendJoin("|", parts);
				else if (route == 3) builder.AppendJoin('|', parts);
				else if (route == 4) builder.AppendJoin("|", new ReadOnlySpan<object?>(parts));
				else if (route == 5) builder.AppendJoin('|', new ReadOnlySpan<object?>(parts));
				else if (route == 6) builder.AppendJoin("|", (IEnumerable<object?>)parts);
				else if (route == 7) builder.AppendJoin('|', (IEnumerable<object?>)parts);
				else if (route == 8) builder.AppendFormat("{0}", value);
				else { var handler = new StringBuilder.AppendInterpolatedStringHandler(0, 1, builder); handler.AppendFormatted(value); builder.Append(ref handler); }
				builder.Append(']');
				var expected = route is >= 2 and <= 7 ? "[|" + name + "|tail]" : "[" + name + "]";
				var snapshot = builder.ToString();
				if (snapshot != expected) return 100 + kind * 10 + route;
				builder.Clear().Append("retry"); GC.Collect();
				if (snapshot != expected || builder.ToString() != "retry") return 200 + route;
			}
			try { new StringBuilder().Insert(-1, values[kind]); return 300 + kind; }
			catch (ArgumentOutOfRangeException) { }
			for (var insert = 0; insert < 2; insert++)
			{
				var limited = new StringBuilder(4, 4).Append("seed");
				try { if (insert == 0) limited.Append(values[kind]); else limited.Insert(2, values[kind]); return 310 + kind; }
				catch (ArgumentOutOfRangeException) { if (insert != 0) return 340 + kind; }
				catch (OutOfMemoryException) { if (insert == 0) return 350 + kind; }
				if (limited.ToString() != "seed" || limited.Capacity != 4) return 320 + kind;
				limited.Clear().Append("ok");
				if (limited.ToString() != "ok") return 330 + kind;
			}
		}
		GC.Collect();
		if (((DefaultClassName)values[0]).Text != "owned\u03a9" || ((DefaultGenericClassName<string>)values[3]).Value != "retained") return 400;
		return 42;
	}
}
