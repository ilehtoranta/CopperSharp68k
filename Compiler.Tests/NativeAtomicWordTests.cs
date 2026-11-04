using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeAtomicWordTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void WordTransitionsAreAtomicAndUseDirectDisplacement(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (string method in new[] { "Increment", "Decrement", "DynamicIncrement", "DynamicDecrement", "Branch" }) {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeAtomicWordFixture).Assembly.Location,
                EntryPoint = typeof(NativeAtomicWordFixture).FullName + "::Empty", IncludedExportNames = [method],
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode
            });
            var bus = new TestBus(); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                uint at = 0x10000u + (uint)reloc.Offset; bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            bool increment = !method.Contains("Decrement");
            foreach (int offset in method.StartsWith("Dynamic") ? new[] { -32770, -170, 0, 170, 32768 } : new[] { 170 })
            foreach (ushort before in new ushort[] { 0, 1, 2, 0x7FFF, 0x8000, 0xFFFE, 0xFFFF }) {
                uint address = unchecked(0x9000u + (uint)offset);
                bus.Memory[address - 1] = 0xA5; bus.Memory[address + 2] = 0x5A;
                bus.WriteWord(address, before); bus.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == method).Address, 0x80000);
                core.State.A[6] = 0x9000; core.State.D[1] = unchecked((uint)offset);
                int updates = 0;
                for (int n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    uint pc = core.State.ProgramCounter; ushort op = bus.ReadWord(pc);
                    ushort old = bus.ReadWord(address); core.ExecuteInstruction();
                    if (old != bus.ReadWord(address)) {
                        updates++;
                        Assert.Equal(increment ? 0x5240 : 0x5340, op & 0xFFC0);
                        if (!method.StartsWith("Dynamic")) {
                            Assert.Equal(0x2E, op & 0x3F); // direct d16(A6), no address scratch
                            Assert.Equal(170, bus.ReadWord(pc + 2));
                        }
                    }
                }
                ushort after = unchecked((ushort)(before + (increment ? 1 : -1)));
                bool condition = increment ? after == 0 : (short)after < 0;
                Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(1, updates);
                Assert.Equal(after, bus.ReadWord(address));
                Assert.Equal(method == "Branch" ? condition ? 17u : 29u : condition ? 1u : 0u, core.State.D[0]);
                Assert.Equal(0x9000u, core.State.A[6]); Assert.Equal(0x80004u, core.State.A[7]);
                Assert.Equal(0xA5, bus.Memory[address - 1]); Assert.Equal(0x5A, bus.Memory[address + 2]);
            }
        }
    }
}

public static class NativeAtomicWordFixture
{
    public static void Empty() { }
    [M68kInternalRegisterAbi]
    [M68kExport("Increment")]
    public static uint Increment([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.IncrementWordIsZero(pointer, 170) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("Decrement")]
    public static uint Decrement([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.DecrementWordIsNegative(pointer, 170) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("DynamicIncrement")]
    public static uint DynamicIncrement([M68kRegister(M68kRegister.A6)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
        => APTR.IncrementWordIsZero(pointer, offset) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("DynamicDecrement")]
    public static uint DynamicDecrement([M68kRegister(M68kRegister.A6)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
        => APTR.DecrementWordIsNegative(pointer, offset) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("Branch")]
    public static uint Branch([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.IncrementWordIsZero(pointer, 170) ? 17u : 29u;
}
