using System.Runtime.CompilerServices;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Compiler.Tests;

public static class CompilerFixtures
{
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

	[M68kImport("fixture.registerAdd")]
	[return: M68kRegister(M68kRegister.D2)]
	public static extern int ImportedAdd(
		[M68kRegister(M68kRegister.D0)] int left,
		[M68kRegister(M68kRegister.D1)] int right);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int CallExecLibrary()
	{
		var first = ExecVectors.Add(10, 11);
		var second = ExecVectors.Add(20, 21);
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
	public static uint CallSdkOpenLibrary() =>
		global::Amiga.Exec.OpenLibrary(0x0000_1800, 37);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CallSdkDosOpen() =>
		global::Amiga.DOS.Open(0x0000_1900, 1005);

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
	public static int IndirectMemoryEntry()
	{
		var bytes = new byte[2];
		var words = new short[2];
		WriteByte(ref bytes[0], 0xF1);
		WriteWord(ref words[0], -1234);
		return ReadUnsignedByte(ref bytes[0]) + ReadSignedWord(ref words[0]);
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
		M68kRuntime.DisposeInt32Array(ref _disposableArray);
		var reused = new int[4];
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
		[M68kRegister(M68kRegister.D1)] int right) =>
		left + right;

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

	public sealed class ManagedNode
	{
		public ManagedBox? Child;
	}
}

public static class StaticInitializationFixtures
{
	private static readonly uint File = global::Amiga.DOS.Open("s:startup-sequence", 1005);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry() => File;
}
