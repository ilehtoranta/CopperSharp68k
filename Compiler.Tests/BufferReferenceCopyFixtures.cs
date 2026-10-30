/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static int ValueTypeBranch<T>() => typeof(T).IsValueType ? 17 : 29;
	public static int CoreLibValueTypePredicateEntry()
	{
		if (ValueTypeBranch<int>() != 17 || ValueTypeBranch<decimal>() != 17 || ValueTypeBranch<int?>() != 17 || ValueTypeBranch<DayOfWeek>() != 17 || ValueTypeBranch<JoinWordValue>() != 17) return 1;
		return ValueTypeBranch<string>() == 29 && ValueTypeBranch<object>() == 29 && ValueTypeBranch<int[]>() == 29 && ValueTypeBranch<List<int>>() == 29 ? 42 : 2;
	}

	public static int CoreLibReferenceCopyOwnershipEntry()
	{
		var values = new object?[5];
		values[0] = new string("first".AsSpan()); values[1] = new string("R\u03A9\0\uD800".AsSpan());
		values[2] = null; values[3] = new string("third".AsSpan()); values[4] = new string("fourth".AsSpan());
		var original = new ReadOnlySpan<object?>(values).ToArray();
		new ReadOnlySpan<object?>(values, 0, 4).CopyTo(new Span<object?>(values, 1, 4));
		GC.Collect();
		if (!ReferenceEquals(values[0], original[0])) return 1;
		for (var i = 1; i < 5; i++) if (!ReferenceEquals(values[i], original[i - 1])) return 2;
		new ReadOnlySpan<object?>(values, 1, 4).CopyTo(new Span<object?>(values, 0, 4));
		for (var i = 0; i < 4; i++) if (!ReferenceEquals(values[i], original[i])) return 3;
		if (!ReferenceEquals(values[4], original[3])) return 4;
		new ReadOnlySpan<object?>(values, 0, 0).CopyTo(new Span<object?>(values, 2, 0));
		new ReadOnlySpan<object?>(values).CopyTo(new Span<object?>(values));
		var copy = new ReadOnlySpan<object?>(values, 1, 3).ToArray();
		for (var i = 0; i < 5; i++) { values[i] = null; original[i] = null; }
		CollectReferenceFormattingGarbage();
		if (copy.Length != 3 || (string?)copy[0] != "R\u03A9\0\uD800" || copy[1] != null || (string?)copy[2] != "third") return 5;
		if (new ReadOnlySpan<object?>(values, 0, 0).ToArray().Length != 0) return 6;
		object?[] covariant = new string[1];
		try { _ = new Span<object?>(covariant); return 7; } catch (ArrayTypeMismatchException) { }
		if (new Span<object?>(null, 0, 0).Length != 0) return 8;
		try { _ = System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<object?>(null!); return 9; } catch (NullReferenceException) { }
		try { _ = new Span<object?>(covariant, -1, 2); return 10; } catch (ArrayTypeMismatchException) { }
		try { _ = new Span<object?>(values, -1, 0); return 11; } catch (ArgumentOutOfRangeException) { }
		try { _ = new Span<object?>(values, 4, 2); return 12; } catch (ArgumentOutOfRangeException) { }
		try { _ = new Span<object?>(null, 0, 1); return 13; } catch (ArgumentOutOfRangeException) { }
		var strings = new string?[4];
		strings[0] = new string("one".AsSpan()); strings[1] = null;
		strings[2] = new string("two".AsSpan()); strings[3] = new string("three".AsSpan());
		var stringOriginal = new ReadOnlySpan<string?>(strings).ToArray();
		new ReadOnlySpan<string?>(strings, 0, 3).CopyTo(new Span<string?>(strings, 1, 3));
		CollectReferenceFormattingGarbage();
		for (var i = 1; i < 4; i++) if (!ReferenceEquals(strings[i], stringOriginal[i - 1])) return 14;
		new ReadOnlySpan<string?>(strings, 1, 3).CopyTo(new Span<string?>(strings, 0, 3));
		new ReadOnlySpan<string?>(strings).CopyTo(new Span<string?>(strings));
		var stringCopy = new ReadOnlySpan<string?>(strings, 0, 3).ToArray();
		for (var i = 0; i < 4; i++) { strings[i] = null; stringOriginal[i] = null; }
		CollectReferenceFormattingGarbage();
		if (stringCopy[0] != "one" || stringCopy[1] != null || stringCopy[2] != "two") return 15;
		if (new Span<string?>(null).Length != 0 || new Span<string?>(null, 0, 0).Length != 0) return 16;
		try { _ = new Span<string?>(strings, 0, -1); return 17; } catch (ArgumentOutOfRangeException) { }
		try { _ = System.Runtime.InteropServices.MemoryMarshal.GetArrayDataReference<string?>(null!); return 18; } catch (NullReferenceException) { }
		return 42;
	}
}
