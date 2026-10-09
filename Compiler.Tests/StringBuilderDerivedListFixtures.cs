/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Collections;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private sealed class InheritedStringJoinList : List<string?>
	{
		public InheritedStringJoinList(bool empty = false) : base(2)
		{
			if (!empty) { Add("inherited"); Add("values"); }
		}
	}

	public static int StringBuilderInheritedListDispatchEntry()
	{
		for (var separator = 0; separator < 2; separator++)
		{
			IEnumerable<string?> source = new InheritedStringJoinList();
			using (var iterator = source.GetEnumerator())
			{
				if (!iterator.MoveNext()) return 3;
				if (iterator.Current != "inherited") return 4;
				if (!iterator.MoveNext()) return 5;
				if (iterator.Current != "values") return 6;
				if (iterator.MoveNext()) return 7;
			}
			var builder = new StringBuilder(1);
			GC.Collect();
			AppendCustomStringJoin(builder, source, separator != 0);
			GC.Collect();
			if (builder.ToString() != (separator == 0 ? "inherited|values" : "inherited::values")) return 100 + builder.Length;
			IEnumerable<string?> empty = new InheritedStringJoinList(empty: true);
			using (var iterator = empty.GetEnumerator())
			{
				GC.Collect();
				if (iterator.MoveNext()) return 8;
			}
			AppendCustomStringJoin(builder, empty, separator != 0);
			GC.Collect();
			if (builder.ToString() != (separator == 0 ? "inherited|values" : "inherited::values")) return 9;
		}
		return 42;
	}

	private sealed class ReimplementedStringJoinList : List<string?>, IEnumerable<string?>
	{
		private readonly StringJoinCallbackState _state;
		private readonly string?[] _values;
		public ReimplementedStringJoinList(StringJoinCallbackState state, string?[] values) : base(2)
		{
			_state = state; _values = values; Add("base storage");
		}
		IEnumerator<string?> IEnumerable<string?>.GetEnumerator()
		{
			GC.Collect(); _state.Creations++;
			return new CustomStringJoinIterator(_state, _values);
		}
		IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<string?>)this).GetEnumerator();
	}

	public static int StringBuilderDerivedListDispatchEntry()
	{
		for (var separator = 0; separator < 2; separator++)
		{
			var state = new StringJoinCallbackState(0);
			IEnumerable<string?> source = new ReimplementedStringJoinList(state, ["custom", "values"]);
			var builder = new StringBuilder(1);
			AppendCustomStringJoin(builder, source, separator != 0);
			GC.Collect();
			if (builder.ToString() != (separator == 0 ? "custom|values" : "custom::values")) return 1;
			if (state.Creations != 1 || state.Moves != 3 || state.Currents != 2 || state.Disposals != 1) return 2;
		}
		return 42;
	}

	private sealed class ReimplementedGenericJoinList<T> : List<T>, IEnumerable<T>
	{
		private readonly GenericJoinState _state;
		private readonly T[] _values;
		public ReimplementedGenericJoinList(GenericJoinState state, T[] values) : base(2)
		{
			_state = state; _values = values; Add(default!);
		}
		IEnumerator<T> IEnumerable<T>.GetEnumerator()
		{
			GC.Collect(); _state.Creations++;
			return new GenericJoinIterator<T>(_values, _state);
		}
		IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();
	}

	private sealed class InheritedGenericJoinList<T> : List<T>
	{
		public InheritedGenericJoinList(T first, T second) : base(2) { Add(first); Add(second); }
	}

	private static int CheckInheritedGenericJoin<T>(T first, T second, string expected)
	{
		for (var separator = 0; separator < 2; separator++)
		{
			IEnumerable<T> source = new InheritedGenericJoinList<T>(first, second);
			var builder = new StringBuilder(1);
			GC.Collect();
			if (separator == 0) builder.AppendJoin('|', source); else builder.AppendJoin("|", source);
			GC.Collect();
			if (builder.ToString() != expected) return 1;
		}
		return 42;
	}

	public static int StringBuilderInheritedGenericListDispatchEntry()
	{
		var previous = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			if (CheckInheritedGenericJoin(1, 2, "1|2") != 42) return 1;
			if (CheckInheritedGenericJoin<object>("inherited", "values", "inherited|values") != 42) return 2;
			if (CheckInheritedGenericJoin(1m, 2m, "1|2") != 42) return 3;
			if (CheckInheritedGenericJoin(1.5f, 2.5f, "1.5|2.5") != 42) return 4;
			if (CheckInheritedGenericJoin(1.5d, 2.5d, "1.5|2.5") != 42) return 5;
			if (CheckInheritedGenericJoin((ushort)1, (ushort)2, "1|2") != 42) return 6;
			if (CheckInheritedGenericJoin(true, false, "True|False") != 42) return 7;
			if (CheckInheritedGenericJoin(new JoinWordValue { Tag = 31 }, new JoinWordValue(), "word|other") != 42) return 8;
			return CheckInheritedGenericJoin<int?>(1, null, "1|");
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
	}

	public static int StringBuilderInheritedIntListDispatchEntry() => CheckInheritedGenericJoin(1, 2, "1|2");
	public static int StringBuilderInheritedDecimalListDispatchEntry() => CheckInheritedGenericJoin(1m, 2m, "1|2");

	public static int StringBuilderInheritedNullableListOwnershipEntry()
	{
		var list = new InheritedGenericJoinList<JoinNestedReferenceValue?>(new JoinNestedReferenceValue {
			Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() }
		}, null);
		using var iterator = ((IEnumerable<JoinNestedReferenceValue?>)list).GetEnumerator();
		if (!iterator.MoveNext()) return 1;
		list[0] = null;
		CollectReferenceFormattingGarbage();
		var copy = iterator.Current;
		if (!copy.HasValue || !ReferenceFormattingTextIsOriginal(copy.Value.Payload.Text, copy.Value.Payload.Tail)) return 2;
		iterator.Dispose();
		CollectReferenceFormattingGarbage();
		if (!ReferenceFormattingTextIsOriginal(copy.Value.Payload.Text, copy.Value.Payload.Tail)) return 3;
		return CheckInheritedGenericJoin(copy, null, "R\u03A9\0\uD800|");
	}

	private static int CheckDerivedGenericJoin<T>(T first, T second, string expected)
	{
		for (var separator = 0; separator < 2; separator++)
		{
			var state = new GenericJoinState();
			var values = new T[2]; values[0] = first; values[1] = second;
			IEnumerable<T> source = new ReimplementedGenericJoinList<T>(state, values);
			var builder = new StringBuilder(1);
			if (separator == 0) builder.AppendJoin('|', source); else builder.AppendJoin("|", source);
			GC.Collect();
			if (builder.ToString() != expected) return 1;
			if (state.Creations != 1 || state.Moves != 3 || state.Currents != 2 || state.Disposals != 1) return 2;
		}
		return 42;
	}

	public static int StringBuilderDerivedGenericListDispatchEntry()
	{
		var previous = System.Globalization.CultureInfo.CurrentCulture;
		try
		{
			System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
			if (CheckDerivedGenericJoin(1, 2, "1|2") != 42) return 1;
			if (CheckDerivedGenericJoin<object>("custom", "values", "custom|values") != 42) return 2;
			if (CheckDerivedGenericJoin(1m, 2m, "1|2") != 42) return 3;
			if (CheckDerivedGenericJoin(1.5f, 2.5f, "1.5|2.5") != 42) return 4;
			if (CheckDerivedGenericJoin(1.5d, 2.5d, "1.5|2.5") != 42) return 5;
			if (CheckDerivedGenericJoin((ushort)1, (ushort)2, "1|2") != 42) return 6;
			if (CheckDerivedGenericJoin(true, false, "True|False") != 42) return 7;
			if (CheckDerivedGenericJoin(new JoinWordValue { Tag = 31 }, new JoinWordValue(), "word|other") != 42) return 8;
			return CheckDerivedGenericJoin<int?>(1, null, "1|");
		}
		finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
	}
}
