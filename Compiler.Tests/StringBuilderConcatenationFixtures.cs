/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int StringBuilderConcatenationOwnershipEntry()
	{
		var builder = new StringBuilder(1).Append('a').Append('\uD800').Append('\uDC00');
		var source = builder.ToString();
		var three = string.Concat(source, "|", "tail");
		var four = string.Concat(source, ":", three, "tail");
		builder.Clear().Append("reuse");
		System.GC.Collect();
		if (source != "a\uD800\uDC00" || three != "a\uD800\uDC00|tail" || four != "a\uD800\uDC00:a\uD800\uDC00|tailtail") return 100;
		if (!ReferenceEquals(source, string.Concat(null, null, source)) || !ReferenceEquals(source, string.Concat(null, source, null, null))) return 200;
		if (string.Concat(null, null, null) != "" || string.Concat(null, null, null, null) != "") return 300;
		return builder.ToString() == "reuse" ? 42 : 400;
	}

	public static int StringBuilderConcatenationAllocationEntry()
	{
		var source = new StringBuilder().Append("seed").ToString();
		for (var arity = 3; arity <= 4; arity++)
		{
			SetStringBuilderAllocationFailure(1);
			try { if (!ReferenceEquals(source, arity == 3 ? string.Concat(null, source, null) : string.Concat(null, source, null, null))) return 100; }
			finally { SetStringBuilderAllocationFailure(0); }
			var threw = false;
			SetStringBuilderAllocationFailure(1);
			try { _ = arity == 3 ? string.Concat(source, "|", "tail") : string.Concat(source, "|", "tail", "!"); }
			catch (OutOfMemoryException) { threw = true; }
			finally { SetStringBuilderAllocationFailure(0); }
			if (!threw || source != "seed") return 200;
			SetStringBuilderAllocationFailure(2);
			try { if ((arity == 3 ? string.Concat(source, "|", "tail") : string.Concat(source, "|", "tail", "!")) != (arity == 3 ? "seed|tail" : "seed|tail!")) return 300; }
			finally { SetStringBuilderAllocationFailure(0); }
		}
		return source == "seed" ? 42 : 400;
	}
}
