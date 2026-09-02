using System.Runtime.CompilerServices;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class ReadOnlyAggregateReturnForwardingExecutionTests
{
	public static IEnumerable<object[]> Cases()
	{
		foreach (var scenario in new[] { "Safe", "ProducerAlias", "Writable" })
		foreach (var peephole in new[]
		{
			M68kPeepholeOptimizationMode.Disabled,
			M68kPeepholeOptimizationMode.FixedPoint
		})
		{
			yield return [scenario, peephole];
		}
	}

	[Theory, MemberData(nameof(Cases))]
	public void ReadOnlyProofPreservesResultsStackRegistersAndImmutableCode(
		string scenario,
		M68kPeepholeOptimizationMode peephole)
	{
		var control = Compile(
			scenario,
			M68kCpuTarget.M68000,
			M68kRuntimeProfile.Rom,
			peephole,
			enabled: false);
		var candidate = Compile(
			scenario,
			M68kCpuTarget.M68000,
			M68kRuntimeProfile.Rom,
			peephole,
			enabled: true);
		if (scenario != "Writable")
		{
			Assert.True(
				candidate.Code.Length < control.Code.Length,
				$"No encoded gain for {scenario}: " +
				$"{control.Code.Length} -> {candidate.Code.Length}.");
		}
		else
		{
			Assert.True(candidate.Code.Length <= control.Code.Length);
		}

		foreach (var seed in new uint[]
		{
			0,
			1,
			41,
			0x7fffffff,
			0x80000000,
			0xffffffff
		})
		foreach (var selector in new uint[] { 0, 1, 0xffffffff })
		foreach (var residue in new uint[] { 0, 2 })
		{
			var before = Execute(control, scenario, seed, selector, residue);
			var after = Execute(candidate, scenario, seed, selector, residue);
			Assert.Equal(Expected(scenario, seed, selector), after.Value);
			Assert.Equal(before.Value, after.Value);
			Assert.Equal(before.GuestBytes, after.GuestBytes);
			Assert.True(
				after.Cycles <= before.Cycles,
				$"{scenario}/{peephole}/{seed:X8}/{selector:X8}/" +
				$"SP%4={residue}: {before.Cycles} -> {after.Cycles} cycles.");
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020, M68kRuntimeProfile.Rom)]
	[InlineData(M68kCpuTarget.M68040, M68kRuntimeProfile.Rom)]
	[InlineData(M68kCpuTarget.M68000, M68kRuntimeProfile.Freestanding)]
	public void NonEligibleTargetsRetainTheOriginalLowering(
		M68kCpuTarget cpu,
		M68kRuntimeProfile profile)
	{
		var control = Compile(
			"Safe",
			cpu,
			profile,
			M68kPeepholeOptimizationMode.FixedPoint,
			enabled: false);
		var candidate = Compile(
			"Safe",
			cpu,
			profile,
			M68kPeepholeOptimizationMode.FixedPoint,
			enabled: true);
		Assert.Equal(control.Code, candidate.Code);
	}

	private static uint Expected(string scenario, uint seed, uint selector) =>
		unchecked(scenario switch
		{
			"Safe" => seed * 7 + 22,
			"ProducerAlias" => seed * 6 + selector + 24,
			"Writable" => seed * 6 + selector + 21,
			_ => throw new ArgumentOutOfRangeException(nameof(scenario))
		});

	private static M68kCompilationResult Compile(
		string scenario,
		M68kCpuTarget cpu,
		M68kRuntimeProfile profile,
		M68kPeepholeOptimizationMode peephole,
		bool enabled) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(ReadOnlyAggregateReturnForwardingFixtures).Assembly.Location,
			EntryPoint = typeof(ReadOnlyAggregateReturnForwardingFixtures).FullName +
				"::Entry" + scenario,
			Cpu = cpu,
			OutputFormat = M68kOutputFormat.Assembly,
			RuntimeProfile = profile,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = peephole,
			IncludedExportNames = [],
			RomSizeOptimizations = new M68kRomSizeOptions
			{
				ElideUnusedRegisterArguments = false,
				ShareReturnSequences = false,
				ReuseIncomingArgumentHomes = false,
				ClusterInternalCalls = false,
				InlineSingleUseMethods = false,
				ForwardReadOnlyAggregateLocals = enabled
			}
		});

	private sealed record Observation(uint Value, byte[] GuestBytes, long Cycles);

	private static Observation Execute(
		M68kCompilationResult compilation,
		string scenario,
		uint seed,
		uint selector,
		uint residue)
	{
		const uint load = 0x10000;
		const uint sentinel = 0x1000;
		const uint stackBottom = 0x7c000;
		var stack = 0x80000u + residue;
		var bus = new TestBus(0x100000);
		bus.Memory.AsSpan(
			(int)stackBottom - 32,
			(int)(stack - stackBottom) + 128).Fill(0xa5);
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)load));
		foreach (var relocation in compilation.Relocations)
		{
			var address = load + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + load);
		}
		var code = bus.Memory.AsSpan((int)load, compilation.Code.Length).ToArray();
		bus.WriteLong(ReadOnlyAggregateReturnForwardingFixtures.Input, seed);
		bus.WriteLong(ReadOnlyAggregateReturnForwardingFixtures.Selector, selector);
		bus.WriteLong(stack, sentinel);
		var high = bus.Memory.AsSpan((int)stack + 4, 64).ToArray();
		var low = bus.Memory.AsSpan((int)stackBottom - 32, 32).ToArray();
		var symbol = Assert.Single(compilation.Symbols, item =>
			item.Name == typeof(ReadOnlyAggregateReturnForwardingFixtures).FullName +
				"::" + scenario);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load + symbol.Address, stack);
		for (var register = 2; register < 8; register++)
		{
			cpu.State.D[register] = 0xdada0000u + (uint)register;
		}
		for (var register = 2; register < 7; register++)
		{
			cpu.State.A[register] = 0xa0a00000u + (uint)register;
		}
		cpu.State.StatusRegister = 0x2700;
		for (var steps = 0;
			steps < 20000 && cpu.State.ProgramCounter != sentinel;
			steps++)
		{
			cpu.ExecuteInstruction();
			Assert.False(
				cpu.State.Halted,
				$"Halted at {cpu.State.ProgramCounter:X8}.");
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		for (var register = 2; register < 8; register++)
		{
			Assert.Equal(0xdada0000u + (uint)register, cpu.State.D[register]);
		}
		for (var register = 2; register < 7; register++)
		{
			Assert.Equal(0xa0a00000u + (uint)register, cpu.State.A[register]);
		}
		Assert.Equal(high, bus.Memory.AsSpan((int)stack + 4, 64).ToArray());
		Assert.Equal(low, bus.Memory.AsSpan((int)stackBottom - 32, 32).ToArray());
		Assert.Equal(code, bus.Memory.AsSpan((int)load, compilation.Code.Length).ToArray());
		return new Observation(
			cpu.State.D[0],
			bus.Memory.AsSpan(
				(int)ReadOnlyAggregateReturnForwardingFixtures.Input,
				8).ToArray(),
			cpu.State.Cycles);
	}
}

