/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
 */
using System.Collections;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private enum JoinSByte : sbyte { Named = -128 }
	private enum JoinByte : byte { Named = 255 }
	private enum JoinInt16 : short { Named = -32768 }
	private enum JoinUInt16 : ushort { Named = 65535 }
	private enum JoinInt32 : int { Named = int.MinValue }
	private enum JoinUInt32 : uint { Named = uint.MaxValue }
	[Flags] private enum JoinInt64 : long { First = 1, Second = 2, Named = long.MinValue }
	[Flags] private enum JoinUInt64 : ulong { First = 1, Second = 2, Named = ulong.MaxValue }
	private sealed class GenericJoinState(int fault = 0)
	{
		public readonly int Fault = fault;
		public readonly InvalidOperationException Failure = new("enumeration");
		public readonly InvalidOperationException DisposalFailure = new("disposal");
		public int Creations, Moves, Currents, Disposals;
	}
	private sealed class GenericJoinSource<T>(T[] values, GenericJoinState state) : IEnumerable<T>
	{
		public IEnumerator<T> GetEnumerator()
		{
			GC.Collect(); state.Creations++;
			if (state.Fault == 1) throw state.Failure;
			return state.Fault == 8 ? null! : new GenericJoinIterator<T>(values, state);
		}
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
	private sealed class GenericJoinIterator<T>(T[] values, GenericJoinState state) : IEnumerator<T>
	{
		private int _index = -1;
		public T Current { get { GC.Collect(); state.Currents++; if (state.Fault == 3 || state.Fault == 5 && _index == 1) throw state.Failure; return values[_index]; } }
		object? IEnumerator.Current => Current;
		public bool MoveNext()
		{
			GC.Collect(); state.Moves++; _index++;
			if (state.Fault == 2 || (state.Fault == 4 || state.Fault == 7) && _index == 1) throw state.Failure;
			return state.Fault != 9 && _index < values.Length;
		}
		public void Dispose() { GC.Collect(); state.Disposals++; if (state.Fault == 6) throw state.Failure; if (state.Fault == 7) throw state.DisposalFailure; }
		public void Reset() => throw new NotSupportedException();
	}
	private static int CheckGenericJoin<T>(T first, T second, string expected)
	{
		T[] values = new T[2]; values[0] = first; values[1] = second;
		for (var sourceKind = 0; sourceKind < 3; sourceKind++)
		for (var capacity = 1; capacity <= 192; capacity += 191)
		for (var character = 0; character < 2; character++)
		{
			var state = new GenericJoinState();
			var list = new List<T>(2); list.Add(first); list.Add(second);
			IEnumerable<T> source = sourceKind == 0 ? values : sourceKind == 1 ? list : new GenericJoinSource<T>(values, state);
			var builder = new StringBuilder(capacity).Append('[');
			if (character == 0) builder.AppendJoin("|", source); else builder.AppendJoin('|', source);
			builder.Append(']'); GC.Collect();
			var snapshot = builder.ToString();
			if (snapshot != expected) return 1 + sourceKind;
			if (sourceKind == 2 && (state.Creations != 1 || state.Moves != 3 || state.Currents != 2 || state.Disposals != 1)) return 4;
			builder.Clear().AppendJoin('|', source); GC.Collect();
			if ("[" + builder.ToString() + "]" != expected) return 5 + 10 * sourceKind + 100 * character + (capacity > 1 ? 1000 : 0);
			if (snapshot != expected)
			{
				return 7 + 10 * sourceKind + 100 * character + (capacity > 1 ? 1000 : 0);
			}
			if (sourceKind == 2 && (state.Creations != 2 || state.Moves != 6 || state.Currents != 4 || state.Disposals != 2)) return 6;
		}
		return 42;
	}
	public static int CoreLibStringBuilderGenericIntegralJoinsEntry()
	{
		if (CheckGenericJoin<sbyte>(sbyte.MinValue, sbyte.MaxValue, "[-128|127]") != 42) return 1;
		if (CheckGenericJoin<byte>(0, byte.MaxValue, "[0|255]") != 42) return 2;
		if (CheckGenericJoin<short>(short.MinValue, short.MaxValue, "[-32768|32767]") != 42) return 3;
		if (CheckGenericJoin<ushort>(0, ushort.MaxValue, "[0|65535]") != 42) return 4;
		if (CheckGenericJoin<uint>(0, uint.MaxValue, "[0|4294967295]") != 42) return 5;
		if (CheckGenericJoin<long>(long.MinValue, long.MaxValue, "[-9223372036854775808|9223372036854775807]") != 42) return 6;
		if (CheckGenericJoin<ulong>(0, ulong.MaxValue, "[0|18446744073709551615]") != 42) return 7;
		return 42;
	}
	public static int CoreLibStringBuilderGenericEnumJoinsEntry()
	{
		if (CheckGenericJoin(JoinSByte.Named, (JoinSByte)127, "[Named|127]") != 42) return 1;
		if (CheckGenericJoin(JoinByte.Named, (JoinByte)0, "[Named|0]") != 42) return 2;
		if (CheckGenericJoin(JoinInt16.Named, (JoinInt16)32767, "[Named|32767]") != 42) return 3;
		if (CheckGenericJoin(JoinUInt16.Named, (JoinUInt16)0, "[Named|0]") != 42) return 4;
		if (CheckGenericJoin(JoinInt32.Named, (JoinInt32)int.MaxValue, "[Named|2147483647]") != 42) return 5;
		if (CheckGenericJoin(JoinUInt32.Named, (JoinUInt32)0, "[Named|0]") != 42) return 6;
		if (CheckGenericJoin(JoinInt64.Named, JoinInt64.First | JoinInt64.Second, "[Named|First, Second]") != 42) return 7;
		if (CheckGenericJoin(JoinUInt64.Named, JoinUInt64.First | JoinUInt64.Second, "[Named|First, Second]") != 42) return 8;
		return 42;
	}
}
