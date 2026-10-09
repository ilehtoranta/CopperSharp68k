using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Amiga;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Compiler.Tests.MultiModule;

namespace CopperSharp.Compiler.Tests;

public static partial class CompilerFixtures
{
	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static uint AggressiveGuestRead(APTR address, int offset) =>
		APTR.ReadUInt32(address, offset);

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void AggressiveGuestWrite(
		APTR address,
		int offset,
		uint value) =>
		APTR.WriteUInt32(address, offset, value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AggressiveGuestWriteReadEntry()
	{
		var address = APTR.FromPointer(0x0000_3000);
		AggressiveGuestWrite(address, 12, 0x1122_3344);
		return AggressiveGuestRead(address, 12);
	}

	private interface IGuestMemoryFixture
	{
		uint Read(APTR address, int offset);
		void Write(APTR address, int offset, uint value);
	}

	private readonly struct AggressiveGuestMemoryFixture(uint reserved) :
		IGuestMemoryFixture
	{
		private readonly uint _reserved = reserved;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public uint Read(APTR address, int offset) =>
			APTR.ReadUInt32(address, offset);

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public void Write(APTR address, int offset, uint value) =>
			APTR.WriteUInt32(address, offset, value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint AggressiveConstrainedGuestPassThrough<T>(
		ref T memory,
		APTR address,
		int offset,
		uint value)
		where T : struct, IGuestMemoryFixture
	{
		memory.Write(address, offset, value);
		return memory.Read(address, offset);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AggressiveConstrainedGuestPassThroughEntry()
	{
		var memory = new AggressiveGuestMemoryFixture(0);
		return AggressiveConstrainedGuestPassThrough(
			ref memory,
			APTR.FromPointer(0x0000_3000),
			20,
			0x99AA_BBCC);
	}

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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MultiModuleAggressiveGuestReadEntry()
	{
		var address = APTR.FromPointer(0x0000_3000);
		APTR.WriteUInt32(address, 8, 0xCAFE_BABE);
		return ExternalMethods.ReadGuestUInt32(address, 8);
	}

	private sealed class FixtureException : Exception
	{
		[MethodImpl(MethodImplOptions.NoInlining)]
		public string FormatBase() => base.ToString();
	}

	private sealed class FixtureExternalException : System.Runtime.InteropServices.ExternalException
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
	public static int AllocatedLargeDenseSwitchEntry() =>
		LargeDenseSwitch(-1) + LargeDenseSwitch(0) + LargeDenseSwitch(5) +
		LargeDenseSwitch(11) + LargeDenseSwitch(12);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int LargeDenseSwitch(int value) => value switch
	{
		0 => 1, 1 => 2, 2 => 3, 3 => 4,
		4 => 5, 5 => 6, 6 => 7, 7 => 8,
		8 => 9, 9 => 10, 10 => 11, 11 => 12,
		_ => 20,
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int AllocatedSparseSwitchEntry()
	{
		var sum = SparseSwitch(0) + SparseSwitch(6) + SparseSwitch(90) +
			SparseSwitch(1);
		return sum == 18 ? 42 : sum;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int SparseSwitch(int value) => value switch
	{
		0 => 1,
		6 => 2,
		12 => 3,
		18 => 4,
		24 => 5,
		30 => 6,
		36 => 7,
		42 => 8,
		48 => 9,
		54 => 10,
		60 => 11,
		66 => 12,
		72 => 13,
		78 => 14,
		84 => 15,
		90 => 16,
		_ => -1,
	};

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
		return _terminalReadFlag != 0 ? ReadTerminalScalar() : 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadTerminalScalar() => _terminalScalar;

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
	public static uint PointerWrapperInternalAbiEntry() =>
		PointerWrapperInternalAbiTarget(
			APTR.FromPointer(10),
			APTR.FromPointer(20),
			5,
			7);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint PointerWrapperInternalAbiTarget(
		APTR first,
		APTR second,
		uint third,
		uint fourth) =>
		first.Raw + second.Raw + third + fourth;

	private readonly struct MixedBankInstanceAbiFixture(uint seed)
	{
		private readonly uint _seed = seed;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public uint Combine(APTR address, int offset, byte value) =>
			_seed + address.Raw + (uint)offset + value;

		[MethodImpl(MethodImplOptions.NoInlining)]
		public uint CombineNarrowTransport(int first, int second, byte value) =>
			_seed + (uint)first + (uint)second + value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MixedBankInstanceAbiEntry()
	{
		var receiver = new MixedBankInstanceAbiFixture(1);
		return receiver.Combine(APTR.FromPointer(10), 20, 11);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MixedBankNarrowTransportAbiEntry()
	{
		var receiver = new MixedBankInstanceAbiFixture(1);
		return receiver.CombineNarrowTransport(10, 20, 11);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint StackArgumentHomePreservesCallerD7Entry()
	{
		var first = 1u;
		var second = 2u;
		var third = 3u;
		var fourth = 4u;
		var fifth = 5u;
		var index = 0u;
		var sum = 0u;
		var guard = 0u;
		while (index < 36)
		{
			sum += StackArgumentHomePreservesCallerD7Target(
				first,
				second,
				third,
				fourth,
				fifth);
			first++;
			second += 2;
			third += 3;
			fourth += 4;
			fifth += 5;
			guard += index;
			index++;
		}

		return index == 36 &&
			sum == 9_990 &&
			first == 37 &&
			second == 74 &&
			third == 111 &&
			fourth == 148 &&
			fifth == 185 &&
			guard == 630
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint StackArgumentHomePreservesCallerD7Target(
		uint first,
		uint second,
		uint third,
		uint fourth,
		uint fifth)
	{
		var copiedFifth = ReadStackArgumentHome(ref fifth);
		return first + second + third + fourth + copiedFifth;
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
		var fifth = 5u;
		var index = 0u;
		var sum = 0u;
		var guard = 0u;
		while (index < 36)
		{
			sum += AnchoredStackArgumentHomePreservesCallerD7Target(
				first,
				second,
				third,
				fourth,
				fifth);
			first++;
			second += 2;
			third += 3;
			fourth += 4;
			fifth += 5;
			guard += index;
			index++;
		}

		return index == 36 &&
			sum == 9_990 &&
			first == 37 &&
			second == 74 &&
			third == 111 &&
			fourth == 148 &&
			fifth == 185 &&
			guard == 630
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint AnchoredStackArgumentHomePreservesCallerD7Target(
		uint first,
		uint second,
		uint third,
		uint fourth,
		uint fifth)
	{
		var scratchLength = (int)(first & 1u) + 1;
		Span<uint> scratch = stackalloc uint[scratchLength];
		scratch[0] = ReadStackArgumentHome(ref fifth);
		return first + second + third + fourth + scratch[0];
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
			value = (value << 3) ^ (value + 17);
			value = (value << 3) ^ (value + 18);
			value = (value << 3) ^ (value + 19);
			value = (value << 3) ^ (value + 20);
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
			value = (value << 3) ^ (value + 18);
			value = (value << 3) ^ (value + 19);
			value = (value << 3) ^ (value + 20);
			value = (value << 3) ^ (value + 21);
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
			value = (value << 3) ^ (value + 20);
			value = (value << 3) ^ (value + 21);
			value = (value << 3) ^ (value + 22);
			value = (value << 3) ^ (value + 23);
			value = (value << 3) ^ (value + 24);
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

	[StructLayout(LayoutKind.Sequential, Pack = 1, Size = 16)]
	private struct TailFrameAliasContext
	{
		public uint Platform;
		public uint Owner;
		public uint Library;
		public uint State;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint TailFrameAliasEntry()
	{
		TailFrameAliasContext context = default;
		context.Platform = 0x1122_3344;
		context.Owner = 0x5566_7788;
		context.Library = 0x99AA_BBCC;
		context.State = 0xDDEE_F00D;
		return TailFrameAliasTarget(
			ref context.Platform,
			context.Owner,
			context.Library,
			context.State);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint TailFrameAliasTarget(
		ref uint platform,
		uint owner,
		uint library,
		uint state)
	{
		TailFrameAliasContext scratch = default;
		scratch.Platform = owner;
		scratch.Owner = library;
		scratch.Library = state;
		scratch.State = owner ^ library ^ state;
		return platform == 0x1122_3344 &&
			scratch.Platform == 0x5566_7788 &&
			scratch.Owner == 0x99AA_BBCC &&
			scratch.Library == 0xDDEE_F00D
				? 42u
				: 0u;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int RecursiveCalleeSaveEntry() => RecursiveCount(6) + 36;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int OverflowTailCallEntry() => OverflowTailTarget(8, 9, 10, 7, 8);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int OverflowTailTarget(
		int first,
		int second,
		int third,
		int fourth,
		int fifth) =>
		first + second + third + fourth + fifth;

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

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CurrentStackPointerIntrinsicEntry() =>
		ReadCurrentStackPointerIntrinsic();

	[M68kImport("intrinsic:m68k-read-stack-pointer")]
	public static extern uint ReadCurrentStackPointerIntrinsic();

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
	public static int HelloAmigaPromotesDosLibraryBase()
	{
		var dosBase = global::Amiga.Exec.OpenLibrary("dos.library", 0);
		if (dosBase is null)
		{
			return global::Amiga.DOS.RETURN_FAIL;
		}

		global::Amiga.DOS.DOSLibraryBase = dosBase.Value;
		global::Amiga.DOS.PutStr("Hello from CopperSharp.\n");
		global::Amiga.Exec.CloseLibrary(global::Amiga.DOS.DOSLibraryBase);
		global::Amiga.DOS.DOSLibraryBase = global::Amiga.APTR.Null;
		return global::Amiga.DOS.RETURN_OK;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ReadDosLibraryBaseAfterSet()
	{
		global::Amiga.DOS.DOSLibraryBase = 0x0000_3C00;
		return global::Amiga.DOS.DOSLibraryBase.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ResidentDosLibraryBaseFromStartupArgs(
		int argLength,
		global::Amiga.CONST_STRPTR argText)
	{
		global::Amiga.DOS.DOSLibraryBase = unchecked((uint)argLength);
		return global::Amiga.DOS.DOSLibraryBase.Raw;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ResidentDosLibraryBaseAcrossVectorCall(
		int libraryBase,
		global::Amiga.CONST_STRPTR argText)
	{
		global::Amiga.DOS.DOSLibraryBase = unchecked((uint)libraryBase);
		return ResidentDosLibraryBasePutStr();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ResidentDosLibraryBasePutStr()
	{
		global::Amiga.DOS.PutStr("resident invocation state\n");
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
	public static uint TerminalClearDosLibraryBaseEntry()
	{
		global::Amiga.DOS.DOSLibraryBase = APTR.Null;
		return 42;
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
	public static uint BptrAddressRoundTripEntry()
	{
		var address = APTR.FromPointer(0x0000_4800);
		var bptr = BPTR.FromAddress(address);
		return bptr.Address.Raw == address.Raw ? 42u : 0u;
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
	public static int CallAmigaIndirectSubroutine() =>
		AmigaIndirectVectors.Add(
			APTR.FromPointer(0x0000_3500), 19, 23,
			APTR.FromPointer(0x0000_3600));

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

	public static class AmigaIndirectVectors
	{
		[AmigaIndirectCall(M68kRegister.A3)]
		[return: M68kRegister(M68kRegister.D0)]
		public static extern int Add(
			[M68kRegister(M68kRegister.A3)] APTR entry,
			[M68kRegister(M68kRegister.D0)] int left,
			[M68kRegister(M68kRegister.D1)] int right,
			[M68kRegister(M68kRegister.A6)] APTR context);
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
	public static int PromotedReferenceFieldAcrossCollectionEntry()
	{
		var owner = new ManagedNode();
		var child = new ManagedBox { Value = 35 };
		owner.Child = child;
		M68kRuntime.Collect();
		var tail = new int[4];
		tail[0] = 7;
		return ReferenceEquals(owner.Child, child)
			? owner.Child!.Value + tail[0]
			: 0;
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
	public static int IllegalOpcodeDataEntry() => "\u4AFC".Length;

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
	public static int AddressTaken64BitArgumentsEntry() =>
		CheckAddressTaken64BitArguments(unchecked((long)0xFEDCBA9876543210UL), 0x1234567887654321UL);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CheckAddressTaken64BitArguments(long signed, ulong unsigned)
	{
		if (!CheckAndReplaceSigned64BitArgument(ref signed) || !CheckAndReplaceUnsigned64BitArgument(ref unsigned)) return 1;
		var signedLow = M68kRuntime.SplitInt64(signed, out var signedHigh);
		var unsignedLow = M68kRuntime.SplitUInt64(unsigned, out var unsignedHigh);
		return signedHigh == 0x87654321u && signedLow == 0x12345678u &&
			unsignedHigh == 0xABCDEF01u && unsignedLow == 0xFEDCBA98u ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool CheckAndReplaceSigned64BitArgument(ref long value)
	{
		var low = M68kRuntime.SplitInt64(value, out var high);
		if (high != 0xFEDCBA98u || low != 0x76543210u) return false;
		value = unchecked((long)0x8765432112345678UL);
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool CheckAndReplaceUnsigned64BitArgument(ref ulong value)
	{
		var low = M68kRuntime.SplitUInt64(value, out var high);
		if (high != 0x12345678u || low != 0x87654321u) return false;
		value = 0xABCDEF01FEDCBA98UL;
		return true;
	}

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
	public static int CoreLibStringBuilderConstructorEntry()
	{
		var builder = new System.Text.StringBuilder();
		return builder.Length == 0 && builder.Capacity == 16 && builder.MaxCapacity == int.MaxValue ? 42 : 0;
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
	public static int CoreLibStringBuilderIntegerCasesEntry()
	{
		var builder = new System.Text.StringBuilder();
		AppendIntegerCase(builder, 0);
		AppendIntegerCase(builder, 1);
		AppendIntegerCase(builder, -1);
		AppendIntegerCase(builder, 9);
		AppendIntegerCase(builder, 10);
		AppendIntegerCase(builder, -10);
		AppendIntegerCase(builder, 99);
		AppendIntegerCase(builder, 100);
		AppendIntegerCase(builder, -100);
		AppendIntegerCase(builder, int.MinValue);
		AppendIntegerCase(builder, int.MaxValue);
		var snapshot = builder.ToString();
		const string expected = "0|1|-1|9|10|-10|99|100|-100|-2147483648|2147483647|";
		if (snapshot.Length != expected.Length) return 1000 + snapshot.Length;
		for (var index = 0; index < expected.Length; index++)
			if (snapshot[index] != expected[index]) return 2000 + index * 65536 + snapshot[index];
		builder.Append(int.MinValue);
		return snapshot == expected &&
			builder.ToString() == "0|1|-1|9|10|-10|99|100|-100|-2147483648|2147483647|-2147483648" ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void AppendIntegerCase(System.Text.StringBuilder builder, int value)
	{
		builder.Append(value);
		builder.Append('|');
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderIntegerLoopEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 20; index++)
		{
			if (builder.Append(index) != builder) return 1;
			builder.Append('|');
		}
		return builder.ToString() == "0|1|2|3|4|5|6|7|8|9|10|11|12|13|14|15|16|17|18|19|" ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderIntegerSwitchLoopEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 11; index++)
		{
			var value = index switch
			{
				0 => 0, 1 => 1, 2 => -1, 3 => 9, 4 => 10, 5 => -10,
				6 => 99, 7 => 100, 8 => -100, 9 => int.MinValue, _ => int.MaxValue
			};
			if (builder.Append(value) != builder) return 1;
			builder.Append('|');
		}
		return builder.ToString() == "0|1|-1|9|10|-10|99|100|-100|-2147483648|2147483647|" ? 42 : 1000 + builder.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibCharacterSpansEntry()
	{
		char[]? data = new char[3];
		data[0] = 'X'; data[1] = '\u03A9'; data[2] = '\uFFFF';
		var range = new Span<char>(data, 1, 2);
		if (range.Length != 2 || range[0] != '\u03A9' || range[1] != '\uFFFF') return 1;
		data = null;
		M68kRuntime.Collect();
		var replacement = new char[3];
		replacement[1] = 'Y';
		if (range[0] != '\u03A9' || range[1] != '\uFFFF') return 2;
		if (new Span<char>((char[]?)null, 0, 0).Length != 0) return 3;
		if (replacement.AsSpan(3).Length != 0 || replacement.AsSpan(1, 2).Length != 2) return 4;
		try { _ = new Span<char>(replacement, -1, 0); return 5; } catch (ArgumentOutOfRangeException) { }
		try { _ = replacement.AsSpan(2, 2); return 6; } catch (ArgumentOutOfRangeException) { }
		try { _ = new Span<char>((char[]?)null, 0, 1); return 7; } catch (ArgumentOutOfRangeException) { }
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderStringAppendEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		if (builder.Append((string?)null) != builder || builder.Append(string.Empty) != builder || builder.Length != 0) return 1;
		if (builder.Append("A") != builder || builder.Append("BC") != builder || builder.Append("\u03A9") != builder) return 2;
		const string unit = "A\0\u03A9\uD83D\uDE00\uFFFF";
		for (var repeat = 0; repeat < 12; repeat++)
			if (builder.Append(unit) != builder) return 3;
		if (builder.Append(unit, 1, 4) != builder || builder.Append((string?)null, 0, 0) != builder) return 4;
		var snapshot = builder.ToString();
		if (snapshot.Length != 80 || snapshot[0] != 'A' || snapshot[1] != 'B' || snapshot[2] != 'C' || snapshot[3] != '\u03A9') return 5;
		for (var index = 0; index < 72; index++)
			if (snapshot[index + 4] != unit[index % unit.Length]) return 6;
		for (var index = 0; index < 4; index++)
			if (snapshot[index + 76] != unit[index + 1]) return 7;
		M68kRuntime.Collect();
		builder.Append(snapshot);
		builder[0] = 'X';
		var updated = builder.ToString();
		if (snapshot[0] != 'A' || updated.Length != 160 || updated[0] != 'X') return 8;
		for (var index = 0; index < snapshot.Length; index++)
			if (updated[index + snapshot.Length] != snapshot[index]) return 9;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderArrayAndSpanAppendEntry()
	{
		char[]? source = new char[257];
		for (var index = 0; index < source.Length; index++) source[index] = (char)(index * 251);
		var builder = new System.Text.StringBuilder(1);
		if (builder.Append((char[]?)null) != builder || builder.Append((char[]?)null, 0, 0) != builder ||
			builder.Append(default(ReadOnlySpan<char>)) != builder || builder.Length != 0) return 1;
		if (builder.Append(source, 3, 97) != builder || builder.Append(source) != builder) return 2;
		var view = new ReadOnlySpan<char>(source).Slice(100, 157);
		source[3] = 'Z';
		source = null;
		M68kRuntime.Collect();
		if (builder.Append(view) != builder) return 3;
		var snapshot = builder.ToString();
		if (snapshot.Length != 511) return 4;
		for (var index = 0; index < snapshot.Length; index++)
		{
			var originalIndex = index < 97 ? index + 3 : index < 354 ? index - 97 : index - 254;
			if (snapshot[index] != (char)(originalIndex * 251) || builder[index] != snapshot[index]) return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder CreatePatternStringBuilder(int capacity, int length, int phase)
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		var builder = new System.Text.StringBuilder(capacity);
		for (var index = 0; index < length; index++) builder.Append(pattern[(index + phase) % 8]);
		return builder;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderWholeBuilderAppendEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var sourceLayout = 0; sourceLayout < 2; sourceLayout++)
		for (var destinationLayout = 0; destinationLayout < 3; destinationLayout++)
		for (var sizeCase = 0; sizeCase < 4; sizeCase++)
		{
			var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 1 : sizeCase == 2 ? 65 : 257;
			System.Text.StringBuilder? source = CreatePatternStringBuilder(sourceLayout == 0 ? 4 : 512, length, 0);
			var destination = CreatePatternStringBuilder(destinationLayout == 0 ? 1 : destinationLayout == 1 ? 9 : 512, 9, 3);
			var sourceBefore = source.ToString();
			var destinationBefore = destination.ToString();
			var capacity = destination.Capacity;
			if (destination.Append((System.Text.StringBuilder?)null) != destination || destination.Capacity != capacity) return 1;
			if (destination.Append(source) != destination) return 2;
			M68kRuntime.Collect();
			if (destination.Length != 9 + length || source.Length != length) return 3;
			if (length == 0 && destination.Capacity != capacity) return 4;
			for (var index = 0; index < destination.Length; index++)
				if (destination[index] != (index < 9 ? pattern[(index + 3) % 8] : pattern[(index - 9) % 8])) return 5;
			var copied = destination.ToString();
			source.Clear().Append('X');
			M68kRuntime.Collect();
			if (destination.ToString() != copied || destinationBefore.Length != 9 || sourceBefore.Length != length) return 6;
			for (var index = 0; index < length; index++) if (sourceBefore[index] != pattern[index % 8]) return 7;
			destination[0] = 'Z';
			if (copied[0] != pattern[3] || destinationBefore[0] != pattern[3] || source.ToString() != "X") return 8;
			destination.Clear();
			if (destination.Append(source) != destination) return 9;
			source = null;
			M68kRuntime.Collect();
			if (destination.ToString() != "X" || copied.Length != 9 + length) return 10;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderRangedBuilderAppendEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var sourceLayout = 0; sourceLayout < 2; sourceLayout++)
		for (var destinationLayout = 0; destinationLayout < 2; destinationLayout++)
		for (var position = 0; position < 11; position++)
		for (var countCase = 0; countCase < 4; countCase++)
		{
			System.Text.StringBuilder? source = CreatePatternStringBuilder(sourceLayout == 0 ? 4 : 128, 65, 0);
			var destination = CreatePatternStringBuilder(destinationLayout == 0 ? 4 : 128, 17, 3);
			var start = StringBuilderEditPosition(position);
			var remaining = 65 - start;
			var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
				: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
			var sourceBefore = source.ToString();
			var destinationBefore = destination.ToString();
			var capacity = destination.Capacity;
			if (destination.Append(source, start, count) != destination) return 1;
			M68kRuntime.Collect();
			if (destination.Length != 17 + count || source.Length != 65 || (count == 0 && destination.Capacity != capacity)) return 2;
			for (var index = 0; index < destination.Length; index++)
				if (destination[index] != (index < 17 ? pattern[(index + 3) % 8] : pattern[(start + index - 17) % 8])) return 3;
			var copied = destination.ToString();
			source[0] = 'Z';
			source.Clear().Append('X');
			source = null;
			M68kRuntime.Collect();
			if (destination.ToString() != copied || sourceBefore.Length != 65) return 4;
			for (var index = 0; index < 65; index++) if (sourceBefore[index] != pattern[index % 8]) return 5;
			for (var index = 0; index < 17; index++) if (destinationBefore[index] != pattern[(index + 3) % 8]) return 6;
			destination.Clear().Append(CreatePatternStringBuilder(4, 1, 3), 0, 1);
			if (destination.ToString() != "\0" || copied.Length != 17 + count) return 7;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSelfBuilderAppendEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			for (var sizeCase = 0; sizeCase < 4; sizeCase++)
			{
				var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 1 : sizeCase == 2 ? 65 : 257;
				var builder = CreatePatternStringBuilder(layout == 0 ? 4 : 1024, length, 0);
				var before = builder.ToString();
				if (builder.Append(builder) != builder) return 1;
				M68kRuntime.Collect();
				if (builder.Length != 2 * length || before.Length != length) return 2;
				for (var index = 0; index < builder.Length; index++) if (builder[index] != pattern[(index % length) % 8]) return 3;
				for (var index = 0; index < length; index++) if (before[index] != pattern[index % 8]) return 4;
			}
			for (var position = 0; position < 11; position++)
			for (var countCase = 0; countCase < 4; countCase++)
			{
				var builder = CreatePatternStringBuilder(layout == 0 ? 4 : 256, 65, 0);
				var before = builder.ToString();
				var start = StringBuilderEditPosition(position);
				var remaining = 65 - start;
				var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
					: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
				var capacity = builder.Capacity;
				if (builder.Append(builder, start, count) != builder) return 5;
				M68kRuntime.Collect();
				if (builder.Length != 65 + count || (count == 0 && builder.Capacity != capacity)) return 6;
				for (var index = 0; index < builder.Length; index++)
					if (builder[index] != pattern[(index < 65 ? index : start + index - 65) % 8]) return 7;
				for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 8;
				builder.Clear().Append(builder).Append(builder, 0, 0).Append('X');
				if (builder.ToString() != "X" || before.Length != 65) return 9;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder AppendBuilderSource(System.Text.StringBuilder destination,
		System.Text.StringBuilder? source, int start, int count, bool ranged) => ranged
		? destination.Append(source, start, count) : destination.Append(source);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderBuilderAppendValidationEntry()
	{
		var destination = new System.Text.StringBuilder(8, 16).Append("seed");
		var source = new System.Text.StringBuilder(4).Append("text");
		var empty = new System.Text.StringBuilder(4);
		var before = destination.ToString();
		var sourceBefore = source.ToString();
		try { destination.Append(source, -1, 0); return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 2; }
		try { destination.Append(source, 0, -1); return 3; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "count") return 4; }
		try { destination.Append(source, 3, 2); return 5; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 6; }
		try { destination.Append(source, int.MaxValue, 1); return 7; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 8; }
		try { destination.Append(source, 1, int.MaxValue); return 9; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 10; }
		try { destination.Append((System.Text.StringBuilder?)null, 1, 0); return 11; }
		catch (ArgumentNullException error) { if (error.ParamName != "value") return 12; }
		try { destination.Append((System.Text.StringBuilder?)null, 0, 1); return 13; }
		catch (ArgumentNullException error) { if (error.ParamName != "value") return 14; }
		try { destination.Append((System.Text.StringBuilder?)null, -1, 0); return 15; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 16; }
		try { destination.Append((System.Text.StringBuilder?)null, 0, -1); return 17; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "count") return 18; }
		try { destination.Append(empty, 0, 1); return 19; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 20; }
		if (destination.Append((System.Text.StringBuilder?)null) != destination || destination.Append(empty) != destination ||
			destination.Append((System.Text.StringBuilder?)null, 0, 0) != destination || destination.Append(source, 5, 0) != destination ||
			destination.Append(source, int.MaxValue, 0) != destination || destination.Append(empty, int.MaxValue, 0) != destination ||
			destination.Append(destination, int.MaxValue, 0) != destination) return 21;
		if (destination.Length != 4 || destination.Capacity != 8 || destination.ToString() != before || source.ToString() != sourceBefore) return 22;
		for (var operation = 0; operation < 4; operation++)
		{
			var limited = new System.Text.StringBuilder(4, 4).Append("seed");
			try { AppendBuilderSource(limited, operation < 2 ? source : limited, 0, 1, (operation & 1) != 0); return 23; }
			catch (ArgumentOutOfRangeException error)
			{
				if (error.ParamName != (operation < 2 ? "Capacity" : "valueCount") || error.ActualValue is not null) return 24;
			}
			if (limited.Length != 4 || limited.Capacity != 4 || limited.MaxCapacity != 4 || limited.ToString() != "seed") return 25;
			if (limited.Append(source, int.MaxValue, 0) != limited || limited.Append(limited, int.MaxValue, 0) != limited) return 26;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderBuilderEqualsEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var leftLayout = 0; leftLayout < 3; leftLayout++)
		for (var rightLayout = 0; rightLayout < 3; rightLayout++)
		for (var sizeCase = 0; sizeCase < 5; sizeCase++)
		{
			var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 1 : sizeCase == 2 ? 16 : sizeCase == 3 ? 65 : 257;
			var left = CreatePatternStringBuilder(leftLayout == 0 ? 1 : leftLayout == 1 ? 4 : 512, length, 0);
			var right = CreatePatternStringBuilder(rightLayout == 0 ? 1 : rightLayout == 1 ? 4 : 512, length, 0);
			// Keep an empty final chunk in some layouts; comparisons must still traverse earlier chunks.
			if (leftLayout == 1) { left.Append('X'); left.Length = length; }
			if (rightLayout == 1) { right.Append('X'); right.Length = length; }
			var before = left.ToString();
			var leftCapacity = left.Capacity;
			var rightCapacity = right.Capacity;
			M68kRuntime.Collect();
			if (!left.Equals(left) || left.Equals((System.Text.StringBuilder?)null) || !left.Equals(right) || !right.Equals(left)) return 1;
			for (var position = 0; position < 12; position++)
			{
				var index = position == 11 ? length - 1 : StringBuilderEditPosition(position);
				if (index < 0 || index >= length) continue;
				right[index] = 'Z';
				M68kRuntime.Collect();
				if (left.Equals(right) || right.Equals(left) || !right.Equals(right) || left[index] != pattern[index % 8]) return 2;
				right[index] = pattern[index % 8];
				if (!left.Equals(right) || !right.Equals(left)) return 3;
			}
			if (left.Capacity != leftCapacity || right.Capacity != rightCapacity || left.Length != length || right.Length != length) return 4;
			right.Append('X');
			if (left.Equals(right) || right.Equals(left)) return 5;
			right.Length = length;
			M68kRuntime.Collect();
			if (!left.Equals(right) || left.ToString() != before) return 6;
			for (var index = 0; index < length; index++) if (before[index] != pattern[index % 8]) return 7;
			left.Clear().Append('X'); right.Clear().Append('X');
			M68kRuntime.Collect();
			if (!left.Equals(right) || !right.Equals(left) || before.Length != length) return 8;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSpanEqualsEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		Span<char> frame = stackalloc char[257];
		for (var layout = 0; layout < 3; layout++)
		for (var sizeCase = 0; sizeCase < 7; sizeCase++)
		{
			var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 1 : sizeCase == 2 ? 4 : sizeCase == 3 ? 16
				: sizeCase == 4 ? 32 : sizeCase == 5 ? 65 : 257;
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, length, 0);
			if (layout == 1) { builder.Append('X'); builder.Length = length; }
			var before = builder.ToString();
			var capacity = builder.Capacity;
			char[]? storage = new char[length + 2];
			storage[0] = 'L'; storage[length + 1] = 'R';
			for (var index = 0; index < length; index++) storage[index + 1] = pattern[index % 8];
			var writable = new Span<char>(storage, 1, length);
			var retained = new ReadOnlySpan<char>(storage, 1, length);
			storage = null;
			M68kRuntime.Collect();
			var pressure = new char[length + 2];
			pressure[0] = 'P';
			if (!builder.Equals(retained) || !builder.Equals(before.AsSpan()) || builder.Equals(default(ReadOnlySpan<char>)) != (length == 0)) return 1;
			for (var index = 0; index < length; index++) frame[index] = pattern[index % 8];
			M68kRuntime.Collect();
			if (!builder.Equals((ReadOnlySpan<char>)frame.Slice(0, length)) || builder.Equals((ReadOnlySpan<char>)pressure)) return 2;
			for (var position = 0; position < 12; position++)
			{
				var index = position == 11 ? length - 1 : StringBuilderEditPosition(position);
				if (index < 0 || index >= length) continue;
				writable[index] = 'Z';
				M68kRuntime.Collect();
				if (builder.Equals(retained) || !builder.Equals(before.AsSpan()) || builder[index] != pattern[index % 8]) return 3;
				writable[index] = pattern[index % 8];
				if (!builder.Equals(retained)) return 4;
			}
			if (length > 0 && builder.Equals(retained.Slice(0, length - 1))) return 5;
			if (builder.Length != length || builder.Capacity != capacity || builder.ToString() != before || pressure[0] != 'P') return 6;
			builder.Clear().Append('X');
			M68kRuntime.Collect();
			if (!builder.Equals("X".AsSpan()) || builder.Equals(before.AsSpan()) || before.Length != length) return 7;
			for (var index = 0; index < length; index++) if (retained[index] != pattern[index % 8]) return 8;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEqualsContractEntry()
	{
		var limited = new System.Text.StringBuilder(4, 4).Append("text");
		var roomy = new System.Text.StringBuilder(128, 256).Append("text");
		if (!limited.Equals(roomy) || !roomy.Equals(limited) || !limited.Equals("text".AsSpan())) return 1;
		if (limited.Equals((System.Text.StringBuilder?)null) || limited.Equals("Text".AsSpan()) || limited.Equals("text\0".AsSpan())) return 2;
		if (!limited.Equals(limited) || limited.Equals((object)roomy) || !limited.Equals((object)limited) || limited.Equals((object)"text")) return 3;
		var empty = new System.Text.StringBuilder(0);
		if (!empty.Equals(new System.Text.StringBuilder(128)) || !empty.Equals(default(ReadOnlySpan<char>)) || !empty.Equals(string.Empty.AsSpan())) return 4;
		var unicode = new System.Text.StringBuilder(1).Append("\0\u03A9\uD83D\uDE00\uD800\uFFFF");
		if (!unicode.Equals("\0\u03A9\uD83D\uDE00\uD800\uFFFF".AsSpan()) || unicode.Equals("\0\u03C9\uD83D\uDE00\uD800\uFFFF".AsSpan()) ||
			unicode.Equals("\0\u03A9\uDE00\uD83D\uD800\uFFFF".AsSpan())) return 5;
		if (new System.Text.StringBuilder().Append("\u00E9").Equals("e\u0301".AsSpan())) return 6;
		return limited.Length == 4 && limited.Capacity == 4 && limited.MaxCapacity == 4 && roomy.Capacity == 128 && roomy.MaxCapacity == 256 ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEqualsWithoutAllocationEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var leftLayout = 0; leftLayout < 2; leftLayout++)
		for (var rightLayout = 0; rightLayout < 2; rightLayout++)
		{
			var left = CreatePatternStringBuilder(leftLayout == 0 ? 4 : 128, 65, 0);
			var right = CreatePatternStringBuilder(rightLayout == 0 ? 4 : 128, 65, 0);
			var empty = new System.Text.StringBuilder();
			var before = left.ToString();
			var capacity = left.Capacity;
			var chars = new char[67];
			chars[0] = 'L'; chars[66] = 'R';
			for (var index = 0; index < 65; index++) chars[index + 1] = pattern[index % 8];
			var span = new ReadOnlySpan<char>(chars, 1, 65);
			SetStringBuilderAllocationFailure(1);
			if (!left.Equals(right) || !right.Equals(left) || !left.Equals(left) || !left.Equals(span) || !left.Equals(before.AsSpan()) ||
				left.Equals(empty) || left.Equals((System.Text.StringBuilder?)null) || left.Equals(default(ReadOnlySpan<char>)) ||
				!empty.Equals(default(ReadOnlySpan<char>))) { SetStringBuilderAllocationFailure(0); return 1; }
			for (var position = 0; position < 11; position++)
			{
				var index = StringBuilderEditPosition(position);
				if (index >= 65) continue;
				right[index] = 'Z'; chars[index + 1] = 'Z';
				if (left.Equals(right) || right.Equals(left) || left.Equals(span)) { SetStringBuilderAllocationFailure(0); return 2; }
				right[index] = pattern[index % 8]; chars[index + 1] = pattern[index % 8];
				if (!left.Equals(right) || !left.Equals(span)) { SetStringBuilderAllocationFailure(0); return 3; }
			}
			SetStringBuilderAllocationFailure(0);
			if (left.Length != 65 || left.Capacity != capacity || left.ToString() != before || right.ToString() != before || chars[0] != 'L' || chars[66] != 'R') return 4;
			left.Clear().Append('X'); right.Clear().Append('X');
			SetStringBuilderAllocationFailure(1);
			if (!left.Equals(right) || !left.Equals("X".AsSpan())) { SetStringBuilderAllocationFailure(0); return 5; }
			SetStringBuilderAllocationFailure(0);
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibSpanElementOwnersEntry()
	{
		char[]? storage = new char[3];
		storage[0] = '\0'; storage[1] = '\u03A9'; storage[2] = '\uD800';
		var writable = new Span<char>(storage);
		var readOnly = new ReadOnlySpan<char>(storage).Slice(1);
		ref var first = ref writable[0];
		ref readonly var second = ref readOnly[0];
		ref var third = ref writable[2];
		writable = default; readOnly = default; storage = null;
		M68kRuntime.Collect();
		var pressure = new char[3]; pressure[1] = 'P';
		if (first != '\0' || second != '\u03A9' || third != '\uD800') return 1;
		first = 'X'; third = '\uFFFF';
		M68kRuntime.Collect();
		if (first != 'X' || second != '\u03A9' || third != '\uFFFF' || pressure[1] != 'P') return 2;
		var stringSpan = new System.Text.StringBuilder(1).Append("\0\u03A9\uD800").ToString().AsSpan();
		ref readonly var stringCharacter = ref stringSpan[2];
		stringSpan = default;
		M68kRuntime.Collect();
		if (stringCharacter != '\uD800') return 3;
		Span<char> frame = stackalloc char[2];
		frame[0] = 'A'; frame[1] = '\uD800';
		ref var frameCharacter = ref frame[1];
		frame = default;
		M68kRuntime.Collect();
		if (frameCharacter != '\uD800') return 4;
		frameCharacter = 'Z';
		M68kRuntime.Collect();
		if (frameCharacter != 'Z') return 5;
		int[]? integers = new int[2]; integers[0] = 0x12345678; integers[1] = -17;
		Span<int> integerSpan = integers;
		ref var number = ref integerSpan[1];
		integerSpan = default; integers = null;
		M68kRuntime.Collect();
		if (number != -17) return 6;
		number = 42;
		M68kRuntime.Collect();
		return number == 42 && first == 'X' && second == '\u03A9' && third == '\uFFFF' && stringCharacter == '\uD800' ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibOrdinalCharacterEqualityEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uD800\uFFFF";
		for (var sizeCase = 0; sizeCase < 7; sizeCase++)
		{
			var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 1 : sizeCase == 2 ? 4 : sizeCase == 3 ? 16 : sizeCase == 4 ? 32 : sizeCase == 5 ? 65 : 257;
			char[]? left = new char[length + 2];
			char[]? right = new char[length + 4];
			left[0] = 'L'; left[length + 1] = 'L'; right[0] = 'R'; right[length + 3] = 'R';
			for (var index = 0; index < length; index++) left[index + 1] = right[index + 2] = pattern[index % 9];
			var first = new ReadOnlySpan<char>(left, 1, length);
			var second = new Span<char>(right, 2, length);
			var longer = new ReadOnlySpan<char>(right, 1, length + 1);
			left = null; right = null;
			M68kRuntime.Collect();
			if (!CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal(first, second) ||
				!CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal(first, first) ||
				CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal(first, default) != (length == 0) ||
				CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal(first, longer)) return 1;
			for (var index = 0; index < length; index++)
			{
				second[index] = 'Z';
				if (CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal(first, second) || CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal(second, first)) return 2;
				second[index] = pattern[index % 9];
			}
			M68kRuntime.Collect();
			for (var index = 0; index < length; index++) if (first[index] != pattern[index % 9] || second[index] != pattern[index % 9]) return 3;
		}
		return !CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal("A", "a") &&
			!CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal("\u00E9", "e\u0301") &&
			!CopperSharp.Runtime.ShadowCharacterSpans.EqualsOrdinal("\uD83D\uDE00", "\uDE00\uD83D") ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderChunksEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 3; layout++)
		for (var sizeCase = 0; sizeCase < 7; sizeCase++)
		{
			var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 1 : sizeCase == 2 ? 16 : sizeCase == 3 ? 65
				: sizeCase == 4 ? 128 : sizeCase == 5 ? 129 : 257;
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, length, 0);
			var before = builder.ToString(0, builder.Length);
			var capacity = builder.Capacity;
			var position = 0;
			var chunks = 0;
			foreach (var chunk in builder.GetChunks())
			{
				M68kRuntime.Collect();
				var span = chunk.Span;
				for (var index = 0; index < span.Length; index++)
					if (span[index] != pattern[(position + index) % 8]) return 1;
				position += span.Length;
				chunks++;
			}
			if (position != length) return 100 + layout * 10 + sizeCase;
			if (chunks < 1) return 200 + layout * 10 + sizeCase;
			if (builder.Length != length) return 300 + layout * 10 + sizeCase;
			if (builder.Capacity != capacity) return 400 + layout * 10 + sizeCase;
			if (builder.ToString(0, builder.Length) != before) return 500 + layout * 10 + sizeCase;
			if (layout == 2 && chunks != 1 || layout == 0 && length == 65 && chunks != 8 || layout == 0 && length == 129 && chunks != 9) return 3;
			var enumerator = builder.GetChunks();
			position = 0;
			while (enumerator.MoveNext())
			{
				var current = enumerator.Current;
				var copied = current;
				current = default;
				M68kRuntime.Collect();
				var span = copied.Span;
				for (var index = 0; index < span.Length; index++) if (span[index] != before[position + index]) return 4;
				position += span.Length;
			}
			if (position != length || enumerator.MoveNext() || enumerator.MoveNext()) return 5;
			builder.Clear().Append('X');
			var reused = builder.GetChunks();
			if (!reused.MoveNext() || reused.Current.Length != 1 || reused.Current.Span[0] != 'X' || reused.MoveNext()) return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinEntry()
	{
		for (var layout = 0; layout < 3; layout++)
		for (var valueCase = 0; valueCase < 6; valueCase++)
		for (var separatorCase = 0; separatorCase < 6; separatorCase++)
		{
			var values = CreateAppendJoinValues(valueCase);
			var separator = separatorCase == 0 ? null : separatorCase == 1 ? "" : separatorCase == 2 ? "|"
				: separatorCase == 3 ? new System.Text.StringBuilder(5).Append("\u03A9\0\uD83D\uDE00\uFFFF").ToString(0, 5)
				: separatorCase == 4 ? "\0" : "\u03A9";
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			var result = separatorCase < 4 ? builder.AppendJoin(separator, values) : builder.AppendJoin(separator![0], values);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinMatches(builder, before, values, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (separatorCase < 4) builder.AppendJoin(separator, values); else builder.AppendJoin(separator![0], values);
			M68kRuntime.Collect();
			if (!AppendJoinMatches(builder, "", values, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != before.Length + builder.Length - 1 || builder[builder.Length - 1] != 'X') return 3;
			for (var index = 0; index < before.Length; index++) if (snapshot[index] != before[index]) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[before.Length + index] != builder[index]) return 3;
			if (valueCase == 5)
			{
				const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
				for (var index = 0; index < 65; index++) if (values[0]![index] != pattern[index % 8]) return 4;
				for (var index = 0; index < 129; index++) if (values[3]![index] != pattern[index % 8]) return 5;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinSpanEntry()
	{
		for (var layout = 0; layout < 3; layout++)
		for (var valueCase = 0; valueCase < 6; valueCase++)
		for (var separatorCase = 0; separatorCase < 6; separatorCase++)
		{
			string?[]? original = CreateAppendJoinValues(valueCase);
			string?[]? storage = new string?[original.Length + 2];
			storage[0] = "excluded-left"; storage[storage.Length - 1] = "excluded-right";
			for (var index = 0; index < original.Length; index++) storage[index + 1] = original[index];
			var values = new ReadOnlySpan<string?>(storage).Slice(1, original.Length);
			original = null; storage = null;
			M68kRuntime.Collect();
			var separator = separatorCase == 0 ? null : separatorCase == 1 ? "" : separatorCase == 2 ? "|"
				: separatorCase == 3 ? new System.Text.StringBuilder(5).Append("\u03A9\0\uD83D\uDE00\uFFFF").ToString(0, 5)
				: separatorCase == 4 ? "\0" : "\u03A9";
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			var result = separatorCase < 4 ? builder.AppendJoin(separator, values) : builder.AppendJoin(separator![0], values);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinSpanMatches(builder, before, values, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (separatorCase < 4) builder.AppendJoin(separator, values); else builder.AppendJoin(separator![0], values);
			M68kRuntime.Collect();
			if (!AppendJoinSpanMatches(builder, "", values, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != before.Length + builder.Length - 1 || builder[builder.Length - 1] != 'X') return 3;
			for (var index = 0; index < before.Length; index++) if (snapshot[index] != before[index]) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[before.Length + index] != builder[index]) return 3;
			if (valueCase == 5)
			{
				const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
				for (var index = 0; index < 65; index++) if (values[0]![index] != pattern[index % 8]) return 4;
				for (var index = 0; index < 129; index++) if (values[3]![index] != pattern[index % 8]) return 5;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AppendJoinSpanMatches(System.Text.StringBuilder builder, string prefix, ReadOnlySpan<string?> values, string? separator)
	{
		var position = 0;
		for (var index = 0; index < prefix.Length; index++) if (position >= builder.Length || builder[position++] != prefix[index]) return false;
		for (var item = 0; item < values.Length; item++)
		{
			if (item != 0 && separator != null)
				for (var index = 0; index < separator.Length; index++) if (position >= builder.Length || builder[position++] != separator[index]) return false;
			var value = values[item];
			if (value != null)
				for (var index = 0; index < value.Length; index++) if (position >= builder.Length || builder[position++] != value[index]) return false;
		}
		return position == builder.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinSpanLifetimeEntry()
	{
		var returned = CreateRetainedAppendJoinSpan();
		var copied = CopyAppendJoinSpan(returned);
		returned = default;
		M68kRuntime.Collect();
		var pressure = new string?[64]; pressure[0] = "pressure";
		var builder = new System.Text.StringBuilder(1).Append("seed");
		if (AppendRetainedJoinSpan(builder, copied, false) != builder || !AppendJoinSpanMatches(builder, "seed", copied, "|")) return 1;
		builder.Clear();
		if (AppendRetainedJoinSpan(builder, copied, true) != builder || !AppendJoinSpanMatches(builder, "", copied, "|")) return 2;
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		if (copied.Length != 4 || copied[1] != null || copied[2] != "" || copied[0]!.Length != 65 || copied[3]!.Length != 129) return 3;
		for (var index = 0; index < 65; index++) if (copied[0]![index] != pattern[index % 8]) return 4;
		for (var index = 0; index < 129; index++) if (copied[3]![index] != pattern[index % 8]) return 5;
		return pressure[0] == "pressure" ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<string?> CreateRetainedAppendJoinSpan()
	{
		var storage = new[] { "excluded-left", CreatePatternStringBuilder(1, 65, 0).ToString(0, 65), null, "",
			CreatePatternStringBuilder(1, 129, 0).ToString(0, 129), "excluded-right" };
		return new ReadOnlySpan<string?>(storage, 1, 4);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<string?> CopyAppendJoinSpan(ReadOnlySpan<string?> value)
	{
		M68kRuntime.Collect();
		return value.Slice(0, value.Length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder AppendRetainedJoinSpan(System.Text.StringBuilder builder, ReadOnlySpan<string?> values, bool character)
	{
		M68kRuntime.Collect();
		return character ? builder.AppendJoin('|', values) : builder.AppendJoin("|", values);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinSpanContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		var empty = default(ReadOnlySpan<string?>);
		if (builder.AppendJoin("|", empty) != builder || builder.AppendJoin('|', empty) != builder) return 1;
		string?[]? missing = null;
		if (builder.AppendJoin("|", new ReadOnlySpan<string?>(missing)) != builder) return 2;
		if (builder.AppendJoin('|', new ReadOnlySpan<string?>(missing, 0, 0)) != builder) return 2;
		var storage = new[] { "excluded-left", null, "", "A", null, "excluded-right" };
		var values = new ReadOnlySpan<string?>(storage, 1, 4);
		if (builder.AppendJoin('\0', values.Slice(values.Length, 0)) != builder || builder.ToString(0, builder.Length) != "seed") return 3;
		if (builder.AppendJoin((string?)null, values) != builder || builder.ToString(0, builder.Length) != "seedA") return 4;
		builder.Length = 4;
		if (builder.AppendJoin("", values) != builder || builder.ToString(0, builder.Length) != "seedA") return 5;
		builder.Length = 4;
		if (builder.AppendJoin('\0', values) != builder || builder.ToString(0, builder.Length) != "seed\0\0A\0") return 6;
		if (storage[0] != "excluded-left" || storage[1] != null || storage[2] != "" || storage[3] != "A" || storage[4] != null || storage[5] != "excluded-right") return 7;
		for (var invalid = 0; invalid < 6; invalid++)
		{
			var start = invalid == 0 ? -1 : invalid == 1 ? int.MaxValue : invalid == 2 ? storage.Length + 1 : invalid == 3 ? 1 : 0;
			var length = invalid == 3 ? storage.Length : invalid == 4 ? -1 : invalid == 5 ? int.MaxValue : 0;
			try { builder.AppendJoin('|', new ReadOnlySpan<string?>(storage, start, length)); return 12; }
			catch (ArgumentOutOfRangeException) { }
			if (builder.ToString(0, builder.Length) != "seed\0\0A\0") return 13;
		}
		try { builder.AppendJoin('|', new ReadOnlySpan<string?>(missing, 1, 0)); return 14; }
		catch (ArgumentOutOfRangeException) { }
		try { builder.AppendJoin('|', new ReadOnlySpan<string?>(missing, 0, 1)); return 15; }
		catch (ArgumentOutOfRangeException) { }
		for (var character = 0; character < 2; character++)
		{
			var limited = new System.Text.StringBuilder(4, 8).Append("seed");
			var parts = new ReadOnlySpan<string?>(new[] { "excluded-left", "a", "bc", "def", "excluded-right" }, 1, 3);
			try
			{
				if (character == 0) limited.AppendJoin("|", parts); else limited.AppendJoin('|', parts);
				return 8;
			}
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 9; }
			if (limited.Length != 8 || limited.Capacity != 8 || limited.MaxCapacity != 8 || limited.ToString(0, 8) != "seeda|bc") return 10;
			limited.Clear().AppendJoin('|', parts.Slice(0, 2));
			if (limited.ToString(0, limited.Length) != "a|bc") return 11;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinSpanAllocationFailureEntry()
	{
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(512).Append("seed");
			var values = CreateRetainedAppendJoinSpan();
			var separator = "\u03A9\0\uD83D\uDE00\uFFFF";
			SetStringBuilderAllocationFailure(1);
			if (character == 0) available.AppendJoin(separator, values); else available.AppendJoin('\u03A9', values);
			available.AppendJoin((string?)null, default(ReadOnlySpan<string?>));
			available.AppendJoin('\0', values.Slice(values.Length, 0));
			SetStringBuilderAllocationFailure(0);
			if (!AppendJoinSpanMatches(available, "seed", values, character == 0 ? separator : "\u03A9")) return 1;
			for (var scenario = 0; scenario < (character == 0 ? 4 : 3); scenario++)
			for (var failAt = 1; failAt <= 2; failAt++)
			{
				var capacity = scenario == 0 ? 4 : scenario == 2 ? 6 : 8;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var storage = new[] { "excluded-left", "AB", "CD", "excluded-right" };
				var parts = new ReadOnlySpan<string?>(storage, 1, 2);
				var delimiter = scenario == 3 ? separator : "|";
				var retained = scenario == 0 ? "seed" : scenario == 1 ? "seedAB|C" : scenario == 2 ? "seedAB" : "seedAB\u03A9\0";
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (character == 0) builder.AppendJoin(delimiter, parts); else builder.AppendJoin('|', parts);
					SetStringBuilderAllocationFailure(0); return 2;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (builder.Capacity != capacity || builder.Length != retained.Length || builder.ToString(0, builder.Length) != retained) return 3;
				if (storage[0] != "excluded-left" || storage[1] != "AB" || storage[2] != "CD" || storage[3] != "excluded-right") return 4;
				builder.Length = 4;
				if (character == 0) builder.AppendJoin(delimiter, parts); else builder.AppendJoin('|', parts);
				if (!AppendJoinSpanMatches(builder, "seed", parts, delimiter)) return 5;
				builder.Clear().AppendJoin('|', parts);
				if (builder.ToString(0, builder.Length) != "AB|CD") return 6;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnumerableArrayJoinEntry()
		=> CoreLibStringBuilderEnumerableJoinMatrix(false);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectArrayJoinEntry()
		=> CoreLibStringBuilderObjectJoinMatrix(false);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectSpanJoinEntry()
		=> CoreLibStringBuilderObjectJoinMatrix(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool ObjectInsertionMatches(System.Text.StringBuilder builder, string before, string?[] expected, int position)
	{
		var offset = 0;
		for (var index = 0; index < position; index++) if (offset >= builder.Length || builder[offset++] != before[index]) return false;
		for (var part = 0; part < expected.Length; part++)
		{
			var text = expected[part];
			if (text == null) continue;
			for (var index = 0; index < text.Length; index++) if (offset >= builder.Length || builder[offset++] != text[index]) return false;
		}
		for (var index = position; index < before.Length; index++) if (offset >= builder.Length || builder[offset++] != before[index]) return false;
		return offset == builder.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertObjectEntry()
	{
		for (var layout = 0; layout < 3; layout++)
		for (var positionCase = 0; positionCase < 3; positionCase++)
		for (var scenario = 0; scenario < 7; scenario++)
		{
			var values = scenario < 6 ? CreateObjectJoinValues(scenario) : new object?[] {
				new CustomObjectJoinValue(CreatePatternStringBuilder(1, 65, 0).ToString(0, 65)),
				new DerivedObjectJoinValue(), new InheritedObjectJoinValue(), new HidingObjectJoinValue(),
				new OverrideHidingObjectJoinValue(), new GenericObjectJoinValue<int>("G1"),
				new GenericObjectJoinValue<string>("G2"), new NullObjectJoinValue(), new ReentrantObjectJoinValue() };
			var expected = scenario < 6 ? CreateObjectJoinExpected(scenario) : new string?[] {
				CreatePatternStringBuilder(1, 65, 0).ToString(0, 65), "BD", "I", "H", "H", "G1", "G2", null, "N|7" };
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			var position = positionCase == 0 ? 0 : positionCase == 1 ? 9 : before.Length;
			var offset = position;
			M68kRuntime.Collect();
			for (var index = 0; index < values.Length; index++)
			{
				if (builder.Insert(offset, values[index]) != builder) return 1;
				offset += expected[index]?.Length ?? 0;
			}
			M68kRuntime.Collect();
			if (!ObjectInsertionMatches(builder, before, expected, position)) return 2;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Remove(position, offset - position);
			if (builder.ToString(0, builder.Length) != before) return 3;
			builder.Clear(); offset = 0;
			for (var index = 0; index < values.Length; index++)
			{
				builder.Insert(offset, values[index]); offset += expected[index]?.Length ?? 0;
			}
			M68kRuntime.Collect();
			if (!ObjectInsertionMatches(builder, "", expected, 0) || snapshot.Length != before.Length + builder.Length) return 4;
			for (var index = 0; index < position; index++) if (snapshot[index] != before[index]) return 5;
			for (var index = 0; index < builder.Length; index++) if (snapshot[position + index] != builder[index]) return 6;
			for (var index = position; index < before.Length; index++) if (snapshot[builder.Length + index] != before[index]) return 7;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertObjectContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		var throwing = new ThrowingObjectJoinValue();
		for (var scenario = 0; scenario < 3; scenario++)
		{
			var position = scenario == 0 ? -1 : scenario == 1 ? builder.Length + 1 : int.MaxValue;
			if (builder.Insert(position, (object?)null) != builder) return 1;
			try { builder.Insert(position, (object)throwing); return 2; }
			catch (InvalidOperationException error) { if (error != throwing.Error) return 3; }
			if (throwing.Calls != scenario + 1 || builder.ToString(0, builder.Length) != "seed") return 4;
			for (var kind = 0; kind < 2; kind++)
			{
				object value = kind == 0 ? "" : new NullObjectJoinValue();
				try { builder.Insert(position, value); return 5; }
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 6; }
			}
		}
		throwing.Fail = false;
		try { builder.Insert(-1, (object)throwing); return 7; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 8; }
		if (throwing.Calls != 4 || builder.ToString(0, builder.Length) != "seed") return 9;
		if (builder.Insert(2, (object)throwing) != builder || throwing.Calls != 5 || builder.ToString(0, builder.Length) != "seBed") return 10;
		builder.Remove(2, 1);
		if (builder.Insert(4, (object?)null) != builder || builder.Insert(0, (object)"") != builder ||
			builder.Insert(2, (object)new NullObjectJoinValue()) != builder || builder.ToString(0, builder.Length) != "seed") return 11;
		try { builder.Insert(2, (object)"ABCDE"); return 12; } catch (OutOfMemoryException) { }
		if (builder.ToString(0, builder.Length) != "seed" || builder.Capacity != 8) return 13;
		builder.Clear().Insert(0, (object)throwing);
		return throwing.Calls == 6 && builder.ToString(0, builder.Length) == "B" ? 42 : 14;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder InsertRetainedObject(System.Text.StringBuilder builder, object value)
	{
		M68kRuntime.Collect();
		return builder.Insert(9, value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertObjectLifetimeEntry()
	{
		for (var scenario = 0; scenario < 2; scenario++)
		{
			var value = CreateRetainedAppendObject(scenario);
			var builder = CreatePatternStringBuilder(1, 17, 3);
			var before = builder.ToString(0, builder.Length);
			M68kRuntime.Collect();
			var pressure = new object?[64]; pressure[0] = "pressure";
			if (InsertRetainedObject(builder, value) != builder) return 1;
			M68kRuntime.Collect();
			var expected = scenario == 0 ? ((CustomObjectJoinValue)value).Text : ((GenericObjectJoinValue<string>)value).Text;
			if (!ObjectInsertionMatches(builder, before, new[] { expected }, 9) || pressure[0] != (object)"pressure") return 2;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertObjectMutationEntry()
	{
		var builder = new System.Text.StringBuilder(1).Append("seed");
		var value = new MutatingObjectInsertionValue(builder);
		try { builder.Insert(4, (object)value); return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 2; }
		if (value.Calls != 1 || builder.ToString(0, builder.Length) != "Q") return 3;
		value.Grow = true;
		if (builder.Insert(4, (object)value) != builder || value.Calls != 2 || builder.ToString(0, builder.Length) != "wideBr") return 4;
		builder.Clear().Insert(0, (object)value);
		return value.Calls == 3 && builder.ToString(0, builder.Length) == "Bwider" ? 42 : 5;
	}

	public sealed class MutatingObjectInsertionValue
	{
		private readonly System.Text.StringBuilder _builder;
		public int Calls;
		public bool Grow;
		public MutatingObjectInsertionValue(System.Text.StringBuilder builder) => _builder = builder;
		public override string ToString()
		{
			Calls++;
			_builder.Clear().Append(Grow ? "wider" : "Q");
			return new System.Text.StringBuilder(1).Append("B").ToString(0, 1);
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertObjectRejectsUnknownEntry()
	{
		{
			var builder = new System.Text.StringBuilder(64).Append("seed");
			object value = new RuntimeDerivedObjectJoinValue();
			try { builder.Insert(-1, value); return 1; } catch (NotSupportedException) { }
			try { builder.Insert(2, value); return 2; } catch (NotSupportedException) { }
			if (builder.ToString(0, builder.Length) != "seed") return 3;
			builder.Insert(2, (object)42);
			if (builder.ToString(0, builder.Length) != "se42ed") return 4;
			builder.Clear().Insert(0, (object)true);
			if (builder.ToString(0, builder.Length) != "True") return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertObjectAllocationFailureEntry()
	{
		var available = new System.Text.StringBuilder(64).Append("seed");
		object literal = "A", yes = true, no = false, custom = new BaseObjectJoinValue("BC");
		SetStringBuilderAllocationFailure(1);
		available.Insert(2, literal).Insert(3, yes).Insert(-1, (object?)null).Insert(7, no).Insert(12, custom);
		SetStringBuilderAllocationFailure(0);
		if (available.ToString(0, available.Length) != "seATrueFalseBCed") return 1;
		for (var positionCase = 0; positionCase < 3; positionCase++)
		for (var scenario = 0; scenario < 5; scenario++)
		for (var failAt = 1; failAt <= (scenario < 2 ? 3 : scenario == 4 ? 2 : 1); failAt++)
		{
			var position = positionCase * 2;
			var capacity = scenario < 2 || scenario == 4 ? 4 : 64;
			var builder = new System.Text.StringBuilder(capacity).Append("seed");
			var allocating = new AllocatingObjectJoinValue();
			object value = scenario == 0 || scenario == 3 ? allocating : scenario == 1 ? (object)12 : scenario == 2 ? (object)'B' : "BC";
			var expected = scenario == 1 ? "12" : scenario == 2 ? "B" : "BC";
			SetStringBuilderAllocationFailure(failAt);
			try { builder.Insert(position, value); SetStringBuilderAllocationFailure(0); return 2; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.Capacity != capacity || builder.ToString(0, builder.Length) != "seed" ||
				allocating.Calls != (scenario == 0 || scenario == 3 ? 1 : 0)) return 3;
			if (builder.Insert(position, value) != builder || !ObjectInsertionMatches(builder, "seed", new[] { expected }, position)) return 4;
			builder.Clear().Insert(0, value);
			if (builder.ToString(0, builder.Length) != expected || allocating.Calls != (scenario == 0 || scenario == 3 ? 3 : 0)) return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendObjectEntry()
	{
		for (var layout = 0; layout < 3; layout++)
		for (var scenario = 0; scenario < 7; scenario++)
		{
			var values = scenario < 6 ? CreateObjectJoinValues(scenario) : new object?[] {
				new CustomObjectJoinValue(CreatePatternStringBuilder(1, 65, 0).ToString(0, 65)),
				new DerivedObjectJoinValue(), new InheritedObjectJoinValue(), new HidingObjectJoinValue(),
				new OverrideHidingObjectJoinValue(), new GenericObjectJoinValue<int>("G1"),
				new GenericObjectJoinValue<string>("G2"), new NullObjectJoinValue(), new ReentrantObjectJoinValue() };
			var expected = scenario < 6 ? CreateObjectJoinExpected(scenario) : new string?[] {
				CreatePatternStringBuilder(1, 65, 0).ToString(0, 65), "BD", "I", "H", "H", "G1", "G2", null, "N|7" };
			var builder = new System.Text.StringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512).Append("seed");
			M68kRuntime.Collect();
			for (var index = 0; index < values.Length; index++) if (builder.Append(values[index]) != builder) return 1;
			M68kRuntime.Collect();
			if (!AppendJoinMatches(builder, "seed", expected, "")) return 2;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			for (var index = 0; index < values.Length; index++) builder.Append(values[index]);
			builder.Append('X'); M68kRuntime.Collect();
			if (snapshot.Length != builder.Length + 3) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[index + 4] != builder[index]) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendObjectContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		if (builder.Append((object?)null) != builder || builder.Append((object)"") != builder ||
			builder.Append((object)new NullObjectJoinValue()) != builder || builder.ToString(0, builder.Length) != "seed") return 1;
		var throwing = new ThrowingObjectJoinValue();
		try { builder.Append((object)throwing); return 2; }
		catch (InvalidOperationException error) { if (error != throwing.Error) return 3; }
		if (throwing.Calls != 1 || builder.ToString(0, builder.Length) != "seed") return 4;
		throwing.Fail = false;
		if (builder.Append((object)throwing) != builder || throwing.Calls != 2 || builder.ToString(0, builder.Length) != "seedB") return 5;
		builder.Length = 4;
		try { builder.Append((object)"ABCDE"); return 6; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 7; }
		if (builder.ToString(0, builder.Length) != "seed" || builder.Capacity != 8) return 8;
		builder.Clear().Append((object)throwing);
		return throwing.Calls == 3 && builder.ToString(0, builder.Length) == "B" ? 42 : 9;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object CreateRetainedAppendObject(int scenario)
	{
		var text = CreatePatternStringBuilder(1, scenario == 0 ? 65 : 129, 0).ToString(0, scenario == 0 ? 65 : 129);
		return scenario == 0 ? new CustomObjectJoinValue(text) : new GenericObjectJoinValue<string>(text);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder AppendRetainedObject(System.Text.StringBuilder builder, object value)
	{
		M68kRuntime.Collect();
		return builder.Append(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendObjectLifetimeEntry()
	{
		for (var scenario = 0; scenario < 2; scenario++)
		{
			var value = CreateRetainedAppendObject(scenario);
			var builder = new System.Text.StringBuilder(1).Append("seed");
			M68kRuntime.Collect();
			var pressure = new object?[64]; pressure[0] = "pressure";
			if (AppendRetainedObject(builder, value) != builder) return 1;
			M68kRuntime.Collect();
			var expected = scenario == 0 ? ((CustomObjectJoinValue)value).Text : ((GenericObjectJoinValue<string>)value).Text;
			if (builder.Length != expected.Length + 4 || pressure[0] != (object)"pressure") return 2;
			for (var index = 0; index < expected.Length; index++) if (builder[index + 4] != expected[index]) return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendObjectRejectsUnknownEntry()
	{
		{
			var builder = new System.Text.StringBuilder(64).Append("seed");
			object value = new RuntimeDerivedObjectJoinValue();
			try { builder.Append(value); return 1; } catch (NotSupportedException) { }
			if (builder.ToString(0, builder.Length) != "seed") return 2;
			builder.Append((object)42);
			if (builder.ToString(0, builder.Length) != "seed42") return 3;
			builder.Clear().Append((object)true);
			if (builder.ToString(0, builder.Length) != "True") return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendObjectAllocationFailureEntry()
	{
		var available = new System.Text.StringBuilder(64).Append("seed");
		object literal = "A", yes = true, no = false, custom = new BaseObjectJoinValue("BC");
		SetStringBuilderAllocationFailure(1);
		available.Append(literal).Append(yes).Append((object?)null).Append(no).Append(custom);
		SetStringBuilderAllocationFailure(0);
		if (available.ToString(0, available.Length) != "seedATrueFalseBC") return 1;
		for (var scenario = 0; scenario < 5; scenario++)
		for (var failAt = 1; failAt <= (scenario < 2 ? 3 : scenario == 4 ? 2 : 1); failAt++)
		{
			var capacity = scenario < 2 || scenario == 4 ? 4 : 64;
			var builder = new System.Text.StringBuilder(capacity).Append("seed");
			var allocating = new AllocatingObjectJoinValue();
			object value = scenario == 0 || scenario == 3 ? allocating : scenario == 1 ? (object)12 : scenario == 2 ? (object)'B' : "BC";
			var expected = scenario == 1 ? "12" : scenario == 2 ? "B" : "BC";
			SetStringBuilderAllocationFailure(failAt);
			try { builder.Append(value); SetStringBuilderAllocationFailure(0); return 2; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.Capacity != capacity || builder.ToString(0, builder.Length) != "seed" ||
				allocating.Calls != (scenario == 0 || scenario == 3 ? 1 : 0)) return 3;
			if (builder.Append(value) != builder || builder.Length != 4 + expected.Length) return 4;
			for (var index = 0; index < expected.Length; index++) if (builder[4 + index] != expected[index]) return 5;
			builder.Clear().Append(value);
			if (builder.ToString(0, builder.Length) != expected || allocating.Calls != (scenario == 0 || scenario == 3 ? 3 : 0)) return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object?[] CreateObjectJoinValues(int scenario)
	{
		if (scenario == 0) return new object?[0];
		if (scenario == 1) return new object?[] { null };
		if (scenario == 2) return new object?[] { null, "", null };
		if (scenario == 3) return new object?[] { "\u03A9\0\uD83D\uDE00\uFFFF", '\uD800', '\0', true, false };
		if (scenario == 5) return new object?[] { CreatePatternStringBuilder(1, 65, 0).ToString(0, 65),
			null, 42, CreatePatternStringBuilder(1, 129, 0).ToString(0, 129) };
		return new object?[] { "A", null, "", "\u03A9\0", true, false, '\uFFFF', '\0',
			sbyte.MinValue, sbyte.MaxValue, byte.MaxValue, short.MinValue, short.MaxValue, ushort.MaxValue,
			int.MinValue, int.MaxValue, uint.MaxValue, long.MinValue, long.MaxValue, ulong.MaxValue, 0L, 0UL };
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string?[] CreateObjectJoinExpected(int scenario)
	{
		if (scenario == 0) return new string?[0];
		if (scenario == 1) return new string?[] { null };
		if (scenario == 2) return new string?[] { null, "", null };
		if (scenario == 3) return new[] { "\u03A9\0\uD83D\uDE00\uFFFF", "\uD800", "\0", "True", "False" };
		if (scenario == 5) return new[] { CreatePatternStringBuilder(1, 65, 0).ToString(0, 65),
			null, "42", CreatePatternStringBuilder(1, 129, 0).ToString(0, 129) };
		return new[] { "A", null, "", "\u03A9\0", "True", "False", "\uFFFF", "\0", "-128", "127", "255",
			"-32768", "32767", "65535", "-2147483648", "2147483647", "4294967295", "-9223372036854775808",
			"9223372036854775807", "18446744073709551615", "0", "0" };
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CoreLibStringBuilderObjectJoinMatrix(bool span)
	{
		for (var layout = 0; layout < 3; layout++)
		for (var valueCase = 0; valueCase < 6; valueCase++)
		for (var separatorCase = 0; separatorCase < 6; separatorCase++)
		{
			var values = CreateObjectJoinValues(valueCase);
			var expected = CreateObjectJoinExpected(valueCase);
			var storage = new object?[values.Length + 2];
			storage[0] = "excluded-left"; storage[storage.Length - 1] = "excluded-right";
			for (var index = 0; index < values.Length; index++) storage[index + 1] = values[index];
			var view = new ReadOnlySpan<object?>(storage, 1, values.Length);
			storage = null;
			var separator = separatorCase == 0 ? null : separatorCase == 1 ? "" : separatorCase == 2 ? "|"
				: separatorCase == 3 ? CreatePatternStringBuilder(1, 5, 3).ToString(0, 5) : separatorCase == 4 ? "\0" : "\u03A9";
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			M68kRuntime.Collect();
			var result = span ? separatorCase < 4 ? builder.AppendJoin(separator, view) : builder.AppendJoin(separator![0], view)
				: separatorCase < 4 ? builder.AppendJoin(separator, values) : builder.AppendJoin(separator![0], values);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinMatches(builder, before, expected, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (span) { if (separatorCase < 4) builder.AppendJoin(separator, view); else builder.AppendJoin(separator![0], view); }
			else { if (separatorCase < 4) builder.AppendJoin(separator, values); else builder.AppendJoin(separator![0], values); }
			M68kRuntime.Collect();
			if (!AppendJoinMatches(builder, "", expected, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != before.Length + builder.Length - 1) return 3;
			for (var index = 0; index < before.Length; index++) if (snapshot[index] != before[index]) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[before.Length + index] != builder[index]) return 3;
			for (var index = 0; index < values.Length; index++) if (view[index] != values[index]) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectJoinLifetimeEntry()
	{
		var view = CopyObjectJoinSpan(CreateRetainedObjectJoinSpan());
		var copied = CopyObjectJoinSpan(view);
		M68kRuntime.Collect();
		var pressure = new object?[64]; pressure[0] = "pressure";
		for (var character = 0; character < 2; character++)
		{
			var builder = new System.Text.StringBuilder(1).Append("seed");
			if (AppendRetainedObjectJoinSpan(builder, copied, character != 0) != builder) return 1;
			M68kRuntime.Collect();
			if (!AppendJoinMatches(builder, "seed", CreateObjectJoinExpected(5), "|")) return 2;
		}
		return pressure[0] == (object)"pressure" && view[2] == copied[2] ? 42 : 3;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<object?> CreateRetainedObjectJoinSpan()
	{
		var values = CreateObjectJoinValues(5);
		var storage = new object?[values.Length + 2];
		storage[0] = "excluded-left"; storage[storage.Length - 1] = "excluded-right";
		for (var index = 0; index < values.Length; index++) storage[index + 1] = values[index];
		return new ReadOnlySpan<object?>(storage, 1, values.Length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<object?> CopyObjectJoinSpan(ReadOnlySpan<object?> values)
	{
		M68kRuntime.Collect();
		return values.Slice(0, values.Length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder AppendRetainedObjectJoinSpan(System.Text.StringBuilder builder, ReadOnlySpan<object?> values, bool character)
	{
		M68kRuntime.Collect();
		return character ? builder.AppendJoin('|', values) : builder.AppendJoin("|", values);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectJoinContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		object?[]? missing = null;
		try { builder.AppendJoin("|", missing!); return 1; } catch (ArgumentNullException error) { if (error.ParamName != "values") return 2; }
		try { builder.AppendJoin('|', missing!); return 1; } catch (ArgumentNullException error) { if (error.ParamName != "values") return 2; }
		if (builder.AppendJoin("|", default(ReadOnlySpan<object?>)) != builder) return 3;
		if (builder.AppendJoin('|', new ReadOnlySpan<object?>(missing)) != builder) return 3;
		if (builder.AppendJoin('|', new ReadOnlySpan<object?>(missing, 0, 0)) != builder) return 3;
		var storage = new object?[] { "excluded-left", null, "", 'A', null, "excluded-right" };
		var view = new ReadOnlySpan<object?>(storage, 1, 4);
		if (builder.AppendJoin('\0', view.Slice(view.Length, 0)) != builder) return 3;
		if (builder.AppendJoin((string?)null, view) != builder || builder.ToString(0, builder.Length) != "seedA") return 4;
		builder.Length = 4;
		if (builder.AppendJoin("", view) != builder || builder.ToString(0, builder.Length) != "seedA") return 4;
		builder.Length = 4;
		if (builder.AppendJoin('\0', view) != builder || builder.ToString(0, builder.Length) != "seed\0\0A\0") return 5;
		for (var invalid = 0; invalid < 6; invalid++)
		{
			var start = invalid == 0 ? -1 : invalid == 1 ? int.MaxValue : invalid == 2 ? storage.Length + 1 : invalid == 3 ? 1 : 0;
			var length = invalid == 3 ? storage.Length : invalid == 4 ? -1 : invalid == 5 ? int.MaxValue : 0;
			try { builder.AppendJoin('|', new ReadOnlySpan<object?>(storage, start, length)); return 6; } catch (ArgumentOutOfRangeException) { }
			if (builder.ToString(0, builder.Length) != "seed\0\0A\0") return 7;
		}
		try { builder.AppendJoin('|', new ReadOnlySpan<object?>(missing, 1, 0)); return 8; } catch (ArgumentOutOfRangeException) { }
		try { builder.AppendJoin('|', new ReadOnlySpan<object?>(missing, 0, 1)); return 8; } catch (ArgumentOutOfRangeException) { }
		for (var span = 0; span < 2; span++)
		for (var character = 0; character < 2; character++)
		{
			var limited = new System.Text.StringBuilder(4, 8).Append("seed");
			var parts = new object?[] { 'a', "bc", "def" };
			try
			{
				if (span != 0) { if (character == 0) limited.AppendJoin("|", new ReadOnlySpan<object?>(parts)); else limited.AppendJoin('|', new ReadOnlySpan<object?>(parts)); }
				else { if (character == 0) limited.AppendJoin("|", parts); else limited.AppendJoin('|', parts); }
				return 9;
			}
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 10; }
			if (limited.ToString(0, limited.Length) != "seeda|bc") return 11;
			limited.Clear().AppendJoin('|', parts);
			if (limited.ToString(0, limited.Length) != "a|bc|def") return 12;
		}
		return 42;
	}

	public class UnsupportedObjectJoinValue
	{
		public new virtual string ToString() => throw new InvalidOperationException("A hiding method must not replace Object.ToString.");
	}
	public sealed class RuntimeDerivedObjectJoinValue : CopperSharp.Runtime.ShadowObject
	{
		public override string ToString() => throw new InvalidOperationException("Runtime implementation inheritance is outside the application bridge.");
	}

	public sealed class CustomObjectJoinValue
	{
		private readonly string _text;
		public CustomObjectJoinValue(string text) => _text = text;
		public string Text => _text;
		public override string ToString()
		{
			M68kRuntime.Collect();
			return new System.Text.StringBuilder(1).Append(_text).ToString(0, _text.Length);
		}
	}

	public class BaseObjectJoinValue
	{
		private readonly string _text;
		public BaseObjectJoinValue(string text) => _text = text;
		public virtual int Marker() => 17;
		public override string ToString() => _text;
	}
	public sealed class DerivedObjectJoinValue : BaseObjectJoinValue
	{
		public DerivedObjectJoinValue() : base("B") { }
		public override string ToString() => new System.Text.StringBuilder(1).Append(base.ToString()).Append('D').ToString(0, 2);
	}
	public sealed class InheritedObjectJoinValue : BaseObjectJoinValue
	{
		public InheritedObjectJoinValue() : base("I") { }
	}
	public class HidingObjectJoinValue : BaseObjectJoinValue
	{
		public HidingObjectJoinValue() : base("H") { }
		public new virtual string ToString() => throw new InvalidOperationException("The hiding slot must not run.");
	}
	public sealed class OverrideHidingObjectJoinValue : HidingObjectJoinValue
	{
		public override string ToString() => throw new InvalidOperationException("An override of the hiding slot must not run.");
	}
	public sealed class GenericObjectJoinValue<T>
	{
		private readonly string _text;
		public GenericObjectJoinValue(string text) => _text = text;
		public string Text => _text;
		public override string ToString() => _text;
	}
	public sealed class NullObjectJoinValue
	{
		public override string? ToString() => null;
	}
	public sealed class ReentrantObjectJoinValue
	{
		public override string ToString()
		{
			var builder = new System.Text.StringBuilder(1);
			builder.AppendJoin('|', new object?[] { new NestedOnlyObjectJoinValue(), 7 });
			return builder.ToString(0, builder.Length);
		}
	}
	public sealed class NestedOnlyObjectJoinValue
	{
		public override string ToString() => "N";
	}
	public sealed class UnallocatedObjectJoinValue
	{
		public override string ToString() => throw new InvalidOperationException("Unallocated overrides must remain unlinked.");
	}
	public sealed class ThrowingObjectJoinValue
	{
		public bool Fail = true;
		public int Calls;
		public readonly InvalidOperationException Error = new InvalidOperationException("custom");
		public override string ToString()
		{
			Calls++;
			if (Fail) throw Error;
			return "B";
		}
	}
	public sealed class AllocatingObjectJoinValue
	{
		public int Calls;
		public override string ToString()
		{
			Calls++;
			var text = M68kRuntime.AllocateString(2);
			M68kRuntime.SetStringChar(text, 0, 'B'); M68kRuntime.SetStringChar(text, 1, 'C');
			return text;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCustomObjectJoinLifetimeEntry()
	{
		var view = CopyObjectJoinSpan(CreateRetainedCustomObjectJoinSpan());
		var copy = CopyObjectJoinSpan(view);
		M68kRuntime.Collect();
		var pressure = new object?[64]; pressure[0] = "pressure";
		for (var character = 0; character < 2; character++)
		{
			var builder = new System.Text.StringBuilder(1).Append("seed");
			if (AppendRetainedObjectJoinSpan(builder, copy, character != 0) != builder) return 1;
			M68kRuntime.Collect();
			if (builder.Length != 199 || builder[69] != '|') return 2;
			const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
			for (var index = 0; index < 65; index++) if (builder[4 + index] != pattern[index % 8]) return 3;
			for (var index = 0; index < 129; index++) if (builder[70 + index] != pattern[index % 8]) return 4;
		}
		return pressure[0] == (object)"pressure" && view[0] == copy[0] ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<object?> CreateRetainedCustomObjectJoinSpan()
	{
		var values = new object?[] { "excluded-left", new CustomObjectJoinValue(CreatePatternStringBuilder(1, 65, 0).ToString(0, 65)),
			new GenericObjectJoinValue<string>(CreatePatternStringBuilder(1, 129, 0).ToString(0, 129)), "excluded-right" };
		return new ReadOnlySpan<object?>(values, 1, 2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCustomObjectJoinExceptionEntry()
	{
		for (var span = 0; span < 2; span++)
		for (var character = 0; character < 2; character++)
		{
			var throwing = new ThrowingObjectJoinValue();
			var values = new object?[] { "A", throwing, "C" };
			var builder = new System.Text.StringBuilder(64).Append("seed");
			try
			{
				if (span != 0) { if (character == 0) builder.AppendJoin("|", new ReadOnlySpan<object?>(values)); else builder.AppendJoin('|', new ReadOnlySpan<object?>(values)); }
				else { if (character == 0) builder.AppendJoin("|", values); else builder.AppendJoin('|', values); }
				return 1;
			}
			catch (InvalidOperationException error) { if (error != throwing.Error) return 2; }
			if (builder.ToString(0, builder.Length) != "seedA|" || throwing.Calls != 1) return 3;
			throwing.Fail = false; builder.Length = 4;
			builder.AppendJoin('|', values);
			if (builder.ToString(0, builder.Length) != "seedA|B|C" || throwing.Calls != 2) return 4;
			builder.Clear().AppendJoin('|', values);
			if (builder.ToString(0, builder.Length) != "A|B|C" || throwing.Calls != 3) return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCustomObjectJoinAllocationFailureEntry()
	{
		for (var span = 0; span < 2; span++)
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(64).Append("seed");
			var noAllocation = new object?[] { new BaseObjectJoinValue("BC") };
			SetStringBuilderAllocationFailure(1);
			if (span != 0) { if (character == 0) available.AppendJoin("|", new ReadOnlySpan<object?>(noAllocation)); else available.AppendJoin('|', new ReadOnlySpan<object?>(noAllocation)); }
			else { if (character == 0) available.AppendJoin("|", noAllocation); else available.AppendJoin('|', noAllocation); }
			SetStringBuilderAllocationFailure(0);
			if (available.ToString(0, available.Length) != "seedBC") return 1;
			for (var scenario = 0; scenario < 2; scenario++)
			for (var failAt = 1; failAt <= (scenario == 0 ? 3 : 1); failAt++)
			{
				var capacity = scenario == 0 ? 4 : 64;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var value = new AllocatingObjectJoinValue();
				var parts = scenario == 0 ? new object?[] { value } : new object?[] { "A", value };
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (span != 0) { if (character == 0) builder.AppendJoin("|", new ReadOnlySpan<object?>(parts)); else builder.AppendJoin('|', new ReadOnlySpan<object?>(parts)); }
					else { if (character == 0) builder.AppendJoin("|", parts); else builder.AppendJoin('|', parts); }
					SetStringBuilderAllocationFailure(0); return 2;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (value.Calls != 1 || builder.Capacity != capacity || builder.ToString(0, builder.Length) != (scenario == 0 ? "seed" : "seedA|")) return 3;
				builder.Length = 4; builder.AppendJoin('|', parts);
				if (value.Calls != 2 || builder.ToString(0, builder.Length) != (scenario == 0 ? "seedBC" : "seedA|BC")) return 4;
				builder.Clear().AppendJoin('|', parts);
				if (value.Calls != 3 || builder.ToString(0, builder.Length) != (scenario == 0 ? "BC" : "A|BC")) return 5;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCustomObjectJoinEntry()
	{
		for (var layout = 0; layout < 3; layout++)
		for (var span = 0; span < 2; span++)
		for (var delimiter = 0; delimiter < 4; delimiter++)
		{
			var dynamicText = CreatePatternStringBuilder(1, 65, 0).ToString(0, 65);
			var values = new object?[] { new CustomObjectJoinValue(dynamicText), null, 42, new DerivedObjectJoinValue(),
				new InheritedObjectJoinValue(), new HidingObjectJoinValue(), new OverrideHidingObjectJoinValue(),
				new GenericObjectJoinValue<int>("G1"), new GenericObjectJoinValue<string>("G2"), new NullObjectJoinValue(), new ReentrantObjectJoinValue() };
			var expected = new string?[] { dynamicText, null, "42", "BD", "I", "H", "H", "G1", "G2", null, "N|7" };
			var separator = delimiter == 0 ? null : delimiter == 1 ? "" : delimiter == 2 ? "|" : "\u03A9\0";
			var builder = new System.Text.StringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512).Append("seed");
			var storage = new object?[values.Length + 2]; storage[0] = "excluded-left"; storage[storage.Length - 1] = "excluded-right";
			for (var index = 0; index < values.Length; index++) storage[index + 1] = values[index];
			var view = new ReadOnlySpan<object?>(storage, 1, values.Length); storage = null;
			M68kRuntime.Collect();
			var result = span != 0 ? delimiter == 2 ? builder.AppendJoin('|', view) : builder.AppendJoin(separator, view)
				: delimiter == 2 ? builder.AppendJoin('|', values) : builder.AppendJoin(separator, values);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinMatches(builder, "seed", expected, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (span != 0) { if (delimiter == 2) builder.AppendJoin('|', view); else builder.AppendJoin(separator, view); }
			else { if (delimiter == 2) builder.AppendJoin('|', values); else builder.AppendJoin(separator, values); }
			if (!AppendJoinMatches(builder, "", expected, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != builder.Length + 3) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[index + 4] != builder[index]) return 3;
			for (var index = 0; index < values.Length; index++) if (view[index] != values[index]) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectJoinRejectsUnknownValueEntry()
	{
		for (var span = 0; span < 2; span++)
		for (var character = 0; character < 2; character++)
		{
			var builder = new System.Text.StringBuilder(64).Append("seed");
			var values = new object?[] { 42, new RuntimeDerivedObjectJoinValue(), "unreached" };
			try
			{
				if (span != 0) { if (character == 0) builder.AppendJoin("|", new ReadOnlySpan<object?>(values)); else builder.AppendJoin('|', new ReadOnlySpan<object?>(values)); }
				else { if (character == 0) builder.AppendJoin("|", values); else builder.AppendJoin('|', values); }
				return 1;
			}
			catch (NotSupportedException) { }
			if (builder.ToString(0, builder.Length) != "seed42|") return 2;
			builder.Length = 4; values[1] = true;
			builder.AppendJoin('|', values);
			if (builder.ToString(0, builder.Length) != "seed42|True|unreached") return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectJoinAllocationFailureEntry()
	{
		for (var span = 0; span < 2; span++)
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(64).Append("seed");
			var noConversion = new object?[] { "A", true, null, false };
			SetStringBuilderAllocationFailure(1);
			if (span != 0) { if (character == 0) available.AppendJoin("|", new ReadOnlySpan<object?>(noConversion)); else available.AppendJoin('|', new ReadOnlySpan<object?>(noConversion)); }
			else { if (character == 0) available.AppendJoin("|", noConversion); else available.AppendJoin('|', noConversion); }
			SetStringBuilderAllocationFailure(0);
			if (available.ToString(0, available.Length) != "seedA|True||False") return 1;
			for (var scenario = 0; scenario < 3; scenario++)
			for (var failAt = 1; failAt <= (scenario == 0 ? 3 : 1); failAt++)
			{
				var capacity = scenario == 0 ? 4 : 64;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var parts = scenario == 2 ? new object?[] { "A", 'B' } : new object?[] { 12 };
				var retained = scenario == 2 ? "seedA|" : "seed";
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (span != 0) { if (character == 0) builder.AppendJoin("|", new ReadOnlySpan<object?>(parts)); else builder.AppendJoin('|', new ReadOnlySpan<object?>(parts)); }
					else { if (character == 0) builder.AppendJoin("|", parts); else builder.AppendJoin('|', parts); }
					SetStringBuilderAllocationFailure(0); return 2;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (builder.Capacity != capacity || builder.ToString(0, builder.Length) != retained) return 3;
				builder.Length = 4; builder.AppendJoin('|', parts);
				if (builder.ToString(0, builder.Length) != (scenario == 2 ? "seedA|B" : "seed12")) return 4;
				builder.Clear().AppendJoin('|', parts);
				if (builder.ToString(0, builder.Length) != (scenario == 2 ? "A|B" : "12")) return 5;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnumerableListJoinEntry()
		=> CoreLibStringBuilderEnumerableJoinMatrix(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CoreLibStringBuilderEnumerableJoinMatrix(bool list)
	{
		for (var layout = 0; layout < 3; layout++)
		for (var valueCase = 0; valueCase < 6; valueCase++)
		for (var separatorCase = 0; separatorCase < 6; separatorCase++)
		{
			var values = CreateAppendJoinValues(valueCase);
			var input = list ? CreateStringJoinList(values) : null;
			IEnumerable<string?> source = input != null ? input : values;
			var separator = separatorCase == 0 ? null : separatorCase == 1 ? "" : separatorCase == 2 ? "|"
				: separatorCase == 3 ? new System.Text.StringBuilder(5).Append("\u03A9\0\uD83D\uDE00\uFFFF").ToString(0, 5)
				: separatorCase == 4 ? "\0" : "\u03A9";
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			M68kRuntime.Collect();
			var result = separatorCase < 4 ? builder.AppendJoin<string?>(separator, source) : builder.AppendJoin<string?>(separator![0], source);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinMatches(builder, before, values, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (separatorCase < 4) builder.AppendJoin<string?>(separator, source); else builder.AppendJoin<string?>(separator![0], source);
			M68kRuntime.Collect();
			if (!AppendJoinMatches(builder, "", values, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != before.Length + builder.Length - 1 || builder[builder.Length - 1] != 'X') return 3;
			for (var index = 0; index < before.Length; index++) if (snapshot[index] != before[index]) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[before.Length + index] != builder[index]) return 3;
			if (input != null)
			{
				if (input.Count != values.Length) return 4;
				for (var index = 0; index < values.Length; index++) if (input[index] != values[index]) return 4;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<string?> CreateStringJoinEnumerable(bool list, string?[] values)
	{
		if (!list) return values;
		return CreateStringJoinList(values);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static List<string?> CreateStringJoinList(string?[] values)
	{
		var result = new List<string?>(values.Length);
		for (var index = 0; index < values.Length; index++) result.Add(values[index]);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnumerableJoinContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		try { builder.AppendJoin<string?>("|", null!); return 1; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 2; }
		try { builder.AppendJoin<string?>('|', null!); return 3; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 4; }
		for (var list = 0; list < 2; list++)
		{
			builder.Length = 4;
			var source = CreateStringJoinEnumerable(list != 0, new[] { null, "", "A", null });
			if (builder.AppendJoin<string?>((string?)null, source) != builder || builder.ToString(0, builder.Length) != "seedA") return 5;
			builder.Length = 4;
			if (builder.AppendJoin<string?>("", source) != builder || builder.ToString(0, builder.Length) != "seedA") return 6;
			builder.Length = 4;
			if (builder.AppendJoin<string?>('\0', source) != builder || builder.ToString(0, builder.Length) != "seed\0\0A\0") return 7;
			for (var character = 0; character < 2; character++)
			{
				var limited = new System.Text.StringBuilder(4, 8).Append("seed");
				var parts = CreateStringJoinEnumerable(list != 0, new[] { "a", "bc", "def" });
				try { if (character == 0) limited.AppendJoin<string?>("|", parts); else limited.AppendJoin<string?>('|', parts); return 8; }
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 9; }
				if (limited.Length != 8 || limited.Capacity != 8 || limited.ToString(0, 8) != "seeda|bc") return 10;
				limited.Clear().AppendJoin<string?>('|', parts);
				if (limited.ToString(0, limited.Length) != "a|bc|def") return 11;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnumerableJoinLifetimeEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var builder = new System.Text.StringBuilder(1).Append("seed");
			var result = character == 0 ? builder.AppendJoin<string?>("|", CreateRetainedStringJoinEnumerable(list != 0))
				: builder.AppendJoin<string?>('|', CreateRetainedStringJoinEnumerable(list != 0));
			M68kRuntime.Collect();
			if (result != builder || builder.Length != 201) return 1;
			const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
			for (var index = 0; index < 65; index++) if (builder[4 + index] != pattern[index % 8]) return 2;
			if (builder[69] != '|' || builder[70] != '|' || builder[71] != '|') return 3;
			for (var index = 0; index < 129; index++) if (builder[72 + index] != pattern[index % 8]) return 4;
			builder.Clear().Append('X');
			if (builder.ToString(0, builder.Length) != "X") return 5;
		}
		return 42;
	}

	public sealed class ThrowingStringJoinEnumerable : IEnumerable<string?>
	{
		public IEnumerator<string?> GetEnumerator() => throw new InvalidOperationException("Producer failure.");
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnumerableJoinPropagatesProducerFailureEntry()
	{
		IEnumerable<string?> source = new ThrowingStringJoinEnumerable();
		var builder = new System.Text.StringBuilder(32).Append("seed");
		try { builder.AppendJoin<string?>("|", source); return 1; } catch (InvalidOperationException) { }
		if (builder.ToString(0, builder.Length) != "seed") return 2;
		try { builder.AppendJoin<string?>('|', source); return 3; } catch (InvalidOperationException) { }
		if (builder.ToString(0, builder.Length) != "seed") return 4;
		builder.AppendJoin<string?>('|', CreateStringJoinEnumerable(false, new[] { "A", "B" }));
		return builder.ToString(0, builder.Length) == "seedA|B" ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<string?> CreateRetainedStringJoinEnumerable(bool list) => CreateStringJoinEnumerable(list, CreateAppendJoinValues(5));

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnumerableJoinAllocationFailureEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(512).Append("seed");
			var values = CreateAppendJoinValues(5);
			var source = CreateStringJoinEnumerable(list != 0, values);
			// Each join owns one bridge enumerator, even when no chunk grows.
			SetStringBuilderAllocationFailure(2);
			if (character == 0) available.AppendJoin<string?>("|", source); else available.AppendJoin<string?>('|', source);
			SetStringBuilderAllocationFailure(0);
			if (!AppendJoinMatches(available, "seed", values, "|")) return 1;
			for (var scenario = 0; scenario < (character == 0 ? 4 : 3); scenario++)
			for (var failAt = 1; failAt <= 3; failAt++)
			{
				var capacity = scenario == 0 ? 4 : scenario == 2 ? 6 : 8;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var parts = new[] { "AB", "CD" };
				var input = CreateStringJoinEnumerable(list != 0, parts);
				var delimiter = scenario == 3 ? "\u03A9\0\uD83D\uDE00\uFFFF" : "|";
				var retained = failAt == 1 || scenario == 0 ? "seed" : scenario == 1 ? "seedAB|C" : scenario == 2 ? "seedAB" : "seedAB\u03A9\0";
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (character == 0) builder.AppendJoin<string?>(delimiter, input); else builder.AppendJoin<string?>('|', input);
					SetStringBuilderAllocationFailure(0); return 2;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (builder.Capacity != capacity || builder.Length != retained.Length || builder.ToString(0, builder.Length) != retained) return 3;
				if (parts[0] != "AB" || parts[1] != "CD") return 4;
				builder.Length = 4;
				if (character == 0) builder.AppendJoin<string?>(delimiter, input); else builder.AppendJoin<string?>('|', input);
				if (!AppendJoinMatches(builder, "seed", parts, delimiter)) return 5;
				builder.Clear().AppendJoin<string?>('|', input);
				if (builder.ToString(0, builder.Length) != "AB|CD") return 6;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringJoinEnumeratorLifetimeAndMutationEntry()
	{
		for (var list = 0; list < 2; list++)
		{
			IEnumerable<string?>? source = CreateRetainedStringJoinEnumerable(list != 0);
			var enumerator = CopperSharp.Runtime.ShadowStringJoinEnumeration.GetEnumerator(source);
			source = null;
			M68kRuntime.Collect();
			var pressure = new string?[64]; pressure[0] = "pressure";
			const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
			var item = 0;
			while (enumerator.MoveNext())
			{
				M68kRuntime.Collect();
				var value = enumerator.Current;
				if (item == 1 ? value != null : item == 2 ? value != "" : value == null || value.Length != (item == 0 ? 65 : 129)) return 1;
				if (item == 0 || item == 3)
					for (var index = 0; index < value!.Length; index++) if (value[index] != pattern[index % 8]) return 2;
				item++;
			}
			if (item != 4 || enumerator.MoveNext() || enumerator.Current != null || pressure[0] != "pressure") return 3;
			enumerator.Dispose();
			M68kRuntime.Collect();
			if (enumerator.MoveNext() || enumerator.Current != null) return 4;
		}
		var input = new List<string?>(); input.Add("A"); input.Add("B");
		var active = CopperSharp.Runtime.ShadowStringJoinEnumeration.GetEnumerator(input);
		if (!active.MoveNext() || active.Current != "A") return 5;
		input.Add("C");
		try { active.MoveNext(); return 6; } catch (InvalidOperationException) { }
		active.Dispose();
		if (active.Current != null) return 7;
		if (active.MoveNext()) return 8;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string?[] CreateAppendJoinValues(int scenario)
	{
		if (scenario == 0) return new string?[0];
		if (scenario == 1) return new string?[] { null };
		if (scenario == 2) return new[] { "" };
		if (scenario == 3) return new[] { "\u03A9\0\uD83D\uDE00\uFFFF" };
		if (scenario == 4) return new[] { "A", null, "", "\u03A9\0\uD83D\uDE00\uFFFF", "Z" };
		return new[] { CreatePatternStringBuilder(1, 65, 0).ToString(0, 65), null, "", CreatePatternStringBuilder(1, 129, 0).ToString(0, 129) };
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool AppendJoinMatches(System.Text.StringBuilder builder, string prefix, string?[] values, string? separator)
	{
		var position = 0;
		for (var index = 0; index < prefix.Length; index++) if (position >= builder.Length || builder[position++] != prefix[index]) return false;
		for (var item = 0; item < values.Length; item++)
		{
			if (item != 0 && separator != null)
				for (var index = 0; index < separator.Length; index++) if (position >= builder.Length || builder[position++] != separator[index]) return false;
			var value = values[item];
			if (value != null)
				for (var index = 0; index < value.Length; index++) if (position >= builder.Length || builder[position++] != value[index]) return false;
		}
		return position == builder.Length;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<object?> CreateObjectJoinEnumerable(bool list, object?[] values)
	{
		if (!list) return values;
		var result = new List<object?>(values.Length);
		for (var index = 0; index < values.Length; index++) result.Add(values[index]);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectEnumerableJoinEntry()
		=> CoreLibStringBuilderObjectEnumerableJoinMatrix(false);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectListJoinEntry()
		=> CoreLibStringBuilderObjectEnumerableJoinMatrix(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CoreLibStringBuilderObjectEnumerableJoinMatrix(bool list)
	{
		for (var layout = 0; layout < 3; layout++)
		for (var scenario = 0; scenario < 7; scenario++)
		for (var delimiter = 0; delimiter < 6; delimiter++)
		{
			var values = scenario < 6 ? CreateObjectJoinValues(scenario) : new object?[] {
				new DerivedObjectJoinValue(), new InheritedObjectJoinValue(), new HidingObjectJoinValue(),
				new OverrideHidingObjectJoinValue(), new GenericObjectJoinValue<int>("G1"),
				new GenericObjectJoinValue<string>("G2"), new NullObjectJoinValue(), new ReentrantObjectJoinValue() };
			var expected = scenario < 6 ? CreateObjectJoinExpected(scenario) : new string?[] { "BD", "I", "H", "H", "G1", "G2", null, "N|7" };
			var source = CreateObjectJoinEnumerable(list, values);
			var separator = delimiter == 0 ? null : delimiter == 1 ? "" : delimiter == 2 ? "|"
				: delimiter == 3 ? CreatePatternStringBuilder(1, 5, 3).ToString(0, 5) : delimiter == 4 ? "\0" : "\u03A9";
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			M68kRuntime.Collect();
			var result = delimiter < 4 ? builder.AppendJoin<object?>(separator, source) : builder.AppendJoin<object?>(separator![0], source);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinMatches(builder, before, expected, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (delimiter < 4) builder.AppendJoin<object?>(separator, source); else builder.AppendJoin<object?>(separator![0], source);
			if (!AppendJoinMatches(builder, "", expected, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != before.Length + builder.Length - 1) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[before.Length + index] != builder[index]) return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectEnumerableJoinContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		try { builder.AppendJoin<object?>("|", null!); return 1; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 2; }
		try { builder.AppendJoin<object?>('|', null!); return 3; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 4; }
		for (var list = 0; list < 2; list++)
		{
			builder.Length = 4;
			var source = CreateObjectJoinEnumerable(list != 0, new object?[] { null, "", "A", null });
			if (builder.AppendJoin<object?>((string?)null, source) != builder || builder.ToString(0, builder.Length) != "seedA") return 5;
			builder.Length = 4;
			if (builder.AppendJoin<object?>("", source) != builder || builder.ToString(0, builder.Length) != "seedA") return 6;
			builder.Length = 4;
			if (builder.AppendJoin<object?>('\0', source) != builder || builder.ToString(0, builder.Length) != "seed\0\0A\0") return 7;
			for (var character = 0; character < 2; character++)
			{
				var limited = new System.Text.StringBuilder(4, 8).Append("seed");
				var parts = CreateObjectJoinEnumerable(list != 0, new object?[] { "a", "bc", "def" });
				try { if (character == 0) limited.AppendJoin<object?>("|", parts); else limited.AppendJoin<object?>('|', parts); return 8; }
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 9; }
				if (limited.Length != 8 || limited.Capacity != 8 || limited.ToString(0, 8) != "seeda|bc") return 10;
				limited.Clear().AppendJoin<object?>('|', parts);
				if (limited.ToString(0, limited.Length) != "a|bc|def") return 11;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<object?> CreateRetainedObjectJoinEnumerable(bool list)
	{
		var values = new object?[] { new CustomObjectJoinValue(CreatePatternStringBuilder(1, 65, 0).ToString(0, 65)),
			null, 42, new GenericObjectJoinValue<string>(CreatePatternStringBuilder(1, 129, 0).ToString(0, 129)) };
		return CreateObjectJoinEnumerable(list, values);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectEnumerableJoinLifetimeEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var builder = new System.Text.StringBuilder(1).Append("seed");
			var result = character == 0 ? builder.AppendJoin<object?>("|", CreateRetainedObjectJoinEnumerable(list != 0))
				: builder.AppendJoin<object?>('|', CreateRetainedObjectJoinEnumerable(list != 0));
			M68kRuntime.Collect();
			if (result != builder || builder.Length != 203 || builder[69] != '|' || builder[70] != '|' || builder[71] != '4' || builder[72] != '2' || builder[73] != '|') return 1;
			const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
			for (var index = 0; index < 65; index++) if (builder[4 + index] != pattern[index % 8]) return 2;
			for (var index = 0; index < 129; index++) if (builder[74 + index] != pattern[index % 8]) return 3;
		}
		for (var list = 0; list < 2; list++)
		{
			IEnumerable<object?>? source = CreateRetainedObjectJoinEnumerable(list != 0);
			var enumerator = CopperSharp.Runtime.ShadowObjectJoinEnumeration.GetEnumerator(source);
			source = null;
			M68kRuntime.Collect();
			var pressure = new object?[64]; pressure[0] = "pressure";
			if (!enumerator.MoveNext() || enumerator.Current is not CustomObjectJoinValue) return 4;
			M68kRuntime.Collect();
			var first = ((CustomObjectJoinValue)enumerator.Current!).Text;
			if (first.Length != 65 || first[0] != 'a' || first[64] != 'a') return 5;
			if (!enumerator.MoveNext() || enumerator.Current != null) return 6;
			if (!enumerator.MoveNext() || enumerator.Current is not int value || value != 42) return 7;
			if (!enumerator.MoveNext() || enumerator.Current is not GenericObjectJoinValue<string>) return 8;
			M68kRuntime.Collect();
			var last = ((GenericObjectJoinValue<string>)enumerator.Current!).Text;
			if (last.Length != 129 || last[128] != 'a' || enumerator.MoveNext() || enumerator.Current != null) return 9;
			enumerator.Dispose();
			M68kRuntime.Collect();
			if (enumerator.Current != null || enumerator.MoveNext() || pressure[0] != (object)"pressure") return 10;
		}
		return 42;
	}

	public sealed class MutatingObjectJoinValue
	{
		private readonly List<object?> _list;
		public bool Mutate = true;
		public int Calls;
		public MutatingObjectJoinValue(List<object?> list) => _list = list;
		public override string ToString()
		{
			Calls++;
			if (Mutate) _list.Add("C");
			return "B";
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectEnumerableJoinExceptionEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var value = new ThrowingObjectJoinValue();
			var source = CreateObjectJoinEnumerable(list != 0, new object?[] { "A", value, "C" });
			var builder = new System.Text.StringBuilder(64).Append("seed");
			try { if (character == 0) builder.AppendJoin<object?>("|", source); else builder.AppendJoin<object?>('|', source); return 1; }
			catch (InvalidOperationException error) { if (error != value.Error) return 2; }
			if (builder.ToString(0, builder.Length) != "seedA|" || value.Calls != 1) return 3;
			value.Fail = false; builder.Length = 4;
			builder.AppendJoin<object?>('|', source);
			if (builder.ToString(0, builder.Length) != "seedA|B|C" || value.Calls != 2) return 4;
			builder.Clear().AppendJoin<object?>('|', source);
			if (builder.ToString(0, builder.Length) != "A|B|C" || value.Calls != 3) return 5;
		}
		for (var character = 0; character < 2; character++)
		{
			var list = new List<object?>(3); list.Add("A");
			var value = new MutatingObjectJoinValue(list); list.Add(value);
			var builder = new System.Text.StringBuilder(64).Append("seed");
			try { if (character == 0) builder.AppendJoin<object?>("|", list); else builder.AppendJoin<object?>('|', list); return 6; }
			catch (InvalidOperationException) { }
			if (builder.ToString(0, builder.Length) != "seedA|B" || value.Calls != 1 || list.Count != 3) return 7;
			value.Mutate = false; builder.Length = 4;
			builder.AppendJoin<object?>('|', list);
			if (builder.ToString(0, builder.Length) != "seedA|B|C" || value.Calls != 2) return 8;
			builder.Clear().AppendJoin<object?>('|', list);
			if (builder.ToString(0, builder.Length) != "A|B|C" || value.Calls != 3) return 9;
		}
		return 42;
	}

	public sealed class ThrowingObjectJoinEnumerable : IEnumerable<object?>
	{
		public IEnumerator<object?> GetEnumerator() => throw new InvalidOperationException("Object producer failure.");
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectEnumerableJoinRejectsUnknownEntry()
	{
		var builder = new System.Text.StringBuilder(64).Append("seed");
		IEnumerable<object?> source = new ThrowingObjectJoinEnumerable();
		for (var character = 0; character < 2; character++)
		{
			try { if (character == 0) builder.AppendJoin<object?>("|", source); else builder.AppendJoin<object?>('|', source); return 1; }
			catch (InvalidOperationException) { }
			if (builder.ToString(0, builder.Length) != "seed") return 2;
		}
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			builder.Length = 4;
			source = CreateObjectJoinEnumerable(list != 0, new object?[] { 42, new RuntimeDerivedObjectJoinValue(), "unreached" });
			try { if (character == 0) builder.AppendJoin<object?>("|", source); else builder.AppendJoin<object?>('|', source); return 3; }
			catch (NotSupportedException) { }
			if (builder.ToString(0, builder.Length) != "seed42|") return 4;
			builder.Length = 4;
			builder.AppendJoin<object?>('|', CreateObjectJoinEnumerable(list != 0, new object?[] { 42, true }));
			if (builder.ToString(0, builder.Length) != "seed42|True") return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderObjectEnumerableJoinAllocationFailureEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(64).Append("seed");
			var noConversion = CreateObjectJoinEnumerable(list != 0, new object?[] { new BaseObjectJoinValue("BC") });
			SetStringBuilderAllocationFailure(2);
			if (character == 0) available.AppendJoin<object?>("|", noConversion); else available.AppendJoin<object?>('|', noConversion);
			SetStringBuilderAllocationFailure(0);
			if (available.ToString(0, available.Length) != "seedBC") return 1;
			for (var scenario = 0; scenario < 4; scenario++)
			for (var failAt = 1; failAt <= (scenario == 0 || scenario == 2 ? 4 : 2); failAt++)
			{
				var capacity = scenario == 0 || scenario == 2 ? 4 : 64;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var value = new AllocatingObjectJoinValue();
				var values = scenario == 0 ? new object?[] { value } : scenario == 1 ? new object?[] { "A", value }
					: scenario == 2 ? new object?[] { 12 } : new object?[] { "A", 'B' };
				var source = CreateObjectJoinEnumerable(list != 0, values);
				SetStringBuilderAllocationFailure(failAt);
				try { if (character == 0) builder.AppendJoin<object?>("|", source); else builder.AppendJoin<object?>('|', source); SetStringBuilderAllocationFailure(0); return 2; }
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				var expected = scenario == 0 ? "BC" : scenario == 1 ? "A|BC" : scenario == 2 ? "12" : "A|B";
				var retained = failAt == 1 || capacity == 4 ? "seed" : "seedA|";
				if (builder.Capacity != capacity || builder.ToString(0, builder.Length) != retained) return 3;
				if (value.Calls != (scenario < 2 && failAt > 1 ? 1 : 0)) return 4;
				builder.Length = 4; builder.AppendJoin<object?>('|', source);
				if (builder.ToString(0, builder.Length) != "seed" + expected) return 5;
				builder.Clear().AppendJoin<object?>('|', source);
				if (builder.ToString(0, builder.Length) != expected || value.Calls != (scenario < 2 ? (failAt > 1 ? 3 : 2) : 0)) return 6;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<int> CreateInt32JoinEnumerable(bool list, int[] values)
	{
		if (!list) return values;
		var result = new List<int>(values.Length);
		for (var index = 0; index < values.Length; index++) result.Add(values[index]);
		return result;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32EnumerableJoinContractEntry()
	{
		var builder = new System.Text.StringBuilder(64).Append("seed");
		try { builder.AppendJoin<int>("|", null!); return 1; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 2; }
		try { builder.AppendJoin<int>('|', null!); return 3; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 4; }
		for (var list = 0; list < 2; list++)
		{
			var values = new int[3]; values[0] = int.MinValue; values[2] = int.MaxValue;
			var source = CreateInt32JoinEnumerable(list != 0, values);
			builder.Length = 4;
			if (builder.AppendJoin<int>("|", source) != builder || builder.ToString(0, builder.Length) != "seed-2147483648|0|2147483647") return 5;
			builder.Length = 4;
			if (builder.AppendJoin<int>((string?)null, source) != builder || builder.ToString(0, builder.Length) != "seed-214748364802147483647") return 6;
			builder.Length = 4;
			if (builder.AppendJoin<int>('\0', source) != builder || builder.ToString(0, builder.Length) != "seed-2147483648\0" + "0\0" + "2147483647") return 7;
			for (var character = 0; character < 2; character++)
			{
				var limited = new System.Text.StringBuilder(4, 8).Append("seed");
				var numbers = new int[3]; numbers[0] = 1; numbers[1] = 23; numbers[2] = 456;
				var parts = CreateInt32JoinEnumerable(list != 0, numbers);
				try { if (character == 0) limited.AppendJoin<int>("|", parts); else limited.AppendJoin<int>('|', parts); return 8; }
				catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 9; }
				if (limited.Length != 8 || limited.Capacity != 8 || limited.ToString(0, 8) != "seed1|23") return 10;
				limited.Clear().AppendJoin<int>('|', parts);
				if (limited.ToString(0, limited.Length) != "1|23|456") return 11;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int[] CreateInt32JoinValues(int scenario)
	{
		if (scenario == 0) return new int[0];
		if (scenario == 1) { var single = new int[1]; single[0] = int.MinValue; return single; }
		var values = new int[8];
		values[0] = int.MinValue; values[1] = -1000000000; values[2] = -12; values[3] = -1;
		values[4] = 0; values[5] = 1; values[6] = 1000000000; values[7] = int.MaxValue;
		return values;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32EnumerableJoinEntry() => CoreLibStringBuilderInt32EnumerableJoinMatrix(false);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32ListJoinEntry() => CoreLibStringBuilderInt32EnumerableJoinMatrix(true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CoreLibStringBuilderInt32EnumerableJoinMatrix(bool list)
	{
		for (var layout = 0; layout < 3; layout++)
		for (var scenario = 0; scenario < 3; scenario++)
		for (var delimiter = 0; delimiter < 6; delimiter++)
		{
			var source = CreateInt32JoinEnumerable(list, CreateInt32JoinValues(scenario));
			var expected = scenario == 0 ? new string[0] : scenario == 1 ? new[] { "-2147483648" }
				: new[] { "-2147483648", "-1000000000", "-12", "-1", "0", "1", "1000000000", "2147483647" };
			var separator = delimiter == 0 ? null : delimiter == 1 ? "" : delimiter == 2 ? "|"
				: delimiter == 3 ? CreatePatternStringBuilder(1, 5, 3).ToString(0, 5) : delimiter == 4 ? "\0" : "\u03A9";
			var builder = CreatePatternStringBuilder(layout == 0 ? 1 : layout == 1 ? 4 : 512, 17, 3);
			var before = builder.ToString(0, builder.Length);
			M68kRuntime.Collect();
			var result = delimiter < 4 ? builder.AppendJoin<int>(separator, source) : builder.AppendJoin<int>(separator![0], source);
			M68kRuntime.Collect();
			if (result != builder || !AppendJoinMatches(builder, before, expected, separator)) return 1;
			var snapshot = builder.ToString(0, builder.Length);
			builder.Clear();
			if (delimiter < 4) builder.AppendJoin<int>(separator, source); else builder.AppendJoin<int>(separator![0], source);
			if (!AppendJoinMatches(builder, "", expected, separator)) return 2;
			builder.Append('X');
			if (snapshot.Length != before.Length + builder.Length - 1) return 3;
			for (var index = 0; index < builder.Length - 1; index++) if (snapshot[before.Length + index] != builder[index]) return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static IEnumerable<int> CreateRetainedInt32JoinEnumerable(bool list) => CreateInt32JoinEnumerable(list, CreateInt32JoinValues(2));

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32EnumerableJoinLifetimeEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var builder = new System.Text.StringBuilder(1).Append("seed");
			var result = character == 0 ? builder.AppendJoin<int>("|", CreateRetainedInt32JoinEnumerable(list != 0))
				: builder.AppendJoin<int>('|', CreateRetainedInt32JoinEnumerable(list != 0));
			M68kRuntime.Collect();
			if (result != builder || builder.ToString(0, builder.Length) != "seed-2147483648|-1000000000|-12|-1|0|1|1000000000|2147483647") return 1;
		}
		for (var list = 0; list < 2; list++)
		{
			IEnumerable<int>? source = CreateRetainedInt32JoinEnumerable(list != 0);
			var enumerator = CopperSharp.Runtime.ShadowInt32JoinEnumeration.GetEnumerator(source);
			source = null;
			M68kRuntime.Collect();
			var pressure = new int[64]; pressure[0] = 123;
			for (var index = 0; index < 8; index++)
			{
				if (!enumerator.MoveNext()) return 2;
				M68kRuntime.Collect();
				var expected = index == 0 ? int.MinValue : index == 1 ? -1000000000 : index == 2 ? -12 : index == 3 ? -1
					: index == 4 ? 0 : index == 5 ? 1 : index == 6 ? 1000000000 : int.MaxValue;
				if (enumerator.Current != expected) return 3;
			}
			if (enumerator.MoveNext() || enumerator.Current != 0 || enumerator.MoveNext()) return 4;
			enumerator.Dispose();
			M68kRuntime.Collect();
			if (enumerator.Current != 0 || enumerator.MoveNext() || pressure[0] != 123) return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32EnumerableJoinMutationEntry()
	{
		for (var mutation = 0; mutation < 5; mutation++)
		{
			var list = new List<int>(4); list.Add(12); list.Add(34);
			var enumerator = CopperSharp.Runtime.ShadowInt32JoinEnumeration.GetEnumerator(list);
			if (!enumerator.MoveNext() || enumerator.Current != 12) return 1;
			if (mutation == 2 && (!enumerator.MoveNext() || enumerator.Current != 34 || enumerator.MoveNext() || enumerator.Current != 0)) return 6;
			if (mutation == 1) list[0] = 78; else if (mutation == 3) list.RemoveAt(0); else if (mutation == 4) list.Clear(); else list.Add(56);
			try { enumerator.MoveNext(); return 2; } catch (InvalidOperationException) { }
			enumerator.Dispose();
			if (enumerator.Current != 0 || enumerator.MoveNext()) return 3;
			var builder = new System.Text.StringBuilder(1).AppendJoin<int>('|', list);
			var expected = mutation == 1 ? "78|34" : mutation == 3 ? "34" : mutation == 4 ? "" : "12|34|56";
			if (builder.ToString(0, builder.Length) != expected) return 4;
			builder.Clear().Append("seed").AppendJoin<int>("|", list);
			if (builder.ToString(0, builder.Length) != "seed" + expected) return 5;
		}
		return 42;
	}

	public sealed class ThrowingInt32JoinEnumerable : IEnumerable<int>
	{
		public IEnumerator<int> GetEnumerator() => throw new InvalidOperationException("Int32 producer failure.");
		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32EnumerableJoinRejectsUnknownEntry()
	{
		var builder = new System.Text.StringBuilder(64).Append("seed");
		IEnumerable<int> source = new ThrowingInt32JoinEnumerable();
		for (var character = 0; character < 2; character++)
		{
			try { if (character == 0) builder.AppendJoin<int>("|", source); else builder.AppendJoin<int>('|', source); return 1; }
			catch (InvalidOperationException) { }
			if (builder.ToString(0, builder.Length) != "seed") return 2;
		}
		builder.AppendJoin<int>('|', CreateInt32JoinValues(1));
		if (builder.ToString(0, builder.Length) != "seed-2147483648") return 3;
		builder.Clear().AppendJoin<int>("|", CreateInt32JoinEnumerable(true, CreateInt32JoinValues(1)));
		return builder.ToString(0, builder.Length) == "-2147483648" ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInt32EnumerableJoinAllocationFailureEntry()
	{
		for (var list = 0; list < 2; list++)
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(64).Append("seed");
			var empty = CreateInt32JoinEnumerable(list != 0, new int[0]);
			SetStringBuilderAllocationFailure(2);
			if (character == 0) available.AppendJoin<int>("|", empty); else available.AppendJoin<int>('|', empty);
			SetStringBuilderAllocationFailure(0);
			if (available.ToString(0, available.Length) != "seed") return 1;
			for (var scenario = 0; scenario < 2; scenario++)
			for (var failAt = 1; failAt <= 5; failAt++)
			{
				var capacity = scenario == 0 ? 4 : 64;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var values = new int[scenario == 0 ? 1 : 2]; values[0] = scenario == 0 ? 12 : 1;
				if (scenario != 0) values[1] = 23;
				var source = CreateInt32JoinEnumerable(list != 0, values);
				SetStringBuilderAllocationFailure(failAt);
				try { if (character == 0) builder.AppendJoin<int>("|", source); else builder.AppendJoin<int>('|', source); SetStringBuilderAllocationFailure(0); return 2; }
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				var expected = scenario == 0 ? "12" : "1|23";
				var retained = scenario == 0 || failAt <= 3 ? "seed" : "seed1|";
				if (builder.Capacity != capacity || builder.ToString(0, builder.Length) != retained) return 3;
				builder.Length = 4; builder.AppendJoin<int>('|', source);
				if (builder.ToString(0, builder.Length) != "seed" + expected) return 4;
				builder.Clear().AppendJoin<int>("|", source);
				if (builder.ToString(0, builder.Length) != expected) return 5;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int Int32CopyPattern(int index) => index == 0 ? int.MinValue : index == 1 ? -1 : index == 2 ? 0 : index == 3 ? int.MaxValue : index * 1234567 - 3456;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCustomFormattingEntry()
	{
		var provider = new BuilderFormatProvider();
		var value = new BuilderFormatValue(provider);
		var builder = new System.Text.StringBuilder(64);
		builder.AppendFormat(provider, "[{0:custom}][{1,-6:span}][{2,-8:fallback}]", "text", value, value);
		if (builder.ToString(0, builder.Length) != "[custom][x\u03A9z   ][formal  ]") return 1;
		if (provider.Queries != 1 || provider.Calls != 3 || value.SpanCalls != 2 || value.FormatCalls != 1) return 2;
		builder.Clear().AppendFormat(provider, "{0:custom}/{1}", (object?)null, "tail");
		if (builder.ToString(0, builder.Length) != "custom/tail" || provider.Queries != 2 || provider.Calls != 5) return 3;
		builder.Clear().Append("seed");
		try { builder.AppendFormat(provider, "pre{0:throw}post", "ignored"); return 5; }
		catch (InvalidOperationException) { }
		if (builder.ToString(0, builder.Length) != "seedpre" || provider.Queries != 3 || provider.Calls != 6) return 6;
		provider.ReturnFormatter = false;
		builder.Clear().AppendFormat(provider, "{0,-8:fallback}", value);
		if (builder.ToString(0, builder.Length) != "formal  " || provider.Queries != 4 || provider.Calls != 6 || value.FormatCalls != 2) return 7;
		M68kRuntime.Collect();
		return provider.ValidType && value.ValidProvider ? 42 : 4;
	}

	private sealed class BuilderFormatProvider : IFormatProvider, ICustomFormatter
	{
		public int Queries, Calls;
		public bool ValidType = true;
		public bool ReturnFormatter = true;
		object? IFormatProvider.GetFormat(Type? formatType)
		{
			Queries++;
			M68kRuntime.Collect();
			if (formatType != typeof(ICustomFormatter) || !ReferenceEquals(formatType, typeof(ICustomFormatter))) ValidType = false;
			return ReturnFormatter && formatType == typeof(ICustomFormatter) ? this : null;
		}
		string ICustomFormatter.Format(string? format, object? arg, IFormatProvider? provider)
		{
			Calls++;
			M68kRuntime.Collect();
			if (!ReferenceEquals(provider, this)) ValidType = false;
			if (format == "throw") M68kRuntime.ThrowInvalidOperationException();
			return format == "custom" ? "custom" : null!;
		}
	}

	private sealed class BuilderFormatValue : ISpanFormattable
	{
		private readonly IFormatProvider _provider;
		public int SpanCalls, FormatCalls;
		public bool ValidProvider = true;
		public BuilderFormatValue(IFormatProvider provider) => _provider = provider;
		bool ISpanFormattable.TryFormat(Span<char> destination, out int charsWritten, ReadOnlySpan<char> format, IFormatProvider? provider)
		{
			SpanCalls++;
			M68kRuntime.Collect();
			if (!ReferenceEquals(provider, _provider)) ValidProvider = false;
			charsWritten = 0;
			if (format.Length != 4 || destination.Length < 3) return false;
			destination[0] = 'x'; destination[1] = '\u03A9'; destination[2] = 'z';
			charsWritten = 3;
			return true;
		}
		string IFormattable.ToString(string? format, IFormatProvider? provider)
		{
			FormatCalls++;
			M68kRuntime.Collect();
			if (!ReferenceEquals(provider, _provider)) ValidProvider = false;
			return format == "fallback" ? "formal" : "failed";
		}
		public override string ToString() => "plain";
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibRuntimeTypeIdentityEntry()
	{
		Type first = typeof(ICustomFormatter), second = typeof(IFormattable);
		M68kRuntime.Collect();
		if (first == second || first != typeof(ICustomFormatter) || !ReferenceEquals(first, typeof(ICustomFormatter))) return 1;
		if (first == null || null == first || first != CoreLibFormatterType()) return 2;
		if (Type.GetTypeFromHandle(default) != null) return 3;
		object erased = first;
		M68kRuntime.Collect();
		return erased is Type && ReferenceEquals(erased, first) ? 42 : 4;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Type CoreLibFormatterType() => typeof(ICustomFormatter);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCompositeFormatContractEntry()
	{
		var builder = new System.Text.StringBuilder(1).Append("seed");
		if (!CoreLibExpectMalformedFormat(builder, "pre{", "seedpre")) return 1;
		if (!CoreLibExpectMalformedFormat(builder, "pre}", "seedpre")) return 2;
		if (!CoreLibExpectMalformedFormat(builder, "pre{2}", "seedpre")) return 3;
		if (!CoreLibExpectMalformedFormat(builder, "pre{0}mid{1", "seedpreamid")) return 4;
		if (!CoreLibExpectMalformedFormat(builder, "pre{0,+2}", "seedpre")) return 5;
		if (!CoreLibExpectMalformedFormat(builder, "pre{-1}", "seedpre")) return 6;
		if (!CoreLibExpectMalformedFormat(builder, "pre{0:{{}}}", "seedpre")) return 7;
		try { builder.AppendFormat((string)null!, "a"); return 8; }
		catch (ArgumentNullException exception) { if (exception.ParamName != "format") return 9; }
		if (builder.ToString(0, builder.Length) != "seed") return 10;
		builder.AppendFormat("|{0}:{0}|", "ok");
		M68kRuntime.Collect();
		if (builder.ToString(0, builder.Length) != "seed|ok:ok|") return 11;
		builder.Clear();
		builder.AppendFormat((IFormatProvider?)null, "<{0}|{1}|{2}|{3}>", CoreLibFreshFormatText('a'), CoreLibFreshFormatText('b'), CoreLibFreshFormatText('c'), CoreLibFreshFormatText('d'));
		var first = builder.ToString(0, builder.Length);
		if (first != "<a\u03A9|b\u03A9|c\u03A9|d\u03A9>") return 12;
		builder.Clear().AppendFormat("<{0}|{1}|{2}>", CoreLibFreshFormatText('e'), CoreLibFreshFormatText('f'), CoreLibFreshFormatText('g'));
		if (builder.ToString(0, builder.Length) != "<e\u03A9|f\u03A9|g\u03A9>") return 13;
		builder.Clear().AppendFormat("<{0}|{1}>", CoreLibFreshFormatText('h'), CoreLibFreshFormatText('i'));
		if (builder.ToString(0, builder.Length) != "<h\u03A9|i\u03A9>") return 14;
		builder.Clear().AppendFormat("<{0}>", CoreLibFreshFormatText('j'));
		if (builder.ToString(0, builder.Length) != "<j\u03A9>") return 15;
		object?[] array = new object?[3];
		array[0] = CoreLibFreshFormatText('k'); array[1] = CoreLibFreshFormatText('l');
		builder.Clear().AppendFormat("{{{0}}}/{1}/{2}", array);
		if (builder.ToString(0, builder.Length) != "{k\u03A9}/l\u03A9/") return 16;
		builder.Clear().AppendFormat((IFormatProvider?)null, "{0}/{1}", new ReadOnlySpan<object?>(array, 1, 2));
		if (builder.ToString(0, builder.Length) != "l\u03A9/" || (string)array[0]! != "k\u03A9") return 17;
		builder.Clear().AppendFormat("plain{{}}", new object[0]);
		if (builder.ToString(0, builder.Length) != "plain{}") return 18;
		try { builder.AppendFormat("pre", (object?[])null!); return 19; }
		catch (ArgumentNullException exception) { if (exception.ParamName != "args") return 20; }
		try { builder.AppendFormat((string)null!, (object?[])null!); return 21; }
		catch (ArgumentNullException exception) { if (exception.ParamName != "format") return 22; }
		M68kRuntime.Collect();
		return builder.ToString(0, builder.Length) == "plain{}" && first == "<a\u03A9|b\u03A9|c\u03A9|d\u03A9>" ? 42 : 23;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool CoreLibExpectMalformedFormat(System.Text.StringBuilder builder, string format, string expected)
	{
		try { builder.AppendFormat(format, "a", "b"); return false; }
		catch (FormatException) { }
		M68kRuntime.Collect();
		if (builder.ToString(0, builder.Length) != expected) return false;
		builder.Clear().Append("seed");
		return true;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CoreLibFreshFormatText(char value)
	{
		var data = new char[2]; data[0] = value; data[1] = '\u03A9';
		return new string(data.AsSpan());
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibTwoCharacterSearchEntry()
	{
		for (var length = 0; length <= 65; length++)
		for (var offset = 0; offset <= 3; offset++)
		{
			var storage = new char[length + offset + 2];
			for (var index = 0; index < storage.Length; index++) storage[index] = '#';
			for (var index = 0; index < length; index++) storage[index + offset] = "ab\0\u03A9\uD800{}"[index % 7];
			ReadOnlySpan<char> span = new ReadOnlySpan<char>(storage, offset, length);
			storage = null!;
			M68kRuntime.Collect();
			if (span.IndexOfAny('{', '}') != (length > 5 ? 5 : -1)) return 1;
			if (span.IndexOfAny('}', '{') != (length > 5 ? 5 : -1)) return 2;
			if (span.IndexOfAny('\0', '\uD800') != (length > 2 ? 2 : -1)) return 3;
			if (span.IndexOfAny('#', '#') != -1) return 4;
			if (span.IndexOfAny('a', 'a') != (length > 0 ? 0 : -1)) return 5;
			if (span.IndexOfAny('z', '\uFFFF') != -1) return 6;
		}
		ReadOnlySpan<char> empty = default;
		return empty.IndexOfAny('\0', '\0') == -1 ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibFormattingReferenceOwnerEntry()
	{
		ReadOnlySpan<object> values = CoreLibCreateFormattingReferences();
		M68kRuntime.Collect();
		var pressure = new object[8];
		for (var index = 0; index < pressure.Length; index++) pressure[index] = CoreLibFreshFormatText('p');
		M68kRuntime.Collect();
		if ((string)values[0] != "b\u03A9" || (string)values[1] != "c\u03A9" || pressure.Length != 8) return 1;
		var chars = new char[8];
		for (var index = 0; index < chars.Length; index++) chars[index] = (char)('a' + index);
		ref char character = ref System.Runtime.CompilerServices.Unsafe.Add(ref chars[0], (nint)5);
		character = ref System.Runtime.CompilerServices.Unsafe.Add(ref character, (nuint)1);
		chars = null!;
		M68kRuntime.Collect();
		var charPressure = new char[8];
		for (var index = 0; index < charPressure.Length; index++) charPressure[index] = '#';
		return character == 'g' && charPressure[7] == '#' ? 42 : 2;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlySpan<object> CoreLibCreateFormattingReferences()
	{
		var values = new object[4];
		for (var index = 0; index < values.Length; index++) values[index] = CoreLibFreshFormatText((char)('a' + index));
		ref object first = ref System.Runtime.CompilerServices.Unsafe.Add(ref values[0], (nuint)1);
		ref object same = ref System.Runtime.CompilerServices.Unsafe.As<object, object>(ref first);
		ref readonly object readonlyFirst = ref same;
		ref object writable = ref System.Runtime.CompilerServices.Unsafe.AsRef(in readonlyFirst);
		return System.Runtime.InteropServices.MemoryMarshal.CreateReadOnlySpan(ref writable, 2);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCompositeFormatEntry()
	{
		var builder = new System.Text.StringBuilder(4).Append("seed");
		if (builder.AppendFormat("|{{{0}}}|{1,5}|{2,-4}|", -12, "xy", null) != builder) return 1;
		var snapshot = builder.ToString(0, builder.Length);
		if (snapshot != "seed|{-12}|   xy|    |") return 2;
		M68kRuntime.Collect();
		builder.Clear().AppendFormat("{1}:{0}:{1}", "value", 42);
		if (builder.ToString(0, builder.Length) != "42:value:42" || snapshot != "seed|{-12}|   xy|    |") return 3;
		builder.Clear().AppendFormat("{0}/{1}/{2}", uint.MaxValue, long.MinValue, ulong.MaxValue);
		if (builder.ToString(0, builder.Length) != "4294967295/-9223372036854775808/18446744073709551615") return 4;
		builder.Clear().AppendFormat("{0}/{1}/{2}", false, '\u03A9', (short)-32768);
		if (builder.ToString(0, builder.Length) != "False/\u03A9/-32768") return 5;
		builder.Clear().AppendFormat("{0}/{1}/{2}", sbyte.MinValue, byte.MaxValue, ushort.MaxValue);
		return builder.ToString(0, builder.Length) == "-128/255/65535" ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibNumberFormatInfoStateEntry()
	{
		var info = new System.Globalization.NumberFormatInfo();
		if (info.NegativeSign != "-" || info.PositiveSign != "+" || info.IsReadOnly) return 1;
		var chars = new char[3]; chars[0] = '\u2212'; chars[1] = '\0'; chars[2] = '\u03A9';
		var sign = new string(new ReadOnlySpan<char>(chars));
		info.NegativeSign = sign;
		info.PositiveSign = "plus";
		chars = null!; sign = null!;
		M68kRuntime.Collect();
		var pressure = new char[80]; pressure[0] = 'x';
		if (info.NegativeSign != "\u2212\0\u03A9" || info.PositiveSign != "plus") return 2;
		if (!ReferenceEquals(info, info.GetFormat(typeof(System.Globalization.NumberFormatInfo))) || info.GetFormat(typeof(ICustomFormatter)) is not null) return 3;
		try { info.NegativeSign = null!; return 4; } catch (ArgumentNullException error) { if (error.ParamName != "value") return 5; }
		if (info.NegativeSign != "\u2212\0\u03A9") return 6;
		info.NegativeSign = string.Empty;
		return info.NegativeSign.Length == 0 && pressure[0] == 'x' ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderDecimalProvidersEntry()
	{
		for (var signCase = 0; signCase < 4; signCase++)
		for (var width = 0; width < 4; width++)
		for (var providerCase = 0; providerCase < 2; providerCase++)
		for (var capacityCase = 0; capacityCase < 2; capacityCase++)
		for (var formatCase = 0; formatCase < 3; formatCase++)
		{
			var info = new System.Globalization.NumberFormatInfo();
			info.NegativeSign = new string(DecimalSignText(signCase).AsSpan());
			var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider provider = providerCase == 0 ? info : supplied;
			info = null!;
			M68kRuntime.Collect();
			var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 80);
			var value = StandardIntegerValue(width * 8);
			builder.AppendFormat(provider, formatCase == 0 ? "{0}" : formatCase == 1 ? "{0:D24}" : "{0:g0}", value);
			var snapshot = builder.ToString(0, builder.Length);
			var sign = DecimalSignText(signCase);
			var digits = DecimalMagnitudeText(width);
			var padding = formatCase == 1 ? 24 - digits.Length : 0;
			if (snapshot.Length != sign.Length + padding + digits.Length) return 1;
			for (var index = 0; index < snapshot.Length; index++)
				if (snapshot[index] != (index < sign.Length ? sign[index] : index < sign.Length + padding ? '0' : digits[index - sign.Length - padding])) return 2;
			if (providerCase != 0 && (supplied.CustomQueries != 1 || supplied.NumberQueries != (capacityCase == 0 ? 2 : 1))) return 3;
			supplied.Info!.NegativeSign = "changed";
			builder.Clear().Append("changed");
			M68kRuntime.Collect();
			for (var index = 0; index < snapshot.Length; index++)
				if (snapshot[index] != (index < sign.Length ? sign[index] : index < sign.Length + padding ? '0' : digits[index - sign.Length - padding])) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibDecimalProviderContractsEntry()
	{
		for (var mode = 0; mode < 3; mode++)
		{
			var provider = new CoreLibDecimalSignProvider { Info = new System.Globalization.NumberFormatInfo { NegativeSign = "custom" }, Mode = mode };
			var builder = new System.Text.StringBuilder(80).AppendFormat(provider, "[{0:D4}]", -12);
			if (builder.ToString(0, builder.Length) != (mode == 0 ? "[custom0012]" : "[-0012]") || provider.NumberQueries != 1 || provider.CustomQueries != 1) return 1;
		}
		var throwing = new CoreLibDecimalSignProvider { Mode = 3, Error = new InvalidOperationException("provider") };
		var text = new System.Text.StringBuilder(80).Append("seed");
		try { text.AppendFormat(throwing, "pre{0:D4}post", -12); return 2; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || text.ToString(0, text.Length) != "seedpre") return 3; }
		throwing.NumberQueries = 0; throwing.CustomQueries = 0;
		text.Clear().AppendFormat(throwing, "{0:D4}/{1:D4}/{2:D4}", 12, uint.MaxValue, 12L);
		if (text.ToString(0, text.Length) != "0012/4294967295/0012" || throwing.NumberQueries != 0 || throwing.CustomQueries != 1) return 4;
		text.Clear().AppendFormat(throwing, "{0:X4}/{1:B10}", (sbyte)-1, (short)-1);
		if (text.ToString(0, text.Length) != "00FF/1111111111111111" || throwing.NumberQueries != 0) return 5;
		text.Clear().Append("seed");
		try { text.AppendFormat(throwing, "pre{0:D1000000000}post", -12); return 6; } catch (FormatException) { }
		if (text.ToString(0, text.Length) != "seedpre" || throwing.NumberQueries != 0) return 7;
		var changing = new CoreLibDecimalSignProvider { Info = new System.Globalization.NumberFormatInfo(), Mode = 4 };
		text = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:D2}", -12);
		return text.ToString(0, text.Length) == "second12" && changing.NumberQueries == 2 && changing.CustomQueries == 1 ? 42 : 8;
	}

	private static string DecimalSignText(int scenario) => scenario == 0 ? "-" : scenario == 1 ? string.Empty : scenario == 2 ? "minus" : "\u2212\0\u03A9";
	private static string DecimalMagnitudeText(int width) => width == 0 ? "128" : width == 1 ? "32768" : width == 2 ? "2147483648" : "9223372036854775808";

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibDecimalProviderSpanHelpersEntry()
	{
		for (var signCase = 0; signCase < 4; signCase++)
		for (var width = 0; width < 4; width++)
		for (var formatCase = 0; formatCase < 3; formatCase++)
		for (var providerCase = 0; providerCase < 2; providerCase++)
		{
			var info = new System.Globalization.NumberFormatInfo { NegativeSign = new string(DecimalSignText(signCase).AsSpan()) };
			var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider provider = providerCase == 0 ? info : supplied;
			info = null!;
			var sign = DecimalSignText(signCase); var digits = DecimalMagnitudeText(width);
			var padding = formatCase == 1 ? 24 - digits.Length : 0;
			var length = sign.Length + padding + digits.Length;
			for (var sizeCase = 0; sizeCase < 4; sizeCase++)
			{
				var size = sizeCase == 0 ? 0 : length + sizeCase - 2;
				var storage = new char[size + 2];
				for (var index = 0; index < storage.Length; index++) storage[index] = '#';
				var before = supplied.NumberQueries;
				M68kRuntime.Collect();
				var success = TryProvidedSignedDecimal(width, formatCase == 0 ? string.Empty : formatCase == 1 ? "D24" : "G0", provider,
					new Span<char>(storage, 1, size), out var written);
				M68kRuntime.Collect();
				if (success != (size >= length) || written != (success ? length : 0)) return 1;
				if (providerCase != 0 && (supplied.NumberQueries != before + 1 || supplied.CustomQueries != 0)) return 2;
				for (var index = 0; index < storage.Length; index++)
				{
					var expected = '#'; var offset = index - 1;
					if (success && offset >= 0 && offset < written)
						expected = offset < sign.Length ? sign[offset] : offset < sign.Length + padding ? '0' : digits[offset - sign.Length - padding];
					if (storage[index] != expected) return 3;
				}
			}
		}
		return 42;
	}

	private static bool TryProvidedSignedDecimal(int width, ReadOnlySpan<char> format, IFormatProvider provider, Span<char> destination, out int written) =>
		width == 3 ? CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(long.MinValue, format, provider, destination, out written)
			: CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(width == 0 ? sbyte.MinValue : width == 1 ? short.MinValue : int.MinValue,
				width == 0 ? 255 : width == 1 ? 65535 : -1, format, provider, destination, out written);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibDecimalProviderAllocationContractsEntry()
	{
		for (var signCase = 0; signCase < 4; signCase++)
		for (var width = 0; width < 4; width++)
		{
			var info = new System.Globalization.NumberFormatInfo { NegativeSign = new string(DecimalSignText(signCase).AsSpan()) };
			var provider = new CoreLibDecimalSignProvider { Info = info };
			var buffer = new char[24 + info.NegativeSign.Length];
			SetStringBuilderAllocationFailure(1);
			var success = TryProvidedSignedDecimal(width, "D24", provider, buffer, out var written);
			SetStringBuilderAllocationFailure(0);
			if (!success || written != buffer.Length || provider.NumberQueries != 1) return 1;
			SetStringBuilderAllocationFailure(2);
			var text = width == 3 ? CopperSharp.Runtime.ShadowNumberFormatting.FormatInt64(long.MinValue, "D24", provider)
				: CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(width == 0 ? sbyte.MinValue : width == 1 ? short.MinValue : int.MinValue,
					width == 0 ? 255 : width == 1 ? 65535 : -1, "D24", provider);
			SetStringBuilderAllocationFailure(0);
			if (text.Length != written || provider.NumberQueries != 2) return 2;
			for (var index = 0; index < written; index++) if (text[index] != buffer[index]) return 3;
		}
		for (var signCase = 0; signCase < 4; signCase++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var provider = new CoreLibDecimalSignProvider { Info = new System.Globalization.NumberFormatInfo { NegativeSign = new string(DecimalSignText(signCase).AsSpan()) } };
			var builder = new System.Text.StringBuilder(80).Append("seed");
			object value = long.MinValue;
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(provider, "pre{0:D100}post", value); SetStringBuilderAllocationFailure(0); return 4; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre" || provider.NumberQueries != failAt || provider.CustomQueries != 1) return 5;
			builder.Clear().AppendFormat(provider, "{0:D24}", value);
			var sign = DecimalSignText(signCase); var digits = DecimalMagnitudeText(3);
			var text = builder.ToString(0, builder.Length);
			if (text.Length != 24 + sign.Length) return 6;
			for (var index = 0; index < text.Length; index++)
				if (text[index] != (index < sign.Length ? sign[index] : index < sign.Length + 5 ? '0' : digits[index - sign.Length - 5])) return 7;
		}
		return 42;
	}

	private sealed class CoreLibDecimalSignProvider : IFormatProvider
	{
		public System.Globalization.NumberFormatInfo? Info;
		public int Mode;
		public int NumberQueries;
		public int CustomQueries;
		public InvalidOperationException? Error;
		public object? GetFormat(Type? formatType)
		{
			M68kRuntime.Collect();
			if (formatType == typeof(ICustomFormatter)) { CustomQueries++; return null; }
			if (formatType != typeof(System.Globalization.NumberFormatInfo)) throw new InvalidOperationException("type query");
			NumberQueries++;
			if (Mode == 1) return null;
			if (Mode == 2) return "wrong";
			if (Mode == 3) throw Error!;
			if (Mode == 4) Info!.NegativeSign = NumberQueries == 1 ? "first" : "second";
			if (Mode == 5)
			{
				Info!.NegativeSign = NumberQueries == 1 ? "first" : "second";
				Info.NumberDecimalSeparator = NumberQueries == 1 ? ":" : "::";
				Info.NumberDecimalDigits = NumberQueries == 1 ? 1 : 3;
			}
			if (Mode == 6)
			{
				Info!.NegativeSign = NumberQueries == 1 ? "first" : "second";
				Info.NumberDecimalSeparator = NumberQueries == 1 ? ":" : "::";
				Info.PositiveSign = NumberQueries == 1 ? "one" : "two";
			}
			if (Mode == 7)
			{
				Info!.NegativeSign = NumberQueries == 1 ? "first" : "second";
				Info.NumberDecimalSeparator = NumberQueries == 1 ? ":" : "::";
				Info.NumberGroupSeparator = NumberQueries == 1 ? "a" : "b";
				Info.NumberGroupSizes = NumberQueries == 1 ? [3] : [2];
				Info.NumberNegativePattern = NumberQueries == 1 ? 0 : 4;
				Info.NumberDecimalDigits = NumberQueries == 1 ? 1 : 3;
			}
			return Info;
		}
	}

	private static long _duplicatedInteger;
	private static double _duplicatedDouble;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static double DuplicateDoubleAssignment(double value) => _duplicatedDouble = value;
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long DuplicateLongAssignment(long value) => _duplicatedInteger = value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DuplicateLongAssignmentEntry()
	{
		if (DuplicateLongAssignment(0x12345678_76543210L) != 0x12345678_76543210L || _duplicatedInteger != 0x12345678_76543210L) return 1;
		if (DuplicateLongAssignment(long.MinValue) != long.MinValue || _duplicatedInteger != long.MinValue) return 2;
		if (DuplicateLongAssignment(long.MaxValue) != long.MaxValue || _duplicatedInteger != long.MaxValue) return 3;
		if (DuplicateLongAssignment(-1) != -1 || _duplicatedInteger != -1) return 4;
		return DuplicateLongAssignment(0) == 0 && _duplicatedInteger == 0 ? 42 : 5;
	}

	private static System.Globalization.NumberFormatInfo GroupedInfo(int style, int pattern) => new()
	{
		NegativeSign = new string("\u2212\0\u03A9".AsSpan()),
		NumberDecimalSeparator = new string("\u03A9\0\uD83D\uDE00".AsSpan()),
		NumberGroupSeparator = new string("\0\uD800".AsSpan()), NumberDecimalDigits = 4, NumberNegativePattern = pattern,
		NumberGroupSizes = style switch { 0 => [3], 1 => [3, 2], 2 => [3, 2, 0], 3 => [1, 0], 4 => [], 5 => [0], 6 => [1], _ => [9] }
	};

	private static string GroupedFormat(int scenario) => scenario == 0 ? "N" : scenario == 1 ? "n0" : scenario == 2 ? "N3" : "n65";
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibGroupedSmokeEntry()
	{
		Span<char> storage = stackalloc char[64];
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12345, -1, "N3", null, storage, out var count)) return 1;
		if (count != 11 || new string(storage.Slice(0, count)) != "-12,345.000") return 2;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(-12345, -1, "N3", null) != "-12,345.000") return 3;
		var info = new System.Globalization.NumberFormatInfo { NumberGroupSizes = [2], NumberNegativePattern = 4 };
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12345, -1, "N3", info, storage, out count)) return 4;
		if (count != 13 || new string(storage.Slice(0, count)) != "1,23,45.000 -") return 5;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(-12345, -1, "N3", info) != "1,23,45.000 -") return 6;
		if (!CopperSharp.Runtime.ShadowGroupedIntegerFormatting.TryFormat(0, 12345, false, info, 3, storage, out count)) return 7;
		if (count != 11 || new string(storage.Slice(0, count)) != "1,23,45.000") return 8;
		return 42;
	}
	private static string GroupedComposite(int scenario) => scenario == 0 ? "{0:N}" : scenario == 1 ? "{0:n0}" : scenario == 2 ? "{0:N3}" : "{0:n65}";
	private static int GroupedPrecision(int scenario, bool custom) => scenario == 0 ? custom ? 4 : 2 : scenario == 1 ? 0 : scenario == 2 ? 3 : 65;
	private static string GroupedMagnitude(int width, bool custom) => width switch
	{
		0 => "128", 1 => "255", 2 => "32,768", 3 => "65,535",
		4 => custom ? "2,14,74,83,648" : "2,147,483,648", 5 => custom ? "4,29,49,67,295" : "4,294,967,295",
		6 => custom ? "92,23,37,20,36,85,47,75,808" : "9,223,372,036,854,775,808",
		_ => custom ? "1,84,46,74,40,73,70,95,51,615" : "18,446,744,073,709,551,615"
	};

	private static string GroupingContractMagnitude(int style, bool unsigned) => style switch
	{
		0 => unsigned ? "18,446,744,073,709,551,615" : "9,223,372,036,854,775,808",
		1 => unsigned ? "1,84,46,74,40,73,70,95,51,615" : "92,23,37,20,36,85,47,75,808",
		2 => unsigned ? "184467440737095,51,615" : "92233720368547,75,808",
		3 => unsigned ? "1844674407370955161,5" : "922337203685477580,8",
		4 or 5 => unsigned ? "18446744073709551615" : "9223372036854775808",
		6 => unsigned ? "1,8,4,4,6,7,4,4,0,7,3,7,0,9,5,5,1,6,1,5" : "9,2,2,3,3,7,2,0,3,6,8,5,4,7,7,5,8,0,8",
		_ => unsigned ? "18,446744073,709551615" : "9,223372036,854775808"
	};

	// The group locations are literal oracles; this only expands provider
	// tokens and adds the five documented negative-pattern decorations.
	private static string GroupedExpected(string magnitude, bool negative, bool custom, int pattern, int precision)
	{
		var sign = custom ? "\u2212\0\u03A9" : "-";
		var text = new System.Text.StringBuilder(128);
		if (negative && pattern == 0) text.Append('(');
		if (negative && (pattern == 1 || pattern == 2)) text.Append(sign);
		if (negative && pattern == 2) text.Append(' ');
		for (var index = 0; index < magnitude.Length; index++)
			if (magnitude[index] == ',') text.Append(custom ? "\0\uD800" : ","); else text.Append(magnitude[index]);
		if (precision != 0) text.Append(custom ? "\u03A9\0\uD83D\uDE00" : ".").Append('0', precision);
		if (negative && pattern == 4) text.Append(' ');
		if (negative && (pattern == 3 || pattern == 4)) text.Append(sign);
		if (negative && pattern == 0) text.Append(')');
		return text.ToString(0, text.Length);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderGroupedIntegersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var info = GroupedInfo(1, 2); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			info = null!;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				var custom = providerCase != 0;
				var expected = GroupedExpected(GroupedMagnitude(width, custom), (width & 1) == 0, custom, custom ? 2 : 1, GroupedPrecision(formatCase, custom));
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 128);
				supplied.NumberQueries = 0; supplied.CustomQueries = 0; M68kRuntime.Collect();
				if (builder.AppendFormat(provider, GroupedComposite(formatCase), StandardIntegerValue(width * 4)) != builder) return 1;
				var snapshot = builder.ToString(0, builder.Length);
				if (snapshot != expected || providerCase == 2 && (supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1)) return 2;
				builder.Clear().Append("changed"); M68kRuntime.Collect();
				if (snapshot != expected) return 3;
			}
		}
		return 42;
	}

	private static bool CheckGroupedSpan(object value, string format, string expected, IFormatProvider? provider, int sizeCase)
	{
		var size = sizeCase == 0 ? 0 : expected.Length + sizeCase - 2;
		var storage = new char[size + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
		M68kRuntime.Collect();
		var success = TryStandardInteger(value, format, new Span<char>(storage, 1, size), out var written, provider);
		M68kRuntime.Collect();
		if (success != (size >= expected.Length) || written != (success ? expected.Length : 0)) return false;
		for (var index = 0; index < storage.Length; index++)
			if (storage[index] != (success && index > 0 && index <= written ? expected[index - 1] : '#')) return false;
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibGroupedIntegerSpanHelpersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var info = GroupedInfo(1, 2); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			info = null!;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			{
				var custom = providerCase != 0;
				var expected = GroupedExpected(GroupedMagnitude(width, custom), (width & 1) == 0, custom, custom ? 2 : 1, GroupedPrecision(formatCase, custom));
				var value = StandardIntegerValue(width * 4);
				for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				{
					var before = supplied.NumberQueries;
					if (!CheckGroupedSpan(value, GroupedFormat(formatCase), expected, provider, sizeCase)) return 1;
					if (providerCase == 2 && (supplied.NumberQueries != before + 1 || supplied.CustomQueries != 0)) return 2;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibGroupedProviderContractsEntry()
	{
		for (var style = 0; style < 8; style++)
		for (var pattern = 0; pattern < 5; pattern++)
		for (var valueCase = 0; valueCase < 3; valueCase++)
		{
			RecordGroupedProgress(100 + style * 100 + pattern * 10 + valueCase);
			var info = GroupedInfo(style, pattern); var supplied = new CoreLibDecimalSignProvider { Info = info };
			var magnitude = valueCase == 0 ? "0" : GroupingContractMagnitude(style, valueCase == 2);
			var expected = GroupedExpected(magnitude, valueCase == 1, true, pattern, 3);
			object value = valueCase == 0 ? 0 : valueCase == 1 ? (object)long.MinValue : ulong.MaxValue;
			info = null!;
			for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				if (!CheckGroupedSpan(value, "N3", expected, supplied, sizeCase)) return 1;
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				supplied.NumberQueries = 0; supplied.CustomQueries = 0;
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 128);
				builder.AppendFormat(supplied, "{0:N3}", value);
				var snapshot = builder.ToString(0, builder.Length);
				if (snapshot != expected || supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1) return 2;
				supplied.Info!.NumberGroupSizes = [1]; supplied.Info.NumberNegativePattern = 1;
				builder.Clear(); M68kRuntime.Collect();
				if (snapshot != expected) return 3;
				supplied.Info = GroupedInfo(style, pattern);
			}
		}
		RecordGroupedProgress(1000);
		var state = new System.Globalization.NumberFormatInfo { NegativeSign = "", NumberGroupSeparator = "", NumberDecimalSeparator = "::", NumberDecimalDigits = 0, NumberNegativePattern = 0 };
		var output = new System.Text.StringBuilder(128).AppendFormat(state, "[{0,8:N}][{1,-8:n3}][{2:N0}]", -12, 0u, 1000);
		if (output.ToString(0, output.Length) != "[    (12)][0::000  ][1000]") return 4;
		output.Clear(); state.NegativeSign = "minus"; state.NumberGroupSeparator = "_"; state.NumberDecimalDigits = 3; state.NumberNegativePattern = 3;
		output.AppendFormat(state, "{0:N}|{1:N\0ignored}|{2:n000000000003}", -1000, -1000, 1000u);
		if (output.ToString(0, output.Length) != "1_000::000minus|1_000minus|1_000::000") return 5;
		for (var mode = 1; mode <= 2; mode++)
		{
			var provider = new CoreLibDecimalSignProvider { Mode = mode };
			output.Clear().AppendFormat(provider, "{0:N}/{1:N0}/{2:n3}", -1000, 0u, ulong.MaxValue);
			if (output.ToString(0, output.Length) != "-1,000.00/0/18,446,744,073,709,551,615.000" || provider.NumberQueries != 3 || provider.CustomQueries != 1) return 6;
		}
		RecordGroupedProgress(1001);
		var changing = new CoreLibDecimalSignProvider { Mode = 7, Info = new System.Globalization.NumberFormatInfo() };
		output = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:N}", -12345);
		if (output.ToString(0, output.Length) != "1b23b45::000 second" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 7;
		var throwing = new CoreLibDecimalSignProvider { Mode = 3, Error = new InvalidOperationException("provider") };
		output = new System.Text.StringBuilder(128).Append("seed");
		try { output.AppendFormat(throwing, "pre{0:N0}post", 0u); return 8; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || output.ToString(0, output.Length) != "seedpre") return 9; }
		throwing.NumberQueries = 0;
		try { output.AppendFormat(throwing, "{0:N1000000000}", 12u); return 10; } catch (FormatException) { }
		if (throwing.NumberQueries != 0) return 11;
		var written = 37;
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(0, "N0", throwing, Span<char>.Empty, out written); return 12; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || written != 37) return 13; }
		RecordGroupedProgress(1002);
		// The public setter and getter must keep their independent-copy contracts.
		int[] original = [3, 2, 0]; state.NumberGroupSizes = original; original[0] = 1;
		var copy = state.NumberGroupSizes; copy[0] = 1;
		if (state.NumberGroupSizes[0] != 3) return 14;
		try { state.NumberGroupSizes = [0, 3]; return 15; } catch (ArgumentException) { }
		try { state.NumberGroupSizes = [10]; return 16; } catch (ArgumentException) { }
		try { state.NumberGroupSizes = null!; return 17; } catch (ArgumentNullException error) { if (error.ParamName != "value") return 18; }
		try { state.NumberNegativePattern = 5; return 19; } catch (ArgumentOutOfRangeException) { }
		return state.NumberGroupSizes[0] == 3 && state.NumberNegativePattern == 3 ? 42 : 20;
	}

	[M68kImport("fixture.grouped-progress")]
	private static extern void RecordGroupedProgress([M68kRegister(M68kRegister.D0)] int value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibGroupedIntegerAllocationContractsEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		for (var width = 0; width < 8; width++)
		{
			var info = GroupedInfo(1, 2); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			var custom = providerCase != 0; var formatCase = width % 4;
			var value = StandardIntegerValue(width * 4); var format = GroupedFormat(formatCase);
			var expected = GroupedExpected(GroupedMagnitude(width, custom), (width & 1) == 0, custom, custom ? 2 : 1, GroupedPrecision(formatCase, custom));
			var storage = new char[expected.Length + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
			SetStringBuilderAllocationFailure(1);
			var shortSuccess = TryStandardInteger(value, format, new Span<char>(storage, 1, expected.Length - 1), out var shortWritten, provider);
			SetStringBuilderAllocationFailure(0);
			if (shortSuccess || shortWritten != 0) return 1;
			for (var index = 0; index < storage.Length; index++) if (storage[index] != '#') return 2;
			SetStringBuilderAllocationFailure(1);
			var success = TryStandardInteger(value, format, new Span<char>(storage, 1, expected.Length), out var written, provider);
			SetStringBuilderAllocationFailure(0);
			if (!success || written != expected.Length) return 3;
			for (var index = 0; index < storage.Length; index++) if (storage[index] != (index > 0 && index <= written ? expected[index - 1] : '#')) return 4;
			SetStringBuilderAllocationFailure(2);
			var text = FormatStandardInteger(value, format, provider);
			SetStringBuilderAllocationFailure(0);
			if (text != expected || providerCase == 2 && supplied.NumberQueries != 3) return 5;
		}
		var guard = new char[1]; guard[0] = '#';
		SetStringBuilderAllocationFailure(1);
		var hugeSuccess = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(long.MinValue, "N999999999", null, guard, out var hugeWritten);
		SetStringBuilderAllocationFailure(0);
		if (hugeSuccess || hugeWritten != 0 || guard[0] != '#') return 6;
		for (var style = 0; style < 3; style++)
		for (var pattern = 0; pattern < 5; pattern++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var supplied = new CoreLibDecimalSignProvider { Info = GroupedInfo(style, pattern) };
			var expected = GroupedExpected(GroupingContractMagnitude(style, false), true, true, pattern, 0);
			var builder = new System.Text.StringBuilder(8).Append("seed"); object value = long.MinValue;
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(supplied, "pre{0:N65}post", value); SetStringBuilderAllocationFailure(0); return 7; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre" || supplied.NumberQueries != failAt || supplied.CustomQueries != 1) return 8;
			builder.Clear().AppendFormat(supplied, "{0:N0}", value);
			if (builder.ToString(0, builder.Length) != expected) return 9;
		}
		var state = GroupedInfo(1, 1); int[] input = [1, 0];
		SetStringBuilderAllocationFailure(1);
		try { state.NumberGroupSizes = input; SetStringBuilderAllocationFailure(0); return 10; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		if (state.NumberGroupSizes[0] != 3 || input[0] != 1) return 11;
		SetStringBuilderAllocationFailure(2);
		state.NumberGroupSizes = input;
		SetStringBuilderAllocationFailure(0);
		input[0] = 9;
		if (state.NumberGroupSizes[0] != 1) return 12;
		SetStringBuilderAllocationFailure(1);
		try { var failedCopy = state.NumberGroupSizes; SetStringBuilderAllocationFailure(0); return 13; }
		catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
		SetStringBuilderAllocationFailure(2);
		var copy = state.NumberGroupSizes;
		SetStringBuilderAllocationFailure(0);
		copy[0] = 9;
		return state.NumberGroupSizes[0] == 1 ? 42 : 14;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderScientificIntegersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var info = ScientificInfo(); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			info = null!; M68kRuntime.Collect();
			for (var scenario = 0; scenario < 32; scenario++)
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 128);
				supplied.NumberQueries = 0; supplied.CustomQueries = 0;
				if (builder.AppendFormat(provider, ScientificCompositeFormat(scenario), StandardIntegerValue(scenario)) != builder) return 1;
				var snapshot = builder.ToString(0, builder.Length);
				if (!CheckScientificText(snapshot.AsSpan(), ScientificText(scenario), providerCase != 0)) return 2;
				if (providerCase == 2 && (supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1)) return 3;
				builder.Clear().Append("changed"); M68kRuntime.Collect();
				if (!CheckScientificText(snapshot.AsSpan(), ScientificText(scenario), providerCase != 0)) return 4;
			}
		}
		return 42;
	}

	private static System.Globalization.NumberFormatInfo ScientificInfo() => new()
	{
		NegativeSign = new string("\u2212\0\u03A9".AsSpan()),
		PositiveSign = new string("+\0\u03A9".AsSpan()),
		NumberDecimalSeparator = new string("\u03A9\0\uD83D\uDE00".AsSpan()), NumberDecimalDigits = 99,
		NumberNegativePattern = 0
	};

	private static string ScientificCompositeFormat(int scenario) => (scenario % 4) switch
	{
		0 => "{0:E0}", 1 => "{0:e3}", 2 => "{0:G3}", _ => "{0:r1}"
	};

	private static string ScientificText(int scenario) => scenario switch
	{
		0 => "-1E+002", 1 => "-1.280e+002", 2 => "-128", 3 => "-1e+02",
		4 => "3E+002", 5 => "2.550e+002", 6 => "255", 7 => "3e+02",
		8 => "-3E+004", 9 => "-3.277e+004", 10 => "-3.28E+04", 11 => "-3e+04",
		12 => "7E+004", 13 => "6.554e+004", 14 => "6.55E+04", 15 => "7e+04",
		16 => "-2E+009", 17 => "-2.147e+009", 18 => "-2.15E+09", 19 => "-2e+09",
		20 => "4E+009", 21 => "4.295e+009", 22 => "4.29E+09", 23 => "4e+09",
		24 => "-9E+018", 25 => "-9.223e+018", 26 => "-9.22E+18", 27 => "-9e+18",
		28 => "2E+019", 29 => "1.845e+019", 30 => "1.84E+19", _ => "2e+19"
	};

	private static string ScientificToken(char character) => character == '-' ? "\u2212\0\u03A9" : character == '+' ? "+\0\u03A9" : "\u03A9\0\uD83D\uDE00";
	private static int ScientificTextLength(string expected, bool custom)
	{
		var length = 0;
		for (var index = 0; index < expected.Length; index++)
			length += custom && (expected[index] == '-' || expected[index] == '+' || expected[index] == '.') ? ScientificToken(expected[index]).Length : 1;
		return length;
	}

	private static bool CheckScientificText(ReadOnlySpan<char> text, string expected, bool custom)
	{
		if (text.Length != ScientificTextLength(expected, custom)) return false;
		var offset = 0;
		for (var index = 0; index < expected.Length; index++)
		{
			var character = expected[index];
			if (custom && (character == '-' || character == '+' || character == '.'))
			{
				var token = ScientificToken(character);
				for (var codeUnit = 0; codeUnit < token.Length; codeUnit++) if (text[offset++] != token[codeUnit]) return false;
			}
			else if (text[offset++] != character) return false;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibScientificIntegerSpanHelpersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var info = ScientificInfo(); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			info = null!;
			for (var scenario = 0; scenario < 32; scenario++)
			{
				var expected = ScientificText(scenario); var custom = providerCase != 0;
				var length = ScientificTextLength(expected, custom);
				var value = StandardIntegerValue(scenario); var format = ScientificCompositeFormat(scenario);
				for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				{
					var size = sizeCase == 0 ? 0 : length + sizeCase - 2;
					var storage = new char[size + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
					var before = supplied.NumberQueries; M68kRuntime.Collect();
					var success = TryStandardInteger(value, format.AsSpan(3, format.Length - 4), new Span<char>(storage, 1, size), out var written, provider);
					M68kRuntime.Collect();
					if (success != (size >= length) || written != (success ? length : 0)) return 1;
					if (providerCase == 2 && (supplied.NumberQueries != before + 1 || supplied.CustomQueries != 0)) return 2;
					if (success && !CheckScientificText(new ReadOnlySpan<char>(storage, 1, written), expected, custom)) return 3;
					for (var index = 0; index < storage.Length; index++)
						if ((!success || index == 0 || index > written) && storage[index] != '#') return 4;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibScientificProviderContractsEntry()
	{
		var info = new System.Globalization.NumberFormatInfo { NegativeSign = "", PositiveSign = "", NumberDecimalSeparator = "::", NumberDecimalDigits = 0 };
		var builder = new System.Text.StringBuilder(128).AppendFormat(info, "[{0,12:E1}][{1,-8:r1}][{2:R}]", -25, 250, 0u);
		if (builder.ToString(0, builder.Length) != "[    2::5E001][3e02    ][0]") return 1;
		var snapshot = builder.ToString(0, builder.Length);
		info.NegativeSign = "minus"; info.PositiveSign = "plus"; info.NumberDecimalSeparator = ","; M68kRuntime.Collect();
		builder.Clear().AppendFormat(info, "{0:E}|{1:E\0ignored}|{2:G2}|{3:r0}", -250, 25, 1250, -1000);
		if (builder.ToString(0, builder.Length) != "minus2,500000Eplus002|3Eplus001|1,3Eplus03|minus1000" || snapshot != "[    2::5E001][3e02    ][0]") return 2;
		builder.Clear().AppendFormat("{0:E2}/{1:G3}/{2:g4}/{3:r3}", 9995, 9995, 9995, 12995);
		if (builder.ToString(0, builder.Length) != "1.00E+004/1E+04/9995/1.3e+04") return 3;
		for (var mode = 1; mode <= 2; mode++)
		{
			var provider = new CoreLibDecimalSignProvider { Mode = mode };
			builder.Clear().AppendFormat(provider, "{0:E0}/{1:G3}/{2:R}", 0u, 9995, ulong.MaxValue);
			if (builder.ToString(0, builder.Length) != "0E+000/1E+04/18446744073709551615" || provider.NumberQueries != 3 || provider.CustomQueries != 1) return 4;
		}
		var changing = new CoreLibDecimalSignProvider { Mode = 6, Info = new System.Globalization.NumberFormatInfo() };
		builder = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:E1}", -12);
		if (builder.ToString(0, builder.Length) != "second1::2Etwo001" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 5;
		var throwing = new CoreLibDecimalSignProvider { Mode = 3, Error = new InvalidOperationException("provider") };
		builder = new System.Text.StringBuilder(128).Append("seed");
		try { builder.AppendFormat(throwing, "pre{0:G9}post", 12u); return 6; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || builder.ToString(0, builder.Length) != "seedpre") return 7; }
		throwing.NumberQueries = 0; builder.Clear().Append("seed");
		try { builder.AppendFormat(throwing, "pre{0:E1000000000}post", -12); return 8; } catch (FormatException) { }
		if (throwing.NumberQueries != 0 || builder.ToString(0, builder.Length) != "seedpre") return 9;
		var written = 37;
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(0, "R", throwing, Span<char>.Empty, out written); return 10; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || written != 37) return 11; }
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibScientificIntegerAllocationContractsEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		for (var width = 0; width < 8; width++)
		{
			var info = ScientificInfo(); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			var scenario = width * 4 + width % 4; var value = StandardIntegerValue(scenario);
			var expected = ScientificText(scenario); var custom = providerCase != 0;
			var composite = ScientificCompositeFormat(scenario); var format = composite.AsSpan(3, composite.Length - 4);
			var length = ScientificTextLength(expected, custom);
			var storage = new char[length + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
			SetStringBuilderAllocationFailure(1);
			var shortSuccess = TryStandardInteger(value, format, new Span<char>(storage, 1, length - 1), out var shortWritten, provider);
			SetStringBuilderAllocationFailure(0);
			if (shortSuccess || shortWritten != 0) return 1;
			for (var index = 0; index < storage.Length; index++) if (storage[index] != '#') return 2;
			SetStringBuilderAllocationFailure(1);
			var success = TryStandardInteger(value, format, new Span<char>(storage, 1, length), out var written, provider);
			SetStringBuilderAllocationFailure(0);
			if (!success || written != length || !CheckScientificText(new ReadOnlySpan<char>(storage, 1, written), expected, custom)) return 3;
			var formatText = new string(format);
			SetStringBuilderAllocationFailure(2);
			var text = FormatStandardInteger(value, formatText, provider);
			SetStringBuilderAllocationFailure(0);
			if (!CheckScientificText(text.AsSpan(), expected, custom) || providerCase == 2 && supplied.NumberQueries != 3) return 4;
		}
		var guard = new char[1]; guard[0] = '#';
		SetStringBuilderAllocationFailure(1);
		var hugeSuccess = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(long.MinValue, "E999999999", null, guard, out var hugeWritten);
		SetStringBuilderAllocationFailure(0);
		if (hugeSuccess || hugeWritten != 0 || guard[0] != '#') return 5;
		for (var signCase = 0; signCase < 3; signCase++)
		for (var formatCase = 0; formatCase < 4; formatCase++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var info = signCase == 1 ? ScientificInfo() : new System.Globalization.NumberFormatInfo
				{ NegativeSign = signCase == 0 ? "-" : "", PositiveSign = signCase == 0 ? "+" : "", NumberDecimalSeparator = signCase == 0 ? "." : "::" };
			var provider = new CoreLibDecimalSignProvider { Info = info };
			var builder = new System.Text.StringBuilder(8).Append("seed"); object value = long.MinValue;
			var format = formatCase == 0 ? "pre{0:E65}post" : formatCase == 1 ? "pre{0:G3}post" : formatCase == 2 ? "pre{0:R}post" : "pre{0:r3}post";
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(provider, format, value); SetStringBuilderAllocationFailure(0); return 6; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre" || provider.NumberQueries != failAt || provider.CustomQueries != 1) return 7;
			builder.Clear().AppendFormat(provider, "{0:E0}", value);
			var text = builder.ToString(0, builder.Length);
			if (signCase != 2 && !CheckScientificText(text.AsSpan(), "-9E+018", signCase == 1) || signCase == 2 && text != "9E018") return 8;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderFixedPointIntegersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var info = FixedPointInfo(); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			info = null!; M68kRuntime.Collect();
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			for (var capacityCase = 0; capacityCase < 2; capacityCase++)
			{
				var format = formatCase == 0 ? "{0:F}" : formatCase == 1 ? "{0:F0}" : formatCase == 2 ? "{0:f3}" : "{0:F65}";
				var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : 128);
				supplied.NumberQueries = 0; supplied.CustomQueries = 0;
				if (builder.AppendFormat(provider, format, StandardIntegerValue(width * 4)) != builder) return 1;
				var snapshot = builder.ToString(0, builder.Length);
				if (!CheckFixedPoint(snapshot.AsSpan(), width, providerCase != 0, FixedPointPrecision(formatCase, providerCase != 0))) return 2;
				if (providerCase == 2 && (supplied.NumberQueries != (capacityCase == 0 ? 2 : 1) || supplied.CustomQueries != 1)) return 3;
				builder.Clear().Append("changed"); M68kRuntime.Collect();
				if (!CheckFixedPoint(snapshot.AsSpan(), width, providerCase != 0, FixedPointPrecision(formatCase, providerCase != 0))) return 4;
			}
		}
		return 42;
	}

	private static System.Globalization.NumberFormatInfo FixedPointInfo() => new()
	{
		NegativeSign = new string("\u2212\0\u03A9".AsSpan()),
		NumberDecimalSeparator = new string("\u03A9\0\uD83D\uDE00".AsSpan()), NumberDecimalDigits = 4,
		NumberNegativePattern = 0
	};

	private static string FixedPointFormat(int scenario) => scenario == 0 ? "F" : scenario == 1 ? "F0" : scenario == 2 ? "f3" : "F65";
	private static int FixedPointPrecision(int scenario, bool custom) => scenario == 0 ? custom ? 4 : 2 : scenario == 1 ? 0 : scenario == 2 ? 3 : 65;
	private static string FixedPointMagnitude(int width) => width switch
	{
		0 => "128", 1 => "255", 2 => "32768", 3 => "65535", 4 => "2147483648", 5 => "4294967295",
		6 => "9223372036854775808", _ => "18446744073709551615"
	};

	private static bool CheckFixedPoint(ReadOnlySpan<char> text, int width, bool custom, int precision)
	{
		var sign = (width & 1) != 0 ? string.Empty : custom ? "\u2212\0\u03A9" : "-";
		var separator = precision == 0 ? string.Empty : custom ? "\u03A9\0\uD83D\uDE00" : ".";
		var digits = FixedPointMagnitude(width);
		if (text.Length != sign.Length + digits.Length + separator.Length + precision) return false;
		for (var index = 0; index < text.Length; index++)
		{
			var offset = index - sign.Length;
			var expected = index < sign.Length ? sign[index] : offset < digits.Length ? digits[offset]
				: offset < digits.Length + separator.Length ? separator[offset - digits.Length] : '0';
			if (text[index] != expected) return false;
		}
		return true;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibFixedPointIntegerSpanHelpersEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		{
			var info = FixedPointInfo(); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			info = null!;
			for (var width = 0; width < 8; width++)
			for (var formatCase = 0; formatCase < 4; formatCase++)
			{
				var custom = providerCase != 0; var precision = FixedPointPrecision(formatCase, custom);
				var length = FixedPointMagnitude(width).Length + ((width & 1) != 0 ? 0 : custom ? 3 : 1)
					+ (precision == 0 ? 0 : custom ? 4 : 1) + precision;
				var value = StandardIntegerValue(width * 4);
				for (var sizeCase = 0; sizeCase < 4; sizeCase++)
				{
					var size = sizeCase == 0 ? 0 : length + sizeCase - 2;
					var storage = new char[size + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
					var before = supplied.NumberQueries; M68kRuntime.Collect();
					var success = TryStandardInteger(value, FixedPointFormat(formatCase).AsSpan(), new Span<char>(storage, 1, size), out var written, provider);
					M68kRuntime.Collect();
					if (success != (size >= length) || written != (success ? length : 0)) return 1;
					if (providerCase == 2 && (supplied.NumberQueries != before + 1 || supplied.CustomQueries != 0)) return 2;
					if (success && !CheckFixedPoint(new ReadOnlySpan<char>(storage, 1, written), width, custom, precision)) return 3;
					for (var index = 0; index < storage.Length; index++)
						if ((!success || index == 0 || index > written) && storage[index] != '#') return 4;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibFixedPointProviderContractsEntry()
	{
		var info = new System.Globalization.NumberFormatInfo { NegativeSign = "", NumberDecimalSeparator = "::", NumberDecimalDigits = 0 };
		var builder = new System.Text.StringBuilder(80).AppendFormat(info, "[{0,8:F}][{1,-8:f3}]", -12, 12u);
		if (builder.ToString(0, builder.Length) != "[      12][12::000 ]") return 1;
		var snapshot = builder.ToString(0, builder.Length);
		info.NegativeSign = "minus"; info.NumberDecimalDigits = 3; info.NumberDecimalSeparator = ","; M68kRuntime.Collect();
		builder.Clear().AppendFormat(info, "{0:F}|{1:F\0ignored}|{2:f000000000000000003}", -12, -12, 12u);
		if (builder.ToString(0, builder.Length) != "minus12,000|minus12|12,000" || snapshot != "[      12][12::000 ]") return 2;
		for (var mode = 1; mode <= 2; mode++)
		{
			var provider = new CoreLibDecimalSignProvider { Mode = mode };
			builder.Clear().AppendFormat(provider, "{0:F}/{1:F0}/{2:f3}", -12, 0u, ulong.MaxValue);
			if (builder.ToString(0, builder.Length) != "-12.00/0/18446744073709551615.000" || provider.NumberQueries != 3 || provider.CustomQueries != 1) return 3;
		}
		var changing = new CoreLibDecimalSignProvider { Mode = 5, Info = new System.Globalization.NumberFormatInfo() };
		builder = new System.Text.StringBuilder(1).AppendFormat(changing, "{0:F}", -12);
		if (builder.ToString(0, builder.Length) != "second12::000" || changing.NumberQueries != 2 || changing.CustomQueries != 1) return 4;
		var throwing = new CoreLibDecimalSignProvider { Mode = 3, Error = new InvalidOperationException("provider") };
		builder = new System.Text.StringBuilder(80).Append("seed");
		try { builder.AppendFormat(throwing, "pre{0:F0}post", 0u); return 5; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || builder.ToString(0, builder.Length) != "seedpre") return 6; }
		throwing.NumberQueries = 0; builder.Clear().Append("seed");
		try { builder.AppendFormat(throwing, "pre{0:F1000000000}post", -12); return 7; } catch (FormatException) { }
		if (throwing.NumberQueries != 0 || builder.ToString(0, builder.Length) != "seedpre") return 8;
		var guard = new char[2]; guard[0] = '#'; guard[1] = '#'; var written = 37;
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-12, -1, "F0", throwing, guard, out written); return 19; }
		catch (InvalidOperationException error) { if (!ReferenceEquals(error, throwing.Error) || written != 37 || guard[0] != '#' || guard[1] != '#') return 20; }
		info.NumberDecimalDigits = 99;
		var output = CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(0, "F", info);
		if (output.Length != 101 || output[0] != '0' || output[1] != ',') return 9;
		for (var index = 2; index < output.Length; index++) if (output[index] != '0') return 9;
		info.NumberDecimalDigits = 4;
		try { info.NumberDecimalDigits = -1; return 10; } catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 11; }
		try { info.NumberDecimalDigits = 100; return 12; } catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 13; }
		try { info.NumberDecimalSeparator = null!; return 14; } catch (ArgumentNullException error) { if (error.ParamName != "value") return 15; }
		try { info.NumberDecimalSeparator = string.Empty; return 16; } catch (ArgumentException error) { if (error.ParamName != "value") return 17; }
		return info.NumberDecimalDigits == 4 && info.NumberDecimalSeparator == "," ? 42 : 18;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibFixedPointIntegerAllocationContractsEntry()
	{
		for (var providerCase = 0; providerCase < 3; providerCase++)
		for (var width = 0; width < 8; width++)
		{
			var info = FixedPointInfo(); var supplied = new CoreLibDecimalSignProvider { Info = info };
			IFormatProvider? provider = providerCase == 0 ? null : providerCase == 1 ? info : supplied;
			var custom = providerCase != 0;
			var length = FixedPointMagnitude(width).Length + ((width & 1) != 0 ? 0 : custom ? 3 : 1) + (custom ? 4 : 1) + 4;
			var storage = new char[length + 2]; for (var index = 0; index < storage.Length; index++) storage[index] = '#';
			var value = StandardIntegerValue(width * 4);
			SetStringBuilderAllocationFailure(1);
			var shortSuccess = TryStandardInteger(value, "F4", new Span<char>(storage, 1, length - 1), out var shortWritten, provider);
			SetStringBuilderAllocationFailure(0);
			if (shortSuccess || shortWritten != 0) return 1;
			for (var index = 0; index < storage.Length; index++) if (storage[index] != '#') return 2;
			SetStringBuilderAllocationFailure(1);
			var success = TryStandardInteger(value, "F4", new Span<char>(storage, 1, length), out var written, provider);
			SetStringBuilderAllocationFailure(0);
			if (!success || written != length || !CheckFixedPoint(new ReadOnlySpan<char>(storage, 1, written), width, custom, 4)) return 3;
			SetStringBuilderAllocationFailure(2);
			var text = FormatStandardInteger(value, "F4", provider);
			SetStringBuilderAllocationFailure(0);
			if (!CheckFixedPoint(text.AsSpan(), width, custom, 4) || providerCase == 2 && supplied.NumberQueries != 3) return 4;
		}
		var guard = new char[1]; guard[0] = '#';
		SetStringBuilderAllocationFailure(1);
		var hugeSuccess = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(long.MinValue, "F999999999", null, guard, out var hugeWritten);
		SetStringBuilderAllocationFailure(0);
		if (hugeSuccess || hugeWritten != 0 || guard[0] != '#') return 5;
		for (var signCase = 0; signCase < 3; signCase++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var info = signCase == 1 ? FixedPointInfo() : new System.Globalization.NumberFormatInfo
				{ NegativeSign = signCase == 0 ? "-" : "", NumberDecimalSeparator = signCase == 0 ? "." : "::" };
			var provider = new CoreLibDecimalSignProvider { Info = info };
			var builder = new System.Text.StringBuilder(80).Append("seed"); object value = long.MinValue;
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(provider, "pre{0:F100}post", value); SetStringBuilderAllocationFailure(0); return 6; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre" || provider.NumberQueries != failAt || provider.CustomQueries != 1) return 7;
			builder.Clear().AppendFormat(provider, "{0:F4}", value);
			var text = builder.ToString(0, builder.Length); var digits = FixedPointMagnitude(6);
			var sign = info.NegativeSign; var separator = info.NumberDecimalSeparator;
			if (text.Length != sign.Length + digits.Length + separator.Length + 4) return 8;
			for (var index = 0; index < text.Length; index++)
			{
				var offset = index - sign.Length;
				var expected = index < sign.Length ? sign[index] : offset < digits.Length ? digits[offset]
					: offset < digits.Length + separator.Length ? separator[offset - digits.Length] : '0';
				if (text[index] != expected) return 9;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderStandardIntegerFormatsEntry()
	{
		for (var scenario = 0; scenario < 32; scenario++)
		for (var capacityCase = 0; capacityCase < 3; capacityCase++)
		{
			var builder = new System.Text.StringBuilder(capacityCase == 0 ? 1 : capacityCase == 1 ? 7 : 80);
			var value = StandardIntegerValue(scenario);
			var format = StandardIntegerCompositeFormat(scenario);
			var expected = StandardIntegerText(scenario);
			builder.AppendFormat(format, value);
			var snapshot = builder.ToString(0, builder.Length);
			M68kRuntime.Collect();
			if (snapshot != expected) return 1;
			builder.Clear().Append("pre").AppendFormat(format, value).Append("post");
			M68kRuntime.Collect();
			var text = builder.ToString(0, builder.Length);
			if (text.Length != expected.Length + 7 || snapshot != expected) return 2;
			for (var index = 0; index < text.Length; index++)
				if (text[index] != (index < 3 ? "pre"[index] : index < expected.Length + 3 ? expected[index - 3] : "post"[index - expected.Length - 3])) return 2;
		}
		var aligned = new System.Text.StringBuilder(80).AppendFormat("[{0,8:X4}][{1,-8:D4}]", (sbyte)-1, (short)-12);
		if (aligned.ToString(0, aligned.Length) != "[    00FF][-0012   ]") return 3;
		aligned.Clear().AppendFormat(new CoreLibPrimitiveFormatProvider(), "{0:X4}/{1:B10}", (sbyte)-1, (short)-1);
		if (aligned.ToString(0, aligned.Length) != "00FF/1111111111111111") return 4;
		aligned.Clear().Append("seed");
		try { aligned.AppendFormat("pre{0:D1000000000}post", 42); return 5; } catch (FormatException) { }
		return aligned.ToString(0, aligned.Length) == "seedpre" ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStandardIntegerFormattingHelpersEntry()
	{
		for (var scenario = 0; scenario < 32; scenario++)
		{
			var value = StandardIntegerValue(scenario);
			var format = StandardIntegerCompositeFormat(scenario);
			var specifier = format.AsSpan(3, format.Length - 4);
			var expected = StandardIntegerText(scenario);
			for (var capacityCase = 0; capacityCase < 4; capacityCase++)
			{
				var size = capacityCase == 0 ? 0 : expected.Length + capacityCase - 2;
				var storage = new char[size + 2];
				for (var index = 0; index < storage.Length; index++) storage[index] = '#';
				M68kRuntime.Collect();
				var success = TryStandardInteger(value, specifier, new Span<char>(storage, 1, size), out var written);
				M68kRuntime.Collect();
				if (success != (size >= expected.Length) || written != (success ? expected.Length : 0)) return 1;
				for (var index = 0; index < storage.Length; index++)
					if (storage[index] != (success && index > 0 && index <= written ? expected[index - 1] : '#')) return 2;
			}
		}
		var buffer = new char[1]; buffer[0] = '#';
		if (CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(-1, -1, "D999999999", null, buffer, out var count) || count != 0 || buffer[0] != '#') return 3;
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(42, -1, "D1000000000", null, buffer, out _); return 4; } catch (FormatException) { }
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64(42, "Z", null, buffer, out _); return 5; } catch (FormatException) { }
		return buffer[0] == '#' ? 42 : 6;
	}

	private static bool TryStandardInteger(object value, ReadOnlySpan<char> format, Span<char> destination, out int written, IFormatProvider? provider = null)
	{
		if (value is sbyte signedByte) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(signedByte, 255, format, provider, destination, out written);
		if (value is byte unsignedByte) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(unsignedByte, format, provider, destination, out written);
		if (value is short signedShort) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(signedShort, 65535, format, provider, destination, out written);
		if (value is ushort unsignedShort) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(unsignedShort, format, provider, destination, out written);
		if (value is int signed) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt32(signed, -1, format, provider, destination, out written);
		if (value is uint unsigned) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(unsigned, format, provider, destination, out written);
		if (value is long signedLong) return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(signedLong, format, provider, destination, out written);
		return CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64((ulong)value, format, provider, destination, out written);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStandardIntegerFormattingAllocationContractsEntry()
	{
		// Initialize the ambient culture cache before measuring individual formatting calls.
		_ = System.Globalization.NumberFormatInfo.CurrentInfo.NegativeSign;
		for (var scenario = 0; scenario < 32; scenario++)
		{
			var value = StandardIntegerValue(scenario);
			var format = StandardIntegerCompositeFormat(scenario);
			var expected = StandardIntegerText(scenario);
			var buffer = new char[expected.Length];
			var specifier = format.AsSpan(3, format.Length - 4);
			var formatText = new string(specifier);
			SetStringBuilderAllocationFailure(1);
			var success = TryStandardInteger(value, specifier, buffer, out var written);
			SetStringBuilderAllocationFailure(0);
			if (!success || written != expected.Length) return 1;
			for (var index = 0; index < written; index++) if (buffer[index] != expected[index]) return 2;
			SetStringBuilderAllocationFailure(2);
			var text = FormatStandardInteger(value, formatText);
			SetStringBuilderAllocationFailure(0);
			if (text != expected) return 6;
		}
		for (var scenario = 0; scenario < 3; scenario++)
		{
			var format = scenario == 0 ? "D3" : scenario == 1 ? "X3" : "B3";
			var buffer = new char[3];
			SetStringBuilderAllocationFailure(1);
			var success = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(0, format, null, buffer, out var written);
			SetStringBuilderAllocationFailure(0);
			if (!success || written != 3 || buffer[0] != '0' || buffer[1] != '0' || buffer[2] != '0') return 7;
			SetStringBuilderAllocationFailure(2);
			var text = CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(0, format, null);
			SetStringBuilderAllocationFailure(0);
			if (text != "000") return 8;
		}
		for (var scenario = 0; scenario < 3; scenario++)
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var builder = new System.Text.StringBuilder(80).Append("seed");
			object value = scenario == 0 ? (object)long.MinValue : ulong.MaxValue;
			var format = scenario == 0 ? "pre{0:D100}post" : scenario == 1 ? "pre{0:X100}post" : "pre{0:B100}post";
			SetStringBuilderAllocationFailure(failAt);
			try { builder.AppendFormat(format, value); SetStringBuilderAllocationFailure(0); return 3; }
			catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
			if (builder.ToString(0, builder.Length) != "seedpre") return 4;
			builder.Clear().Append("seed").AppendFormat("pre{0:D6}post", -12);
			if (builder.ToString(0, builder.Length) != "seedpre-000012post") return 5;
		}
		return 42;
	}

	private static string FormatStandardInteger(object value, string format, IFormatProvider? provider = null)
	{
		if (value is sbyte signedByte) return CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(signedByte, 255, format, provider);
		if (value is byte unsignedByte) return CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(unsignedByte, format, provider);
		if (value is short signedShort) return CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(signedShort, 65535, format, provider);
		if (value is ushort unsignedShort) return CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(unsignedShort, format, provider);
		if (value is int signed) return CopperSharp.Runtime.ShadowNumberFormatting.FormatInt32(signed, -1, format, provider);
		if (value is uint unsigned) return CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(unsigned, format, provider);
		if (value is long signedLong) return CopperSharp.Runtime.ShadowNumberFormatting.FormatInt64(signedLong, format, provider);
		return CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt64((ulong)value, format, provider);
	}

	private static object StandardIntegerValue(int scenario) => (scenario / 4) switch
	{
		0 => sbyte.MinValue, 1 => byte.MaxValue, 2 => short.MinValue, 3 => ushort.MaxValue,
		4 => int.MinValue, 5 => uint.MaxValue, 6 => long.MinValue, _ => ulong.MaxValue
	};

	private static string StandardIntegerCompositeFormat(int scenario)
	{
		var group = scenario / 4;
		return (scenario % 4) switch
		{
			0 => group < 4 ? "{0:D6}" : group < 6 ? "{0:D12}" : "{0:D22}",
			1 => group < 2 ? "{0:X4}" : group < 4 ? "{0:X6}" : group < 6 ? "{0:X10}" : "{0:X18}",
			2 => "{0:x1}",
			_ => group < 2 ? "{0:B10}" : group < 4 ? "{0:B18}" : group < 6 ? "{0:B34}" : "{0:B66}"
		};
	}

	private static string StandardIntegerText(int scenario) => scenario switch
	{
		0 => "-000128", 1 => "0080", 2 => "80", 3 => "0010000000",
		4 => "000255", 5 => "00FF", 6 => "ff", 7 => "0011111111",
		8 => "-032768", 9 => "008000", 10 => "8000", 11 => "001000000000000000",
		12 => "065535", 13 => "00FFFF", 14 => "ffff", 15 => "001111111111111111",
		16 => "-002147483648", 17 => "0080000000", 18 => "80000000", 19 => "0010000000000000000000000000000000",
		20 => "004294967295", 21 => "00FFFFFFFF", 22 => "ffffffff", 23 => "0011111111111111111111111111111111",
		24 => "-0009223372036854775808", 25 => "008000000000000000", 26 => "8000000000000000", 27 => "001000000000000000000000000000000000000000000000000000000000000000",
		28 => "0018446744073709551615", 29 => "00FFFFFFFFFFFFFFFF", 30 => "ffffffffffffffff", _ => "001111111111111111111111111111111111111111111111111111111111111111"
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibSpanStringConstructionEntry()
	{
		for (var length = 0; length <= 65; length++)
		for (var offset = 0; offset <= 3; offset++)
		{
			var array = new char[length + offset + 2];
			for (var index = 0; index < array.Length; index++) array[index] = "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[index % 7];
			ReadOnlySpan<char> value = new ReadOnlySpan<char>(array, offset, length);
			array = null!;
			M68kRuntime.Collect();
			var text = new string(value);
			value = default;
			M68kRuntime.Collect(); var pressure = new char[length + offset + 2]; pressure[0] = 'z';
			if (text.Length != length || length == 0 && !ReferenceEquals(text, string.Empty)) return 1;
			for (var index = 0; index < length; index++) if (text[index] != "A\0\u03A9\uD83D\uDE00\uD800\uFFFF"[(index + offset) % 7]) return 2;
		}
		Span<char> stack = stackalloc char[3]; stack[0] = 'x'; stack[1] = '\u03A9'; stack[2] = '\0';
		var snapshot = new string(stack); stack[0] = 'y'; M68kRuntime.Collect();
		return snapshot == "x\u03A9\0" && ReferenceEquals(new string(default(ReadOnlySpan<char>)), string.Empty) ? 42 : 3;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibInt32ArrayCopyEntry()
	{
		for (var same = 0; same < 2; same++)
		for (var sourceIndex = 0; sourceIndex <= 8; sourceIndex++)
		for (var destinationIndex = 0; destinationIndex <= 8; destinationIndex++)
		for (var length = 0; length <= 8 - sourceIndex && length <= 8 - destinationIndex; length++)
		{
			var source = new int[8]; var destination = same == 0 ? new int[8] : source;
			for (var index = 0; index < 8; index++) { source[index] = Int32CopyPattern(index); if (same == 0) destination[index] = 123; }
			M68kRuntime.Collect();
			Array.Copy(source, sourceIndex, destination, destinationIndex, length);
			M68kRuntime.Collect();
			for (var index = 0; index < 8; index++)
			{
				var expected = index >= destinationIndex && index < destinationIndex + length ? Int32CopyPattern(index - destinationIndex + sourceIndex)
					: same == 0 ? 123 : Int32CopyPattern(index);
				if (destination[index] != expected || same == 0 && source[index] != Int32CopyPattern(index)) return 1;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibInt32ArrayCopyContractEntry()
	{
		var source = new int[3]; source[0] = 12; source[1] = 34; source[2] = 56;
		var destination = new int[3]; destination[0] = 78; destination[1] = 90; destination[2] = -1;
		for (var scenario = 0; scenario < 12; scenario++)
		{
			try
			{
				if (scenario == 0) Array.Copy(null!, -1, destination, -1, -1);
				else if (scenario == 1) Array.Copy(source, -1, null!, -1, -1);
				else Array.Copy(source, scenario == 3 || scenario == 5 ? -1 : scenario == 6 ? 4 : scenario == 8 ? 3 : scenario == 10 ? int.MaxValue : 0,
					destination, scenario == 4 || scenario == 5 ? -1 : scenario == 7 ? 4 : scenario == 9 ? 3 : scenario == 11 ? int.MaxValue : 0,
					scenario == 2 ? -1 : scenario == 6 || scenario == 7 ? 0 : 1);
				return 1;
			}
			catch (ArgumentNullException error) { if (scenario > 1 || error.ParamName != (scenario == 0 ? "sourceArray" : "destinationArray")) return 2; }
			catch (ArgumentOutOfRangeException error) { if (scenario < 2 || scenario > 5 || error.ParamName != (scenario == 2 ? "length" : scenario == 4 ? "destinationIndex" : "sourceIndex")) return 3; }
			catch (ArgumentException error) { if (scenario < 6 || error.ParamName != (scenario == 6 || scenario == 8 || scenario == 10 ? "sourceArray" : "destinationArray")) return 4; }
			if (source[0] != 12 || source[1] != 34 || source[2] != 56 || destination[0] != 78 || destination[1] != 90 || destination[2] != -1) return 5;
		}
		Array.Copy(source, 3, destination, 3, 0);
		Array.Copy(source, 1, destination, 0, 2);
		return destination[0] == 34 && destination[1] == 56 && destination[2] == -1 ? 42 : 6;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibInt32ArrayCopyRejectsUnknownEntry()
	{
		var values = new int[3]; values[0] = 12; values[1] = 34; values[2] = 56;
		for (var scenario = 0; scenario < 3; scenario++)
		{
			Array unsupported = scenario == 0 ? new byte[3] : scenario == 1 ? new long[3] : new object[3];
			try { Array.Copy(unsupported, 0, values, 0, 0); return 1; } catch (NotSupportedException) { }
			try { Array.Copy(values, 0, unsupported, 0, 1); return 2; } catch (NotSupportedException) { }
			if (values[0] != 12 || values[1] != 34 || values[2] != 56) return 3;
		}
		Array.Copy(values, 0, values, 1, 2);
		return values[0] == 12 && values[1] == 12 && values[2] == 34 ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe int CoreLibMemoryZeroEntry()
	{
		CopperSharp.Runtime.ShadowBuffer.ZeroMemoryInternal(null, 0);
		for (var offset = 0; offset < 4; offset++)
		for (var length = 0; length <= 65; length++)
		{
			var bytes = new byte[72]; var integers = new int[72]; var raw = new byte[72]; var references = new object?[72]; var characters = new char[72];
			for (var index = 0; index < 72; index++) { bytes[index] = 0xA5; raw[index] = 0x5A; integers[index] = Int32CopyPattern(index); references[index] = "keep"; characters[index] = '\uFFFF'; }
			M68kRuntime.Collect();
			Array.Clear(bytes, offset, length); Array.Clear(integers, offset, length); Array.Clear(references, offset, length); Array.Clear(characters, offset, length);
			fixed (byte* pointer = raw) CopperSharp.Runtime.ShadowBuffer.ZeroMemoryInternal(pointer + offset, (nuint)length);
			M68kRuntime.Collect();
			for (var index = 0; index < 72; index++)
			{
				var cleared = index >= offset && index < offset + length;
				if (bytes[index] != (cleared ? 0 : 0xA5) || raw[index] != (cleared ? 0 : 0x5A) || integers[index] != (cleared ? 0 : Int32CopyPattern(index))) return 1;
				if (references[index] != (cleared ? null : (object)"keep")) return 2;
				if (characters[index] != (cleared ? 0 : 0xFFFF)) return 3;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe int CoreLibRawZeroSmokeEntry()
	{
		var bytes = new byte[8]; for (var index = 0; index < 8; index++) bytes[index] = 0x55;
		fixed (byte* pointer = bytes) CopperSharp.Runtime.ShadowBuffer.ZeroMemoryInternal(pointer + 1, 3);
		for (var index = 0; index < 8; index++) if (bytes[index] != (index >= 1 && index < 4 ? 0 : 0x55)) return 1;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibMemoryZeroContractEntry()
	{
		var values = new int[3]; values[0] = 12; values[1] = 34; values[2] = 56;
		try { Array.Clear(null!, -1, -1); return 1; } catch (ArgumentNullException error) { if (error.ParamName != "array") return 2; }
		for (var scenario = 0; scenario < 6; scenario++)
		{
			try { Array.Clear(values, scenario == 0 || scenario == 4 ? -1 : scenario == 2 ? 4 : scenario == 3 ? 3 : 0,
				scenario == 1 || scenario == 4 ? -1 : scenario == 0 || scenario == 2 ? 0 : scenario == 5 ? int.MaxValue : 1); return 3; }
			catch (IndexOutOfRangeException) { }
			if (values[0] != 12 || values[1] != 34 || values[2] != 56) return 4;
		}
		Array.Clear(values, 3, 0); Array.Clear(values, 1, 1);
		Array.Clear(new byte[0], 0, 0); Array.Clear(new char[0], 0, 0); Array.Clear(new int[0], 0, 0); Array.Clear(new object[0], 0, 0);
		return values[0] == 12 && values[1] == 0 && values[2] == 56 ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibMemoryClearRejectsUnknownEntry()
	{
		for (var scenario = 0; scenario < 3; scenario++)
		{
			Array values = scenario == 0 ? new uint[3] : scenario == 1 ? new short[3] : new long[3];
			try { Array.Clear(values, 0, 0); return 1; } catch (NotSupportedException) { }
		}
		var retained = new string?[3]; retained[0] = "first"; retained[1] = "cleared"; retained[2] = "last";
		Array.Clear(retained, 1, 1);
		return retained[0] == "first" && retained[1] == null && retained[2] == "last" ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe int CoreLibMemoryAlgorithmsAllocationFreeEntry()
	{
		var values = new int[8]; var bytes = new byte[72];
		for (var index = 0; index < 8; index++) values[index] = Int32CopyPattern(index);
		for (var index = 0; index < 72; index++) bytes[index] = 0xA5;
		SetStringBuilderAllocationFailure(1);
		Array.Copy(values, 0, values, 1, 7);
		Array.Clear(values, 3, 3); Array.Clear(bytes, 1, 65);
		fixed (byte* pointer = bytes) CopperSharp.Runtime.ShadowBuffer.ZeroMemoryInternal(pointer + 66, 3);
		SetStringBuilderAllocationFailure(0);
		for (var index = 0; index < 8; index++)
			if (values[index] != (index >= 3 && index < 6 ? 0 : Int32CopyPattern(index == 0 ? 0 : index - 1))) return 1;
		for (var index = 0; index < 72; index++) if (bytes[index] != (index >= 1 && index < 69 ? 0 : 0xA5)) return 2;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibMemoryReferenceOffsetsRetainOwnersEntry()
	{
		int[]? integers = new int[8]; integers[4] = 123; integers[7] = int.MaxValue;
		ref int first = ref System.Runtime.CompilerServices.Unsafe.Add(ref integers[4], -4);
		ref int last = ref System.Runtime.CompilerServices.Unsafe.Add(ref first, 7);
		integers = null;
		M68kRuntime.Collect(); var pressure = new int[8]; pressure[7] = -1;
		if (last != int.MaxValue) return 1;
		first = int.MinValue;
		M68kRuntime.Collect();
		if (first != int.MinValue || System.Runtime.CompilerServices.Unsafe.Add(ref first, 4) != 123) return 2;
		byte[]? bytes = new byte[72]; bytes[0] = 0xA5; bytes[71] = 0x5A;
		ref byte middle = ref System.Runtime.CompilerServices.Unsafe.Add(ref bytes[0], (nuint)35);
		ref byte start = ref System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref middle, (nint)(-35));
		ref byte end = ref System.Runtime.CompilerServices.Unsafe.AddByteOffset(ref start, (nuint)71);
		bytes = null;
		M68kRuntime.Collect(); var other = new byte[72]; other[0] = 12; other[71] = 34;
		if (start != 0xA5 || end != 0x5A) return 3;
		start = 123; end = 234;
		M68kRuntime.Collect();
		if (start != 123 || end != 234 || pressure[7] != -1 || other[0] != 12 || other[71] != 34) return 4;
		nint[]? native = new nint[4]; native[0] = -123; native[3] = 234;
		ref nint nativeEnd = ref System.Runtime.CompilerServices.Unsafe.Add(ref native[3], (nuint)0);
		ref nint nativeStart = ref System.Runtime.CompilerServices.Unsafe.Add(ref nativeEnd, (nint)(-3));
		ref nint nativeLast = ref System.Runtime.CompilerServices.Unsafe.Add(ref nativeStart, 3);
		native = null;
		M68kRuntime.Collect(); var nativePressure = new nint[4]; nativePressure[0] = 345;
		return nativeStart == -123 && nativeLast == 234 && nativePressure[0] == 345 ? 42 : 5;
	}

	private sealed class ReferenceBoxPayload { public int Value; }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinContractEntry()
	{
		var builder = new System.Text.StringBuilder(8, 8).Append("seed");
		try { builder.AppendJoin("|", (string?[])null!); return 1; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 2; }
		try { builder.AppendJoin('\0', (string?[])null!); return 3; }
		catch (ArgumentNullException error) { if (error.ParamName != "values") return 4; }
		if (builder.Length != 4 || builder.Capacity != 8 || builder.ToString(0, 4) != "seed") return 5;
		var values = new string?[] { null, "", "A", null };
		if (builder.AppendJoin((string?)null, values) != builder || builder.ToString(0, builder.Length) != "seedA") return 6;
		builder.Length = 4;
		if (builder.AppendJoin("", values) != builder || builder.ToString(0, builder.Length) != "seedA") return 7;
		builder.Length = 4;
		if (builder.AppendJoin('\0', values) != builder || builder.ToString(0, builder.Length) != "seed\0\0A\0") return 8;
		for (var character = 0; character < 2; character++)
		{
			var limited = new System.Text.StringBuilder(4, 8).Append("seed");
			var parts = new[] { "a", "bc", "def" };
			try
			{
				if (character == 0) limited.AppendJoin("|", parts); else limited.AppendJoin('|', parts);
				return 9;
			}
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "valueCount") return 10; }
			if (limited.Length != 8 || limited.Capacity != 8 || limited.MaxCapacity != 8 || limited.ToString(0, 8) != "seeda|bc") return 11;
			limited.Clear().AppendJoin('|', new[] { "X", "Y" });
			if (limited.ToString(0, limited.Length) != "X|Y") return 12;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendJoinAllocationFailureEntry()
	{
		for (var character = 0; character < 2; character++)
		{
			var available = new System.Text.StringBuilder(512).Append("seed");
			var values = CreateAppendJoinValues(5);
			var empty = new string?[0];
			var separator = "\u03A9\0\uD83D\uDE00\uFFFF";
			SetStringBuilderAllocationFailure(1);
			if (character == 0) available.AppendJoin(separator, values); else available.AppendJoin('\u03A9', values);
			available.AppendJoin((string?)null, empty);
			SetStringBuilderAllocationFailure(0);
			if (!AppendJoinMatches(available, "seed", values, character == 0 ? separator : "\u03A9")) return 1;
			for (var scenario = 0; scenario < (character == 0 ? 4 : 3); scenario++)
			for (var failAt = 1; failAt <= 2; failAt++)
			{
				var capacity = scenario == 0 ? 4 : scenario == 2 ? 6 : 8;
				var builder = new System.Text.StringBuilder(capacity).Append("seed");
				var parts = new[] { "AB", "CD" };
				var delimiter = scenario == 3 ? separator : "|";
				var retained = scenario == 0 ? "seed" : scenario == 1 ? "seedAB|C" : scenario == 2 ? "seedAB" : "seedAB\u03A9\0";
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (character == 0) builder.AppendJoin(delimiter, parts); else builder.AppendJoin('|', parts);
					SetStringBuilderAllocationFailure(0); return 2;
				}
				catch (OutOfMemoryException) { SetStringBuilderAllocationFailure(0); }
				if (builder.Capacity != capacity || builder.Length != retained.Length || builder.ToString(0, builder.Length) != retained) return 3;
				if (parts[0] != "AB" || parts[1] != "CD") return 4;
				builder.Length = 4;
				if (character == 0) builder.AppendJoin(delimiter, parts); else builder.AppendJoin('|', parts);
				if (!AppendJoinMatches(builder, "seed", parts, delimiter)) return 5;
				builder.Clear().AppendJoin('|', new[] { "X", "Y" });
				if (builder.ToString(0, builder.Length) != "X|Y") return 6;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static object? BoxClosedReference<T>(T value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ClosedReferenceBoxIdentityEntry()
	{
		var payload = new ReferenceBoxPayload { Value = 42 };
		var array = new[] { payload };
		SetStringBuilderAllocationFailure(1);
		var first = BoxClosedReference(payload);
		var second = BoxClosedReference(array);
		var third = BoxClosedReference("text");
		var fourth = BoxClosedReference((object)payload);
		var nil = BoxClosedReference((ReferenceBoxPayload?)null);
		SetStringBuilderAllocationFailure(0);
		return first == payload && second == array && third == (object)"text" && fourth == payload && nil == null ? 42 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ClosedReferenceBoxGcEntry()
	{
		ReferenceBoxPayload? payload = new ReferenceBoxPayload { Value = 42 };
		ReferenceBoxPayload[]? array = new[] { payload };
		var first = BoxClosedReference(payload);
		var second = BoxClosedReference(array);
		payload = null; array = null;
		M68kRuntime.Collect();
		var pressure = new ReferenceBoxPayload { Value = 99 };
		M68kRuntime.Collect();
		return ((ReferenceBoxPayload)first!).Value == 42 && ((ReferenceBoxPayload[])second!)[0] == first && pressure.Value == 99 ? 42 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReferenceArrayCountdownEntry() => FillReferenceArrayCountdown(9);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderChunksLifetimeEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var lengthCase = 0; lengthCase < 2; lengthCase++)
		{
			var length = lengthCase == 0 ? 65 : 129;
			System.Text.StringBuilder? builder = CreatePatternStringBuilder(1, length, 0);
			var enumerator = CopyChunkEnumerator(builder.GetChunks());
			builder = null;
			M68kRuntime.Collect();
			var position = 0;
			while (enumerator.MoveNext())
			{
				M68kRuntime.Collect();
				var span = enumerator.Current.Span;
				for (var index = 0; index < span.Length; index++) if (span[index] != pattern[(position + index) % 8]) return 1;
				position += span.Length;
			}
			if (position != length) return 2;
			enumerator = default;
			var retained = CreateRetainedChunkView(length);
			var copied = retained;
			retained = default;
			M68kRuntime.Collect();
			var pressure = new char[512];
			pressure[0] = 'X';
			var view = copied.Span;
			if (view.Length != 16) return 3;
			for (var index = 0; index < view.Length; index++) if (view[index] != pattern[index % 8]) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder.ChunkEnumerator CopyChunkEnumerator(System.Text.StringBuilder.ChunkEnumerator value) => value.GetEnumerator();

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ReadOnlyMemory<char> CreateRetainedChunkView(int length)
	{
		var builder = CreatePatternStringBuilder(1, length, 0);
		var enumerator = builder.GetChunks();
		for (var chunk = 0; chunk < 6; chunk++) enumerator.MoveNext();
		return enumerator.Current;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderChunksContractEntry()
	{
		var empty = new System.Text.StringBuilder(1).GetChunks();
		try { _ = empty.Current; return 1; } catch (InvalidOperationException) { }
		if (!empty.MoveNext() || empty.Current.Length != 0 || empty.MoveNext() || empty.MoveNext()) return 2;
		var uninitialized = default(System.Text.StringBuilder.ChunkEnumerator);
		if (uninitialized.MoveNext()) return 3;
		try { _ = uninitialized.Current; return 4; } catch (InvalidOperationException) { }
		var original = new System.Text.StringBuilder(1).Append("text").GetChunks();
		var copy = original.GetEnumerator();
		if (!original.MoveNext() || !copy.MoveNext() || original.Current.Length != copy.Current.Length) return 5;
		var position = 0;
		while (copy.MoveNext()) position += copy.Current.Length;
		if (position + original.Current.Length != 4 || copy.MoveNext()) return 6;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderChunksAllocationFailureEntry()
	{
		for (var sizeCase = 0; sizeCase < 3; sizeCase++)
		{
			var length = sizeCase == 0 ? 0 : sizeCase == 1 ? 65 : 129;
			var builder = CreatePatternStringBuilder(1, length, 0);
			var capacity = builder.Capacity;
			var before = builder.ToString(0, length);
			for (var failAt = 1; failAt <= 2; failAt++)
			{
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					var enumerator = builder.GetChunks();
					var position = 0;
					while (enumerator.MoveNext()) position += enumerator.Current.Length;
					SetStringBuilderAllocationFailure(0);
					if (sizeCase == 2 || position != length) return 1;
				}
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					if (sizeCase != 2) return 2;
				}
				if (builder.Length != length || builder.Capacity != capacity || builder.ToString(0, length) != before) return 3;
				var retry = builder.GetChunks();
				var total = 0;
				while (retry.MoveNext()) total += retry.Current.Length;
				if (total != length) return 4;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int FillReferenceArrayCountdown(int count)
	{
		var array = new string[count];
		while (0 <= --count) array[count] = "chunk";
		for (var index = 0; index < array.Length; index++) if (array[index] != "chunk") return 1;
		return count == -1 ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendLineEntry()
	{
		var builder = new System.Text.StringBuilder(1);
		if (builder.AppendLine((string?)null) != builder || builder.AppendLine("\u03A9\0\uD83D\uDE00") != builder ||
			builder.AppendLine(string.Empty) != builder || builder.AppendLine() != builder) return 1;
		builder.Append("\r\n").AppendLine("tail");
		M68kRuntime.Collect();
		return builder.ToString() == "\n\u03A9\0\uD83D\uDE00\n\n\n\r\ntail\n" && Environment.NewLine == "\n" ? 42 : 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static unsafe int CoreLibCharacterSpanReferenceEntry()
	{
		char[]? data = new char[3];
		data[0] = '\u03A9'; data[1] = '\uFFFF'; data[2] = 'X';
		var writable = new Span<char>(data);
		var readOnly = new ReadOnlySpan<char>(data).Slice(1);
		ref var first = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(writable);
		ref var second = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(readOnly);
		writable = default; readOnly = default; data = null;
		M68kRuntime.Collect();
		var replacement = new char[3];
		replacement[0] = 'Y'; replacement[1] = 'Z';
		if (first != '\u03A9' || second != '\uFFFF') return 1;
		first = 'A'; second = 'B';
		M68kRuntime.Collect();
		if (first != 'A' || second != 'B') return 2;
		if (new Span<char>((char[]?)null).Length != 0 || new ReadOnlySpan<char>((char[]?)null).Length != 0) return 3;
		fixed (char* empty = &System.Runtime.InteropServices.MemoryMarshal.GetReference(default(Span<char>)))
			if (empty != null) return 4;
		fixed (char* empty = &System.Runtime.InteropServices.MemoryMarshal.GetReference(default(ReadOnlySpan<char>)))
			if (empty != null) return 4;
		Span<char> frame = stackalloc char[1];
		frame[0] = '\u03A9';
		ref var frameData = ref System.Runtime.InteropServices.MemoryMarshal.GetReference(frame);
		M68kRuntime.Collect();
		return frameData == '\u03A9' ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderTextValidationEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		builder.Append("seed");
		var chars = new char[4];
		try { builder.Append("text", -1, 0); return 1; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append("text", 0, -1); return 2; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append("text", 3, 2); return 3; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append("text", int.MaxValue, 1); return 4; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append((string?)null, 1, 0); return 5; } catch (ArgumentNullException) { }
		try { builder.Append(chars, -1, 0); return 6; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append(chars, 0, -1); return 7; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append(chars, 3, 2); return 8; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append(chars, int.MaxValue, 1); return 9; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append((char[]?)null, 0, 1); return 10; } catch (ArgumentNullException) { }
		if (builder.Append("text", 4, 0) != builder || builder.Append(chars, 4, 0) != builder) return 11;
		return builder.ToString() == "seed" ? 42 : 12;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderTextCapacityLimitEntry()
	{
		var builder = new System.Text.StringBuilder(4, 4);
		builder.Append("seed");
		var chars = new char[1];
		chars[0] = 'X';
		try { builder.Append("X"); return 1; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append("XYZ", 1, 1); return 2; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append(chars); return 3; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append(chars, 0, 1); return 4; } catch (ArgumentOutOfRangeException) { }
		try { builder.Append(new ReadOnlySpan<char>(chars)); return 5; } catch (ArgumentOutOfRangeException) { }
		try { builder.AppendLine(); return 6; } catch (ArgumentOutOfRangeException) { }
		return builder.Length == 4 && builder.ToString() == "seed" ? 42 : 7;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderTextAllocationFailureEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 16; index++) builder.Append('X');
		try { builder.Append("growth"); }
		catch (OutOfMemoryException)
		{
			return builder.Length == 16 && builder[0] == 'X' && builder[15] == 'X' ? 42 : 1;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderClearEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		if (builder.Clear() != builder || builder.Length != 0 || builder.Capacity != 4) return 1;
		builder.Append("\u03A9\0\uD83D\uDE00");
		var first = builder.ToString();
		if (builder.Clear() != builder || builder.Length != 0 || builder.Capacity != 4) return 2;
		for (var index = 0; index < 257; index++) builder.Append((char)(index * 251));
		var snapshot = builder.ToString();
		if (builder.Clear() != builder || builder.Length != 0 || builder.MaxCapacity != int.MaxValue || builder.ToString() != string.Empty) return 3;
		M68kRuntime.Collect();
		for (var cycle = 0; cycle < 12; cycle++)
		{
			builder.Append("reuse\u03A9\0\uD83D\uDE00");
			if (builder.Length != 9 || builder[5] != '\u03A9' || builder[8] != '\uDE00') return 4;
			if (builder.Clear() != builder || builder.Clear() != builder || builder.Length != 0) return 5;
			M68kRuntime.Collect();
		}
		builder.Append(42);
		if (builder.ToString() != "42" || first != "\u03A9\0\uD83D\uDE00" || snapshot.Length != 257) return 6;
		for (var index = 0; index < snapshot.Length; index++)
			if (snapshot[index] != (char)(index * 251)) return 7;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderLengthTruncationEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		for (var index = 0; index < 257; index++) builder.Append((char)(index * 251));
		var snapshot = builder.ToString();
		builder.Length = builder.Length;
		builder.Length = 256;
		if (builder.Length != 256 || builder[255] != (char)(255 * 251)) return 1;
		builder.Length = 128;
		builder.Length = 127;
		builder.Length = 17;
		builder.Length = 16;
		builder.Length = 5;
		builder.Length = 4;
		builder.Length = 3;
		M68kRuntime.Collect();
		if (builder.Length != 3 || builder[0] != (char)0 || builder[1] != (char)251 || builder[2] != (char)502) return 2;
		builder.Append("\u03A9\0\uD83D\uDE00");
		if (builder.Length != 7 || builder[3] != '\u03A9' || builder[6] != '\uDE00') return 3;
		builder.Length = 0;
		builder.Append("after");
		if (builder.ToString() != "after" || snapshot.Length != 257) return 4;
		for (var index = 0; index < snapshot.Length; index++)
			if (snapshot[index] != (char)(index * 251)) return 5;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderLengthGrowthEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		builder.Append("seed");
		var snapshot = builder.ToString();
		builder.Length = 257;
		if (builder.Length != 257) return 1;
		for (var index = 0; index < builder.Length; index++)
			if (builder[index] != (index < 4 ? "seed"[index] : '\0')) return 2;
		builder[200] = '\uFFFF';
		builder.Length = 5;
		builder.Length = 300;
		M68kRuntime.Collect();
		for (var index = 4; index < builder.Length; index++)
			if (builder[index] != '\0') return 3;
		builder.Length = 0;
		builder.Length = 17;
		for (var index = 0; index < builder.Length; index++)
			if (builder[index] != '\0') return 4;
		builder.Append("tail");
		return builder.Length == 21 && builder[17] == 't' && builder[20] == 'l' && snapshot == "seed" ? 42 : 5;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderLengthValidationEntry()
	{
		var builder = new System.Text.StringBuilder(4, 32);
		builder.Append("seed");
		try { builder.Length = -1; return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value" || (int)error.ActualValue! != -1) return 2; }
		try { builder.Length = 33; return 3; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 4; }
		try { builder.Length = int.MaxValue; return 5; } catch (ArgumentOutOfRangeException) { }
		if (builder.Length != 4 || builder.ToString() != "seed" || builder.Capacity != 4 || builder.MaxCapacity != 32) return 6;
		builder.Length = 32;
		if (builder.Length != 32 || builder.MaxCapacity != 32) return 7;
		for (var index = 4; index < 32; index++) if (builder[index] != '\0') return 8;
		builder.Clear();
		if (builder.Length != 0 || builder.MaxCapacity != 32) return 9;
		builder.Length = 1;
		return builder[0] == '\0' ? 42 : 10;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibCharacterArrayCopyEntry()
	{
		var source = new char[5];
		source[0] = '\u03A9'; source[1] = '\0'; source[2] = '\uD83D'; source[3] = '\uDE00'; source[4] = '\uFFFF';
		var destination = new char[6];
		for (var index = 0; index < destination.Length; index++) destination[index] = '#';
		Array.Copy(source, destination, 0);
		if (destination[0] != '#' || destination[5] != '#') return 1;
		Array.Copy(source, destination, 5);
		Array.Copy(destination, destination, 5);
		M68kRuntime.Collect();
		for (var index = 0; index < source.Length; index++) if (destination[index] != source[index]) return 2;
		if (destination[5] != '#') return 3;
		try { Array.Copy(null!, destination, 1); return 4; }
		catch (ArgumentNullException error) { if (error.ParamName != "sourceArray") return 5; }
		try { Array.Copy(source, null!, 1); return 6; }
		catch (ArgumentNullException error) { if (error.ParamName != "destinationArray") return 7; }
		try { Array.Copy(source, destination, -1); return 8; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "length") return 9; }
		try { Array.Copy(source, destination, 6); return 10; } catch (ArgumentException) { }
		try { Array.Copy(destination, source, 6); return 11; } catch (ArgumentException) { }
		try { Array.Copy(source, destination, int.MaxValue); return 12; } catch (ArgumentException) { }
		try { Array.Copy(new int[1], destination, 1); return 13; } catch (NotSupportedException) { }
		try { Array.Copy(source, new int[1], 1); return 14; } catch (NotSupportedException) { }
		for (var index = 0; index < source.Length; index++) if (destination[index] != source[index]) return 15;
		return destination[5] == '#' ? 42 : 16;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderLengthAllocationFailureEntry()
	{
		var builder = new System.Text.StringBuilder();
		builder.Append("seed");
		try { builder.Length = 257; }
		catch (OutOfMemoryException)
		{
			if (builder.Length != 16 || builder[0] != 's' || builder[3] != 'd') return 1;
			for (var index = 4; index < builder.Length; index++) if (builder[index] != '\0') return 2;
			builder.Length = 4;
			builder.Append('X');
			return builder.Length == 5 && builder[4] == 'X' ? 42 : 3;
		}
		return 4;
	}

	[M68kImport("fixture.string-builder-allocation-failure")]
	private static extern void SetStringBuilderAllocationFailure([M68kRegister(M68kRegister.D0)] int fail);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderClearAllocationFailureEntry() => CoreLibStringBuilderChunkRebuildFailure(clear: true);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderTruncationAllocationFailureEntry() => CoreLibStringBuilderChunkRebuildFailure(clear: false);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int CoreLibStringBuilderChunkRebuildFailure(bool clear)
	{
		var builder = new System.Text.StringBuilder(4);
		for (var index = 0; index < 65; index++) builder.Append((char)(index * 251));
		var snapshot = builder.ToString();
		var capacity = builder.Capacity;
		SetStringBuilderAllocationFailure(1);
		try
		{
			if (clear) builder.Clear(); else builder.Length = 3;
			SetStringBuilderAllocationFailure(0);
			return 1;
		}
		catch (OutOfMemoryException)
		{
			SetStringBuilderAllocationFailure(0);
			if (builder.Length != 65 || builder.Capacity != capacity) return 2;
			for (var index = 0; index < builder.Length; index++)
				if (builder[index] != (char)(index * 251)) return 3;
			if (clear) builder.Clear(); else builder.Length = 3;
			builder.Append('X');
			if (builder.Length != (clear ? 1 : 4) || builder[builder.Length - 1] != 'X') return 4;
			for (var index = 0; index < snapshot.Length; index++)
				if (snapshot[index] != (char)(index * 251)) return 5;
			return 42;
		}
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertionAllocationFailureEntry()
	{
		for (var failAt = 1; failAt <= 2; failAt++)
		{
			var builder = new System.Text.StringBuilder(4);
			for (var index = 0; index < 65; index++) builder.Append((char)(index * 251));
			var snapshot = builder.ToString();
			var capacity = builder.Capacity;
			SetStringBuilderAllocationFailure(failAt);
			try
			{
				builder.Insert(65, snapshot);
				SetStringBuilderAllocationFailure(0);
				return 1;
			}
			catch (OutOfMemoryException)
			{
				SetStringBuilderAllocationFailure(0);
				if (builder.Length != 65 || builder.Capacity != capacity) return 2;
				for (var index = 0; index < 65; index++)
					if (builder[index] != (char)(index * 251)) return 3;
				builder.Insert(65, snapshot);
				if (builder.Length != 130) return 4;
				for (var index = 0; index < 130; index++)
					if (builder[index] != (char)((index % 65) * 251)) return 5;
				builder.Remove(65, 65);
				for (var index = 0; index < snapshot.Length; index++)
					if (snapshot[index] != (char)(index * 251)) return 6;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderBooleanAppendEntry()
	{
		for (var layout = 0; layout < 2; layout++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			builder.Append('\0').Append('\u03A9');
			var before = builder.ToString();
			for (var scenario = 0; scenario < 12; scenario++)
			{
				var value = scenario % 2 == 0;
				var expected = value ? "True" : "False";
				var start = builder.Length;
				if (builder.Append(value) != builder) return 1;
				M68kRuntime.Collect();
				if (builder.Length != start + expected.Length) return 2;
				for (var index = 0; index < expected.Length; index++)
					if (builder[start + index] != expected[index]) return 3;
			}
			if (before.Length != 2 || before[0] != '\0' || before[1] != '\u03A9') return 4;
			builder.Clear().Append(false).Append(true);
			if (builder.ToString() != "FalseTrue") return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderBooleanInsertionEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		for (var scenario = 0; scenario < 11; scenario++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var position = StringBuilderEditPosition(scenario);
			var value = scenario % 2 == 0;
			var expected = value ? "True" : "False";
			if (builder.Insert(position, value) != builder) return 1;
			M68kRuntime.Collect();
			if (builder.Length != 65 + expected.Length) return 2;
			for (var index = 0; index < builder.Length; index++)
			{
				var character = index < position ? pattern[index % 8]
					: index < position + expected.Length ? expected[index - position]
					: pattern[(index - expected.Length) % 8];
				if (builder[index] != character) return 3;
			}
			builder.Remove(position, expected.Length);
			if (builder.ToString() != before) return 4;
			builder.Clear().Insert(0, value);
			if (builder.ToString() != expected) return 5;
			for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint StringBuilderUnsignedValue(int scenario) => scenario switch
	{
		0 => 0u, 1 => 1u, 2 => 9u, 3 => 10u, 4 => 99u, 5 => 100u,
		6 => 999_999_999u, 7 => 1_000_000_000u, 8 => 2_147_483_647u,
		9 => 2_147_483_648u, _ => uint.MaxValue
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string StringBuilderUnsignedText(int scenario) => scenario switch
	{
		0 => "0", 1 => "1", 2 => "9", 3 => "10", 4 => "99", 5 => "100",
		6 => "999999999", 7 => "1000000000", 8 => "2147483647",
		9 => "2147483648", _ => "4294967295"
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderUnsignedAppendEntry()
	{
		for (var layout = 0; layout < 3; layout++)
		for (var scenario = 0; scenario < 11; scenario++)
		{
			// Leave zero, nine or ten characters available to exercise fallback and direct formatting.
			var builder = new System.Text.StringBuilder(layout == 0 ? 1 : layout == 1 ? 10 : 11);
			builder.Append('\u03A9');
			var before = builder.ToString();
			var expected = StringBuilderUnsignedText(scenario);
			if (builder.Append(StringBuilderUnsignedValue(scenario)) != builder) return 1;
			M68kRuntime.Collect();
			if (builder.Length != expected.Length + 1 || builder[0] != '\u03A9') return 2;
			for (var index = 0; index < expected.Length; index++)
				if (builder[index + 1] != expected[index]) return 3;
			builder.Append('|').Append(uint.MaxValue);
			M68kRuntime.Collect();
			if (builder.ToString(builder.Length - 11, 11) != "|4294967295" || before != "\u03A9") return 4;
			builder.Clear().Append(0u).Append(uint.MaxValue);
			if (builder.ToString() != "04294967295") return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderUnsignedInsertionEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		for (var scenario = 0; scenario < 11; scenario++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var position = StringBuilderEditPosition(scenario);
			var expected = StringBuilderUnsignedText(scenario);
			if (builder.Insert(position, StringBuilderUnsignedValue(scenario)) != builder) return 1;
			M68kRuntime.Collect();
			if (builder.Length != 65 + expected.Length) return 2;
			for (var index = 0; index < builder.Length; index++)
			{
				var character = index < position ? pattern[index % 8]
					: index < position + expected.Length ? expected[index - position]
					: pattern[(index - expected.Length) % 8];
				if (builder[index] != character) return 3;
			}
			builder.Remove(position, expected.Length);
			if (builder.ToString() != before) return 4;
			builder.Clear().Insert(0, StringBuilderUnsignedValue(scenario));
			if (builder.ToString() != expected) return 5;
			for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StringBuilderSmallIntegerValue(int kind, int scenario) => kind switch
	{
		0 => scenario switch { 0 => -128, 1 => -127, 2 => -100, 3 => -10, 4 => -1, 5 => 0, 6 => 1, 7 => 9, 8 => 10, 9 => 99, 10 => 100, _ => 127 },
		1 => scenario switch { 0 => 0, 1 => 1, 2 => 9, 3 => 10, 4 => 99, 5 => 100, 6 => 127, 7 => 128, 8 => 200, 9 => 253, 10 => 254, _ => 255 },
		2 => scenario switch { 0 => -32768, 1 => -32767, 2 => -10000, 3 => -1000, 4 => -100, 5 => -10, 6 => -1, 7 => 0, 8 => 1, 9 => 10000, 10 => 32766, _ => 32767 },
		_ => scenario switch { 0 => 0, 1 => 1, 2 => 9, 3 => 10, 4 => 99, 5 => 100, 6 => 32767, 7 => 32768, 8 => 50000, 9 => 65533, 10 => 65534, _ => 65535 }
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string StringBuilderSmallIntegerText(int kind, int scenario) => kind switch
	{
		0 => scenario switch { 0 => "-128", 1 => "-127", 2 => "-100", 3 => "-10", 4 => "-1", 5 => "0", 6 => "1", 7 => "9", 8 => "10", 9 => "99", 10 => "100", _ => "127" },
		1 => scenario switch { 0 => "0", 1 => "1", 2 => "9", 3 => "10", 4 => "99", 5 => "100", 6 => "127", 7 => "128", 8 => "200", 9 => "253", 10 => "254", _ => "255" },
		2 => scenario switch { 0 => "-32768", 1 => "-32767", 2 => "-10000", 3 => "-1000", 4 => "-100", 5 => "-10", 6 => "-1", 7 => "0", 8 => "1", 9 => "10000", 10 => "32766", _ => "32767" },
		_ => scenario switch { 0 => "0", 1 => "1", 2 => "9", 3 => "10", 4 => "99", 5 => "100", 6 => "32767", 7 => "32768", 8 => "50000", 9 => "65533", 10 => "65534", _ => "65535" }
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder AppendSmallInteger(System.Text.StringBuilder builder, int kind, int value) => kind switch
	{
		0 => builder.Append((sbyte)value), 1 => builder.Append((byte)value),
		2 => builder.Append((short)value), _ => builder.Append((ushort)value)
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static System.Text.StringBuilder InsertSmallInteger(System.Text.StringBuilder builder, int position, int kind, int value) => kind switch
	{
		0 => builder.Insert(position, (sbyte)value), 1 => builder.Insert(position, (byte)value),
		2 => builder.Insert(position, (short)value), _ => builder.Insert(position, (ushort)value)
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSmallIntegerAppendEntry()
	{
		for (var kind = 0; kind < 4; kind++)
		for (var layout = 0; layout < 3; layout++)
		for (var scenario = 0; scenario < 12; scenario++)
		{
			var limitScenario = kind == 0 || kind == 2 ? 0 : 11;
			var limitText = StringBuilderSmallIntegerText(kind, limitScenario);
			// Leave zero, one less than the maximum, or exactly the maximum text length available.
			var builder = new System.Text.StringBuilder(layout == 0 ? 1 : layout == 1 ? limitText.Length : limitText.Length + 1);
			builder.Append('\u03A9');
			var before = builder.ToString();
			var expected = StringBuilderSmallIntegerText(kind, scenario);
			if (AppendSmallInteger(builder, kind, StringBuilderSmallIntegerValue(kind, scenario)) != builder) return 1;
			M68kRuntime.Collect();
			if (builder.Length != expected.Length + 1 || builder[0] != '\u03A9') return 2;
			for (var index = 0; index < expected.Length; index++) if (builder[index + 1] != expected[index]) return 3;
			builder.Append('|');
			AppendSmallInteger(builder, kind, StringBuilderSmallIntegerValue(kind, limitScenario));
			M68kRuntime.Collect();
			if (builder.ToString(builder.Length - limitText.Length, limitText.Length) != limitText || before != "\u03A9") return 4;
			builder.Clear();
			AppendSmallInteger(builder, kind, StringBuilderSmallIntegerValue(kind, 11));
			if (builder.ToString() != StringBuilderSmallIntegerText(kind, 11)) return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSmallIntegerInsertionEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var kind = 0; kind < 4; kind++)
		for (var layout = 0; layout < 2; layout++)
		for (var scenario = 0; scenario < 12; scenario++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var position = StringBuilderEditPosition(scenario % 11);
			var expected = StringBuilderSmallIntegerText(kind, scenario);
			if (InsertSmallInteger(builder, position, kind, StringBuilderSmallIntegerValue(kind, scenario)) != builder) return 1;
			M68kRuntime.Collect();
			if (builder.Length != 65 + expected.Length) return 2;
			for (var index = 0; index < builder.Length; index++)
			{
				var character = index < position ? pattern[index % 8]
					: index < position + expected.Length ? expected[index - position]
					: pattern[(index - expected.Length) % 8];
				if (builder[index] != character) return 3;
			}
			builder.Remove(position, expected.Length);
			if (builder.ToString() != before) return 4;
			builder.Clear();
			InsertSmallInteger(builder, 0, kind, StringBuilderSmallIntegerValue(kind, scenario));
			if (builder.ToString() != expected) return 5;
			for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long StringBuilderInt64Value(int scenario) => scenario switch
	{
		0 => 0L, 1 => -1L, 2 => 9L, 3 => -10L, 4 => 99L, 5 => -100L,
		6 => 2_147_483_648L, 7 => -2_147_483_649L, 8 => 4_294_967_295L,
		9 => 4_294_967_296L, 10 => -4_294_967_296L, 11 => 1_000_000_000_000_000_000L,
		12 => long.MaxValue, _ => long.MinValue
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static ulong StringBuilderUInt64Value(int scenario) => scenario switch
	{
		0 => 0UL, 1 => 1UL, 2 => 9UL, 3 => 10UL, 4 => 99UL, 5 => 100UL,
		6 => 2_147_483_648UL, 7 => 4_294_967_295UL, 8 => 4_294_967_296UL,
		9 => 4_294_967_297UL, 10 => 1_000_000_000_000_000_000UL, 11 => 9_223_372_036_854_775_807UL,
		12 => 9_223_372_036_854_775_808UL, _ => ulong.MaxValue
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string StringBuilder64BitText(bool signed, int scenario) => signed
		? scenario switch
		{
			0 => "0", 1 => "-1", 2 => "9", 3 => "-10", 4 => "99", 5 => "-100",
			6 => "2147483648", 7 => "-2147483649", 8 => "4294967295", 9 => "4294967296",
			10 => "-4294967296", 11 => "1000000000000000000", 12 => "9223372036854775807", _ => "-9223372036854775808"
		}
		: scenario switch
		{
			0 => "0", 1 => "1", 2 => "9", 3 => "10", 4 => "99", 5 => "100",
			6 => "2147483648", 7 => "4294967295", 8 => "4294967296", 9 => "4294967297",
			10 => "1000000000000000000", 11 => "9223372036854775807", 12 => "9223372036854775808", _ => "18446744073709551615"
		};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilder64BitAppendEntry()
	{
		for (var signedCase = 0; signedCase < 2; signedCase++)
		for (var layout = 0; layout < 3; layout++)
		for (var scenario = 0; scenario < 14; scenario++)
		{
			var signed = signedCase == 0;
			// Leave zero, nineteen or twenty characters available in the current chunk.
			var builder = new System.Text.StringBuilder(layout == 0 ? 1 : layout == 1 ? 20 : 21);
			builder.Append('\u03A9');
			var before = builder.ToString();
			var expected = StringBuilder64BitText(signed, scenario);
			var returned = signed ? builder.Append(StringBuilderInt64Value(scenario)) : builder.Append(StringBuilderUInt64Value(scenario));
			M68kRuntime.Collect();
			if (returned != builder || builder.Length != expected.Length + 1 || builder[0] != '\u03A9') return 1;
			for (var index = 0; index < expected.Length; index++) if (builder[index + 1] != expected[index]) return 2;
			builder.Append('|');
			if (signed) builder.Append(long.MinValue); else builder.Append(ulong.MaxValue);
			M68kRuntime.Collect();
			if (builder.ToString(builder.Length - 21, 21) != (signed ? "|-9223372036854775808" : "|18446744073709551615") || before != "\u03A9") return 3;
			builder.Clear();
			if (signed) builder.Append(long.MinValue); else builder.Append(ulong.MaxValue);
			if (builder.ToString() != StringBuilder64BitText(signed, 13)) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilder64BitInsertionEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var signedCase = 0; signedCase < 2; signedCase++)
		for (var layout = 0; layout < 2; layout++)
		for (var scenario = 0; scenario < 14; scenario++)
		{
			var signed = signedCase == 0;
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var position = StringBuilderEditPosition(scenario % 11);
			var expected = StringBuilder64BitText(signed, scenario);
			var returned = signed ? builder.Insert(position, StringBuilderInt64Value(scenario)) : builder.Insert(position, StringBuilderUInt64Value(scenario));
			M68kRuntime.Collect();
			if (returned != builder || builder.Length != 65 + expected.Length) return 1;
			for (var index = 0; index < builder.Length; index++)
			{
				var character = index < position ? pattern[index % 8]
					: index < position + expected.Length ? expected[index - position]
					: pattern[(index - expected.Length) % 8];
				if (builder[index] != character) return 2;
			}
			builder.Remove(position, expected.Length);
			if (builder.ToString() != before) return 3;
			builder.Clear();
			if (signed) builder.Insert(0, StringBuilderInt64Value(scenario)); else builder.Insert(0, StringBuilderUInt64Value(scenario));
			if (builder.ToString() != expected) return 4;
			for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSmallIntegerValidationEntry()
	{
		for (var kind = 0; kind < 4; kind++)
		{
			var limitScenario = kind == 0 || kind == 2 ? 0 : 11;
			var value = StringBuilderSmallIntegerValue(kind, limitScenario);
			var builder = new System.Text.StringBuilder(8, 16);
			builder.Append("seed");
			try { InsertSmallInteger(builder, -1, kind, value); return 1; }
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 2; }
			try { InsertSmallInteger(builder, int.MaxValue, kind, value); return 3; }
			catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 4; }
			if (builder.ToString() != "seed" || builder.Length != 4 || builder.Capacity != 8) return 5;
			var limited = new System.Text.StringBuilder(2, 2);
			limited.Append('X');
			var before = limited.ToString();
			try { AppendSmallInteger(limited, kind, value); return 6; } catch (ArgumentOutOfRangeException) { }
			try { InsertSmallInteger(limited, 1, kind, value); return 7; } catch (OutOfMemoryException) { }
			if (limited.ToString() != "X" || limited.Length != 1 || limited.Capacity != 2 || limited.MaxCapacity != 2) return 8;
			limited.Clear();
			AppendSmallInteger(limited, kind, 0);
			if (limited.ToString() != "0" || before != "X") return 9;
			// Compare the literal fixture oracle with the host's formatting in the host test.
			for (var scenario = 0; scenario < 12; scenario++)
			{
				var actual = new System.Text.StringBuilder();
				AppendSmallInteger(actual, kind, StringBuilderSmallIntegerValue(kind, scenario));
				if (actual.ToString() != StringBuilderSmallIntegerText(kind, scenario)) return 10;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderPrimitiveValidationEntry()
	{
		var builder = new System.Text.StringBuilder(8, 16);
		builder.Append("seed");
		try { builder.Insert(-1, true); return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 2; }
		try { builder.Insert(5, false); return 3; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 4; }
		try { builder.Insert(-1, uint.MaxValue); return 5; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 6; }
		try { builder.Insert(int.MaxValue, 0u); return 7; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 8; }
		try { builder.Insert(-1, long.MinValue); return 15; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 16; }
		try { builder.Insert(5, long.MaxValue); return 17; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 18; }
		try { builder.Insert(-1, ulong.MaxValue); return 19; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 20; }
		try { builder.Insert(int.MaxValue, 0UL); return 21; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 22; }
		if (builder.ToString() != "seed" || builder.Length != 4 || builder.Capacity != 8) return 9;
		var limited = new System.Text.StringBuilder(4, 4);
		limited.Append('X');
		try { limited.Append(true); return 10; } catch (ArgumentOutOfRangeException) { }
		try { limited.Append(uint.MaxValue); return 11; } catch (ArgumentOutOfRangeException) { }
		try { limited.Insert(0, false); return 12; } catch (OutOfMemoryException) { }
		try { limited.Insert(1, uint.MaxValue); return 13; } catch (OutOfMemoryException) { }
		try { limited.Append(long.MinValue); return 23; } catch (ArgumentOutOfRangeException) { }
		try { limited.Append(ulong.MaxValue); return 24; } catch (ArgumentOutOfRangeException) { }
		try { limited.Insert(0, long.MinValue); return 25; } catch (OutOfMemoryException) { }
		try { limited.Insert(1, ulong.MaxValue); return 26; } catch (OutOfMemoryException) { }
		return limited.ToString() == "X" && limited.Length == 1 && limited.Capacity == 4 && limited.MaxCapacity == 4 ? 42 : 14;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibConstrainedIntegerToStringEntry()
	{
		var unsigned = uint.MaxValue;
		var signed = int.MinValue;
		var first = CoreLibConstrainedIntegerText(ref unsigned);
		M68kRuntime.Collect();
		var second = CoreLibConstrainedIntegerText(ref signed);
		M68kRuntime.Collect();
		var signedByte = sbyte.MinValue;
		var unsignedByte = byte.MaxValue;
		var signedShort = short.MinValue;
		var unsignedShort = ushort.MaxValue;
		var third = CoreLibConstrainedIntegerText(ref signedByte);
		M68kRuntime.Collect();
		var fourth = CoreLibConstrainedIntegerText(ref unsignedByte);
		M68kRuntime.Collect();
		var fifth = CoreLibConstrainedIntegerText(ref signedShort);
		M68kRuntime.Collect();
		var sixth = CoreLibConstrainedIntegerText(ref unsignedShort);
		M68kRuntime.Collect();
		return first == "4294967295" && second == "-2147483648" && third == "-128" && fourth == "255" &&
			fifth == "-32768" && sixth == "65535" ? 42 : 1;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static string CoreLibConstrainedIntegerText<T>(ref T value) where T : struct => value.ToString()!;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CoreLibConstrainedBooleanToStringEntry()
	{
		var value = true;
		return CoreLibConstrainedIntegerText(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CoreLibConstrainedCharacterToStringEntry()
	{
		var value = 'A';
		return CoreLibConstrainedIntegerText(ref value);
	}

	private enum CoreLibConstrainedIntegerProbeEnum { Value = 42 }

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static string CoreLibConstrainedEnumToStringEntry()
	{
		var value = CoreLibConstrainedIntegerProbeEnum.Value;
		return CoreLibConstrainedIntegerText(ref value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibUnsignedFormattingHelpersEntry()
	{
		for (var scenario = 0; scenario < 11; scenario++)
		{
			var value = StringBuilderUnsignedValue(scenario);
			var expected = StringBuilderUnsignedText(scenario);
			for (var size = 0; size <= 11; size++)
			{
				var storage = new char[13];
				for (var index = 0; index < storage.Length; index++) storage[index] = '#';
				var destination = new Span<char>(storage, 1, size);
				var success = CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(value, default, null, destination, out var written);
				M68kRuntime.Collect();
				if (success != (size >= expected.Length) || written != (success ? expected.Length : 0)) return 1;
				for (var index = 0; index < storage.Length; index++)
					if (storage[index] != (success && index >= 1 && index <= written ? expected[index - 1] : '#')) return 2;
			}
			var text = CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(value, null, null);
			M68kRuntime.Collect();
			if (text != expected || CopperSharp.Runtime.ShadowNumberFormatting.UInt32ToDecStr(value) != expected) return 3;
			if (CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(value, string.Empty, null) != expected) return 4;
		}
		var buffer = new char[10];
		for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(42, "000.0", null, buffer, out _); return 5; }
		catch (NotSupportedException) { }
		try { _ = CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(42, "000.0", null); return 6; }
		catch (NotSupportedException) { }
		var provider = new CoreLibPrimitiveFormatProvider();
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt32(42, default, provider, buffer, out var count) || count != 2) return 7;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt32(42, null, provider) != "42") return 8;
		for (var index = 0; index < buffer.Length; index++) if (buffer[index] != (index < 2 ? "42"[index] : '#')) return 9;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLib64BitFormattingHelpersEntry()
	{
		for (var signedCase = 0; signedCase < 2; signedCase++)
		for (var scenario = 0; scenario < 14; scenario++)
		{
			var signed = signedCase == 0;
			var expected = StringBuilder64BitText(signed, scenario);
			for (var size = 0; size <= 21; size++)
			{
				var storage = new char[23];
				for (var index = 0; index < storage.Length; index++) storage[index] = '#';
				var destination = new Span<char>(storage, 1, size);
				var success = signed
					? CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(StringBuilderInt64Value(scenario), default, null, destination, out var written)
					: CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64(StringBuilderUInt64Value(scenario), default, null, destination, out written);
				M68kRuntime.Collect();
				if (success != (size >= expected.Length) || written != (success ? expected.Length : 0)) return 1;
				for (var index = 0; index < storage.Length; index++)
					if (storage[index] != (success && index >= 1 && index <= written ? expected[index - 1] : '#')) return 2;
			}
			var text = signed ? CopperSharp.Runtime.ShadowNumberFormatting.FormatInt64(StringBuilderInt64Value(scenario), null, null)
				: CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt64(StringBuilderUInt64Value(scenario), null, null);
			M68kRuntime.Collect();
			if (text != expected) return 3;
			if (signed)
			{
				if (CopperSharp.Runtime.ShadowNumberFormatting.Int64ToDecStr(StringBuilderInt64Value(scenario)) != expected ||
					CopperSharp.Runtime.ShadowNumberFormatting.FormatInt64(StringBuilderInt64Value(scenario), string.Empty, null) != expected) return 4;
			}
			else if (CopperSharp.Runtime.ShadowNumberFormatting.UInt64ToDecStr(StringBuilderUInt64Value(scenario)) != expected ||
				CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt64(StringBuilderUInt64Value(scenario), string.Empty, null) != expected) return 5;
		}
		var buffer = new char[20];
		for (var index = 0; index < buffer.Length; index++) buffer[index] = '#';
		var provider = new CoreLibPrimitiveFormatProvider();
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(42, "000.0", null, buffer, out _); return 6; } catch (NotSupportedException) { }
		try { CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64(42, "000.0", null, buffer, out _); return 7; } catch (NotSupportedException) { }
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatInt64(42, default, provider, buffer, out var count) || count != 2) return 8;
		if (!CopperSharp.Runtime.ShadowNumberFormatting.TryFormatUInt64(42, default, provider, buffer, out count) || count != 2) return 9;
		try { _ = CopperSharp.Runtime.ShadowNumberFormatting.FormatInt64(42, "000.0", null); return 10; } catch (NotSupportedException) { }
		try { _ = CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt64(42, "000.0", null); return 11; } catch (NotSupportedException) { }
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatInt64(42, null, provider) != "42") return 12;
		if (CopperSharp.Runtime.ShadowNumberFormatting.FormatUInt64(42, null, provider) != "42") return 13;
		for (var index = 0; index < buffer.Length; index++) if (buffer[index] != (index < 2 ? "42"[index] : '#')) return 14;
		return 42;
	}

	private sealed class CoreLibPrimitiveFormatProvider : IFormatProvider
	{
		public object? GetFormat(Type? formatType) => null;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderPrimitiveAllocationFailureEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			var available = new System.Text.StringBuilder(128);
			SetStringBuilderAllocationFailure(1);
			// Boolean appends need no allocation when there is room.
			available.Append(true).Append(false);
			SetStringBuilderAllocationFailure(0);
			if (available.ToString() != "TrueFalse") return 1;
			for (var operation = 0; operation < 4; operation++)
			// CoreLib's generic null check boxes uint; append fallback also allocates decimal text.
			for (var failAt = 1; failAt <= (operation == 1 ? 4 : operation == 3 ? 3 : 2); failAt++)
			{
				var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
				for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
				builder.Capacity = 65;
				var before = builder.ToString();
				// Insertion into earlier chunks can shift later offsets before allocation in CoreLib.
				// Pin failure preservation at the end of multiple chunks and in the middle of one chunk.
				var position = operation < 2 || layout == 0 ? 65 : 31;
				var expected = operation == 0 ? "True" : operation == 2 ? "False" : "4294967295";
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (operation == 0) builder.Append(true);
					else if (operation == 1) builder.Append(uint.MaxValue);
					else if (operation == 2) builder.Insert(position, false);
					else builder.Insert(position, uint.MaxValue);
					SetStringBuilderAllocationFailure(0);
					return 2;
				}
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					if (builder.Length != 65 || builder.Capacity != 65) return 3;
					for (var index = 0; index < 65; index++)
						if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8]) return 4;
					if (operation == 0) builder.Append(true);
					else if (operation == 1) builder.Append(uint.MaxValue);
					else if (operation == 2) builder.Insert(position, false);
					else builder.Insert(position, uint.MaxValue);
					if (builder.Length != 65 + expected.Length) return 5;
					for (var index = 0; index < builder.Length; index++)
					{
						var character = index < position ? pattern[index % 8]
							: index < position + expected.Length ? expected[index - position]
							: pattern[(index - expected.Length) % 8];
						if (builder[index] != character) return 6;
					}
					builder.Clear().Append(42u).Insert(0, true);
					if (builder.ToString() != "True42") return 7;
					for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 8;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilder64BitAllocationFailureEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			for (var operation = 0; operation < 4; operation++)
			// CoreLib boxes both 64-bit types; append fallback also allocates decimal text.
			for (var failAt = 1; failAt <= (operation < 2 ? 4 : 3); failAt++)
			{
				var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
				for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
				builder.Capacity = 65;
				var before = builder.ToString();
				// Insertion into earlier chunks can shift later offsets before allocation in CoreLib.
				// Pin failure preservation at the end of multiple chunks and in the middle of one chunk.
				var position = operation < 2 || layout == 0 ? 65 : 31;
				var expected = operation == 0 || operation == 2 ? "-9223372036854775808" : "18446744073709551615";
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (operation == 0) builder.Append(long.MinValue);
					else if (operation == 1) builder.Append(ulong.MaxValue);
					else if (operation == 2) builder.Insert(position, long.MinValue);
					else builder.Insert(position, ulong.MaxValue);
					SetStringBuilderAllocationFailure(0);
					return 2;
				}
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					if (builder.Length != 65 || builder.Capacity != 65) return 3;
					for (var index = 0; index < 65; index++)
						if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8]) return 4;
					if (operation == 0) builder.Append(long.MinValue);
					else if (operation == 1) builder.Append(ulong.MaxValue);
					else if (operation == 2) builder.Insert(position, long.MinValue);
					else builder.Insert(position, ulong.MaxValue);
					if (builder.Length != 65 + expected.Length) return 5;
					for (var index = 0; index < builder.Length; index++)
					{
						var character = index < position ? pattern[index % 8]
							: index < position + expected.Length ? expected[index - position]
							: pattern[(index - expected.Length) % 8];
						if (builder[index] != character) return 6;
					}
					builder.Clear().Append(42L).Insert(0, 1UL);
					if (builder.ToString() != "142") return 7;
					for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 8;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSmallIntegerAllocationFailureEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var kind = 0; kind < 4; kind++)
		{
			var scenario = kind == 0 || kind == 2 ? 0 : 11;
			var value = StringBuilderSmallIntegerValue(kind, scenario);
			var expected = StringBuilderSmallIntegerText(kind, scenario);
			var available = new System.Text.StringBuilder(128);
			available.Append('X');
			// Allow the generic null-check box, but fail any second allocation.
			SetStringBuilderAllocationFailure(2);
			AppendSmallInteger(available, kind, value);
			SetStringBuilderAllocationFailure(0);
			SetStringBuilderAllocationFailure(2);
			InsertSmallInteger(available, 0, kind, value);
			SetStringBuilderAllocationFailure(0);
			if (available.Length != 2 * expected.Length + 1 || available[expected.Length] != 'X') return 1;
			for (var index = 0; index < expected.Length; index++)
				if (available[index] != expected[index] || available[index + expected.Length + 1] != expected[index]) return 2;
			for (var layout = 0; layout < 2; layout++)
			for (var operation = 0; operation < 2; operation++)
			// Append fallback allocates a box, text, array and chunk; insertion allocates a box and chunk storage.
			for (var failAt = 1; failAt <= (operation == 0 ? 4 : 3); failAt++)
			{
				var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
				for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
				builder.Capacity = 65;
				var before = builder.ToString();
				// Pin preservation at the end of multiple chunks and in the middle of one chunk.
				var position = operation == 0 || layout == 0 ? 65 : 31;
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					if (operation == 0) AppendSmallInteger(builder, kind, value);
					else InsertSmallInteger(builder, position, kind, value);
					SetStringBuilderAllocationFailure(0);
					return 3;
				}
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					if (builder.Length != 65 || builder.Capacity != 65) return 4;
					for (var index = 0; index < 65; index++)
						if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8]) return 5;
					if (operation == 0) AppendSmallInteger(builder, kind, value);
					else InsertSmallInteger(builder, position, kind, value);
					if (builder.Length != 65 + expected.Length) return 6;
					for (var index = 0; index < builder.Length; index++)
					{
						var character = index < position ? pattern[index % 8]
							: index < position + expected.Length ? expected[index - position]
							: pattern[(index - expected.Length) % 8];
						if (builder[index] != character) return 7;
					}
					builder.Clear();
					AppendSmallInteger(builder, kind, 0);
					InsertSmallInteger(builder, 0, kind, value);
					if (builder.Length != expected.Length + 1 || builder[expected.Length] != '0') return 8;
					for (var index = 0; index < expected.Length; index++) if (builder[index] != expected[index]) return 9;
					for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 10;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderBuilderAppendAllocationFailureEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			var source = CreatePatternStringBuilder(layout == 0 ? 4 : 128, 65, 0);
			var empty = new System.Text.StringBuilder();
			var noOp = CreatePatternStringBuilder(layout == 0 ? 4 : 128, 65, 0);
			noOp.Capacity = 65;
			SetStringBuilderAllocationFailure(1);
			if (noOp.Append((System.Text.StringBuilder?)null) != noOp || noOp.Append(empty) != noOp ||
				noOp.Append(source, int.MaxValue, 0) != noOp || noOp.Append(noOp, int.MaxValue, 0) != noOp ||
				noOp.Append((System.Text.StringBuilder?)null, 0, 0) != noOp) { SetStringBuilderAllocationFailure(0); return 1; }
			SetStringBuilderAllocationFailure(0);
			if (noOp.Length != 65 || noOp.Capacity != 65) return 2;
			var available = CreatePatternStringBuilder(256, 17, 3);
			SetStringBuilderAllocationFailure(1);
			available.Append(source).Append(source, 1, 35);
			SetStringBuilderAllocationFailure(0);
			if (available.Length != 117) return 3;
			for (var index = 0; index < available.Length; index++)
				if (available[index] != pattern[(index < 17 ? index + 3 : index < 82 ? index - 17 : index - 81) % 8]) return 4;
			for (var ranged = 0; ranged < 2; ranged++)
			{
				var self = CreatePatternStringBuilder(layout == 0 ? 4 : 128, 65, 0);
				self.Capacity = 256;
				var before = self.ToString();
				var start = ranged == 0 ? 0 : 15;
				var count = ranged == 0 ? 65 : 35;
				// Self-append snapshots allocate even when the destination has room.
				SetStringBuilderAllocationFailure(1);
				try { AppendBuilderSource(self, self, start, count, ranged != 0); SetStringBuilderAllocationFailure(0); return 5; }
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					if (self.Length != 65 || self.Capacity != 256 || self.ToString() != before) return 6;
				}
				// Permit the snapshot but fail any additional allocation.
				SetStringBuilderAllocationFailure(2);
				AppendBuilderSource(self, self, start, count, ranged != 0);
				SetStringBuilderAllocationFailure(0);
				if (self.Length != 65 + count) return 7;
				for (var index = 0; index < self.Length; index++)
					if (self[index] != pattern[(index < 65 ? index : start + index - 65) % 8]) return 8;
				for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 9;
			}
			for (var operation = 0; operation < 4; operation++)
			// Distinct sources allocate chunk storage; self-appends first allocate a snapshot.
			for (var failAt = 1; failAt <= (operation < 2 ? 2 : 3); failAt++)
			{
				var builder = CreatePatternStringBuilder(layout == 0 ? 4 : 128, 65, 0);
				builder.Capacity = 65;
				var before = builder.ToString();
				var ranged = (operation & 1) != 0;
				var start = ranged ? 15 : 0;
				var count = ranged ? 35 : 65;
				var value = operation < 2 ? source : builder;
				SetStringBuilderAllocationFailure(failAt);
				try
				{
					AppendBuilderSource(builder, value, start, count, ranged);
					SetStringBuilderAllocationFailure(0);
					return 10;
				}
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					// No character space was available, so failures occur before a partial copy.
					if (builder.Length != 65 || builder.Capacity != 65) return 11;
					for (var index = 0; index < 65; index++)
						if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8] || source[index] != pattern[index % 8]) return 12;
					AppendBuilderSource(builder, value, start, count, ranged);
					if (builder.Length != 65 + count) return 13;
					for (var index = 0; index < builder.Length; index++)
						if (builder[index] != pattern[(index < 65 ? index : start + index - 65) % 8]) return 14;
					builder.Clear().Append(CreatePatternStringBuilder(4, 3, 3), 0, 3).Append('X');
					if (builder.ToString() != "\0\u03A9\uD83DX") return 15;
					for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8] || source[index] != pattern[index % 8]) return 16;
				}
			}
			for (var operation = 0; operation < 4; operation++)
			for (var growthFailure = 1; growthFailure <= 2; growthFailure++)
			{
				var builder = CreatePatternStringBuilder(layout == 0 ? 4 : 128, 65, 0);
				builder.Capacity = 72;
				var before = builder.ToString();
				var ranged = (operation & 1) != 0;
				var start = ranged ? 15 : 0;
				var count = ranged ? 35 : 65;
				var value = operation < 2 ? source : builder;
				SetStringBuilderAllocationFailure(growthFailure + (operation < 2 ? 0 : 1));
				try { AppendBuilderSource(builder, value, start, count, ranged); SetStringBuilderAllocationFailure(0); return 17; }
				catch (OutOfMemoryException)
				{
					SetStringBuilderAllocationFailure(0);
					// CoreLib consumes available space before growing, so failure retains a partial append.
					if (builder.Length != 72 || builder.Capacity != 72) return 18;
					for (var index = 0; index < 72; index++)
						if (builder[index] != pattern[(index < 65 ? index : start + index - 65) % 8]) return 19;
					for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8] || source[index] != pattern[index % 8]) return 20;
					builder.Length = 65;
					AppendBuilderSource(builder, value, start, count, ranged);
					if (builder.Length != 65 + count) return 21;
					for (var index = 0; index < builder.Length; index++)
						if (builder[index] != pattern[(index < 65 ? index : start + index - 65) % 8]) return 22;
					builder.Clear().Append('X');
					if (builder.ToString() != "X" || before.Length != 65) return 23;
				}
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEnsureCapacityEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128, 256);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var capacity = builder.Capacity;
			if (builder.EnsureCapacity(0) != capacity || builder.EnsureCapacity(64) != capacity ||
				builder.EnsureCapacity(capacity) != capacity) return 1;
			if (builder.EnsureCapacity(capacity + 17) != capacity + 17 || builder.Capacity != capacity + 17) return 2;
			if (builder.EnsureCapacity(capacity + 1) != capacity + 17) return 3;
			M68kRuntime.Collect();
			if (builder.Length != 65 || builder.MaxCapacity != 256) return 4;
			for (var index = 0; index < 65; index++)
				if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8]) return 5;
			if (builder.EnsureCapacity(256) != 256) return 6;
			builder.Append(pattern);
			M68kRuntime.Collect();
			if (builder.Length != 73 || builder.Capacity != 256) return 7;
			for (var index = 0; index < 8; index++) if (builder[index + 65] != pattern[index]) return 8;
			builder.Clear();
			capacity = builder.Capacity;
			if (builder.EnsureCapacity(1) != capacity || builder.Length != 0) return 9;
			builder.Append('X');
			if (builder.ToString() != "X" || before.Length != 65) return 10;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCapacitySetterEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128, 256);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			builder.Capacity = builder.Capacity;
			builder.Capacity = 145;
			M68kRuntime.Collect();
			if (builder.Capacity != 145 || builder.Length != 65 || builder.MaxCapacity != 256) return 1;
			builder.Capacity = 65;
			M68kRuntime.Collect();
			if (builder.Capacity != 65 || builder.ToString() != before) return 2;
			builder.Length = 70;
			builder.Capacity = 70;
			M68kRuntime.Collect();
			for (var index = 0; index < 70; index++)
				if (builder[index] != (index < 65 ? pattern[index % 8] : '\0')) return 3;
			builder.Length = 31;
			builder.Capacity = 31;
			builder.Append('\u03A9');
			M68kRuntime.Collect();
			if (builder.Length != 32 || builder[31] != '\u03A9') return 4;
			for (var index = 0; index < 31; index++) if (builder[index] != pattern[index % 8]) return 5;
			builder.Clear();
			builder.Capacity = 0;
			if (builder.Length != 0 || builder.Capacity != 0 || builder.EnsureCapacity(0) != 0) return 6;
			if (builder.EnsureCapacity(7) != 7) return 7;
			builder.Append(pattern);
			M68kRuntime.Collect();
			if (builder.Length != 8 || builder.Capacity < 8 || builder.MaxCapacity != 256) return 8;
			for (var index = 0; index < 8; index++) if (builder[index] != pattern[index]) return 9;
			for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 10;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCapacityManagementValidationEntry()
	{
		var builder = new System.Text.StringBuilder(8, 16);
		builder.Append("seed");
		try { builder.Capacity = -1; return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value" || (int)error.ActualValue! != -1) return 2; }
		try { builder.Capacity = 3; return 3; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 4; }
		try { builder.Capacity = 17; return 5; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 6; }
		try { builder.Capacity = int.MaxValue; return 7; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 8; }
		try { _ = builder.EnsureCapacity(-1); return 9; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "capacity" || (int)error.ActualValue! != -1) return 10; }
		try { _ = builder.EnsureCapacity(17); return 11; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 12; }
		try { _ = builder.EnsureCapacity(int.MaxValue); return 13; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 14; }
		if (builder.Length != 4 || builder.Capacity != 8 || builder.MaxCapacity != 16 || builder.ToString() != "seed") return 15;
		builder.Capacity = 16;
		if (builder.EnsureCapacity(16) != 16) return 16;
		// Small appends can leave existing storage above MaxCapacity in CoreLib.
		var oversized = new System.Text.StringBuilder(4, 10);
		for (var index = 0; index < 9; index++) oversized.Append((char)('A' + index));
		var capacity = oversized.Capacity;
		if (capacity <= oversized.MaxCapacity || oversized.EnsureCapacity(11) != capacity) return 17;
		try { oversized.Capacity = capacity; return 18; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "value") return 19; }
		oversized.Capacity = 10;
		if (oversized.Capacity != 10 || oversized.Length != 9 || oversized.MaxCapacity != 10) return 20;
		for (var index = 0; index < 9; index++) if (oversized[index] != (char)('A' + index)) return 21;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCapacityManagementAllocationFailureEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		for (var operation = 0; operation < 3; operation++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128, 256);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var capacity = builder.Capacity;
			var requested = operation == 2 ? 65 : capacity + 17;
			SetStringBuilderAllocationFailure(1);
			// Reserving already available storage and assigning the same capacity do not allocate.
			if (builder.EnsureCapacity(0) != capacity || builder.EnsureCapacity(65) != capacity ||
				builder.EnsureCapacity(capacity) != capacity) return 1;
			builder.Capacity = capacity;
			try
			{
				if (operation == 0) _ = builder.EnsureCapacity(requested); else builder.Capacity = requested;
				SetStringBuilderAllocationFailure(0);
				return 2;
			}
			catch (OutOfMemoryException)
			{
				SetStringBuilderAllocationFailure(0);
				if (builder.Length != 65 || builder.Capacity != capacity || builder.MaxCapacity != 256) return 3;
				for (var index = 0; index < 65; index++)
					if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8]) return 4;
				if (operation == 0)
				{
					if (builder.EnsureCapacity(requested) != requested) return 5;
				}
				else builder.Capacity = requested;
				if (builder.Capacity != requested || builder.Length != 65) return 6;
				builder.Append('X');
				if (builder.Length != 66 || builder[65] != 'X') return 7;
				for (var index = 0; index < 65; index++)
					if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8]) return 8;
				builder.Clear().Append('Y');
				if (builder.ToString() != "Y") return 9;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderRangedToStringEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var capacity = builder.Capacity;
			for (var position = 0; position < 11; position++)
			for (var countCase = 0; countCase < 4; countCase++)
			{
				var start = StringBuilderEditPosition(position);
				var remaining = 65 - start;
				var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
					: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
				var text = builder.ToString(start, count);
				if (text.Length != count || builder.Length != 65 || builder.Capacity != capacity) return 1;
				if (count == 0 && ReferenceEquals(text, string.Empty)) return 2;
				if (count != 0) builder[start] = 'X';
				M68kRuntime.Collect();
				for (var index = 0; index < count; index++)
					if (text[index] != pattern[(start + index) % 8]) return 3;
				if (count != 0) builder[start] = pattern[start % 8];
			}
			builder.Clear();
			if (builder.ToString(0, 0).Length != 0) return 4;
			builder.Append(pattern);
			if (builder.ToString(4, 3) != "\u03A9\uD83D\uDE00") return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderArrayCopyToEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var layout = 0; layout < 2; layout++)
		{
			var builder = new System.Text.StringBuilder(layout == 0 ? 4 : 128);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var capacity = builder.Capacity;
			for (var position = 0; position < 11; position++)
			for (var countCase = 0; countCase < 4; countCase++)
			{
				var start = StringBuilderEditPosition(position);
				var remaining = 65 - start;
				var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
					: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
				var destination = new char[72];
				for (var index = 0; index < destination.Length; index++) destination[index] = '\uF00D';
				var destinationIndex = countCase + 2;
				builder.CopyTo(start, destination, destinationIndex, count);
				M68kRuntime.Collect();
				for (var index = 0; index < destination.Length; index++)
				{
					var expected = index >= destinationIndex && index < destinationIndex + count
						? pattern[(start + index - destinationIndex) % 8] : '\uF00D';
					if (destination[index] != expected) return 1;
				}
				if (builder.Length != 65 || builder.Capacity != capacity) return 2;
			}
			builder.CopyTo(65, new char[0], 0, 0);
			builder.Clear();
			builder.CopyTo(0, new char[0], 0, 0);
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderSpanCopyToEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		var builder = new System.Text.StringBuilder(4);
		for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
		for (var position = 0; position < 11; position++)
		for (var countCase = 0; countCase < 4; countCase++)
		{
			var start = StringBuilderEditPosition(position);
			var remaining = 65 - start;
			var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
				: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
			char[]? storage = new char[72];
			for (var index = 0; index < storage.Length; index++) storage[index] = '\uF00D';
			var destination = new Span<char>(storage, 2, 68);
			storage = null;
			M68kRuntime.Collect();
			builder.CopyTo(start, destination, count);
			M68kRuntime.Collect();
			for (var index = 0; index < destination.Length; index++)
				if (destination[index] != (index < count ? pattern[(start + index) % 8] : '\uF00D')) return 1;
		}
		Span<char> stack = stackalloc char[71];
		for (var index = 0; index < stack.Length; index++) stack[index] = '\uFFFF';
		builder.CopyTo(0, stack.Slice(3, 65), 65);
		M68kRuntime.Collect();
		for (var index = 0; index < stack.Length; index++)
			if (stack[index] != (index >= 3 && index < 68 ? pattern[(index - 3) % 8] : '\uFFFF')) return 2;
		builder.CopyTo(65, default(Span<char>), 0);
		builder.Clear();
		builder.CopyTo(0, default(Span<char>), 0);
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderExtractionValidationEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		builder.Append("seed");
		var destination = new char[4];
		for (var index = 0; index < destination.Length; index++) destination[index] = '\uF00D';
		try { _ = builder.ToString(-1, 0); return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 2; }
		try { _ = builder.ToString(0, -1); return 3; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "length") return 4; }
		try { _ = builder.ToString(5, 0); return 5; } catch (ArgumentOutOfRangeException) { }
		try { _ = builder.ToString(1, int.MaxValue); return 6; } catch (ArgumentOutOfRangeException) { }
		try { builder.CopyTo(0, null!, 0, 0); return 7; }
		catch (ArgumentNullException error) { if (error.ParamName != "destination") return 8; }
		try { builder.CopyTo(0, destination, -1, 0); return 9; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "destinationIndex") return 10; }
		try { builder.CopyTo(0, destination, 0, -1); return 11; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "count") return 12; }
		try { builder.CopyTo(-1, destination, 0, 0); return 13; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "sourceIndex") return 14; }
		try { builder.CopyTo(5, destination, 0, 0); return 15; } catch (ArgumentOutOfRangeException) { }
		try { builder.CopyTo(1, destination, 0, 4); return 16; } catch (ArgumentException) { }
		try { builder.CopyTo(0, destination, 1, 4); return 17; } catch (ArgumentException) { }
		try { builder.CopyTo(0, destination, 5, 0); return 18; } catch (ArgumentException) { }
		try { builder.CopyTo(0, destination, 1, int.MaxValue); return 19; } catch (ArgumentException) { }
		var span = new Span<char>(destination);
		try { builder.CopyTo(-1, span, 0); return 20; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "sourceIndex") return 21; }
		try { builder.CopyTo(0, span, -1); return 22; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "count") return 23; }
		try { builder.CopyTo(5, span, 0); return 24; } catch (ArgumentOutOfRangeException) { }
		try { builder.CopyTo(1, span, 4); return 25; } catch (ArgumentException) { }
		try { builder.CopyTo(0, span, int.MaxValue); return 26; } catch (ArgumentException) { }
		try { builder.CopyTo(0, default(Span<char>), 1); return 27; } catch (ArgumentException) { }
		for (var index = 0; index < destination.Length; index++) if (destination[index] != '\uF00D') return 28;
		return builder.ToString() == "seed" && builder.Length == 4 ? 42 : 29;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderExtractionAllocationFailureEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		var builder = new System.Text.StringBuilder(4);
		for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
		var before = builder.ToString();
		var capacity = builder.Capacity;
		var destination = new char[72];
		for (var index = 0; index < destination.Length; index++) destination[index] = '\uF00D';
		for (var countCase = 0; countCase < 2; countCase++)
		{
			var count = countCase == 0 ? 0 : 35;
			SetStringBuilderAllocationFailure(1);
			// Both CopyTo overloads remain usable with the allocator disabled.
			builder.CopyTo(0, destination, 2, 65);
			builder.CopyTo(0, new Span<char>(destination, 3, 65), 65);
			try
			{
				_ = builder.ToString(17, count);
				SetStringBuilderAllocationFailure(0);
				return 2;
			}
			catch (OutOfMemoryException)
			{
				SetStringBuilderAllocationFailure(0);
				if (builder.Length != 65 || builder.Capacity != capacity) return 3;
				for (var index = 0; index < 65; index++)
					if (builder[index] != pattern[index % 8] || before[index] != pattern[index % 8] ||
						destination[index + 3] != pattern[index % 8]) return 4;
				if (destination[0] != '\uF00D' || destination[1] != '\uF00D' || destination[2] != 'a' || destination[68] != '\uF00D') return 5;
				var text = builder.ToString(17, count);
				if (text.Length != count) return 6;
				for (var index = 0; index < text.Length; index++) if (text[index] != pattern[(17 + index) % 8]) return 7;
			}
		}
		builder.Clear().Append('X');
		return builder.ToString(0, 1) == "X" ? 42 : 8;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCharacterReplacementEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var scenario = 0; scenario < 11; scenario++)
		for (var countCase = 0; countCase < 4; countCase++)
		{
			var start = StringBuilderEditPosition(scenario);
			var remaining = 65 - start;
			var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
				: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
			var builder = new System.Text.StringBuilder(4);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			var capacity = builder.Capacity;
			if (builder.Replace('a', '\uFFFF', start, count) != builder) return 1;
			M68kRuntime.Collect();
			var after = builder.ToString();
			if (builder.Length != 65 || builder.Capacity != capacity) return 2;
			for (var index = 0; index < 65; index++)
			{
				var original = pattern[index % 8];
				var expected = index >= start && index < start + count && original == 'a' ? '\uFFFF' : original;
				if (builder[index] != expected || after[index] != expected || before[index] != original) return 3;
			}
			builder.Replace('\0', '\u03A9').Replace('\uD83D', '\uDE00');
			for (var index = 0; index < 65; index++)
			{
				var expected = after[index];
				if (expected == '\0') expected = '\u03A9';
				if (expected == '\uD83D') expected = '\uDE00';
				if (builder[index] != expected) return 4;
			}
			builder.Replace('X', 'Y').Replace('\uFFFF', '\uFFFF');
			builder.Clear().Append('X');
			if (builder.ToString() != "X") return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderStringReplacementEntry()
	{
		const string pattern = "aba\0\u03A9\uD83D\uDE00\uFFFF";
		for (var scenario = 0; scenario < 8; scenario++)
		for (var range = 0; range < 4; range++)
		{
			var oldValue = scenario == 3 ? "\uFFFFaba\0" : scenario == 4 ? "\0\u03A9\uD83D\uDE00" :
				scenario == 5 ? "missing" : scenario >= 6 ? "a" : "aba";
			string? newValue = scenario == 0 || scenario == 3 ? "Z" : scenario == 1 ? "xyz" :
				scenario == 2 ? "\u03A9\0\uD83D\uDE00\uFFFFLONG" : scenario == 7 ? null : scenario == 6 ? "aa" : "";
			var start = range == 0 ? 0 : range == 1 ? 15 : range == 2 ? 4 : 65;
			var count = range == 0 ? 65 : range == 1 ? 19 : range == 2 ? 12 : 0;
			var builder = new System.Text.StringBuilder(4);
			for (var index = 0; index < 65; index++) builder.Append(pattern[index % 8]);
			var before = builder.ToString();
			if (builder.Replace(oldValue, newValue, start, count) != builder) return 1;
			M68kRuntime.Collect();
			var after = builder.ToString();
			for (var index = 0; index < 65; index++) if (before[index] != pattern[index % 8]) return 3;
			if (!StringBuilderReplacementMatches(builder, before, after, oldValue, newValue, start, count)) return 2;
			builder.Clear().Append("abaaba");
			builder.Replace("aba", "Q");
			if (builder.ToString() != "QQ") return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibUninitializedSpanOwnerEntry()
	{
		var source = new char[8];
		for (var index = 0; index < source.Length; index++) source[index] = (char)(0xD83D + index);
		var first = ReadSpanAfterCollection(source);
		for (var index = 0; index < source.Length; index++) if (source[index] != (char)(0xD83D + index)) return 1;
		return first == '\uD83D' ? 42 : 2;
	}

	[SkipLocalsInit]
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe char ReadSpanAfterCollection(char[] source)
	{
		M68kRuntime.Collect();
		var span = new ReadOnlySpan<char>(source);
		return span[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderReplacementMatchGrowthEntry()
	{
		for (var scenario = 0; scenario < 2; scenario++)
		{
			var builder = new System.Text.StringBuilder(1024);
			for (var index = 0; index < 513; index++) builder.Append('a');
			var before = builder.ToString();
			var expected = scenario == 0 ? "bc" : "\u03A9\0\uD83D\uDE00\uFFFF";
			if (scenario == 0)
			{
				if (builder.Replace("a", expected, 1, 511) != builder) return 1;
			}
			else
			{
				char[]? oldArray = new char[3];
				oldArray[0] = 'X'; oldArray[1] = 'a'; oldArray[2] = 'Y';
				char[]? newArray = new char[7];
				newArray[0] = 'X'; newArray[6] = 'Y';
				for (var index = 0; index < expected.Length; index++) newArray[index + 1] = expected[index];
				var oldView = new ReadOnlySpan<char>(oldArray, 1, 1);
				var newView = new ReadOnlySpan<char>(newArray, 1, 5);
				oldArray = null; newArray = null;
				M68kRuntime.Collect();
				if (builder.Replace(oldView, newView, 1, 511) != builder) return 2;
				M68kRuntime.Collect();
				builder.Clear().Append("aa").Replace(oldView, newView);
				var reused = builder.ToString();
				if (reused.Length != 10) return 3;
				for (var index = 0; index < 10; index++) if (reused[index] != expected[index % 5]) return 4;
				builder.Clear();
				for (var index = 0; index < 513; index++) builder.Append('a');
				builder.Replace(oldView, newView, 1, 511);
			}
			M68kRuntime.Collect();
			var after = builder.ToString();
			if (!StringBuilderReplacementMatches(builder, before, after, "a", expected, 1, 511)) return 5;
			for (var index = 0; index < before.Length; index++) if (before[index] != 'a') return 6;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderReplacementAllocationFailureEntry()
	{
		// Two match-list growth allocations precede the new chunk and its array.
		for (var failAt = 1; failAt <= 4; failAt++)
		{
			var builder = new System.Text.StringBuilder(1024);
			for (var index = 0; index < 513; index++) builder.Append('a');
			var before = builder.ToString();
			SetStringBuilderAllocationFailure(failAt);
			try
			{
				builder.Replace("a", "bc", 1, 511);
				SetStringBuilderAllocationFailure(0);
				return 1;
			}
			catch (OutOfMemoryException)
			{
				SetStringBuilderAllocationFailure(0);
				if (builder.Length != 513 || builder.Capacity != 1024) return 2;
				for (var index = 0; index < 513; index++) if (builder[index] != 'a' || before[index] != 'a') return 3;
				builder.Replace("a", "bc", 1, 511);
				if (!StringBuilderReplacementMatches(builder, before, builder.ToString(), "a", "bc", 1, 511)) return 4;
			}
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static bool StringBuilderReplacementMatches(System.Text.StringBuilder builder, string before, string after,
		string oldValue, string? newValue, int start, int count)
	{
		var resultIndex = 0;
		for (var cursor = 0; cursor < before.Length;)
		{
			var matches = cursor >= start && cursor <= start + count - oldValue.Length;
			for (var index = 0; matches && index < oldValue.Length; index++)
				if (before[cursor + index] != oldValue[index]) matches = false;
			if (matches)
			{
				for (var index = 0; newValue != null && index < newValue.Length; index++)
				{
					if (resultIndex >= builder.Length || resultIndex >= after.Length ||
						builder[resultIndex] != newValue[index] || after[resultIndex] != newValue[index]) return false;
					resultIndex++;
				}
				cursor += oldValue.Length;
			}
			else
			{
				if (resultIndex >= builder.Length || resultIndex >= after.Length ||
					builder[resultIndex] != before[cursor] || after[resultIndex] != before[cursor]) return false;
				resultIndex++;
				cursor++;
			}
		}
		return builder.Length == resultIndex && after.Length == resultIndex;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderReplacementValidationEntry()
	{
		var builder = new System.Text.StringBuilder(4, 32);
		builder.Append("seed");
		try { builder.Replace('s', 'X', -1, 0); return 1; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 2; }
		try { builder.Replace('s', 'X', 0, -1); return 3; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "count") return 4; }
		try { builder.Replace('s', 'X', 5, 0); return 5; } catch (ArgumentOutOfRangeException) { }
		try { builder.Replace('s', 'X', 1, int.MaxValue); return 6; } catch (ArgumentOutOfRangeException) { }
		try { builder.Replace((string)null!, "X"); return 7; }
		catch (ArgumentNullException error) { if (error.ParamName != "oldValue") return 8; }
		try { builder.Replace("", "X"); return 9; }
		catch (ArgumentException error) { if (error.ParamName != "oldValue") return 10; }
		try { builder.Replace("s", "X", -1, 0); return 11; } catch (ArgumentOutOfRangeException) { }
		try { builder.Replace("s", "X", 0, -1); return 12; } catch (ArgumentOutOfRangeException) { }
		try { builder.Replace("s", "X", 1, int.MaxValue); return 13; } catch (ArgumentOutOfRangeException) { }
		if (builder.Replace("missing", "X") != builder || builder.Replace('s', 's') != builder ||
			builder.Replace("s", "X", 4, 0) != builder) return 14;
		if (builder.ToString() != "seed" || builder.MaxCapacity != 32) return 15;
		try { builder.Replace("e", "12345678901234567890"); return 17; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "requiredLength") return 21; }
		if (builder.ToString() != "seed") return 18;
		try { builder.Replace(default(ReadOnlySpan<char>), default(ReadOnlySpan<char>)); return 19; }
		catch (ArgumentException error) { if (error.ParamName != "oldValue") return 20; }
		builder.Clear().Replace('a', 'b').Replace("a", "b");
		return builder.Length == 0 ? 42 : 16;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderStringInsertionEntry()
	{
		const string inserted = "I\0\u03A9\uD83D\uDE00\uFFFF";
		for (var scenario = 0; scenario < 11; scenario++)
		{
			var position = StringBuilderEditPosition(scenario);
			var builder = new System.Text.StringBuilder(4);
			for (var index = 0; index < 65; index++) builder.Append((char)(index * 251));
			var before = builder.ToString();
			if (builder.Insert(position, inserted) != builder || builder.Length != 71) return 1;
			M68kRuntime.Collect();
			var after = builder.ToString();
			for (var index = 0; index < after.Length; index++)
			{
				var expected = index < position ? (char)(index * 251) : index < position + inserted.Length
					? inserted[index - position] : (char)((index - inserted.Length) * 251);
				if (builder[index] != expected || after[index] != expected) return 2;
			}
			if (builder.Remove(position, inserted.Length) != builder || builder.Length != 65) return 3;
			for (var index = 0; index < 65; index++)
				if (builder[index] != (char)(index * 251) || before[index] != (char)(index * 251)) return 4;
			if (after[position] != 'I') return 5;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderRemovalEntry()
	{
		for (var scenario = 0; scenario < 11; scenario++)
		for (var countCase = 0; countCase < 4; countCase++)
		{
			var position = StringBuilderEditPosition(scenario);
			var remaining = 65 - position;
			var count = countCase == 0 ? 0 : countCase == 1 ? (remaining == 0 ? 0 : 1)
				: countCase == 2 ? (remaining < 17 ? remaining : 17) : remaining;
			var builder = new System.Text.StringBuilder(4);
			for (var index = 0; index < 65; index++) builder.Append((char)(index * 251));
			var before = builder.ToString();
			if (builder.Remove(position, count) != builder || builder.Length != 65 - count) return 1;
			M68kRuntime.Collect();
			var after = builder.ToString();
			for (var index = 0; index < after.Length; index++)
			{
				var expected = (char)((index < position ? index : index + count) * 251);
				if (builder[index] != expected || after[index] != expected) return 2;
			}
			builder.Append('\uFFFF');
			if (builder[builder.Length - 1] != '\uFFFF') return 3;
			for (var index = 0; index < before.Length; index++)
				if (before[index] != (char)(index * 251)) return 4;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderInsertionOverloadsEntry()
	{
		var builder = new System.Text.StringBuilder(4);
		if (builder.Insert(0, '\u03A9') != builder || builder.Insert(1, '\0') != builder ||
			builder.Insert(2, '\uD83D') != builder || builder.Insert(3, '\uDE00') != builder) return 1;
		char[]? source = new char[257];
		for (var index = 0; index < source.Length; index++) source[index] = (char)(index * 251);
		if (builder.Insert(2, source, 7, 65) != builder) return 2;
		var before = builder.ToString();
		var view = new ReadOnlySpan<char>(source, 100, 157);
		source = null;
		M68kRuntime.Collect();
		if (builder.Insert(1, view) != builder || builder.Length != 226) return 3;
		M68kRuntime.Collect();
		var after = builder.ToString();
		if (after[0] != '\u03A9' || after[158] != '\0' || after[224] != '\uD83D' || after[225] != '\uDE00') return 4;
		for (var index = 0; index < 157; index++)
			if (after[index + 1] != (char)((index + 100) * 251)) return 5;
		for (var index = 0; index < 65; index++)
			if (after[index + 159] != (char)((index + 7) * 251) || before[index + 2] != (char)((index + 7) * 251)) return 6;
		builder.Clear();
		var entire = new char[3];
		entire[0] = '\u03A9'; entire[1] = '\0'; entire[2] = '\uFFFF';
		if (builder.Insert(0, entire) != builder || builder.ToString() != "\u03A9\0\uFFFF") return 7;
		builder.Clear();
		if (builder.Insert(0, int.MinValue) != builder || builder.Insert(11, 42) != builder ||
			builder.Insert(0, 0) != builder || builder.ToString() != "0-214748364842") return 8;
		return before.Length == 69 && before[0] == '\u03A9' && before[1] == '\0' &&
			before[67] == '\uD83D' && before[68] == '\uDE00' ? 42 : 9;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderRepeatedInsertionEntry()
	{
		const string inserted = "I\0\u03A9\uD83D\uDE00\uFFFF";
		var builder = new System.Text.StringBuilder(4);
		for (var index = 0; index < 65; index++) builder.Append((char)(index * 251));
		var before = builder.ToString();
		if (builder.Insert(17, inserted, 23) != builder || builder.Length != 203) return 1;
		M68kRuntime.Collect();
		var after = builder.ToString();
		for (var index = 0; index < after.Length; index++)
		{
			var expected = index < 17 ? (char)(index * 251) : index < 155 ? inserted[(index - 17) % 6]
				: (char)((index - 138) * 251);
			if (builder[index] != expected || after[index] != expected) return 2;
		}
		builder.Remove(17, 138);
		for (var index = 0; index < 65; index++)
			if (builder[index] != (char)(index * 251) || before[index] != (char)(index * 251)) return 3;
		return after[17] == 'I' && after[154] == '\uFFFF' ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderEditValidationEntry()
	{
		var builder = new System.Text.StringBuilder(4, 32);
		builder.Append("seed");
		var chars = new char[3];
		if (builder.Insert(4, (string?)null) != builder || builder.Insert(0, string.Empty) != builder ||
			builder.Insert(0, (char[]?)null) != builder || builder.Insert(0, (char[]?)null, 0, 0) != builder ||
			builder.Insert(0, default(ReadOnlySpan<char>)) != builder || builder.Insert(2, "X", 0) != builder ||
			builder.Insert(1, (string?)null, 3) != builder || builder.Remove(4, 0) != builder) return 1;
		try { builder.Insert(-1, "X"); return 2; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "index") return 3; }
		try { builder.Insert(5, "X"); return 4; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(int.MaxValue, default(ReadOnlySpan<char>)); return 5; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(0, "X", -1); return 6; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "count") return 7; }
		try { builder.Insert(0, chars, -1, 1); return 8; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(0, chars, 0, -1); return 9; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(0, chars, 2, 2); return 10; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(0, chars, int.MaxValue, 1); return 11; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(0, (char[]?)null, 0, 1); return 12; } catch (ArgumentNullException) { }
		try { builder.Remove(-1, 0); return 13; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "startIndex") return 14; }
		try { builder.Remove(0, -1); return 15; }
		catch (ArgumentOutOfRangeException error) { if (error.ParamName != "length") return 16; }
		try { builder.Remove(5, 0); return 17; } catch (ArgumentOutOfRangeException) { }
		try { builder.Remove(1, int.MaxValue); return 18; } catch (ArgumentOutOfRangeException) { }
		try { builder.Insert(2, "X", 29); return 19; } catch (OutOfMemoryException) { }
		try { builder.Insert(2, "XX", int.MaxValue); return 21; } catch (OutOfMemoryException) { }
		return builder.Length == 4 && builder.ToString() == "seed" && builder.MaxCapacity == 32 ? 42 : 20;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibReadOnlyCharacterArrayRangeEntry()
	{
		char[]? source = new char[4];
		source[0] = 'X'; source[1] = '\u03A9'; source[2] = '\uFFFF'; source[3] = 'Y';
		var range = new ReadOnlySpan<char>(source, 1, 2);
		source = null;
		M68kRuntime.Collect();
		var replacement = new char[4];
		replacement[1] = 'Z';
		if (range.Length != 2 || range[0] != '\u03A9' || range[1] != '\uFFFF') return 1;
		if (new ReadOnlySpan<char>((char[]?)null, 0, 0).Length != 0 || new ReadOnlySpan<char>(replacement, 4, 0).Length != 0) return 2;
		try { return new ReadOnlySpan<char>((char[]?)null, 0, 1).Length + 3; } catch (ArgumentOutOfRangeException) { }
		try { return new ReadOnlySpan<char>(replacement, -1, 0).Length + 4; } catch (ArgumentOutOfRangeException) { }
		try { return new ReadOnlySpan<char>(replacement, 0, -1).Length + 5; } catch (ArgumentOutOfRangeException) { }
		try { return new ReadOnlySpan<char>(replacement, 3, 2).Length + 6; } catch (ArgumentOutOfRangeException) { }
		try { return new ReadOnlySpan<char>(replacement, int.MaxValue, 1).Length + 7; } catch (ArgumentOutOfRangeException) { }
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Int64MultiplicationEntry()
	{
		if (MultiplyInt64(0, long.MinValue) != 0 || MultiplyInt64(long.MaxValue, 1) != long.MaxValue) return 1;
		if (MultiplyInt64(-1, long.MinValue) != long.MinValue || MultiplyInt64(long.MaxValue, -1) != -long.MaxValue) return 2;
		if (MultiplyInt64(0x100000001L, 0x100000001L) != 0x200000001L) return 3;
		if (MultiplyInt64(0xFFFFFFFFL, 0xFFFFFFFFL) != unchecked((long)0xFFFFFFFE00000001UL)) return 4;
		if (MultiplyInt64(long.MinValue, 2) != 0 || MultiplyInt64(-3, -7) != 21) return 5;
		if (MultiplyInt64(int.MaxValue, int.MaxValue) != 4611686014132420609L) return 6;
		if (MultiplyInt64(0x123456789ABCDEFL, -0x1020304050607L) != unchecked(0x123456789ABCDEFL * -0x1020304050607L)) return 7;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static long MultiplyInt64(long first, long second) => unchecked(first * second);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ByrefArgumentReassignmentEntry()
	{
		char[]? owner = new char[5];
		owner[0] = 'A'; owner[1] = 'B'; owner[2] = '\u03A9'; owner[3] = '\uD83D'; owner[4] = '\uDE00';
		ref var start = ref owner[0];
		owner = null;
		M68kRuntime.Collect();
		if (AdvanceByrefArgument(ref start, 4) != '\uDE00' || start != 'A') return 1;
		if (ReassignByrefArgumentOwner(ref start, false) != '\u03A9' ||
			ReassignByrefArgumentOwner(ref start, true) != '\uFFFF' || start != 'A') return 2;
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static char AdvanceByrefArgument(ref char source, int count)
	{
		while (count-- > 0)
		{
			source = ref System.Runtime.CompilerServices.Unsafe.Add(ref source, 1);
			M68kRuntime.Collect();
		}
		return source;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static char ReassignByrefArgumentOwner(ref char source, bool alternate)
	{
		char[]? owner = new char[4];
		owner[0] = 'X'; owner[1] = 'Y'; owner[2] = '\u03A9'; owner[3] = '\uFFFF';
		source = ref owner[0];
		if (alternate) source = ref System.Runtime.CompilerServices.Unsafe.Add(ref source, 1);
		owner = null;
		M68kRuntime.Collect();
		var replacement = new char[4];
		replacement[2] = 'Z';
		for (var index = 0; index < 2; index++) source = ref System.Runtime.CompilerServices.Unsafe.Add(ref source, 1);
		M68kRuntime.Collect();
		return source;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int StringBuilderEditPosition(int scenario) => scenario switch
	{
		0 => 0, 1 => 1, 2 => 3, 3 => 4, 4 => 15, 5 => 16,
		6 => 31, 7 => 32, 8 => 33, 9 => 64, _ => 65
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderIntegerCapacityLimitEntry()
	{
		var builder = new System.Text.StringBuilder(4, 4);
		builder.Append(42);
		try { builder.Append(int.MinValue); }
		catch (ArgumentOutOfRangeException)
		{
			return builder.ToString() == "42" ? 42 : 1;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderIntegerAllocationFailureEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 15; index++) builder.Append('X');
		try { builder.Append(int.MinValue); }
		catch (OutOfMemoryException)
		{
			return builder.Length == 15 && builder[14] == 'X' ? 42 : 1;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAppendCharEntry()
	{
		var builder = new System.Text.StringBuilder();
		builder.Append('4');
		builder.Append('2');
		return builder.Length == 2 && builder[0] == '4' && builder[1] == '2' ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderChunkGrowthEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 257; index++)
		{
			if (builder.Append((char)(index * 251)) != builder) return 1;
		}
		if (builder.Length != 257 || builder.Capacity < 257) return 2;
		for (var index = 0; index < builder.Length; index++)
		{
			if (builder[index] != (char)(index * 251)) return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderToStringEntry()
	{
		var builder = new System.Text.StringBuilder();
		if (builder.ToString() != string.Empty) return 1;
		for (var index = 0; index < 257; index++) builder.Append((char)(index * 251));
		var text = builder.ToString();
		builder.Append('\uD83D');
		builder.Append('\uDE00');
		builder[0] = 'X';
		if (text.Length != 257) return 2;
		for (var index = 0; index < text.Length; index++)
		{
			if (text[index] != (char)(index * 251)) return 3;
		}
		var updated = builder.ToString();
		return updated.Length == 259 && updated[0] == 'X' &&
			updated[257] == '\uD83D' && updated[258] == '\uDE00' ? 42 : 4;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderGcEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 257; index++) builder.Append((char)(index * 251));
		var first = builder.ToString();
		M68kRuntime.Collect();
		for (var index = 257; index < 513; index++) builder.Append((char)(index * 251));
		var second = builder.ToString();
		M68kRuntime.Collect();
		if (first.Length != 257 || second.Length != 513 || builder.Length != 513) return 1;
		for (var index = 0; index < second.Length; index++)
		{
			var expected = (char)(index * 251);
			if (builder[index] != expected || second[index] != expected) return 2;
			if (index < first.Length && first[index] != expected) return 3;
		}
		return 42;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCapacityLimitEntry()
	{
		var builder = new System.Text.StringBuilder(16, 32);
		try
		{
			for (var index = 0; index < 33; index++) builder.Append((char)index);
		}
		catch (ArgumentOutOfRangeException)
		{
			return builder.Length == 32 && builder[31] == (char)31 ? 42 : 1;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderCapacityValidationEntry()
	{
		var builder = new System.Text.StringBuilder(0, 32);
		if (builder.Length != 0 || builder.Capacity != 16 || builder.MaxCapacity != 32) return 1;
		try { _ = new System.Text.StringBuilder(-1, 32); }
		catch (ArgumentOutOfRangeException error)
		{
			return error.ParamName == "capacity" && (int)error.ActualValue! == -1 ? 42 : 2;
		}
		return 3;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ScalarArgumentAssignmentEntry()
	{
		return AssignScalarArgument(0) == 42 && AssignNarrowArgument((char)0) == '\uFFFF' &&
			AssignReferenceArgument(null) == 42 ? 42 : 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AssignScalarArgument(int value)
	{
		while (value < 42) value += 3;
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static char AssignNarrowArgument(char value)
	{
		if (value == 0) value = '\uFFFF';
		return value;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int AssignReferenceArgument(char[]? value)
	{
		value = new char[1];
		value[0] = (char)42;
		M68kRuntime.Collect();
		return value[0];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderAllocationFailureEntry()
	{
		var builder = new System.Text.StringBuilder();
		try
		{
			for (var index = 0; index < 17; index++) builder.Append((char)index);
		}
		catch (OutOfMemoryException)
		{
			return builder.Length == 16 && builder[15] == (char)15 ? 42 : 1;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibStringBuilderToStringAllocationFailureEntry()
	{
		var builder = new System.Text.StringBuilder();
		for (var index = 0; index < 16; index++) builder.Append((char)index);
		try { _ = builder.ToString(); }
		catch (OutOfMemoryException)
		{
			return builder.Length == 16 && builder[15] == (char)15 ? 42 : 1;
		}
		return 2;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CoreLibOutOfMemoryConstructorEntry()
	{
		try { throw new OutOfMemoryException(); }
		catch (OutOfMemoryException) { return 42; }
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
	public static int CoreLibExternalExceptionToStringCutPointEntry()
	{
		var text = new FixtureExternalException().FormatBase();
		return text == "System.Runtime.InteropServices.ExternalException" ? 42 : 0;
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
	public static int DynamicSignedByteArrayEntry()
	{
		var values = new sbyte[3];
		values[0] = 12;
		values[1] = -5;
		values[2] = 35;
		return values[DynamicArrayIndex()];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int DynamicShortArrayEntry()
	{
		var values = new short[3];
		values[0] = 300;
		values[1] = -20;
		values[2] = 7;
		return values[DynamicArrayIndex()];
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int DynamicArrayIndex() => 1;

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
	public static uint AptrUnsignedWordPromotionEntry()
	{
		var address = APTR.FromPointer(0x0000_4000);
		APTR.WriteUInt16(address, 16, 0x04B8);
		APTR.WriteUInt16(address, 18, 0x0128);
		var negativeSize = APTR.ReadUInt16(address, 16);
		var positiveSize = APTR.ReadUInt16(address, 18);
		return (uint)negativeSize + positiveSize;
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
	public static uint ExplicitCollectWithDynamicFrameEntry() => ExplicitCollectWithDynamicFrame(3);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint ExplicitCollectWithDynamicFrame(int count)
	{
		var scratch = stackalloc uint[count];
		scratch[0] = 42;
		M68kRuntime.Collect();
		return scratch[0];
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
	public static int PromotedReferenceArrayAcrossCollectionEntry()
	{
		var values = new object[1];
		var child = new ManagedBox { Value = 35 };
		values[0] = child;
		M68kRuntime.Collect();
		var tail = new int[4];
		tail[0] = 7;
		return ReferenceEquals(values[0], child)
			? ((ManagedBox)values[0]).Value + tail[0]
			: 0;
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
	private static BoxedPair DirectMultiwordBranchReturn(bool second)
	{
		if (second)
			return ReturnConstructedMultiword();
		return ReturnMultiwordArgument(new BoxedPair(19, 23));
	}

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
	public static int IdenticalDirectBodyB(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IdenticalCallCascadeFoldEntry() =>
		IdenticalCallCascadeBodyA(10) + IdenticalCallCascadeBodyB(20);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalCallCascadeBodyA(int value) =>
		IdenticalCallCascadeLeafA(value) * 2;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IdenticalCallCascadeBodyB(int value) =>
		IdenticalCallCascadeLeafB(value) * 2;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalCallCascadeLeafA(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IdenticalCallCascadeLeafB(int value) => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int IdenticalConstructedGenericBodyFoldEntry() =>
		IdenticalConstructedGenericBodyA<int>(10) +
		IdenticalConstructedGenericBodyB<int>(20);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalConstructedGenericBodyA<T>(int value)
		where T : struct => value + 7;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static int IdenticalConstructedGenericBodyB<T>(int value)
		where T : struct => value + 7;

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
