using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeBitTestAndSetTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void BitUpdateIsOneInstructionAndReturnsTheOldBit(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (string method in new[] { "Dynamic", "Constant", "Discarded", "SetBranch" }) {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeBitTestAndSetFixture).Assembly.Location,
                EntryPoint = typeof(NativeBitTestAndSetFixture).FullName + "::Empty",
                IncludedExportNames = [method], Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo
            });
            var bus = new TestBus(); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                uint at = 0x10000u + (uint)reloc.Offset; bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            foreach (uint bit in new uint[] { 0, 1, 7, 8, 31, uint.MaxValue })
            foreach (byte before in new byte[] { 0, 1, 0x7F, 0x80, 0xFE, 0xFF }) {
                bus.Memory[0x8FFF] = 0xA5; bus.Memory[0x9001] = 0x5A;
                bus.Memory[0x9000] = before; bus.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == method).Address, 0x80000);
                core.State.D[0] = 0x9000; core.State.D[1] = bit;
                int updates = 0;
                for (int n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    uint pc = core.State.ProgramCounter;
                    ushort op = bus.ReadWord(pc);
                    byte old = bus.Memory[0x9000]; core.ExecuteInstruction();
                    if (method == "SetBranch" && ((op & 0xF1C0) == 0x01C0 || (op & 0xFFC0) == 0x08C0))
                        Assert.Equal(0x6000, bus.ReadWord(core.State.ProgramCounter) & 0xF000);
                    if (old != bus.Memory[0x9000]) {
                        updates++;
                        Assert.True((op & 0xF1C0) == 0x01C0 || (op & 0xFFC0) == 0x08C0,
                            $"Update split across instructions: {op:X4}");
                    }
                }
                uint mask = 1u << (method == "Dynamic" ? (int)(bit & 7) : 0);
                Assert.Equal(0x1000u, core.State.ProgramCounter);
                Assert.Equal((byte)(before | mask), bus.Memory[0x9000]);
                Assert.Equal((before & mask) == 0 ? 1 : 0, updates);
                Assert.Equal(method == "Discarded" ? (uint)(before | mask) : method == "SetBranch"
                    ? (before & mask) == 0 ? 29u : 17u : (before & mask) == 0 ? 0u : 1u, core.State.D[0]);
                Assert.Equal(0xA5, bus.Memory[0x8FFF]); Assert.Equal(0x5A, bus.Memory[0x9001]);
                Assert.Equal(0x80004u, core.State.A[7]);
            }
        }
    }
}

public static class NativeBitTestAndSetFixture
{
    public static void Empty() { }
    [M68kExport("Dynamic")]
    public static bool Dynamic([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => APTR.BitTestAndSet8(pointer, (int)bit);
    [M68kExport("Constant")]
    public static bool Constant([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => APTR.BitTestAndSet8(pointer, 0);
    [M68kExport("Discarded")]
    public static uint Discarded([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) {
        _ = APTR.BitTestAndSet8(pointer, 0);
        return APTR.ReadUInt8(pointer, 0);
    }
    [M68kExport("SetBranch")]
    public static uint Branch([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => APTR.BitTestAndSet8(pointer, 0) ? 17u : 29u;
}
