using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Copper68k;
using Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntegerArithmeticSelectionTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;

	public static IEnumerable<object[]> ExecutionCases
	{
		get
		{
			foreach (var method in new[]
			{
				nameof(IntegerArithmeticSelectionFixture.PowerQuotient),
				nameof(IntegerArithmeticSelectionFixture.PowerRemainder),
				nameof(IntegerArithmeticSelectionFixture.HighPowerQuotient),
				nameof(IntegerArithmeticSelectionFixture.MaskedProduct),
				nameof(IntegerArithmeticSelectionFixture.UpperProduct),
				nameof(IntegerArithmeticSelectionFixture.SignedWordProduct),
				nameof(IntegerArithmeticSelectionFixture.NegatedWordQuotient),
				nameof(IntegerArithmeticSelectionFixture.WordRemainder),
				nameof(IntegerArithmeticSelectionFixture.ScaledRemainder),
				nameof(IntegerArithmeticSelectionFixture.QuotientThenRemainder),
				nameof(IntegerArithmeticSelectionFixture.RemainderThenQuotient),
				nameof(IntegerArithmeticSelectionFixture.InterleavedPairs),
				nameof(IntegerArithmeticSelectionFixture.SignedPair),
				nameof(IntegerArithmeticSelectionFixture.NarrowConstantProduct),
				nameof(IntegerArithmeticSelectionFixture.UnboundedDivision),
				nameof(IntegerArithmeticSelectionFixture.MutatedInput),
				nameof(IntegerArithmeticSelectionFixture.PowerPair),
				nameof(IntegerArithmeticSelectionFixture.SignedWordPair),
				nameof(IntegerArithmeticSelectionFixture.SignedWordRemainderFirst),
				nameof(IntegerArithmeticSelectionFixture.UnsignedWordPair),
				nameof(IntegerArithmeticSelectionFixture.OutsideSignedWordQuotient),
				nameof(IntegerArithmeticSelectionFixture.CopiedStructPair),
				nameof(IntegerArithmeticSelectionFixture.FrameFieldMutation),
				nameof(IntegerArithmeticSelectionFixture.OutputHomeStructPair),
				nameof(IntegerArithmeticSelectionFixture.OutputHomeCallMutation),
				nameof(IntegerArithmeticSelectionFixture.AliasedFrameFieldMutation),
				nameof(IntegerArithmeticSelectionFixture.FullProductHalves)
			})
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040)
			})
			foreach (var mode in new[] { M68kPeepholeOptimizationMode.FixedPoint, M68kPeepholeOptimizationMode.Disabled })
				yield return [method, target, model, mode];
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void IntegerResultsAgreeWithClrAcrossBounds(string method, M68kCpuTarget target,
		M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		var result = Compile(method, target, mode);
		var host = typeof(IntegerArithmeticSelectionFixture).GetMethod(method)!;
		foreach (var (left, right) in new (uint, uint)[]
		{
			(0, 0), (1, 1), (49, 50), (50, 49), (32767, 65535), (32768, 32767),
			(65535, 65535), (65536, 65537), (262143, 32768), (0x7fff_ffff, 0x8000_0000),
			(0x8000_0000, 0xffff_ffff), (0xffff_ffcf, 0xffff_ffce), (0xffff_ffff, 0xffff_ffff),
			(0x1234_abcd, 0xfedc_9876)
		})
		{
			var product = (ulong)left * right;
			var expected = method == nameof(IntegerArithmeticSelectionFixture.FullProductHalves)
				? (uint)product ^ (uint)(product >> 32)
				: (uint)host.Invoke(null, [left, right])!;
			Assert.Equal(expected, Execute(result, model, left, right));
		}
	}

	[Theory]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.PowerQuotient), "lsr.l")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.PowerRemainder), "andi.l")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.HighPowerQuotient), "lsr.l")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.MaskedProduct), "mulu.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.UpperProduct), "mulu.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.SignedWordProduct), "muls.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.NegatedWordQuotient), "divs.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.WordRemainder), "divs.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.PowerPair), "lsr.l")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.SignedWordPair), "divs.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.SignedWordRemainderFirst), "divs.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.UnsignedWordPair), "divu.w")]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.FullProductHalves), "mulu.w")]
	public void ProvenOperationsUseNative68000Instructions(string method, string mnemonic)
	{
		var result = Compile(method);
		Assert.Contains(mnemonic, result.Text!);
		Assert.DoesNotContain("_003Adiv_loop", result.Text!);
		Assert.DoesNotContain("_003Amul_loop", result.Text!);
	}

	[Theory]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.QuotientThenRemainder), 1)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.RemainderThenQuotient), 1)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.InterleavedPairs), 2)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.SignedPair), 1)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.CopiedStructPair), 1)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.FrameFieldMutation), 2)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.OutputHomeStructPair), 1)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.OutputHomeCallMutation), 2)]
	[InlineData(nameof(IntegerArithmeticSelectionFixture.AliasedFrameFieldMutation), 2)]
	public void DivRemSharesOneSoftwareLoopForEachInputPair(string method, int expectedLoops)
	{
		var result = Compile(method);
		Assert.Equal(expectedLoops, Regex.Matches(result.Text!, @"^C68K_generated_003Adiv_loop[^\r\n]*:",
			RegexOptions.Multiline).Count);
	}

	[Fact]
	public void BoundedTimeConversionUsesNativeMultiplyAndUnboundedDivisionStaysWide()
	{
		var bounded = Compile(nameof(IntegerArithmeticSelectionFixture.ScaledRemainder));
		Assert.Contains("muls.w", bounded.Text!);
		Assert.DoesNotContain("_003Amul_loop", bounded.Text!);
		var wide = Compile(nameof(IntegerArithmeticSelectionFixture.UnboundedDivision));
		Assert.Contains("_003Adiv_loop", wide.Text!);
		Assert.DoesNotContain("divs.w", wide.Text!);
		var overflow = Compile(nameof(IntegerArithmeticSelectionFixture.OutsideSignedWordQuotient));
		Assert.Contains("_003Adiv_loop", overflow.Text!);
		Assert.DoesNotContain("divs.w", overflow.Text!);
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.FixedPoint)]
	[InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040, M68kPeepholeOptimizationMode.Disabled)]
	public void ArithmeticExceptionsAndPairSignsArePreserved(M68kCpuTarget target,
		M68kCpuModel model, M68kPeepholeOptimizationMode mode)
	{
		foreach (var method in new[]
		{
			nameof(IntegerArithmeticSelectionFixture.UnsignedPairWithCatch),
			nameof(IntegerArithmeticSelectionFixture.SignedPairWithCatch),
			nameof(IntegerArithmeticSelectionFixture.CheckedProductWithCatch)
		})
		{
			var result = Compile(method, target, mode, M68kExceptionMode.Full);
			var host = typeof(IntegerArithmeticSelectionFixture).GetMethod(method)!;
			foreach (var (left, right) in new (uint, uint)[]
			{
				(84, 0), (84, 6), (unchecked((uint)-85), 6), (85, unchecked((uint)-6)),
				(42, 1), (uint.MaxValue, 2), (uint.MaxValue, 0), (65535, 65535)
			})
				Assert.Equal((uint)host.Invoke(null, [left, right])!, Execute(result, model, left, right));
		}
	}

	private static M68kCompilationResult Compile(string method,
		M68kCpuTarget target = M68kCpuTarget.M68000,
		M68kPeepholeOptimizationMode mode = M68kPeepholeOptimizationMode.FixedPoint,
		M68kExceptionMode exceptionMode = M68kExceptionMode.Yolo) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(IntegerArithmeticSelectionFixture).Assembly.Location,
			EntryPoint = $"{typeof(IntegerArithmeticSelectionFixture).FullName}::{method}Entry",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = M68kRuntimeProfile.Resident,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = exceptionMode,
			PeepholeOptimization = mode,
			IncludedExportNames = []
		});

	private static uint Execute(M68kCompilationResult result, M68kCpuModel model, uint left, uint right)
	{
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(StackPointer, ReturnSentinel);
		bus.WriteLong(0x4000, left);
		bus.WriteLong(0x4004, right);
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var register = 0; register < 8; register++) cpu.State.D[register] = 0xa5a5_0000u + (uint)register;
		cpu.State.D[0] = left;
		cpu.State.D[1] = right;
		for (var instruction = 0; instruction < 100_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				return cpu.State.D[0];
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted || cpu.State.Stopped,
				$"{model}: arithmetic stopped at ${cpu.State.ProgramCounter:X8}.");
		}
		throw new InvalidOperationException($"{model}: arithmetic did not return.");
	}
}

