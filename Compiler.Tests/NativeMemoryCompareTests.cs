using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeMemoryCompareTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void MemoryPredicatesKeepTheReadWidthOrderAndPreservedRegisters(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var name in new[] { "byte", "word", "changed", "live", "byte.default", "word.default", "changed.default", "live.default" })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeMemoryCompareFixture).Assembly.Location,
                EntryPoint = typeof(NativeMemoryCompareFixture).FullName + "::Empty",
                IncludedExportNames = ["compare." + name], Cpu = target,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new ReadBus(); result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations) {
                uint at = 0x10000u + (uint)relocation.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            var kind = name.Split('.')[0];
            bool word = kind == "word";
            foreach (int value in word ? new[] { 0, 1, 4, 255, 256, 0x4000, 0x8000, 0xFFFF } : Enumerable.Range(0, 256))
            {
                bus.Target = word ? 0x900Eu : 0x900Fu; bus.Width = word ? 2 : 1;
                bus.Inner.Memory.AsSpan(0x9000, 32).Fill(0xA5);
                if (word) bus.Inner.WriteWord(bus.Target, (ushort)value); else bus.Inner.Memory[bus.Target] = (byte)value;
                bus.Inner.WriteLong(0x9016, 0x000F00F0); bus.Inner.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == "compare." + name).Address, 0x80000);
                core.State.A[1] = 0x9000; core.State.D[0] = 0x12345678;
                for (int n = 2; n < 8; n++) core.State.D[n] = 0xD0000000u + (uint)n;
                for (int n = 2; n < 7; n++) core.State.A[n] = 0xA0000000u + (uint)n;
                var savedD = core.State.D.Skip(2).ToArray(); var savedA = core.State.A.Skip(2).Take(5).ToArray();
                int predicates = 0; bus.Reads = bus.Writes = 0;
                for (int n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    ushort op = bus.Inner.ReadWord(core.State.ProgramCounter);
                    if ((op & 0xFFF8) == (word ? 0x0C68 : 0x0C28)) predicates++;
                    core.ExecuteInstruction();
                }
                bool match = value == (word ? 0x4000 : 4);
                uint expected = kind == "live" ? match ? 0x12345678u + (uint)value : 0x12345678u ^ (uint)value
                    : match ? 0x12345678u & 0x000F00F0u : 0x12345678u;
                Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(expected, core.State.D[0]);
                Assert.Equal(kind is "byte" or "word" ? 1 : 0, predicates);
                Assert.Equal(1, bus.Reads); Assert.Equal(kind == "changed" ? 1 : 0, bus.Writes);
                Assert.Equal(savedD, core.State.D.Skip(2)); Assert.Equal(savedA, core.State.A.Skip(2).Take(5));
                Assert.Equal(0x80004u, core.State.A[7]);
                Assert.Equal((byte)0xA5, bus.Inner.Memory[bus.Target - 1]);
                Assert.Equal((byte)0xA5, bus.Inner.Memory[bus.Target + (uint)bus.Width]);
            }
        }
    }

    private sealed class ReadBus : IM68kBus
    {
        public readonly TestBus Inner = new(0x100000);
        public uint Target; public int Width, Reads, Writes;
        private void Read(uint at, int width) { if (at <= Target && at + width > Target) { Assert.Equal(Target, at); Assert.Equal(Width, width); Reads++; } }
        private void Write(uint at, int width) { if (at <= Target && at + width > Target) { Assert.Equal(Target, at); Assert.Equal(Width, width); Writes++; } }
        public void ResetExternalDevices(long cycle) => Inner.ResetExternalDevices(cycle);
        public byte ReadByte(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 1); return Inner.ReadByte(at, ref cycle, kind); }
        public ushort ReadWord(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 2); return Inner.ReadWord(at, ref cycle, kind); }
        public uint ReadLong(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 4); return Inner.ReadLong(at, ref cycle, kind); }
        public void WriteByte(uint at, byte value, ref long cycle, M68kBusAccessKind kind) { Write(at, 1); Inner.WriteByte(at, value, ref cycle, kind); }
        public void WriteWord(uint at, ushort value, ref long cycle, M68kBusAccessKind kind) { Write(at, 2); Inner.WriteWord(at, value, ref cycle, kind); }
        public void WriteLong(uint at, uint value, ref long cycle, M68kBusAccessKind kind) { Write(at, 4); Inner.WriteLong(at, value, ref cycle, kind); }
    }
}
