/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/

using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static unsafe int CoreLibStringBuilderSpanReplacementMatrixEntry()
	{
		char* oldBuffer = stackalloc char[40];
		char* newBuffer = stackalloc char[40];
		const string pattern = "ababa\0\u03A9\uD83D\uDE00\uD800\uFFFF";
		for (var scenario = 0; scenario < 11; scenario++)
		for (var range = 0; range < 5; range++)
		for (var capacityCase = 0; capacityCase < 3; capacityCase++)
		for (var inputKind = 0; inputKind < 3; inputKind++)
		{
			var oldText = scenario == 3 ? "\uFFFFababa\0" : scenario == 4 ? "\0\u03A9\uD83D\uDE00" :
				scenario == 5 ? "missing" : scenario == 8 ? "aa" : scenario == 9 ? "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" : scenario >= 6 ? "a" : "aba";
			var newText = scenario == 0 ? "Q" : scenario == 1 ? "xyz" : scenario == 2 ? "\u03A9\0\uD83D\uDE00\uD800\uFFFFLONG" :
				scenario == 3 || scenario == 7 ? "" : scenario == 4 ? "\u03A9" : scenario == 9 ? "\0\uD800" : scenario == 6 ? "aa" : "a";
			var oldSpan = inputKind == 2 ? FillReplacementStackSpan(oldBuffer, oldText) : CreateReplacementSpan(oldText, inputKind);
			var newSpan = inputKind == 2 ? FillReplacementStackSpan(newBuffer, newText) : CreateReplacementSpan(newText, inputKind);
			var builder = new StringBuilder(capacityCase == 0 ? 1 : capacityCase == 1 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(scenario is 8 or 9 ? 'a' : pattern[index % 11]);
			var before = builder.ToString();
			var start = range <= 1 ? 0 : range == 2 ? 3 : range == 3 ? 31 : 65;
			var count = range <= 1 ? 65 : range == 2 ? 33 : range == 3 ? 34 : 0;
			System.GC.Collect(); var pressure = new char[80]; pressure[0] = 'z';
			var returned = range == 0 ? builder.Replace(oldSpan, newSpan) : builder.Replace(oldSpan, newSpan, start, count);
			if (returned != builder) return 1;
			System.GC.Collect(); var snapshot = builder.ToString();
			if (!StringBuilderReplacementMatches(builder, before, snapshot, oldText, newText, start, count)) return 2;
			for (var index = 0; index < oldSpan.Length; index++) if (oldSpan[index] != oldText[index]) return 3;
			for (var index = 0; index < newSpan.Length; index++) if (newSpan[index] != newText[index]) return 4;
			builder.Clear(); builder.Append("tail"); System.GC.Collect();
			if (builder.ToString() != "tail") return 5;
			for (var index = 0; index < before.Length; index++) if (before[index] != (scenario is 8 or 9 ? 'a' : pattern[index % 11])) return 6;
			System.GC.KeepAlive(pressure);
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<char> CreateReplacementSpan(string text, int inputKind)
	{
		var array = new char[text.Length + 2]; array[0] = '['; array[array.Length - 1] = ']';
		for (var index = 0; index < text.Length; index++) array[index + 1] = text[index];
		return inputKind == 0 ? new ReadOnlySpan<char>(array, 1, text.Length) : new string(new ReadOnlySpan<char>(array)).AsSpan(1, text.Length);
	}

	private static unsafe ReadOnlySpan<char> FillReplacementStackSpan(char* buffer, string text)
	{
		buffer[0] = '['; buffer[text.Length + 1] = ']';
		for (var index = 0; index < text.Length; index++) buffer[index + 1] = text[index];
		return new ReadOnlySpan<char>(buffer + 1, text.Length);
	}

	public static int CoreLibStringBuilderSpanReplacementAliasingEntry()
	{
		var oldAlias = new StringBuilder(4);
		for (var index = 0; index < 12; index++) oldAlias.Append(index % 2 == 0 ? 'a' : 'b');
		var oldView = FirstReplacementChunk(oldAlias).Span.Slice(0, 2);
		System.GC.Collect();
		oldAlias.Replace(oldView, "XY".AsSpan());
		// Matches are gathered and applied per chunk. Updating the old span's
		// own chunk changes the search text observed by subsequent chunks.
		if (oldAlias.ToString() != "XYXYabababab") return 1;
		var newAlias = new StringBuilder(32); newAlias.Append("Zabcabc");
		var newView = FirstReplacementChunk(newAlias).Span.Slice(0, 3);
		System.GC.Collect(); newAlias.Replace("abc".AsSpan(), newView, 0, 7);
		if (newAlias.ToString() != "ZZabZZa") return 2;
		var both = new StringBuilder(32); both.Append("abXYabXY");
		var chunk = FirstReplacementChunk(both).Span;
		System.GC.Collect(); both.Replace(chunk.Slice(0, 2), chunk.Slice(2, 2));
		if (both.ToString() != "XYXYXYXY") return 3;
		var shared = new char[3]; shared[0] = 'a'; shared[1] = 'b'; shared[2] = 'X';
		var overlap = new StringBuilder(4); overlap.Append("ababab");
		overlap.Replace(new ReadOnlySpan<char>(shared, 0, 2), new ReadOnlySpan<char>(shared, 1, 2), 0, 6);
		if (overlap.ToString() != "bXbXbX" || shared[0] != 'a' || shared[1] != 'b' || shared[2] != 'X') return 4;
		var retained = DetachedReplacementChunk();
		System.GC.Collect(); var pressure = new char[80]; pressure[0] = 'z';
		var grown = new StringBuilder(1); grown.Append("aaaaaa");
		grown.Replace("a".AsSpan(), retained); System.GC.Collect();
		if (grown.Length != 36) return 5;
		for (var index = 0; index < 36; index++) if (grown[index] != "\u03A9\0\uD83D\uDE00\uD800\uFFFF"[index % 6]) return 6;
		grown.Clear(); System.GC.Collect();
		for (var index = 0; index < retained.Length; index++) if (retained[index] != "\u03A9\0\uD83D\uDE00\uD800\uFFFF"[index]) return 7;
		System.GC.KeepAlive(pressure);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlyMemory<char> FirstReplacementChunk(StringBuilder builder)
	{
		var enumerator = builder.GetChunks();
		enumerator.MoveNext();
		return enumerator.Current;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<char> DetachedReplacementChunk()
	{
		var builder = new StringBuilder(16); builder.Append("[\u03A9\0\uD83D\uDE00\uD800\uFFFF]");
		return FirstReplacementChunk(builder).Span.Slice(1, 6);
	}

	public static unsafe int CoreLibStringBuilderSpanReplacementAllocationFailureEntry()
	{
		char* oldBuffer = stackalloc char[4]; char* newBuffer = stackalloc char[5];
		for (var inputKind = 0; inputKind < 3; inputKind++)
		for (var ranged = 0; ranged < 2; ranged++)
		{
			var oldSpan = inputKind == 2 ? FillReplacementStackSpan(oldBuffer, "a") : CreateReplacementSpan("a", inputKind);
			var newSpan = inputKind == 2 ? FillReplacementStackSpan(newBuffer, "bc") : CreateReplacementSpan("bc", inputKind);
			var fitting = new StringBuilder(128); for (var index = 0; index < 65; index++) fitting.Append('a');
			var empty = new StringBuilder(1);
			SetStringBuilderAllocationFailure(1);
			if (ranged == 0)
			{
				empty.Replace(oldSpan, newSpan); fitting.Replace("missing".AsSpan(), newSpan);
				fitting.Replace(oldSpan, oldSpan); fitting.Replace(oldSpan, default(ReadOnlySpan<char>));
			}
			else
			{
				fitting.Replace(oldSpan, newSpan, 65, 0); fitting.Replace("missing".AsSpan(), newSpan, 0, 65);
				fitting.Replace(oldSpan, oldSpan, 0, 65); fitting.Replace(oldSpan, default(ReadOnlySpan<char>), 0, 65);
			}
			SetStringBuilderAllocationFailure(0);
			if (fitting.Length != 0 || empty.Length != 0) return 1;
			for (var failAt = 1; failAt <= (ranged == 0 ? 5 : 4); failAt++)
			{
				var builder = new StringBuilder(1024); for (var index = 0; index < 513; index++) builder.Append('a');
				var before = builder.ToString();
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (ranged == 0) builder.Replace(oldSpan, newSpan); else builder.Replace(oldSpan, newSpan, 1, 511);
					SetStringBuilderAllocationFailure(0); return 2;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (builder.Length != 513 || builder.Capacity != 1024 || oldSpan[0] != 'a' || newSpan[0] != 'b' || newSpan[1] != 'c') return 3;
				for (var index = 0; index < 513; index++) if (builder[index] != 'a' || before[index] != 'a') return 4;
				if (ranged == 0) builder.Replace(oldSpan, newSpan); else builder.Replace(oldSpan, newSpan, 1, 511);
				if (!StringBuilderReplacementMatches(builder, before, builder.ToString(), "a", "bc", ranged == 0 ? 0 : 1, ranged == 0 ? 513 : 511)) return 5;
			}
		}
		return 42;
	}

	public static int CoreLibStringBuilderSpanReplacementValidationEntry()
	{
		for (var emptyKind = 0; emptyKind < 2; emptyKind++)
		for (var scenario = 0; scenario < 12; scenario++)
		{
			var builder = new StringBuilder(4, 32); builder.Append("seed");
			var empty = emptyKind == 0 ? default(ReadOnlySpan<char>) : CreateReplacementSpan("", 0);
			var oldSpan = scenario < 10 ? empty : "e".AsSpan();
			var start = scenario == 0 ? -1 : scenario == 1 ? int.MinValue : scenario == 2 ? int.MaxValue : scenario == 3 ? 5 : scenario == 8 ? 4 : scenario == 9 ? 3 : 0;
			var count = scenario <= 3 ? -1 : scenario == 4 ? -1 : scenario == 5 ? int.MaxValue : scenario == 6 ? 5 : scenario == 9 ? 2 : 0;
			var expectedParameter = scenario <= 3 ? "startIndex" : scenario is 4 or 5 or 6 or 9 ? "count" : scenario >= 10 ? "requiredLength" : "oldValue";
			try
			{
				if (scenario == 10) builder.Replace(oldSpan, "12345678901234567890".AsSpan());
				else if (scenario == 11) builder.Replace(oldSpan, "12345678901234567890".AsSpan(), 0, 4);
				else builder.Replace(oldSpan, empty, start, count);
				return 1;
			}
			catch (ArgumentOutOfRangeException exception) { if (expectedParameter == "oldValue" || exception.ParamName != expectedParameter) return 2; }
			catch (ArgumentException exception) { if (expectedParameter != "oldValue" || exception.ParamName != expectedParameter) return 3; }
			if (builder.ToString() != "seed" || builder.Capacity != 4 || builder.MaxCapacity != 32) return 4;
		}
		var deletion = new StringBuilder("seed", 4);
		if (deletion.Replace("e".AsSpan(), default(ReadOnlySpan<char>)) != deletion || deletion.ToString() != "sd") return 5;
		deletion.Replace("s".AsSpan(), default(ReadOnlySpan<char>), 0, 1);
		if (deletion.ToString() != "d" || deletion.Replace("x".AsSpan(), "long".AsSpan(), 1, 0) != deletion) return 6;
		try { deletion.Replace(default(ReadOnlySpan<char>), default(ReadOnlySpan<char>)); return 7; }
		catch (ArgumentException exception) { if (exception.ParamName != "oldValue") return 8; }
		return 42;
	}
}
