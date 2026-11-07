using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IntrinsicPhiExecutionTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void LoadedHeadSurvivesAddressCopyAndControlFlowMerge(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(IntrinsicPhiFixture).Assembly.Location,
                EntryPoint = typeof(IntrinsicPhiFixture).FullName + "::Empty",
                IncludedExportNames = ["intrinsic.phi.load"], Cpu = target,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new TestBus(0x100000);
            result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations) {
                var at = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            const uint list = 0x30000, head = 0x30100, next = 0x30200;
            foreach (var occupied in new[] { true, false }) {
                bus.WriteLong(list, head); bus.WriteLong(head, occupied ? next : 0);
                bus.WriteLong(next + 4, head); bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == "intrinsic.phi.load").Address, 0x80000);
                cpu.State.A[0] = list; cpu.State.D[1] = 0xDEADBEEF;
                for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(occupied ? head : 0, cpu.State.D[0]);
                Assert.Equal(occupied ? next : head, bus.ReadLong(list));
                Assert.Equal(occupied ? list : head, bus.ReadLong(next + 4));
                Assert.Equal(occupied ? next : 0, bus.ReadLong(head));
            }
        }
    }
}
