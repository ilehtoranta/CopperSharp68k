using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NarrowInstanceFieldBoundaryTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	public static IEnumerable<object[]> ExecutionCases
	{
		get
		{
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040)
			})
			foreach (var mode in new[]
			{
				M68kPeepholeOptimizationMode.FixedPoint,
				M68kPeepholeOptimizationMode.Disabled
			})
			foreach (var profile in new[]
			{
				M68kRuntimeProfile.Freestanding,
				M68kRuntimeProfile.Resident
			})
				yield return [target, model, mode, profile];
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void DirectZeroStoresPreservePaddingAndNarrowValuesAgree(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile)
	{
		Assert.Equal(24, Unsafe.SizeOf<NarrowInstanceFieldBoundaryFixture.Payload>());
		var hostResult = NarrowInstanceFieldBoundaryFixture.Entry();
		Assert.Equal(0u, hostResult);
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(NarrowInstanceFieldBoundaryFixture).Assembly.Location,
			EntryPoint = $"{typeof(NarrowInstanceFieldBoundaryFixture).FullName}::{nameof(NarrowInstanceFieldBoundaryFixture.Entry)}",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = profile,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = mode,
			IncludedExportNames = []
		});
		Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
		Assert.Empty(result.NativeCompatibility.RuntimeFeatures);
		Assert.Empty(result.NativeCompatibility.RuntimeHelpers);
		Assert.Empty(result.NativeCompatibility.ExternalNativeTargets);

		var memory = new TestBus();
		result.Code.CopyTo(memory.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			memory.WriteLong(address, memory.ReadLong(address) + LoadAddress);
		}
		var image = memory.Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray();
		memory.WriteLong(StackPointer, ReturnSentinel);
		var bus = new ProtectedImageBus(memory, LoadAddress, checked((uint)result.Code.Length));
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var register = 0; register < 8; register++)
			cpu.State.D[register] = 0xA5A5_0000u + unchecked((uint)register);
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				Assert.True(cpu.State.D[0] == hostResult,
					$"{model}/{mode}/{profile}: mismatched boundary mask ${cpu.State.D[0]:X8}.");
				Assert.Equal(image, memory.Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray());
				return;
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted || cpu.State.Stopped,
				$"{model}/{mode}/{profile} stopped at ${cpu.State.ProgramCounter:X8}.");
		}
		Assert.Fail($"{model}/{mode}/{profile} did not return.");
	}

	// The ordinary compiler TestBus owns storage. This adapter only prohibits
	// writes to the one relocated native image, including transient writes that
	// an end-of-run byte comparison alone could miss. It supplies no gateways.
	private sealed class ProtectedImageBus(TestBus memory, uint imageStart, uint imageBytes) : IM68kBus
	{
		public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) =>
			memory.ReadByte(address, ref cycle, kind);
		public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) =>
			memory.ReadWord(address, ref cycle, kind);
		public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) =>
			memory.ReadLong(address, ref cycle, kind);
		public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind)
		{
			AssertWritable(address, 1);
			memory.WriteByte(address, value, ref cycle, kind);
		}
		public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind)
		{
			AssertWritable(address, 2);
			memory.WriteWord(address, value, ref cycle, kind);
		}
		public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind)
		{
			AssertWritable(address, 4);
			memory.WriteLong(address, value, ref cycle, kind);
		}
		public void ResetExternalDevices(long cycle) => memory.ResetExternalDevices(cycle);
		public bool HasHostGateway(uint address) => false;
		public bool TryInvokeHostGateway(uint instructionProgramCounter, uint token, M68kCpuState state) => false;
		private void AssertWritable(uint address, uint size)
		{
			if ((ulong)address < (ulong)imageStart + imageBytes &&
				(ulong)address + size > imageStart)
				throw new InvalidOperationException($"Native image write at ${address:X8}.");
		}
	}
}

public static class NarrowInstanceFieldBoundaryFixture
{
	public enum ByteValue : byte { Selected = 0xE5 }
	public enum SignedWordValue : short { Selected = -23333 }

	// CLR host offsets and the compiler's existing four-byte scalar slots
	// coincide. Padding is deliberately observable without changing that ABI.
	[StructLayout(LayoutKind.Explicit, Size = 24)]
	public struct Payload
	{
		[FieldOffset(0)] public bool Enabled;
		[FieldOffset(4)] public char Character;
		[FieldOffset(8)] public uint FirstGuard;
		[FieldOffset(12)] public ByteValue Byte;
		[FieldOffset(16)] public SignedWordValue SignedWord;
		[FieldOffset(20)] public uint LastGuard;
	}

