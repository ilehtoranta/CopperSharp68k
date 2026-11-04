using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeWordUpdateTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void WordUpdatesAreSingleInstructionsWithWrappedResultFlags(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var increment in new[] { false, true })
        foreach (var form in new[] { "dynamic", "constant", "discarded", "address" })
        {
            var name = "word." + (increment ? "inc." : "dec.") + form;
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeWordUpdateFixture).Assembly.Location,
                EntryPoint = typeof(NativeWordUpdateFixture).FullName + "::Empty",
                IncludedExportNames = [name], Cpu = target,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
            });
            var bus = new TestBus();
            var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
            code.CopyTo(bus.Memory, 0x10000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            foreach (int offset in new[] { -4, 0, 6 })
            foreach (ushort before in new ushort[] { 0, 1, 0x7FFF, 0x8000, 0xFFFF })
            {
                uint address = (uint)(0x9000 + (form == "constant" ? 6 : offset));
                bus.Memory[address - 1] = 0xA5; bus.Memory[address + 2] = 0x5A;
                bus.WriteWord(address, before); bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == name).Address, 0x80000);
                cpu.State.D[0] = 0x9000; cpu.State.D[1] = unchecked((uint)offset);
                cpu.State.A[0] = 0x9000;
                int updates = 0;
                for (int n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++)
                {
                    ushort opcode = bus.ReadWord(cpu.State.ProgramCounter), old = bus.ReadWord(address);
                    cpu.ExecuteInstruction();
                    if (old != bus.ReadWord(address))
                    {
                        updates++;
                        Assert.True((opcode & 0xFFF8) == (increment ? 0x5250 : 0x5350) ||
                            (opcode & 0xFFF8) == (increment ? 0x5268 : 0x5368), $"Split word update: {opcode:X4}");
                    }
                }
                ushort after = unchecked((ushort)(before + (increment ? 1 : -1)));
                uint expected = form is "discarded" or "address" ? after : (increment ? after == 0 : (after & 0x8000) != 0) ? 1u : 0u;
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(after, bus.ReadWord(address)); Assert.Equal(1, updates);
                Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(0x80004u, cpu.State.A[7]);
                Assert.Equal(0xA5, bus.Memory[address - 1]); Assert.Equal(0x5A, bus.Memory[address + 2]);
                Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
            }
        }
    }
}
