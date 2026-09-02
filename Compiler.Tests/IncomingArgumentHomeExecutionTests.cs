using System.Runtime.CompilerServices;
using Copper68k;

namespace CopperSharp.Compiler.Tests;

public sealed class IncomingArgumentHomeExecutionTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var scenario in new[] { "Read", "Mutate", "Twice", "HiddenReturn", "Large" })
        foreach (var cpu in new[] { M68kCpuTarget.M68000, M68kCpuTarget.M68020, M68kCpuTarget.M68040 })
        foreach (var peephole in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var otherOptions in new[] { false, true })
            yield return [scenario, cpu, peephole, otherOptions];
    }

    [Theory, MemberData(nameof(Cases))]
    public void IncomingValuesRetainIndependentCopiesMutationAndHiddenReturnBuffers(
        string scenario, M68kCpuTarget cpu, M68kPeepholeOptimizationMode peephole,
        bool otherOptions)
    {
        var before = Compile(scenario, cpu, peephole, false, otherOptions);
        var after = Compile(scenario, cpu, peephole, true, otherOptions);
        if (cpu == M68kCpuTarget.M68000)
        {
            Assert.True(after.Code.Length < before.Code.Length,
                $"No encoded gain for {scenario}: {before.Code.Length} -> {after.Code.Length}.");
        }
        else
        {
            Assert.Equal(before.Code, after.Code);
        }
        foreach (var value in new uint[] { 0, 1, 41, 0x7fffffff, 0x80000000, 0xffffffff })
        foreach (var selector in new uint[] { 0, 1, 0xffffffff })
        foreach (var residue in new uint[] { 0, 2 })
        {
            var control = Execute(before, scenario, cpu, value, selector, residue);
            var candidate = Execute(after, scenario, cpu, value, selector, residue);
            Assert.Equal(Expected(scenario, value, selector), candidate.Value);
            Assert.Equal(control.Value, candidate.Value);
            Assert.Equal(control.Guest, candidate.Guest);
            Assert.True(candidate.Cycles <= control.Cycles,
                $"{scenario}/{cpu}/{peephole}/{value:X8}/{selector:X8}/SP%4={residue}: {control.Cycles} -> {candidate.Cycles} cycles.");
        }
    }

    [Fact]
    public void FreestandingAndDynamicStackMethodsRetainTheOriginalHomes()
    {
        var before = Compile("Mutate", M68kCpuTarget.M68000,
            M68kPeepholeOptimizationMode.FixedPoint, false, false,
            M68kRuntimeProfile.Freestanding);
        var after = Compile("Mutate", M68kCpuTarget.M68000,
            M68kPeepholeOptimizationMode.FixedPoint, true, false,
            M68kRuntimeProfile.Freestanding);
        Assert.Equal(before.Code, after.Code);
        before = Compile("Dynamic", M68kCpuTarget.M68000,
            M68kPeepholeOptimizationMode.FixedPoint, false, false);
        after = Compile("Dynamic", M68kCpuTarget.M68000,
            M68kPeepholeOptimizationMode.FixedPoint, true, false);
        Assert.Equal(before.Code, after.Code);
        foreach (var residue in new uint[] { 0, 2 })
        {
            var a = Execute(before, "Dynamic", M68kCpuTarget.M68000, 41, 7, residue);
            var b = Execute(after, "Dynamic", M68kCpuTarget.M68000, 41, 7, residue);
            Assert.Equal(unchecked(6u * 41 + 21 + 7), b.Value);
            Assert.Equal(a.Value, b.Value);
        }
    }

    private static uint Expected(string scenario, uint seed, uint selector)
    {
        unchecked
        {
            var sum = seed * 6 + 21;
            return scenario switch
            {
                "Read" => sum * 3 + selector,
                "Mutate" => (seed + 1) * 7 + selector * 11 + (seed + 2) * 13 + (seed * 4 + 18) * 17
                    + (seed + 101) * 3 + ((seed + 2) ^ 7) * 5,
                "Twice" => (sum + 11) * 3 + (sum - (seed + 2) + ((seed + 2) ^ 13)) * 5 + sum * 2 + selector,
                "HiddenReturn" => (sum - (seed + 1) + selector) * 3 + sum * 5,
                "Large" => (seed * 16 + 136 + selector) * 3 + (seed * 17 + 153) * 5,
                _ => throw new ArgumentOutOfRangeException(nameof(scenario))
            };
        }
    }

    private static M68kCompilationResult Compile(string scenario, M68kCpuTarget cpu,
        M68kPeepholeOptimizationMode peephole, bool enabled, bool otherOptions,
        M68kRuntimeProfile profile = M68kRuntimeProfile.Rom) =>
            M68kCompiler.Compile(new M68kCompilationRequest {
                AssemblyPath = typeof(IncomingArgumentHomeFixtures).Assembly.Location,
                EntryPoint = typeof(IncomingArgumentHomeFixtures).FullName + "::Entry" + scenario,
                Cpu = cpu, OutputFormat = M68kOutputFormat.Assembly, RuntimeProfile = profile,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                PeepholeOptimization = peephole, IncludedExportNames = [],
                RomSizeOptimizations = new M68kRomSizeOptions {
                    ElideUnusedRegisterArguments = otherOptions,
                    ShareReturnSequences = otherOptions,
                    ReuseIncomingArgumentHomes = enabled,
                    ClusterInternalCalls = false,
                    ForwardReadOnlyAggregateLocals = false
                },
                BulkCopy = scenario == "Large" ? new M68kBulkCopyOptions {
                    MinimumBytes = 32,
                    ManagedAssemblyName = typeof(IncomingArgumentHomeFixtures).Assembly.GetName().Name,
                    ManagedMethod = typeof(IncomingArgumentHomeFixtures).FullName + "::Copy"
                } : null,
            });

    private sealed record Observation(uint Value, byte[] Guest, long Cycles);
    private static Observation Execute(M68kCompilationResult result, string scenario, M68kCpuTarget target, uint value, uint selector, uint residue)
    {
        const uint load = 0x10000, sentinel = 0x1000, stackBottom = 0x7c000;
        var stack = 0x80000u + residue;
        var bus = new TestBus(0x100000);
        bus.Memory.AsSpan((int)stackBottom - 32, (int)(stack - stackBottom) + 128).Fill(0xa5);
        result.Code.CopyTo(bus.Memory.AsSpan((int)load));
        foreach (var relocation in result.Relocations)
        {
            var address = load + (uint)relocation.Offset;
            bus.WriteLong(address, bus.ReadLong(address) + load);
        }
        var code = bus.Memory.AsSpan((int)load, result.Code.Length).ToArray();
        bus.WriteLong(IncomingArgumentHomeFixtures.Input, value);
        bus.WriteLong(IncomingArgumentHomeFixtures.Selector, selector);
        bus.WriteLong(stack, sentinel);
        var high = bus.Memory.AsSpan((int)stack + 4, 64).ToArray();
        var low = bus.Memory.AsSpan((int)stackBottom - 32, 32).ToArray();
        var symbol = Assert.Single(result.Symbols, s => s.Name == typeof(IncomingArgumentHomeFixtures).FullName + "::" + scenario);
        var model = target switch { M68kCpuTarget.M68000 => M68kCpuModel.M68000, M68kCpuTarget.M68020 => M68kCpuModel.M68020, _ => M68kCpuModel.M68040 };
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(load + symbol.Address, stack);
        for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xdada0000u + (uint)i;
        for (var i = 2; i < 7; i++) cpu.State.A[i] = 0xa0a00000u + (uint)i;
        cpu.State.StatusRegister = 0x2700;
        for (var i = 0; i < 20000 && cpu.State.ProgramCounter != sentinel; i++)
        {
            cpu.ExecuteInstruction(); Assert.False(cpu.State.Halted, $"Halted at {cpu.State.ProgramCounter:X8}.");
        }
        Assert.Equal(sentinel, cpu.State.ProgramCounter);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        for (var i = 2; i < 8; i++) Assert.Equal(0xdada0000u + (uint)i, cpu.State.D[i]);
        for (var i = 2; i < 7; i++) Assert.Equal(0xa0a00000u + (uint)i, cpu.State.A[i]);
        Assert.Equal(high, bus.Memory.AsSpan((int)stack + 4, 64).ToArray());
        Assert.Equal(low, bus.Memory.AsSpan((int)stackBottom - 32, 32).ToArray());
        Assert.Equal(code, bus.Memory.AsSpan((int)load, result.Code.Length).ToArray());
        return new(cpu.State.D[0], bus.Memory.AsSpan((int)IncomingArgumentHomeFixtures.Input, 8).ToArray(), cpu.State.Cycles);
    }
}

