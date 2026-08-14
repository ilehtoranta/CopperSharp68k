using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public static class CompilerFixtures
{
	private enum ListByteState : byte
	{
		First = 0xA5,
		Second = 0x5A,
		Missing = 0x3C
	}

	private enum ListIntState
	{
		First = 0x1122_3344,
		Second = 0x5566_7788,
		Missing = 0x1234_5678
	}

	private enum ListLongState : long
	{
		First = 0x0000_0001_0000_0002L,
		HighDiffers = 0x0000_0003_0000_0002L,
		LowDiffers = 0x0000_0001_0000_0004L,
		Missing = 0x0000_0003_0000_0004L
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiModuleEntry() => ExternalMethods.AddAndDouble(12, 9);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MultiModuleGenericEntry() => ExternalMethods.AddOne<uint>(41);

	private sealed class FixtureException : Exception
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string FormatBase() => base.ToString();
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
	private static uint _hunkBssExtraA;
	private static uint _hunkBssExtraB;
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
	public static uint HunkBssEntry() => InitializeHunkBss(
		ref _counter,
		ref _zeroStatic,
		ref _terminalScalar,
		ref _terminalReadFlag,
		ref _managedByrefStaticEscapeSink,
		ref _hunkBssExtraA,
		ref _hunkBssExtraB);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint InitializeHunkBss(
		ref int counter,
		ref int zeroStatic,
		ref uint terminalScalar,
		ref uint terminalReadFlag,
		ref uint escapeSink,
		ref uint extraA,
		ref uint extraB)
	{
		counter = 1;
		zeroStatic = 2;
		terminalScalar = 3;
		terminalReadFlag = 4;
		escapeSink = 21;
		extraA = 5;
		extraB = 6;
		return unchecked((uint)(counter + zeroStatic)) + terminalScalar +
			terminalReadFlag + escapeSink + extraA + extraB;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AllocatedDenseSwitchEntry()
	{
		var sum = DenseSwitch(-1) + DenseSwitch(0) + DenseSwitch(1) +
			DenseSwitch(2) + DenseSwitch(3) + DenseSwitch(4);
		return sum == 209 ? 42 : sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DenseSwitch(int value)
	{
		int result;
		switch (value)
		{
			case 0: result = 10; break;
			case 1: result = 20; break;
			case 2: result = 30; break;
			case 3: result = 40; break;
			default: result = 50; break;
		}
		return result + value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LoopCarriedInitializerEntry()
	{
		var position = 48;
		var direction = 1;
		ObserveLoopPosition(position);
		SetupBeforeLoop();
		while (!StopAfterOneLoopIteration())
		{
			ObserveLoopPosition(position);
			position += direction;
			if (position >= 140)
			{
				direction = -1;
			}
			else if (position <= 48)
			{
				direction = 1;
			}
		}

		return position;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ObserveLoopPosition(int position)
	{
		_terminalScalar = (uint)position;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetupBeforeLoop()
	{
		_terminalReadFlag = 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool StopAfterOneLoopIteration()
	{
		var stop = _counter != 0;
		_counter++;
		return stop;
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
	public static uint DivisionDifferentialCorpusEntry()
	{
		var hash = 0x811C_9DC5u;
		hash = MixUnsignedDivision(hash, 0, 1);
		hash = MixUnsignedDivision(hash, 1, 1);
		hash = MixUnsignedDivision(hash, uint.MaxValue, 1);
		hash = MixUnsignedDivision(hash, uint.MaxValue, uint.MaxValue);
		hash = MixUnsignedDivision(hash, uint.MaxValue, 0x8000_0000u);
		hash = MixUnsignedDivision(hash, 0x8000_0000u, uint.MaxValue);
		hash = MixUnsignedDivision(hash, 0xFFFF_0000u, 0x0000_FFFFu);

		var state = 0xC001_D00Du;
		for (var index = 0; index < 48; index++)
		{
			state = unchecked((state * 1_664_525u) + 1_013_904_223u);
			var dividend = state ^ (state << 13) ^ (state >> 9);
			state = unchecked((state * 22_695_477u) + 1u);
			var divisor = state | 1u;
			hash = MixUnsignedDivision(hash, dividend, divisor);
		}

		hash = MixSignedDivision(hash, 0, 1);
		hash = MixSignedDivision(hash, int.MaxValue, 1);
		hash = MixSignedDivision(hash, int.MinValue, 1);
		hash = MixSignedDivision(hash, int.MinValue, int.MinValue);
		hash = MixSignedDivision(hash, int.MaxValue, -1);
		hash = MixSignedDivision(hash, -17, 5);
		hash = MixSignedDivision(hash, 17, -5);
		hash = MixSignedDivision(hash, -17, -5);
		for (var index = 0; index < 32; index++)
		{
			state = unchecked((state * 1_103_515_245u) + 12_345u);
			var dividend = unchecked((int)(state ^ (state >> 11)));
			state = unchecked((state * 214_013u) + 2_531_011u);
			var divisor = unchecked((int)(state | 1u));
			hash = MixSignedDivision(hash, dividend, divisor);
		}

		return hash;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint MixUnsignedDivision(uint hash, uint dividend, uint divisor)
	{
		var quotient = dividend / divisor;
		var remainder = dividend % divisor;
		return unchecked((hash * 16_777_619u) ^ quotient ^
			((remainder << 7) | (remainder >> 25)));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint MixSignedDivision(uint hash, int dividend, int divisor)
	{
		var quotient = dividend / divisor;
		var remainder = dividend % divisor;
		var bits = unchecked((uint)remainder);
		return unchecked((hash * 16_777_619u) ^ (uint)quotient ^
			((bits << 11) | (bits >> 21)));
	}

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
	public static uint StackArgumentHomePreservesCallerD7Entry()
	{
		var first = 1u;
		var second = 2u;
		var third = 3u;
		var fourth = 4u;
		var index = 0u;
		var sum = 0u;
		while (index < 36)
		{
			sum += StackArgumentHomePreservesCallerD7Target(
				first,
				second,
				third,
				fourth);
			first++;
			second += 2;
			third += 3;
			fourth += 4;
			index++;
		}

		return index == 36 &&
			sum == 6_660 &&
			first == 37 &&
			second == 74 &&
			third == 111 &&
			fourth == 148
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint StackArgumentHomePreservesCallerD7Target(
		uint first,
		uint second,
		uint third,
		uint fourth)
	{
		var copiedThird = ReadStackArgumentHome(ref third);
		return first + second + copiedThird + fourth;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadStackArgumentHome(ref uint value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AnchoredStackArgumentHomePreservesCallerD7Entry()
	{
		var first = 1u;
		var second = 2u;
		var third = 3u;
		var fourth = 4u;
		var index = 0u;
		var sum = 0u;
		while (index < 36)
		{
			sum += AnchoredStackArgumentHomePreservesCallerD7Target(
				first,
				second,
				third,
				fourth);
			first++;
			second += 2;
			third += 3;
			fourth += 4;
			index++;
		}

		return index == 36 &&
			sum == 6_660 &&
			first == 37 &&
			second == 74 &&
			third == 111 &&
			fourth == 148
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint AnchoredStackArgumentHomePreservesCallerD7Target(
		uint first,
		uint second,
		uint third,
		uint fourth)
	{
		var scratchLength = (int)(first & 1u) + 1;
		Span<uint> scratch = stackalloc uint[scratchLength];
		scratch[0] = ReadStackArgumentHome(ref third);
		return first + second + scratch[0] + fourth;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AnchoredMultiwordStackArgumentHomeEntry()
	{
		var source = new BoxedPair(19, 23);
		return AnchoredMultiwordStackArgumentHome(source, 1) == 42 ? 42u : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AnchoredMultiwordStackArgumentHome(
		BoxedPair source,
		int scratchLength)
	{
		Span<int> scratch = stackalloc int[scratchLength];
		scratch[0] = source.First;
		return scratch[0] + source.Second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IncomingDataOverlapEntry() =>
		IncomingDataOverlap(1, 2, 3, 4, 5, 6, 7);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IncomingDataOverlap(
		int first,
		int second,
		int third,
		int fourth,
		int fifth,
		int sixth,
		int seventh) =>
		first +
		(second * 3) +
		(third * 5) +
		(fourth * 7) +
		(fifth * 11) +
		(sixth * 13) +
		(seventh * 17);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IncomingInt64OverlapEntry() =>
		IncomingInt64Overlap(1, 2, 0x1122_3344_5566_7788L, 3, 4);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IncomingInt64Overlap(
		int first,
		int second,
		long wide,
		int third,
		int fourth)
	{
		var low = M68kRuntime.SplitInt64(wide, out var high);
		return first + second + third + fourth +
			(int)(low & 0xFF) + (int)(high & 0xFF);
	}

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

	private struct ExternalTransparentScalarFieldRecord
	{
		public APTR Pointer;
		public int Bias;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ExternalTransparentScalarFieldEntry()
	{
		var record = new ExternalTransparentScalarFieldRecord
		{
			Pointer = APTR.FromPointer(40),
			Bias = 2
		};
		return record.Pointer.Raw + (uint)record.Bias;
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
	public static uint UnsignedDivideByZeroCatchEntry()
	{
		try
		{
			return uint.MaxValue / unchecked((uint)_counter);
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
	public static int CrossMethodFinallyCatchEntry()
	{
		_counter = 0;
		try
		{
			try
			{
				ThrowThroughFinally();
				return 1;
			}
			catch (NullReferenceException)
			{
				return _counter + 35;
			}
		}
		finally
		{
			_counter += 100;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ThrowThroughFinally()
	{
		try
		{
			throw null!;
		}
		finally
		{
			_counter = 7;
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
	public static uint VariableShiftCorpusEntry()
	{
		var value = 0x8F31_A5C7u;
		value = MixVariableShifts(value, -1);
		value = MixVariableShifts(value, 0);
		value = MixVariableShifts(value, 1);
		value = MixVariableShifts(value, 15);
		value = MixVariableShifts(value, 16);
		value = MixVariableShifts(value, 31);
		value = MixVariableShifts(value, 32);
		value = MixVariableShifts(value, 63);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint VariableShiftDifferentialEntry()
	{
		var checksum = 0u;
		var random = 0x68C0_D3A5u;
		for (var index = 0; index < 32; index++)
		{
			random ^= random << 13;
			random ^= random >> 17;
			random ^= random << 5;
			var count = unchecked((int)(random >> 24)) - 128;
			checksum = unchecked(
				((checksum << 1) | (checksum >> 31)) ^
				MixVariableShifts(random, count));
		}
		return checksum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint MixVariableShifts(uint value, int count) =>
		unchecked(
			((value << count) ^ (value >> count) ^
			 (uint)((int)(value ^ 0x55AA_33CCu) >> count)) *
			16_777_619u);

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
	public static uint PlatformBaseSameMergeEntry()
	{
		var first = PlatformBaseStateSelector() != 0
			? PlatformBaseStateA()
			: PlatformBaseStateAAlias();
		return first + PlatformBaseStateAHelper();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PlatformBaseDifferentMergeEntry()
	{
		var first = PlatformBaseStateSelector() != 0
			? PlatformBaseStateA()
			: PlatformBaseStateB();
		return first + PlatformBaseStateAHelper();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PlatformBaseUnknownMergeEntry()
	{
		var first = PlatformBaseStateSelector() != 0
			? PlatformBaseStateA()
			: 1u;
		return first + PlatformBaseStateAHelper();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PlatformBasePreservedAcrossInternalCallEntry()
	{
		var first = PlatformBaseStateA();
		var middle = PlatformBaseNeutralHelper();
		return first + middle + PlatformBaseStateA();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PlatformBaseIncomingA6Entry()
	{
		var first = global::Amiga.Exec.TypeOfMem(0x0000_1800);
		var value0 = APTR.FromPointer(1);
		var value1 = APTR.FromPointer(2);
		var value2 = APTR.FromPointer(3);
		var value3 = APTR.FromPointer(4);
		var value4 = APTR.FromPointer(5);
		var value5 = APTR.FromPointer(6);
		var value6 = APTR.FromPointer(7);
		return first + PlatformBaseIncomingA6Helper(
			value0,
			value1,
			value2,
			value3,
			value4,
			value5,
			value6);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint PlatformBaseIncomingA6Helper(
		APTR value0,
		APTR value1,
		APTR value2,
		APTR value3,
		APTR value4,
		APTR value5,
		APTR value6)
	{
		var sum = value0.Raw + value1.Raw + value2.Raw + value3.Raw + value4.Raw +
			value5.Raw + value6.Raw;
		global::Amiga.Exec.FreeMem(0x0000_1800, 4);
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PlatformBaseTailCallEntry()
	{
		_ = PlatformBaseStateA();
		return PlatformBaseStateAHelper();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PlatformBaseNestedFinallyEntry()
	{
		uint result;
		try
		{
			try
			{
				result = PlatformBaseStateA();
			}
			finally
			{
				_ = PlatformBaseStateA();
			}
		}
		finally
		{
			_ = PlatformBaseStateB();
		}
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint PlatformBaseStateAHelper() => PlatformBaseStateA();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint PlatformBaseNeutralHelper() => 7;

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint PlatformBaseStateA();

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint PlatformBaseStateAAlias();

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint PlatformBaseStateB();

	[MethodImpl(MethodImplOptions.InternalCall)]
	public static extern uint PlatformBaseStateSelector();

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
	public static int CallCallerProvidedLibrary() =>
		CallerProvidedVectors.Add(APTR.FromPointer(0x0000_3400), 19, 23);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallInvalidLibrarySignature() => InvalidVectors.MissingRegister(42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallInvalidLibraryLvo() => InvalidVectors.InvalidLvo();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallInvalidCallerProvidedLibrary() =>
		InvalidCallerProvidedVectors.Read(42);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkOpenLibrary()
	{
		var library = global::Amiga.Exec.OpenLibrary(0x0000_1800, 37);
		return library.HasValue ? library.Value.Raw : 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkOpenLibraryRaw() =>
		global::Amiga.Exec.OpenLibraryRaw(0x0000_1800, 37).Raw;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkOpenLibraryLiteral()
	{
		var library = global::Amiga.Exec.OpenLibrary("fixture.library", 37);
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

	[AmigaLibrary("fixture.device", AmigaLibraryBasePolicy.CallerProvided)]
	public static class CallerProvidedVectors
	{
		[AmigaLvo(-60)]
		public static extern int Add(
			[M68kRegister(M68kRegister.A6)] APTR deviceBase,
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

	[AmigaLibrary("invalid.device", AmigaLibraryBasePolicy.CallerProvided)]
	public static class InvalidCallerProvidedVectors
	{
		[AmigaLvo(-60)]
		public static extern int Read(
			[M68kRegister(M68kRegister.D0)] int value);
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
	public static int NonNullMergeTrueEntry() => NonNullMergeEntry(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NonNullMergeFalseEntry() => NonNullMergeEntry(false);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NonNullMergeEntry(bool firstPath)
	{
		ManagedBox box;
		if (firstPath)
		{
			box = new ManagedBox { Value = 19 };
		}
		else
		{
			box = new ManagedBox { Value = 23 };
		}
		box.OtherValue = 3;
		return box.Value + box.OtherValue;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullableMergeObjectEntry() => NullableMergeEntry(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NullableMergeEntry(bool hasValue)
	{
		ManagedBox? box = hasValue
			? new ManagedBox { Value = 42 }
			: null;
		return box!.Value;
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
	public static int InterfaceDispatchWithUnrelatedSdkRectangleEntry()
	{
		var rectangle = new Rectangle { MinX = 40, MaxX = 40 };
		IValueSource value = new InterfaceValueSource();
		return value.GetValue() + rectangle.MaxX - rectangle.MinX;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static IExternalValueSource ExternalInterfaceIdentityEntry() => null!;

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
	public static int Int64ToStringEntry()
	{
		var zero = 0L;
		var positive = 42L;
		var negative = -42L;
		var signedMinimum = long.MinValue;
		var signedMaximum = long.MaxValue;
		var unsignedInt32HighBit = 2_147_483_648UL;
		var unsignedHighLane = 4_294_967_296UL;
		var unsignedMaximum = ulong.MaxValue;
		if (zero.ToString() != "0") return 1;
		if (positive.ToString() != "42") return 2;
		if (negative.ToString() != "-42") return 3;
		if (signedMinimum.ToString() != "-9223372036854775808") return 4;
		if (signedMaximum.ToString() != "9223372036854775807") return 5;
		if (unsignedInt32HighBit.ToString() != "2147483648") return 6;
		if (unsignedHighLane.ToString() != "4294967296") return 7;
		if (unsignedMaximum.ToString() != "18446744073709551615") return 8;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DirectWidenedInt64ArgumentEntry()
	{
		if (ReadInt64Low(42L) != 42) return 1;
		if (ReadInt64Low(-42L) != -42) return 2;
		if (ReadUInt64Low(2_147_483_648UL) != 0x8000_0000u) return 3;
		if (ReadUInt64Low(ulong.MaxValue) != uint.MaxValue) return 4;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ReadInt64Low(long value) => unchecked((int)value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadUInt64Low(ulong value) => unchecked((uint)value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SplitInt64IntrinsicEntry()
	{
		var signedLow = M68kRuntime.SplitInt64(-42L, out var signedHigh);
		if (signedHigh != uint.MaxValue || signedLow != 0xffff_ffd6u) return 1;
		var unsignedLow = M68kRuntime.SplitUInt64(
			ulong.MaxValue,
			out var unsignedHigh);
		if (unsignedHigh != uint.MaxValue || unsignedLow != uint.MaxValue) return 2;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UInt64LaneFormatterEntry() =>
		CopperSharp.Runtime.ShadowIntegerFormatter.FormatUInt64(1, 42) ==
			"4294967338"
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
	public static int StringFormatParamsIntegerEntry()
	{
		var first = -42;
		var second = 42;
		var third = 123_456;
		var fourth = -654_321;
		var text = string.Format(
			"{{{3}}}:{0}:{1}:{2}:{0}",
			new object[] { first, second, third, fourth });
		return text.Length == 27 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringFormatSharedComputedParamsEntry()
	{
		int shared;
		var text = string.Format(
			"{0}",
			new object[] { shared = ProduceStringFormatValue() });
		return text.Length == 2 && shared == 42 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int ProduceStringFormatValue() => 42;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringFormatFixedArgumentsEntry()
	{
		var first = string.Format("value={0}", 42);
		var third = string.Format("{2}:{0}:{1}", -42, 7, 1234);
		return first.Length == 8 && third.Length == 10 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringFormatSpanParamsEntry()
	{
		var text = string.Format("{3}:{0}:{1}:{2}", -42, 7, 1234, -5678);
		return text.Length == 16 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringFormatSpanEightParamsEntry()
	{
		var text = string.Format(
			"{0}{1}{2}{3}{4}{5}{6}{7}",
			1, 2, 3, 4, 5, 6, 7, 8);
		return text.Length == 8 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringFormatOverflowingIndexEntry()
	{
		try
		{
			_ = string.Format("{4294967296}", new object[] { 42 });
		}
		catch (FormatException)
		{
			return 42;
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedEscapingStringFormatParamsEntry()
	{
		var arguments = new object[] { 1, 2, 3, 4 };
		return string.Format("{0}:{1}:{2}:{3}", arguments).Length;
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
	public static int ShadowMathIntegralSurfaceEntry()
	{
		var score = 0;
		score += Math.Abs((sbyte)(-1)) == 1 ? 1 : 0;
		score += Math.Abs((short)(-2)) == 2 ? 1 : 0;
		score += Math.Abs(-3) == 3 ? 1 : 0;
		score += HasInt64Bits(Math.Abs(-4L), 0, 4) ? 1 : 0;
		score += Math.Abs((nint)(-5)) == (nint)5 ? 1 : 0;

		score += Math.Sign((sbyte)(-1)) == -1 ? 1 : 0;
		score += Math.Sign((short)(-1)) == -1 ? 1 : 0;
		score += Math.Sign(0) == 0 ? 1 : 0;
		score += Math.Sign(1L) == 1 ? 1 : 0;
		score += Math.Sign((nint)1) == 1 ? 1 : 0;

		score += Math.Min((byte)2, (byte)1) == 1 ? 1 : 0;
		score += Math.Max((byte)1, (byte)2) == 2 ? 1 : 0;
		score += Math.Clamp((byte)3, (byte)1, (byte)2) == 2 ? 1 : 0;
		score += Math.Min((sbyte)2, (sbyte)1) == 1 ? 1 : 0;
		score += Math.Max((sbyte)1, (sbyte)2) == 2 ? 1 : 0;
		score += Math.Clamp((sbyte)3, (sbyte)1, (sbyte)2) == 2 ? 1 : 0;
		score += Math.Min((short)2, (short)1) == 1 ? 1 : 0;
		score += Math.Max((short)1, (short)2) == 2 ? 1 : 0;
		score += Math.Clamp((short)3, (short)1, (short)2) == 2 ? 1 : 0;
		score += Math.Min((ushort)2, (ushort)1) == 1 ? 1 : 0;
		score += Math.Max((ushort)1, (ushort)2) == 2 ? 1 : 0;
		score += Math.Clamp((ushort)3, (ushort)1, (ushort)2) == 2 ? 1 : 0;
		score += Math.Min(2, 1) == 1 ? 1 : 0;
		score += Math.Max(1, 2) == 2 ? 1 : 0;
		score += Math.Clamp(3, 1, 2) == 2 ? 1 : 0;
		score += Math.Min(2u, 1u) == 1u ? 1 : 0;
		score += Math.Max(1u, 2u) == 2u ? 1 : 0;
		score += Math.Clamp(3u, 1u, 2u) == 2u ? 1 : 0;
		score += HasInt64Bits(Math.Min(2L, 1L), 0, 1) ? 1 : 0;
		score += HasInt64Bits(Math.Max(1L, 2L), 0, 2) ? 1 : 0;
		score += HasInt64Bits(Math.Clamp(3L, 1L, 2L), 0, 2) ? 1 : 0;
		score += HasUInt64Bits(Math.Min(2UL, 1UL), 0, 1) ? 1 : 0;
		score += HasUInt64Bits(Math.Max(1UL, 2UL), 0, 2) ? 1 : 0;
		score += HasUInt64Bits(Math.Clamp(3UL, 1UL, 2UL), 0, 2) ? 1 : 0;
		score += Math.Min((nint)2, (nint)1) == (nint)1 ? 1 : 0;
		score += Math.Max((nint)1, (nint)2) == (nint)2 ? 1 : 0;
		score += Math.Clamp((nint)3, (nint)1, (nint)2) == (nint)2 ? 1 : 0;
		score += Math.Min((nuint)2, (nuint)1) == (nuint)1 ? 1 : 0;
		score += Math.Max((nuint)1, (nuint)2) == (nuint)2 ? 1 : 0;
		score += Math.Clamp((nuint)3, (nuint)1, (nuint)2) == (nuint)2 ? 1 : 0;

		score += HasInt64Bits(Math.BigMul(-2, 3), 0xffff_ffffu, 0xffff_fffau) ? 1 : 0;
		score += HasUInt64Bits(Math.BigMul(2u, 3u), 0, 6) ? 1 : 0;
		return score;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasInt64Bits(long value, uint expectedHigh, uint expectedLow)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		return high == expectedHigh && low == expectedLow;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool HasUInt64Bits(ulong value, uint expectedHigh, uint expectedLow)
	{
		var low = M68kRuntime.SplitUInt64(value, out var high);
		return high == expectedHigh && low == expectedLow;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShadowMathIeeeSurfaceEntry()
	{
		var score = 0;
		score += double.IsFinite(42.0) ? 1 : 0;
		score += double.IsInfinity(double.PositiveInfinity) ? 1 : 0;
		score += double.IsNaN(double.NaN) ? 1 : 0;
		score += double.IsNegative(-0.0) ? 1 : 0;
		score += double.IsNegativeInfinity(double.NegativeInfinity) ? 1 : 0;
		score += double.IsPositiveInfinity(double.PositiveInfinity) ? 1 : 0;
		score += double.IsNormal(1.0) ? 1 : 0;
		score += double.IsSubnormal(double.Epsilon) ? 1 : 0;
		score += float.IsFinite(42.0f) ? 1 : 0;
		score += float.IsInfinity(float.PositiveInfinity) ? 1 : 0;
		score += float.IsNaN(float.NaN) ? 1 : 0;
		score += float.IsNegative(-0.0f) ? 1 : 0;
		score += float.IsNegativeInfinity(float.NegativeInfinity) ? 1 : 0;
		score += float.IsPositiveInfinity(float.PositiveInfinity) ? 1 : 0;
		score += float.IsNormal(1.0f) ? 1 : 0;
		score += float.IsSubnormal(float.Epsilon) ? 1 : 0;

		var low = M68kRuntime.SplitDouble(Math.Abs(-1.0), out var high);
		score += high == 0x3ff0_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.CopySign(1.0, -0.0), out high);
		score += high == 0xbff0_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Min(0.0, -0.0), out high);
		score += high == 0x8000_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Max(-0.0, 0.0), out high);
		score += high == 0 && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Clamp(3.0, 1.0, 2.0), out high);
		score += high == 0x4000_0000u && low == 0 ? 1 : 0;
		score += Math.Sign(-1.0) == -1 ? 1 : 0;

		score += M68kRuntime.SingleToUInt32Bits(Math.Abs(-1.0f)) == 0x3f80_0000u ? 1 : 0;
		score += M68kRuntime.SingleToUInt32Bits(Math.Min(0.0f, -0.0f)) == 0x8000_0000u ? 1 : 0;
		score += M68kRuntime.SingleToUInt32Bits(Math.Max(-0.0f, 0.0f)) == 0 ? 1 : 0;
		score += M68kRuntime.SingleToUInt32Bits(Math.Clamp(3.0f, 1.0f, 2.0f)) == 0x4000_0000u ? 1 : 0;
		score += Math.Sign(-1.0f) == -1 ? 1 : 0;
		return score == 27 ? 42 : score;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShadowMathFloatingSignNaNCatchEntry()
	{
		try
		{
			return Math.Sign(double.NaN);
		}
		catch (ArithmeticException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ShadowMathSoftwareRoundingEntry()
	{
		var score = 0;
		var low = M68kRuntime.SplitDouble(Math.Sqrt(4.0), out var high);
		score += high == 0x4000_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Round(2.5), out high);
		score += high == 0x4000_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Round(2.5, MidpointRounding.AwayFromZero), out high);
		score += high == 0x4008_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Truncate(-2.75), out high);
		score += high == 0xc000_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Floor(-2.25), out high);
		score += high == 0xc008_0000u && low == 0 ? 1 : 0;
		low = M68kRuntime.SplitDouble(Math.Ceiling(-2.25), out high);
		score += high == 0xc000_0000u && low == 0 ? 1 : 0;
		return score == 6 ? 42 : score;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double NativeMathSqrtEntry() => Math.Sqrt(4.0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static double NativeMathTruncateEntry() => Math.Truncate(-2.75);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CheckedInt32AddEntry()
	{
		if (CheckedInt32Add(19, 23) != 42)
		{
			return 1;
		}
		return CheckedInt32Add(-20, -22) == -42 ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CheckedInt32AddOverflowCatchEntry()
	{
		var caught = 0;
		try
		{
			_ = CheckedInt32Add(int.MaxValue, 1);
		}
		catch (OverflowException)
		{
			caught |= 1;
		}

		try
		{
			_ = CheckedInt32Add(int.MinValue, -1);
		}
		catch (OverflowException)
		{
			caught |= 2;
		}

		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CheckedInt32Add(int left, int right) => checked(left + right);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CheckedUInt32ToInt32Entry()
	{
		if (CheckedUInt32ToInt32(int.MaxValue) != int.MaxValue)
		{
			return 1;
		}

		var caught = 0;
		try
		{
			_ = CheckedUInt32ToInt32(0x8000_0000u);
		}
		catch (OverflowException)
		{
			caught |= 1;
		}
		try
		{
			_ = CheckedUInt32ToInt32(uint.MaxValue);
		}
		catch (OverflowException)
		{
			caught |= 2;
		}
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CheckedUInt32ToInt32(uint value) => checked((int)value);

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
	public static int DictionaryInt32Entry()
	{
		var values = new Dictionary<int, int>();
		values.Add(1, 1);
		values.Add(5, 5);
		values.Add(9, 9);
		values.Add(13, 13);
		values.Add(17, 17);
		if (values.Count != 5 || values[1] != 1 || values[17] != 17) return 1;
		if (!values.TryGetValue(13, out var thirteen) || thirteen != 13) return 2;
		if (values.TryGetValue(2, out var missing) || missing != 0) return 3;
		values[9] = 11;
		try
		{
			values.Add(5, 0);
			return 4;
		}
		catch (ArgumentException)
		{
		}
		try
		{
			_ = values[2];
			return 5;
		}
		catch (KeyNotFoundException)
		{
		}
		return values[1] + values[5] + values[9] + values[13] + values[17] - 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DictionaryInt32ReferenceGcEntry()
	{
		var values = new Dictionary<int, string>();
		values.Add(1, "x1234567x".Substring(1, 7));
		values.Add(5, "x12345678x".Substring(1, 8));
		values.Add(9, "x123456789x".Substring(1, 9));
		values.Add(13, "x1234567890x".Substring(1, 10));
		values.Add(17, "x12345678x".Substring(1, 8));
		M68kRuntime.Collect();
		return values[1].Length + values[5].Length + values[9].Length +
			values[13].Length + values[17].Length + values.Count - 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DictionaryStringGcEntry()
	{
		var values = new Dictionary<string, string>();
		values.Add("a", "1234567");
		values.Add("e", "12345678");
		values.Add("i", "123456789");
		values.Add("m", "1234567890");
		values.Add("q", "12345678");
		var equalKey = "xix".Substring(1, 1);
		if (!values.TryGetValue(equalKey, out var found) || found.Length != 9) return 1;
		if (values.TryGetValue("I", out var missing) || missing is not null) return 2;
		return found.Length + values["a"].Length + values["e"].Length +
			values["m"].Length + values["q"].Length + values.Count - 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DictionaryStringNullKeyEntry()
	{
		var values = new Dictionary<string, int>();
		try
		{
			values.Add(null!, 1);
			return 1;
		}
		catch (ArgumentNullException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DictionaryReferenceFreeStructValueEntry()
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		var one = new DictionaryImageDescriptor(1);
		var five = new DictionaryImageDescriptor(5);
		var nine = new DictionaryImageDescriptor(9);
		var thirteenValue = new DictionaryImageDescriptor(13);
		var seventeen = new DictionaryImageDescriptor(17);
		var replacement = new DictionaryImageDescriptor(19);
		values.Add(1, one);
		values.Add(5, five);
		values.Add(9, nine);
		values.Add(13, thirteenValue);
		values[17] = seventeen;
		values[9] = replacement;

		if (values.Count != 5 || !values[1].Matches(1) ||
			!values[9].Matches(19) || !values[17].Matches(17))
		{
			return 1;
		}
		if (!values.TryGetValue(13, out var thirteen) || !thirteen.Matches(13))
		{
			return 2;
		}
		if (values.TryGetValue(2, out var missing) || !missing.IsDefault())
		{
			return 3;
		}
		return values[5].Matches(5) ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedDictionaryReferenceStructValueEntry()
	{
		var values = new Dictionary<uint, DictionaryReferenceValue>();
		values.Add(1, new DictionaryReferenceValue(null));
		return values.Count;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DictionaryReferenceFreeStructValuesIdentityEntry()
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		var descriptor = new DictionaryImageDescriptor(1);
		values.Add(1, descriptor);
		var first = values.Values;
		var second = values.Values;
		return ReferenceEquals(first, second) ? 42 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedDictionaryReferenceFreeStructKeysEntry()
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		return ReferenceEquals(values.Keys, null) ? 1 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int GenericStringLookupResultEntry()
	{
		var buckets = new int[4];
		var hashes = new int[4];
		var keys = new string[4];
		var key = "i";
		var hash = M68kRuntime.DefaultHashCode(key) & int.MaxValue;
		var bucket = hash & 3;
		buckets[bucket] = 1;
		hashes[0] = hash;
		keys[0] = key;
		var equalKey = "xix".Substring(1, 1);
		return FindGenericCandidate(buckets, hashes, keys, equalKey) == 0 ? 42 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FindGenericCandidate<TKey>(
		int[] buckets,
		int[] hashes,
		TKey[] keys,
		TKey key)
	{
		var hash = M68kRuntime.DefaultHashCode(key) & int.MaxValue;
		var bucket = hash & (buckets.Length - 1);
		for (var probes = 0; probes < buckets.Length; probes++)
		{
			var entry = buckets[bucket];
			if (entry == 0)
			{
				return -1;
			}
			var index = entry - 1;
			if (hashes[index] == hash)
			{
				if (M68kRuntime.DefaultEquals(keys[index], key))
				{
					return index;
				}
			}
			bucket = (bucket + 1) & (buckets.Length - 1);
		}
		return -1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListCapacityMutationEntry()
	{
		try
		{
			_ = new List<int>(-1);
			return 1;
		}
		catch (ArgumentOutOfRangeException)
		{
		}

		var values = new List<int>(2);
		if (values.Capacity != 2)
		{
			return 2;
		}
		values.Add(10);
		values.Add(20);
		values.Add(30);
		if (values.Capacity != 4)
		{
			return 3;
		}
		values.Capacity = 6;
		values.RemoveAt(1);
		var copy = values.ToArray();
		copy[0] = 0;
		if (values.Count != 2 ||
			values.Capacity != 6 ||
			values[0] != 10 ||
			values[1] != 30 ||
			copy.Length != 2 ||
			copy[1] != 30)
		{
			return 4;
		}
		values.Clear();
		if (values.Count != 0 || values.Capacity != 6 || values.ToArray().Length != 0)
		{
			return 5;
		}
		values.Capacity = 0;
		return values.Capacity == 0 ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListMutationRangeExceptionEntry()
	{
		var values = new List<int>(1);
		values.Add(42);
		try
		{
			values.Capacity = 0;
		}
		catch (ArgumentOutOfRangeException)
		{
			try
			{
				values.RemoveAt(-1);
			}
			catch (ArgumentOutOfRangeException)
			{
				try
				{
					values.RemoveAt(values.Count);
				}
				catch (ArgumentOutOfRangeException)
				{
					return values[0];
				}
			}
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListMutationMetricEntry()
	{
		var values = new List<int>(2);
		values.Add(10);
		values.Add(20);
		values.Add(30);
		values.Capacity = 6;
		values.RemoveAt(1);
		var copy = values.ToArray();
		values.Clear();
		return copy[0] + copy[1] + values.Count + 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListDirectEnumerationEntry()
	{
		var values = new List<int>(3);
		values.Add(10);
		values.Add(12);
		values.Add(20);
		var sum = 0;
		foreach (var value in values)
		{
			sum += value;
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListEmptyEnumerationEntry()
	{
		var values = new List<int>();
		var enumerator = values.GetEnumerator();
		if (enumerator.MoveNext() || enumerator.Current != 0)
		{
			return 1;
		}
		enumerator.Dispose();
		return enumerator.MoveNext() ? 2 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListEnumerationMutationEntry()
	{
		var values = new List<int>(2);
		values.Add(19);
		values.Add(23);
		var enumerator = values.GetEnumerator();
		if (!enumerator.MoveNext() || enumerator.Current != 19)
		{
			return 1;
		}
		values[0] = 1;
		try
		{
			_ = enumerator.MoveNext();
		}
		catch (InvalidOperationException)
		{
			return 42;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListEnumerationCapacityEntry()
	{
		var values = new List<int>(2);
		values.Add(19);
		values.Add(23);
		var enumerator = values.GetEnumerator();
		values.Capacity = 8;
		var sum = 0;
		while (enumerator.MoveNext())
		{
			sum += enumerator.Current;
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListNarrowEnumerationEntry()
	{
		var bytes = new List<byte>(2);
		bytes.Add(7);
		bytes.Add(12);
		var words = new List<short>(2);
		words.Add(10);
		words.Add(13);
		var sum = 0;
		foreach (var value in bytes)
		{
			sum += value;
		}
		foreach (var value in words)
		{
			sum += value;
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListInt64EnumerationEntry()
	{
		const long expected = 0x0000_002A_0000_002AL;
		var values = new List<long>(2);
		values.Add(0x0000_0001_0000_0002L);
		values.Add(expected);
		var result = expected;
		var count = 0;
		foreach (var value in values)
		{
			result = value;
			count++;
		}
		return count == 2 ? (int)result : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListReferenceEnumerationGcEntry()
	{
		var values = new List<ListReferenceValue>(2);
		values.Add(new ListReferenceValue(19));
		values.Add(new ListReferenceValue(23));
		var sum = 0;
		foreach (var value in values)
		{
			M68kRuntime.Collect();
			sum += value.Value;
		}
		M68kRuntime.Collect();
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListEnumerationMetricEntry()
	{
		var values = new List<int>(3);
		values.Add(10);
		values.Add(12);
		values.Add(20);
		var sum = 0;
		foreach (var value in values)
		{
			sum += value;
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListInterfaceEnumerationEntry()
	{
		IEnumerable<int> values = new List<int> { 10, 12, 20 };
		var sum = 0;
		foreach (var value in values)
		{
			sum += value;
		}
		return sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListNarrowMutationEntry()
	{
		var bytes = new List<byte>(3);
		bytes.Add(5);
		bytes.Add(7);
		bytes.Add(9);
		bytes.RemoveAt(1);
		if (bytes.Count != 2 || bytes[0] != 5 || bytes[1] != 9)
		{
			return 1;
		}
		bytes.Clear();
		bytes.Add(10);

		var words = new List<short>(2);
		words.Add(12);
		words.Add(15);
		words.RemoveAt(0);
		if (words.Count != 1 || words[0] != 15)
		{
			return 2;
		}
		words.Clear();
		words.Add(32);
		return bytes[0] + words[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long ListInt64MutationEntry()
	{
		const long expected = 0x0000_002A_5566_7788L;
		var values = new List<long>(2);
		values.Add(0x0000_0001_0000_0002L);
		values.Add(expected);
		values.Add(0x0000_0003_0000_0004L);
		values.RemoveAt(0);
		var copy = values.ToArray();
		values.Clear();
		return copy[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long TypedInt64ArrayEntry()
	{
		const long expected = 0x0000_002A_5566_7788L;
		var values = new long[2];
		values[0] = 0x0000_0001_0000_0002L;
		values[1] = expected;
		return values[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListReferenceMutationGcEntry()
	{
		var values = new List<ListReferenceValue>(3);
		values.Add(new ListReferenceValue(19));
		values.Add(new ListReferenceValue(1));
		values.Add(new ListReferenceValue(23));
		values.RemoveAt(1);
		M68kRuntime.Collect();
		var copy = values.ToArray();
		M68kRuntime.Collect();
		var result = copy[0].Value + copy[1].Value;
		values.Clear();
		M68kRuntime.Collect();
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableEnvironmentEntry()
	{
		var newLine = Environment.NewLine;
		return newLine.Length == 1 &&
			newLine[0] == '\n' &&
			Environment.ProcessorCount == 1
			? 42
			: newLine.Length + Environment.ProcessorCount;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableEnvironmentNewLineEntry()
	{
		var newLine = Environment.NewLine;
		return newLine.Length == 1 && newLine[0] == '\n' ? 42 : newLine.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableEnvironmentProcessorCountEntry() =>
		Environment.ProcessorCount == 1 ? 42 : Environment.ProcessorCount;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableStopwatchEntry()
	{
		var frequencyLow = M68kRuntime.SplitInt64(
			System.Diagnostics.Stopwatch.Frequency,
			out var frequencyHigh);
		_ = System.Diagnostics.Stopwatch.GetTimestamp();
		_ = System.Diagnostics.Stopwatch.GetTimestamp();
		return System.Diagnostics.Stopwatch.IsHighResolution &&
			frequencyHigh == 0 &&
			frequencyLow != 0
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long PortableStopwatchTimestampEntry() =>
		System.Diagnostics.Stopwatch.GetTimestamp();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableStopwatchTwoTimestampEntry()
	{
		_ = System.Diagnostics.Stopwatch.GetTimestamp();
		_ = System.Diagnostics.Stopwatch.GetTimestamp();
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long PortableStopwatchFrequencyEntry() =>
		System.Diagnostics.Stopwatch.Frequency;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableStopwatchHighResolutionEntry() =>
		System.Diagnostics.Stopwatch.IsHighResolution ? 42 : 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableStopwatchInstanceEntry()
	{
		var stopwatch = new System.Diagnostics.Stopwatch();
		if (stopwatch.IsRunning)
		{
			return 1;
		}
		var low = M68kRuntime.SplitInt64(stopwatch.ElapsedTicks, out var high);
		if (high != 0 || low != 0)
		{
			return 2;
		}

		stopwatch.Start();
		stopwatch.Start();
		low = M68kRuntime.SplitInt64(stopwatch.ElapsedTicks, out high);
		if (!stopwatch.IsRunning || high != 0 || low != 30)
		{
			return 3;
		}

		stopwatch.Stop();
		stopwatch.Stop();
		low = M68kRuntime.SplitInt64(stopwatch.ElapsedTicks, out high);
		if (stopwatch.IsRunning || high != 0 || low != 60)
		{
			return 4;
		}

		stopwatch.Start();
		stopwatch.Stop();
		low = M68kRuntime.SplitInt64(stopwatch.ElapsedTicks, out high);
		if (high != 0 || low != 102)
		{
			return 5;
		}

		stopwatch.Restart();
		if (!stopwatch.IsRunning)
		{
			return 6;
		}
		stopwatch.Reset();
		low = M68kRuntime.SplitInt64(stopwatch.ElapsedTicks, out high);
		if (stopwatch.IsRunning || high != 0 || low != 0)
		{
			return 7;
		}

		var started = System.Diagnostics.Stopwatch.StartNew();
		started.Stop();
		low = M68kRuntime.SplitInt64(started.ElapsedTicks, out high);
		return !started.IsRunning && high == 0 && low == 42 ? 42 : 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableStopwatchResetOnlyEntry()
	{
		var stopwatch = new System.Diagnostics.Stopwatch();
		stopwatch.Reset();
		return stopwatch.IsRunning ? 0 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendIntEntry()
	{
		var builder = new System.Text.StringBuilder();
		builder.Append(4);
		builder.Append(2);
		return builder.Length == 2 && builder[0] == '4' && builder[1] == '2' ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibListIntEntry()
	{
		var values = new List<int>(2);
		return values.Count == 0 && values.Capacity == 2 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibExceptionToStringCutPointEntry()
	{
		var text = new FixtureException().FormatBase();
		return text.Length == 16 && text[0] == 'S' && text[15] == 'n' ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long PortableStopwatchElapsedValuesEntry()
	{
		var stopwatch = new System.Diagnostics.Stopwatch();
		_ = stopwatch.Elapsed;
		_ = System.Diagnostics.Stopwatch.GetElapsedTime(100, 200);
		_ = System.Diagnostics.Stopwatch.GetElapsedTime(100);
		return stopwatch.ElapsedMilliseconds;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortablePinnedTimeSpanEntry()
	{
		const long ticks = 937_840_050_000;
		var first = new TimeSpan(ticks);
		var second = TimeSpan.FromTicks(ticks + 1);
		var same = first;
		if (first == second) return 1;
		if (first != same) return 2;
		if (first >= second) return 3;
		if (first > second) return 4;
		if (!(first < second)) return 5;
		if (!(first <= second)) return 6;
		if (!(second > first)) return 7;
		if (!(second >= first)) return 8;
		var ticksLow = M68kRuntime.SplitInt64(first.Ticks, out var ticksHigh);
		if (ticksHigh != 0x0000_00da || ticksLow != 0x5b9f_7f50) return 9;
		if (first.Days != 1) return 10;
		if (first.Hours != 2) return 11;
		if (first.Minutes != 3) return 12;
		if (first.Seconds != 4) return 13;
		if (first.Milliseconds != 5) return 14;
		var negative = new TimeSpan(-ticks);
		if (negative.Days != -1) return 15;
		if (negative.Hours != -2) return 16;
		if (negative.Minutes != -3) return 17;
		if (negative.Seconds != -4) return 18;
		return negative.Milliseconds == -5 ? 42 : 19;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableTimeSpanTotalsEntry()
	{
		var low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(864_000_000_000).TotalDays,
			out var high);
		if (high != 0x3ff0_0000 || low != 0) return 1;
		low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(864_000_000_000).TotalHours,
			out high);
		if (high != 0x4038_0000 || low != 0) return 2;
		low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(864_000_000_000).TotalMinutes,
			out high);
		if (high != 0x4096_8000 || low != 0) return 3;
		low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(864_000_000_000).TotalSeconds,
			out high);
		if (high != 0x40f5_1800 || low != 0) return 4;
		low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(864_000_000_000).TotalMilliseconds,
			out high);
		if (high != 0x4194_9970 || low != 0) return 5;
		low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(long.MaxValue).TotalMilliseconds,
			out high);
		if (high != 0x430a_36e2 || low != 0xeb1c_4328) return 6;
		low = M68kRuntime.SplitDouble(
			TimeSpan.FromTicks(long.MinValue).TotalMilliseconds,
			out high);
		if (high != 0xc30a_36e2 || low != 0xeb1c_4328) return 7;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableStopwatchInvalidOperationEntry()
	{
		try
		{
			_ = System.Diagnostics.Stopwatch.GetTimestamp();
			return 1;
		}
		catch (InvalidOperationException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListReferenceClearReclaimsEntry()
	{
		var values = new List<ListReferenceValue>(1);
		values.Add(new ListReferenceValue(1));
		values.Clear();
		M68kRuntime.Collect();
		values.Add(new ListReferenceValue(42));
		return values[0].Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListReferenceRemoveAtReclaimsEntry()
	{
		var values = new List<ListReferenceValue>(2);
		values.Add(new ListReferenceValue(19));
		values.Add(new ListReferenceValue(1));
		values.RemoveAt(1);
		M68kRuntime.Collect();
		values.Add(new ListReferenceValue(23));
		return values[0].Value + values[1].Value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListReferenceRetentionControlEntry()
	{
		try
		{
			var values = new List<ListReferenceValue>(1);
			values.Add(new ListReferenceValue(1));
			M68kRuntime.Collect();
			values.Add(new ListReferenceValue(42));
			return 0;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
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
	public static int ListFloatingEqualityEntry()
	{
		var singles = new List<float>(3);
		singles.Add(float.NaN);
		singles.Add(0.0f);
		singles.Add(1.5f);
		if (!singles.Contains(-float.NaN) ||
			!singles.Contains(-0.0f) ||
			singles.IndexOf(-0.0f) != 1 ||
			singles.Contains(float.PositiveInfinity) ||
			!singles.Remove(-float.NaN) ||
			singles.Count != 2)
		{
			return 1;
		}

		var doubles = new List<double>(3);
		doubles.Add(double.NaN);
		doubles.Add(0.0d);
		doubles.Add(1.5d);
		if (!doubles.Contains(-double.NaN) ||
			!doubles.Contains(-0.0d) ||
			doubles.IndexOf(-0.0d) != 1 ||
			doubles.Contains(double.NegativeInfinity) ||
			!doubles.Remove(-double.NaN) ||
			doubles.Count != 2)
		{
			return 2;
		}

		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListInt32EqualityEntry()
	{
		var values = new List<int>(4);
		values.Add(10);
		values.Add(20);
		values.Add(10);
		values.Add(12);
		if (!values.Contains(20) || values.Contains(99) || values.IndexOf(10) != 0)
		{
			return 1;
		}

		var stable = values.GetEnumerator();
		if (!stable.MoveNext() || values.Remove(99) ||
			!stable.MoveNext() || stable.Current != 20)
		{
			return 2;
		}

		var invalidated = values.GetEnumerator();
		if (!invalidated.MoveNext() || !values.Remove(10))
		{
			return 3;
		}
		try
		{
			invalidated.MoveNext();
			return 4;
		}
		catch (InvalidOperationException)
		{
		}

		return values.Count == 3 && values.IndexOf(10) == 1 &&
			values.Contains(12) ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListInt64EqualityEntry()
	{
		const long first = 0x0000_0001_0000_0002L;
		const long highDiffers = 0x0000_0003_0000_0002L;
		const long lowDiffers = 0x0000_0001_0000_0004L;
		var values = new List<long>(3);
		values.Add(first);
		values.Add(highDiffers);
		values.Add(lowDiffers);
		if (!values.Contains(highDiffers))
		{
			return 1;
		}
		if (values.IndexOf(lowDiffers) != 2)
		{
			return 2;
		}
		if (values.Contains(0x0000_0003_0000_0004L))
		{
			return 3;
		}
		if (!values.Remove(first))
		{
			return 4;
		}
		if (values.Count != 2)
		{
			return 5;
		}
		if (values.IndexOf(highDiffers) != 0)
		{
			return 6;
		}
		return values.IndexOf(lowDiffers) == 1 ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListNarrowIntegralEqualityEntry()
	{
		var bytes = new List<byte>(1);
		bytes.Add(0xA5);
		var signed = new List<sbyte>(1);
		signed.Add(-42);
		var chars = new List<char>(1);
		chars.Add('\u03A9');
		var shorts = new List<short>(1);
		shorts.Add(-1234);
		var ushorts = new List<ushort>(1);
		ushorts.Add(54321);
		var booleans = new List<bool>(1);
		booleans.Add(true);
		return bytes.Contains(0xA5) && signed.IndexOf(-42) == 0 &&
			chars.Contains('\u03A9') && shorts.Remove(-1234) &&
			ushorts.Contains(54321) && booleans.Contains(true) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListIntegralEqualityMetricEntry()
	{
		var values = new List<int>(4);
		values.Add(10);
		values.Add(20);
		values.Add(10);
		return values.Contains(20) && values.IndexOf(10) == 0 &&
			values.Remove(10) && values.Count == 2 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListNullableIntEqualityEntry()
	{
		var values = new List<int?>(4);
		values.Add(null);
		values.Add(42);
		values.Add(null);
		values.Add(-7);
		if (!values.Contains(null))
		{
			return 1;
		}
		if (values.IndexOf(null) != 0)
		{
			return 2;
		}
		if (!values.Contains(42))
		{
			return 3;
		}
		if (values.Contains(7))
		{
			return 4;
		}
		if (!values.Remove(null))
		{
			return 5;
		}
		if (values.IndexOf(null) != 1)
		{
			return 6;
		}
		if (!values.Remove(42))
		{
			return 7;
		}
		return values.Count == 2 ? 42 : 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListByteEnumEqualityEntry()
	{
		var values = new List<ListByteState>(3);
		values.Add(ListByteState.First);
		values.Add(ListByteState.Second);
		values.Add(ListByteState.First);
		return values.Contains(ListByteState.Second) &&
			!values.Contains(ListByteState.Missing) &&
			values.IndexOf(ListByteState.First) == 0 &&
			values.Remove(ListByteState.First) &&
			values.IndexOf(ListByteState.First) == 1 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListIntEnumEqualityEntry()
	{
		var values = new List<ListIntState>(3);
		values.Add(ListIntState.First);
		values.Add(ListIntState.Second);
		values.Add(ListIntState.First);
		return values.Contains(ListIntState.Second) &&
			!values.Contains(ListIntState.Missing) &&
			values.IndexOf(ListIntState.First) == 0 &&
			values.Remove(ListIntState.First) &&
			values.IndexOf(ListIntState.First) == 1 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListLongEnumEqualityEntry()
	{
		var values = new List<ListLongState>(3);
		values.Add(ListLongState.First);
		values.Add(ListLongState.HighDiffers);
		values.Add(ListLongState.LowDiffers);
		if (!values.Contains(ListLongState.HighDiffers))
		{
			return 1;
		}
		if (values.Contains(ListLongState.Missing))
		{
			return 2;
		}
		if (values.IndexOf(ListLongState.LowDiffers) != 2)
		{
			return 3;
		}
		if (!values.Remove(ListLongState.First))
		{
			return 4;
		}
		return values.IndexOf(ListLongState.HighDiffers) == 0 &&
			values.IndexOf(ListLongState.LowDiffers) == 1 ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListExternalEnumEqualityEntry()
	{
		var values = new List<ExternalListState>(2);
		values.Add(ExternalListState.First);
		values.Add(ExternalListState.Second);
		return values.Contains(ExternalListState.Second) &&
			!values.Contains(ExternalListState.Missing) &&
			values.Remove(ExternalListState.First) &&
			values.IndexOf(ExternalListState.Second) == 0 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListStringEqualityEntry()
	{
		var equalContent = "xAmigax".Substring(1, 5);
		var values = new List<string?>(4);
		values.Add("Amiga");
		values.Add(null);
		values.Add("Amiga");
		values.Add("Workbench");
		if (!values.Contains(equalContent))
		{
			return 1;
		}
		if (values.IndexOf(equalContent) != 0)
		{
			return 2;
		}
		if (!values.Contains(null))
		{
			return 3;
		}
		if (values.Contains("amiga"))
		{
			return 4;
		}
		if (values.Contains("missing"))
		{
			return 5;
		}
		if (!values.Remove(equalContent))
		{
			return 6;
		}
		if (values.IndexOf("Amiga") != 1)
		{
			return 7;
		}
		if (!values.Remove(null))
		{
			return 8;
		}
		if (values.Contains(null))
		{
			return 9;
		}
		return values.Count == 2 ? 42 : 10;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListStringEqualityMetricEntry()
	{
		var values = new List<string?>(4);
		values.Add("alpha");
		values.Add("beta");
		values.Add(null);
		return values.Contains("beta") && values.IndexOf("alpha") == 0 &&
			values.Remove(null) && values.Count == 2 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListSealedReferenceFallbackEqualityEntry()
	{
		var first = new RuntimeObjectEqualsFallbackSource();
		var same = first;
		var different = new RuntimeObjectEqualsFallbackSource();
		var values = new List<RuntimeObjectEqualsFallbackSource?>(4);
		values.Add(first);
		values.Add(null);
		values.Add(same);
		if (!values.Contains(same))
		{
			return 1;
		}
		if (values.IndexOf(same) != 0)
		{
			return 2;
		}
		if (values.Contains(different))
		{
			return 3;
		}
		if (!values.Contains(null))
		{
			return 4;
		}
		if (!values.Remove(same))
		{
			return 5;
		}
		if (values.IndexOf(same) != 1)
		{
			return 6;
		}
		if (!values.Remove(null))
		{
			return 7;
		}
		if (values.Contains(null))
		{
			return 8;
		}
		return values.Count == 1 ? 42 : 9;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListSealedReferenceOverrideEqualityEntry()
	{
		var first = new RuntimeObjectEqualsDerived { Value = 7 };
		var equal = new RuntimeObjectEqualsDerived { Value = 7 };
		var different = new RuntimeObjectEqualsDerived { Value = 8 };
		var values = new List<RuntimeObjectEqualsDerived?>(4);
		values.Add(first);
		values.Add(null);
		values.Add(first);
		if (!values.Contains(equal))
		{
			return 1;
		}
		if (values.IndexOf(equal) != 0)
		{
			return 2;
		}
		if (values.Contains(different))
		{
			return 3;
		}
		if (!values.Contains(null))
		{
			return 4;
		}
		if (!values.Remove(equal))
		{
			return 5;
		}
		if (values.IndexOf(first) != 1)
		{
			return 6;
		}
		return values.Count == 2 ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ListSealedEquatableReferenceEntry()
	{
		var first = new RuntimeEquatableOnly(42);
		var equal = new RuntimeEquatableOnly(42);
		var different = new RuntimeEquatableOnly(7);
		var values = new List<RuntimeEquatableOnly?>(3);
		values.Add(first);
		values.Add(null);
		if (!values.Contains(equal))
		{
			return 1;
		}
		if (values.Contains(different))
		{
			return 2;
		}
		if (!values.Contains(null))
		{
			return 3;
		}
		if (!values.Remove(equal))
		{
			return 4;
		}
		if (!values.Remove(null))
		{
			return 5;
		}
		return values.Count == 0 ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedListNonSealedReferenceEntry()
	{
		var values = new List<RuntimeObjectEqualsBase>();
		values.Add(new RuntimeObjectEqualsBase());
		return values.Contains(new RuntimeObjectEqualsBase()) ? 1 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PublicIntegralEqualityComparerEntry()
	{
		var firstInt = EqualityComparer<int>.Default;
		var collectionTrigger = new int[1];
		collectionTrigger[0] = 42;
		var secondInt = EqualityComparer<int>.Default;
		if (collectionTrigger[0] != 42) return 1;
		if (!ReferenceEquals(firstInt, secondInt)) return 2;
		var longs = EqualityComparer<long>.Default;
		var bytes = EqualityComparer<byte>.Default;
		var longEnums = EqualityComparer<ListLongState>.Default;
		var thirdInt = EqualityComparer<int>.Default;
		if (!ReferenceEquals(firstInt, thirdInt)) return 10;
		IEqualityComparer<int> throughInterface = firstInt;
		if (!throughInterface.Equals(31, 31) ||
			throughInterface.Equals(31, 32) ||
			throughInterface.GetHashCode(31) != 31) return 14;
		if (ReferenceEquals(firstInt, longs)) return 3;
		if (!firstInt.Equals(19, 19)) return 4;
		if (firstInt.Equals(19, 23)) return 5;
		if (firstInt.GetHashCode(19) != 19) return 11;
		if (!longs.Equals(0x0000_002A_5566_7788L, 0x0000_002A_5566_7788L)) return 6;
		if (longs.Equals(0x0000_002A_5566_7788L, 0x0000_002B_5566_7788L)) return 7;
		if (longs.GetHashCode(0x0000_002A_5566_7788L) !=
			unchecked((int)0x5566_77A2)) return 12;
		if (!bytes.Equals(0x7B, 0x7B)) return 8;
		if (bytes.Equals(0x7B, 0x7C)) return 9;
		if (bytes.GetHashCode(0x7B) != 0x7B) return 13;
		return longEnums.Equals(ListLongState.First, ListLongState.First) &&
			!longEnums.Equals(ListLongState.First, ListLongState.HighDiffers) &&
			longEnums.GetHashCode(ListLongState.First) == 3
			? 42
			: 15;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PublicFloatingEqualityComparerEntry()
	{
		var singles = EqualityComparer<float>.Default;
		var doubles = EqualityComparer<double>.Default;
		if (!ReferenceEquals(singles, EqualityComparer<float>.Default)) return 1;
		if (!singles.Equals(float.NaN, -float.NaN)) return 2;
		if (!singles.Equals(0.0f, -0.0f)) return 3;
		if (singles.GetHashCode(float.NaN) != singles.GetHashCode(-float.NaN)) return 4;
		if (singles.GetHashCode(0.0f) != singles.GetHashCode(-0.0f)) return 5;
		if (singles.GetHashCode(1.5f) != unchecked((int)0x3FC0_0000)) return 6;
		if (!doubles.Equals(double.NaN, -double.NaN)) return 7;
		if (!doubles.Equals(0.0d, -0.0d)) return 8;
		if (doubles.GetHashCode(double.NaN) != doubles.GetHashCode(-double.NaN)) return 9;
		if (doubles.GetHashCode(0.0d) != doubles.GetHashCode(-0.0d)) return 10;
		if (doubles.GetHashCode(1.5d) != 0x3FF8_0000) return 11;
		IEqualityComparer<double> throughInterface = doubles;
		return throughInterface.Equals(1.5d, 1.5d) &&
			throughInterface.GetHashCode(1.5d) == 0x3FF8_0000
			? 42
			: 12;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PublicStringEqualityComparerEntry()
	{
		var comparer = EqualityComparer<string?>.Default;
		var equalContent = "xAmigax".Substring(1, 5);
		if (!ReferenceEquals(comparer, EqualityComparer<string?>.Default)) return 1;
		if (!comparer.Equals("Amiga", equalContent)) return 2;
		if (comparer.Equals("Amiga", "amiga")) return 3;
		if (comparer.GetHashCode("Amiga") != comparer.GetHashCode(equalContent)) return 4;
		if (comparer.GetHashCode(null!) != 0) return 5;
		IEqualityComparer<string?> throughInterface = comparer;
		return throughInterface.Equals("Amiga", equalContent) &&
			throughInterface.GetHashCode("Amiga") == comparer.GetHashCode(equalContent)
			? 42
			: 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PublicNullableIntEqualityComparerEntry()
	{
		var comparer = EqualityComparer<int?>.Default;
		int? empty = null;
		int? first = 19;
		int? second = 19;
		int? different = 23;
		if (!ReferenceEquals(comparer, EqualityComparer<int?>.Default)) return 1;
		if (!comparer.Equals(empty, empty)) return 2;
		if (comparer.Equals(empty, first)) return 3;
		if (!comparer.Equals(first, second)) return 4;
		if (comparer.Equals(first, different)) return 5;
		if (comparer.GetHashCode(empty!) != 0) return 6;
		if (comparer.GetHashCode(first) != 19) return 7;
		IEqualityComparer<int?> throughInterface = comparer;
		return throughInterface.Equals(first, second) &&
			throughInterface.GetHashCode(first) == 19
			? 42
			: 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PublicSealedReferenceEqualityComparerEntry()
	{
		var comparer = EqualityComparer<RuntimeObjectEqualsDerived?>.Default;
		var collectionTrigger = new int[1];
		collectionTrigger[0] = 42;
		var secondComparer = EqualityComparer<RuntimeObjectEqualsDerived?>.Default;
		var first = new RuntimeObjectEqualsDerived { Value = 19 };
		var equal = new RuntimeObjectEqualsDerived { Value = 19 };
		var different = new RuntimeObjectEqualsDerived { Value = 23 };
		if (collectionTrigger[0] != 42) return 1;
		if (!ReferenceEquals(comparer, secondComparer)) return 2;
		if (!comparer.Equals(first, equal)) return 3;
		if (comparer.Equals(first, different)) return 4;
		if (!comparer.Equals(null, null)) return 5;
		if (comparer.Equals(first, null)) return 6;
		if (comparer.Equals(null, first)) return 6;
		if (comparer.GetHashCode(null!) != 0) return 7;
		if (comparer.GetHashCode(first) != 19) return 8;
		if (comparer.GetHashCode(equal) != 19) return 8;
		IEqualityComparer<RuntimeObjectEqualsDerived?> throughInterface = comparer;
		if (!throughInterface.Equals(first, equal)) return 9;
		return throughInterface.GetHashCode(first) == 19 ? 42 : 10;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PublicSealedEquatableEqualityComparerEntry()
	{
		var comparer = EqualityComparer<RuntimeEquatableOnly?>.Default;
		var collectionTrigger = new int[1];
		collectionTrigger[0] = 42;
		var secondComparer = EqualityComparer<RuntimeEquatableOnly?>.Default;
		var first = new RuntimeEquatableOnly(19);
		var equal = new RuntimeEquatableOnly(19);
		var different = new RuntimeEquatableOnly(23);
		if (collectionTrigger[0] != 42) return 1;
		if (!ReferenceEquals(comparer, secondComparer)) return 2;
		if (!comparer.Equals(first, equal)) return 3;
		if (comparer.Equals(first, different)) return 4;
		if (!comparer.Equals(null, null)) return 5;
		if (comparer.Equals(first, null)) return 6;
		if (comparer.Equals(null, first)) return 6;
		if (comparer.GetHashCode(null!) != 0) return 7;
		if (comparer.GetHashCode(first) != 19) return 8;
		if (comparer.GetHashCode(equal) != 19) return 8;
		IEqualityComparer<RuntimeEquatableOnly?> throughInterface = comparer;
		if (!throughInterface.Equals(first, equal)) return 9;
		return throughInterface.GetHashCode(first) == 19 ? 42 : 10;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedNonSealedReferenceEqualityComparerEntry()
	{
		var comparer = EqualityComparer<RuntimeObjectEqualsBase?>.Default;
		return comparer.Equals(null, null) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleWriteEntry()
	{
		Console.Write("A\0");
		Console.WriteLine("B\u00E4");
		Console.WriteLine((string?)null);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsolePrimitiveEntry()
	{
		Console.Write(int.MinValue);
		Console.Write("|");
		Console.WriteLine(uint.MaxValue);
		Console.WriteLine(-42);
		Console.Write(42u);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleInt64Entry()
	{
		Console.Write(long.MinValue);
		Console.Write("|");
		Console.WriteLine(ulong.MaxValue);
		Console.WriteLine(-42L);
		Console.Write(42UL);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleBooleanEntry()
	{
		Console.Write(true);
		Console.WriteLine(false);
		Console.WriteLine(true);
		Console.Write(false);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleCharacterEntry()
	{
		Console.Write('\0');
		Console.Write('\u00e4');
		Console.WriteLine('\u0100');
		Console.Write('A');
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleReadEntry()
	{
		if (Console.Read() != 'A') return 1;
		if (Console.Read() != 0) return 2;
		if (Console.Read() != '\u00e4') return 3;
		return Console.Read() == -1 ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleReadLineEntry()
	{
		if (Console.ReadLine() != "A\0\u00e4") return 1;
		if (Console.ReadLine() != "B") return 2;
		if (Console.ReadLine() != "") return 3;
		if (Console.ReadLine() != "C") return 4;
		return Console.ReadLine() is null ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleInputIOExceptionEntry()
	{
		try
		{
			_ = Console.Read();
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleInputAllocationFailureEntry()
	{
		try
		{
			_ = Console.Read();
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemExistsEntry()
	{
		if (!File.Exists(".coppersharp-portable-file")) return 1;
		if (File.Exists(".coppersharp-portable-directory")) return 2;
		if (Directory.Exists(".coppersharp-portable-file")) return 3;
		if (!Directory.Exists(".coppersharp-portable-directory")) return 4;
		if (File.Exists(null)) return 5;
		if (Directory.Exists("")) return 6;
		if (File.Exists("bad\0path")) return 7;
		if (Directory.Exists("bad\u0100path")) return 8;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemExistsAllocationFailureEntry()
	{
		try
		{
			_ = File.Exists(".coppersharp-portable-file");
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemMissingEntry() =>
		File.Exists(".coppersharp-portable-missing") ? 1 : 42;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteEntry()
	{
		File.Delete(".coppersharp-portable-file");
		Directory.Delete(".coppersharp-portable-directory");
		File.Delete(".coppersharp-portable-missing");
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteUnhandledDirectoryNotFoundEntry()
	{
		Directory.Delete(".coppersharp-portable-missing");
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteUnhandledUnauthorizedEntry()
	{
		File.Delete(".coppersharp-portable-directory");
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteUnhandledIOExceptionEntry()
	{
		Directory.Delete(".coppersharp-portable-file");
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteInvalidPathEntry()
	{
		var caught = 0;
		try { File.Delete(null!); }
		catch (ArgumentNullException) { caught++; }
		try { Directory.Delete(""); }
		catch (ArgumentException) { caught++; }
		try { File.Delete("bad\0path"); }
		catch (ArgumentException) { caught++; }
		try { Directory.Delete("bad\u0100path"); }
		catch (ArgumentException) { caught++; }
		return caught == 4 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteDirectoryNotFoundEntry()
	{
		var caught = 0;
		try { Directory.Delete(".coppersharp-portable-missing"); }
		catch (DirectoryNotFoundException) { caught++; }
		try { Directory.Delete(".coppersharp-portable-missing"); }
		catch (IOException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteUnauthorizedEntry()
	{
		var caught = 0;
		try { File.Delete(".coppersharp-portable-directory"); }
		catch (UnauthorizedAccessException) { caught++; }
		try { File.Delete(".coppersharp-portable-directory"); }
		catch (SystemException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteIOExceptionEntry()
	{
		try
		{
			Directory.Delete(".coppersharp-portable-directory");
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteProtectedEntry()
	{
		try
		{
			File.Delete(".coppersharp-portable-file");
			return 1;
		}
		catch (UnauthorizedAccessException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSystemDeleteAllocationFailureEntry()
	{
		try
		{
			File.Delete(".coppersharp-portable-file");
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveEntry()
	{
		Directory.Move(
			".coppersharp-portable-directory-source",
			".coppersharp-portable-directory-destination");
		Directory.Move(
			".coppersharp-portable-file-source",
			".coppersharp-portable-file-destination");
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveInvalidPathEntry()
	{
		var caught = 0;
		try { Directory.Move(null!, "destination"); }
		catch (ArgumentNullException) { caught++; }
		try { Directory.Move("source", null!); }
		catch (ArgumentNullException) { caught++; }
		try { Directory.Move("", "destination"); }
		catch (ArgumentException) { caught++; }
		try { Directory.Move("source", "bad\0path"); }
		catch (ArgumentException) { caught++; }
		try { Directory.Move("bad\u0100path", "destination"); }
		catch (ArgumentException) { caught++; }
		return caught == 5 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveSamePathEntry()
	{
		try
		{
			Directory.Move("same", "same");
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveDirectoryNotFoundEntry()
	{
		var caught = 0;
		try { Directory.Move("missing", "destination"); }
		catch (DirectoryNotFoundException) { caught++; }
		try { Directory.Move("missing", "destination"); }
		catch (IOException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveUnauthorizedEntry()
	{
		var caught = 0;
		try { Directory.Move("source", "destination"); }
		catch (UnauthorizedAccessException) { caught++; }
		try { Directory.Move("source", "destination"); }
		catch (SystemException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveIOExceptionEntry()
	{
		try
		{
			Directory.Move("source", "destination");
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableDirectoryMoveOutOfMemoryEntry()
	{
		try
		{
			Directory.Move("source", "destination");
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesEntry()
	{
		var file = File.GetAttributes(".coppersharp-portable-file");
		var directory = File.GetAttributes(".coppersharp-portable-directory");
		return file == (FileAttributes.ReadOnly | FileAttributes.Archive) &&
			directory == FileAttributes.Directory
			? 42
			: (int)file + (int)directory;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesInvalidPathEntry()
	{
		var caught = 0;
		try { _ = File.GetAttributes((string)null!); }
		catch (ArgumentNullException) { caught++; }
		try { _ = File.GetAttributes(""); }
		catch (ArgumentException) { caught++; }
		try { _ = File.GetAttributes("bad\0path"); }
		catch (ArgumentException) { caught++; }
		try { _ = File.GetAttributes("bad\u0100path"); }
		catch (ArgumentException) { caught++; }
		return caught == 4 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesFileNotFoundEntry()
	{
		var caught = 0;
		try { _ = File.GetAttributes(".coppersharp-portable-missing"); }
		catch (FileNotFoundException) { caught++; }
		try { _ = File.GetAttributes(".coppersharp-portable-missing"); }
		catch (IOException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesDirectoryNotFoundEntry()
	{
		var caught = 0;
		try { _ = File.GetAttributes("missing/file"); }
		catch (DirectoryNotFoundException) { caught++; }
		try { _ = File.GetAttributes("missing/file"); }
		catch (IOException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesUnauthorizedEntry()
	{
		var caught = 0;
		try { _ = File.GetAttributes(".coppersharp-portable-file"); }
		catch (UnauthorizedAccessException) { caught++; }
		try { _ = File.GetAttributes(".coppersharp-portable-file"); }
		catch (SystemException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesIOExceptionEntry()
	{
		try
		{
			_ = File.GetAttributes(".coppersharp-portable-file");
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesOutOfMemoryEntry()
	{
		try
		{
			_ = File.GetAttributes(".coppersharp-portable-file");
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileGetAttributesUnhandledFileNotFoundEntry()
	{
		_ = File.GetAttributes(".coppersharp-portable-missing");
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesEntry()
	{
		File.SetAttributes(
			".coppersharp-portable-file",
			FileAttributes.ReadOnly | FileAttributes.Archive);
		File.SetAttributes(
			".coppersharp-portable-file",
			FileAttributes.Normal);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesInvalidEntry()
	{
		var caught = 0;
		try { File.SetAttributes((string)null!, FileAttributes.Normal); }
		catch (ArgumentNullException) { caught++; }
		try { File.SetAttributes("", FileAttributes.Normal); }
		catch (ArgumentException) { caught++; }
		try { File.SetAttributes("bad\0path", FileAttributes.Normal); }
		catch (ArgumentException) { caught++; }
		try { File.SetAttributes("bad\u0100path", FileAttributes.Normal); }
		catch (ArgumentException) { caught++; }
		try { File.SetAttributes("valid", (FileAttributes)8); }
		catch (ArgumentException) { caught++; }
		return caught == 5 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesKnownUnsupportedFlagsEntry()
	{
		File.SetAttributes(
			".coppersharp-portable-file",
			FileAttributes.Hidden |
				FileAttributes.System |
				FileAttributes.Directory |
				FileAttributes.ReparsePoint);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesFileNotFoundEntry()
	{
		var caught = 0;
		try { File.SetAttributes(".coppersharp-portable-missing", FileAttributes.Normal); }
		catch (FileNotFoundException) { caught++; }
		try { File.SetAttributes(".coppersharp-portable-missing", FileAttributes.Normal); }
		catch (IOException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesDirectoryNotFoundEntry()
	{
		var caught = 0;
		try { File.SetAttributes("missing/file", FileAttributes.Normal); }
		catch (DirectoryNotFoundException) { caught++; }
		try { File.SetAttributes("missing/file", FileAttributes.Normal); }
		catch (IOException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesUnauthorizedEntry()
	{
		var caught = 0;
		try { File.SetAttributes(".coppersharp-portable-file", FileAttributes.ReadOnly); }
		catch (UnauthorizedAccessException) { caught++; }
		try { File.SetAttributes(".coppersharp-portable-file", FileAttributes.ReadOnly); }
		catch (SystemException) { caught += 2; }
		return caught == 3 ? 42 : caught;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesIOExceptionEntry()
	{
		try
		{
			File.SetAttributes(".coppersharp-portable-file", FileAttributes.ReadOnly);
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableFileSetAttributesOutOfMemoryEntry()
	{
		try
		{
			File.SetAttributes(".coppersharp-portable-file", FileAttributes.ReadOnly);
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedFileMoveEntry()
	{
		File.Move(".coppersharp-portable-file", ".coppersharp-portable-file-moved");
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedDirectoryCreateEntry()
	{
		_ = Directory.CreateDirectory(".coppersharp-portable-directory");
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedConsoleInEntry() => Console.In.Read();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedConsoleOutEntry()
	{
		Console.Out.WriteLine("unsupported");
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedConsoleErrorEntry()
	{
		Console.Error.WriteLine("unsupported");
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedConsoleInputEncodingEntry() =>
		Console.InputEncoding.CodePage;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedConsoleOutputEncodingEntry() =>
		Console.OutputEncoding.CodePage;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleCharacterAllocationFailureEntry()
	{
		try
		{
			Console.Write('A');
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleBooleanShortWriteEntry()
	{
		try
		{
			Console.Write(true);
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsolePrimitiveAllocationFailureEntry()
	{
		try
		{
			Console.WriteLine(42);
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleInt64AllocationFailureEntry()
	{
		try
		{
			Console.WriteLine(ulong.MaxValue);
			return 1;
		}
		catch (OutOfMemoryException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleOpenFailureEntry()
	{
		try
		{
			Console.WriteLine("unavailable");
			return 1;
		}
		catch (IOException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleStartupArgsEntry(
		int argLength,
		global::Amiga.CONST_STRPTR argText)
	{
		Console.WriteLine((string?)null);
		return argLength == 17 && argText.Raw == 0x0000_1800 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PortableConsoleUnhandledFailureEntry()
	{
		Console.WriteLine("unhandled");
		return 1;
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
	public static int ArrayAlgorithmsEntry()
	{
		var firstEmpty = Array.Empty<int>();
		var secondEmpty = Array.Empty<int>();
		if (firstEmpty.Length != 0 || !ReferenceEquals(firstEmpty, secondEmpty))
		{
			return 1;
		}

		var values = new int[6];
		values[0] = 1;
		values[1] = 2;
		values[2] = 3;
		values[3] = 2;
		values[4] = 5;
		values[5] = 2;
		Array.Fill(values, 7, 1, 2);
		if (Array.IndexOf(values, 2) != 3 ||
			Array.IndexOf(values, 7, 2) != 2 ||
			Array.IndexOf(values, 2, 4, 2) != 5 ||
			Array.LastIndexOf(values, 7) != 2 ||
			Array.LastIndexOf(values, 7, 1) != 1 ||
			Array.LastIndexOf(values, 7, 2, 2) != 2)
		{
			return 2;
		}

		Array.Reverse(values, 1, 4);
		Array.Reverse(values);
		if (values[0] != 2 || values[1] != 7 || values[2] != 7 ||
			values[3] != 2 || values[4] != 5 || values[5] != 1)
		{
			return 3;
		}

		Array.Fill(values, 4);
		for (var index = 0; index < values.Length; index++)
		{
			if (values[index] != 4)
			{
				return 4;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArrayFloatingEqualityEntry()
	{
		var values = new float[4];
		values[0] = 1.0f;
		values[1] = float.NaN;
		values[2] = 2.0f;
		values[3] = float.NaN;
		return Array.IndexOf(values, float.NaN) == 1 &&
			Array.LastIndexOf(values, float.NaN) == 3
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArrayFloatingEmptySearchEntry()
	{
		var values = Array.Empty<float>();
		return Array.IndexOf(values, float.NaN) == -1 &&
			Array.LastIndexOf(values, float.NaN) == -1
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArrayAlgorithmsExceptionEntry()
	{
		var caught = 0;
		try { Array.Fill<int>(null!, 1); }
		catch (ArgumentNullException) { caught++; }
		try { Array.Fill(new int[2], 0, -1, 1); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = Array.IndexOf(new int[2], 0, 0, 3); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { _ = Array.LastIndexOf(Array.Empty<int>(), 0, 1, 0); }
		catch (ArgumentOutOfRangeException) { caught++; }
		try { Array.Reverse(new int[2], 1, 2); }
		catch (ArgumentException) { caught++; }
		return caught == 5 ? 42 : caught;
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
	public static int UnsignedWordNormalizationChainEntry()
	{
		var first = (ushort)DirtyUnsignedWordSource();
		var second = first;
		var third = second;
		if (DirtyNarrowCondition() != 0)
		{
			third = 7;
		}

		ushort increment = 3;
		return (ushort)(third + increment);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint DirtyUnsignedWordSource() => 0xABCD_FFFEu;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsignedByteNormalizationChainEntry()
	{
		var first = (byte)DirtyUnsignedByteSource();
		var second = first;
		var third = second;
		if (DirtyNarrowCondition() != 0)
		{
			third = 7;
		}

		byte increment = 3;
		return (byte)(third + increment);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SignedByteNormalizationChainEntry()
	{
		var first = (sbyte)DirtySignedByteSource();
		var second = first;
		var third = second;
		if (DirtyNarrowCondition() != 0)
		{
			third = 7;
		}

		sbyte increment = -1;
		return (sbyte)(third + increment);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int SignedWordNormalizationChainEntry()
	{
		var first = (short)DirtySignedWordSource();
		var second = first;
		var third = second;
		if (DirtyNarrowCondition() != 0)
		{
			third = 7;
		}

		short increment = -1;
		return (short)(third + increment);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint DirtyUnsignedByteSource() => 0xABCD_00FEu;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint DirtySignedByteSource() => 0x1234_0080u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint DirtySignedWordSource() => 0xABCD_8000u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DirtyNarrowCondition() => 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowArrayNormalizationEntry()
	{
		var bytes = new byte[1];
		var words = new ushort[1];
		var signedBytes = new sbyte[1];
		var signedWords = new short[1];

		bytes[0] = (byte)DirtyUnsignedByteSource();
		words[0] = (ushort)DirtyUnsignedWordSource();
		signedBytes[0] = (sbyte)DirtySignedByteSource();
		signedWords[0] = (short)DirtySignedWordSource();

		byte byteIncrement = 3;
		ushort wordIncrement = 3;
		sbyte signedByteIncrement = -1;
		short signedWordIncrement = -1;
		bytes[0] = (byte)(bytes[0] + byteIncrement);
		words[0] = (ushort)(words[0] + wordIncrement);
		signedBytes[0] = (sbyte)(signedBytes[0] + signedByteIncrement);
		signedWords[0] = (short)(signedWords[0] + signedWordIncrement);

		return bytes[0] + words[0] + signedBytes[0] + signedWords[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowFrameAndSpillNormalizationEntry()
	{
		var a = (ushort)DirtySpillSource(1);
		var b = (ushort)DirtySpillSource(2);
		var c = (ushort)DirtySpillSource(3);
		var d = (ushort)DirtySpillSource(4);
		var e = (ushort)DirtySpillSource(5);
		var f = (ushort)DirtySpillSource(6);
		var g = (ushort)DirtySpillSource(7);
		var h = (ushort)DirtySpillSource(8);

		var ab = (ushort)(a + b);
		var cd = (ushort)(c + d);
		var ef = (ushort)(e + f);
		var gh = (ushort)(g + h);
		return (ushort)((ushort)(ab + cd) + (ushort)(ef + gh));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint DirtySpillSource(int value) =>
		0xABCD_0000u | (uint)value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowCallBoundaryEntry() =>
		AcceptUnsignedWord((ushort)DirtyUnsignedWordSource());

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AcceptUnsignedWord(ushort value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowStackArgumentBoundaryEntry() =>
		AcceptStackUnsignedWord(
			1,
			2,
			3,
			4,
			5,
			6,
			(ushort)DirtyUnsignedWordSource());

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AcceptStackUnsignedWord(
		int first,
		int second,
		int third,
		int fourth,
		int fifth,
		int sixth,
		ushort value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowReturnBoundaryEntry() => ReturnDirtyUnsignedWord();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ushort ReturnDirtyUnsignedWord() =>
		(ushort)DirtyUnsignedWordSource();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowCheckedConversionBoundaryEntry()
	{
		var value = (ushort)DirtyUnsignedWordSource();
		return checked((int)(uint)value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowLogicalNormalizationEntry()
	{
		var value = (ushort)DirtyUnsignedWordSource();
		ushort one = 1;
		ushort fifteen = 15;
		ushort xorMask = 0x00FF;
		ushort andMask = 0x0FFF;
		ushort orMask = 0x1000;
		ushort multiplier = 3;
		value = (ushort)((value << one) | (value >> fifteen));
		value = (ushort)(value ^ xorMask);
		value = (ushort)(value & andMask);
		value = (ushort)(value | orMask);
		value = (ushort)~value;
		value = (ushort)(value * multiplier);
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NarrowCompareNormalizationEntry()
	{
		var unsignedValue = (ushort)DirtyUnsignedWordSource();
		var signedValue = (sbyte)DirtySignedByteSource();
		return (unsignedValue > 65000 ? 1 : 0) +
			(signedValue < -1 ? 2 : 0);
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
	public static uint ConstantMultiplyEntry() =>
		MultiplyByFnvPrime(0xFEDCBA98u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint MultiplyByFnvPrime(uint value) =>
		value * 0x01000193u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MultiplyByFnvPrimeArgument(uint value) =>
		value * 0x01000193u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ConstantMultiplyDifferentialEntry()
	{
		static uint Mix(uint checksum, uint value) =>
			unchecked(((checksum << 1) | (checksum >> 31)) ^
				MultiplyByFnvPrimeArgument(value));

		var checksum = 0u;
		checksum = Mix(checksum, 0);
		checksum = Mix(checksum, 1);
		checksum = Mix(checksum, 0x7FFF_FFFFu);
		checksum = Mix(checksum, 0x8000_0000u);
		checksum = Mix(checksum, uint.MaxValue);
		var random = 0x6800_C0DEu;
		for (var index = 0; index < 32; index++)
		{
			random ^= random << 13;
			random ^= random >> 17;
			random ^= random << 5;
			checksum = Mix(checksum, random);
		}
		return checksum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint DenseConstantMultiplyEntry() =>
		MultiplyByDenseConstant(0x12345678u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint MultiplyByDenseConstant(uint value) =>
		value * 0x55555555u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SubtractConstantMultiplyEntry() =>
		MultiplyBySubtractConstant(0x12345678u);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint MultiplyBySubtractConstant(uint value) =>
		value * 0x7FFFFFFFu;

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
	public static uint AptrByteWordAccessEntry()
	{
		var address = APTR.FromPointer(0x0000_4000);
		APTR.WriteUInt8(address, 3, 0xA5);
		APTR.WriteUInt16(address, 6, 0x5AA5);
		return (uint)(APTR.ReadUInt8(address, 3) << 16) | APTR.ReadUInt16(address, 6);
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
			(int)FileInfoBlock.GetProtection(address) +
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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TransparentScalarInstanceReceiverEntry()
	{
		var left = new TransparentScalarWrapper(19);
		var right = new TransparentScalarWrapper(23);
		return left.Add(right);
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

	public readonly struct TransparentScalarWrapper
	{
		public TransparentScalarWrapper(uint raw) => Raw = raw;

		public uint Raw { get; }

		[MethodImpl(MethodImplOptions.NoInlining)]
		public uint Add(TransparentScalarWrapper other) => Raw + other.Raw;
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
	public static int ObjectArrayBoxedValueStoreEntry()
	{
		var values = new object[1];
		values[0] = 42;
		return values[0] is int ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StringArrayStoreTypeCheckEntry()
	{
		var strings = new string[2];
		strings[0] = "Amiga";
		object[] values = strings;
		try
		{
			values[1] = new object();
			return 0;
		}
		catch (ArrayTypeMismatchException)
		{
			return strings[0].Length == 5 ? 42 : 0;
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
	public static int ConstrainedGenericInterfaceMethodDispatchEntry()
	{
		return ReadConstrainedGenericMethod(ref _runtimeGenericMethodSource);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ConstrainedGenericMultiArgumentDefaultFinallyEntry()
	{
		var destination = new RuntimeConstrainedWriter();
		return WriteConstrainedWithDefaultAndFinally(ref destination, 19, 23);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ValueTypeConstructorStoredThroughOutParameterEntry()
	{
		WriteAggregate(out var value, 19, 23);
		return value.First + value.Second;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void WriteAggregate(out RuntimeOutAggregate value, int first, int second) =>
		value = new RuntimeOutAggregate(first, second);

	private readonly struct RuntimeOutAggregate
	{
		public RuntimeOutAggregate(int first, int second)
		{
			First = first;
			Second = second;
		}

		public int First { get; }
		public int Second { get; }
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
	public static int MemoryArraySliceAndSpanEntry()
	{
		var values = new int[5];
		values[0] = 10;
		values[1] = 20;
		values[2] = 30;
		values[3] = 40;
		values[4] = 50;
		Memory<int> memory = new(values, 1, 3);
		Memory<int> whole = new Memory<int>(values);
		Memory<int> slice = memory.Slice(1, 2);
		Span<int> writable = slice.Span;
		writable[0]++;
		ReadOnlyMemory<int> readOnly = memory;
		ReadOnlySpan<int> tail = readOnly.Slice(1).Span;
		ReadOnlyMemory<int> implicitReadOnly = values;
		ReadOnlyMemory<int> fullReadOnly = new ReadOnlyMemory<int>(values);
		ReadOnlyMemory<int> readOnlyRange =
			new ReadOnlyMemory<int>(values, 0, 4).Slice(2, 2);
		return memory.Length == 3 &&
			!memory.IsEmpty &&
			whole.Length == 5 &&
			slice.Length == 2 &&
			readOnly.Length == 3 &&
			!readOnly.IsEmpty &&
			tail.Length == 2 &&
			tail[0] == 31 &&
			tail[1] == 40 &&
			implicitReadOnly.Length == 5 &&
			fullReadOnly.Length == 5 &&
			readOnlyRange.Span[0] == 31 &&
			readOnlyRange.Span[1] == 40 &&
			values[2] == 31
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryNullAndBoundsEntry()
	{
		int[] source = null!;
		Memory<int> empty = source;
		ReadOnlyMemory<int> readOnlyEmpty = new(source, 0, 0);
		if (!empty.IsEmpty || empty.Length != 0 ||
			!readOnlyEmpty.IsEmpty || readOnlyEmpty.Length != 0)
		{
			return 0;
		}

		try
		{
			_ = new Memory<int>(source, 1, 0);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
		}

		Memory<int> values = new int[2];
		try
		{
			_ = values.Slice(-1);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
		}

		try
		{
			_ = values.Slice(1, 2);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
		}

		try
		{
			_ = new ReadOnlyMemory<int>(source, 0, 1);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
		}

		ReadOnlyMemory<int> readOnlyValues = new int[2];
		try
		{
			_ = readOnlyValues.Slice(1, int.MaxValue);
			return 0;
		}
		catch (ArgumentOutOfRangeException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryReferenceOwnerSurvivesCollectionEntry()
	{
		var values = new ManagedBox[2];
		values[1] = new ManagedBox { Value = 41 };
		Memory<ManagedBox> memory = new(values, 1, 1);
		ReadOnlyMemory<ManagedBox> readOnly = memory;
		values = null!;
		memory = default;
		M68kRuntime.Collect();
		var replacement = new ManagedBox[2];
		replacement[1] = new ManagedBox { Value = 100 };
		return readOnly.Span[0].Value + readOnly.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryScalarWidthAndEndianEntry()
	{
		var bytes = new byte[3];
		bytes[1] = 7;
		Memory<byte> byteMemory = new(bytes, 1, 1);
		var chars = new char[2];
		chars[1] = 'Z';
		ReadOnlyMemory<char> charMemory = new(chars, 1, 1);
		var states = new ListByteState[2];
		states[1] = ListByteState.Second;
		Memory<ListByteState> enumMemory = new(states, 1, 1);
		return byteMemory.Span[0] == 7 &&
			charMemory.Span[0] == 'Z' &&
			enumMemory.Span[0] == ListByteState.Second
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long MemoryLongSpanEntry()
	{
		var values = new long[2];
		values[1] = 0x1122334455667788L;
		ReadOnlyMemory<long> memory = new(values, 1, 1);
		return memory.Span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryLongSpanLowWordEntry()
	{
		var values = new long[2];
		values[1] = 0x1122334455667788L;
		ReadOnlyMemory<long> memory = new(values, 1, 1);
		return unchecked((int)memory.Span[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static float MemoryFloatSpanEntry()
	{
		var values = new float[2];
		Memory<float> memory = new(values, 1, 1);
		memory.Span[0] = 21.5f;
		return memory.Span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryCopyOperationsEntry()
	{
		var values = new int[6];
		values[0] = 1;
		values[1] = 2;
		values[2] = 3;
		values[3] = 4;
		values[4] = 5;
		values[5] = 6;
		Memory<int> whole = values;
		whole.Slice(0, 5).CopyTo(whole.Slice(1, 5));
		if (values[0] != 1 || values[1] != 1 || values[2] != 2 ||
			values[3] != 3 || values[4] != 4 || values[5] != 5)
		{
			return 0;
		}

		ReadOnlyMemory<int> backwardSource = whole.Slice(1, 5);
		backwardSource.CopyTo(whole.Slice(0, 5));
		if (values[0] != 1 || values[1] != 2 || values[2] != 3 ||
			values[3] != 4 || values[4] != 5 || values[5] != 5)
		{
			return 0;
		}

		var destinationValues = new int[7];
		Memory<int> destination = new(destinationValues, 1, 6);
		if (!whole.TryCopyTo(destination))
		{
			return 0;
		}
		ReadOnlyMemory<int> readOnlyWhole = whole;
		Memory<int> oversizedDestination = destinationValues;
		if (!readOnlyWhole.TryCopyTo(oversizedDestination))
		{
			return 0;
		}
		Memory<int> emptySource = default;
		Memory<int> emptyDestination = default;
		emptySource.CopyTo(emptyDestination);
		return emptySource.TryCopyTo(emptyDestination) &&
			destinationValues[0] == 1 && destinationValues[1] == 2 &&
			destinationValues[5] == 5 &&
			destinationValues[6] == 5
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryCopyShortDestinationEntry()
	{
		var sourceValues = new int[2];
		sourceValues[0] = 11;
		sourceValues[1] = 13;
		var destinationValues = new int[1];
		destinationValues[0] = 29;
		Memory<int> source = sourceValues;
		Memory<int> destination = destinationValues;
		var caught = false;
		try
		{
			source.CopyTo(destination);
		}
		catch (ArgumentException)
		{
			caught = true;
		}
		if (!caught || destinationValues[0] != 29 || source.TryCopyTo(destination) ||
			destinationValues[0] != 29)
		{
			return 0;
		}
		ReadOnlyMemory<int> readOnly = source;
		return !readOnly.TryCopyTo(destination) && destinationValues[0] == 29
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryCopyScalarWidthsEntry()
	{
		var bytes = new byte[2];
		bytes[1] = 7;
		var byteDestination = new byte[1];
		new ReadOnlyMemory<byte>(bytes, 1, 1).CopyTo(byteDestination);
		var chars = new char[1];
		chars[0] = 'Z';
		var charDestination = new char[1];
		new Memory<char>(chars).CopyTo(charDestination);
		var states = new ListByteState[1];
		states[0] = ListByteState.Second;
		var stateDestination = new ListByteState[1];
		new Memory<ListByteState>(states).CopyTo(stateDestination);
		var floats = new float[1];
		Memory<float> floatSource = floats;
		floatSource.Span[0] = 21.5f;
		var floatDestination = new float[1];
		Memory<float> floatDestinationMemory = floatDestination;
		floatSource.CopyTo(floatDestinationMemory);
		return byteDestination[0] == 7 && charDestination[0] == 'Z' &&
			stateDestination[0] == ListByteState.Second &&
			floatDestinationMemory.Span[0] == 21.5f
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long MemoryCopyLongEntry()
	{
		var source = new long[1];
		source[0] = 0x1122334455667788L;
		var destination = new long[1];
		new ReadOnlyMemory<long>(source).CopyTo(destination);
		return destination[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryCopyLongLowWordEntry()
	{
		var source = new long[1];
		source[0] = 0x1122334455667788L;
		var destination = new long[1];
		new ReadOnlyMemory<long>(source).CopyTo(destination);
		return unchecked((int)destination[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int MemoryReferenceCopySurvivesCollectionEntry()
	{
		var sourceValues = new ManagedBox[1];
		sourceValues[0] = new ManagedBox { Value = 41 };
		var destinationValues = new ManagedBox[1];
		ReadOnlyMemory<ManagedBox> source = sourceValues;
		Memory<ManagedBox> destination = destinationValues;
		source.CopyTo(destination);
		source = default;
		sourceValues = null!;
		M68kRuntime.Collect();
		var replacement = new ManagedBox[1];
		replacement[0] = new ManagedBox { Value = 100 };
		return destination.Span[0].Value + destination.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedMemoryToArrayEntry()
	{
		Memory<int> memory = new int[1];
		return memory.ToArray().Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeToArrayEntry()
	{
		var values = Enumerable.Range(-2, 5).ToArray();
		var empty = Enumerable.Range(42, 0).ToArray();
		return values.Length == 5 &&
			values[0] == -2 && values[1] == -1 && values[2] == 0 &&
			values[3] == 1 && values[4] == 2 && empty.Length == 0
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeValidatesArgumentsAtFactoryCallEntry()
	{
		var caught = 0;
		try
		{
			_ = Enumerable.Range(0, -1);
		}
		catch (ArgumentOutOfRangeException)
		{
			caught |= 1;
		}
		try
		{
			_ = Enumerable.Range(int.MaxValue, 2);
		}
		catch (ArgumentOutOfRangeException)
		{
			caught |= 2;
		}
		return caught == 3 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeLocalRepeatedToArrayEntry()
	{
		IEnumerable<int> source = Enumerable.Range(40, 2);
		var first = source.ToArray();
		var second = source.ToArray();
		return !ReferenceEquals(first, second) &&
			first[0] == 40 && first[1] == 41 &&
			second[0] == 40 && second[1] == 41
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSameFamilyMergeToArrayEntry() =>
		LinqRangeSameFamilyMergeToArray(true) +
		LinqRangeSameFamilyMergeToArray(false) == 82
			? 42
			: 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqRangeSameFamilyMergeToArray(bool first)
	{
		IEnumerable<int> source;
		if (first)
		{
			source = Enumerable.Range(39, 2);
		}
		else
		{
			source = Enumerable.Range(40, 1);
		}
		var values = source.ToArray();
		return values[0] + values.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqMixedFactoryMergeEntry() =>
		UnsupportedLinqMixedFactoryMerge(false).Length;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int[] UnsupportedLinqMixedFactoryMerge(bool range)
	{
		IEnumerable<int> source;
		if (range)
		{
			source = Enumerable.Range(0, 1);
		}
		else
		{
			source = Enumerable.Repeat(0, 1);
		}
		return source.ToArray();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectToArrayEntry()
	{
		var calls = 0;
		var selected = Enumerable.Range(1, 3).Select(value =>
		{
			calls++;
			return value * 2;
		});
		if (calls != 0)
		{
			return 0;
		}
		var first = selected.ToArray();
		var second = selected.ToArray();
		return calls == 6 && !ReferenceEquals(first, second) &&
			first[0] == 2 && first[1] == 4 && first[2] == 6 &&
			second[0] == 2 && second[1] == 4 && second[2] == 6
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectStaticToArrayEntry()
	{
		var values = Enumerable.Range(1, 3).Select(LinqSelectDouble).ToArray();
		return values[0] == 2 && values[1] == 4 && values[2] == 6 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqSelectDouble(int value) => value * 2;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectDefersSelectorExceptionEntry()
	{
		var selected = Enumerable.Range(1, 3).Select(LinqSelectThrowOnTwo);
		try
		{
			_ = selected.ToArray();
			return 0;
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqSelectThrowOnTwo(int value)
	{
		if (value == 2)
		{
			throw null!;
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectCaptureSurvivesCollectionEntry()
	{
		var box = new ManagedBox { Value = 39 };
		var selected = Enumerable.Range(1, 2).Select(value => box.Value + value);
		M68kRuntime.Collect();
		var values = selected.ToArray();
		return values[0] == 40 && values[1] == 41 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectNullSelectorEntry()
	{
		try
		{
			_ = Enumerable.Select(
				Enumerable.Range(0, 1),
				(Func<int, int>)null!);
			return 0;
		}
		catch (ArgumentNullException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqRepeatSelectEntry() =>
		Enumerable.Repeat(1, 1).Select(static value => value + 1).ToArray()[0];

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqIndexedSelectEntry() =>
		Enumerable.Range(1, 1).Select(static (value, index) => value + index).ToArray()[0];

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereToArrayEntry()
	{
		var calls = 0;
		var filtered = Enumerable.Range(1, 4).Where(value =>
		{
			calls++;
			return (value & 1) == 0;
		});
		if (calls != 0)
		{
			return 0;
		}
		var first = filtered.ToArray();
		var second = filtered.ToArray();
		return calls == 8 && !ReferenceEquals(first, second) &&
			first.Length == 2 && first[0] == 2 && first[1] == 4 &&
			second.Length == 2 && second[0] == 2 && second[1] == 4
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereAllNoneEmptyEntry()
	{
		var all = Enumerable.Range(39, 3).Where(LinqWhereAlways).ToArray();
		var none = Enumerable.Range(1, 3).Where(LinqWhereNever).ToArray();
		var empty = Enumerable.Range(1, 0).Where(LinqWhereAlways).ToArray();
		return all.Length == 3 && all[0] == 39 && all[1] == 40 && all[2] == 41 &&
			none.Length == 0 && empty.Length == 0
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqWhereAlways(int value) => true;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqWhereNever(int value) => false;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectWhereToArrayEntry()
	{
		var selectorCalls = 0;
		var predicateCalls = 0;
		var values = Enumerable.Range(1, 4)
			.Select(value =>
			{
				selectorCalls++;
				return value * 2;
			})
			.Where(value =>
			{
				predicateCalls++;
				return value > 4;
			})
			.ToArray();
		return selectorCalls == 4 && predicateCalls == 4 &&
			values.Length == 2 && values[0] == 6 && values[1] == 8
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectWhereStaticToArrayEntry()
	{
		var values = Enumerable.Range(1, 4)
			.Select(LinqSelectDouble)
			.Where(LinqWhereGreaterThanFour)
			.ToArray();
		return values.Length == 2 && values[0] == 6 && values[1] == 8 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqWhereGreaterThanFour(int value) => value > 4;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereCaptureSurvivesCollectionEntry()
	{
		var box = new ManagedBox { Value = 2 };
		var filtered = Enumerable.Range(1, 3).Where(value => value > box.Value);
		M68kRuntime.Collect();
		var values = filtered.ToArray();
		return values.Length == 1 && values[0] == 3 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereNullPredicateEntry()
	{
		try
		{
			_ = Enumerable.Where(
				Enumerable.Range(0, 1),
				(Func<int, bool>)null!);
			return 0;
		}
		catch (ArgumentNullException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereDefersPredicateExceptionEntry()
	{
		var filtered = Enumerable.Range(1, 3).Where(LinqWhereThrowOnTwo);
		try
		{
			_ = filtered.ToArray();
			return 0;
		}
		catch (NullReferenceException)
		{
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqWhereThrowOnTwo(int value)
	{
		if (value == 2)
		{
			throw null!;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqRepeatWhereEntry() =>
		Enumerable.Repeat(1, 1).Where(static value => value != 0).ToArray()[0];

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqIndexedWhereEntry() =>
		Enumerable.Range(1, 1).Where(static (value, index) => value != index).ToArray()[0];

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqAnyWithoutPredicateEntry()
	{
		if (Enumerable.Range(0, 0).Any() || !Enumerable.Range(0, 1).Any() ||
			Enumerable.Repeat(1, 0).Any() || !Enumerable.Repeat(1, 1).Any())
		{
			return 0;
		}

		var selectCalls = 0;
		var selected = Enumerable.Range(1, 2).Select(value =>
		{
			selectCalls++;
			return value * 2;
		});
		if (!selected.Any() || selectCalls != 0)
		{
			return 0;
		}

		var whereCalls = 0;
		var filtered = Enumerable.Range(1, 4).Where(value =>
		{
			whereCalls++;
			return value == 3;
		});
		if (!filtered.Any() || whereCalls != 3)
		{
			return 0;
		}

		var projectedCalls = 0;
		var projectedPredicateCalls = 0;
		var projectedFiltered = Enumerable.Range(1, 4)
			.Select(value =>
			{
				projectedCalls++;
				return value * 2;
			})
			.Where(value =>
			{
				projectedPredicateCalls++;
				return value > 4;
			});
		return projectedFiltered.Any() && projectedCalls == 3 &&
			projectedPredicateCalls == 3
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqAnyPredicateEntry()
	{
		var rangeCalls = 0;
		var rangeFound = Enumerable.Range(1, 5).Any(value =>
		{
			rangeCalls++;
			return value == 3;
		});

		var repeatCalls = 0;
		var repeatFound = Enumerable.Repeat(1, 4).Any(value =>
		{
			repeatCalls++;
			return repeatCalls == 3;
		});

		var selectCalls = 0;
		var selectPredicateCalls = 0;
		var selectFound = Enumerable.Range(1, 5)
			.Select(value =>
			{
				selectCalls++;
				return value * 2;
			})
			.Any(value =>
			{
				selectPredicateCalls++;
				return value >= 6;
			});

		var whereCalls = 0;
		var wherePredicateCalls = 0;
		var whereFound = Enumerable.Range(1, 4)
			.Where(value =>
			{
				whereCalls++;
				return (value & 1) == 0;
			})
			.Any(value =>
			{
				wherePredicateCalls++;
				return value > 2;
			});

		var projectedCalls = 0;
		var projectedWhereCalls = 0;
		var projectedAnyCalls = 0;
		var projectedFound = Enumerable.Range(1, 5)
			.Select(value =>
			{
				projectedCalls++;
				return value * 2;
			})
			.Where(value =>
			{
				projectedWhereCalls++;
				return (value & 3) == 0;
			})
			.Any(value =>
			{
				projectedAnyCalls++;
				return value > 4;
			});

		return rangeFound && rangeCalls == 3 &&
			repeatFound && repeatCalls == 3 &&
			selectFound && selectCalls == 3 && selectPredicateCalls == 3 &&
			whereFound && whereCalls == 4 && wherePredicateCalls == 2 &&
			projectedFound && projectedCalls == 4 &&
			projectedWhereCalls == 4 && projectedAnyCalls == 2
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqAnyExceptionTimingEntry()
	{
		var caught = 0;
		try
		{
			_ = Enumerable.Range(1, 1).Any((Func<int, bool>)null!);
		}
		catch (ArgumentNullException)
		{
			caught |= 1;
		}
		try
		{
			_ = Enumerable.Range(1, 3).Any(LinqAnyThrowOnTwo);
		}
		catch (NullReferenceException)
		{
			caught |= 2;
		}
		try
		{
			if (Enumerable.Range(1, 3).Any(LinqAnyTrueThenThrow))
			{
				caught |= 4;
			}
		}
		catch (NullReferenceException)
		{
			return 0;
		}
		return caught == 7 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqAnyThrowOnTwo(int value)
	{
		if (value == 2)
		{
			throw null!;
		}
		return false;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqAnyTrueThenThrow(int value)
	{
		if (value != 1)
		{
			throw null!;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqAnyCaptureSurvivesCollectionEntry()
	{
		var whereBox = new ManagedBox { Value = 2 };
		var anyBox = new ManagedBox { Value = 3 };
		var filtered = Enumerable.Range(1, 4).Where(value => value > whereBox.Value);
		Func<int, bool> predicate = value => value == anyBox.Value;
		M68kRuntime.Collect();
		return filtered.Any(predicate) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectWhereAnyStaticEntry() =>
		Enumerable.Range(1, 4)
			.Select(LinqSelectDouble)
			.Where(LinqWhereGreaterThanFour)
			.Any(LinqAnyGreaterThanSix)
				? 42
				: 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool LinqAnyGreaterThanSix(int value) => value > 6;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqArrayAnyEntry() => new[] { 42 }.Any() ? 42 : 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqByteAnyEntry() =>
		Enumerable.Repeat((byte)1, 1).Any() ? 42 : 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqTakeToArrayEntry()
	{
		var range = Enumerable.Range(3, 5).Take(2).ToArray();
		var empty = Enumerable.Range(3, 5).Take(-1).ToArray();
		var repeat = Enumerable.Repeat(7, 3).Take(8).ToArray();

		var selectCalls = 0;
		var selected = Enumerable.Range(1, 5)
			.Select(value =>
			{
				selectCalls++;
				return value * 10;
			})
			.Take(2);
		if (selectCalls != 0)
		{
			return 0;
		}
		var selectedValues = selected.ToArray();

		var whereCalls = 0;
		var filtered = Enumerable.Range(1, 8)
			.Where(value =>
			{
				whereCalls++;
				return (value & 1) == 0;
			})
			.Take(2)
			.ToArray();

		var projectedCalls = 0;
		var projectedWhereCalls = 0;
		var projected = Enumerable.Range(1, 6)
			.Select(value =>
			{
				projectedCalls++;
				return value * 3;
			})
			.Where(value =>
			{
				projectedWhereCalls++;
				return (value & 1) == 0;
			})
			.Take(2)
			.ToArray();

		var repeated = Enumerable.Range(1, 5).Take(4).Take(2).ToArray();
		if (range.Length != 2)
		{
			return 10 + range.Length;
		}
		if (range[0] != 3)
		{
			return 20 + range[0];
		}
		if (range[1] != 4)
		{
			return 30 + range[1];
		}
		if (empty.Length != 0)
		{
			return 40 + empty.Length;
		}
		if (repeat.Length != 3 || repeat[0] != 7 || repeat[2] != 7)
		{
			return 2;
		}
		if (selectedValues.Length != 2 || selectedValues[0] != 10 ||
			selectedValues[1] != 20 || selectCalls != 2)
		{
			return 3;
		}
		if (filtered.Length != 2 || filtered[0] != 2 || filtered[1] != 4 ||
			whereCalls != 4)
		{
			return 4;
		}
		if (projected.Length != 2 || projected[0] != 6 || projected[1] != 12 ||
			projectedCalls != 4 || projectedWhereCalls != 4)
		{
			return 5;
		}
		if (repeated.Length != 2 || repeated[0] != 1 || repeated[1] != 2)
		{
			return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqTakeAnyEntry()
	{
		var zeroCalls = 0;
		var zero = Enumerable.Range(1, 4)
			.Where(value =>
			{
				zeroCalls++;
				return true;
			})
			.Take(0)
			.Any();

		var selectCalls = 0;
		var selected = Enumerable.Range(1, 4)
			.Select(value =>
			{
				selectCalls++;
				return value * 2;
			})
			.Take(2)
			.Any();

		var whereCalls = 0;
		var terminalCalls = 0;
		var filtered = Enumerable.Range(1, 8)
			.Where(value =>
			{
				whereCalls++;
				return (value & 1) == 0;
			})
			.Take(2)
			.Any(value =>
			{
				terminalCalls++;
				return false;
			});

		var projectedCalls = 0;
		var projectedWhereCalls = 0;
		var projectedTerminalCalls = 0;
		var projected = Enumerable.Range(1, 6)
			.Select(value =>
			{
				projectedCalls++;
				return value * 3;
			})
			.Where(value =>
			{
				projectedWhereCalls++;
				return (value & 1) == 0;
			})
			.Take(2)
			.Any(value =>
			{
				projectedTerminalCalls++;
				return value == 12;
			});

		if (zero || zeroCalls != 0)
		{
			return 1;
		}
		if (!selected || selectCalls != 0)
		{
			return 2;
		}
		if (filtered || whereCalls != 4 || terminalCalls != 2)
		{
			return 3;
		}
		if (!projected || projectedCalls != 4 || projectedWhereCalls != 4 ||
			projectedTerminalCalls != 2)
		{
			return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqTakeExceptionTimingEntry()
	{
		var caught = 0;
		try
		{
			_ = Enumerable.Take<int>(null!, 1);
		}
		catch (ArgumentNullException)
		{
			caught |= 1;
		}

		try
		{
			var safe = Enumerable.Range(1, 3).Select(LinqTakeThrowOnTwo).Take(1);
			var values = safe.ToArray();
			if (values.Length != 1)
			{
				return 10 + values.Length;
			}
			if (values[0] != 1)
			{
				return 20 + values[0];
			}
			caught |= 2;
		}
		catch (NullReferenceException)
		{
			return 0;
		}

		try
		{
			_ = Enumerable.Range(1, 3).Select(LinqTakeThrowOnTwo).Take(2).ToArray();
		}
		catch (NullReferenceException)
		{
			caught |= 4;
		}
		if (caught == 7)
		{
			return 42;
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqTakeThrowOnTwo(int value)
	{
		if (value == 2)
		{
			throw null!;
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqTakeCaptureSurvivesCollectionEntry()
	{
		var whereBox = new ManagedBox { Value = 1 };
		var anyBox = new ManagedBox { Value = 4 };
		var values = Enumerable.Range(1, 8)
			.Where(value => value > whereBox.Value)
			.Take(3);
		Func<int, bool> predicate = value => value == anyBox.Value;
		M68kRuntime.Collect();
		return values.Any(predicate) ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectWhereTakeAnyStaticEntry() =>
		Enumerable.Range(1, 8)
			.Select(LinqSelectDouble)
			.Where(LinqWhereGreaterThanFour)
			.Take(2)
			.Any(LinqAnyGreaterThanSix)
				? 42
				: 0;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeTakeStaticToArrayEntry()
	{
		var values = Enumerable.Range(3, 5).Take(2).ToArray();
		return values.Length * 100 + values[0] * 10 + values[1];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeRepeatTakeEntry()
	{
		var range = Enumerable.Range(3, 5).Take(2).ToArray();
		var empty = Enumerable.Range(3, 5).Take(-1).ToArray();
		var repeat = Enumerable.Repeat(7, 3).Take(8).ToArray();
		var repeated = Enumerable.Range(1, 5).Take(4).Take(2).ToArray();
		if (range.Length != 2 || range[0] != 3 || range[1] != 4 ||
			empty.Length != 0)
		{
			return 0;
		}
		if (repeat.Length != 3 || repeat[0] != 7 || repeat[2] != 7)
		{
			return 0;
		}
		return repeated.Length == 2 && repeated[0] == 1 && repeated[1] == 2
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSelectTakeEntry()
	{
		var calls = 0;
		var selected = Enumerable.Range(1, 5)
			.Select(value =>
			{
				calls++;
				return value * 10;
			})
			.Take(2);
		if (calls != 0)
		{
			return 0;
		}
		var values = selected.ToArray();
		return values.Length == 2 && values[0] == 10 && values[1] == 20 &&
			calls == 2
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereTakeEntry()
	{
		var calls = 0;
		var values = Enumerable.Range(1, 8)
			.Where(value =>
			{
				calls++;
				return (value & 1) == 0;
			})
			.Take(2)
			.ToArray();
		return values.Length == 2 && values[0] == 2 && values[1] == 4 &&
			calls == 4
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSelectWhereTakeEntry()
	{
		var selectCalls = 0;
		var whereCalls = 0;
		var values = Enumerable.Range(1, 6)
			.Select(value =>
			{
				selectCalls++;
				return value * 3;
			})
			.Where(value =>
			{
				whereCalls++;
				return (value & 1) == 0;
			})
			.Take(2)
			.ToArray();
		return values.Length == 2 && values[0] == 6 && values[1] == 12 &&
			selectCalls == 4 && whereCalls == 4
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeRepeatSumEntry()
	{
		if (Enumerable.Range(1, 4).Sum() != 10)
		{
			return 1;
		}
		if (Enumerable.Range(1, 0).Sum() != 0)
		{
			return 2;
		}
		if (Enumerable.Repeat(3, 4).Sum() != 12)
		{
			return 3;
		}
		if (Enumerable.Range(1, 4).Sum(static value => value * 2) != 20)
		{
			return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSelectSumEntry()
	{
		var selected = Enumerable.Range(1, 3).Select(static value => value * 3);
		if (selected.Sum() != 18)
		{
			return 1;
		}
		if (selected.Sum(static value => value + 1) != 21)
		{
			return 2;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeWhereSumEntry()
	{
		var filtered = Enumerable.Range(1, 6).Where(static value => (value & 1) == 0);
		if (filtered.Sum() != 12)
		{
			return 1;
		}
		if (filtered.Sum(static value => value * 2) != 24)
		{
			return 2;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSelectWhereTakeSumEntry()
	{
		var values = Enumerable.Range(1, 8)
			.Select(static value => value * 2)
			.Where(static value => value > 4)
			.Take(2);
		if (values.Sum() != 14)
		{
			return 1;
		}
		if (values.Sum(static value => value + 1) != 16)
		{
			return 2;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSumEveryPrivateTargetEntry()
	{
		var range = Enumerable.Range(1, 3);
		var repeat = Enumerable.Repeat(2, 3);
		var select = Enumerable.Range(1, 3).Select(static value => value * 2);
		var rangeWhere = Enumerable.Range(1, 4).Where(static value => value > 1);
		var rangeWhereTake = Enumerable.Range(1, 4)
			.Where(static value => value > 1)
			.Take(2);
		var selectWhere = Enumerable.Range(1, 4)
			.Select(static value => value * 2)
			.Where(static value => value > 2);
		var selectWhereTake = Enumerable.Range(1, 4)
			.Select(static value => value * 2)
			.Where(static value => value > 2)
			.Take(2);
		return range.Sum() + range.Sum(static value => value) +
			repeat.Sum() + repeat.Sum(static value => value) +
			select.Sum() + select.Sum(static value => value) +
			rangeWhere.Sum() + rangeWhere.Sum(static value => value) +
			rangeWhereTake.Sum() + rangeWhereTake.Sum(static value => value) +
			selectWhere.Sum() + selectWhere.Sum(static value => value) +
			selectWhereTake.Sum() + selectWhereTake.Sum(static value => value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSumExceptionTimingEntry()
	{
		var caught = 0;
		try
		{
			_ = Enumerable.Sum((IEnumerable<int>)null!);
		}
		catch (ArgumentNullException)
		{
			caught |= 1;
		}

		try
		{
			_ = Enumerable.Sum<int>(null!, static value => value);
		}
		catch (ArgumentNullException)
		{
			caught |= 2;
		}

		try
		{
			_ = Enumerable.Range(1, 1).Sum((Func<int, int>)null!);
		}
		catch (ArgumentNullException)
		{
			caught |= 4;
		}

		try
		{
			_ = Enumerable.Range(int.MaxValue - 1, 2).Sum();
		}
		catch (OverflowException)
		{
			caught |= 8;
		}

		try
		{
			_ = Enumerable.Range(1, 3).Sum(LinqSumThrowOnTwo);
		}
		catch (NullReferenceException)
		{
			caught |= 16;
		}

		if (caught == 31)
		{
			return 42;
		}
		return 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqSumThrowOnTwo(int value)
	{
		if (value == 2)
		{
			throw null!;
		}
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqSumCaptureSurvivesCollectionEntry()
	{
		var selectBox = new ManagedBox { Value = 3 };
		var whereBox = new ManagedBox { Value = 3 };
		var sumBox = new ManagedBox { Value = 1 };
		var values = Enumerable.Range(1, 5)
			.Select(value => value * selectBox.Value)
			.Where(value => value > whereBox.Value)
			.Take(2);
		Func<int, int> selector = value => value + sumBox.Value;
		M68kRuntime.Collect();
		return values.Sum(selector) == 17 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRangeSelectWhereTakeSumStaticEntry() =>
		Enumerable.Range(1, 8)
			.Select(LinqSelectDouble)
			.Where(LinqWhereGreaterThanFour)
			.Take(2)
			.Sum(LinqSumTriple);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqSumTriple(int value) => value * 3;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryValuesOrderByThenByEntry()
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		var first = new DictionaryImageDescriptor(2, 1, 1);
		var second = new DictionaryImageDescriptor(1, 2, 2);
		var third = new DictionaryImageDescriptor(1, 1, 3);
		var fourth = new DictionaryImageDescriptor(1, 1, 4);
		var fifth = new DictionaryImageDescriptor(2, 0, 5);
		values.Add(10, first);
		values.Add(11, second);
		values.Add(12, third);
		values.Add(13, fourth);
		values.Add(14, fifth);
		var ordered = values.Values
			.OrderBy(LinqOrderCylinder)
			.ThenBy(LinqOrderHead);
		var deferred = new DictionaryImageDescriptor(0, 9, 6);
		values.Add(15, deferred);

		var encoded = 0;
		foreach (var descriptor in ordered)
		{
			encoded = encoded * 10 + (int)descriptor.DataId;
		}
		return encoded;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqOrderCylinder(DictionaryImageDescriptor descriptor) =>
		descriptor.Cylinder;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqOrderHead(DictionaryImageDescriptor descriptor) =>
		descriptor.Head;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryOrderingStatefulRepeatedEntry()
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		var first = new DictionaryImageDescriptor(2, 2, 1);
		var second = new DictionaryImageDescriptor(1, 2, 2);
		var third = new DictionaryImageDescriptor(1, 1, 3);
		values.Add(10, first);
		values.Add(11, second);
		values.Add(12, third);
		var primaryCalls = 0;
		var secondaryCalls = 0;
		var ordered = values.Values
			.OrderBy(value =>
			{
				primaryCalls++;
				return value.Cylinder;
			})
			.ThenBy(value =>
			{
				secondaryCalls++;
				return value.Head;
			});

		var encoded = 0;
		foreach (var value in ordered)
		{
			encoded = encoded * 10 + (int)value.DataId;
		}
		foreach (var value in ordered)
		{
			encoded = encoded * 10 + (int)value.DataId;
		}
		return encoded == 321321 && primaryCalls == 6 && secondaryCalls == 6
			? 42
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryOrderingExceptionTimingEntry()
	{
		var caught = 0;
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		var descriptor = new DictionaryImageDescriptor(1, 1, 1);
		values.Add(1, descriptor);
		try
		{
			_ = Enumerable.OrderBy<DictionaryImageDescriptor, int>(
				null!,
				LinqOrderCylinder);
		}
		catch (ArgumentNullException)
		{
			caught |= 1;
		}
		try
		{
			_ = values.Values.OrderBy(
				(Func<DictionaryImageDescriptor, int>)null!);
		}
		catch (ArgumentNullException)
		{
			caught |= 2;
		}

		var primary = values.Values.OrderBy(LinqOrderCylinder);
		try
		{
			_ = primary.ThenBy((Func<DictionaryImageDescriptor, int>)null!);
		}
		catch (ArgumentNullException)
		{
			caught |= 4;
		}
		try
		{
			_ = Enumerable.ThenBy<DictionaryImageDescriptor, int>(
				null!,
				LinqOrderHead);
		}
		catch (ArgumentNullException)
		{
			caught |= 8;
		}

		var throwingPrimary = values.Values
			.OrderBy(LinqOrderThrow)
			.ThenBy(LinqOrderHead);
		try
		{
			foreach (var value in throwingPrimary)
			{
				_ = value.DataId;
			}
		}
		catch (NullReferenceException)
		{
			caught |= 16;
		}
		var throwingSecondary = values.Values
			.OrderBy(LinqOrderCylinder)
			.ThenBy(LinqOrderThrow);
		try
		{
			foreach (var value in throwingSecondary)
			{
				_ = value.DataId;
			}
		}
		catch (NullReferenceException)
		{
			caught |= 32;
		}
		return caught == 63 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqOrderThrow(DictionaryImageDescriptor descriptor) =>
		throw null!;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryOrderingSize0Entry() =>
		LinqDictionaryOrderingSize(0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryOrderingSize1Entry() =>
		LinqDictionaryOrderingSize(1);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryOrderingSize16Entry() =>
		LinqDictionaryOrderingSize(16);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqDictionaryOrderingSize168Entry() =>
		LinqDictionaryOrderingSize(168);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqDictionaryOrderingSize(int count)
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		for (var index = 0; index < count; index++)
		{
			var reverse = count - index - 1;
			var descriptor =
				new DictionaryImageDescriptor(reverse / 2, reverse % 2, index + 1);
			values.Add(
				(uint)(index + 1),
				descriptor);
		}
		var ordered = values.Values
			.OrderBy(LinqOrderCylinder)
			.ThenBy(LinqOrderHead);
		var seen = 0;
		var previousCylinder = -1;
		var previousHead = -1;
		foreach (var value in ordered)
		{
			if (value.Cylinder < previousCylinder ||
				(value.Cylinder == previousCylinder && value.Head < previousHead))
			{
				return 0;
			}
			previousCylinder = value.Cylinder;
			previousHead = value.Head;
			seen++;
		}
		return seen == count ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StablePermutationSortEntry()
	{
		var permutation = new int[6];
		permutation[0] = 0;
		permutation[1] = 1;
		permutation[2] = 2;
		permutation[3] = 3;
		permutation[4] = 4;
		permutation[5] = 5;
		var primary = new int[6];
		primary[0] = 2;
		primary[1] = 1;
		primary[2] = 1;
		primary[3] = 1;
		primary[4] = 2;
		primary[5] = 0;
		var secondary = new int[6];
		secondary[0] = 1;
		secondary[1] = 2;
		secondary[2] = 1;
		secondary[3] = 1;
		secondary[4] = 0;
		secondary[5] = 9;
		CopperSharp.Runtime.ShadowInt32StablePermutationSort.Sort(
			permutation,
			primary,
			secondary);
		var encoded = 0;
		for (var index = 0; index < permutation.Length; index++)
		{
			encoded = encoded * 10 + permutation[index] + 1;
		}
		return encoded;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqArrayOrderByEntry() =>
		new[] { 2, 1 }.OrderBy(static value => value) is null ? 0 : 42;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqAdditionalThenByEntry()
	{
		var values = new Dictionary<uint, DictionaryImageDescriptor>();
		var descriptor = new DictionaryImageDescriptor(1, 2, 3);
		values.Add(1, descriptor);
		var ordered = values.Values
			.OrderBy(LinqOrderCylinder)
			.ThenBy(LinqOrderHead)
			.ThenBy(static value => value.StartBit);
		return ordered is null ? 0 : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqArrayImageBlockSumSelectorEntry()
	{
		var values = CreateLinqImageBlocks();
		if (values.Sum(StaticImageBlockDelegateTarget) != 42)
		{
			return 1;
		}
		return new DelegateImageBlock[0].Sum(StaticImageBlockDelegateTarget) == 0
			? 42
			: 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqArrayImageBlockSumStaticEntry() =>
		CreateLinqIpfDescriptorBlocks().Sum(LinqIpfDescriptorBits);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DelegateImageBlock[] CreateLinqIpfDescriptorBlocks()
	{
		var block = new DelegateImageBlock(19, 23, 0, 0, 0, 0, 0);
		var values = new DelegateImageBlock[1];
		values[0] = block;
		return values;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqIpfDescriptorBits(DelegateImageBlock block) =>
		checked((int)block.BlockBits + (int)block.GapBits);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DelegateImageBlock[] CreateLinqImageBlocks()
	{
		var block = new DelegateImageBlock(1, 2, 3, 4, 5, 6, 21);
		var values = new DelegateImageBlock[1];
		values[0] = block;
		return values;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqArrayImageBlockSumExceptionTimingEntry()
	{
		var caught = 0;
		try
		{
			_ = Enumerable.Sum<DelegateImageBlock>(
				null!,
				StaticImageBlockDelegateTarget);
		}
		catch (ArgumentNullException)
		{
			caught |= 1;
		}

		try
		{
			_ = CreateLinqImageBlocks().Sum(
				(Func<DelegateImageBlock, int>)null!);
		}
		catch (ArgumentNullException)
		{
			caught |= 2;
		}

		try
		{
			_ = CreateLinqImageBlockOverflowValues().Sum(
				static value => (int)value.BlockBits);
		}
		catch (OverflowException)
		{
			caught |= 4;
		}

		try
		{
			_ = CreateLinqImageBlocks().Sum(LinqImageBlockThrow);
		}
		catch (NullReferenceException)
		{
			caught |= 8;
		}

		try
		{
			_ = CreateLinqImageBlockConversionOverflowValues().Sum(
				LinqIpfDescriptorBits);
		}
		catch (OverflowException)
		{
			caught |= 16;
		}

		return caught == 31 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DelegateImageBlock[] CreateLinqImageBlockOverflowValues()
	{
		var first = new DelegateImageBlock(int.MaxValue, 0, 0, 0, 0, 0, 0);
		var second = new DelegateImageBlock(1, 0, 0, 0, 0, 0, 0);
		var values = new DelegateImageBlock[2];
		values[0] = first;
		values[1] = second;
		return values;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DelegateImageBlock[] CreateLinqImageBlockConversionOverflowValues()
	{
		var block = new DelegateImageBlock(0x8000_0000u, 0, 0, 0, 0, 0, 0);
		var values = new DelegateImageBlock[1];
		values[0] = block;
		return values;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LinqImageBlockThrow(DelegateImageBlock value) => throw null!;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqArrayImageBlockSumCaptureSurvivesCollectionEntry()
	{
		var block = new DelegateImageBlock(0, 0, 0, 0, 0, 0, 41);
		var values = new DelegateImageBlock[1];
		values[0] = block;
		var box = new ManagedBox { Value = 1 };
		Func<DelegateImageBlock, int> selector =
			value => (int)value.DataOffset + box.Value;
		M68kRuntime.Collect();
		return values.Sum(selector) == 42 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqArraySumEntry() =>
		new[] { 1, 2 }.Sum();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqArraySumSelectorEntry() =>
		new[] { 1, 2 }.Sum(static value => value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqReferenceStructArraySumSelectorEntry()
	{
		var value = new ReferenceDelegateBlock(null);
		var values = new ReferenceDelegateBlock[1];
		values[0] = value;
		return values.Sum(static item => item.Value is null ? 0 : 1);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static long UnsupportedLinqLongSumEntry() =>
		Enumerable.Repeat(1L, 2).Sum();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqArrayTakeEntry() =>
		new[] { 42 }.Take(1).ToArray()[0];

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedLinqByteTakeEntry() =>
		Enumerable.Repeat((byte)1, 1).Take(1).ToArray()[0];

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRepeatByteToArrayEntry()
	{
		var values = Enumerable.Repeat((byte)7, 4).ToArray();
		return values.Length == 4 && values[0] == 7 && values[1] == 7 &&
			values[2] == 7 && values[3] == 7
				? 42
				: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int LinqRepeatReferenceSurvivesCollectionEntry()
	{
		var value = new ManagedBox { Value = 40 };
		var values = Enumerable.Repeat(value, 2).ToArray();
		value = null!;
		M68kRuntime.Collect();
		var replacement = new ManagedBox { Value = 100 };
		return ReferenceEquals(values[0], values[1])
			? values[0].Value + values.Length
			: 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int UnsupportedEnumerableArrayToArrayEntry()
	{
		var source = new[] { 42 };
		return Enumerable.ToArray(source)[0];
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
	private static int ReadConstrainedGenericMethod<T>(ref T source)
		where T : struct, IRuntimeGenericMethodSource => source.GetValue<uint>();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int WriteConstrainedWithDefaultAndFinally<T>(
		ref T destination,
		int first,
		int second)
		where T : struct, IRuntimeConstrainedWriter
	{
		var zero = default(T);
		try
		{
			destination.Write(first, second);
			return destination.Read() + zero.Read();
		}
		finally
		{
			destination.Write(destination.Read(), 0);
		}
	}

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

	private readonly struct DelegateImageBlock
	{
		public DelegateImageBlock(
			uint blockBits,
			uint gapBits,
			uint gapOffset,
			uint encoderType,
			uint flags,
			uint gapValue,
			uint dataOffset)
		{
			BlockBits = blockBits;
			GapBits = gapBits;
			GapOffset = gapOffset;
			EncoderType = encoderType;
			Flags = flags;
			GapValue = gapValue;
			DataOffset = dataOffset;
		}

		public readonly uint BlockBits;
		public readonly uint GapBits;
		public readonly uint GapOffset;
		public readonly uint EncoderType;
		public readonly uint Flags;
		public readonly uint GapValue;
		public readonly uint DataOffset;
	}

	private struct ReferenceDelegateBlock
	{
		public ReferenceDelegateBlock(object? value) => Value = value;

		public object? Value;
	}

	private readonly struct DictionaryImageDescriptor
	{
		public DictionaryImageDescriptor(int seed)
		{
			Cylinder = seed;
			Head = seed + 1;
			DensityType = (uint)(seed + 2);
			SignalType = (uint)(seed + 3);
			TrackSize = (uint)(seed + 4);
			StartPosition = (uint)(seed + 5);
			StartBit = seed + 6;
			DataBits = (uint)(seed + 7);
			GapBits = (uint)(seed + 8);
			TrackBits = (uint)(seed + 9);
			BlockCount = seed + 10;
			Process = (uint)(seed + 11);
			Flags = (uint)(seed + 12);
			DataId = (uint)(seed + 13);
		}

		public DictionaryImageDescriptor(int cylinder, int head, int id)
		{
			Cylinder = cylinder;
			Head = head;
			DensityType = (uint)(id + 2);
			SignalType = (uint)(id + 3);
			TrackSize = (uint)(id + 4);
			StartPosition = (uint)(id + 5);
			StartBit = id + 6;
			DataBits = (uint)(id + 7);
			GapBits = (uint)(id + 8);
			TrackBits = (uint)(id + 9);
			BlockCount = id + 10;
			Process = (uint)(id + 11);
			Flags = (uint)(id + 12);
			DataId = (uint)id;
		}

		public readonly int Cylinder;
		public readonly int Head;
		public readonly uint DensityType;
		public readonly uint SignalType;
		public readonly uint TrackSize;
		public readonly uint StartPosition;
		public readonly int StartBit;
		public readonly uint DataBits;
		public readonly uint GapBits;
		public readonly uint TrackBits;
		public readonly int BlockCount;
		public readonly uint Process;
		public readonly uint Flags;
		public readonly uint DataId;

		public readonly bool Matches(int seed) =>
			Cylinder == seed &&
			Head == seed + 1 &&
			DensityType == (uint)(seed + 2) &&
			SignalType == (uint)(seed + 3) &&
			TrackSize == (uint)(seed + 4) &&
			StartPosition == (uint)(seed + 5) &&
			StartBit == seed + 6 &&
			DataBits == (uint)(seed + 7) &&
			GapBits == (uint)(seed + 8) &&
			TrackBits == (uint)(seed + 9) &&
			BlockCount == seed + 10 &&
			Process == (uint)(seed + 11) &&
			Flags == (uint)(seed + 12) &&
			DataId == (uint)(seed + 13);

		public readonly bool IsDefault() =>
			Cylinder == 0 && Head == 0 && DensityType == 0 && SignalType == 0 &&
			TrackSize == 0 && StartPosition == 0 && StartBit == 0 &&
			DataBits == 0 && GapBits == 0 && TrackBits == 0 && BlockCount == 0 &&
			Process == 0 && Flags == 0 && DataId == 0;
	}

	private struct DictionaryReferenceValue
	{
		public DictionaryReferenceValue(object? value) => Value = value;

		public object? Value;
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
	private static bool TryWritePackedRectangle(
		bool succeed,
		out Rectangle bounds)
	{
		bounds = default;
		if (!succeed)
		{
			return false;
		}

		var candidate = new Rectangle
		{
			MinX = -3840,
			MinY = -7,
			MaxX = 123,
			MaxY = 2047
		};
		bounds = candidate;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int PackedRectangleOutStoreEntry()
	{
		var failed = new Rectangle
		{
			MinX = 1,
			MinY = 2,
			MaxX = 3,
			MaxY = 4
		};
		if (TryWritePackedRectangle(false, out failed) ||
			failed.MinX != 0 ||
			failed.MinY != 0 ||
			failed.MaxX != 0 ||
			failed.MaxY != 0)
		{
			return 1;
		}

		if (!TryWritePackedRectangle(true, out var bounds))
		{
			return 2;
		}
		return bounds.MinX == -3840 &&
			bounds.MinY == -7 &&
			bounds.MaxX == 123 &&
			bounds.MaxY == 2047
				? 42
				: 3;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool TryWriteNestedPackedRectangle(
		out ExternalValueTypes.NestedRectangle bounds)
	{
		bounds = new ExternalValueTypes.NestedRectangle
		{
			MinX = -1234,
			MinY = 17,
			MaxX = 2046,
			MaxY = 8191
		};
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int NestedExternalPackedRectangleOutStoreEntry()
	{
		if (!TryWriteNestedPackedRectangle(out var bounds)) return 1;
		if (bounds.MinX != -1234) return 2;
		if (bounds.MinY != 17) return 3;
		if (bounds.MaxX != 2046) return 4;
		return bounds.MaxY == 8191 ? 42 : 5;
	}

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
	public static int IdenticalDirectBodyFoldEntry() =>
		IdenticalDirectBodyA(10) + IdenticalDirectBodyB(20);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalDirectBodyA(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalDirectBodyB(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IdenticalAddressTakenBodiesEntry()
	{
		var first = new Func<int, int>(IdenticalAddressTakenBodyA);
		var second = new Func<int, int>(IdenticalAddressTakenBodyB);
		return first.Equals(second) ? -1 : first(1) + second(2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalAddressTakenBodyA(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalAddressTakenBodyB(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StaticDelegateEntry()
	{
		var transform = new Func<int, int>(StaticDelegateTarget);
		return transform(35);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StaticDelegateTarget(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int StaticMultiwordDelegateEntry()
	{
		var transform = new Func<BoxedPair, int>(StaticMultiwordDelegateTarget);
		var value = new BoxedPair(19, 23);
		return transform(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StaticMultiwordDelegateTarget(BoxedPair value) => value.Sum();

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArrayImageBlockDelegateEntry()
	{
		var block = new DelegateImageBlock(1, 2, 3, 4, 5, 6, 21);
		var values = new DelegateImageBlock[1];
		values[0] = block;
		Func<DelegateImageBlock, int> selector = StaticImageBlockDelegateTarget;
		M68kRuntime.Collect();
		return selector(values[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ArrayDictionaryImageDescriptorDelegateEntry()
	{
		var descriptor = new DictionaryImageDescriptor(1);
		var values = new DictionaryImageDescriptor[1];
		values[0] = descriptor;
		Func<DictionaryImageDescriptor, int> selector =
			StaticDictionaryImageDescriptorDelegateTarget;
		M68kRuntime.Collect();
		return selector(values[0]);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StaticDictionaryImageDescriptorDelegateTarget(
		DictionaryImageDescriptor value) => value.Matches(1) ? 42 : 1;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StaticImageBlockDelegateTarget(DelegateImageBlock value) =>
		(int)value.BlockBits +
		(int)value.GapBits +
		(int)value.GapOffset +
		(int)value.EncoderType +
		(int)value.Flags +
		(int)value.GapValue +
		(int)value.DataOffset;

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

	private interface IRuntimeGenericMethodSource
	{
		int GetValue<TMarker>() where TMarker : struct;
	}

	private interface IRuntimeConstrainedWriter
	{
		void Write(int first, int second);
		int Read();
	}

	private static RuntimeConstrainedSource _runtimeConstrainedSource =
		new RuntimeConstrainedSource(0);
	private static RuntimeGenericMethodSource _runtimeGenericMethodSource =
		new RuntimeGenericMethodSource(0);
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

	private readonly struct RuntimeGenericMethodSource : IRuntimeGenericMethodSource
	{
		private readonly int _value;

		public RuntimeGenericMethodSource(int value)
		{
			_value = value;
		}

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue<TMarker>() where TMarker : struct => _value + 42;
	}

	private struct RuntimeStatefulConstrainedSource : IRuntimeConstrainedSource
	{
		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public int GetValue() => Value;
	}

	private struct RuntimeConstrainedWriter : IRuntimeConstrainedWriter
	{
		private int _value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public void Write(int first, int second) => _value = first + second;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public readonly int Read() => _value;
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

	private sealed class RuntimeEquatableOnly : IEquatable<RuntimeEquatableOnly>
	{
		public RuntimeEquatableOnly(int value) => Value = value;

		public int Value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public bool Equals(RuntimeEquatableOnly? other)
		{
			if (other is null)
			{
				return false;
			}
			return other.Value == Value;
		}

		// Deliberately differs from typed equality so the comparer-precedence test
		// cannot accidentally pass through object.Equals.
		public override bool Equals(object? other) => false;

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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int BooleanPhiWithCompanionValuesEntry() =>
		BooleanPhiWithCompanionValues(17) + BooleanPhiWithCompanionValues(3);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int BooleanPhiWithCompanionValues(int value)
	{
		bool accepted;
		int companion;
		if (value > 10)
		{
			accepted = value < 20;
			companion = value + 5;
		}
		else
		{
			accepted = value == 5;
			companion = value + 7;
		}
		return accepted ? companion + 100 : companion + 200;
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
