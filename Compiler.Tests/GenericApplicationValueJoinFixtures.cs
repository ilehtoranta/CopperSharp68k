/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static JoinWideValue ClearWideJoinArgument(ref JoinWideValue value) { value = default; GC.Collect(); return value; }
	private static int CheckWideJoinCopies(JoinWideValue first, JoinWideValue second) =>
		first.First == 31 && first.Second == -7 && second.First == 0 && second.Second == 0 ? 42 : 1;
	private static int CheckWideJoinArgumentSnapshot(JoinWideValue value) => CheckWideJoinCopies(value, ClearWideJoinArgument(ref value));
	private static JoinNestedReferenceValue ClearReferenceJoinArgument(ref JoinNestedReferenceValue value)
	{
		value = default; CollectReferenceFormattingGarbage(); return value;
	}
	private static int CheckReferenceJoinCopies(JoinNestedReferenceValue first, JoinNestedReferenceValue second) =>
		first.Tag == 31 && ReferenceFormattingTextIsOriginal(first.Payload.Text, first.Payload.Tail) &&
		second.Tag == 0 && second.Payload.Text == null && second.Payload.Tail == null ? 42 : 2;
	private static int CheckReferenceJoinArgumentSnapshot(JoinNestedReferenceValue value) =>
		CheckReferenceJoinCopies(value, ClearReferenceJoinArgument(ref value));
	public static int CoreLibGenericApplicationValueSnapshotEntry()
	{
		if (CheckWideJoinArgumentSnapshot(new() { First = 31, Second = -7 }) != 42) return 1;
		var value = new JoinNestedReferenceValue { Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		if (CheckReferenceJoinArgumentSnapshot(value) != 42) return 2;
		return CheckReferenceJoinCopies(value, ClearReferenceJoinArgument(ref value));
	}
	public static int CoreLibGenericWideValueTransportEntry()
	{
		var values = new JoinWideValue[2]; values[0] = new() { First = 31, Second = -7 };
		if (values[0].First != 31 || values[0].Second != -7) return 101;
		IEnumerator<JoinWideValue> iterator = ShadowGenericJoinEnumeration.GetEnumerator(values);
		if (!iterator.MoveNext()) return 102;
		var first = iterator.Current; GC.Collect();
		if (first.First != 31) return 103;
		if (first.Second != -7) return 104;
		if (first.ToString() != "wide") return 105;
		values[0] = default; GC.Collect();
		var current = iterator.Current;
		if (current.First != 31 || current.Second != -7) return 106;
		iterator.Dispose();
		values[0] = first;
		var builder = new StringBuilder(192).Append('[').AppendJoin('|', values).Append(']');
		if (builder.ToString() != "[wide|other]") return 107;
		var list = new List<JoinWideValue>(2); list.Add(first); list.Add(default);
		iterator = ShadowGenericJoinEnumeration.GetEnumerator(list);
		if (!iterator.MoveNext()) return 108;
		current = iterator.Current;
		if (current.First != 31 || current.Second != -7) return 109;
		iterator.Dispose();
		builder.Clear().Append('[').AppendJoin('|', list).Append(']');
		return builder.ToString() == "[wide|other]" ? 42 : 110;
	}
	private struct JoinWordValue
	{
		public int Tag;
		public override string ToString() { GC.Collect(); return Tag == 31 ? "word" : "other"; }
	}
	private struct JoinWideValue
	{
		public int First, Second;
		public override string ToString() { GC.Collect(); return First == 31 && Second == -7 ? "wide" : "other"; }
	}
	private struct JoinReferenceValue
	{
		public string Text;
		public override string ToString() { GC.Collect(); return new string(Text.AsSpan()); }
	}
	private struct JoinNestedReferenceValue
	{
		public int Tag;
		public ReferenceFormattingPayload<string> Payload;
		public override string ToString()
		{
			GC.Collect();
			if (Tag != 31 || !ReferenceFormattingTextIsOriginal(Payload.Text, Payload.Tail)) throw new InvalidOperationException();
			return new string(Payload.Text.AsSpan());
		}
	}
	private struct JoinGenericValue<T>
	{
		public T Value;
		public int Tag;
		public override string ToString()
		{
			GC.Collect();
			return Value is string text ? new string(text.AsSpan()) : Tag == 31 ? "generic" : "other";
		}
	}
	public static int CoreLibGenericApplicationValueJoinsEntry()
	{
		if (CheckGenericJoin(new JoinWordValue { Tag = 31 }, new JoinWordValue { Tag = 0 }, "[word|other]") != 42) return 1;
		var wideResult = CheckGenericJoin(new JoinWideValue { First = 31, Second = -7 }, new JoinWideValue(), "[wide|other]");
		if (wideResult != 42) return 200 + wideResult;
		if (CheckGenericJoin(new JoinReferenceValue { Text = new string("first".AsSpan()) }, new JoinReferenceValue { Text = new string("second".AsSpan()) }, "[first|second]") != 42) return 3;
		var first = new JoinNestedReferenceValue { Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		var second = new JoinNestedReferenceValue { Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		if (CheckGenericJoin(first, second, "[R\u03A9\0\uD800|R\u03A9\0\uD800]") != 42) return 4;
		if (CheckGenericJoin(new DefaultWideNameValue { First = 31, Second = -7 }, new DefaultWideNameValue(),
			"[CopperSharp.Compiler.Tests.CompilerFixtures+DefaultWideNameValue|CopperSharp.Compiler.Tests.CompilerFixtures+DefaultWideNameValue]") != 42) return 5;
		if (CheckGenericJoin(new JoinGenericValue<int> { Value = 31, Tag = 31 }, new JoinGenericValue<int>(), "[generic|other]") != 42) return 6;
		return CheckGenericJoin(new JoinGenericValue<string> { Value = new string("first".AsSpan()), Tag = 31 },
			new JoinGenericValue<string> { Value = new string("second".AsSpan()), Tag = 31 }, "[first|second]");
	}
}
