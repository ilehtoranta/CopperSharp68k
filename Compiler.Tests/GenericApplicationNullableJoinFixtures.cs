/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	public static int CoreLibApplicationNullableTransportEntry()
	{
		var values = new JoinNestedReferenceValue?[2];
		values[0] = new JoinNestedReferenceValue { Tag = 31, Payload = new() {
			Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		var copy = values[0]; values[0] = null; CollectReferenceFormattingGarbage();
		if (!copy.HasValue || values[0].HasValue) return 1;
		var value = copy.Value;
		if (value.Tag != 31 || !ReferenceFormattingTextIsOriginal(value.Payload.Text, value.Payload.Tail)) return 2;
		object? boxed = copy; copy = null; CollectReferenceFormattingGarbage();
		if (boxed is not JoinNestedReferenceValue unboxed || unboxed.Tag != 31 ||
			!ReferenceFormattingTextIsOriginal(unboxed.Payload.Text, unboxed.Payload.Tail)) return 3;
		JoinNestedReferenceValue? empty = null;
		object? emptyBox = empty;
		var fallback = empty.GetValueOrDefault(unboxed);
		return emptyBox == null && fallback.Tag == 31 && ReferenceFormattingTextIsOriginal(fallback.Payload.Text, fallback.Payload.Tail) ? 42 : 4;
	}

	public static int CoreLibGenericApplicationNullableJoinsEntry()
	{
		if (CheckSmallNullableJoin(new JoinWordValue { Tag = 31 }, default(JoinWordValue), "word", "other") != 42) return 1;
		if (CheckSmallNullableJoin(new JoinWideValue { First = 31, Second = -7 }, default(JoinWideValue), "wide", "other") != 42) return 2;
		if (CheckSmallNullableJoin(new JoinReferenceValue { Text = new string("first".AsSpan()) },
			new JoinReferenceValue { Text = new string("second".AsSpan()) }, "first", "second") != 42) return 5;
		if (CheckSmallNullableJoin(new DefaultWideNameValue { First = 31, Second = -7 }, default(DefaultWideNameValue),
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultWideNameValue", "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultWideNameValue") != 42) return 6;
		var first = new JoinNestedReferenceValue { Tag = 31, Payload = new() {
			Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		var second = new JoinNestedReferenceValue { Tag = 31, Payload = new() {
			Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		if (CheckSmallNullableJoin(first, second, "R\u03A9\0\uD800", "R\u03A9\0\uD800") != 42) return 3;
		return CheckSmallNullableJoin(new JoinGenericValue<string> { Value = new string("first".AsSpan()), Tag = 31 },
			new JoinGenericValue<string> { Value = new string("second".AsSpan()), Tag = 31 }, "first", "second");
	}

	public static int CoreLibGenericApplicationNullableCallbacksEntry()
	{
		if (GenericJoinCallbacks<JoinWideValue?>(new JoinWideValue { First = 31, Second = -7 }, null, "wide", "") != 42) return 1;
		var value = new JoinGenericValue<string> { Value = new string("R\u03A9\0\uD800".AsSpan()), Tag = 31 };
		if (GenericJoinCallbacks<JoinGenericValue<string>?>(null, value, "", "R\u03A9\0\uD800") != 42) return 2;
		return CheckStructGenericJoin<JoinGenericValue<string>?>(value, null, "R\u03A9\0\uD800", "");
	}

	public static int CoreLibApplicationNullablePublicOwnershipEntry()
	{
		var list = new List<JoinNestedReferenceValue?>(2);
		list.Add(new JoinNestedReferenceValue { Tag = 31, Payload = new() {
			Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } });
		list.Add(null);
		using var iterator = list.GetEnumerator();
		if (!iterator.MoveNext()) return 1;
		list[0] = null; CollectReferenceFormattingGarbage();
		var copy = iterator.Current;
		if (!copy.HasValue || copy.Value.Tag != 31 || !ReferenceFormattingTextIsOriginal(copy.Value.Payload.Text, copy.Value.Payload.Tail)) return 2;
		object? boxed = copy; copy = null; iterator.Dispose(); CollectReferenceFormattingGarbage();
		return boxed is JoinNestedReferenceValue unboxed && ReferenceFormattingTextIsOriginal(unboxed.Payload.Text, unboxed.Payload.Tail) ? 42 : 3;
	}

	public static int CoreLibGenericApplicationNullableOwnershipEntry()
	{
		var values = new JoinNestedReferenceValue?[2];
		values[0] = new JoinNestedReferenceValue { Tag = 31, Payload = new() {
			Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() } };
		IEnumerator<JoinNestedReferenceValue?> iterator = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(values);
		if (!iterator.MoveNext()) return 1;
		var copy = iterator.Current; values[0] = null; CollectReferenceFormattingGarbage();
		if (!copy.HasValue || !ReferenceFormattingTextIsOriginal(copy.Value.Payload.Text, copy.Value.Payload.Tail)) return 8;
		copy = null; CollectReferenceFormattingGarbage();
		var current = iterator.Current;
		if (!current.HasValue || current.Value.Tag != 31 || !ReferenceFormattingTextIsOriginal(current.Value.Payload.Text, current.Value.Payload.Tail)) return 2;
		object? boxed = current; current = null;
		iterator.Dispose(); iterator.Dispose(); CollectReferenceFormattingGarbage();
		if (iterator.Current.HasValue || iterator.MoveNext()) return 3;
		if (boxed is not JoinNestedReferenceValue unboxed || !ReferenceFormattingTextIsOriginal(unboxed.Payload.Text, unboxed.Payload.Tail)) return 4;
		var list = new List<JoinNestedReferenceValue?>(2); list.Add(unboxed); list.Add(null);
		iterator = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(list);
		if (!iterator.MoveNext()) return 5;
		unboxed = default; list[0] = null; CollectReferenceFormattingGarbage();
		current = iterator.Current;
		if (!current.HasValue || !ReferenceFormattingTextIsOriginal(current.Value.Payload.Text, current.Value.Payload.Tail)) return 6;
		copy = current; current = null; iterator.Dispose(); CollectReferenceFormattingGarbage();
		return copy.HasValue && ReferenceFormattingTextIsOriginal(copy.Value.Payload.Text, copy.Value.Payload.Tail) ? 42 : 7;
	}

	public static int CoreLibGenericApplicationNullableCapacityEntry()
	{
		var value = new JoinGenericValue<string> { Value = new string("reference".AsSpan()), Tag = 31 };
		return BooleanCharJoinCapacity<JoinGenericValue<string>?>(value, null) == 42 ? NullableJoinLeadingEmptyCapacity(value) : 1;
	}

	public static int CoreLibGenericApplicationNullableAllocationEntry() =>
		BooleanCharJoinAllocation<JoinGenericValue<string>?>(null, new JoinGenericValue<string> { Value = new string("reference".AsSpan()), Tag = 31 }, "", "reference");
}
