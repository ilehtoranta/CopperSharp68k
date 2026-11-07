using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeInterruptMaskTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void DiscardedStatusStillMasksInterruptsAndPreservesLiveRegisters(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeInterruptMaskFixture).Assembly.Location,
                EntryPoint = typeof(NativeInterruptMaskFixture).FullName + "::Empty", IncludedExportNames = ["mask.discard"],
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode
            });
            var bus = new TestBus(0x100000); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                var at = 0x10000u + (uint)reloc.Offset;
                bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            foreach (ushort status in new ushort[] { 0x2000, 0x2015, 0x231F, 0x2704 })
            {
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == "mask.discard").Address, 0x80000);
                cpu.State.StatusRegister = status;
                for (var n = 0; n < 8; n++) cpu.State.D[n] = 0xD5000000u + (uint)n;
                for (var n = 0; n < 7; n++) cpu.State.A[n] = 0xA5000000u + (uint)n;
                var data = cpu.State.D.ToArray(); var addresses = cpu.State.A.Take(7).ToArray();
                for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal((status | 0x0700) & 0xFF00, cpu.State.StatusRegister & 0xFF00);
                Assert.Equal(data[0], cpu.State.D[0]);
                Assert.Equal(data.Skip(2), cpu.State.D.Skip(2));
                Assert.Equal(addresses.Skip(2), cpu.State.A.Skip(2).Take(5));
                Assert.Equal(0x80004u, cpu.State.A[7]);
            }
        }
    }
}
