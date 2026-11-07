using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class InternalBooleanTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void InternalBooleanReturnsCanonicalD0AndCanBeUsedByARegisterCaller(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(InternalBooleanFixture).Assembly.Location,
                EntryPoint = typeof(InternalBooleanFixture).FullName + "::Empty",
                ManagedAssemblyPaths = [typeof(APTR).Assembly.Location], IncludedExportNames = ["bool.native", "bool.caller"],
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
            foreach (var name in new[] { "bool.native", "bool.caller" })
            foreach (uint actual in new[] { 0u, 1u, 0x12345678u, 0x80000000u, 0xFFFFFFFFu })
            foreach (uint expected in new[] { actual, actual ^ 1 })
            {
                bus.WriteLong(0x30000, actual); bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == name).Address, 0x80000);
                for (var n = 2; n < 8; n++) cpu.State.D[n] = 0xD5000000u + (uint)n;
                for (var n = 2; n < 7; n++) cpu.State.A[n] = 0xA5000000u + (uint)n;
                cpu.State.A[0] = 0x30000; cpu.State.D[0] = expected;
                var data = cpu.State.D.Skip(2).ToArray(); var addresses = cpu.State.A.Skip(2).Take(5).ToArray();
                for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(name == "bool.native" ? (actual == expected ? 1u : 0u) :
                    actual == expected ? 0x12345678u : 0x87654321u, cpu.State.D[0]);
                Assert.Equal(data, cpu.State.D.Skip(2)); Assert.Equal(addresses, cpu.State.A.Skip(2).Take(5));
                Assert.Equal(0x80004u, cpu.State.A[7]);
            }
        }
    }
}
