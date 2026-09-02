using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ConstructedScalarLibraryBaseTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint Control = 0x0000_6000;
	private const uint Payload = 0x0000_7000;

	public static TheoryData<M68kCpuTarget, M68kCpuModel,
		M68kPeepholeOptimizationMode, M68kRuntimeProfile> Configurations
	{
		get
		{
			var result = new TheoryData<M68kCpuTarget, M68kCpuModel,
				M68kPeepholeOptimizationMode, M68kRuntimeProfile>();
			foreach (var (target, model) in new[]
			{
				(M68kCpuTarget.M68000, M68kCpuModel.M68000),
				(M68kCpuTarget.M68020, M68kCpuModel.M68020),
				(M68kCpuTarget.M68040, M68kCpuModel.M68040)
			})
			foreach (var optimization in new[]
			{
				M68kPeepholeOptimizationMode.FixedPoint,
				M68kPeepholeOptimizationMode.Disabled
			})
			foreach (var profile in new[]
			{
				M68kRuntimeProfile.Freestanding,
				M68kRuntimeProfile.Resident
			})
				result.Add(target, model, optimization, profile);
			return result;
		}
	}

	public static IEnumerable<object[]> ScalarCases =>
		from configuration in Configurations
		from entry in new[]
		{
			nameof(ConstructedScalarLibraryBaseFixture.DirectLibraryBase),
			nameof(ConstructedScalarLibraryBaseFixture.LocalLibraryBase),
			nameof(ConstructedScalarLibraryBaseFixture.FactoryLibraryBase),
			nameof(ConstructedScalarLibraryBaseFixture.ReturnedLibraryBase),
			nameof(ConstructedScalarLibraryBaseFixture.DirectConversion),
			nameof(ConstructedScalarLibraryBaseFixture.DirectMemoryRead),
			nameof(ConstructedScalarLibraryBaseFixture.TransformedScalar),
			nameof(ConstructedScalarLibraryBaseFixture.TwoArgumentScalar)
		}
		select new object[] { entry }.Concat(configuration).ToArray();

	[Theory]
	[MemberData(nameof(ScalarCases))]
	public void ConstructedScalarConsumersReceivePayloadBits(string entry,
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode optimization, M68kRuntimeProfile profile)
	{
		var compilation = Compile(entry, target, optimization, profile);
		foreach (var raw in new uint[] { 0, 1, 0x1234_5678, 0xFEDC_BA98, uint.MaxValue })
		{
			var bus = Load(compilation);
			bus.WriteLong(Control, raw);
			bus.WriteLong(Control + 4, Payload);
			bus.WriteLong(Payload, raw);
			var loadedImage = bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray();
			var expected = entry switch
			{
				nameof(ConstructedScalarLibraryBaseFixture.TransformedScalar) =>
					unchecked((raw ^ 0x51A3_779Du) + 17u),
				nameof(ConstructedScalarLibraryBaseFixture.TwoArgumentScalar) => unchecked(raw + 23u),
				_ => raw
			};
			var observed = Execute(compilation, bus, model);
			PersistExecution(entry, target, optimization, profile, raw, expected, observed, 0);
			Assert.Equal(expected, observed.Result);
			Assert.Equal(StackPointer + 4, observed.StackPointer);
			Assert.Equal(raw, bus.ReadLong(Control));
			Assert.Equal(raw, bus.ReadLong(Payload));
			if (profile == M68kRuntimeProfile.Resident)
				Assert.Equal(loadedImage,
					bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray());
		}
	}

	[Theory]
	[MemberData(nameof(Configurations))]
	public void ConstructedLibraryBaseSuppliesA6ToDeclaredDosVector(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode optimization, M68kRuntimeProfile profile)
	{
		const string entry = nameof(ConstructedScalarLibraryBaseFixture.CallDosVector);
		const uint libraryBase = 0x0000_5000;
		const uint vector = libraryBase - 132;
		var compilation = Compile(entry, target, optimization, profile);
		var bus = Load(compilation);
		bus.WriteLong(Control, libraryBase);
		var calls = 0;
		bus.RegisterGateway(vector, state =>
		{
			Assert.Equal(libraryBase, state.A[6]);
			calls++;
			state.D[0] = 205;
		});
		// The supplied vector occupies six bytes; permit harmless instruction prefetch.
		bus.WriteWord(vector + 6, 0x4E71);
		bus.WriteWord(vector + 8, 0x4E71);
		var vectorBytes = bus.Memory.AsSpan((int)vector, 10).ToArray();
		var loadedImage = bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray();
		var observed = Execute(compilation, bus, model);
		PersistExecution(entry, target, optimization, profile, libraryBase, 205, observed, calls);
		Assert.Equal(205u, observed.Result);
		Assert.Equal(1, calls);
		Assert.Equal(StackPointer + 4, observed.StackPointer);
		Assert.Equal(vectorBytes, bus.Memory.AsSpan((int)vector, 10).ToArray());
		if (profile == M68kRuntimeProfile.Resident)
			Assert.Equal(loadedImage,
				bus.Memory.AsSpan((int)LoadAddress, compilation.Code.Length).ToArray());
	}

	[Fact]
	public void FixtureIncludesActualNewobjAndNonIdentityConstructorBodies()
	{
		using var module = new CompilationModule(typeof(ConstructedScalarLibraryBaseFixture).Assembly.Location);
		foreach (var entry in new[]
		{
			nameof(ConstructedScalarLibraryBaseFixture.DirectLibraryBase),
			nameof(ConstructedScalarLibraryBaseFixture.DirectConversion),
			nameof(ConstructedScalarLibraryBaseFixture.TransformedScalar),
			nameof(ConstructedScalarLibraryBaseFixture.TwoArgumentScalar)
		})
		{
			var method = module.ResolveEntryPoint($"{typeof(ConstructedScalarLibraryBaseFixture).FullName}::{entry}");
			Assert.Contains(method.Instructions, instruction => instruction.OpCode == OpCodes.Newobj);
		}
		Assert.Equal(unchecked((123u ^ 0x51A3_779Du) + 17u),
			new ConstructedScalarLibraryBaseFixture.Transformed(123).Raw);
		Assert.Equal(146u, new ConstructedScalarLibraryBaseFixture.Sum(123, 23).Raw);
	}

	private static M68kCompilationResult Compile(string entry, M68kCpuTarget target,
		M68kPeepholeOptimizationMode optimization, M68kRuntimeProfile profile)
	{
		var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(ConstructedScalarLibraryBaseFixture).Assembly.Location,
			EntryPoint = $"{typeof(ConstructedScalarLibraryBaseFixture).FullName}::{entry}",
			Cpu = target,
			OutputFormat = M68kOutputFormat.Hunk,
			RuntimeProfile = profile,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = optimization,
			IncludedExportNames = []
		});
		if (EvidenceDirectory is { } directory)
		{
			Directory.CreateDirectory(directory);
			var prefix = Path.Combine(directory, $"{entry}-{target}-{optimization}-{profile}");
			File.WriteAllBytes(prefix + ".hunk", result.Image);
			File.WriteAllText(prefix + ".map", result.Map);
			File.WriteAllText(prefix + ".compatibility.json", JsonSerializer.Serialize(result.NativeCompatibility));
		}
		Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
		Assert.Empty(result.NativeCompatibility.RuntimeFeatures);
		Assert.Empty(result.NativeCompatibility.RuntimeHelpers);
		Assert.Empty(result.NativeCompatibility.ExternalNativeTargets);
		return result;
	}

	private static TestBus Load(M68kCompilationResult compilation)
	{
		var bus = new TestBus();
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in compilation.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(StackPointer, ReturnSentinel);
		return bus;
	}

	private static Execution Execute(M68kCompilationResult compilation, TestBus bus, M68kCpuModel model)
	{
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + compilation.EntryPoint, StackPointer);
		for (var instructions = 0; instructions < 20_000; instructions++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
				return new(cpu.State.D[0], cpu.State.A[7], instructions);
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		throw new Xunit.Sdk.XunitException($"{model} did not return within 20000 instructions.");
	}

	private sealed record Execution(uint Result, uint StackPointer, int Instructions);
	private static string? EvidenceDirectory => Environment.GetEnvironmentVariable("COPPERSHARP_SCALAR_EVIDENCE");
	private static void PersistExecution(string entry, M68kCpuTarget target,
		M68kPeepholeOptimizationMode optimization, M68kRuntimeProfile profile,
		uint raw, uint expected, Execution observed, int suppliedDosVectorCalls)
	{
		if (EvidenceDirectory is not { } directory)
			return;
		File.WriteAllText(Path.Combine(directory,
			$"{entry}-{target}-{optimization}-{profile}-{raw:X8}.execution.json"),
			JsonSerializer.Serialize(new { entry, target, optimization, profile, raw, expected,
				observed, suppliedDosVectorCalls, originalCommands = 0, shippingQualified = false }));
	}
}

