/*
- Copyright (C) 2026 Ilkka Lehtoranta
- SPDX-License-Identifier: MIT
*/
using System.Runtime.CompilerServices;
using System.Text;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	private struct InterfaceReferenceReturnWords
	{
		public string? Text;
		public int First, Second, Third;
	}
	private interface IInterfaceReferenceReturn { InterfaceReferenceReturnWords Current { get; } }
	private sealed class InterfaceReferenceReturnClass(InterfaceReturnState state, bool throws) : IInterfaceReferenceReturn
	{
		public InterfaceReferenceReturnWords Current => BuildInterfaceReferenceReturn(state, throws);
	}
	private struct InterfaceReferenceReturnStruct(InterfaceReturnState state) : IInterfaceReferenceReturn
	{
		public InterfaceReferenceReturnWords Current => BuildInterfaceReferenceReturn(state, false);
	}
	private static InterfaceReferenceReturnWords BuildInterfaceReferenceReturn(InterfaceReturnState state, bool throws)
	{
		var text = new StringBuilder(1).Append("owned\u03A9\0").ToString();
		GC.Collect(); state.Calls++;
		if (throws) throw state.Failure;
		return new InterfaceReferenceReturnWords { Text = text, First = 0x12345678, Second = int.MinValue, Third = 42 };
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static InterfaceReferenceReturnWords ReadInterfaceReferenceReturn(IInterfaceReferenceReturn source) => source.Current;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static InterfaceReferenceReturnWords ReadConstrainedInterfaceReferenceReturn<T>(ref T source) where T : IInterfaceReferenceReturn => source.Current;
	private static bool ValidInterfaceReferenceReturn(InterfaceReferenceReturnWords value) =>
		value.Text == "owned\u03A9\0" && value.First == 0x12345678 && value.Second == int.MinValue && value.Third == 42;
	public static int AggregateInterfaceReferenceReturnsPreserveOwnersEntry()
	{
		var state = new InterfaceReturnState();
		var first = ReadInterfaceReferenceReturn(new InterfaceReferenceReturnClass(state, false));
		GC.Collect();
		if (!ValidInterfaceReferenceReturn(first)) return 1;
		IInterfaceReferenceReturn boxed = new InterfaceReferenceReturnStruct(state);
		var second = ReadInterfaceReferenceReturn(boxed);
		GC.Collect();
		if (!ValidInterfaceReferenceReturn(first) || !ValidInterfaceReferenceReturn(second) || ReferenceEquals(first.Text, second.Text)) return 2;
		var value = new InterfaceReferenceReturnStruct(state);
		var third = ReadConstrainedInterfaceReferenceReturn(ref value);
		GC.Collect();
		if (!ValidInterfaceReferenceReturn(third) || !ValidInterfaceReferenceReturn(first)) return 3;
		try { ReadInterfaceReferenceReturn(new InterfaceReferenceReturnClass(state, true)); return 4; }
		catch (InvalidOperationException exception) { if (!ReferenceEquals(exception, state.Failure)) return 5; }
		var retry = ReadInterfaceReferenceReturn(new InterfaceReferenceReturnClass(state, false));
		GC.Collect();
		return ValidInterfaceReferenceReturn(first) && ValidInterfaceReferenceReturn(second) && ValidInterfaceReferenceReturn(third) && ValidInterfaceReferenceReturn(retry) && state.Calls == 5 ? 42 : 6;
	}
}
