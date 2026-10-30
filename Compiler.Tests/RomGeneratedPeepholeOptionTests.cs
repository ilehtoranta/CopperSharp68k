using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class RomGeneratedPeepholeOptionTests
{
	private const uint RomBase = 0x100000;
	private const uint Stack = 0x90000;
	private const uint Sentinel = 0x1000;

	[Fact]
	public void DefaultRomPolicyKeepsGeneratedPassesOff()
	{
		Assert.False(new M68kRomSizeOptions().GeneratedPeepholes);
		var defaultMode = Compile(new M68kRomSizeOptions());
		var explicitOff = Compile(new M68kRomSizeOptions { GeneratedPeepholes = false });
		var optIn = Compile(new M68kRomSizeOptions { GeneratedPeepholes = true });
		Assert.Equal(defaultMode.Code, explicitOff.Code);
		Assert.Contains("GENERATED-SIZE passes=none", defaultMode.Map);
		foreach (var pass in new[] { "CompactGuestMemory", "NarrowOperations",
			"RemoveRedundantTransport", "CompactPrivateStackCopies" })
			Assert.Contains(pass, optIn.Map);
	}

	[Theory]
	[InlineData(0u)]
	[InlineData(1u)]
	[InlineData(0x12345678u)]
	[InlineData(uint.MaxValue)]
	public void OptInRomPreservesCompiledMc68000GuestResults(uint input)
	{
		var basic = Compile(new M68kRomSizeOptions());
		var optIn = Compile(new M68kRomSizeOptions { GeneratedPeepholes = true });
		Assert.Equal(Execute(basic, input), Execute(optIn, input));
	}

	private static M68kCompilationResult Compile(M68kRomSizeOptions policy) =>
		AmigaM68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(GeneratedSizeFixtures).Assembly.Location,
			EntryPoint = typeof(GeneratedSizeFixtures).FullName + "::Entry",
			Cpu = M68kCpuTarget.M68000,
			ClrPolicy = M68kClrPolicy.Always,
			OutputFormat = M68kOutputFormat.KickstartRom,
			RuntimeProfile = M68kRuntimeProfile.Rom,
			MemoryManagement = M68kMemoryManagement.None,
			ExceptionMode = M68kExceptionMode.Yolo,
			PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
			RomSizeOptimizations = policy,
			IncludedExportNames = [],
			Rom = new KickstartRomOutputOptions
			{
				Size = 262144, BaseAddress = RomBase, InitialStackPointer = Stack
			}
		});

	private static (uint Result, string GuestMemory) Execute(
		M68kCompilationResult compiled, uint input)
	{
		var bus = new TestBus(0x200000);
		compiled.Image.CopyTo(bus.Memory, (int)RomBase);
		bus.WriteLong(0x40000, input);
		bus.WriteLong(Stack, Sentinel);
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(compiled.EntryPoint, Stack);
		for (var steps = 0; steps < 100000 && cpu.State.ProgramCounter != Sentinel; steps++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted);
		}
		Assert.Equal(Sentinel, cpu.State.ProgramCounter);
		Assert.Equal(Stack + 4, cpu.State.A[7]);
		return (cpu.State.D[0], Convert.ToHexString(bus.Memory.AsSpan(0x40000, 0x30)));
	}
}
