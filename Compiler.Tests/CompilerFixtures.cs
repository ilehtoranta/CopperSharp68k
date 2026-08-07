using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public static class CompilerFixtures
{
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiModuleEntry() => ExternalMethods.AddAndDouble(12, 9);

	private sealed class FixtureException : Exception
	{
	}

	private static int _counter;
	private static int _zeroStatic;
	private static uint _terminalScalar;
	private static uint _terminalReadFlag;
	#pragma warning disable CS0414 // Written-only terminal GC fixture.
	private static object? _terminalReference;
	#pragma warning restore CS0414
	private static APTR _terminalAddress;
	#pragma warning disable CS0169 // Targeted only by raw-CIL escape fixtures.
	private static uint _managedByrefStaticEscapeSink;
	#pragma warning restore CS0169

	[M68kEntryPoint]
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DefaultEntry()
	{
		var left = 9;
		var right = 5;
		return Arithmetic(left, right) + LoopAndBranch(6);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalPrivateDefaultStoresEntry()
	{
		_terminalScalar = 0;
		_terminalReference = null;
		_terminalAddress = APTR.Null;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalObservedReferenceStoreEntry()
	{
		_terminalReference = null;
		M68kRuntime.Collect();
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalStoreBeforeUnknownCallEntry()
	{
		_terminalScalar = 0;
		return NonTerminalPrivateStore();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalOverwriteEntry()
	{
		_terminalScalar = 17;
		_terminalScalar = 0;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalConditionalReadEntry()
	{
		_terminalScalar = 0;
		return _terminalReadFlag != 0 ? _terminalScalar : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalEscapedStaticAddressEntry()
	{
		_terminalScalar = 0;
		IgnoreReference(ref _terminalScalar);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void IgnoreReference(ref uint value)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalExceptionalReadEntry()
	{
		try
		{
			_terminalScalar = 0;
			return 84 / _terminalReadFlag;
		}
		catch (DivideByZeroException)
		{
			return _terminalScalar;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalMultipleReturnsEntry()
	{
		if (_terminalReadFlag != 0)
		{
			_terminalScalar = 0;
			return 1;
		}
		_terminalScalar = 0;
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalLoopEntry()
	{
		while (_terminalReadFlag != 0)
		{
			_terminalReadFlag--;
		}
		_terminalScalar = 0;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalArrayStoreEntry()
	{
		var values = new int[1];
		values[0] = 0;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TerminalFinallyStoreEntry()
	{
		try
		{
			return 42;
		}
		finally
		{
			_terminalAddress = APTR.Null;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NonTerminalPrivateStoreEntry() =>
		NonTerminalPrivateStore();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint NonTerminalPrivateStore()
	{
		_terminalAddress = APTR.Null;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Arithmetic(int left, int right)
	{
		var product = left * right;
		var quotient = product / 4;
		var remainder = product % 4;
		return quotient + remainder + (left - right);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArithmeticEntry() => Arithmetic(9, 5);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ForwardStackArgumentEntry() =>
		ForwardStackArgumentTarget("ok", 1, 2, 3, 4);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ForwardStackArgumentTarget(
		string marker,
		int first,
		int second,
		int third,
		int fourth) =>
		(marker.Length * 10_000) + (first * 1_000) + (second * 100) + (third * 10) + fourth;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int TryCatchEntry()
	{
		try
		{
			throw null!;
		}
		catch
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AddressNullBranchInTryEntry()
	{
		var pointer = APTR.FromPointer(0x0000_4400);
		try
		{
			return pointer.IsNull ? 0 : 42;
		}
		catch
		{
			return 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DiscardCallResultInTryEntry()
	{
		try
		{
			DiscardedCallResult();
			return 42;
		}
		catch
		{
			return 0;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DiscardedCallResult() => 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ComparisonStoreBranchInTryEntry()
	{
		var value = DiscardedCallResult();
		try
		{
			return value == 0 ? 0 : 42;
		}
		catch
		{
			return 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int TypedCatchEntry()
	{
		try
		{
			throw null!;
		}
		catch (InvalidOperationException)
		{
			return 1;
		}
		catch (Exception)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CustomExceptionCatchEntry()
	{
		try
		{
			throw new FixtureException();
		}
		catch (FixtureException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DivideByZeroCatchEntry()
	{
		try
		{
			return 84 / _counter;
		}
		catch (DivideByZeroException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CalleeSaveExceptionUnwindEntry()
	{
		_counter = 2;
		try
		{
			return DivideThenThrow();
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DivideThenThrow()
	{
		_ = 84 / _counter;
		throw null!;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullDereferenceCatchEntry()
	{
		ManagedBox? box = null;
		try
		{
			return box!.Value;
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoundsCatchEntry()
	{
		try
		{
			var values = new int[1];
			return values[2];
		}
		catch (IndexOutOfRangeException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int OutOfMemoryCatchEntry()
	{
		try
		{
			_ = new int[1024];
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FinallyEntry()
	{
		var value = 0;
		try
		{
			value = 1;
		}
		finally
		{
			value += 2;
		}

		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CrossMethodCatchEntry()
	{
		try
		{
			ThrowNull();
			return 1;
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowNull()
	{
		throw null!;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnhandledExceptionEntry()
	{
		throw null!;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NestedCatchEntry()
	{
		try
		{
			try
			{
				throw null!;
			}
			catch (InvalidOperationException)
			{
				return 1;
			}
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RethrowEntry()
	{
		try
		{
			try
			{
				throw null!;
			}
			catch
			{
				throw;
			}
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExceptionalFinallyEntry()
	{
		var value = 0;
		try
		{
			try
			{
				throw null!;
			}
			finally
			{
				value = 7;
			}
		}
		catch (NullReferenceException)
		{
			return value + 35;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int A5ImportInsideCatchEntry()
	{
		var value = 0;
		try
		{
			value = ImportedA5Value(41);
			throw null!;
		}
		catch (NullReferenceException)
		{
			return value + 1;
		}
	}

	[M68kImport("fixture.a5Value")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ImportedA5Value(
		[M68kRegister(M68kRegister.A5)] int value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedA5ImportEntry() => ImportedA5Value(42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int A5ImportThroughFramelessEntry()
	{
		var value = 0;
		try
		{
			value = FramelessA5Import();
			throw null!;
		}
		catch (NullReferenceException)
		{
			return value + 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FramelessA5Import() => ImportedA5Value(41);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int A5PromotionThroughFramelessEntry()
	{
		var value = 0u;
		try
		{
			value = FramelessPromotedAptr();
			throw null!;
		}
		catch (NullReferenceException)
		{
			return value == 0x0000_4400u ? 42 : 1;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint FramelessPromotedAptr()
	{
		var pointer = APTR.FromPointer(0x0000_4400);
		return pointer.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExternalFailureCatchEntry()
	{
		try
		{
			ExternalFailure();
			return 1;
		}
		catch (Exception)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern int ExternalFailure();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExternalSuccessEntry() => ExternalSuccess();

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern int ExternalSuccess();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DiscardCallResultEntry()
	{
		Arithmetic(9, 5);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManyAssignedLocalsEntry()
	{
		var a = 1;
		var b = 2;
		var c = 3;
		var d = 4;
		var e = 5;
		var f = 6;
		var g = 7;
		var h = 8;
		return a + b + c + d + e + f + g + h;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BranchAssignedLocalsEntry()
	{
		var condition = 0;
		int a;
		int b;
		int c;
		int d;
		if (condition == 0)
		{
			a = 1;
			b = 2;
			c = 3;
			d = 4;
		}
		else
		{
			a = 5;
			b = 6;
			c = 7;
			d = 8;
		}

		return a + b + c + d;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LoopAndBranch(int count)
	{
		var sum = 0;
		for (var index = 0; index < count; index++)
		{
			sum += index;
		}

		if (sum == 15)
		{
			sum ^= 0x55;
		}

		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CacheBoundaryLoop240Entry() => CacheBoundaryLoop240(2);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CacheBoundaryLoop240(int count)
	{
		var value = 1u;
		var remaining = count;
		while (remaining-- != 0)
		{
			value = (value << 3) ^ (value + 1);
			value = (value << 3) ^ (value + 2);
			value = (value << 3) ^ (value + 3);
			value = (value << 3) ^ (value + 4);
			value = (value << 3) ^ (value + 5);
			value = (value << 3) ^ (value + 6);
			value = (value << 3) ^ (value + 7);
			value = (value << 3) ^ (value + 8);
			value = (value << 3) ^ (value + 9);
			value = (value << 3) ^ (value + 10);
			value = (value << 3) ^ (value + 11);
			value = (value << 3) ^ (value + 12);
			value = (value << 3) ^ (value + 13);
			value = (value << 3) ^ (value + 14);
			value = (value << 3) ^ (value + 15);
			value = (value << 3) ^ (value + 16);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CacheBoundaryLoop256Entry() => CacheBoundaryLoop256(2);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CacheBoundaryLoop256(int count)
	{
		var value = 1u;
		var remaining = count;
		while (remaining-- != 0)
		{
			value = (value << 3) ^ (value + 1);
			value = (value << 3) ^ (value + 2);
			value = (value << 3) ^ (value + 3);
			value = (value << 3) ^ (value + 4);
			value = (value << 3) ^ (value + 5);
			value = (value << 3) ^ (value + 6);
			value = (value << 3) ^ (value + 7);
			value = (value << 3) ^ (value + 8);
			value = (value << 3) ^ (value + 9);
			value = (value << 3) ^ (value + 10);
			value = (value << 3) ^ (value + 11);
			value = (value << 3) ^ (value + 12);
			value = (value << 3) ^ (value + 13);
			value = (value << 3) ^ (value + 14);
			value = (value << 3) ^ (value + 15);
			value = (value << 3) ^ (value + 16);
			value = (value << 3) ^ (value + 17);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CacheBoundaryLoop280Entry() => CacheBoundaryLoop280(2);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CacheBoundaryLoop280(int count)
	{
		var value = 1u;
		var remaining = count;
		while (remaining-- != 0)
		{
			value = (value << 3) ^ (value + 1);
			value = (value << 3) ^ (value + 2);
			value = (value << 3) ^ (value + 3);
			value = (value << 3) ^ (value + 4);
			value = (value << 3) ^ (value + 5);
			value = (value << 3) ^ (value + 6);
			value = (value << 3) ^ (value + 7);
			value = (value << 3) ^ (value + 8);
			value = (value << 3) ^ (value + 9);
			value = (value << 3) ^ (value + 10);
			value = (value << 3) ^ (value + 11);
			value = (value << 3) ^ (value + 12);
			value = (value << 3) ^ (value + 13);
			value = (value << 3) ^ (value + 14);
			value = (value << 3) ^ (value + 15);
			value = (value << 3) ^ (value + 16);
			value = (value << 3) ^ (value + 17);
			value = (value << 3) ^ (value + 18);
			value = (value << 3) ^ (value + 19);
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShiftAndCompare()
	{
		var value = 3 << 5;
		value = (int)((uint)value >> 2);
		return value > 20 && value < 30 ? value : -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstantUnsignedShiftEntry()
	{
		var value = AddThree(40);
		return (int)((uint)value >> 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstantUnsignedShiftNineEntry()
	{
		var value = AddThree(1024);
		return (int)((uint)value >> 9);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int QuickArithmeticEntry() => QuickArithmetic(40);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoundaryQuickConstantEntry() => 135;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int QuickArithmetic(int value) => SubtractTwo(AddThree(value));

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AddThree(int value) => value + 3;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SubtractTwo(int value) => value - 2;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int TailCallEntry() => TailForwarder(39);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RecursiveCalleeSaveEntry() => RecursiveCount(6) + 36;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int OverflowTailCallEntry() => OverflowTailTarget(10, 20, 12);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int OverflowTailTarget(int first, int second, int third) =>
		first + second + third;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int RecursiveCount(int value) =>
		value == 0 ? 0 : 1 + RecursiveCount(value - 1);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int TailForwarder(int value) => TailTarget(value, 3);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int TailTarget(int value, int add) => value + add;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallImport()
	{
		return ImportedValue() + 8;
	}

	[M68kImport("fixture.value")]
	public static extern int ImportedValue();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallRegisterImport() => ImportedAdd(17, 25);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RegisterPromotedLoopCounterAcrossRegisterCall()
	{
		var sum = 0;
		for (var value = 3; value >= 1; value--)
		{
			sum += ImportedAdd(value, 0);
		}

		return sum;
	}

	[M68kImport("fixture.registerAdd")]
	[return: M68kRegister(M68kRegister.D2)]
	public static extern int ImportedAdd(
		[M68kRegister(M68kRegister.D0)] int left,
		[M68kRegister(M68kRegister.D1)] int right);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallBoopsiDoMethod() =>
		BOOPSI.DoMethod(0x0000_1234, 0x8042_3BA6, 7, 9);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallBoopsiDoMethodStackVarargs()
	{
		var method = 0x8042_C9CBu;
		var attribute = 0x8042_E86Eu;
		var everyTime = 0x4987_9DB1u;
		var target = 0x0000_5678u;
		var argCount = 2u;
		var returnId = 0x8042_76EFu;
		var quit = 0xffff_ffffu;
		return BOOPSI.DoMethod(
			0x0000_1234,
			method,
			attribute,
			everyTime,
			target,
			argCount,
			returnId,
			quit);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallMuiNewObjectStackTags()
	{
		var title = CString.FromLiteral("Fixture Window");
		return MUIMaster.MUI_NewObject(
			CString.FromLiteral(global::Amiga.MUI.Window.Name),
			global::Amiga.MUI.Window.Title, title,
			global::Amiga.MUI.Tag.Done);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallMuiMakeObjectStackParameters()
	{
		var label = CString.FromLiteral("Fixture Button");
		return MUIMaster.MUI_MakeObject(global::Amiga.MUI.MakeObject.Button, label);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallIntuitionNewObjectStackTags()
	{
		var title = CString.FromLiteral("Fixture Custom Object");
		var classPtr = 0x0000_1111u;
		var classId = 0x0000_2222u;
		return global::Amiga.Intuition.NewObject(
			classPtr,
			classId,
			global::Amiga.MUI.Window.Title, title,
			global::Amiga.MUI.Tag.Done);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallDosPrintfStackArguments()
	{
		global::Amiga.DOS.DOSLibraryBase = 0x0000_3C00;
		var value = 10u;
		return global::Amiga.DOS.Printf("value: %ld %s\n", value, "items");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallDosPutStrImplicitLiteral()
	{
		global::Amiga.DOS.DOSLibraryBase = 0x0000_3C00;
		return global::Amiga.DOS.PutStr("implicit CString\n");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReadDosLibraryBaseAfterSet()
	{
		global::Amiga.DOS.DOSLibraryBase = 0x0000_3C00;
		return global::Amiga.DOS.DOSLibraryBase.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SetDosLibraryBaseFromNullableValue()
	{
		APTR? library = APTR.FromPointer(0x0000_3C00);
		global::Amiga.DOS.DOSLibraryBase = library.Value;
		return global::Amiga.DOS.DOSLibraryBase.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReadGraphicsLibraryBaseAfterSet()
	{
		global::Amiga.Graphics.GraphicsLibraryBase = 0x0000_3E00;
		return global::Amiga.Graphics.GraphicsLibraryBase.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReadIffParseLibraryBaseAfterSet()
	{
		global::Amiga.IffParse.IffParseLibraryBase = 0x0000_4000;
		return global::Amiga.IffParse.IffParseLibraryBase.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ClearDosLibraryBaseWithNull()
	{
		global::Amiga.DOS.DOSLibraryBase = APTR.Null;
		return global::Amiga.DOS.DOSLibraryBase.IsNull ? 42u : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ClearDosLibraryBaseBeforeVectorCall()
	{
		global::Amiga.DOS.DOSLibraryBase = APTR.Null;
		return global::Amiga.DOS.PutStr("terminal base read\n");
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NullableAptrNullEntry()
	{
		APTR? pointer = null;
		return pointer.HasValue ? 0u : 42u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NullableAptrValueEntry()
	{
		APTR? pointer = APTR.FromPointer(0x0000_4400);
		return pointer.HasValue ? pointer.Value.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint StrPtrValueEntry()
	{
		var pointer = global::Amiga.STRPTR.FromPointer(0x0000_4500);
		return pointer.IsNotNull ? pointer.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConstStrPtrValueEntry()
	{
		var pointer = global::Amiga.CONST_STRPTR.FromAddress(APTR.FromPointer(0x0000_4600));
		return pointer.IsNotNull ? pointer.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConstStrPtrFromStrPtrEntry()
	{
		global::Amiga.STRPTR mutable = global::Amiga.STRPTR.FromPointer(0x0000_4700);
		global::Amiga.CONST_STRPTR constant = mutable;
		return constant.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AmigaStartupArgsEntry(int argLength, global::Amiga.CONST_STRPTR argText)
	{
		return argLength + (int)argText.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PromotedAptrLocalAcrossExecCall()
	{
		var pointer = APTR.FromPointer(0x0000_4400);
		var library = global::Amiga.Exec.OpenLibrary(0x0000_1800, 37);
		return library.HasValue ? pointer.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PromotedAptrLocalAvoidsCachedPlatformBaseRegister()
	{
		var pointer = APTR.FromPointer(0x0000_4400);
		var value = CachedPlatformBaseCall();
		return value == 7 ? pointer.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint CachedPlatformBaseCall();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PromotedAptrLocalsCanUseA6()
	{
		var pointer0 = APTR.FromPointer(0x0000_0100);
		var pointer1 = APTR.FromPointer(0x0000_0200);
		var pointer2 = APTR.FromPointer(0x0000_0300);
		var pointer3 = APTR.FromPointer(0x0000_0400);
		var pointer4 = APTR.FromPointer(0x0000_0500);
		return pointer0.Raw + pointer1.Raw + pointer2.Raw + pointer3.Raw + pointer4.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint A6PromotionBetweenExecCallsReloadsBase()
	{
		var first = global::Amiga.Exec.OpenLibrary(0x0000_1800, 37);
		var pointer0 = APTR.FromPointer(0x0000_0100);
		var pointer1 = APTR.FromPointer(0x0000_0200);
		var pointer2 = APTR.FromPointer(0x0000_0300);
		var pointer3 = APTR.FromPointer(0x0000_0400);
		var pointer4 = APTR.FromPointer(0x0000_0500);
		var raw = pointer0.Raw + pointer1.Raw + pointer2.Raw + pointer3.Raw + pointer4.Raw;
		var second = global::Amiga.Exec.OpenLibrary(0x0000_1900, 37);
		return first.HasValue && second.HasValue ? raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NullableUIntValueEntry()
	{
		uint? value = 37u;
		return value.HasValue ? value.Value : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NullableIntNullEntry()
	{
		int? value = null;
		return !value.HasValue && value.GetValueOrDefault() == 0 ? 42u : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NullableUIntDefaultEntry()
	{
		uint? missing = null;
		uint? present = 13u;
		return missing.GetValueOrDefault(29u) + present.GetValueOrDefault(100u);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallExecLibrary()
	{
		var first = ExecVectors.Add(10, 11);
		var second = ExecVectors.Add(20, 21);
		return first + second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallExecLibraryAfterMergedPaths()
	{
		var first = _counter == 0
			? ExecVectors.Add(1, 2)
			: ExecVectors.Add(3, 4);
		var second = ExecVectors.Add(5, 6);
		return first + second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallManualLibrary() => DosVectors.Add(17, 25);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallProvidedLibrary() => GraphicsVectors.Add(19, 23);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallInvalidLibrarySignature() => InvalidVectors.MissingRegister(42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallInvalidLibraryLvo() => InvalidVectors.InvalidLvo();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkOpenLibrary()
	{
		var library = global::Amiga.Exec.OpenLibrary(0x0000_1800, 37);
		return library.HasValue ? library.Value.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkDosOpen()
	{
		var file = global::Amiga.DOS.Open(
			0x0000_1900,
			global::Amiga.DOS.FileMode.OldFile);
		return file.HasValue ? file.Value.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint DecodeBptrAddress()
	{
		BPTR pointer = BPTR.FromRaw(0x0000_0042);
		return pointer.Address.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long CallSdkDosSeek64()
	{
		BPTR file = BPTR.FromRaw(0x0000_0042);
		return global::Amiga.DOS.Seek64(file, 0x1122_3344_5566_7788L, -1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkDosLockRecord64()
	{
		BPTR file = BPTR.FromRaw(0x0000_0042);
		return (uint)global::Amiga.DOS.LockRecord64(
			file,
			0x1122_3344_5566_7788UL,
			0x99AA_BBCC_DDEE_F001UL,
			3,
			4);
	}

	[AmigaLibrary("exec.library", AmigaLibraryBasePolicy.ExecBase)]
	public static class ExecVectors
	{
		[AmigaLvo(-30)]
		[return: M68kRegister(M68kRegister.D0)]
		public static extern int Add(
			[M68kRegister(M68kRegister.D0)] int left,
			[M68kRegister(M68kRegister.D1)] int right);
	}

	[AmigaLibrary("dos.library", AmigaLibraryBasePolicy.Manual)]
	public static class DosVectors
	{
		[AmigaLvo(-42)]
		[return: M68kRegister(M68kRegister.D2)]
		public static extern int Add(
			[M68kRegister(M68kRegister.D0)] int left,
			[M68kRegister(M68kRegister.D1)] int right);
	}

	[AmigaLibrary("graphics.library", AmigaLibraryBasePolicy.Provided)]
	public static class GraphicsVectors
	{
		[AmigaLvo(-54)]
		public static extern int Add(
			[M68kRegister(M68kRegister.D0)] int left,
			[M68kRegister(M68kRegister.D1)] int right);
	}

	[AmigaLibrary("invalid.library", AmigaLibraryBasePolicy.Manual)]
	public static class InvalidVectors
	{
		[AmigaLvo(-30)]
		public static extern int MissingRegister(int value);

		[AmigaLvo(-40_000)]
		public static extern int InvalidLvo();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedObjectEntry()
	{
		_counter = 3;
		var box = new ManagedBox();
		box.Value = 7 + _counter;
		return box.Add(4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedFieldEntry()
	{
		_counter = 3;
		var box = new ManagedBox();
		box.Value = 7 + _counter;
		return box.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ZeroManagedStoresEntry()
	{
		var box = new ManagedBox();
		box.Value = 0;
		_zeroStatic = 0;
		return box.Value + _zeroStatic;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReferenceReturnEntry()
	{
		var box = new ManagedBox { Value = 37 };
		return IdentityBox(box).Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructorArgumentsEntry()
	{
		var box = new ConstructedBox(12, 30);
		return box.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int WideConstructorArgumentsEntry() =>
		new WideConstructedBox(10, 20, 12).Value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int HybridMixedArgumentsEntry()
	{
		var first = new ManagedBox { Value = 10 };
		var second = new ManagedBox { Value = 11 };
		var third = new ManagedBox { Value = 15 };
		return HybridMixedArguments(1, first, 2, second, 3, third);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int HybridMixedArguments(
		int firstValue,
		ManagedBox first,
		int secondValue,
		ManagedBox second,
		int thirdValue,
		ManagedBox third) =>
		firstValue + first.Value +
		secondValue + second.Value +
		thirdValue + third.Value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int GcHybridReferenceArgumentsEntry()
	{
		var first = new ManagedBox { Value = 10 };
		var second = new ManagedBox { Value = 11 };
		var third = new ManagedBox { Value = 21 };
		return GcHybridReferenceArguments(first, second, third);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int GcHybridReferenceArguments(
		ManagedBox first,
		ManagedBox second,
		ManagedBox third)
	{
		_ = new ManagedBox();
		return first.Value + second.Value + third.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int HybridInt64ArgumentsEntry()
	{
		ConsumeInt64(0x0000_0001_0000_002A);
		ConsumeInt64AfterScalar(7, 0x0000_0002_0000_002A);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ConsumeInt64(long value)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ConsumeInt64AfterScalar(int prefix, long value)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int HybridManagedPointerArgumentsEntry()
	{
		var first = new byte[] { 10 };
		var second = new byte[] { 11 };
		var third = new byte[] { 15 };
		return ReadHybridPointers(ref first[0], 1, ref second[0], 2, ref third[0], 3);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadHybridPointers(
		ref byte first,
		int firstValue,
		ref byte second,
		int secondValue,
		ref byte third,
		int thirdValue) =>
		first + firstValue + second + secondValue + third + thirdValue;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FrameByrefAcrossCollectionEntry()
	{
		var value = 42;
		ref var byref = ref value;
		M68kRuntime.Collect();
		return byref;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint StaticByrefAcrossCollectionEntry()
	{
		_terminalScalar = 42;
		ref var byref = ref _terminalScalar;
		M68kRuntime.Collect();
		return byref;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArrayInteriorByrefAcrossCollectionEntry()
	{
		var owner = new byte[] { 42 };
		ref var byref = ref owner[0];
		owner = null!;
		M68kRuntime.Collect();
		return byref;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectInteriorByrefAcrossCollectionEntry()
	{
		var owner = new ManagedBox { Value = 42 };
		ref var byref = ref owner.Value;
		owner = null!;
		M68kRuntime.Collect();
		return byref;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxInteriorByrefTemplateEntry()
	{
		var source = new BoxedPair(19, 23);
		object owner = source;
		var copy = (BoxedPair)owner;
		owner = null!;
		M68kRuntime.Collect();
		return copy.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void IgnoreIntReference(ref int value)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void IgnoreObjectAndIntReference(
		ManagedBox owner,
		ref int value)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void IgnoreReadonlyReference(in int value, int replacement)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedByrefStaticEscapeTemplateEntry()
	{
		var owner = new ManagedBox { Value = 42 };
		ref var byref = ref owner.Value;
		IgnoreIntReference(ref byref);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedByrefHeapEscapeTemplateEntry()
	{
		var owner = new ManagedBox { Value = 42 };
		ref var byref = ref owner.Value;
		IgnoreObjectAndIntReference(owner, ref byref);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ReadonlyByrefWriteTemplate(in int value, int replacement) =>
		IgnoreReadonlyReference(in value, replacement);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadonlyByrefWriteTemplateEntry()
	{
		var value = 42;
		ReadonlyByrefWriteTemplate(in value, 0);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IncompatibleByrefTypeTemplate(bool selectFirst)
	{
		var owner = new ManagedBox { Value = 42, OtherValue = 42 };
		ref var byref = ref (selectFirst ? ref owner.Value : ref owner.OtherValue);
		return byref;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IncompatibleByrefTypeTemplateEntry() =>
		IncompatibleByrefTypeTemplate(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReferenceBearingAggregateHomeAcrossCollectionEntry()
	{
		ManagedReferenceAggregate aggregate = default;
		aggregate.Reference = new ManagedBox { Value = 42 };
		aggregate.Scalar = 19;
		M68kRuntime.Collect();
		return aggregate.Reference!.Value + aggregate.Scalar - 19;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int UnknownByrefAcrossCollection(ref int value)
	{
		M68kRuntime.Collect();
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ForwardBorrowedByrefAcrossCollection(ref int value) =>
		UnknownByrefAcrossCollection(ref value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BorrowedFrameByrefAcrossCollectionEntry()
	{
		var value = 42;
		return UnknownByrefAcrossCollection(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BorrowedArrayByrefAcrossCollectionEntry()
	{
		var owner = new int[] { 42 };
		ref var byref = ref owner[0];
		owner = null!;
		return UnknownByrefAcrossCollection(ref byref);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BorrowedObjectByrefAcrossCollectionEntry()
	{
		var owner = new ManagedBox { Value = 42 };
		ref var byref = ref owner.Value;
		owner = null!;
		return ForwardBorrowedByrefAcrossCollection(ref byref);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IncompatibleOwnerByrefMerge(bool selectFirst)
	{
		var first = new ManagedBox { Value = 42 };
		var second = new ManagedBox { Value = 0 };
		ref var byref = ref (selectFirst ? ref first.Value : ref second.Value);
		first = null!;
		second = null!;
		M68kRuntime.Collect();
		return byref;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IncompatibleOwnerByrefMergeEntry() =>
		IncompatibleOwnerByrefMerge(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ref int ReturnBorrowedByref(ref int value) => ref value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BorrowedByrefReturnAcrossCollectionEntry()
	{
		var owner = new ManagedBox { Value = 42 };
		ref var byref = ref owner.Value;
		ref var returned = ref ReturnBorrowedByref(ref byref);
		owner = null!;
		M68kRuntime.Collect();
		return returned;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ref int ReturnUntransportedObjectInterior()
	{
		var owner = new ManagedBox { Value = 42 };
		return ref owner.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedBorrowedByrefReturnEntry() =>
		ReturnUntransportedObjectInterior();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadCompatibleOwnerByrefPhi(bool selectFirst)
	{
		var owner = new int[] { 19, 23 };
		ref var byref = ref (selectFirst ? ref owner[0] : ref owner[1]);
		owner = null!;
		M68kRuntime.Collect();
		return byref + (selectFirst ? 23 : 19);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CompatibleOwnerByrefPhiEntry() =>
		ReadCompatibleOwnerByrefPhi(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CollectAndThrowForByref()
	{
		M68kRuntime.Collect();
		throw null!;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExceptionEdgeByrefAcrossCollectionEntry()
	{
		var owner = new ManagedBox { Value = 42 };
		ref var byref = ref owner.Value;
		owner = null!;
		try
		{
			CollectAndThrowForByref();
		}
		catch (NullReferenceException)
		{
			return byref;
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InheritedObjectLayoutEntry()
	{
		var value = new InheritedLayoutDerived
		{
			BaseValue = 7,
			BaseReference = new ManagedBox { Value = 11 },
			DerivedReference = new ManagedBox { Value = 13 },
			DerivedValue = 11
		};
		return value.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SealedDirectCallEntry() =>
		new SealedDirectClass().GetValue();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExplicitBaseCallEntry() =>
		new DirectBaseCallDerived().GetBaseValue();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int VirtualDispatchEntry()
	{
		VirtualBase value = new SealedVirtualDerived();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int VirtualBaseDispatchEntry()
	{
		VirtualBase value = new VirtualBase();
		return value.GetValue() + 41;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int VirtualArgumentDispatchEntry()
	{
		VirtualMathBase value = new VirtualMathDerived();
		return value.Add(40);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiSlotVirtualDispatchEntry()
	{
		MultiSlotBase value = new MultiSlotDerived();
		return value.First() + value.Second();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AbstractVirtualDispatchEntry()
	{
		AbstractValueSource value = new ConcreteValueSource();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int WideVirtualDispatchEntry()
	{
		WideVirtualBase value = new WideVirtualDerived();
		return value.Sum(10, 20, 12);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullVirtualDispatchEntry()
	{
		VirtualBase? value = null;
		try
		{
			return value!.GetValue();
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceDispatchEntry()
	{
		IValueSource value = new InterfaceValueSource();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceArgumentDispatchEntry()
	{
		IAdder value = new InterfaceAdder();
		return value.Add(40);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceTwoDataArgumentDispatchEntry()
	{
		IAdder value = new InterfaceAdder();
		return value.AddTwo(19, 23);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceLongArgumentDispatchEntry()
	{
		IAdder value = new InterfaceAdder();
		return value.AddLong(0x00000001_00000007L);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultipleInterfaceDispatchEntry()
	{
		var implementation = new MultipleInterfaceSource();
		IFirstValue first = implementation;
		ISecondValue second = implementation;
		return first.GetFirst() + second.GetSecond();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InheritedInterfaceDispatchEntry()
	{
		IDerivedValueSource derived = new DerivedValueSource();
		IBaseValueSource baseValue = derived;
		return baseValue.GetBaseValue() + derived.GetDerivedValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExplicitInterfaceDispatchEntry()
	{
		var implementation = new ExplicitInterfaceSource();
		IExplicitFirst first = implementation;
		IExplicitSecond second = implementation;
		return first.GetValue() + second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InheritedClassInterfaceDispatchEntry()
	{
		IValueSource value = new InheritedInterfaceValueSource();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullInterfaceDispatchEntry()
	{
		IValueSource? value = null;
		try
		{
			return value!.GetValue();
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int WideInterfaceDispatchEntry()
	{
		IWideAdder value = new WideInterfaceAdder();
		return value.Add(10, 20, 12);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedDefaultInterfaceDispatchEntry()
	{
		IDefaultValueSource value = new DefaultValueSource();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullComparisonEntry()
	{
		ManagedBox? box = null;
		var nullScore = box == null ? 20 : 0;
		box = new ManagedBox();
		return nullScore + (box != null ? 22 : 0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReferenceEqualityEntry()
	{
		var left = new ManagedBox();
		var right = left;
		var other = new ManagedBox();
		return (left == right ? 21 : 0) + (left == other ? 0 : 21);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ManagedBox IdentityBox(ManagedBox box) => box;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringLiteralEntry() => "Copper68k".Length;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringCharIndexerEntry()
	{
		var dynamicText = M68kRuntime.AllocateString(2);
		return "A\u03a9"[0] == 'A' &&
			"A\u03a9"[1] == '\u03a9' &&
			dynamicText[0] == '\0' &&
			dynamicText[1] == '\0'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringCharIndexerExceptionEntry()
	{
		var score = 0;
		try
		{
			_ = "A"[-1];
		}
		catch (IndexOutOfRangeException)
		{
			score += 10;
		}
		try
		{
			_ = "A"[1];
		}
		catch (IndexOutOfRangeException)
		{
			score += 10;
		}
		try
		{
			string text = null!;
			_ = text[0];
		}
		catch (NullReferenceException)
		{
			score += 22;
		}
		return score;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringOrdinalEqualityEntry()
	{
		string alias = "Copper";
		string nullText = null!;
		var dynamicFirst = M68kRuntime.AllocateString(2);
		var dynamicEqual = M68kRuntime.AllocateString(2);
		var dynamicDifferentLength = M68kRuntime.AllocateString(3);
		return alias == "Copper" &&
			alias != "Coppex" &&
			string.Equals(dynamicFirst, dynamicEqual) &&
			!string.Equals(dynamicFirst, dynamicDifferentLength) &&
			string.Equals(nullText, null) &&
			!string.Equals(nullText, alias) &&
			nullText != alias
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedStringConcatEntry() =>
		string.Concat(new object(), new object()).Length;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringConcatEntry()
	{
		string nullText = null!;
		var combined = string.Concat("Copper", "Sharp");
		var combinedMatches = combined.Length == 11 &&
			combined[0] == 'C' &&
			combined[5] == 'r' &&
			combined[6] == 'S' &&
			combined[10] == 'p';
		var right = string.Concat(nullText, "R");
		var rightMatches = right == "R";
		var left = string.Concat("L", nullText);
		var leftMatches = left == "L";
		var empty = string.Concat(nullText, nullText);
		return combinedMatches &&
			rightMatches &&
			leftMatches &&
			empty.Length == 0
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringConcatAllocatedEntry()
	{
		var combined = string.Concat("Copper", "Sharp");
		return combined.Length == 11 &&
			combined[0] == 'C' &&
			combined[5] == 'r' &&
			combined[6] == 'S' &&
			combined[10] == 'p'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringConcatNullFastPathsEntry()
	{
		string nullText = null!;
		var right = string.Concat(nullText, "R");
		var left = string.Concat("L", nullText);
		var empty = string.Concat(nullText, nullText);
		return right == "R" &&
			left == "L" &&
			empty.Length == 0
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringConcatSurvivesCollectionEntry()
	{
		var first = M68kRuntime.AllocateString(2);
		var second = M68kRuntime.AllocateString(2);
		var combined = string.Concat(first, second);
		first = null!;
		second = null!;
		M68kRuntime.Collect();
		_ = M68kRuntime.AllocateString(5);
		return combined.Length == 4 &&
			combined[0] == '\0' &&
			combined[1] == '\0' &&
			combined[2] == '\0' &&
			combined[3] == '\0'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringSubstringEntry()
	{
		const string text = "CopperSharp68k";
		if (!object.ReferenceEquals(text.Substring(0), text)) return 1;
		if (!object.ReferenceEquals(text.Substring(0, text.Length), text)) return 2;

		var suffix = text.Substring(6);
		if (suffix.Length != 8 || suffix[0] != 'S' || suffix[7] != 'k') return 3;

		var middle = text.Substring(6, 5);
		if (middle.Length != 5 || middle[0] != 'S' || middle[4] != 'p') return 4;

		if (text.Substring(text.Length).Length != 0) return 5;
		if (text.Substring(3, 0).Length != 0) return 6;

		var surrogatePair = "A\uD83D\uDE00B".Substring(1, 2);
		if (surrogatePair.Length != 2 ||
			surrogatePair[0] != '\uD83D' ||
			surrogatePair[1] != '\uDE00')
		{
			return 7;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringSubstringAllocatedEntry()
	{
		var slice = "Copper".Substring(1, 4);
		return slice.Length == 4 &&
			slice[0] == 'o' &&
			slice[1] == 'p' &&
			slice[2] == 'p' &&
			slice[3] == 'e'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringSubstringExceptionEntry()
	{
		var caught = 0;
		try { _ = "abc".Substring(-1); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".Substring(4); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".Substring(0, -1); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".Substring(2, 2); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".Substring(2, int.MaxValue); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try
		{
			string text = null!;
			_ = text.Substring(0);
		}
		catch (NullReferenceException) { caught++; }
		return caught == 6 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringSubstringSurvivesCollectionEntry()
	{
		var source = string.Concat("AB", "CD");
		var slice = source.Substring(1, 2);
		source = null!;
		M68kRuntime.Collect();
		_ = M68kRuntime.AllocateString(5);
		return slice.Length == 2 &&
			slice[0] == 'B' &&
			slice[1] == 'C'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringCopyToEntry()
	{
		var destination = new char[6];
		destination[0] = 'L';
		destination[5] = 'R';
		"Copper".CopyTo(1, destination, 2, 3);
		"Copper".CopyTo(6, destination, 6, 0);
		return destination[0] == 'L' &&
			destination[1] == '\0' &&
			destination[2] == 'o' &&
			destination[3] == 'p' &&
			destination[4] == 'p' &&
			destination[5] == 'R'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IntegerToStringEntry()
	{
		var zero = 0.ToString();
		var positive = 42.ToString();
		var negative = (-42).ToString();
		var minimum = int.MinValue.ToString();
		var maximum = int.MaxValue.ToString();
		var unsignedMaximum = uint.MaxValue.ToString();
		return zero == "0" &&
			positive == "42" &&
			negative == "-42" &&
			minimum == "-2147483648" &&
			maximum == "2147483647" &&
			unsignedMaximum == "4294967295"
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IntegerToStringBoundaryEntry() =>
		9u.ToString() == "9" &&
		10u.ToString() == "10" &&
		999u.ToString() == "999" &&
		1_000u.ToString() == "1000" &&
		9_999u.ToString() == "9999" &&
		10_000u.ToString() == "10000" &&
		10_001u.ToString() == "10001" &&
		99_999_999u.ToString() == "99999999" &&
		100_000_000u.ToString() == "100000000" &&
		100_000_001u.ToString() == "100000001" &&
		999_999_999u.ToString() == "999999999" &&
		1_000_000_000u.ToString() == "1000000000"
			? 42
			: 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IntegerFormatStringEntry() =>
		42.ToString((string?)null) == "42" &&
		42.ToString("") == "42" &&
		42.ToString("G") == "42" &&
		42.ToString("G0") == "42" &&
		(-42).ToString("D5") == "-00042" &&
		42u.ToString("D8") == "00000042" &&
		42u.ToString("X8") == "0000002A" &&
		0xDEAD_BEEFu.ToString("x") == "deadbeef" &&
		(-1).ToString("X") == "FFFFFFFF"
			? 42
			: 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IntegerFormatStringExceptionEntry()
	{
		var caught = 0;
		try { _ = 42.ToString("Q"); }
		catch (FormatException) { caught++; }
		try { _ = 42u.ToString("D1000000000"); }
		catch (FormatException) { caught++; }
		return caught == 2 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterpolatedIntegerEntry()
	{
		var signed = -42;
		var unsigned = 0x2Au;
		var text = $"signed={signed}; hex={unsigned:X8}";
		return text == "signed=-42; hex=0000002A" ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringCopyToExceptionEntry()
	{
		var caught = 0;
		var buffer = new char[3];
		try { "abc".CopyTo(0, null!, 0, 0); }
		catch (ArgumentNullException) { caught++; }
		try { "abc".CopyTo(-1, buffer, 0, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { "abc".CopyTo(4, buffer, 0, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { "abc".CopyTo(0, buffer, -1, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { "abc".CopyTo(0, buffer, 4, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { "abc".CopyTo(0, buffer, 0, -1); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { "abc".CopyTo(2, buffer, 0, 2); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { "abc".CopyTo(0, buffer, 2, 2); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try
		{
			string text = null!;
			text.CopyTo(0, buffer, 0, 0);
		}
		catch (NullReferenceException) { caught++; }
		return caught == 9 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringCopyToSpanEntry()
	{
		var destination = new char[5];
		"A\u03A9\uD83D\uDE00B".CopyTo(destination);
		return destination[0] == 'A' &&
			destination[1] == '\u03A9' &&
			destination[2] == '\uD83D' &&
			destination[3] == '\uDE00' &&
			destination[4] == 'B'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringCopyToSpanExceptionEntry()
	{
		var caught = 0;
		try { "abc".CopyTo(new char[2]); }
		catch (ArgumentException) { caught++; }
		Span<char> empty = default;
		"".CopyTo(empty);
		try
		{
			string text = null!;
			text.CopyTo(new char[3]);
		}
		catch (NullReferenceException) { caught++; }
		return caught == 2 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringToCharArrayEntry()
	{
		const string text = "A\u03A9\uD83D\uDE00B";
		var full = text.ToCharArray();
		if (full.Length != 5 ||
			full[0] != 'A' ||
			full[1] != '\u03A9' ||
			full[2] != '\uD83D' ||
			full[3] != '\uDE00' ||
			full[4] != 'B')
		{
			return 1;
		}

		var pair = text.ToCharArray(2, 2);
		if (pair.Length != 2 ||
			pair[0] != '\uD83D' ||
			pair[1] != '\uDE00')
		{
			return 2;
		}

		var empty = "".ToCharArray();
		var emptyRange = text.ToCharArray(1, 0);
		if (!object.ReferenceEquals(empty, emptyRange)) return 3;

		var first = "A".ToCharArray();
		var second = "A".ToCharArray();
		if (object.ReferenceEquals(first, second)) return 4;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringToCharArrayAllocatedEntry()
	{
		var chars = "Copper".ToCharArray(1, 4);
		return chars.Length == 4 &&
			chars[0] == 'o' &&
			chars[1] == 'p' &&
			chars[2] == 'p' &&
			chars[3] == 'e'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringToCharArrayExceptionEntry()
	{
		var caught = 0;
		try { _ = "abc".ToCharArray(-1, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".ToCharArray(4, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".ToCharArray(0, -1); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = "abc".ToCharArray(2, 2); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try
		{
			string text = null!;
			_ = text.ToCharArray();
		}
		catch (NullReferenceException) { caught++; }
		return caught == 5 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringToCharArraySurvivesCollectionEntry()
	{
		var source = string.Concat("A\u03A9", "BC");
		var chars = source.ToCharArray(1, 2);
		source = null!;
		M68kRuntime.Collect();
		_ = M68kRuntime.AllocateString(5);
		return chars.Length == 2 &&
			chars[0] == '\u03A9' &&
			chars[1] == 'B'
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringEnumerationEntry()
	{
		const string text = "A\u03A9\uD83D\uDE00B";
		var index = 0;
		var matches = true;
		foreach (var character in text)
		{
			if ((index == 0 && character != 'A') ||
				(index == 1 && character != '\u03A9') ||
				(index == 2 && character != '\uD83D') ||
				(index == 3 && character != '\uDE00') ||
				(index == 4 && character != 'B'))
			{
				matches = false;
			}
			index++;
		}
		return matches && index == 5 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringEnumerationNullEntry()
	{
		try
		{
			string text = null!;
			foreach (var character in text)
			{
				_ = character;
			}
			return 0;
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringOrdinalSearchEntry()
	{
		const string text = "CopperSharp68k";
		if (!text.StartsWith("Copper", StringComparison.Ordinal)) return 1;
		if (!text.StartsWith("", StringComparison.Ordinal)) return 2;
		if (text.StartsWith("copper", StringComparison.Ordinal)) return 3;
		if (!text.EndsWith("68k", StringComparison.Ordinal)) return 4;
		if (!text.EndsWith("", StringComparison.Ordinal)) return 5;
		if (text.EndsWith("68K", StringComparison.Ordinal)) return 6;
		if (!text.Contains("Sharp")) return 7;
		if (!text.Contains("Sharp", StringComparison.Ordinal)) return 8;
		if (!text.Contains("", StringComparison.Ordinal)) return 9;
		if (text.Contains("Amiga", StringComparison.Ordinal)) return 10;
		if (text.IndexOf("Copper", StringComparison.Ordinal) != 0) return 11;
		if (text.IndexOf("Sharp", StringComparison.Ordinal) != 6) return 12;
		if (text.IndexOf("", StringComparison.Ordinal) != 0) return 13;
		if (text.IndexOf("Amiga", StringComparison.Ordinal) != -1) return 14;
		if (text.IndexOf("CopperSharp68k!", StringComparison.Ordinal) != -1) return 15;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringOrdinalSearchNullEntry()
	{
		var caught = 0;
		try
		{
			_ = "x".StartsWith(null!, StringComparison.Ordinal);
		}
		catch (ArgumentNullException)
		{
			caught++;
		}
		try
		{
			_ = "x".EndsWith(null!, StringComparison.Ordinal);
		}
		catch (ArgumentNullException)
		{
			caught++;
		}
		try
		{
			_ = "x".Contains(null!);
		}
		catch (ArgumentNullException)
		{
			caught++;
		}
		try
		{
			_ = "x".IndexOf(null!, StringComparison.Ordinal);
		}
		catch (ArgumentNullException)
		{
			caught++;
		}
		return caught == 4 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringNonOrdinalComparisonRejectedEntry()
	{
		try
		{
			_ = "Copper".Contains("copper", StringComparison.OrdinalIgnoreCase);
			return 0;
		}
		catch (ArgumentException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedStringConcatRootEntry() =>
		UnsupportedStringConcatMiddle();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int UnsupportedStringConcatMiddle() =>
		UnsupportedStringConcatEntry();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint InitializedArrayEntry()
	{
		var values = new uint[] { 1, 2, 3, 36 };
		return values[0] + values[1] + values[2] + values[3];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShadowMathAbsEntry() => Math.Abs(-42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShadowMathOverflowCatchEntry()
	{
		try
		{
			return Math.Abs(int.MinValue);
		}
		catch (OverflowException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ShadowBitConverterEntry()
	{
		var bytes = BitConverter.GetBytes(0x01020304);
		return (uint)(bytes[0] << 24 |
			bytes[1] << 16 |
			bytes[2] << 8 |
			bytes[3]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CStringLiteralEntry() =>
		global::Amiga.CString.ToUInt32(global::Amiga.CString.FromLiteral("abc"));

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CStringBufferEntry()
	{
		using var buffer = new global::Amiga.CStringBuffer("Amiga");
		var pointer = global::Amiga.CString.ToUInt32(buffer.Value);
		var packed = global::Amiga.APTR.ReadUInt32(
			global::Amiga.APTR.FromPointer(pointer),
			0);
		return packed == 0x416D_6967u && buffer.ByteSize == 8u ? pointer : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CStringStorageEntry()
	{
		using var storage = new global::Amiga.CStringStorage("Retained");
		var pointer = global::Amiga.CString.ToUInt32(storage.Value);
		return storage.ByteSize == 12u ? pointer : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SealedDisposableUsingEntry()
	{
		RuntimeDisposable.DisposeCount = 0;
		using (var disposable = new RuntimeDisposable())
		{
			if (RuntimeDisposable.DisposeCount != 0)
			{
				return 1;
			}
		}
		return RuntimeDisposable.DisposeCount;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceTypedDisposableEntry()
	{
		IDisposable disposable = new RuntimeDisposable();
		disposable.Dispose();
		return RuntimeDisposable.DisposeCount;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListInt32Entry()
	{
		var values = new List<int>();
		values.Add(10);
		values.Add(20);
		values.Add(30);
		values.Add(40);
		values.Add(50);
		if (values.Count != 5 || values[0] != 10 || values[4] != 50)
		{
			return 1;
		}
		values[1] = 32;
		return values[0] + values[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long ListInt64Entry()
	{
		const long first = 0x0000_0001_0000_0002L;
		const long replacement = 0x0000_002A_5566_7788L;
		var values = new List<long>();
		values.Add(first);
		values.Add(0x0000_0005_0000_0006L);
		values.Add(0x0000_0007_0000_0008L);
		values.Add(0x0000_0009_0000_000AL);
		values.Add(0x0000_000B_0000_000CL);
		values[4] = replacement;
		return values[4];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListRangeExceptionEntry()
	{
		var values = new List<int>();
		values.Add(1);
		try
		{
			_ = values[-1];
		}
		catch (ArgumentOutOfRangeException)
		{
			try
			{
				values[values.Count] = 2;
			}
			catch (ArgumentOutOfRangeException)
			{
				return 42;
			}
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListReferenceGcEntry()
	{
		var values = new List<ListReferenceValue>();
		values.Add(new ListReferenceValue(19));
		values.Add(new ListReferenceValue(1));
		values.Add(new ListReferenceValue(2));
		values.Add(new ListReferenceValue(3));
		values.Add(new ListReferenceValue(23));
		M68kRuntime.Collect();
		return values[0].Value + values[4].Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedListStructEntry()
	{
		var values = new List<ListPair>();
		values.Add(new ListPair(19, 23));
		var value = values[0];
		return value.First + value.Second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedListRemoveEntry()
	{
		var values = new List<int>();
		values.Add(1);
		return values.Remove(1) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CStringRejectsEmbeddedNullEntry()
	{
		try
		{
			using var buffer = new global::Amiga.CStringBuffer("bad\0value");
			return buffer.ByteSize == 0 ? 1 : 2;
		}
		catch (ArgumentException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CStringRejectsUnmappableEntry()
	{
		try
		{
			using var buffer = new global::Amiga.CStringBuffer("bad\u0100value");
			return buffer.ByteSize == 0 ? 1 : 2;
		}
		catch (ArgumentException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CStringAllocationFailureEntry()
	{
		try
		{
			using var buffer = new global::Amiga.CStringBuffer("OOM");
			return buffer.ByteSize == 0 ? 1 : 2;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ManagedArrayEntry()
	{
		var values = new int[4];
		values[0] = 3;
		values[1] = 5;
		values[2] = 7;
		values[3] = 11;
		var sum = 0;
		for (var index = 0; index < values.Length; index++)
		{
			sum += values[index];
		}

		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ByteArrayEntry()
	{
		var values = new byte[4];
		values[0] = 3;
		values[1] = 250;
		values[2] = 7;
		values[3] = 11;
		return values[0] + values[1] + values[2] + values[3];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShortArrayEntry()
	{
		var values = new short[3];
		values[0] = 300;
		values[1] = -20;
		values[2] = 7;
		return values[0] + values[1] + values[2];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SignedByteArrayEntry()
	{
		var values = new sbyte[3];
		values[0] = 12;
		values[1] = -5;
		values[2] = 35;
		return values[0] + values[1] + values[2];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsignedShortArrayEntry()
	{
		var values = new ushort[3];
		values[0] = 30;
		values[1] = 65000;
		values[2] = 12;
		return values[0] - values[1] + values[2] + 65000;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowByteArithmeticEntry()
	{
		byte left = 250;
		byte right = 10;
		return (byte)(left + right);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowShortArithmeticEntry()
	{
		short left = 30000;
		short right = 10000;
		return (short)(left + right);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowArithmeticOperationsEntry()
	{
		sbyte signedLeft = -120;
		sbyte signedRight = 10;
		var signedSum = (sbyte)(signedLeft + signedRight);

		ushort unsignedLeft = 65000;
		ushort unsignedRight = 1000;
		var unsignedDifference = (ushort)(unsignedLeft - unsignedRight);

		byte productLeft = 15;
		byte productRight = 17;
		var product = (byte)(productLeft * productRight);

		short shiftValue = -100;
		var shifted = (short)(shiftValue >> 1);
		var negated = (sbyte)-signedLeft;

		return signedSum + unsignedDifference + product + shifted + negated;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowUnsignedSubtractionEntry()
	{
		ushort left = 65000;
		ushort right = 1000;
		return (ushort)(left - right);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowByteMultiplyEntry()
	{
		byte left = 15;
		byte right = 17;
		return (byte)(left * right);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowShortShiftEntry()
	{
		short value = -100;
		return (short)(value >> 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowSignedNegateEntry()
	{
		sbyte value = -120;
		return (sbyte)-value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IndirectMemoryEntry()
	{
		var bytes = new byte[2];
		var words = new short[2];
		WriteByte(ref bytes[0], 0xF1);
		WriteWord(ref words[0], -1234);
		return ReadUnsignedByte(ref bytes[0]) + ReadSignedWord(ref words[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AddressReadConstantEntry()
	{
		var address = M68kAddress.FromUInt32(0x0000_4000);
		return M68kAddress.ReadUInt32(address, 8);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AddressReadNegativeEntry()
	{
		var address = M68kAddress.FromUInt32(0x0000_4000);
		return M68kAddress.ReadUInt32(address, -8);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AddressReadLargeEntry()
	{
		var address = M68kAddress.FromUInt32(0x0000_4000);
		return M68kAddress.ReadUInt32(address, 40_000);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AddressReadDynamicEntry()
	{
		var address = M68kAddress.FromUInt32(0x0000_4000);
		return M68kAddress.ReadUInt32(address, (int)_terminalScalar);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FileInfoBlockFixedFieldsEntry()
	{
		var address = _terminalScalar;
		return FileInfoBlock.GetDirEntryType(address) +
			FileInfoBlock.GetSize(address) +
			FileInfoBlock.GetDateDays(address) +
			FileInfoBlock.GetDateMinute(address) +
			FileInfoBlock.GetDateTick(address);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AddressWriteConstantEntry()
	{
		var address = M68kAddress.FromUInt32(0x0000_4000);
		M68kAddress.WriteUInt32(address, 8, 42);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteByte(ref byte target, int value) => target = (byte)value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteWord(ref short target, int value) => target = (short)value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadUnsignedByte(ref byte target) => target;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadSignedWord(ref short target) => target;

	private static int[]? _disposableArray;
	private static int[]? _secondDisposableArray;
	private static int[]? _keptArray;
	private static ManagedNode? _keptNode;
	private static ManagedChainNode? _keptChain;
	private static ManagedBox?[]? _keptBoxes;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ExplicitDisposeEntry()
	{
		_disposableArray = new int[4];
		_disposableArray[0] = 42;
		M68kRuntime.DisposeInt32Array(ref _disposableArray);
		return _disposableArray is null ? 42u : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolDisposeReuseEntry()
	{
		_disposableArray = new int[4];
		_disposableArray[0] = 13;
		_disposableArray[1] = 99;
		M68kRuntime.DisposeInt32Array(ref _disposableArray);
		var reused = new int[4];
		if (reused[1] != 0)
		{
			return 0;
		}
		reused[0] = 42;
		return reused[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolCollectCoalescesEntry()
	{
		_disposableArray = new int[4];
		_secondDisposableArray = new int[4];
		M68kRuntime.DisposeInt32Array(ref _disposableArray);
		M68kRuntime.DisposeInt32Array(ref _secondDisposableArray);
		M68kRuntime.Collect();
		var merged = new int[12];
		merged[11] = 42;
		return merged[11];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolCollectReclaimsUnrootedEntry()
	{
		_keptArray = new int[4];
		_keptArray[0] = 7;
		AllocateUnrootedArray();
		M68kRuntime.Collect();
		var reclaimed = new int[12];
		reclaimed[11] = 35;
		return _keptArray[0] + reclaimed[11];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolCollectTracesCallerFrameEntry()
	{
		var kept = new int[4];
		kept[0] = 7;
		AllocateUnrootedArray();
		CollectFromCallee();
		var reused = new int[4];
		reused[0] = 35;
		return kept[0] + reused[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CollectFromCallee()
	{
		M68kRuntime.Collect();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NonAllocatingCallWithLiveReferenceEntry() =>
		ConsumeLiveReference(new ManagedBox(), NonAllocatingLeaf());

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int NonAllocatingLeaf() => 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeLiveReference(ManagedBox value, int number) =>
		value is null ? 0 : number + 35;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolAllocationFailureCollectsRootsEntry()
	{
		_keptArray = new int[4];
		_keptArray[0] = 7;
		AllocateUnrootedArray();
		var reclaimed = new int[12];
		reclaimed[11] = 35;
		return _keptArray[0] + reclaimed[11];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolAllocationFailureIgnoresIntegerStackRootsEntry()
	{
		AllocateUnrootedArray();
		return 0x4010 + new int[12].Length - 0x3FF2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PoolTelemetryCountersEntry()
	{
		_keptArray = new int[4];
		_keptArray[0] = 1;
		return M68kRuntime.GetGcStaleBlocks();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PoolTelemetryCountersResetAfterCollectEntry()
	{
		_keptArray = new int[4];
		_keptArray[0] = 1;
		M68kRuntime.Collect();
		return M68kRuntime.GetGcStaleBlocks();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AllocateUnrootedArray()
	{
		var unrooted = new int[4];
		unrooted[0] = 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolCollectTracesObjectFieldsEntry()
	{
		_keptNode = new ManagedNode();
		var child = new ManagedBox { Value = 35 };
		_keptNode.Child = child;
		M68kRuntime.Collect();
		var tail = new int[4];
		tail[0] = 7;
		return _keptNode.Child!.Value + tail[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolCollectTracesReferenceArrayEntry()
	{
		_keptBoxes = new ManagedBox?[1];
		_keptBoxes[0] = new ManagedBox { Value = 35 };
		M68kRuntime.Collect();
		var tail = new int[4];
		tail[0] = 7;
		return _keptBoxes[0]!.Value + tail[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PoolCollectTracesDeepObjectGraphEntry()
	{
		_keptChain = new ManagedChainNode { Value = 1 };
		var second = new ManagedChainNode { Value = 2 };
		var third = new ManagedChainNode { Value = 32 };
		_keptChain.Next = second;
		second.Next = third;
		M68kRuntime.Collect();
		var tail = new int[4];
		tail[0] = 7;
		return _keptChain.Next!.Next!.Value + _keptChain.Next.Value + _keptChain.Value + tail[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ExplicitCollectEntry()
	{
		M68kRuntime.Collect();
		return 42;
	}

	[M68kExport("fixture.add")]
	[return: M68kRegister(M68kRegister.D0)]
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExportedAdd(
		[M68kRegister(M68kRegister.D0)] int left,
		[M68kRegister(M68kRegister.D1)] int right,
		[M68kRegister(M68kRegister.D2)] int ignored) =>
		left + right;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ExportAddressEntry() =>
		APTR.ToUInt32(APTR.ExportAddress("fixture.add"));

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static T SharedIdentity<T>(T value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SharedGenericEntry() =>
		SharedIdentity(39) + SharedIdentity("abc").Length;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FrameworkGenericSpecializationEntry() =>
		!RuntimeHelpers.IsReferenceOrContainsReferences<int>() &&
		RuntimeHelpers.IsReferenceOrContainsReferences<string>() &&
		!RuntimeHelpers.IsReferenceOrContainsReferences<BoxedPair>() &&
		RuntimeHelpers.IsReferenceOrContainsReferences<ManagedReferenceAggregate>()
			? 42
			: 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float UnsupportedFloat() => 1.25f;

	public static float NativeFloatAdd() => AddFloat(1.25f, 2.5f);

	private static float AddFloat(float left, float right) => left + right;

	public static double NativeDoubleMultiply() => MultiplyDouble(1.5d, 4.0d);

	private static double MultiplyDouble(double left, double right) => left * right;

	public sealed class ManagedBox
	{
		public int Value;
		public int OtherValue;
		public uint ByrefEscapeSink;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Add(int value) => Value + value;
	}

	public struct ManagedReferenceAggregate
	{
		public ManagedBox? Reference;
		public int Scalar;
	}

	public sealed class ConstructedBox
	{
		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public ConstructedBox(int left, int right)
		{
			Value = left + right;
		}
	}

	public sealed class WideConstructedBox
	{
		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public WideConstructedBox(int first, int second, int third)
		{
			Value = first + second + third;
		}
	}

	public sealed class ManagedNode
	{
		public ManagedBox? Child;
	}

	public sealed class ManagedChainNode
	{
		public ManagedChainNode? Next;
		public int Value;
	}

	public class InheritedLayoutBase
	{
		public int BaseValue;
		public ManagedBox? BaseReference;
	}

	public sealed class InheritedLayoutDerived : InheritedLayoutBase
	{
		public ManagedBox? DerivedReference;
		public int DerivedValue;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Sum() =>
			BaseValue + BaseReference!.Value + DerivedReference!.Value + DerivedValue;
	}

	public class VirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int GetValue() => 1;
	}

	public sealed class SealedVirtualDerived : VirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public sealed override int GetValue() => 42;
	}

	public sealed class SealedDirectClass
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => 42;
	}

	public sealed class DirectBaseCallDerived : VirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 2;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetBaseValue() => base.GetValue() + 41;
	}

	public class VirtualMathBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int Add(int value) => value + 1;
	}

	public sealed class VirtualMathDerived : VirtualMathBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int Add(int value) => value + 2;
	}

	public class MultiSlotBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int First() => 20;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int Second() => 1;
	}

	public sealed class MultiSlotDerived : MultiSlotBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int Second() => 22;
	}

	public abstract class AbstractValueSource
	{
		public abstract int GetValue();
	}

	public sealed class ConcreteValueSource : AbstractValueSource
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 42;
	}

	public class WideVirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int Sum(int first, int second, int third) =>
			first + second + third;
	}

	public sealed class WideVirtualDerived : WideVirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int Sum(int first, int second, int third) =>
			first + second + third;
	}

	public interface IValueSource
	{
		int GetValue();
	}

	public sealed class InterfaceValueSource : IValueSource
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => 42;
	}

	public interface IAdder
	{
		int Add(int value);

		int AddTwo(int first, int second);

		int AddLong(long value);
	}

	public sealed class InterfaceAdder : IAdder
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Add(int value) => value + 2;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int AddTwo(int first, int second) => first + second;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int AddLong(long value) => (int)value + 35;
	}

	public interface IFirstValue
	{
		int GetFirst();
	}

	public interface ISecondValue
	{
		int GetSecond();
	}

	public sealed class MultipleInterfaceSource : IFirstValue, ISecondValue
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetFirst() => 20;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetSecond() => 22;
	}

	public interface IBaseValueSource
	{
		int GetBaseValue();
	}

	public interface IDerivedValueSource : IBaseValueSource
	{
		int GetDerivedValue();
	}

	public sealed class DerivedValueSource : IDerivedValueSource
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetBaseValue() => 19;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetDerivedValue() => 23;
	}

	public interface IExplicitFirst
	{
		int GetValue();
	}

	public interface IExplicitSecond
	{
		int GetValue();
	}

	public sealed class ExplicitInterfaceSource : IExplicitFirst, IExplicitSecond
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		int IExplicitFirst.GetValue() => 20;

		[MethodImpl(MethodImplOptions.NoInlining)]
		int IExplicitSecond.GetValue() => 22;
	}

	public class InterfaceValueSourceBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => 42;
	}

	public sealed class InheritedInterfaceValueSource :
		InterfaceValueSourceBase,
		IValueSource
	{
	}

	public interface IWideAdder
	{
		int Add(int first, int second, int third);
	}

	public sealed class WideInterfaceAdder : IWideAdder
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Add(int first, int second, int third) =>
			first + second + third;
	}

	public interface IDefaultValueSource
	{
		int GetValue() => 42;
	}

	public sealed class DefaultValueSource : IDefaultValueSource
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StoreInternalRegisterCallResult()
	{
		var value = InternalRegisterAdd(17, 25);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int InternalRegisterAdd(int left, int right) => left + right;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RuntimeClassTypeTestEntry()
	{
		object value = CreateRuntimeTypeTestObject(0);
		return value is VirtualBase &&
			value is SealedVirtualDerived &&
			value is not InterfaceValueSource
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RuntimeInterfaceTypeTestEntry()
	{
		object value = CreateRuntimeTypeTestObject(1);
		return value is IValueSource ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RuntimeArrayTypeTestEntry()
	{
		object value = new int[1];
		return value is int[] && value is not uint[] ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RuntimeCastClassEntry()
	{
		object value = CreateRuntimeTypeTestObject(0);
		var valid = (VirtualBase)value;
		try
		{
			_ = (InterfaceValueSource)value;
			return 0;
		}
		catch (InvalidCastException)
		{
			return valid.GetValue() == 42 ? 42 : 0;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReferenceArrayStoreTypeCheckEntry()
	{
		VirtualBase[] values = new SealedVirtualDerived[2];
		values[0] = new SealedVirtualDerived();
		try
		{
			values[1] = new DirectBaseCallDerived();
			return 0;
		}
		catch (ArrayTypeMismatchException)
		{
			return values[0].GetValue() == 42 ? 42 : 0;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericTypeIdentityEntry()
	{
		object first = new RuntimeGenericBox<int>();
		object second = new RuntimeGenericBox<uint>();
		return first is RuntimeGenericBox<int> &&
			first is not RuntimeGenericBox<uint> &&
			second is RuntimeGenericBox<uint> &&
			second is not RuntimeGenericBox<int>
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericInstanceFieldEntry()
	{
		var first = new RuntimeGenericBox<int> { Value = 19 };
		var second = new RuntimeGenericBox<uint> { Value = 23 };
		return first.Value + second.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericDependentFieldTemplateEntry()
	{
		var scalar = new RuntimeDependentGenericBox<int> { Value = 19 };
		var reference = new RuntimeDependentGenericBox<ManagedBox>
		{
			Value = new ManagedBox { Value = 23 }
		};
		M68kRuntime.Collect();
		return scalar.Value + reference.Value.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericStaticFieldEntry()
	{
		RuntimeGenericStatics<int>.Value = 7;
		RuntimeGenericStatics<uint>.Value = 11;
		RuntimeGenericStatics<ManagedBox>.Value = new ManagedBox { Value = 24 };
		M68kRuntime.Collect();
		return RuntimeGenericStatics<int>.Value +
			(int)RuntimeGenericStatics<uint>.Value +
			RuntimeGenericStatics<ManagedBox>.Value!.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericStaticInitializerTemplateEntry() =>
		RuntimeInitializedGenericStatics<int>.Value +
		RuntimeInitializedGenericStatics<uint>.Value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericMethodSpecializationEntry()
	{
		RuntimeGenericStatics<int>.Value = 19;
		RuntimeGenericStatics<uint>.Value = 23;
		return ReadGenericStatic<int>() + (int)ReadGenericStatic<uint>();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static T? ReadGenericStatic<T>() => RuntimeGenericStatics<T>.Value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericCompoundFieldEntry()
	{
		var scalar = new RuntimeCompoundGenericBox<int>
		{
			Values = new[] { 9 },
			Nested = new RuntimeDependentGenericBox<int> { Value = 10 }
		};
		var reference = new RuntimeCompoundGenericBox<ManagedBox>
		{
			Values = new[] { new ManagedBox { Value = 11 } },
			Nested = new RuntimeDependentGenericBox<ManagedBox>
			{
				Value = new ManagedBox { Value = 12 }
			}
		};
		M68kRuntime.Collect();
		return scalar.Values![0] + scalar.Nested!.Value +
			reference.Values![0].Value + reference.Nested!.Value.Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedOwnerGenericMethodEntry()
	{
		RuntimeGenericStatics<int>.Value = 19;
		RuntimeGenericStatics<uint>.Value = 23;
		return RuntimeGenericMethods<int>.OwnerValue<uint>(0) +
			(int)RuntimeGenericMethods<uint>.OwnerValue<int>(0);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericInterfaceDispatchEntry()
	{
		IRuntimeGenericSource<int> first = new RuntimeIntGenericSource();
		IRuntimeGenericSource<uint> second = new RuntimeUIntGenericSource();
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericImplementerDispatchEntry()
	{
		IRuntimeGenericSource<int> first = new RuntimeGenericSource<int>(19);
		IRuntimeGenericSource<uint> second = new RuntimeGenericSource<uint>(23);
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ExplicitConstructedGenericInterfaceDispatchEntry()
	{
		IRuntimeGenericSource<int> first =
			new RuntimeExplicitGenericSource<int>(19);
		IRuntimeGenericSource<uint> second =
			new RuntimeExplicitGenericSource<uint>(23);
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InheritedConstructedGenericInterfaceDispatchEntry()
	{
		IRuntimeGenericSource<int> first =
			new RuntimeInheritedGenericSource<int>(19);
		IRuntimeGenericSource<uint> second =
			new RuntimeInheritedGenericSource<uint>(23);
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericInterfaceInheritanceEntry()
	{
		IRuntimeGenericSource<int> first =
			new RuntimeGenericChildSource<int>(19);
		IRuntimeGenericSource<uint> second =
			new RuntimeGenericChildSource<uint>(23);
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CovariantGenericInterfaceDispatchEntry()
	{
		IRuntimeCovariantSource<RuntimeVariantDerived> exact =
			new RuntimeVariantSource<RuntimeVariantDerived>(
				new RuntimeVariantDerived { Value = 42 });
		IRuntimeCovariantSource<RuntimeVariantBase> converted = exact;
		return converted.GetValue().Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CovariantGenericInterfaceCastEntry()
	{
		object value = new RuntimeVariantSource<RuntimeVariantDerived>(
			new RuntimeVariantDerived { Value = 42 });
		var converted = (IRuntimeCovariantSource<RuntimeVariantBase>)value;
		return converted.GetValue().Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ContravariantGenericInterfaceDispatchEntry()
	{
		IRuntimeContravariantSink<RuntimeVariantBase> exact =
			new RuntimeVariantSink();
		IRuntimeContravariantSink<RuntimeVariantDerived> converted = exact;
		return converted.Accept(new RuntimeVariantDerived { Value = 42 });
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MixedVarianceGenericInterfaceDispatchEntry()
	{
		IRuntimeVariantMap<RuntimeVariantBase, RuntimeVariantDerived> exact =
			new RuntimeVariantMap();
		IRuntimeVariantMap<RuntimeVariantDerived, RuntimeVariantBase> converted =
			exact;
		return converted.Map(new RuntimeVariantDerived { Value = 19 }).Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InheritedCovariantGenericInterfaceDispatchEntry()
	{
		IRuntimeCovariantChildSource<RuntimeVariantDerived> exact =
			new RuntimeVariantChildSource<RuntimeVariantDerived>(
				new RuntimeVariantDerived { Value = 42 });
		IRuntimeCovariantSource<RuntimeVariantBase> converted = exact;
		return converted.GetValue().Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InvalidCovariantDirectionTypeTestEntry()
	{
		object value = new RuntimeVariantSource<RuntimeVariantBase>(
			new RuntimeVariantBase { Value = 19 });
		return value is IRuntimeCovariantSource<RuntimeVariantDerived> ? 0 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ValueTypeVarianceRemainsInvariantEntry()
	{
		object value = new RuntimeVariantSource<int>(19);
		return value is IRuntimeCovariantSource<object> ? 0 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericVirtualDispatchEntry()
	{
		RuntimeGenericVirtualSource<int> first = new RuntimeIntVirtualSource();
		RuntimeGenericVirtualSource<uint> second = new RuntimeUIntVirtualSource();
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericVirtualOverrideEntry()
	{
		RuntimeGenericVirtualSource<int> first =
			new RuntimeGenericVirtualDerived<int>(19);
		RuntimeGenericVirtualSource<uint> second =
			new RuntimeGenericVirtualDerived<uint>(23);
		return first.GetValue() + (int)second.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiHopPermutedGenericVirtualOverrideEntry()
	{
		RuntimeMultiHopVirtualBase<int> value =
			new RuntimeMultiHopVirtualLeaf<int, uint>();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ClosedMultiHopGenericVirtualOverrideEntry()
	{
		RuntimeMultiHopVirtualBase<int> value =
			new RuntimeClosedMultiHopVirtualLeaf();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiHopPermutedGenericInterfaceEntry()
	{
		IRuntimePermutedPair<int, uint> value =
			new RuntimePermutedInterfaceLeaf<int, uint>();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedGenericBaseLayoutEntry()
	{
		var value = new RuntimeGenericLayoutDerived<ManagedBox>
		{
			BaseValue = new ManagedBox { Value = 19 },
			DerivedValue = 23
		};
		M68kRuntime.Collect();
		return value.BaseValue!.Value + value.DerivedValue;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericValueTypeDispatchEntry()
	{
		return ReadConstrained(ref _runtimeConstrainedSource);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StatefulConstrainedGenericValueTypeDispatchEntry()
	{
		_runtimeStatefulConstrainedSource.Value = 42;
		return ReadConstrained(ref _runtimeStatefulConstrainedSource);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericReferenceInterfaceDispatchEntry()
	{
		RuntimeConstrainedReferenceBase source =
			new RuntimeConstrainedReferenceDerived();
		return ReadConstrainedReference(ref source);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericReferenceVirtualDispatchEntry()
	{
		RuntimeConstrainedVirtualBase source =
			new RuntimeConstrainedVirtualDerived();
		return ReadConstrainedVirtual(ref source);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericObjectVirtualDispatchEntry()
	{
		var source = new RuntimeConstrainedObjectSource();
		return ReadConstrainedObjectVirtual(ref source);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericObjectVirtualFallbackEntry()
	{
		var source = new RuntimeObjectHashFallbackSource();
		return ReadConstrainedObjectVirtual(ref source) == 0 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectGetHashCodeFallbackEntry()
	{
		object source = new RuntimeObjectHashFallbackSource();
		return source.GetHashCode() == 0 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectGetHashCodeOverrideEntry()
	{
		object source = new RuntimeObjectHashDerived();
		return source.GetHashCode();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectGetHashCodeBaseTypedOverrideEntry()
	{
		RuntimeObjectHashBase source = new RuntimeObjectHashDerived();
		return source.GetHashCode();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectEqualsFallbackEntry()
	{
		object first = new RuntimeObjectEqualsFallbackSource();
		object same = first;
		object different = new RuntimeObjectEqualsFallbackSource();
		return first.Equals(same) &&
			!first.Equals(different) &&
			!first.Equals(null)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectEqualsOverrideEntry()
	{
		object first = new RuntimeObjectEqualsDerived { Value = 7 };
		object sameValue = new RuntimeObjectEqualsDerived { Value = 7 };
		object differentValue = new RuntimeObjectEqualsDerived { Value = 8 };
		return first.Equals(sameValue) &&
			!first.Equals(differentValue) &&
			!first.Equals(null)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectEqualsBaseTypedOverrideEntry()
	{
		RuntimeObjectEqualsBase first =
			new RuntimeObjectEqualsDerived { Value = 7 };
		object sameValue = new RuntimeObjectEqualsDerived { Value = 7 };
		return first.Equals(sameValue) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StaticObjectEqualsEntry()
	{
		object first = new RuntimeObjectEqualsFallbackSource();
		object same = first;
		object different = new RuntimeObjectEqualsFallbackSource();
		object overrideFirst = new RuntimeObjectEqualsDerived { Value = 7 };
		object overrideSame = new RuntimeObjectEqualsDerived { Value = 7 };
		return object.Equals(first, same) &&
			!object.Equals(first, different) &&
			!object.Equals(first, null) &&
			!object.Equals(null, first) &&
			object.Equals(null, null) &&
			object.Equals(overrideFirst, overrideSame)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StaticObjectEqualsDelegateEntry()
	{
		object first = new Func<int, int>(StaticDelegateTarget);
		object logicallyEqual = new Func<int, int>(StaticDelegateTarget);
		object different = new Func<int, int>(StaticDelegateDoubleTarget);
		return object.Equals(first, logicallyEqual) &&
			!object.Equals(first, different) &&
			!object.Equals(first, null) &&
			!object.Equals(null, first)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ObjectReferenceEqualsEntry()
	{
		object first = new RuntimeObjectEqualsFallbackSource();
		object alias = first;
		object different = new RuntimeObjectEqualsFallbackSource();
		return object.ReferenceEquals(first, alias) &&
			!object.ReferenceEquals(first, different) &&
			!object.ReferenceEquals(first, null) &&
			object.ReferenceEquals(null, null)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DelegateReferenceEqualsEntry()
	{
		var first = new Func<int, int>(StaticDelegateTarget);
		var alias = first;
		var logicallyEqual = new Func<int, int>(StaticDelegateTarget);
		return object.ReferenceEquals(first, alias) &&
			!object.ReferenceEquals(first, logicallyEqual)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericObjectEqualsFallbackEntry()
	{
		var first = new RuntimeObjectEqualsFallbackSource();
		object same = first;
		object different = new RuntimeObjectEqualsFallbackSource();
		return EqualsConstrainedObjectVirtual(ref first, same) &&
			!EqualsConstrainedObjectVirtual(ref first, different)
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericObjectEqualsOverrideEntry()
	{
		var first = new RuntimeObjectEqualsDerived { Value = 7 };
		object sameValue = new RuntimeObjectEqualsDerived { Value = 7 };
		return EqualsConstrainedObjectVirtual(ref first, sameValue) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullConstrainedGenericObjectEqualsEntry()
	{
		RuntimeObjectEqualsFallbackSource first = null!;
		try
		{
			return EqualsConstrainedObjectVirtual(ref first, null!) ? 1 : 0;
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullConstrainedGenericObjectVirtualDispatchEntry()
	{
		RuntimeObjectHashFallbackSource source = null!;
		try
		{
			return ReadConstrainedObjectVirtual(ref source);
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullConstrainedGenericReferenceDispatchEntry()
	{
		RuntimeConstrainedReferenceBase source = null!;
		try
		{
			return ReadConstrainedReference(ref source);
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanArrayLengthAndIndexerEntry()
	{
		var values = new int[2];
		values[0] = 19;
		values[1] = 23;
		Span<int> span = values;
		return span.Length + span[0] + span[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanArrayOwnerSurvivesCollectionEntry()
	{
		var values = new int[2];
		values[0] = 19;
		values[1] = 23;
		Span<int> span = values;
		values = null!;
		M68kRuntime.Collect();
		var replacement = new int[2];
		replacement[0] = 100;
		replacement[1] = 200;
		return span.Length + span[0] + span[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanFromFrameRefAcrossCollectionEntry()
	{
		var value = 40;
		Span<int> span = new(ref value);
		M68kRuntime.Collect();
		span[0]++;
		return span.Length + span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanFromStaticRefAcrossCollectionEntry()
	{
		_zeroStatic = 40;
		Span<int> span = new(ref _zeroStatic);
		M68kRuntime.Collect();
		span[0]++;
		return span.Length + span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanFromArrayRefAcrossCollectionEntry()
	{
		var values = new int[2];
		values[1] = 41;
		Span<int> span = new(ref values[1]);
		values = null!;
		M68kRuntime.Collect();
		var replacement = new int[2];
		replacement[1] = 100;
		return span.Length + span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanFromObjectRefAcrossCollectionEntry()
	{
		var value = new ManagedBox { Value = 40 };
		Span<int> span = new(ref value.Value);
		value = null!;
		M68kRuntime.Collect();
		var replacement = new ManagedBox { Value = 100 };
		span[0]++;
		return span.Length + span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanFromArrayRefAcrossCollectionEntry()
	{
		var values = new int[1];
		values[0] = 41;
		ReadOnlySpan<int> span = new(in values[0]);
		values = null!;
		M68kRuntime.Collect();
		var replacement = new int[1];
		replacement[0] = 100;
		return span.Length + span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedSpanFromBorrowedRefEntry()
	{
		var value = 41;
		return ConsumeBorrowedSpan(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeBorrowedSpan(ref int value)
	{
		Span<int> span = new(ref value);
		return span.Length + span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanIsEmptyEntry()
	{
		int[] emptySource = null!;
		Span<int> empty = emptySource;
		Span<int> present = new int[1];
		return empty.IsEmpty && !present.IsEmpty ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanDefaultAcrossCollectionEntry()
	{
		Span<int> span = default;
		M68kRuntime.Collect();
		return span.IsEmpty && span.Length == 0 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanSliceOwnerSurvivesCollectionEntry()
	{
		var values = new int[4];
		values[0] = 5;
		values[1] = 19;
		values[2] = 23;
		values[3] = 7;
		Span<int> span = values;
		Span<int> tail = span.Slice(1);
		Span<int> middle = span.Slice(1, 2);
		values = null!;
		span = default;
		M68kRuntime.Collect();
		var replacement = new int[4];
		replacement[0] = 100;
		replacement[1] = 200;
		replacement[2] = 300;
		replacement[3] = 400;
		return tail.Length + tail[0] + tail[1] +
			middle.Length + middle[0] + middle[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int WideSpanExactLayoutEntry()
	{
		var values = new BoxedTriple[3];
		var first = new BoxedTriple(1, 2, 3);
		var second = new BoxedTriple(4, 5, 6);
		var selected = new BoxedTriple(10, 12, 18);
		values[0] = first;
		values[1] = second;
		values[2] = selected;
		Span<BoxedTriple> span = values;
		Span<BoxedTriple> tail = span.Slice(1);
		values = null!;
		span = default;
		M68kRuntime.Collect();
		var replacement = new BoxedTriple[3];
		var replacementValue = new BoxedTriple(100, 200, 300);
		replacement[2] = replacementValue;
		return tail.Length +
			tail[1].First +
			tail[1].Second +
			tail[1].Third;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanSliceBoundsEntry()
	{
		Span<int> span = new int[2];
		try
		{
			_ = span.Slice(-1);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
		}
		try
		{
			_ = span.Slice(1, 2);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanArraySliceOwnerSurvivesCollectionEntry()
	{
		var values = new int[4];
		values[0] = 5;
		values[1] = 19;
		values[2] = 23;
		values[3] = 7;
		ReadOnlySpan<int> span = values;
		ReadOnlySpan<int> tail = span.Slice(1);
		ReadOnlySpan<int> middle = span.Slice(1, 2);
		values = null!;
		span = default;
		M68kRuntime.Collect();
		var replacement = new int[4];
		replacement[0] = 100;
		replacement[1] = 200;
		replacement[2] = 300;
		replacement[3] = 400;
		return !tail.IsEmpty
			? tail.Length + tail[0] + tail[1] +
				middle.Length + middle[0] + middle[1]
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanFromSpanOwnerSurvivesCollectionEntry()
	{
		var values = new int[2];
		values[0] = 19;
		values[1] = 23;
		Span<int> writable = values;
		ReadOnlySpan<int> readOnly = writable;
		values = null!;
		writable = default;
		M68kRuntime.Collect();
		var replacement = new int[2];
		replacement[0] = 100;
		replacement[1] = 200;
		return readOnly.Length + readOnly[0] + readOnly[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanFromStringEntry()
	{
		ReadOnlySpan<char> literal = "AZ";
		M68kRuntime.Collect();
		string nullText = null!;
		ReadOnlySpan<char> empty = nullText;
		return literal.Length == 2 &&
			literal[0] == 'A' &&
			literal[1] == 'Z' &&
			empty.IsEmpty
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanCharSequenceEqualEntry()
	{
		ReadOnlySpan<char> first = "Copper";
		ReadOnlySpan<char> equal = "Copper";
		ReadOnlySpan<char> different = "Coppex";
		ReadOnlySpan<char> shorter = "Coppe";
		string nullText = null!;
		ReadOnlySpan<char> empty = nullText;
		return first.SequenceEqual(equal) &&
			!first.SequenceEqual(different) &&
			!first.SequenceEqual(shorter) &&
			empty.SequenceEqual(default)
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStringReadOnlySpanOwnerSurvivesCollectionEntry()
	{
		var text = M68kRuntime.AllocateString(2);
		ReadOnlySpan<char> characters = text;
		text = null!;
		M68kRuntime.Collect();
		if (characters.Length != 2 ||
			characters[0] != '\0' ||
			characters[1] != '\0')
		{
			return 1;
		}
		var replacement = new ushort[3];
		replacement[0] = 'X';
		replacement[1] = 'Y';
		return characters.Length == 2 &&
			characters[0] == '\0' &&
			characters[1] == '\0'
				? 42
				: 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStringLengthValidationEntry()
	{
		var score = 0;
		try
		{
			_ = M68kRuntime.AllocateString(-1);
		}
		catch (ArgumentOutOfRangeException)
		{
			score += 20;
		}
		try
		{
			_ = M68kRuntime.AllocateString(int.MaxValue);
		}
		catch (OutOfMemoryException)
		{
			score += 22;
		}
		return score;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanSliceBoundsEntry()
	{
		ReadOnlySpan<int> span = new int[2];
		try
		{
			_ = span.Slice(-1);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
		}
		try
		{
			_ = span.Slice(1, 2);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanReturnOwnerSurvivesCollectionEntry()
	{
		int[]? values = [19, 22];
		var returned = ReturnReadOnlySpan(values);
		values = null;
		var replacement = new int[2];
		replacement[0] = 1;
		return returned[0] + returned[1] + replacement[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<int> ReturnReadOnlySpan(int[] values) => values;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedReadOnlySpanParameterEntry()
	{
		ReadOnlySpan<int> span = new int[1];
		return ConsumeReadOnlySpan(span);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeReadOnlySpan(ReadOnlySpan<int> span) => span.Length;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedImportedReadOnlySpanParameterEntry()
	{
		ReadOnlySpan<int> span = new int[1];
		return ImportedReadOnlySpan(span);
	}

	[M68kImport("fixture.readOnlySpan")]
	[return: M68kRegister(M68kRegister.D0)]
	public static extern int ImportedReadOnlySpan(
		[M68kRegister(M68kRegister.A0)] ReadOnlySpan<int> span);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanParameterOwnerSurvivesCollectionEntry()
	{
		var values = new int[2];
		values[0] = 19;
		values[1] = 23;
		ReadOnlySpan<int> span = values;
		values = null!;
		return ForwardReadOnlySpanAcrossCollection(span);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ForwardReadOnlySpanAcrossCollection(ReadOnlySpan<int> span) =>
		ConsumeReadOnlySpanAcrossCollection(span);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeReadOnlySpanAcrossCollection(ReadOnlySpan<int> span)
	{
		M68kRuntime.Collect();
		if (span.Length != 2 || span[0] != 19 || span[1] != 23)
		{
			return 1;
		}
		var replacement = new int[2];
		replacement[0] = 100;
		replacement[1] = 200;
		return span.Length == 2 && span[0] == 19 && span[1] == 23
			? 42
			: 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanParameterOwnerSurvivesCollectionEntry()
	{
		var values = new int[2];
		values[0] = 19;
		values[1] = 23;
		Span<int> span = values;
		values = null!;
		return ForwardSpanAcrossCollection(span);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ForwardSpanAcrossCollection(Span<int> span) =>
		ConsumeSpanAcrossCollection(span);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeSpanAcrossCollection(Span<int> span)
	{
		M68kRuntime.Collect();
		if (span.Length != 2 || span[0] != 19 || span[1] != 23)
		{
			return 1;
		}
		span[0] = 20;
		var replacement = new int[2];
		replacement[0] = 100;
		replacement[1] = 200;
		return span.Length == 2 && span[0] == 20 && span[1] == 23
			? 42
			: 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstantStackallocSpanEntry()
	{
		Span<int> span = stackalloc int[3];
		span[0] = 11;
		span[1] = 13;
		span[2] = 18;
		return ForwardStackallocSpanAcrossCollection(span);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ForwardStackallocSpanAcrossCollection(Span<int> span) =>
		ConsumeStackallocSpanAcrossCollection(span);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeStackallocSpanAcrossCollection(Span<int> span)
	{
		M68kRuntime.Collect();
		if (span.Length != 3 || span[0] != 11 || span[1] != 13 || span[2] != 18)
		{
			return 1;
		}
		span[1] = 14;
		var replacement = new int[3];
		replacement[0] = 100;
		return span[0] + span[1] + span[2] - 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultipleConstantStackallocSpanEntry()
	{
		Span<byte> bytes = stackalloc byte[5];
		bytes[0] = 5;
		bytes[1] = 7;
		bytes[2] = 9;
		bytes[3] = 11;
		bytes[4] = 10;
		Span<int> integers = stackalloc int[1];
		integers[0] = 17;
		Span<int> empty = stackalloc int[0];
		return empty.IsEmpty
			? bytes[0] + bytes[1] + bytes[2] + bytes[3] + bytes[4] +
				integers[0] - 17
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStackallocSpanEntry(int count)
	{
		Span<int> span = stackalloc int[count];
		return span.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanByteCopyToEntry()
	{
		Span<byte> values = stackalloc byte[6];
		values[0] = 1;
		values[1] = 2;
		values[2] = 3;
		values[3] = 4;
		values[4] = 5;
		values[5] = 6;
		values.Slice(0, 5).CopyTo(values.Slice(1, 5));
		if (values[0] != 1) return 11;
		if (values[1] != 1) return 12;
		if (values[2] != 2) return 13;
		if (values[3] != 3) return 14;
		if (values[4] != 4) return 15;
		if (values[5] != 5) return 16;
		values.Slice(1, 5).CopyTo(values.Slice(0, 5));
		Span<byte> empty = stackalloc byte[0];
		empty.CopyTo(empty);
		ReadOnlySpan<byte> readOnlyEmpty = empty;
		readOnlyEmpty.CopyTo(empty);
		return values[0] == 1 && values[1] == 2 && values[2] == 3 &&
			values[3] == 4 && values[4] == 5 && values[5] == 5
				? 42
				: 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadOnlySpanIntCopyToEntry()
	{
		Span<int> source = stackalloc int[3];
		source[0] = 11;
		source[1] = 13;
		source[2] = 18;
		ReadOnlySpan<int> readOnly = source;
		Span<int> destination = stackalloc int[3];
		readOnly.CopyTo(destination);
		return destination[0] + destination[1] + destination[2];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanFloatCopyToEntry()
	{
		Span<float> source = stackalloc float[2];
		Span<float> destination = stackalloc float[2];
		source.CopyTo(destination);
		return source.Length == 2 && destination.Length == 2 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float SpanFloatElementAccessEntry()
	{
		Span<float> values = stackalloc float[2];
		values[0] = 1.25f;
		values[1] = 2.5f;
		ReadOnlySpan<float> readOnly = values;
		return readOnly[0] + readOnly[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long SpanLongElementAccessEntry()
	{
		Span<long> values = stackalloc long[1];
		values[0] = 0x1122334455667788L;
		ReadOnlySpan<long> readOnly = values;
		return readOnly[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanLongLowWordElementAccessEntry()
	{
		Span<long> values = stackalloc long[1];
		values[0] = 0x1122334455667788L;
		ReadOnlySpan<long> readOnly = values;
		return unchecked((int)readOnly[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanCopyToShortDestinationEntry()
	{
		Span<byte> source = stackalloc byte[2];
		Span<byte> destination = stackalloc byte[1];
		try
		{
			source.CopyTo(destination);
			return 0;
		}
		catch (ArgumentException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStackallocSpanCallerEntry() =>
		DynamicStackallocSpanEntry(3);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStackallocNestedCallEntry()
	{
		Span<int> span = stackalloc int[3];
		span[0] = 10;
		span[1] = 13;
		span[2] = 19;
		return AddDynamicStackallocValues(span[0], span[1]) + span[2];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AddDynamicStackallocValues(int left, int right) =>
		left + right;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStackallocNegativeCountEntry()
	{
		try
		{
			return DynamicStackallocSpanEntry(-1);
		}
		catch (OverflowException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStackallocExceptionUnwindEntry()
	{
		Span<int> span = stackalloc int[3];
		span[0] = 42;
		try
		{
			return DynamicStackallocSpanEntry(-1);
		}
		catch (OverflowException)
		{
			return span[0];
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicStackallocGcEntry()
	{
		Span<int> scratch = stackalloc int[3];
		scratch[0] = 3;
		int[] retained = [18, 20];
		var trigger = new int[1];
		trigger[0] = 1;
		return scratch[0] + retained[0] + retained[1] + trigger[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanReturnOwnerSurvivesCollectionEntry()
	{
		int[]? values = [10, 11];
		var returned = ReturnSpan(values);
		values = null;
		var replacement = new int[2];
		replacement[0] = 1;
		returned[1] = 31;
		return returned[0] + returned[1] + replacement[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Span<int> ReturnSpan(int[] values) => values;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SpanParameterReturnOwnerSurvivesCollectionEntry()
	{
		int[]? values = [14, 15];
		Span<int> source = values;
		var returned = ReturnSpanParameter(source);
		source = default;
		values = null;
		var replacement = new int[2];
		replacement[0] = 1;
		returned[1] = 27;
		return returned[0] + returned[1] + replacement[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Span<int> ReturnSpanParameter(Span<int> value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static Span<int> UnsupportedSpanEntryPoint() => default;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static ReadOnlySpan<int> UnsupportedReadOnlySpanEntryPoint() => default;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedSpanParameterEntry()
	{
		var values = new int[1];
		Span<int> span = values;
		return ConsumeSpan(span);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ConsumeSpan(Span<int> span) => span.Length;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadConstrained<T>(ref T source)
		where T : struct, IRuntimeConstrainedSource => source.GetValue();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadConstrainedReference<T>(ref T source)
		where T : class, IRuntimeConstrainedSource => source.GetValue();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadConstrainedVirtual<T>(ref T source)
		where T : RuntimeConstrainedVirtualBase => source.GetValue();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadConstrainedObjectVirtual<T>(ref T source) =>
		source!.GetHashCode();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool EqualsConstrainedObjectVirtual<T>(
		ref T source,
		object other)
		where T : class =>
		source.Equals(other);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedScalarTypeIdentityEntry()
	{
		object value = 42;
		if (value is not int || value is uint || (int)value != 42)
		{
			return 0;
		}
		try
		{
			_ = (uint)value;
			return 0;
		}
		catch (InvalidCastException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedInt64TypeIdentityEntry()
	{
		const long expected = 0x00000023_00000007L;
		object value = expected;
		if (value is not long || value is ulong || (int)(long)value != 7)
		{
			return 0;
		}
		try
		{
			_ = (ulong)value;
			return 0;
		}
		catch (InvalidCastException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedInt64GcEntry()
	{
		const long expected = 0x00000023_00000007L;
		object value = expected;
		M68kRuntime.Collect();
		return (int)(long)value + 35;
	}

	private interface IBoxedWord
	{
		int GetValue();

		int Add(int value);

		int AddIfNull(object value);

		int AddTwo(int first, int second);

		int CheckReferences(object first, object second);

		int Mix(int delta, object marker);

		int MixReverse(object marker, int delta);

		int ThrowWithTwo(int first, int second);

		int AddLong(long value);

		int ThrowLong(long value);
	}

	private struct BoxedWord : IBoxedWord
	{
		public BoxedWord(int value) => Value = value;

		public int Value;

		public readonly int GetValue() => Value;

		public readonly int Add(int value) => Value + value;

		public readonly int AddIfNull(object value) =>
			value is null ? Value + 7 : 0;

		public readonly int AddTwo(int first, int second) =>
			Value + first + second;

		public readonly int CheckReferences(object first, object second) =>
			first is null && second is not null ? Value + 7 : 0;

		public readonly int Mix(int delta, object marker) =>
			marker is not null ? Value + delta : 0;

		public readonly int MixReverse(object marker, int delta) =>
			marker is not null ? Value + delta : 0;

		public readonly int ThrowWithTwo(int first, int second) =>
			throw null!;

		public readonly int AddLong(long value) => Value + (int)value;

		public readonly int ThrowLong(long value) => throw null!;
	}

	private struct OtherBoxedWord
	{
		public OtherBoxedWord(int value) => Value = value;

		public int Value;
	}

	private interface IBoxedPair
	{
		int Sum();
	}

	private struct BoxedPair : IBoxedPair
	{
		public BoxedPair(int first, int second)
		{
			First = first;
			Second = second;
		}

		public int First;

		public int Second;

		public readonly int Sum() => First + Second;
	}

	private sealed class MultiwordFieldHolder
	{
		public BoxedPair Value;
	}

	private static BoxedPair _multiwordStaticField;

	private struct OtherBoxedPair
	{
		public OtherBoxedPair(int first, int second)
		{
			First = first;
			Second = second;
		}

		public int First;

		public int Second;
	}

	private struct BoxedTriple
	{
		public BoxedTriple(int first, int second, int third)
		{
			First = first;
			Second = second;
			Third = third;
		}

		public int First;

		public int Second;

		public int Third;

		public readonly int Sum() => First + Second + Third;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructEntry()
	{
		var source = new BoxedWord(42);
		object value = source;
		source.Value = 0;
		if (value is not BoxedWord || value is OtherBoxedWord)
		{
			return 0;
		}
		try
		{
			_ = (OtherBoxedWord)value;
			return 0;
		}
		catch (InvalidCastException)
		{
			return ((BoxedWord)value).Value;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructGcEntry()
	{
		var source = new BoxedWord(42);
		object value = source;
		M68kRuntime.Collect();
		return ((BoxedWord)value).Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceEntry()
	{
		var source = new BoxedWord(42);
		IBoxedWord value = source;
		source.Value = 0;
		M68kRuntime.Collect();
		return value.GetValue();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceArgumentEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		source.Value = 0;
		M68kRuntime.Collect();
		return value.Add(7);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceReferenceArgumentEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		source.Value = 0;
		M68kRuntime.Collect();
		return value.AddIfNull(null!);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceTwoDataArgumentsEntry()
	{
		var source = new BoxedWord(30);
		IBoxedWord value = source;
		source.Value = 0;
		M68kRuntime.Collect();
		return value.AddTwo(5, 7);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceTwoReferenceArgumentsEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		object marker = new ManagedBox();
		source.Value = 0;
		M68kRuntime.Collect();
		return value.CheckReferences(null!, marker);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceMixedArgumentsEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		object marker = new ManagedBox();
		source.Value = 0;
		M68kRuntime.Collect();
		return value.Mix(7, marker) == 42 &&
			value.MixReverse(marker, 7) == 42
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceTwoDataExceptionEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		M68kRuntime.Collect();
		try
		{
			return value.ThrowWithTwo(5, 7);
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceLongArgumentEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		source.Value = 0;
		M68kRuntime.Collect();
		return value.AddLong(0x00000001_00000007L);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedSingleWordStructInterfaceLongExceptionEntry()
	{
		var source = new BoxedWord(35);
		IBoxedWord value = source;
		M68kRuntime.Collect();
		try
		{
			return value.ThrowLong(0x00000001_00000007L);
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordStructLocalEntry()
	{
		var source = new BoxedPair(19, 23);
		IBoxedPair value = source;
		source.First = 0;
		source.Second = 0;
		M68kRuntime.Collect();
		return value.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int BoxMultiwordArgument(BoxedPair source)
	{
		IBoxedPair value = source;
		M68kRuntime.Collect();
		return value.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordArgumentEntry()
	{
		var source = new BoxedPair(19, 23);
		return BoxMultiwordArgument(source);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ForwardMultiwordArgument(BoxedPair source) =>
		BoxMultiwordArgument(source);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ForwardedMultiwordArgumentEntry()
	{
		var source = new BoxedPair(19, 23);
		return ForwardMultiwordArgument(source);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReplaceMultiwordArgument(BoxedPair value)
	{
		var replacement = new BoxedPair(19, 23);
		value = ReturnMultiwordArgument(replacement);
		return BoxMultiwordArgument(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArgumentStoreEntry()
	{
		var initial = new BoxedPair(1, 2);
		return ReplaceMultiwordArgument(initial);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordInstanceFieldEntry()
	{
		var holder = new MultiwordFieldHolder();
		var source = new BoxedPair(19, 23);
		holder.Value = source;
		var copy = holder.Value;
		var replacement = new BoxedPair(1, 2);
		holder.Value = replacement;
		return BoxMultiwordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordInstanceFieldExpressionEntry()
	{
		var holder = new MultiwordFieldHolder();
		var source = new BoxedPair(19, 23);
		holder.Value = source;
		return BoxMultiwordArgument(holder.Value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordStaticFieldEntry()
	{
		var source = new BoxedPair(19, 23);
		_multiwordStaticField = source;
		var copy = _multiwordStaticField;
		var replacement = new BoxedPair(1, 2);
		_multiwordStaticField = replacement;
		return BoxMultiwordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordStaticFieldExpressionEntry()
	{
		var source = new BoxedPair(19, 23);
		_multiwordStaticField = source;
		return BoxMultiwordArgument(_multiwordStaticField);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayEntry()
	{
		var values = new BoxedPair[2];
		var source = new BoxedPair(19, 23);
		values[0] = source;
		var copy = values[0];
		var replacement = new BoxedPair(1, 2);
		values[0] = replacement;
		return BoxMultiwordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayExpressionEntry()
	{
		var values = new BoxedPair[1];
		var source = new BoxedPair(19, 23);
		values[0] = source;
		return BoxMultiwordArgument(values[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ThreeWordArrayEntry()
	{
		var values = new BoxedTriple[1];
		var source = new BoxedTriple(9, 14, 19);
		values[0] = source;
		return SumThreeWordArgument(values[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayZeroInitializationEntry()
	{
		var values = new BoxedPair[1];
		return 42 - BoxMultiwordArgument(values[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayCollectionEntry()
	{
		var values = new BoxedPair[1];
		var source = new BoxedPair(19, 23);
		values[0] = source;
		M68kRuntime.Collect();
		return BoxMultiwordArgument(values[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayLoadBoundsEntry()
	{
		var values = new BoxedPair[1];
		try
		{
			return BoxMultiwordArgument(values[1]);
		}
		catch (IndexOutOfRangeException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayStoreBoundsEntry()
	{
		var values = new BoxedPair[1];
		var source = new BoxedPair(19, 23);
		try
		{
			values[1] = source;
			return 0;
		}
		catch (IndexOutOfRangeException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArrayNegativeLengthEntry()
	{
		var length = -1;
		try
		{
			_ = new BoxedPair[length];
			return 0;
		}
		catch (OverflowException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArraySizeOverflowEntry()
	{
		try
		{
			_ = new BoxedPair[int.MaxValue];
			return 0;
		}
		catch (OverflowException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedPair ReadIndirect(ref BoxedPair value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedTriple ReadIndirect(ref BoxedTriple value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteIndirect(ref BoxedPair target, BoxedPair value) =>
		target = value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ClearIndirect(ref BoxedPair target) => target = default;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void CopyIndirect(
		ref BoxedPair target,
		ref BoxedPair source) =>
		target = source;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordIndirectLoadEntry()
	{
		var source = new BoxedPair(19, 23);
		var copy = ReadIndirect(ref source);
		source = new BoxedPair(1, 2);
		return BoxMultiwordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ThreeWordIndirectLoadEntry()
	{
		var source = new BoxedTriple(9, 14, 19);
		var copy = ReadIndirect(ref source);
		source = new BoxedTriple(1, 2, 3);
		return SumThreeWordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordIndirectStoreEntry()
	{
		var target = new BoxedPair(1, 2);
		var replacement = new BoxedPair(19, 23);
		WriteIndirect(ref target, replacement);
		return BoxMultiwordArgument(target);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordIndirectInitializeEntry()
	{
		var target = new BoxedPair(19, 23);
		ClearIndirect(ref target);
		return 42 - BoxMultiwordArgument(target);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordIndirectCopyEntry()
	{
		var target = new BoxedPair(1, 2);
		var source = new BoxedPair(19, 23);
		CopyIndirect(ref target, ref source);
		source = new BoxedPair(3, 4);
		return BoxMultiwordArgument(target);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SumThreeWordArgument(BoxedTriple source) => source.Sum();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ThreeWordArgumentEntry()
	{
		var source = new BoxedTriple(9, 14, 19);
		return SumThreeWordArgument(source);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int MixMultiwordArgument(
		int prefix,
		BoxedPair source,
		int suffix) =>
		prefix + source.Sum() + suffix;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MixedScalarMultiwordArgumentEntry()
	{
		var source = new BoxedPair(19, 20);
		return MixMultiwordArgument(1, source, 2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int MixReferenceMultiwordArgument(
		object marker,
		BoxedPair source,
		object? tail) =>
		marker is not null && tail is null ? source.Sum() : 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MixedReferenceMultiwordArgumentEntry()
	{
		object marker = new ManagedBox();
		var source = new BoxedPair(19, 23);
		return MixReferenceMultiwordArgument(marker, source, null);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SumTwoMultiwordArguments(BoxedPair first, BoxedPair second) =>
		first.Sum() + second.Sum();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int TwoMultiwordArgumentsEntry()
	{
		var first = new BoxedPair(9, 10);
		var second = new BoxedPair(11, 12);
		return SumTwoMultiwordArguments(first, second);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ThrowMultiwordArgument(BoxedPair source) => throw null!;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordArgumentExceptionEntry()
	{
		var source = new BoxedPair(19, 23);
		try
		{
			return ThrowMultiwordArgument(source);
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordExpressionArgumentEntry()
	{
		var source = new BoxedPair(19, 23);
		object value = source;
		return BoxMultiwordArgument((BoxedPair)value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordExpressionEntry()
	{
		var source = new BoxedPair(19, 23);
		object value = source;
		M68kRuntime.Collect();
		IBoxedPair copy = (BoxedPair)value;
		M68kRuntime.Collect();
		return copy.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedPair ReturnMultiwordArgument(BoxedPair value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedPair ReturnConstructedMultiword()
	{
		var value = new BoxedPair(19, 23);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedTriple ReturnThreeWordArgument(BoxedTriple value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedTriple ReturnConstructedThreeWord()
	{
		var value = new BoxedTriple(9, 14, 19);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedPair ReturnMixedMultiwordArgument(
		int prefix,
		BoxedPair value,
		object marker)
	{
		_ = prefix;
		_ = marker;
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedPair ThrowBeforeMultiwordReturn(BoxedPair value) =>
		throw null!;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static BoxedPair ReturnConditionalMultiword(
		bool condition,
		BoxedPair first,
		BoxedPair second) =>
		condition ? first : second;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordReturnEntry()
	{
		var source = new BoxedPair(19, 23);
		return BoxMultiwordArgument(ReturnMultiwordArgument(source));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedMultiwordReturnEntry() =>
		BoxMultiwordArgument(ReturnConstructedMultiword());

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ThreeWordReturnEntry()
	{
		var source = new BoxedTriple(9, 14, 19);
		return SumThreeWordArgument(ReturnThreeWordArgument(source));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstructedThreeWordReturnEntry() =>
		SumThreeWordArgument(ReturnConstructedThreeWord());

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MixedMultiwordReturnEntry()
	{
		var source = new BoxedPair(19, 23);
		return BoxMultiwordArgument(
			ReturnMixedMultiwordArgument(1, source, new ManagedBox()));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordLocalCopyEntry()
	{
		var source = new BoxedPair(19, 23);
		var copy = source;
		source.First = 0;
		source.Second = 0;
		return BoxMultiwordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordReturnExceptionEntry()
	{
		var source = new BoxedPair(19, 23);
		try
		{
			return BoxMultiwordArgument(ThrowBeforeMultiwordReturn(source));
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordReturnEntry()
	{
		var source = new BoxedPair(19, 23);
		IBoxedPair value = ReturnMultiwordArgument(source);
		M68kRuntime.Collect();
		return value.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NestedMultiwordReturnEntry()
	{
		var source = new BoxedPair(19, 23);
		var copy = ReturnMultiwordArgument(
			ReturnMultiwordArgument(source));
		source.First = 0;
		source.Second = 0;
		return BoxMultiwordArgument(copy);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiwordPhiReturnEntry()
	{
		var first = new BoxedPair(19, 23);
		var second = new BoxedPair(1, 2);
		return BoxMultiwordArgument(
			ReturnConditionalMultiword(true, first, second));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordUnboxAnyEntry()
	{
		var source = new BoxedPair(19, 23);
		object value = source;
		source.First = 0;
		source.Second = 0;
		M68kRuntime.Collect();
		var copy = (BoxedPair)value;
		return copy.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedThreeWordUnboxAnyEntry()
	{
		var source = new BoxedTriple(9, 14, 19);
		object value = source;
		source.First = 0;
		source.Second = 0;
		source.Third = 0;
		M68kRuntime.Collect();
		var copy = (BoxedTriple)value;
		return copy.Sum();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordUnboxAnyIdentityEntry()
	{
		var source = new BoxedPair(19, 23);
		object value = source;
		try
		{
			var wrong = (OtherBoxedPair)value;
			return wrong.First + wrong.Second;
		}
		catch (InvalidCastException)
		{
			var copy = (BoxedPair)value;
			return copy.Sum();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BoxedMultiwordUnboxAnyNullEntry()
	{
		object value = null!;
		try
		{
			var copy = (BoxedPair)value;
			return copy.Sum();
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}


	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StaticDelegateEntry()
	{
		var transform = new Func<int, int>(StaticDelegateTarget);
		return transform(35);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StaticDelegateTarget(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NonCapturingLambdaEntry()
	{
		Func<int, int> transform = static value => value + 7;
		return transform(35);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ClosedInstanceDelegateEntry()
	{
		var box = new ManagedBox { Value = 35 };
		var transform = new Func<int, int>(box.Add);
		return transform(7);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CapturingLambdaEntry()
	{
		var captured = 35;
		Func<int> value = () => captured + 7;
		return value();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CapturingLambdaGcEntry()
	{
		var captured = 35;
		Func<int> value = () => captured + 7;
		M68kRuntime.Collect();
		return value();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int VirtualDelegateEntry()
	{
		VirtualBase source = new SealedVirtualDerived();
		var value = new Func<int>(source.GetValue);
		return value();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceDelegateEntry()
	{
		IValueSource source = new InterfaceValueSource();
		var value = new Func<int>(source.GetValue);
		return value();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CapturingActionEntry()
	{
		var result = 35;
		Action<int> add = value => result += value;
		add(7);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DelegateEqualityEntry()
	{
		var first = new Func<int, int>(StaticDelegateTarget);
		var same = new Func<int, int>(StaticDelegateTarget);
		var box = new ManagedBox { Value = 0 };
		var closedFirst = new Func<int, int>(box.Add);
		var closedSame = new Func<int, int>(box.Add);
		var closedDifferent = new Func<int, int>(new ManagedBox().Add);
		return first == same &&
			first != closedFirst &&
			closedFirst == closedSame &&
			closedFirst != closedDifferent
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DelegateEqualsEntry()
	{
		var first = new Func<int, int>(StaticDelegateTarget);
		var same = new Func<int, int>(StaticDelegateTarget);
		var different = new Func<int, int>(StaticDelegateDoubleTarget);
		return first.Equals(same) && !first.Equals(different) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int OrdinaryObjectEqualsEntry()
	{
		object receiver = new object();
		return receiver.Equals(receiver) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MulticastDelegateEntry()
	{
		var trace = 0;
		Func<int, int> first = value =>
		{
			trace = trace * 10 + 1;
			return value + 100;
		};
		Func<int, int> second = value =>
		{
			trace = trace * 10 + 2;
			return value + 7;
		};
		var handlers = first + second;
		var result = handlers(35);
		return trace == 12 ? result : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MulticastDelegateGcEntry()
	{
		var result = 0;
		Action<int> first = value => result += value;
		Action<int> second = value => result += value * 2;
		var handlers = first + second;
		M68kRuntime.Collect();
		handlers(14);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MulticastDelegateExceptionEntry()
	{
		var trace = 0;
		Action<int> first = value =>
		{
			trace = 1;
			throw null!;
		};
		Action<int> second = value => trace = 42;
		var handlers = first + second;
		try
		{
			handlers(0);
			return 0;
		}
		catch (Exception)
		{
			return trace == 1 ? 42 : 0;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MulticastDelegateEqualityEntry()
	{
		var first = new Func<int, int>(StaticDelegateTarget);
		var second = new Func<int, int>(StaticDelegateDoubleTarget);
		var equivalentFirst = new Func<int, int>(StaticDelegateTarget);
		var equivalentSecond = new Func<int, int>(StaticDelegateDoubleTarget);
		return first + second == equivalentFirst + equivalentSecond &&
			first + second != equivalentSecond + equivalentFirst
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IncompatibleDelegateCombineEntry()
	{
		try
		{
			_ = Delegate.Combine(
				new Action<int>(StaticDelegateActionTarget),
				new Func<int, int>(StaticDelegateTarget));
			return 0;
		}
		catch (ArgumentException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MulticastDelegateRemoveEntry()
	{
		var trace = 0;
		Func<int, int> first = value =>
		{
			trace = trace * 10 + 1;
			return value + 100;
		};
		Func<int, int> second = value =>
		{
			trace = trace * 10 + 2;
			return value + 7;
		};
		var pair = first + second;
		var handlers = pair + pair;
		handlers -= pair;
		if (handlers!(35) != 42 || trace != 12)
		{
			return 0;
		}
		handlers -= first;
		if (handlers!(35) != 42 || trace != 122)
		{
			return 0;
		}
		handlers -= second;
		return handlers == null ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MulticastDelegateRemoveGcEntry()
	{
		var result = 0;
		Action<int> first = value => result += value;
		Action<int> second = value => result += value * 2;
		Action<int> third = value => result += value * 4;
		var handlers = first + second + third;
		handlers -= first;
		M68kRuntime.Collect();
		handlers!(7);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StaticDelegateDoubleTarget(int value) => value * 2;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void StaticDelegateActionTarget(int value)
	{
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int InterfaceArrayStoreTypeCheckEntry()
	{
		IValueSource[] values = new InterfaceValueSource[2];
		values[0] = new InterfaceValueSource();
		values[1] = null!;
		try
		{
			((object[])values)[1] = new SealedVirtualDerived();
			return 0;
		}
		catch (ArrayTypeMismatchException)
		{
			return values[0].GetValue();
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object CreateRuntimeTypeTestObject(int kind) =>
		kind == 0 ? new SealedVirtualDerived() : new InterfaceValueSource();

	private sealed class RuntimeGenericBox<T>
	{
		public int Value;
	}

	private sealed class RuntimeDependentGenericBox<T>
	{
		public T? Value;
	}

	private static class RuntimeGenericStatics<T>
	{
		public static T? Value;
	}

	private static class RuntimeInitializedGenericStatics<T>
	{
		public static int Value = 21;
	}

	private sealed class RuntimeCompoundGenericBox<T>
	{
		public T[]? Values;
		public RuntimeDependentGenericBox<T>? Nested;
	}

	private static class RuntimeGenericMethods<T>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public static T? OwnerValue<U>(U ignored) => RuntimeGenericStatics<T>.Value;
	}

	private interface IRuntimeGenericSource<T>
	{
		T GetValue();
	}

	private interface IRuntimeGenericChildSource<T> : IRuntimeGenericSource<T>
	{
	}

	private class RuntimeVariantBase
	{
		public int Value;
	}

	private sealed class RuntimeVariantDerived : RuntimeVariantBase
	{
	}

	private interface IRuntimeCovariantSource<out T>
	{
		T GetValue();
	}

	private interface IRuntimeContravariantSink<in T>
	{
		int Accept(T value);
	}

	private interface IRuntimeVariantMap<in TIn, out TOut>
	{
		TOut Map(TIn value);
	}

	private interface IRuntimeCovariantChildSource<out T> :
		IRuntimeCovariantSource<T>
	{
	}

	private sealed class RuntimeVariantSource<T> : IRuntimeCovariantSource<T>
	{
		private readonly T _value;

		public RuntimeVariantSource(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public T GetValue() => _value;
	}

	private sealed class RuntimeVariantSink :
		IRuntimeContravariantSink<RuntimeVariantBase>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Accept(RuntimeVariantBase value) => value.Value;
	}

	private sealed class RuntimeVariantMap :
		IRuntimeVariantMap<RuntimeVariantBase, RuntimeVariantDerived>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public RuntimeVariantDerived Map(RuntimeVariantBase value) =>
			new() { Value = value.Value + 23 };
	}

	private sealed class RuntimeVariantChildSource<T> :
		IRuntimeCovariantChildSource<T>
	{
		private readonly T _value;

		public RuntimeVariantChildSource(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public T GetValue() => _value;
	}

	private sealed class RuntimeIntGenericSource : IRuntimeGenericSource<int>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => 19;
	}

	private sealed class RuntimeUIntGenericSource : IRuntimeGenericSource<uint>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public uint GetValue() => 23;
	}

	private sealed class RuntimeGenericSource<T> : IRuntimeGenericSource<T>
	{
		private readonly T _value;

		public RuntimeGenericSource(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public T GetValue() => _value;
	}

	private sealed class RuntimeExplicitGenericSource<T> : IRuntimeGenericSource<T>
	{
		private readonly T _value;

		public RuntimeExplicitGenericSource(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		T IRuntimeGenericSource<T>.GetValue() => _value;
	}

	private class RuntimeGenericInterfaceBase<T> : IRuntimeGenericSource<T>
	{
		private readonly T _value;

		protected RuntimeGenericInterfaceBase(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public T GetValue() => _value;
	}

	private sealed class RuntimeInheritedGenericSource<T> :
		RuntimeGenericInterfaceBase<T>
	{
		public RuntimeInheritedGenericSource(T value)
			: base(value)
		{
		}
	}

	private sealed class RuntimeGenericChildSource<T> :
		IRuntimeGenericChildSource<T>
	{
		private readonly T _value;

		public RuntimeGenericChildSource(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public T GetValue() => _value;
	}

	private abstract class RuntimeGenericVirtualSource<T>
	{
		public abstract T GetValue();
	}

	private sealed class RuntimeIntVirtualSource : RuntimeGenericVirtualSource<int>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 19;
	}

	private sealed class RuntimeUIntVirtualSource : RuntimeGenericVirtualSource<uint>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override uint GetValue() => 23;
	}

	private sealed class RuntimeGenericVirtualDerived<T> : RuntimeGenericVirtualSource<T>
	{
		private readonly T _value;

		public RuntimeGenericVirtualDerived(T value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public override T GetValue() => _value;
	}

	private abstract class RuntimeMultiHopVirtualBase<T>
	{
		public abstract int GetValue();
	}

	private abstract class RuntimeMultiHopVirtualMiddle<TLeft, TRight> :
		RuntimeMultiHopVirtualBase<TRight>
	{
	}

	private sealed class RuntimeMultiHopVirtualLeaf<TLeft, TRight> :
		RuntimeMultiHopVirtualMiddle<TRight, TLeft>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 42;
	}

	private sealed class RuntimeClosedMultiHopVirtualLeaf :
		RuntimeMultiHopVirtualMiddle<uint, int>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 42;
	}

	private interface IRuntimePermutedPair<TLeft, TRight>
	{
		int GetValue();
	}

	private class RuntimePermutedInterfaceMiddle<TLeft, TRight> :
		IRuntimePermutedPair<TRight, TLeft>
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => 42;
	}

	private sealed class RuntimePermutedInterfaceLeaf<TLeft, TRight> :
		RuntimePermutedInterfaceMiddle<TRight, TLeft>
	{
	}

	private class RuntimeGenericLayoutBase<T>
	{
		public T? BaseValue;
	}

	private sealed class RuntimeGenericLayoutDerived<T> : RuntimeGenericLayoutBase<T>
	{
		public int DerivedValue;
	}

	private sealed class RuntimeDisposable : IDisposable
	{
		public static int DisposeCount;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Dispose()
		{
			DisposeCount = 42;
		}
	}

	private sealed class ListReferenceValue
	{
		public ListReferenceValue(int value) => Value = value;

		public int Value;
	}

	private readonly struct ListPair
	{
		public ListPair(int first, int second)
		{
			First = first;
			Second = second;
		}

		public int First { get; }

		public int Second { get; }
	}

	private interface IRuntimeConstrainedSource
	{
		int GetValue();
	}

	private static RuntimeConstrainedSource _runtimeConstrainedSource =
		new RuntimeConstrainedSource(0);
	private static RuntimeStatefulConstrainedSource _runtimeStatefulConstrainedSource;

	private readonly struct RuntimeConstrainedSource : IRuntimeConstrainedSource
	{
		private readonly int _value;

		public RuntimeConstrainedSource(int value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => 42;
	}

	private struct RuntimeStatefulConstrainedSource : IRuntimeConstrainedSource
	{
		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => Value;
	}

	private class RuntimeConstrainedReferenceBase : IRuntimeConstrainedSource
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int GetValue() => 6;
	}

	private sealed class RuntimeConstrainedReferenceDerived :
		RuntimeConstrainedReferenceBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 42;
	}

	private class RuntimeConstrainedVirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public virtual int GetValue() => 6;
	}

	private sealed class RuntimeConstrainedVirtualDerived :
		RuntimeConstrainedVirtualBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetValue() => 42;
	}

	private sealed class RuntimeConstrainedObjectSource
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetHashCode() => 42;
	}

	private sealed class RuntimeObjectHashFallbackSource
	{
	}

	private class RuntimeObjectHashBase
	{
	}

	private sealed class RuntimeObjectHashDerived : RuntimeObjectHashBase
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public override int GetHashCode() => 42;
	}

	private sealed class RuntimeObjectEqualsFallbackSource
	{
	}

	private class RuntimeObjectEqualsBase
	{
	}

	private sealed class RuntimeObjectEqualsDerived : RuntimeObjectEqualsBase
	{
		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public override bool Equals(object? other) =>
			other is RuntimeObjectEqualsDerived candidate &&
			candidate.Value == Value;

		public override int GetHashCode() => Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MaterializedEqualityEntry() =>
		MaterializedEquality(17, 17) + MaterializedEquality(17, 25);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int MaterializedEquality(int left, int right)
	{
		var equal = left == right;
		return equal ? 20 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BooleanOrControlFlowEntry() =>
		BooleanOrControlFlow(17, 10, 42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int BooleanOrControlFlow(
		int value,
		int lowerBound,
		int upperBound)
	{
		var outside = value < lowerBound || value >= upperBound;
		return outside ? 1 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BooleanAndControlFlowEntry() =>
		BooleanAndControlFlow(17, 10, 42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int BooleanAndControlFlow(
		int value,
		int lowerBound,
		int upperBound)
	{
		var inside = value >= lowerBound && value < upperBound;
		return inside ? 42 : 1;
	}
}

public static class StaticInitializationFixtures
{
	private static readonly BPTR? File = global::Amiga.DOS.Open(
		"s:startup-sequence",
		global::Amiga.DOS.FileMode.OldFile);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry() => File.HasValue ? File.Value.Raw : 0u;
}

public static class TypeInitializationRuntimeFixtures
{
	private static int _failureAttempts;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int OnceOnlyEntry() =>
		OnceOnlyProbe.ReadAndIncrement() + OnceOnlyProbe.ReadAndIncrement();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RecursiveEntry() => RecursiveProbe.Value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FailureEntry()
	{
		var catches = 0;
		try
		{
			_ = FailureProbe.Value;
		}
		catch (TypeInitializationException)
		{
			catches++;
		}
		try
		{
			_ = FailureProbe.Value;
		}
		catch (TypeInitializationException)
		{
			catches++;
		}
		return catches == 2 && _failureAttempts == 1 ? 42 : 0;
	}

	private static class OnceOnlyProbe
	{
		private static int _value;

		static OnceOnlyProbe()
		{
			_value = 41;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public static int ReadAndIncrement() => _value++;
	}

	private static class RecursiveProbe
	{
		public static int Value;

		static RecursiveProbe()
		{
			Value = 40;
			Value = ReadDuringInitialization() + 1;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		private static int ReadDuringInitialization() => Value + 1;
	}

	private static class FailureProbe
	{
		public static int Value;

		static FailureProbe()
		{
			_failureAttempts++;
			var denominator = _failureAttempts - 1;
			Value = 1 / denominator;
		}
	}
}
