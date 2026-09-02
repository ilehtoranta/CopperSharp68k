using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class UnusedRegisterArgumentExecutionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var name in new[] { "Scalar", "Effects", "Forward", "Pointer", "Aggregate", "UsedPointer" })
        foreach (var cpu in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020 })
        foreach (var peephole in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var shareReturns in new[] { false, true })
            yield return new object[] { name, cpu, peephole, shareReturns };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void IgnoredInputsPreserveEvaluationStackLayoutAliasesAndResults(
        string name, M68kCpuTarget target, M68kPeepholeOptimizationMode peephole, bool shareReturns)
    {
        var before = Compile(name, target, peephole, enabled: false, shareReturns: shareReturns);
        var after = Compile(name, target, peephole, enabled: true, shareReturns: shareReturns);
        var shouldChange = target == M68kCpuTarget.M68000 && name != "UsedPointer";
        if (shouldChange)
        {
            Assert.True(after.Code.Length < before.Code.Length,
                $"No encoded gain for {name}: {before.Code.Length} -> {after.Code.Length}.");
        }
        else
        {
            Assert.Equal(before.Code, after.Code);
        }
        foreach (var value in new uint[] { 0, 1, 41, 0x7fffffff, 0x80000000, 0xffffffff })
        foreach (var selector in new uint[] { 0, 1, 0xffffffff })
        foreach (var residue in new uint[] { 0, 2 })
        {
            var control = Execute(before, name, target, value, selector, residue);
            var candidate = Execute(after, name, target, value, selector, residue);
            var expected = name switch
            {
                "Scalar" => Many(value, selector, 5, 7),
                "Effects" => Many(unchecked(value + 34), selector, 68, 85),
                "Forward" => selector == 0 ? unchecked(value + 17) : unchecked(value - 11),
                "Pointer" => selector == 0 ? value | 0x5500u : value & 0xffffu,
                "Aggregate" => unchecked(value * 7 + (selector == 0 ? value + 1 : value ^ 0x12345678u)),
                "UsedPointer" => selector == 0 ? unchecked(value + 7) : value ^ 0x00ff00ffu,
                _ => throw new ArgumentOutOfRangeException(nameof(name))
            };
            Assert.Equal(expected, candidate.Value);
            Assert.Equal(control.Value, candidate.Value);
            Assert.Equal(control.GuestBytes, candidate.GuestBytes);
            Assert.Equal(name == "Effects" ? 123456u : 0u, candidate.Trace);
            Assert.True(candidate.Cycles <= control.Cycles,
                $"{name}/{target}/{peephole} value={value:X8} selector={selector:X8} SP%4={residue}: cycles {control.Cycles} -> {candidate.Cycles}.");
        }
    }

    [Fact]
    public void FreestandingProfileKeepsThePrototypeDisabled()
    {
        var before = Compile("Effects", M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint,
            false, M68kRuntimeProfile.Freestanding);
        var after = Compile("Effects", M68kCpuTarget.M68000, M68kPeepholeOptimizationMode.FixedPoint,
            true, M68kRuntimeProfile.Freestanding);
        Assert.Equal(before.Code, after.Code);
    }

    private static uint Many(uint value, uint selector, uint fourth, uint fifth) =>
        unchecked((selector == 0 ? value * 3 : value ^ 0x55aa55aau) + fourth + fifth);

    private static M68kCompilationResult Compile(string scenario, M68kCpuTarget cpu,
        M68kPeepholeOptimizationMode peephole, bool enabled,
        M68kRuntimeProfile profile = M68kRuntimeProfile.Rom, bool shareReturns = false) =>
        M68kCompiler.Compile(new M68kCompilationRequest {
            AssemblyPath = typeof(UnusedRegisterArgumentFixtures).Assembly.Location,
            EntryPoint = typeof(UnusedRegisterArgumentFixtures).FullName + "::Entry" + scenario,
            Cpu = cpu, OutputFormat = M68kOutputFormat.Assembly, RuntimeProfile = profile,
            MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            PeepholeOptimization = peephole, IncludedExportNames = [],
            RomSizeOptimizations = new M68kRomSizeOptions {
                ElideUnusedRegisterArguments = enabled,
                ShareReturnSequences = shareReturns,
                ReuseIncomingArgumentHomes = false,
                ClusterInternalCalls = false,
                ForwardReadOnlyAggregateLocals = false
            }
        });

    private sealed record Observation(uint Value, uint Trace, byte[] GuestBytes, long Cycles);
    private static Observation Execute(M68kCompilationResult compilation, string name, M68kCpuTarget target,
        uint value, uint selector, uint residue)
    {
        const uint load = 0x10000, sentinel = 0x1000, stackBottom = 0x7c000;
        var stack = 0x80000u + residue;
        var bus = new TestBus(0x100000);
        bus.Memory.AsSpan((int)stackBottom - 32, (int)(stack - stackBottom) + 128).Fill(0xa5);
        compilation.Code.CopyTo(bus.Memory.AsSpan((int)load));
        foreach (var relocation in compilation.Relocations)
        {
            var address = load + (uint)relocation.Offset;
            bus.WriteLong(address, bus.ReadLong(address) + load);
        }
        var code = bus.Memory.AsSpan((int)load, compilation.Code.Length).ToArray();
        bus.WriteLong(UnusedRegisterArgumentFixtures.Input, value);
        bus.WriteLong(UnusedRegisterArgumentFixtures.Selector, selector);
        bus.WriteLong(UnusedRegisterArgumentFixtures.Trace, 0);
        bus.WriteLong(stack, sentinel);
        var high = bus.Memory.AsSpan((int)stack + 4, 64).ToArray();
        var low = bus.Memory.AsSpan((int)stackBottom - 32, 32).ToArray();
        var symbol = Assert.Single(compilation.Symbols, s =>
            s.Name == typeof(UnusedRegisterArgumentFixtures).FullName + "::" + name);
        using var cpu = M68kCoreFactory.Default.Create(
            target == M68kCpuTarget.M68000 ? M68kCpuModel.M68000 : M68kCpuModel.M68020, bus);
        cpu.Reset(load + symbol.Address, stack);
        for (var register = 2; register < 8; register++) cpu.State.D[register] = 0xdada0000u + (uint)register;
        for (var register = 2; register < 7; register++) cpu.State.A[register] = 0xa0a00000u + (uint)register;
        cpu.State.StatusRegister = 0x2700;
        for (var steps = 0; steps < 10000 && cpu.State.ProgramCounter != sentinel; steps++)
        {
            cpu.ExecuteInstruction();
            Assert.False(cpu.State.Halted, $"Halted at {cpu.State.ProgramCounter:X8}.");
        }
        Assert.Equal(sentinel, cpu.State.ProgramCounter);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        Assert.Equal(sentinel, bus.ReadLong(stack));
        for (var register = 2; register < 8; register++) Assert.Equal(0xdada0000u + (uint)register, cpu.State.D[register]);
        for (var register = 2; register < 7; register++) Assert.Equal(0xa0a00000u + (uint)register, cpu.State.A[register]);
        Assert.Equal(high, bus.Memory.AsSpan((int)stack + 4, 64).ToArray());
        Assert.Equal(low, bus.Memory.AsSpan((int)stackBottom - 32, 32).ToArray());
        Assert.Equal(code, bus.Memory.AsSpan((int)load, compilation.Code.Length).ToArray());
        return new(cpu.State.D[0], bus.ReadLong(UnusedRegisterArgumentFixtures.Trace),
            bus.Memory.AsSpan((int)UnusedRegisterArgumentFixtures.Input, 12).ToArray(), cpu.State.Cycles);
    }
}

