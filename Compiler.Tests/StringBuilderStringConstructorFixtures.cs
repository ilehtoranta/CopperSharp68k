/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */

using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibStringBuilderStringConstructorsEntry()
	{
		for (var variant = 0; variant < 3; variant++)
		for (var length = 0; length <= 33; length++)
		for (var start = 0; start <= 3; start++)
		for (var capacityCase = 0; capacityCase < (variant == 0 ? 1 : 3); capacityCase++)
		{
			var capacity = capacityCase == 0 ? 0 : capacityCase == 1 ? 1 : 70;
			var count = variant == 2 ? length : length + start;
			var sourceStart = variant == 2 ? start : 0;
			var builder = CreateStringSeededBuilder(variant, length + start, start, length, capacity);
			System.GC.Collect();
			var pressure = new char[160]; pressure[0] = 'z';
			var expectedCapacity = capacity == 0 || variant == 0 ? 16 : capacity;
			if (expectedCapacity < count) expectedCapacity = count;
			if (builder.Length != count || builder.Capacity != expectedCapacity || builder.MaxCapacity != int.MaxValue) return 1;
			var snapshot = builder.ToString();
			for (var index = 0; index < count; index++)
				if (builder[index] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[(sourceStart + index) % 7]) return 2;
			builder.Append('!');
			if (builder.Length != count + 1 || builder[count] != '!') return 3;
			builder.Clear(); builder.Append("tail");
			System.GC.Collect();
			if (builder.ToString() != "tail" || snapshot.Length != count) return 4;
			for (var index = 0; index < count; index++)
				if (snapshot[index] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[(sourceStart + index) % 7]) return 5;
			System.GC.KeepAlive(pressure);
		}
		for (var variant = 0; variant < 3; variant++)
		for (var empty = 0; empty < 2; empty++)
		{
			var builder = ConstructStringBuilder(empty == 0 ? null : "", variant, 0, 0, 0);
			if (builder.Length != 0 || builder.Capacity != 16 || builder.MaxCapacity != int.MaxValue) return 6;
			builder.Append('x'); System.GC.Collect();
			if (builder.ToString() != "x") return 7;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static StringBuilder CreateStringSeededBuilder(int variant, int sourceLength, int start, int length, int capacity)
	{
		var characters = new char[sourceLength];
		for (var index = 0; index < sourceLength; index++) characters[index] = "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 7];
		var source = new string(new ReadOnlySpan<char>(characters));
		var builder = ConstructStringBuilder(source, variant, start, length, capacity);
		if (builder.Length != 0)
		{
			var saved = builder[0]; builder[0] = 'z';
			if (source[variant == 2 ? start : 0] != saved) throw new InvalidOperationException("Constructor aliased string storage.");
			builder[0] = saved;
		}
		return builder;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static StringBuilder ConstructStringBuilder(string? source, int variant, int start, int length, int capacity) =>
		variant == 0 ? new StringBuilder(source) : variant == 1 ? new StringBuilder(source, capacity) : new StringBuilder(source, start, length, capacity);

	public static int CoreLibStringBuilderStringConstructorValidationEntry()
	{
		for (var scenario = 0; scenario < 14; scenario++)
		{
			string? source = "abc"; var variant = 2; var start = 0; var length = 0; var capacity = 0; var parameter = "length";
			switch (scenario)
			{
				case 0: variant = 1; capacity = -1; parameter = "capacity"; break;
				case 1: variant = 1; source = null; capacity = -1; parameter = "capacity"; break;
				case 2: start = -1; length = -1; capacity = -1; parameter = "capacity"; break;
				case 3: start = int.MaxValue; length = -1; capacity = -1; parameter = "capacity"; break;
				case 4: start = -1; length = -1; break;
				case 5: start = -1; parameter = "startIndex"; break;
				case 6: source = null; start = 1; break;
				case 7: source = null; length = 1; break;
				case 8: start = 2; length = 2; break;
				case 9: start = int.MaxValue; length = 1; break;
				case 10: start = 1; length = int.MaxValue; break;
				case 11: start = int.MinValue; parameter = "startIndex"; break;
				case 12: length = int.MinValue; break;
				case 13: capacity = int.MinValue; parameter = "capacity"; break;
			}
			try { _ = ConstructStringBuilder(source, variant, start, length, capacity); return 1; }
			catch (ArgumentOutOfRangeException exception) { if (exception.ParamName != parameter) return 2; }
		}
		return 42;
	}

	public static int CoreLibStringBuilderStringConstructorAllocationFailureEntry()
	{
		for (var variant = 0; variant < 3; variant++)
		for (var sourceCase = 0; sourceCase < 3; sourceCase++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var source = sourceCase == 0 ? null : sourceCase == 1 ? "" : "A\0\u03A9\uD800";
			var start = variant == 2 && sourceCase == 2 ? 1 : 0;
			var count = sourceCase == 2 ? 4 - start : 0;
			SetStringBuilderAllocationFailure(failAt);
			try { _ = ConstructStringBuilder(source, variant, start, count, 1); SetStringBuilderAllocationFailure(0); return 1; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (sourceCase == 2 && source != "A\0\u03A9\uD800") return 2;
			var builder = ConstructStringBuilder(source, variant, start, count, 1);
			if (builder.Length != count) return 3;
			if (sourceCase == 2 && builder.ToString() != (variant == 2 ? "\0\u03A9\uD800" : source)) return 4;
			builder.Append('!');
			if (builder.Length != count + 1 || builder[count] != '!') return 5;
		}
		return 42;
	}
}
