using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class GeneratedHelperCleanupScopeTests
{
    [Theory]
    [InlineData(16)]
    [InlineData(64)]
    public void InlinedCallersRetainMethodLocalAnalysisAndNativeBehavior(int count)
    {
        var assembler = new M68kAssembler
        {
            GeneratedCodeSizeOptions = new() { InlineMemoryHelpers = true, RemoveRedundantTransport = true },
            GeneratedCodeSizeRanges = Enumerable.Range(0, count).Select(i => ($"m{i}", $"m{i}:end")).ToArray(),
            GeneratedPrivateHelperRanges = [("helper", "helper:end")]
        };
        for (var i = 0; i < count; i++)
        {
            assembler.Mark($"m{i}");
            assembler.EmitJsr("helper", false);
            assembler.EmitWord(0x8081); // OR.L D1,D0: zero becomes known after inlining.
            assembler.EmitWord(0x7200); // MOVEQ #0,D1; preserve the public result/flags.
            assembler.EmitWord(0x4E75);
            assembler.Mark($"m{i}:end");
        }
        assembler.Mark("helper"); assembler.EmitWord(0x7200); assembler.EmitWord(0x4E75);
        assembler.Mark("helper:end"); assembler.MarkDataStart();
        var bytesBefore = assembler.Link(0x10000, new Dictionary<string, uint>()).Bytes.Length;
        assembler.OptimizeForM68000();
        Assert.Equal(1, assembler.GeneratedCodeSizeStatistics[nameof(M68kCodeSizeOptions.InlineMemoryHelpers)].Rewrites);
        Assert.True(assembler.PeepholeOptimizationStatistics.Converged);
        Assert.False(assembler.HasAnalysisScope);
        // Count analysis work, not wall time. A whole-image rescan for every
        // newly exposed caller rewrite exceeds this bound for the larger case.
        Assert.InRange(assembler.PeepholeOptimizationStatistics.AnalyzedBytes, 1, 64L * bytesBefore);
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        foreach (var i in new[] { 0, count - 1 })
        {
            var code = assembler.Link(load, new Dictionary<string, uint>());
            var bus = new TestBus(0x100000); code.Bytes.CopyTo(bus.Memory, load);
            bus.WriteLong(0x80000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(load + (uint)code.Labels[$"m{i}"], 0x80000);
            cpu.State.D[0] = 0x87654321; cpu.State.D[1] = 0x12345678;
            cpu.State.StatusRegister = 0x2710;
            for (var step = 0; step < 100 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
            Assert.False(cpu.State.Halted); Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal(0x87654321u, cpu.State.D[0]); Assert.Equal(0u, cpu.State.D[1]);
            Assert.Equal((ushort)0x2714, cpu.State.StatusRegister); Assert.Equal(0x80004u, cpu.State.A[7]);
        }
    }
}
