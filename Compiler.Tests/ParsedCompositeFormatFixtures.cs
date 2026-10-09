/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Globalization;
using System.Text;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CompositeFormatSegmentArrayGcEntry()
	{
		var segments = CreateCompositeSegments();
		System.GC.Collect();
		for (var pass = 0; pass < 12; pass++)
		{
			var churn = new char[73]; churn[0] = (char)pass;
			System.GC.Collect();
			for (var index = 0; index < segments.Length; index++)
			{
				var segment = RetainCompositeSegment(segments[index]);
				if (segment.Item1.Length != 19 || segment.Item1[18] != (char)('A' + index) ||
					segment.Item4.Length != 23 || segment.Item4[22] != (char)('a' + index) ||
					segment.Item2 != index || segment.Item3 != -index - 1) return 1;
			}
			if (churn[0] != pass) return 2;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (string, int, int, string)[] CreateCompositeSegments()
	{
		var result = new (string, int, int, string)[7];
		for (var index = 0; index < result.Length; index++)
			result[index] = (CreateCompositeText((char)('A' + index), 19), index, -index - 1, CreateCompositeText((char)('a' + index), 23));
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CreateCompositeText(char character, int length)
	{
		return new StringBuilder(length).Append(character, length).ToString();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (string, int, int, string) RetainCompositeSegment((string, int, int, string) segment)
	{
		System.GC.Collect();
		return segment;
	}

	public static int CompositeTypeEqualityEntry() =>
		CompositeTypeEqual<char>() + CompositeTypeEqual<int>() + CompositeTypeNotEqual<char>() + CompositeTypeNotEqual<int>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CompositeTypeEqual<T>() { if (typeof(T) == typeof(char)) return 17; return 29; }
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CompositeTypeNotEqual<T>() { if (typeof(T) != typeof(char)) return 31; return 43; }

	public static int CoreLibStringBuilderParsedCompositeFormatEntry()
	{
		var provider = new NumberFormatInfo();
		var single = CompositeFormat.Parse("<{0:X2}>");
		var pair = CompositeFormat.Parse("{0}|{1,-3}");
		var triple = CompositeFormat.Parse("{2}:{0}:{1:X2}");
		var many = CompositeFormat.Parse("[{0:X2}|{1,-4}|{2}|{3}]");
		if (single.Format != "<{0:X2}>" || single.MinimumArgumentCount != 1 || many.MinimumArgumentCount != 4) return 1;
		System.GC.Collect();
		var builder = new StringBuilder(1);
		if (builder.AppendFormat<int>(provider, single, 31) != builder || builder.ToString() != "<1F>") return 2;
		builder.Clear(); builder.AppendFormat<int, string>(provider, pair, 31, "A");
		if (builder.ToString() != "31|A  ") return 3;
		builder.Clear(); builder.AppendFormat<string, int, string>(provider, triple, "A", 31, "B");
		if (builder.ToString() != "B:A:1F") return 4;
		var args = new object?[4]; args[0] = 31; args[1] = "x"; args[2] = null; args[3] = 6;
		builder.Clear(); builder.AppendFormat(provider, many, args);
		if (builder.ToString() != "[1F|x   ||6]") return 5;
		builder.Clear(); builder.AppendFormat(provider, many, new ReadOnlySpan<object?>(args));
		System.GC.Collect();
		if (builder.ToString() != "[1F|x   ||6]") return 6;
		var longFormat = CreateLongCompositeFormat();
		System.GC.Collect();
		if (longFormat.MinimumArgumentCount != 4) return 7;
		builder.Clear(); builder.AppendFormat(provider, longFormat, args);
		if (builder.Length != 529 || builder.ToString(513, 16) != "{}|6|1F|x   ||1F") return 8;
		for (var index = 0; index < 513; index++) if (builder[index] != 'q') return 9;
		args[0] = 10; args[3] = 7;
		System.GC.Collect();
		builder.Clear(); builder.AppendFormat(provider, longFormat, new ReadOnlySpan<object?>(args));
		return builder.Length == 529 && builder.ToString(513, 16) == "{}|7|0A|x   ||0A" ? 42 : 10;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static CompositeFormat CreateLongCompositeFormat()
	{
		var source = new StringBuilder(1).Append('q', 513).Append("{{}}|{3}|{0:X2}|{1,-4}|{2}|{0:X2}");
		return CompositeFormat.Parse(source.ToString());
	}
}