public static unsafe class UnusedRegisterArgumentFixtures
{
    public const uint Input = 0x40000, Selector = 0x40004, Trace = 0x40008;
    public static uint EntryScalar() => Scalar();
    public static uint EntryEffects() => Effects();
    public static uint EntryForward() => Forward();
    public static uint EntryPointer() => Pointer();
    public static uint EntryAggregate() => Aggregate();
    public static uint EntryUsedPointer() => UsedPointer();

    private static uint Read(uint address) => *(uint*)address;
    private static uint Effect(uint marker)
    {
        *(uint*)Trace = unchecked(*(uint*)Trace * 10 + marker);
        return marker * 17;
    }
    public static uint Scalar() => UsesMany(0x12345678, Read(Input), 0xfeedbeef, Read(Selector), 5, 7, 0xabcdef01);
    public static uint Effects() => UsesMany(Effect(1), unchecked(Read(Input) + Effect(2)), Effect(3),
        Read(Selector), Effect(4), Effect(5), Effect(6));
    private static uint UsesMany(uint ignored0, uint value, uint ignored2, uint selector,
        uint fourth, uint fifth, uint ignoredStack)
    {
        if (selector == 0) return unchecked(value * 3 + fourth + fifth);
        return unchecked((value ^ 0x55aa55aau) + fourth + fifth);
    }
    public static uint Forward() => ForwardUnused(0x12345678, Read(Input), Read(Selector));
    private static uint ForwardUnused(uint ignored, uint value, uint selector) => IgnoreLeading(ignored, value, selector);
    private static uint IgnoreLeading(uint ignored, uint value, uint selector)
    {
        if (selector == 0) return unchecked(value + 17);
        return unchecked(value - 11);
    }
    public static uint Pointer()
    {
        uint local = unchecked(Read(Input) + 5);
        return IgnorePointer(&local, Read(Input), Read(Selector));
    }
    private static uint IgnorePointer(uint* unused, uint value, uint selector)
    {
        if (selector == 0) return value | 0x5500;
        return value & 0xffff;
    }
    public struct Pair { public uint A, B; }
    public static uint Aggregate()
    {
        var pair = MakePair(0x12345678, Read(Input), Read(Selector));
        return unchecked(pair.A * 7 + pair.B);
    }
    private static Pair MakePair(uint unused, uint value, uint selector)
    {
        if (selector == 0) return new Pair { A = value, B = unchecked(value + 1) };
        return new Pair { A = value, B = value ^ 0x12345678u };
    }
    public static uint UsedPointer()
    {
        var local = Read(Input);
        Touch(&local, Read(Selector));
        return local;
    }
    private static void Touch(uint* value, uint selector)
    {
        if (selector == 0) *value = unchecked(*value + 7);
        else *value ^= 0x00ff00ffu;
    }
}
