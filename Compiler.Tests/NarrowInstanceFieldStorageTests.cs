using System.Runtime.CompilerServices;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NarrowInstanceFieldStorageTests
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
			foreach (var entry in new[]
			{
				nameof(NarrowInstanceFieldStorageFixture.IndirectWritesThenValueCopy),
				nameof(NarrowInstanceFieldStorageFixture.DirectWritesThenFieldReferences),
				nameof(NarrowInstanceFieldStorageFixture.IndirectWritesThenSimpleGetters)
			})
				yield return [target, model, mode, profile, entry];
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void FieldAndAddressAccessAgreeAcrossAggregateCalls(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile,
		string entry)
	{
		var hostResult = (uint)typeof(NarrowInstanceFieldStorageFixture)
			.GetMethod(entry)!.Invoke(null, null)!;
		Assert.Equal(0u, hostResult);
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(NarrowInstanceFieldStorageFixture).Assembly.Location,
			EntryPoint = $"{typeof(NarrowInstanceFieldStorageFixture).FullName}::{entry}",
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

		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		var loadedImage = bus.Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray();
		bus.WriteLong(StackPointer, ReturnSentinel);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				Assert.True(cpu.State.D[0] == hostResult,
					$"{entry}/{model}/{mode}/{profile}: mismatched field mask ${cpu.State.D[0]:X8}.");
				Assert.Equal(loadedImage,
					bus.Memory.AsSpan((int)LoadAddress, result.Code.Length).ToArray());
				return;
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{entry}/{model}/{mode}/{profile} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		Assert.Fail($"{entry}/{model}/{mode}/{profile} did not return.");
	}
}

public static class NarrowInstanceFieldStorageFixture
{
	public enum ByteValue : byte { Selected = 0xE5 }
	public enum SignedByteValue : sbyte { Selected = -105 }
	public enum WordValue : ushort { Selected = 0xF123 }
	public enum SignedWordValue : short { Selected = -23333 }

	public struct Payload
	{
		public uint Prefix0, Prefix1, Prefix2, Prefix3;
		public ByteValue Byte;
		public SignedByteValue SignedByte;
		public WordValue Word;
		public SignedWordValue SignedWord;
		public byte PrimitiveByte;
		public sbyte PrimitiveSignedByte;
		public ushort PrimitiveWord;
		public short PrimitiveSignedWord;
		public uint Suffix;
	}

	public static uint IndirectWritesThenValueCopy()
	{
		var value = default(Payload);
		SetGuards(ref value);
		SetFieldsThroughReferences(ref value);
		return ValidateCopy(value);
	}

	public static uint DirectWritesThenFieldReferences()
	{
		var value = default(Payload);
		SetGuards(ref value);
		SetFieldsDirectly(ref value);
		uint failed = 0;
		if (Read(ref value.Byte) != 0xE5) failed |= 1;
		if (Read(ref value.SignedByte) != -105) failed |= 2;
		if (Read(ref value.Word) != 0xF123) failed |= 4;
		if (Read(ref value.SignedWord) != -23333) failed |= 8;
		if (Read(ref value.PrimitiveByte) != 0xA7) failed |= 16;
		if (Read(ref value.PrimitiveSignedByte) != -89) failed |= 32;
		if (Read(ref value.PrimitiveWord) != 0xB234) failed |= 64;
		if (Read(ref value.PrimitiveSignedWord) != -19234) failed |= 128;
		return failed | CheckGuards(value);
	}

	public static uint IndirectWritesThenSimpleGetters()
	{
		var value = default(Payload);
		SetGuards(ref value);
		SetFieldsThroughReferences(ref value);
		uint failed = 0;
		if (GetByte(value) != 0xE5) failed |= 1;
		if (GetSignedByte(value) != -105) failed |= 2;
		if (GetWord(value) != 0xF123) failed |= 4;
		if (GetSignedWord(value) != -23333) failed |= 8;
		return failed | CheckGuards(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetGuards(ref Payload value)
	{
		value.Prefix0 = 0x12345678;
		value.Prefix1 = 0xAABBCCDD;
		value.Prefix2 = 0x87654321;
		value.Prefix3 = 0xDDCCBBAA;
		value.Suffix = 0x13579BDF;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint CheckGuards(Payload value) =>
		value.Prefix0 == 0x12345678 && value.Prefix1 == 0xAABBCCDD &&
		value.Prefix2 == 0x87654321 && value.Prefix3 == 0xDDCCBBAA &&
		value.Suffix == 0x13579BDF ? 0u : 256u;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ValidateCopy(Payload value)
	{
		uint failed = 0;
		if ((int)value.Byte != 0xE5) failed |= 1;
		if ((int)value.SignedByte != -105) failed |= 2;
		if ((int)value.Word != 0xF123) failed |= 4;
		if ((int)value.SignedWord != -23333) failed |= 8;
		if ((int)value.PrimitiveByte != 0xA7) failed |= 16;
		if ((int)value.PrimitiveSignedByte != -89) failed |= 32;
		if ((int)value.PrimitiveWord != 0xB234) failed |= 64;
		if ((int)value.PrimitiveSignedWord != -19234) failed |= 128;
		return failed | CheckGuards(value);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetFieldsDirectly(ref Payload value)
	{
		value.Byte = ByteValue.Selected;
		value.SignedByte = SignedByteValue.Selected;
		value.Word = WordValue.Selected;
		value.SignedWord = SignedWordValue.Selected;
		value.PrimitiveByte = 0xA7;
		value.PrimitiveSignedByte = -89;
		value.PrimitiveWord = 0xB234;
		value.PrimitiveSignedWord = -19234;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void SetFieldsThroughReferences(ref Payload value)
	{
		Write(ref value.Byte, ByteValue.Selected);
		Write(ref value.SignedByte, SignedByteValue.Selected);
		Write(ref value.Word, WordValue.Selected);
		Write(ref value.SignedWord, SignedWordValue.Selected);
		Write(ref value.PrimitiveByte, 0xA7);
		Write(ref value.PrimitiveSignedByte, -89);
		Write(ref value.PrimitiveWord, 0xB234);
		Write(ref value.PrimitiveSignedWord, -19234);
	}

	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetByte(Payload value) => (int)value.Byte;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetSignedByte(Payload value) => (int)value.SignedByte;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetWord(Payload value) => (int)value.Word;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int GetSignedWord(Payload value) => (int)value.SignedWord;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref ByteValue target, ByteValue value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref SignedByteValue target, SignedByteValue value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref WordValue target, WordValue value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref SignedWordValue target, SignedWordValue value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref byte target, byte value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref sbyte target, sbyte value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref ushort target, ushort value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static void Write(ref short target, short value) => target = value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref ByteValue value) => (int)value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref SignedByteValue value) => (int)value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref WordValue value) => (int)value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref SignedWordValue value) => (int)value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref byte value) => value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref sbyte value) => value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref ushort value) => value;
	[MethodImpl(MethodImplOptions.NoInlining)] private static int Read(ref short value) => value;
}
