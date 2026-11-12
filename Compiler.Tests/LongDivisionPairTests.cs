using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class LongDivisionPairTests
{
	public static IEnumerable<object[]> PairShapes
	{
		get
		{
			foreach (var target in new[] { M68kCpuTarget.M68020, M68kCpuTarget.M68040, M68kCpuTarget.M68060 })
			foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
			foreach (var (method, count) in new (string, int)[]
			{
				(nameof(IntegerArithmeticSelectionFixture.QuotientThenRemainder), 1),
				(nameof(IntegerArithmeticSelectionFixture.RemainderThenQuotient), 1),
				(nameof(IntegerArithmeticSelectionFixture.InterleavedPairs), 2),
				(nameof(IntegerArithmeticSelectionFixture.SignedPair), 1),
				(nameof(IntegerArithmeticSelectionFixture.CopiedStructPair), 1),
				(nameof(IntegerArithmeticSelectionFixture.FrameFieldMutation), 2),
				(nameof(IntegerArithmeticSelectionFixture.OutputHomeStructPair), 1),
				(nameof(IntegerArithmeticSelectionFixture.OutputHomeCallMutation), 2),
				(nameof(IntegerArithmeticSelectionFixture.AliasedFrameFieldMutation), 2),
			}) yield return [method, count, target, mode];
		}
	}

	[Theory]
	[MemberData(nameof(PairShapes))]
	public void LongDivisionCombinesOnlyMatchingInputs(string method, int expected,
		M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		var result = Compile(typeof(IntegerArithmeticSelectionFixture), method + "Entry", target, mode);
		Assert.Equal(expected, DivisionCount(result));
	}

	public static IEnumerable<object[]> ExecutionCases
	{
		get
		{
			foreach (var target in new[] { M68kCpuTarget.M68020, M68kCpuTarget.M68040, M68kCpuTarget.M68060 })
			foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
			foreach (var signed in new[] { false, true })
			foreach (var remainderFirst in new[] { false, true })
				yield return [target, mode, signed, remainderFirst];
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68040, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68060, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68060, M68kPeepholeOptimizationMode.FixedPoint)]
	public void RemainderBranchesUseRemainderConditionsAfterNativeExchange(M68kCpuTarget target, M68kPeepholeOptimizationMode mode)
	{
		foreach (var signed in new[] { false, true })
		{
			var result = Compile(typeof(LongDivisionPairFixture), signed ? "SignedRemainderBranch" : "UnsignedRemainderBranch", target, mode);
			Assert.Equal(1, DivisionCount(result));
			var bus = Load(result);
			foreach (uint value in new uint[] { 0, 1, 49, 50, 51, 100, 101, 0x80000000, uint.MaxValue })
			{
				var quotient = signed ? unchecked((uint)((int)value / 50)) : value / 50;
				var remainder = signed ? unchecked((uint)((int)value % 50)) : value % 50;
				var taken = signed ? (int)remainder < 0 : remainder == 0;
				Assert.Equal(taken ? 1u : 0u, Execute(result, bus, target, value, 50));
				Assert.Equal(taken ? quotient : remainder, bus.ReadLong(0x4010));
			}
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void LongDivisionPairReturnsBothFullWidthResults(M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode, bool signed, bool remainderFirst)
	{
		var method = (signed ? "Signed" : "Unsigned") + (remainderFirst ? "RemainderFirst" : "QuotientFirst");
		var result = Compile(typeof(LongDivisionPairFixture), method, target, mode);
		Assert.Equal(1, DivisionCount(result));
		var bus = Load(result);
		foreach (var (value, divisor) in Vectors())
		{
			// Exercise representable signed quotients here. Divide-by-zero paths
			// are covered by ArithmeticExceptionsAndPairSignsArePreserved.
			if (signed && value == 0x80000000 && divisor == uint.MaxValue) continue;
			var quotient = signed ? unchecked((uint)((int)value / (int)divisor)) : value / divisor;
			var remainder = signed ? unchecked((uint)((int)value % (int)divisor)) : value % divisor;
			var actual = Execute(result, bus, target, value, divisor);
			Assert.True(quotient == actual, $"{target}/{method}: {value:X8}/{divisor:X8}: quotient {actual:X8}, expected {quotient:X8}");
			Assert.Equal(remainder, bus.ReadLong(0x4010));
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020)]
	[InlineData(M68kCpuTarget.M68040)]
	[InlineData(M68kCpuTarget.M68060)]
	public void ConditionalAndDifferentSignednessDivisionsRemainSeparate(M68kCpuTarget target)
	{
		foreach (var method in new[] { nameof(LongDivisionPairFixture.Conditional), nameof(LongDivisionPairFixture.MixedSignedness) })
		{
			var result = Compile(typeof(LongDivisionPairFixture), method, target, M68kPeepholeOptimizationMode.FixedPoint);
			Assert.Equal(2, DivisionCount(result));
			var bus = Load(result);
			if (method == nameof(LongDivisionPairFixture.Conditional))
			{
				Assert.Equal(0u, Execute(result, bus, target, uint.MaxValue, 0));
				Assert.Equal(0u, bus.ReadLong(0x4010));
				Assert.Equal(2u, Execute(result, bus, target, 100, 50));
				Assert.Equal(0u, bus.ReadLong(0x4010));
				Assert.Equal(2u, Execute(result, bus, target, 101, 50));
				Assert.Equal(1u, bus.ReadLong(0x4010));
			}
			else
			{
				Assert.Equal(uint.MaxValue / 50u, Execute(result, bus, target, uint.MaxValue, 50));
				Assert.Equal(uint.MaxValue, bus.ReadLong(0x4010)); // signed -1 % 50
			}
		}
	}

	private static IEnumerable<(uint Value, uint Divisor)> Vectors()
	{
		foreach (var value in new uint[] { 0, 1, 2, 49, 50, 65535, 65536, 0x7fffffff, 0x80000000, 0x80000001, uint.MaxValue })
		foreach (var divisor in new uint[] { 1, 2, 3, 50, 72, 488, 512, 65535, 65536, 0x7fffffff, 0x80000000, uint.MaxValue })
			yield return (value, divisor);
		var random = new Random(0xD1720);
		for (var i = 0; i < 256; i++)
			yield return ((uint)random.NextInt64(0, 1L << 32), (uint)random.NextInt64(1, 1L << 32));
	}

	private static M68kCompilationResult Compile(Type fixture, string method, M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode) => AmigaM68kCompiler.Compile(new M68kCompilationRequest
	{
		AssemblyPath = fixture.Assembly.Location, EntryPoint = $"{fixture.FullName}::{method}",
		Cpu = target, OutputFormat = M68kOutputFormat.Assembly,
		RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
		ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = mode, IncludedExportNames = []
	});

	private static int DivisionCount(M68kCompilationResult result) => Regex.Matches(result.Text!,
		@"^\s+div[su]\.l\s", RegexOptions.Multiline).Count;

	private static TestBus Load(M68kCompilationResult result)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory, 0x10000);
		foreach (var relocation in result.Relocations)
		{
			var address = 0x10000u + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + 0x10000);
		}
		return bus;
	}

	private static uint Execute(M68kCompilationResult result, TestBus bus, M68kCpuTarget target, uint value, uint divisor)
	{
		bus.WriteLong(0x4000, value); bus.WriteLong(0x4004, divisor);
		bus.WriteLong(0x400c, 0x19283746); bus.WriteLong(0x4014, 0xA1B2C3D4);
		bus.WriteLong(0x80000, 0x1000);
		// As elsewhere in this suite, the compatible integer subset of 060
		// output executes on the 040 model; this is not native 060 timing coverage.
		using var cpu = M68kCoreFactory.Default.Create(target == M68kCpuTarget.M68020 ? M68kCpuModel.M68020 : M68kCpuModel.M68040, bus);
		cpu.Reset(0x10000 + result.EntryPoint, 0x80000);
		for (var register = 0; register < 8; register++) cpu.State.D[register] = 0xA5A50000u + (uint)register;
		for (var step = 0; step < 100_000 && cpu.State.ProgramCounter != 0x1000; step++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted || cpu.State.Stopped);
		}
		Assert.Equal(0x1000u, cpu.State.ProgramCounter);
		Assert.Equal(0x80004u, cpu.State.A[7]);
		Assert.Equal(0x19283746u, bus.ReadLong(0x400c));
		Assert.Equal(0xA1B2C3D4u, bus.ReadLong(0x4014));
		return cpu.State.D[0];
	}
}