public static class ConstructedScalarLibraryBaseFixture
{
	private static uint ReadRaw() => APTR.ReadUInt32(APTR.FromPointer(0x6000), 0);
	public static uint DirectLibraryBase()
	{
		DOS.DOSLibraryBase = new APTR(ReadRaw());
		return DOS.DOSLibraryBase.Raw;
	}
	public static uint LocalLibraryBase()
	{
		var value = new APTR(ReadRaw());
		DOS.DOSLibraryBase = value;
		return DOS.DOSLibraryBase.Raw;
	}
	public static uint FactoryLibraryBase()
	{
		DOS.DOSLibraryBase = APTR.FromPointer(ReadRaw());
		return DOS.DOSLibraryBase.Raw;
	}
	public static uint ReturnedLibraryBase()
	{
		DOS.DOSLibraryBase = Construct(ReadRaw());
		return DOS.DOSLibraryBase.Raw;
	}
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static APTR Construct(uint raw) => new(raw);
	public static uint DirectConversion() => APTR.ToUInt32(new APTR(ReadRaw()));
	public static uint DirectMemoryRead() => APTR.ReadUInt32(
		new APTR(APTR.ReadUInt32(APTR.FromPointer(0x6000), 4)), 0);
	public static uint TransformedScalar() => ReadTransformed(new Transformed(ReadRaw()));
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadTransformed(Transformed value) => value.Raw;
	public static uint TwoArgumentScalar() => ReadSum(new Sum(ReadRaw(), 23));
	[MethodImpl(MethodImplOptions.NoInlining)]
	private static uint ReadSum(Sum value) => value.Raw;
	public readonly struct Transformed
	{
		private readonly uint _value;
		public Transformed(uint value) => _value = unchecked((value ^ 0x51A3_779Du) + 17u);
		public uint Raw => _value;
	}
	public readonly struct Sum
	{
		private readonly uint _value;
		public Sum(uint left, uint right) => _value = unchecked(left + right);
		public uint Raw => _value;
	}
	public static uint CallDosVector()
	{
		DOS.DOSLibraryBase = new APTR(ReadRaw());
		if (DOS.DOSLibraryBase.Raw != ReadRaw())
			return 0xBAD0_0001;
		return (uint)DOS.IoErr();
	}
}
