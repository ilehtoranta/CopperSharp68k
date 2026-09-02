using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Tests.MultiModule;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ExternalCallBodyFoldingTests
{
	private const uint LoadAddress = 0x0001_0000;
	private const uint StackPointer = 0x0008_0000;
	private const uint ReturnSentinel = 0x0000_1000;
	private const uint DosBase = 0x0000_8000;
	private const uint FirstImport = 0x0000_2000;
	private const uint SecondImport = 0x0000_2100;

	public static TheoryData<M68kCpuTarget, M68kCpuModel,
		M68kPeepholeOptimizationMode, M68kRuntimeProfile> ExecutionCases
	{
		get
		{
			var cases = new TheoryData<M68kCpuTarget, M68kCpuModel,
				M68kPeepholeOptimizationMode, M68kRuntimeProfile>();
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
				cases.Add(target, model, mode, profile);
			return cases;
		}
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void DistinctExternalDosVectorsStayDistinctWhileSameTargetBodiesFold(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile)
	{
		var result = AmigaM68kCompiler.Compile(Request(
			nameof(ExternalCallBodyFoldingFixture.DosEntry), target, mode, profile));
		var bus = CreateBus(result);
		var calls = new List<string>();
		var expectedCalls = new[] { "Read", "Write", "Read" };
		var lengths = new uint[] { 7, 17, 18 };
		void Transfer(string operation, M68kCpuState state)
		{
			var index = calls.Count;
			Assert.True(index < expectedCalls.Length, "Unexpected extra DOS call.");
			Assert.Equal(expectedCalls[index], operation);
			Assert.Equal(DosBase, state.A[6]);
			Assert.Equal(0x401u + (uint)index, state.D[1]);
			Assert.Equal(0x5100u + (uint)index * 0x100u, state.D[2]);
			Assert.Equal(lengths[index], state.D[3]);
			calls.Add(operation);
			state.D[0] = lengths[index];
			state.D[1] = 0xD1D1_D1D1;
			state.A[0] = 0xA0A0_A0A0;
			state.A[1] = 0xA1A1_A1A1;
		}
		bus.RegisterGateway(DosBase - 42, state => Transfer("Read", state));
		bus.RegisterGateway(DosBase - 48, state => Transfer("Write", state));

		Assert.Equal(42u, Execute(result, bus, model));
		Assert.Equal(expectedCalls, calls);
		AssertFoldedAliases(result, nameof(ExternalCallBodyFoldingFixture.Read),
			nameof(ExternalCallBodyFoldingFixture.ReadAgain),
			nameof(ExternalCallBodyFoldingFixture.Write));
	}

	[Theory]
	[MemberData(nameof(ExecutionCases))]
	public void DistinctCrossModuleImportsStayDistinctWhileSameTargetBodiesFold(
		M68kCpuTarget target, M68kCpuModel model,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile)
	{
		var result = AmigaM68kCompiler.Compile(Request(
			nameof(ExternalCallBodyFoldingFixture.ImportEntry), target, mode, profile));
		var bus = CreateBus(result);
		var calls = new List<string>();
		var expectedCalls = new[] { "First", "Second", "First" };
		var values = new uint[] { 7, 17, 18 };
		void Imported(string operation, M68kCpuState state)
		{
			var index = calls.Count;
			Assert.True(index < expectedCalls.Length, "Unexpected extra import call.");
			Assert.Equal(expectedCalls[index], operation);
			Assert.Equal(values[index], state.D[0]);
			calls.Add(operation);
			state.D[0] = values[index];
		}
		bus.RegisterGateway(FirstImport, state => Imported("First", state));
		bus.RegisterGateway(SecondImport, state => Imported("Second", state));

		Assert.Equal(42u, Execute(result, bus, model));
		Assert.Equal(expectedCalls, calls);
		AssertFoldedAliases(result, nameof(ExternalCallBodyFoldingFixture.First),
			nameof(ExternalCallBodyFoldingFixture.FirstAgain),
			nameof(ExternalCallBodyFoldingFixture.Second));
	}

	private static M68kCompilationRequest Request(string entry, M68kCpuTarget target,
		M68kPeepholeOptimizationMode mode, M68kRuntimeProfile profile) => new()
	{
		AssemblyPath = typeof(ExternalCallBodyFoldingFixture).Assembly.Location,
		EntryPoint = $"{typeof(ExternalCallBodyFoldingFixture).FullName}::{entry}",
		// Import metadata is read from the adjacent dependency DLL. Do not register
		// it as managed code: the regression must exercise synthetic external calls.
		ManagedAssemblyPaths = [],
		Cpu = target,
		OutputFormat = M68kOutputFormat.Hunk,
		RuntimeProfile = profile,
		MemoryManagement = M68kMemoryManagement.None,
		ExceptionMode = M68kExceptionMode.Yolo,
		PeepholeOptimization = mode,
		IncludedExportNames = [],
		Imports = new Dictionary<string, uint>
		{
			["fixture.fold-first"] = FirstImport,
			["fixture.fold-second"] = SecondImport
		}
	};

	private static void AssertFoldedAliases(M68kCompilationResult result,
		string first, string sameTarget, string differentTarget)
	{
		uint Address(string name) => Assert.Single(result.Symbols,
			symbol => symbol.Name == $"{typeof(ExternalCallBodyFoldingFixture).FullName}::{name}").Address;
		Assert.Equal(Address(first), Address(sameTarget));
		Assert.NotEqual(Address(first), Address(differentTarget));
	}

	private static TestBus CreateBus(M68kCompilationResult result)
	{
		Assert.Equal(M68kMemoryManagement.None, result.NativeCompatibility.MemoryManagement);
		Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
		var bus = new TestBus();
		result.Code.CopyTo(bus.Memory.AsSpan((int)LoadAddress));
		foreach (var relocation in result.Relocations)
		{
			var address = LoadAddress + unchecked((uint)relocation.Offset);
			bus.WriteLong(address, bus.ReadLong(address) + LoadAddress);
		}
		bus.WriteLong(StackPointer, ReturnSentinel);
		return bus;
	}

	private static uint Execute(M68kCompilationResult result, TestBus bus, M68kCpuModel model)
	{
		using var cpu = M68kCoreFactory.Default.Create(model, bus);
		cpu.Reset(LoadAddress + result.EntryPoint, StackPointer);
		for (var instruction = 0; instruction < 20_000; instruction++)
		{
			if (cpu.State.ProgramCounter == ReturnSentinel)
			{
				Assert.Equal(StackPointer + 4, cpu.State.A[7]);
				return cpu.State.D[0];
			}
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted,
				$"{model} halted at ${cpu.State.ProgramCounter:X8}.");
		}
		throw new Xunit.Sdk.XunitException($"{model} did not return.");
	}
}

public static class ExternalCallBodyFoldingFixture
{
	public static int DosEntry()
	{
		DOS.DOSLibraryBase = APTR.FromPointer(0x8000);
		var first = Read(BPTR.FromRaw(0x401), APTR.FromPointer(0x5100), 7);
		var second = Write(BPTR.FromRaw(0x402), APTR.FromPointer(0x5200), 17);
		return first + second + ReadAgain(BPTR.FromRaw(0x403), APTR.FromPointer(0x5300), 18);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Read(BPTR file, APTR buffer, int length) => DOS.Read(file, buffer, length);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Write(BPTR file, APTR buffer, int length) => DOS.Write(file, buffer, length);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int ReadAgain(BPTR file, APTR buffer, int length) => DOS.Read(file, buffer, length);

	public static int ImportEntry() => First(7) + Second(17) + FirstAgain(18);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int First(int value) => ExternalFoldImports.First(value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int Second(int value) => ExternalFoldImports.Second(value);

	[MethodImpl(MethodImplOptions.NoInlining)]
	public static int FirstAgain(int value) => ExternalFoldImports.First(value);
}
