using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeAtomicOrTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void LongwordOrIsOneSizedMemoryUpdateAndPreservesTheBase(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var name in new[] { "or.fixed", "or.dynamic", "or.default" })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeAtomicOrFixture).Assembly.Location,
                EntryPoint = typeof(NativeAtomicOrFixture).FullName + "::Empty", IncludedExportNames = [name],
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode
            });
            var bus = new OrBus(); result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                uint at = 0x10000u + (uint)reloc.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            foreach (int offset in name == "or.fixed" ? new[] { 26 } : new[] { -32770, -26, 0, 26, 32768 })
            foreach (uint before in new[] { 0u, 0x12345678u, 0xFFFFFFFFu })
            foreach (uint bits in new[] { 0u, 1u, 0x80000000u, 0x5555AAAAu, 0xFFFFFFFFu })
            {
                bus.Target = unchecked(0x30000u + (uint)offset);
                bus.Inner.Memory[bus.Target - 1] = 0xA5; bus.Inner.Memory[bus.Target + 4] = 0x5A;
                bus.Inner.WriteLong(bus.Target, before); bus.Inner.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == name).Address, 0x80000);
                for (int n = 2; n <= 7; n++) core.State.D[n] = 0xD0000000u + (uint)n;
                for (int n = 2; n <= 6; n++) core.State.A[n] = 0xA0000000u + (uint)n;
                core.State.A[1] = 0x30000; core.State.D[0] = bits; core.State.D[1] = unchecked((uint)offset);
                bus.ReadBytes = bus.WriteBytes = 0;
                int memoryInstructions = 0;
                for (int n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    ushort op = bus.Inner.ReadWord(core.State.ProgramCounter);
                    int reads = bus.ReadBytes, writes = bus.WriteBytes;
                    core.ExecuteInstruction();
                    if (reads != bus.ReadBytes || writes != bus.WriteBytes) {
                        memoryInstructions++;
                        Assert.Equal(0x8180, op & 0xF1C0); // OR.L Dn,<ea>.
                        Assert.Equal(4, bus.ReadBytes - reads); Assert.Equal(4, bus.WriteBytes - writes);
                    }
                }
                Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(1, memoryInstructions);
                Assert.Equal(before | bits, bus.Inner.ReadLong(bus.Target));
                Assert.Equal(0x30000u, core.State.D[0]); // Dynamic addressing must preserve its input base.
                Assert.Equal(0x80004u, core.State.A[7]);
                for (int n = 2; n <= 7; n++) Assert.Equal(0xD0000000u + (uint)n, core.State.D[n]);
                for (int n = 2; n <= 6; n++) Assert.Equal(0xA0000000u + (uint)n, core.State.A[n]);
                Assert.Equal(0xA5, bus.Inner.Memory[bus.Target - 1]); Assert.Equal(0x5A, bus.Inner.Memory[bus.Target + 4]);
            }
        }
    }

    private sealed class OrBus : IM68kBus
    {
        public readonly TestBus Inner = new(0x100000);
        public uint Target;
        public int ReadBytes, WriteBytes;
        public void ResetExternalDevices(long cycle) => Inner.ResetExternalDevices(cycle);
        private void Read(uint at, int bytes) { if (at >= Target && at < Target + 4) ReadBytes += bytes; }
        private void Write(uint at, int bytes) { if (at >= Target && at < Target + 4) WriteBytes += bytes; }
        public byte ReadByte(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 1); return Inner.ReadByte(at, ref cycle, kind); }
        public ushort ReadWord(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 2); return Inner.ReadWord(at, ref cycle, kind); }
        public uint ReadLong(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 4); return Inner.ReadLong(at, ref cycle, kind); }
        public void WriteByte(uint at, byte value, ref long cycle, M68kBusAccessKind kind) { Write(at, 1); Inner.WriteByte(at, value, ref cycle, kind); }
        public void WriteWord(uint at, ushort value, ref long cycle, M68kBusAccessKind kind) { Write(at, 2); Inner.WriteWord(at, value, ref cycle, kind); }
        public void WriteLong(uint at, uint value, ref long cycle, M68kBusAccessKind kind) { Write(at, 4); Inner.WriteLong(at, value, ref cycle, kind); }
    }
}