	public static unsafe uint Entry()
	{
		if (sizeof(Payload) != 24) return 0x8000_0000;
		var value = default(Payload);
		value.FirstGuard = 0x1357_9BDF;
		value.LastGuard = 0x2468_ACE0;
		Write(ref value.Enabled, true);
		Write(ref value.Character, '\uF123');
		Write(ref value.Byte, ByteValue.Selected);
		Write(ref value.SignedWord, SignedWordValue.Selected);
		var bytes = (byte*)&value;
		PoisonPadding(bytes);
		var failed = CheckHighValues(value);
		if (!GetEnabled(value)) failed |= 0x0010;
		if (GetCharacter(value) != 0xF123) failed |= 0x0020;
		if (GetByte(value) != 0xE5) failed |= 0x0040;
		if (GetSignedWord(value) != -23333) failed |= 0x0080;

		Clear(ref value);
		if (Read(ref value.Enabled)) failed |= 0x0100;
		if (Read(ref value.Character) != 0) failed |= 0x0200;
		if (Read(ref value.Byte) != 0) failed |= 0x0400;
		if (Read(ref value.SignedWord) != 0) failed |= 0x0800;
		return failed | CheckClearedValues(value) | CheckGuards(value) | CheckPadding(bytes);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CheckHighValues(Payload value)
	{
		uint failed = 0;
		if (!value.Enabled) failed |= 1;
		if (value.Character != 0xF123) failed |= 2;
		if ((int)value.Byte != 0xE5) failed |= 4;
		if ((int)value.SignedWord != -23333) failed |= 8;
		return failed;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Clear(ref Payload value)
	{
		value.Enabled = false;
		value.Character = '\0';
		value.Byte = 0;
		value.SignedWord = 0;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CheckClearedValues(Payload value) =>
		!value.Enabled && value.Character == 0 && value.Byte == 0 && value.SignedWord == 0
			? 0u : 0x1000u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CheckGuards(Payload value) =>
		value.FirstGuard == 0x1357_9BDF && value.LastGuard == 0x2468_ACE0 ? 0u : 0x2000u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe void PoisonPadding(byte* bytes)
	{
		// Only unused bytes within the explicit 24-byte value are touched.
		// Boolean values remain canonical true/false; high bits are exercised
		// through char and enum fields rather than a noncanonical bool byte.
		bytes[1] = 0xA1;
		bytes[2] = 0xB2;
		bytes[3] = 0xC3;
		bytes[6] = 0xD6;
		bytes[7] = 0xE7;
		bytes[13] = 0xAD;
		bytes[14] = 0xBE;
		bytes[15] = 0xCF;
		bytes[18] = 0xD8;
		bytes[19] = 0xE9;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static unsafe uint CheckPadding(byte* bytes)
	{
		uint failed = 0;
		if (bytes[1] != 0xA1) failed |= 0x0001_0000;
		if (bytes[2] != 0xB2) failed |= 0x0002_0000;
		if (bytes[3] != 0xC3) failed |= 0x0004_0000;
		if (bytes[6] != 0xD6) failed |= 0x0008_0000;
		if (bytes[7] != 0xE7) failed |= 0x0010_0000;
		if (bytes[13] != 0xAD) failed |= 0x0020_0000;
		if (bytes[14] != 0xBE) failed |= 0x0040_0000;
		if (bytes[15] != 0xCF) failed |= 0x0080_0000;
		if (bytes[18] != 0xD8) failed |= 0x0100_0000;
		if (bytes[19] != 0xE9) failed |= 0x0200_0000;
		return failed;
	}

	[MethodImpl(MethodImplOptions.NoInlining)] private static bool GetEnabled(Payload value) => value.Enabled;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetCharacter(Payload value) => value.Character;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetByte(Payload value) => (int)value.Byte;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetSignedWord(Payload value) => (int)value.SignedWord;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref bool target, bool value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref char target, char value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref ByteValue target, ByteValue value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref SignedWordValue target, SignedWordValue value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static bool Read(ref bool value) => value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref char value) => value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref ByteValue value) => (int)value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref SignedWordValue value) => (int)value;
}
