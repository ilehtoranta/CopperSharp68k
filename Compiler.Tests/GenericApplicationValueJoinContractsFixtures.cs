/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private static JoinNestedReferenceValue CreateOwnedJoinReference() => new()
	{
		Tag = 31, Payload = new() { Text = new string("R\u03A9\0\uD800".AsSpan()), Tail = CreateReferenceFormattingTail() }
	};
	public static int CoreLibGenericApplicationValueCallbackEntry()
	{
		if (GenericJoinCallbacks(new JoinWordValue { Tag = 31 }, new JoinWordValue(), "word", "other") != 42) return 1;
		if (GenericJoinCallbacks(new JoinWideValue { First = 31, Second = -7 }, new JoinWideValue(), "wide", "other") != 42) return 2;
		if (GenericJoinCallbacks(CreateOwnedJoinReference(), CreateOwnedJoinReference(), "R\u03A9\0\uD800", "R\u03A9\0\uD800") != 42) return 3;
		if (CheckStructGenericJoin(new JoinWideValue { First = 31, Second = -7 }, new JoinWideValue(), "wide", "other") != 42) return 4;
		return CheckStructGenericJoin(CreateOwnedJoinReference(), CreateOwnedJoinReference(), "R\u03A9\0\uD800", "R\u03A9\0\uD800");
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerator<JoinNestedReferenceValue> CreateOwnedApplicationJoin(GenericJoinState state) =>
		CreateOwnedGenericJoin(CreateOwnedJoinReference(), CreateOwnedJoinReference(), state);
	public static int CoreLibGenericApplicationValueOwnershipEntry()
	{
		var state = new GenericJoinState(); var iterator = CreateOwnedApplicationJoin(state); CollectReferenceFormattingGarbage();
		if (!iterator.MoveNext()) return 1;
		var held = iterator.Current; iterator.Dispose(); iterator.Dispose(); CollectReferenceFormattingGarbage();
		if (held.Tag != 31 || !ReferenceFormattingTextIsOriginal(held.Payload.Text, held.Payload.Tail)) return 2;
		var cleared = iterator.Current;
		if (iterator.MoveNext() || cleared.Tag != 0 || cleared.Payload.Text != null || cleared.Payload.Tail != null) return 3;
		return state.Creations == 1 && state.Moves == 1 && state.Currents == 1 && state.Disposals == 1 ? 42 : 4;
	}
	public static int CoreLibGenericApplicationValueAllocationEntry()
	{
		if (BooleanCharJoinAllocation(new JoinWideValue { First = 31, Second = -7 }, new JoinWideValue(), "wide", "other") != 42) return 1;
		return BooleanCharJoinAllocation(CreateOwnedJoinReference(), CreateOwnedJoinReference(), "R\u03A9\0\uD800", "R\u03A9\0\uD800");
	}
	public static int CoreLibGenericApplicationValueCapacityEntry()
	{
		if (BooleanCharJoinCapacity(new JoinWideValue { First = 31, Second = -7 }, new JoinWideValue()) != 42) return 1;
		return BooleanCharJoinCapacity(CreateOwnedJoinReference(), CreateOwnedJoinReference());
	}
	private sealed class JoinFormatterState
	{
		public int Calls, ThrowAt;
		public bool ReturnNull;
		public readonly InvalidOperationException Error = new("formatting");
	}
	private struct JoinMutableFormatterValue
	{
		public JoinFormatterState State;
		public int Tag;
		public override string ToString()
		{
			GC.Collect(); State.Calls++;
			if (State.Calls == State.ThrowAt) throw State.Error;
			Tag++;
			return State.ReturnNull ? null! : Tag == 32 ? "value" : "corrupt";
		}
	}
	public static int CoreLibGenericApplicationValueFormatterContractsEntry()
	{
		for (var sourceKind = 0; sourceKind < 4; sourceKind++)
		for (var text = 0; text < 2; text++)
		for (var fault = 0; fault < 4; fault++)
		{
			var formatting = new JoinFormatterState { ThrowAt = fault < 3 ? fault : 0, ReturnNull = fault == 3 };
			var values = new JoinMutableFormatterValue[2];
			values[0] = new() { State = formatting, Tag = 31 }; values[1] = new() { State = formatting, Tag = 31 };
			var enumeration = new GenericJoinState();
			var list = new List<JoinMutableFormatterValue>(2); list.Add(values[0]); list.Add(values[1]);
			IEnumerable<JoinMutableFormatterValue> source = sourceKind == 0 ? values : sourceKind == 1 ? list :
				sourceKind == 2 ? new GenericJoinSource<JoinMutableFormatterValue>(values, enumeration) : new StructGenericJoinSource<JoinMutableFormatterValue>(values, enumeration);
			var builder = new StringBuilder(1).Append("seed"); var snapshot = builder.ToString();
			Exception? caught = null;
			try { AppendGenericJoin(builder, source, text != 0); } catch (Exception error) { caught = error; }
			if (fault is 1 or 2 ? !ReferenceEquals(caught, formatting.Error) : caught != null) return 1;
			var separator = text == 0 ? "|" : "::";
			var expected = fault == 1 ? "seed" : fault == 2 ? "seedvalue" + separator : fault == 3 ? "seed" + separator : "seedvalue" + separator + "value";
			if (builder.ToString() != expected || snapshot != "seed" || formatting.Calls != (fault == 1 ? 1 : 2)) return 2;
			if (values[0].Tag != 31 || values[1].Tag != 31 || list[0].Tag != 31 || list[1].Tag != 31) return 3;
			if (sourceKind >= 2 && (enumeration.Creations != 1 || enumeration.Moves != (fault == 1 ? 1 : fault == 2 ? 2 : 3) ||
				enumeration.Currents != (fault == 1 ? 1 : 2) || enumeration.Disposals != 1)) return 4;
			formatting.ThrowAt = 0; formatting.ReturnNull = false; formatting.Calls = 0;
			builder.Clear().Append("seed"); AppendGenericJoin(builder, source, text != 0); GC.Collect();
			if (builder.ToString() != "seedvalue" + separator + "value" || formatting.Calls != 2 || snapshot != "seed") return 5;
		}
		return 42;
	}
}
