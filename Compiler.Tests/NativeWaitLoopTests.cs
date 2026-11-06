using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeWaitLoopTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void LoopInvariantRegisterArgumentSurvivesAZeroResultCallback(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeWaitLoopFixture).Assembly.Location,
                EntryPoint = typeof(NativeWaitLoopFixture).FullName + "::Empty",
                IncludedExportNames = ["wait.loop"], Cpu = target,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new TestBus(0x100000); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations) {
                uint at = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            int calls = 0;
            bus.RegisterGateway(0x3000, state => {
                calls++; Assert.Equal(0x9000u, state.A[6]); Assert.Equal(0x20u, state.D[0]);
                bus.WriteLong(0x9000, 0x120); state.D[0] = 0;
                state.D[1] = 0xDEAD0001; state.A[0] = 0xDEAD0008; state.A[1] = 0xDEAD0009;
            });
            using var core = M68kCoreFactory.Default.Create(model, bus);
            bus.WriteLong(0x80000, 0x1000);
            core.Reset(0x10000 + result.Symbols.Single(s => s.Name == "wait.loop").Address, 0x80000);
            core.State.D[0] = 0x20; core.State.A[6] = 0x9000;
            for (int n = 2; n < 8; n++) core.State.D[n] = 0xA5000000u + (uint)n;
            for (int n = 2; n < 6; n++) core.State.A[n] = 0xA5000008u + (uint)n;
            var savedD = core.State.D.Skip(2).ToArray(); var savedA = core.State.A.Skip(2).Take(5).ToArray();
            for (int n = 0; n < 2000 && core.State.ProgramCounter != 0x1000; n++) core.ExecuteInstruction();
            Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(1, calls);
            Assert.Equal(0x20u, core.State.D[0]); Assert.Equal(0x9000u, core.State.A[6]);
            Assert.Equal(0x80004u, core.State.A[7]);
            Assert.Equal(savedD, core.State.D.Skip(2)); Assert.Equal(savedA, core.State.A.Skip(2).Take(5));
        }
    }
}
