using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public sealed class VolatilePrivateInitializationExecutionTests
{
    [Theory]
    [InlineData(M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
    public void RemovingAnUnobservableClearPreservesAtomicEffectsAndPrivateBytes(M68kPeepholeOptimizationMode mode)
    {
        var request = new M68kCompilationRequest {
            AssemblyPath = typeof(VolatilePrivateInitializationFixture).Assembly.Location,
            EntryPoint = typeof(VolatilePrivateInitializationFixture).FullName + "::Empty",
            IncludedExportNames = ["volatile.private.entry"],
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Freestanding,
            OutputFormat = M68kOutputFormat.Hunk, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = mode,
            CodeSizeOptimizations = new() { EliminateRedundantInitialization = false }
        };
        var before = AmigaM68kCompiler.Compile(request);
        var after = AmigaM68kCompiler.Compile(request with {
            CodeSizeOptimizations = new() { EliminateRedundantInitialization = true }
        });
        Assert.True(after.Code.Length <= before.Code.Length);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (uint load in new uint[] { 0x10000, 0x20000 })
        foreach (uint residue in new uint[] { 0, 2 })
        {
            var a = Run(before, model, load, residue);
            var b = Run(after, model, load, residue);
            Assert.True(b < a, $"No clear eliminated: {model}/{mode}; before={a}, after={b}");
        }
    }

    private static long Run(M68kCompilationResult result, M68kCpuModel model, uint load, uint residue)
    {
        var bus = new TestBus(); result.Code.CopyTo(bus.Memory.AsSpan((int)load));
        foreach (var relocation in result.Relocations)
        {
            uint at = load + (uint)relocation.Offset;
            bus.WriteLong(at, bus.ReadLong(at) + load);
        }
        uint stack = 0x80000 + residue;
        bus.Memory.AsSpan((int)stack - 512, 512).Fill(0xA5);
        bus.Memory[0x9000] = 0xA5; bus.Memory[0x9001] = 0x7F; bus.Memory[0x9002] = 0x5A;
        bus.WriteLong(stack, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(load + result.Symbols.Single(s => s.Name == "volatile.private.entry").Address, stack);
        for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xD5000000u + (uint)n;
        for (int n = 0; n < 7; n++) cpu.State.A[n] = 0xA5000000u + (uint)n;
        cpu.State.D[0] = 0x9001;
        var data = cpu.State.D.ToArray(); var addresses = cpu.State.A.Take(7).ToArray();
        for (int n = 0; n < 10000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        Assert.Equal(0x444444CCu, cpu.State.D[0]);
        Assert.Equal(0x80, bus.Memory[0x9001]);
        Assert.Equal(0xA5, bus.Memory[0x9000]); Assert.Equal(0x5A, bus.Memory[0x9002]);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        Assert.Equal(data.Skip(2), cpu.State.D.Skip(2));
        Assert.Equal(addresses.Skip(2), cpu.State.A.Skip(2).Take(5));
        return cpu.State.Cycles;
    }
}

public static class VolatilePrivateInitializationFixture
{
#pragma warning disable CS0649 // These bytes must come from aggregate initialization.
    private struct Data { public uint A, B, C, D, E, F, G, H; }
#pragma warning restore CS0649

    public static void Empty() { }

    [M68kExport("volatile.private.entry")]
    public static uint Entry([M68kRegister(M68kRegister.D0)] uint pointer)
    {
        _ = APTR.IncrementByteIsZero(pointer, 0);
        var data = default(Data);
        data.A = 0x11223344; data.H = 0x55667788;
        return Consume(ref data);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Consume(ref Data data) => data.A ^ data.B ^ data.C ^ data.D ^ data.E ^ data.F ^ data.G ^ data.H;
}