public static unsafe class ReadOnlyAggregateReturnForwardingFixtures
{
	public const uint Input = 0x40000;
	public const uint Selector = 0x40004;

	public struct Record
	{
		public uint A;
		public uint B;
		public uint C;
		public uint D;
		public uint E;
		public uint F;
	}

	public static uint EntrySafe() => Safe();
	public static uint EntryProducerAlias() => ProducerAlias();
	public static uint EntryWritable() => Writable();

	private static uint Read(uint address) => *(uint*)address;

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Record Make(uint seed) => new()
	{
		A = seed + 1,
		B = seed + 2,
		C = seed + 3,
		D = seed + 4,
		E = seed + 5,
		F = seed + 6
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint Sum(in Record value) => unchecked(
		value.A + value.B + value.C + value.D + value.E + value.F);

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Record Transform(in Record value, uint selector) => new()
	{
		A = value.B + selector,
		B = value.A + 3,
		C = value.F,
		D = value.C,
		E = value.D,
		F = value.E
	};

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static void Add(ref uint value, uint increment) =>
		value = unchecked(value + increment);

	public static uint Safe()
	{
		var value = Make(Read(Input));
		var first = value.A;
		return unchecked(first + Sum(in value));
	}

	public static uint ProducerAlias()
	{
		var value = Make(Read(Input));
		value = Transform(in value, Read(Selector));
		return Sum(in value);
	}

	public static uint Writable()
	{
		var value = Make(Read(Input));
		Add(ref value.A, Read(Selector));
		return Sum(in value);
	}
}
