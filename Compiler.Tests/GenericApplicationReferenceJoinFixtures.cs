/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private interface IJoinReference { }
	private sealed class JoinReferenceObject : IJoinReference
	{
		public string Text = "";
		public override string ToString() { GC.Collect(); return new string(Text.AsSpan()); }
	}
	private sealed class JoinGenericReference<T>
	{
		public T Value = default!;
		public override string ToString() { GC.Collect(); return Value is string text ? new string(text.AsSpan()) : "generic"; }
	}
	private static int CheckApplicationReferenceJoin<T>(T first, T second, string firstText, string secondText) where T : class
	{
		for (var bits = 0; bits < 4; bits++)
		{
			var left = (bits & 1) != 0 ? first : null;
			var right = (bits & 2) != 0 ? second : null;
			var prefix = "[" + ((bits & 1) != 0 ? firstText : "") + "|";
			var expected = prefix + ((bits & 2) != 0 ? secondText : "") + "]";
			var result = CheckGenericJoin(left, right, expected);
			if (result != 42) return 100 * bits + result;
		}
		return 42;
	}
	public static int CoreLibGenericApplicationReferenceJoinsEntry()
	{
		if (CheckApplicationReferenceJoin(new DefaultClassName(), new DefaultClassName(),
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultClassName", "CopperSharp.Compiler.Tests.CompilerFixtures+DefaultClassName") != 42) return 1;
		if (CheckApplicationReferenceJoin<DefaultClassName>(new DefaultDerivedClassName(), new HiddenDefaultClassName(),
			"CopperSharp.Compiler.Tests.CompilerFixtures+DefaultDerivedClassName", "CopperSharp.Compiler.Tests.CompilerFixtures+HiddenDefaultClassName") != 42) return 2;
		if (CheckApplicationReferenceJoin<DefaultClassOverride>(new DefaultClassOverride(), new HiddenInheritedClassOverride(), "override", "override") != 42) return 3;
		var first = new JoinReferenceObject { Text = new string("R\u03A9\0\uD800".AsSpan()) };
		var second = new JoinReferenceObject { Text = new string("second".AsSpan()) };
		if (CheckApplicationReferenceJoin(first, second, "R\u03A9\0\uD800", "second") != 42) return 4;
		if (CheckApplicationReferenceJoin<IJoinReference>(first, second, "R\u03A9\0\uD800", "second") != 42) return 5;
		if (CheckApplicationReferenceJoin(new JoinGenericReference<string> { Value = new string("first".AsSpan()) },
			new JoinGenericReference<string> { Value = new string("second".AsSpan()) }, "first", "second") != 42) return 6;
		return CheckApplicationReferenceJoin(new JoinGenericReference<int> { Value = 31 }, new JoinGenericReference<int> { Value = -7 }, "generic", "generic");
	}

	public static int CoreLibGenericApplicationReferenceOwnershipEntry()
	{
		var values = new JoinReferenceObject?[2]; values[0] = new JoinReferenceObject { Text = new string("R\u03A9\0\uD800".AsSpan()) };
		IEnumerator<JoinReferenceObject?> iterator = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(values);
		if (!iterator.MoveNext()) return 1;
		var copy = iterator.Current; values[0] = null; CollectReferenceFormattingGarbage();
		if (copy?.Text != "R\u03A9\0\uD800") return 2;
		copy = null; CollectReferenceFormattingGarbage();
		copy = iterator.Current;
		if (copy == null || copy.Text != "R\u03A9\0\uD800" || !ReferenceEquals(copy, iterator.Current)) return 3;
		iterator.Dispose(); iterator.Dispose(); CollectReferenceFormattingGarbage();
		if (iterator.Current != null || iterator.MoveNext() || copy.Text != "R\u03A9\0\uD800") return 4;
		var list = new List<JoinReferenceObject?>(2); list.Add(copy); list.Add(null);
		iterator = CopperSharp.Runtime.ShadowGenericJoinEnumeration.GetEnumerator(list);
		if (!iterator.MoveNext()) return 5;
		copy = null; list[0] = null; CollectReferenceFormattingGarbage();
		copy = iterator.Current;
		if (copy?.Text != "R\u03A9\0\uD800") return 6;
		iterator.Dispose(); CollectReferenceFormattingGarbage();
		return copy.Text == "R\u03A9\0\uD800" ? 42 : 7;
	}
	public static int CoreLibGenericApplicationReferenceCallbacksEntry()
	{
		var value = new JoinReferenceObject { Text = new string("reference".AsSpan()) };
		if (GenericJoinCallbacks<JoinReferenceObject?>(value, null, "reference", "") != 42) return 1;
		if (GenericJoinCallbacks<IJoinReference?>(null, value, "", "reference") != 42) return 2;
		return CheckStructGenericJoin<JoinReferenceObject?>(value, null, "reference", "");
	}
	public static int CoreLibGenericApplicationReferenceCapacityEntry()
	{
		var value = new JoinReferenceObject { Text = new string("reference".AsSpan()) };
		return BooleanCharJoinCapacity<JoinReferenceObject?>(value, null);
	}
	private sealed class JoinReferenceFormatter
	{
		public JoinFormatterState State = null!;
		public int Tag;
		public override string ToString()
		{
			GC.Collect(); State.Calls++;
			if (State.Calls == State.ThrowAt) throw State.Error;
			Tag++;
			return State.ReturnNull ? null! : "value";
		}
	}
	public static int CoreLibGenericApplicationReferenceFormatterEntry()
	{
		for (var sourceKind = 0; sourceKind < 4; sourceKind++)
		for (var text = 0; text < 2; text++)
		for (var fault = 0; fault < 4; fault++)
		{
			var formatting = new JoinFormatterState { ThrowAt = fault < 3 ? fault : 0, ReturnNull = fault == 3 };
			var values = new JoinReferenceFormatter[2];
			values[0] = new() { State = formatting, Tag = 31 }; values[1] = new() { State = formatting, Tag = 31 };
			var enumeration = new GenericJoinState();
			var list = new List<JoinReferenceFormatter>(2); list.Add(values[0]); list.Add(values[1]);
			IEnumerable<JoinReferenceFormatter> source = sourceKind == 0 ? values : sourceKind == 1 ? list :
				sourceKind == 2 ? new GenericJoinSource<JoinReferenceFormatter>(values, enumeration) : new StructGenericJoinSource<JoinReferenceFormatter>(values, enumeration);
			var builder = new StringBuilder(1).Append("seed"); var snapshot = builder.ToString();
			Exception? caught = null;
			try { AppendGenericJoin(builder, source, text != 0); } catch (Exception error) { caught = error; }
			if (fault is 1 or 2 ? !ReferenceEquals(caught, formatting.Error) : caught != null) return 1;
			var separator = text == 0 ? "|" : "::";
			var expected = fault == 1 ? "seed" : fault == 2 ? "seedvalue" + separator : fault == 3 ? "seed" + separator : "seedvalue" + separator + "value";
			if (builder.ToString() != expected || snapshot != "seed" || formatting.Calls != (fault == 1 ? 1 : 2)) return 2;
			if (values[0].Tag != (fault == 1 ? 31 : 32) || values[1].Tag != (fault is 1 or 2 ? 31 : 32) ||
				!ReferenceEquals(values[0], list[0]) || !ReferenceEquals(values[1], list[1])) return 3;
			if (sourceKind >= 2 && (enumeration.Creations != 1 || enumeration.Moves != (fault == 1 ? 1 : fault == 2 ? 2 : 3) ||
				enumeration.Currents != (fault == 1 ? 1 : 2) || enumeration.Disposals != 1)) return 4;
			formatting.ThrowAt = 0; formatting.ReturnNull = false; formatting.Calls = 0;
			builder.Clear().Append("seed"); AppendGenericJoin(builder, source, text != 0); GC.Collect();
			if (builder.ToString() != "seedvalue" + separator + "value" || formatting.Calls != 2 || snapshot != "seed" ||
				values[0].Tag != (fault == 1 ? 32 : 33) || values[1].Tag != (fault is 1 or 2 ? 32 : 33)) return 5;
		}
		return 42;
	}
	public static int CoreLibGenericApplicationReferenceAllocationEntry() =>
		BooleanCharJoinAllocation<JoinReferenceObject?>(null, new JoinReferenceObject { Text = new string("reference".AsSpan()) }, "", "reference");
}
