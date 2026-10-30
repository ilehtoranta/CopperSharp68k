using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class FreestandingCodeSizeStateTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportCallbacksSharePersistentModuleState(bool optimized)
    {
        var image = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(FreestandingStateFixture).Assembly.Location,
            EntryPoint = typeof(FreestandingStateFixture).FullName + "::Entry",
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Freestanding,
            OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None,
            PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
            Hunk = new() { IncludeSymbols = false },
            IncludedExportNames = ["freestanding.state.add", "freestanding.state.read"],
            CodeSizeOptimizations = optimized ? new()
            {
                RemoveRedundantTransport = true, CompactPrivateStackCopies = true,
                SimplifyLoopPhis = true, CompactGuestMemory = true, NarrowOperations = true,
                EliminateRedundantInitialization = true, SizeFirstCosts = true,
                InlineMemoryHelpers = true, ShareArithmeticCores = true
            } : null
        });
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        {
            var bus = new TestBus(0x100000);
            ResidentCodeSizeExecutionTests.LoadImage(image.Image, load).CopyTo(bus.Memory.AsSpan((int)load));
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            uint Call(uint address, uint input = 0)
            {
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(address, 0x80000); cpu.State.D[0] = input;
                for (var i = 2; i < 8; i++) cpu.State.D[i] = 0x12340000u + (uint)i;
                for (var i = 2; i < 7; i++) cpu.State.A[i] = 0x56780000u + (uint)i;
                for (var step = 0; step < 10000 && cpu.State.ProgramCounter != 0x1000; step++)
                { cpu.ExecuteInstruction(); Assert.False(cpu.State.Halted); }
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(0x80004u, cpu.State.A[7]);
                for (var i = 2; i < 8; i++) Assert.Equal(0x12340000u + (uint)i, cpu.State.D[i]);
                for (var i = 2; i < 7; i++) Assert.Equal(0x56780000u + (uint)i, cpu.State.A[i]);
                return cpu.State.D[0];
            }
            Assert.Equal(1u, Call(load + image.EntryPoint));
            var add = bus.ReadLong(0x40000); var read = bus.ReadLong(0x40004);
            Assert.InRange(add, load, load + (uint)image.Code.Length - 1);
            Assert.InRange(read, load, load + (uint)image.Code.Length - 1);
            Assert.NotEqual(add, read);
            Assert.Equal(8u, Call(add, 7));
            Assert.Equal(8u, Call(read));
            Assert.Equal(10u, Call(add, 2));
            Assert.Equal(10u, Call(read));
        }
    }
}
