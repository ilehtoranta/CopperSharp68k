using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class FreestandingClusterEntryTests
{
    [Theory]
    [InlineData(M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
    public void PlatformBasePrefixEntersTheRootAfterCallGraphClustering(M68kPeepholeOptimizationMode peephole)
    {
        var image = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(FreestandingClusterEntryFixture).Assembly.Location,
            EntryPoint = typeof(FreestandingClusterEntryFixture).FullName + "::Entry",
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Freestanding,
            OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None, PeepholeOptimization = peephole,
            Hunk = new() { IncludeSymbols = false }, IncludedExportNames = ["freestanding.cluster.hot"],
            CodeSizeOptimizations = new()
            {
                ClusterInternalCalls = true, ElideUnusedRegisterArguments = false,
                ShareReturnSequences = false, ShareIdenticalMethods = false,
                ReuseIncomingArgumentHomes = false, InlineSingleUseMethods = false,
                ForwardReadOnlyAggregateLocals = false
            }
        });
        var root = Assert.Single(image.Symbols, s => s.Name == typeof(FreestandingClusterEntryFixture).FullName + "::Entry");
        Assert.True(root.Address > 32, "Fixture must place another method between the prefix and its root");
        Assert.Equal(0u, image.EntryPoint);
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        {
            var bus = new TestBus(0x100000);
            ResidentCodeSizeExecutionTests.LoadImage(image.Image, load).CopyTo(bus.Memory.AsSpan((int)load));
            bus.WriteLong(4, 0x50000);
            bus.WriteWord(0x50000 - 294, 0x203C); // Native Exec FindTask stub.
            bus.WriteLong(0x50000 - 292, 0x12345678); bus.WriteWord(0x50000 - 288, 0x4E75);
            bus.WriteLong(0x80000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(load + image.EntryPoint, 0x80000);
            for (var step = 0; step < 10000 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
            Assert.False(cpu.State.Halted); Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal(0x1234567Bu, cpu.State.D[0]); Assert.Equal(0x80004u, cpu.State.A[7]);
        }
    }
}
