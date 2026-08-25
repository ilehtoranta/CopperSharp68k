using System.Runtime.CompilerServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class FunctionAddressSwitchFixtures
{
	private static int _fallbackTrace;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint PureEntry()
	{
		var defaultAddress = APTR.FromPointer(unchecked((uint)(nuint)
			(delegate*<int, int>)&DefaultTarget));
		return PureSwitch(-1).Raw == defaultAddress.Raw &&
			PureSwitch(0).Raw == APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target0)).Raw &&
			PureSwitch(3).Raw == defaultAddress.Raw &&
			PureSwitch(7).Raw == APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target7)).Raw &&
			PureSwitch(11).Raw == APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target11)).Raw &&
			PureSwitch(12).Raw == defaultAddress.Raw
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe APTR PureSwitch(int value)
	{
		switch (value)
		{
			case 0: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target0));
			case 1: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target1));
			case 2: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target2));
			case 4: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target4));
			case 5: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target5));
			case 6: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target6));
			case 7: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target7));
			case 8: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target8));
			case 9: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target9));
			case 10: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target10));
			case 11: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target11));
			default: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&DefaultTarget));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint FallbackEntry()
	{
		_fallbackTrace = 0;
		var defaultAddress = APTR.FromPointer(unchecked((uint)(nuint)
			(delegate*<int, int>)&DefaultTarget));
		var first = SwitchWithImpureArm(0);
		var impure = SwitchWithImpureArm(4);
		var hole = SwitchWithImpureArm(3);
		var above = SwitchWithImpureArm(12);
		return first.Raw == APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target0)).Raw &&
			impure.Raw == APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target4)).Raw &&
			hole.Raw == defaultAddress.Raw &&
			above.Raw == defaultAddress.Raw &&
			_fallbackTrace == 1
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe uint TaggedEntry()
	{
		var defaultAddress = unchecked((uint)(nuint)
			(delegate*<int, int>)&DefaultTarget);
		var target0 = unchecked((uint)(nuint)(delegate*<int, int>)&Target0);
		var target7 = unchecked((uint)(nuint)(delegate*<int, int>)&Target7);
		return TaggedSwitch(-1).Raw == defaultAddress &&
			TaggedSwitch(0).Raw == (target0 | 1u) &&
			TaggedSwitch(3).Raw == defaultAddress &&
			TaggedSwitch(7).Raw == (target7 | 1u) &&
			TaggedSwitch(11).Raw == (defaultAddress | 1u) &&
			TaggedSwitch(12).Raw == defaultAddress
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe APTR TaggedSwitch(int value)
	{
		switch (value)
		{
			case 0: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target0) | 1u);
			case 1: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target1));
			case 2: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target2));
			case 4: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target4) | 1u);
			case 5: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target5));
			case 6: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target6));
			case 7: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target7) | 1u);
			case 8: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target8));
			case 9: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target9));
			case 10: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target10));
			case 11: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&DefaultTarget) | 1u);
			default: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&DefaultTarget));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe APTR SwitchWithImpureArm(int value)
	{
		switch (value)
		{
			case 0: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target0));
			case 1: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target1));
			case 2: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target2));
			case 4:
				_fallbackTrace++;
				return APTR.FromPointer(unchecked((uint)(nuint)
					(delegate*<int, int>)&Target4));
			case 5: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target5));
			case 6: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target6));
			case 7: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target7));
			case 8: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target8));
			case 9: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target9));
			case 10: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target10));
			case 11: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&Target11));
			default: return APTR.FromPointer(unchecked((uint)(nuint)
				(delegate*<int, int>)&DefaultTarget));
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target0(int value) => value;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target1(int value) => value + 1;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target2(int value) => value + 2;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target4(int value) => value + 4;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target5(int value) => value + 5;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target6(int value) => value + 6;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target7(int value) => value + 7;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target8(int value) => value + 8;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target9(int value) => value + 9;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target10(int value) => value + 10;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Target11(int value) => value + 11;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DefaultTarget(int value) => value - 1;
}