public static class LongDivisionPairFixture
{
	private static uint Value() => APTR.ReadUInt32(APTR.FromPointer(0x4000), 0);
	private static uint Divisor() => APTR.ReadUInt32(APTR.FromPointer(0x4004), 0);
	private static void Store(uint remainder) => APTR.WriteUInt32(APTR.FromPointer(0x4010), 0, remainder);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnsignedQuotientFirst()
	{
		var value = Value(); var divisor = Divisor();
		var quotient = value / divisor; var remainder = value % divisor;
		Store(remainder); return quotient;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnsignedRemainderFirst()
	{
		var value = Value(); var divisor = Divisor();
		var remainder = value % divisor; var quotient = value / divisor;
		Store(remainder); return quotient;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedQuotientFirst()
	{
		var value = unchecked((int)Value()); var divisor = unchecked((int)Divisor());
		var quotient = value / divisor; var remainder = value % divisor;
		Store(unchecked((uint)remainder)); return unchecked((uint)quotient);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedRemainderFirst()
	{
		var value = unchecked((int)Value()); var divisor = unchecked((int)Divisor());
		var remainder = value % divisor; var quotient = value / divisor;
		Store(unchecked((uint)remainder)); return unchecked((uint)quotient);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint Conditional()
	{
		var value = Value(); var divisor = Divisor();
		Store(0);
		if (divisor == 0) return 0;
		var quotient = value / divisor;
		if ((value & 1) != 0) Store(value % divisor);
		return quotient;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MixedSignedness()
	{
		var value = Value(); var divisor = Divisor();
		var quotient = value / divisor;
		var remainder = unchecked((int)value) % unchecked((int)divisor);
		Store(unchecked((uint)remainder)); return quotient;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnsignedRemainderBranch()
	{
		var value = Value(); var divisor = Divisor();
		var remainder = value % divisor; var quotient = value / divisor;
		if (remainder == 0) { Store(quotient); return 1; }
		Store(remainder); return 0;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedRemainderBranch()
	{
		var value = unchecked((int)Value()); var divisor = unchecked((int)Divisor());
		var remainder = value % divisor; var quotient = value / divisor;
		if (remainder < 0) { Store(unchecked((uint)quotient)); return 1; }
		Store(unchecked((uint)remainder)); return 0;
	}
}
