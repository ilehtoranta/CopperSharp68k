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

	[M68kEntryPoint]
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DefaultEntry()
	{
		var left = 9;
		var right = 5;
		return Arithmetic(left, right) + LoopAndBranch(6);
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
	public static int ShiftAndCompare()
	{
		var value = 3 << 5;
		value = (int)((uint)value >> 2);
		return value > 20 && value < 30 ? value : -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int QuickArithmeticEntry() => QuickArithmetic(40);

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
	public static uint CStringLiteralEntry() =>
		global::Amiga.CString.ToUInt32(global::Amiga.CString.FromLiteral("abc"));

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
	public static float UnsupportedFloat() => 1.25f;

	public sealed class ManagedBox
	{
		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Add(int value) => Value + value;
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
	}

	public sealed class InterfaceAdder : IAdder
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public int Add(int value) => value + 2;
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
	public static int MaterializedEqualityEntry() =>
		MaterializedEquality(17, 17) + MaterializedEquality(17, 25);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int MaterializedEquality(int left, int right)
	{
		var equal = left == right;
		return equal ? 20 : 1;
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