public static unsafe class IncomingArgumentHomeFixtures
{
    public const uint Input = 0x40000, Selector = 0x40004;
    public struct Record { public uint A, B, C, D, E, F; }
    public struct Wide { public uint A, B, C, D, E, F, G, H, I, J, K, L, M, N, O, P, Q; }
    private static uint ReadInput() => *(uint*)Input;
    private static uint ReadSelector() => *(uint*)Selector;
    private static Record Make(uint n) => new() { A=n+1, B=n+2, C=n+3, D=n+4, E=n+5, F=n+6 };
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint Sum(in Record r) => unchecked(r.A+r.B+r.C+r.D+r.E+r.F);
    [MethodImpl(MethodImplOptions.NoInlining)] private static void Set(ref uint value,uint replacement) => value=replacement;
    public static uint EntryRead()=>Read();
    public static uint EntryMutate()=>Mutate();
    public static uint EntryTwice()=>Twice();
    public static uint EntryHiddenReturn()=>HiddenReturn();
    public static uint EntryLarge()=>Large();
    public static uint EntryDynamic()=>Dynamic();
    public static uint Read() { var r=Make(ReadInput());return unchecked(ReadValue(r,ReadSelector())+Sum(in r)*2); }
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint ReadValue(Record r,uint selector)=>unchecked(Sum(in r)+selector);
    public static uint Mutate() {var r=Make(ReadInput());var value=MutateValue(r,ref r,ReadSelector());return unchecked(value+r.A*3+r.B*5);}
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint MutateValue(Record copy,ref Record original,uint selector)
    {
        var first=copy.A;Set(ref copy.A,selector);original.A=unchecked(original.A+100);original.B^=7;
        return unchecked(first*7+copy.A*11+copy.B*13+(copy.C+copy.D+copy.E+copy.F)*17);
    }
    public static uint Twice() {var r=Make(ReadInput());return unchecked(TwoValues(r,r,ReadSelector())+Sum(in r)*2);}
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint TwoValues(Record left,Record right,uint selector)
    {Set(ref left.A,unchecked(left.A+11));Set(ref right.B,right.B^13);return unchecked(Sum(in left)*3+Sum(in right)*5+selector);}
    public static uint HiddenReturn() {var r=Make(ReadInput());var copy=ReturnValue(r,ReadSelector());return unchecked(Sum(in copy)*3+Sum(in r)*5);}
    [MethodImpl(MethodImplOptions.NoInlining)] private static Record ReturnValue(Record value,uint selector)
    {Set(ref value.A,selector);return value;}
    public static uint Large()
    {
        var n=ReadInput();var r=new Wide {A=n+1,B=n+2,C=n+3,D=n+4,E=n+5,F=n+6,G=n+7,H=n+8,I=n+9,J=n+10,K=n+11,L=n+12,M=n+13,N=n+14,O=n+15,P=n+16,Q=n+17};
        return unchecked(LargeValue(r,ReadSelector())*3+SumWide(in r)*5);
    }
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint LargeValue(Wide value,uint selector)
    {Set(ref value.Q,selector);return SumWide(in value);}
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint SumWide(in Wide v)=>unchecked(v.A+v.B+v.C+v.D+v.E+v.F+v.G+v.H+v.I+v.J+v.K+v.L+v.M+v.N+v.O+v.P+v.Q);
    [MethodImpl(MethodImplOptions.NoInlining)] public static void Copy(byte* source,byte* destination,uint count)
    {for(uint i=0;i<count;i++)destination[i]=source[i];}
    public static uint Dynamic()=>DynamicValue(Make(ReadInput()),ReadSelector());
    [MethodImpl(MethodImplOptions.NoInlining)] private static uint DynamicValue(Record value,uint selector)
    {uint* scratch=stackalloc uint[(int)(selector & 7) + 1];scratch[0]=selector;return unchecked(Sum(in value)+scratch[0]);}
}
