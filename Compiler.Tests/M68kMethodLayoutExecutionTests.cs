using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMethodLayoutExecutionTests
{
	[Theory]
	[InlineData(M68kPeepholeOptimizationMode.Disabled)]
	[InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
	public void ClusteredRomLayoutPreservesExecutionAndReachableSymbols(
		M68kPeepholeOptimizationMode peephole)
	{
		var control = Compile(M68kCpuTarget.M68000, M68kRuntimeProfile.Rom,
			peephole, enabled: false);
		var candidate = Compile(M68kCpuTarget.M68000, M68kRuntimeProfile.Rom,
			peephole, enabled: true);
		Assert.Equal(
			control.Symbols.Select(static symbol => symbol.Name).Order(),
			candidate.Symbols.Select(static symbol => symbol.Name).Order());

		foreach (var value in new uint[] { 0, 1, 0x12345678, 0xffffffff })
		foreach (var selector in new uint[] { 0, 1 })
		foreach (var residue in new uint[] { 0, 2 })
		{
			var before = Execute(control, value, selector, residue);
			var after = Execute(candidate, value, selector, residue);
			Assert.Equal(before.Result, after.Result);
			Assert.Equal(before.Trace, after.Trace);
			Assert.Equal(before.StatusRegister, after.StatusRegister);
			Assert.True(Math.Abs(after.Cycles - before.Cycles) <= 16,
				$"value={value:X8} selector={selector} SP%4={residue}: " +
				$"cycles {before.Cycles} -> {after.Cycles}.");
		}
	}

	[Theory]
	[InlineData(M68kCpuTarget.M68020, M68kRuntimeProfile.Rom)]
	[InlineData(M68kCpuTarget.M68000, M68kRuntimeProfile.Freestanding)]
	public void OtherTargetsAndProfilesKeepTheirExistingLayout(
		M68kCpuTarget cpu,
		M68kRuntimeProfile profile)
	{
		var control = Compile(cpu, profile, M68kPeepholeOptimizationMode.FixedPoint,
			enabled: false);
		var candidate = Compile(cpu, profile, M68kPeepholeOptimizationMode.FixedPoint,
			enabled: true);
		Assert.Equal(control.Code, candidate.Code);
	}

	private static M68kCompilationResult Compile(
		M68kCpuTarget cpu,
		M68kRuntimeProfile profile,
		M68kPeepholeOptimizationMode peephole,
		bool enabled) =>
		M68kCompiler.Compile(new M68kCompilationRequest
		{
			AssemblyPath = typeof(UnusedRegisterArgumentFixtures).Assembly.Location,
			EntryPoint = typeof(UnusedRegisterArgumentFixtures).FullName + "::EntryEffects",
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
				ClusterInternalCalls = enabled,
				ForwardReadOnlyAggregateLocals = false
			}
		});

	private sealed record Observation(
		uint Result,
		uint Trace,
		ushort StatusRegister,
		long Cycles);

	private static Observation Execute(
		M68kCompilationResult compilation,
		uint value,
		uint selector,
		uint residue)
	{
		const uint load = 0x10000;
		const uint sentinel = 0x1000;
		var stack = 0x80000u + residue;
		var bus = new TestBus(0x100000);
		compilation.Code.CopyTo(bus.Memory.AsSpan((int)load));
		foreach (var relocation in compilation.Relocations)
		{
			var address = load + (uint)relocation.Offset;
			bus.WriteLong(address, bus.ReadLong(address) + load);
		}
		var immutableCode = bus.Memory.AsSpan((int)load, compilation.Code.Length).ToArray();
		bus.WriteLong(UnusedRegisterArgumentFixtures.Input, value);
		bus.WriteLong(UnusedRegisterArgumentFixtures.Selector, selector);
		bus.WriteLong(UnusedRegisterArgumentFixtures.Trace, 0);
		bus.WriteLong(stack, sentinel);
		var entry = Assert.Single(compilation.Symbols, symbol =>
			symbol.Name == typeof(UnusedRegisterArgumentFixtures).FullName + "::EntryEffects");
		using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
		cpu.Reset(load + entry.Address, stack);
		for (var register = 2; register < 8; register++)
			cpu.State.D[register] = 0xdada0000u + (uint)register;
		for (var register = 2; register < 7; register++)
			cpu.State.A[register] = 0xa0a00000u + (uint)register;
		cpu.State.StatusRegister = 0x2700;
		for (var steps = 0; steps < 10000 && cpu.State.ProgramCounter != sentinel; steps++)
		{
			cpu.ExecuteInstruction();
			Assert.False(cpu.State.Halted, $"Halted at {cpu.State.ProgramCounter:X8}.");
		}
		Assert.Equal(sentinel, cpu.State.ProgramCounter);
		Assert.Equal(stack + 4, cpu.State.A[7]);
		for (var register = 2; register < 8; register++)
			Assert.Equal(0xdada0000u + (uint)register, cpu.State.D[register]);
		for (var register = 2; register < 7; register++)
			Assert.Equal(0xa0a00000u + (uint)register, cpu.State.A[register]);
		Assert.Equal(immutableCode,
			bus.Memory.AsSpan((int)load, compilation.Code.Length).ToArray());
		return new(
			cpu.State.D[0],
			bus.ReadLong(UnusedRegisterArgumentFixtures.Trace),
			cpu.State.StatusRegister,
			cpu.State.Cycles);
	}
}
