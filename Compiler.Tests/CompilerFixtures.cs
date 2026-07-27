using System.Runtime.CompilerServices;
using Amiga;
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
		return global::Amiga.DOS.Printf(CString.FromLiteral("value: %ld\n"), value);
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
		var file = global::Amiga.DOS.Open(0x0000_1900, 1005);
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
		[M68kRegister(M68kRegister.D1)] int right) =>
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

	public sealed class ManagedNode
	{
		public ManagedBox? Child;
	}

	public sealed class ManagedChainNode
	{
		public ManagedChainNode? Next;
		public int Value;
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
	private static readonly BPTR? File = global::Amiga.DOS.Open("s:startup-sequence", 1005);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Entry() => File.HasValue ? File.Value.Raw : 0u;
}