public static class IntegerArithmeticSelectionFixture
{
	public static uint PowerQuotientEntry() => PowerQuotient(ReadLeft(), ReadRight());
	public static uint PowerRemainderEntry() => PowerRemainder(ReadLeft(), ReadRight());
	public static uint HighPowerQuotientEntry() => HighPowerQuotient(ReadLeft(), ReadRight());
	public static uint MaskedProductEntry() => MaskedProduct(ReadLeft(), ReadRight());
	public static uint UpperProductEntry() => UpperProduct(ReadLeft(), ReadRight());
	public static uint SignedWordProductEntry() => SignedWordProduct(ReadLeft(), ReadRight());
	public static uint NegatedWordQuotientEntry() => NegatedWordQuotient(ReadLeft(), ReadRight());
	public static uint WordRemainderEntry() => WordRemainder(ReadLeft(), ReadRight());
	public static uint ScaledRemainderEntry() => ScaledRemainder(ReadLeft(), ReadRight());
	public static uint QuotientThenRemainderEntry() => QuotientThenRemainder(ReadLeft(), ReadRight());
	public static uint RemainderThenQuotientEntry() => RemainderThenQuotient(ReadLeft(), ReadRight());
	public static uint InterleavedPairsEntry() => InterleavedPairs(ReadLeft(), ReadRight());
	public static uint SignedPairEntry() => SignedPair(ReadLeft(), ReadRight());
	public static uint NarrowConstantProductEntry() => NarrowConstantProduct(ReadLeft(), ReadRight());
	public static uint UnboundedDivisionEntry() => UnboundedDivision(ReadLeft(), ReadRight());
	public static uint MutatedInputEntry() => MutatedInput(ReadLeft(), ReadRight());
	public static uint PowerPairEntry() => PowerPair(ReadLeft(), ReadRight());
	public static uint SignedWordPairEntry() => SignedWordPair(ReadLeft(), ReadRight());
	public static uint SignedWordRemainderFirstEntry() => SignedWordRemainderFirst(ReadLeft(), ReadRight());
	public static uint UnsignedWordPairEntry() => UnsignedWordPair(ReadLeft(), ReadRight());
	public static uint OutsideSignedWordQuotientEntry() => OutsideSignedWordQuotient(ReadLeft(), ReadRight());
	public static uint CopiedStructPairEntry() => CopiedStructPair(ReadLeft(), ReadRight());
	public static uint FrameFieldMutationEntry() => FrameFieldMutation(ReadLeft(), ReadRight());
	public static uint OutputHomeStructPairEntry() => OutputHomeStructPair(ReadLeft(), ReadRight());
	public static uint OutputHomeCallMutationEntry() => OutputHomeCallMutation(ReadLeft(), ReadRight());
	public static uint AliasedFrameFieldMutationEntry() => AliasedFrameFieldMutation(ReadLeft(), ReadRight());
	public static uint FullProductHalvesEntry() => FullProductHalves(ReadLeft(), ReadRight());
	public static uint UnsignedPairWithCatchEntry() => UnsignedPairWithCatch(ReadLeft(), ReadRight());
	public static uint SignedPairWithCatchEntry() => SignedPairWithCatch(ReadLeft(), ReadRight());
	public static uint CheckedProductWithCatchEntry() => CheckedProductWithCatch(ReadLeft(), ReadRight());
	private static uint ReadLeft() => APTR.ReadUInt32(APTR.FromPointer(0x4000), 0);
	private static uint ReadRight() => APTR.ReadUInt32(APTR.FromPointer(0x4004), 0);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PowerQuotient(uint value, uint ignored) => value / 4;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PowerRemainder(uint value, uint ignored) => value % 16;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint HighPowerQuotient(uint value, uint ignored) => value / 0x8000_0000u;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MaskedProduct(uint left, uint right) => (left & 65535u) * (right & 65535u);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UpperProduct(uint left, uint right) => (left >> 16) * (right >> 16);
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedWordProduct(uint left, uint right) => unchecked((uint)((short)left * (short)right));
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NegatedWordQuotient(uint value, uint ignored) => unchecked((uint)((short)-(short)value / 6));
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint WordRemainder(uint value, uint ignored) => unchecked((uint)((short)value % 6));
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint ScaledRemainder(uint value, uint ignored) => unchecked((uint)(((int)value % 50) * 20_000_000));
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint QuotientThenRemainder(uint value, uint ignored)
	{
		uint quotient = value / 50;
		uint remainder = value % 50;
		return quotient ^ (remainder << 24);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint RemainderThenQuotient(uint value, uint ignored)
	{
		uint remainder = value % 50;
		uint quotient = value / 50;
		return quotient ^ (remainder << 24);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint InterleavedPairs(uint value, uint ignored)
	{
		uint minutes = value / 3000;
		uint days = minutes / 1440;
		uint minuteRemainder = minutes % 1440;
		uint tickRemainder = value % 3000;
		return days ^ (minuteRemainder << 8) ^ (tickRemainder << 20);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedPair(uint value, uint ignored)
	{
		int signed = unchecked((int)value);
		int quotient = signed / 50;
		int remainder = signed % 50;
		return unchecked((uint)(quotient ^ (remainder << 24)));
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint NarrowConstantProduct(uint value, uint ignored) => NarrowProduct(ushort.MaxValue, (ushort)value);
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint NarrowProduct(ushort left, ushort right) => (uint)left * right;
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnboundedDivision(uint value, uint ignored) => unchecked((uint)((int)value / 6));
	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint MutatedInput(uint value, uint ignored)
	{
		uint original = value;
		uint quotient = Read(ref original) / 50;
		original = unchecked(original + 1);
		return quotient ^ ((Read(ref original) % 50) << 24);
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Read(ref uint value) => value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint PowerPair(uint value, uint ignored)
	{
		uint remainder = value % 32;
		uint quotient = value / 32;
		return quotient ^ (remainder << 27);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedWordPair(uint value, uint ignored)
	{
		int narrowed = (short)value;
		int quotient = narrowed / -6;
		int remainder = narrowed % -6;
		return unchecked((uint)(quotient ^ (remainder << 24)));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedWordRemainderFirst(uint value, uint ignored)
	{
		int narrowed = (short)value;
		int remainder = narrowed % 6;
		int quotient = narrowed / 6;
		return unchecked((uint)(quotient ^ (remainder << 24)));
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnsignedWordPair(uint value, uint ignored)
	{
		uint narrowed = value & 262143;
		uint quotient = narrowed / 6;
		uint remainder = narrowed % 6;
		return quotient ^ (remainder << 24);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint OutsideSignedWordQuotient(uint value, uint ignored) =>
		unchecked((uint)((int)(value & 262143) / 6));

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CopiedStructPair(uint value, uint right)
	{
		var packet = CreateDatePacket(value, right);
		uint quotient = packet.Stamp.Minutes / 60;
		uint remainder = packet.Stamp.Minutes % 60;
		return quotient ^ (remainder << 24);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static DatePacket CreateDatePacket(uint minutes, uint other) =>
		new(new DateStamp { Days = other, Minutes = minutes, Ticks = other });
	private readonly struct DatePacket(DateStamp stamp)
	{
		public readonly DateStamp Stamp = stamp;
	}
	private struct DateStamp
	{
		public uint Days;
		public uint Minutes;
		public uint Ticks;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint FrameFieldMutation(uint value, uint right)
	{
		var stamp = new DateStamp { Days = right, Minutes = value, Ticks = right };
		uint quotient = stamp.Minutes / 60;
		stamp.Minutes = unchecked(stamp.Minutes + 1);
		return quotient ^ ((stamp.Minutes % 60) << 24);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint OutputHomeStructPair(uint value, uint right)
	{
		ProduceArithmeticOutput(value, out var seconds);
		var stamp = new DateStamp
		{
			Days = right,
			Minutes = seconds / 60,
			Ticks = seconds % 60
		};
		return SummarizeArithmeticStamp(stamp);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint OutputHomeCallMutation(uint value, uint right)
	{
		ProduceArithmeticOutput(value, out var seconds);
		var stamp = new DateStamp { Days = right, Minutes = seconds / 60 };
		ProduceArithmeticOutput(unchecked(value + 1), out seconds);
		stamp.Ticks = seconds % 60;
		return SummarizeArithmeticStamp(stamp);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint AliasedFrameFieldMutation(uint value, uint right)
	{
		ProduceArithmeticStamp(value, right, out var stamp);
		uint quotient = stamp.Minutes / 60;
		ref var alias = ref stamp;
		alias.Minutes = unchecked(alias.Minutes + 1);
		return quotient ^ ((stamp.Minutes % 60) << 24) ^ right;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ProduceArithmeticOutput(uint value, out uint output) => output = value;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void ProduceArithmeticStamp(uint value, uint right, out DateStamp stamp) =>
		stamp = new DateStamp { Days = right, Minutes = value, Ticks = right };

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint SummarizeArithmeticStamp(DateStamp stamp) =>
		stamp.Minutes ^ (stamp.Ticks << 24) ^ stamp.Days;

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint FullProductHalves(uint left, uint right)
	{
		uint leftLow = left & 65535;
		uint leftHigh = left >> 16;
		uint rightLow = right & 65535;
		uint rightHigh = right >> 16;
		uint lowProduct = leftLow * rightLow;
		uint firstCross = leftLow * rightHigh;
		uint secondCross = leftHigh * rightLow;
		uint middle = (lowProduct >> 16) + (firstCross & 65535) + (secondCross & 65535);
		uint low = (lowProduct & 65535) | (middle << 16);
		uint high = leftHigh * rightHigh + (firstCross >> 16) + (secondCross >> 16) + (middle >> 16);
		return low ^ high;
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint UnsignedPairWithCatch(uint left, uint right)
	{
		try
		{
			uint quotient = left / right;
			uint remainder = left % right;
			return quotient ^ (remainder << 24);
		}
		catch (DivideByZeroException) { return 0xdec0de; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint SignedPairWithCatch(uint left, uint right)
	{
		try
		{
			int signed = unchecked((int)left);
			int divisor = unchecked((int)right);
			int remainder = signed % divisor;
			int quotient = signed / divisor;
			return unchecked((uint)(quotient ^ (remainder << 24)));
		}
		catch (DivideByZeroException) { return 0xdec0de; }
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static uint CheckedProductWithCatch(uint left, uint right)
	{
		try { return checked(left * right); }
		catch (OverflowException) { return 0xdec0de; }
	}
}
