/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private struct InterfaceReturnWords
	{
		public int First, Second, Third, Fourth;
	}
	private sealed class InterfaceReturnState
	{
		public int Calls;
		public readonly InvalidOperationException Failure = new("aggregate getter");
	}
	private interface IInterfaceReturnWords { InterfaceReturnWords Current { get; } }
	private interface IParameterizedInterfaceReturnWords { InterfaceReturnWords Read(int value); }
	private sealed class ParameterizedInterfaceReturn : IParameterizedInterfaceReturnWords
	{
		public InterfaceReturnWords Read(int value) => new() { First = value };
	}
	public static int ParameterizedAggregateInterfaceReturnEntry()
	{
		IParameterizedInterfaceReturnWords source = new ParameterizedInterfaceReturn();
		return source.Read(42).First;
	}
	private sealed class InterfaceReturnClass(InterfaceReturnState state, bool throws) : IInterfaceReturnWords
	{
		public InterfaceReturnWords Current
		{
			get
			{
				GC.Collect(); state.Calls++;
				if (throws) throw state.Failure;
				return new InterfaceReturnWords { First = 0x12345678, Second = -1, Third = int.MinValue, Fourth = 42 };
			}
		}
	}
	private struct InterfaceReturnStruct(InterfaceReturnState state) : IInterfaceReturnWords
	{
		public InterfaceReturnWords Current
		{
			get
			{
				GC.Collect(); state.Calls++;
				return new InterfaceReturnWords { First = 17, Second = int.MaxValue, Third = -123, Fourth = 0x76543210 };
			}
		}
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static InterfaceReturnWords ReadInterfaceReturn(IInterfaceReturnWords source) => source.Current;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static InterfaceReturnWords ReadConstrainedInterfaceReturn<T>(ref T source) where T : IInterfaceReturnWords => source.Current;
	public static int AggregateInterfaceReturnsPreserveWordsAndExceptionsEntry()
	{
		var state = new InterfaceReturnState();
		var first = ReadInterfaceReturn(new InterfaceReturnClass(state, false));
		GC.Collect();
		if (first.First != 0x12345678 || first.Second != -1 || first.Third != int.MinValue || first.Fourth != 42) return 1;
		IInterfaceReturnWords boxed = new InterfaceReturnStruct(state);
		var second = ReadInterfaceReturn(boxed);
		GC.Collect();
		if (second.First != 17 || second.Second != int.MaxValue || second.Third != -123 || second.Fourth != 0x76543210) return 2;
		var value = new InterfaceReturnStruct(state);
		var third = ReadConstrainedInterfaceReturn(ref value);
		if (third.First != 17 || third.Second != int.MaxValue || third.Third != -123 || third.Fourth != 0x76543210) return 3;
		try { ReadInterfaceReturn(new InterfaceReturnClass(state, true)); return 4; }
		catch (InvalidOperationException exception) { if (!ReferenceEquals(exception, state.Failure)) return 5; }
		var retry = ReadInterfaceReturn(new InterfaceReturnClass(state, false));
		return retry.First == first.First && retry.Second == first.Second && retry.Third == first.Third && retry.Fourth == first.Fourth && state.Calls == 5 ? 42 : 6;
	}
}
