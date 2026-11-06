using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeImmediateStoreTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void ConstantAddressesAndValuesUseOneAbsoluteSizedStore(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var (name, width, value) in new[] { ("byte", 1, 0xEFu), ("word", 2, 0xC000u) })
        {
            var export = "store." + name + ".absolute";
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
                EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
                Cpu = target, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new StoreBus { Target = 0x30000 - 170, Width = width };
            result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                uint at = 0x10000u + (uint)reloc.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            foreach (bool zero in new[] { false, true }) {
                bus.Inner.Memory.AsSpan((int)bus.Target - 1, width + 2).Fill(0xA5);
                bus.Inner.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
                core.State.D[1] = zero ? 1u : 0u; bus.ReadBytes = bus.WriteBytes = 0;
                for (int n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    ushort opcode = bus.Inner.ReadWord(core.State.ProgramCounter);
                    int written = bus.WriteBytes; core.ExecuteInstruction();
                    if (bus.WriteBytes != written) Assert.Equal(width == 1 ? 0x13FC : 0x33FC, opcode);
                }
                Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(0x80004u, core.State.A[7]);
                uint actual = width == 1 ? bus.Inner.Memory[bus.Target] : bus.Inner.ReadWord(bus.Target);
                Assert.Equal(zero ? 0u : value, actual); Assert.Equal(0, bus.ReadBytes); Assert.Equal(width, bus.WriteBytes);
                Assert.Equal(0xA5, bus.Inner.Memory[bus.Target - 1]); Assert.Equal(0xA5, bus.Inner.Memory[bus.Target + (uint)width]);
            }
        }
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void ConstantStoresKeepWidthsAndNeverReadTheDestination(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var (name, width, value) in new[] { ("byte", 1, 0xEFu), ("word", 2, 0xC000u), ("long", 4, 0x89ABCDEFu) })
        foreach (bool fixedOffset in new[] { false, true })
        foreach (bool defaultAbi in new[] { false, true })
        {
            var export = "store." + name + (fixedOffset ? ".fixed" : "") + (defaultAbi ? ".default" : "");
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
                EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty",
                IncludedExportNames = [export], Cpu = target,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new StoreBus(); result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                uint at = 0x10000u + (uint)reloc.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            foreach (int offset in fixedOffset ? new[] { 170 } : new[] { -32770, -170, 0, 170, 32768 })
            foreach (bool zero in new[] { false, true })
            {
                bus.Target = unchecked(0x30000u + (uint)offset); bus.Width = width;
                bus.Inner.Memory.AsSpan((int)bus.Target - 1, width + 2).Fill(0xA5);
                bus.Inner.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
                core.State.A[6] = 0x30000; core.State.D[0] = unchecked((uint)offset); core.State.D[1] = zero ? 1u : 0u;
                core.State.A[0] = 0x30000;
                bus.ReadBytes = bus.WriteBytes = 0;
                for (int n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++)
                {
                    var opcode = bus.Inner.ReadWord(core.State.ProgramCounter);
                    var written = bus.WriteBytes;
                    core.ExecuteInstruction();
                    if (defaultAbi && fixedOffset && width < 4 && bus.WriteBytes != written)
                        Assert.Equal(0x3C, opcode & 0x3F); // Direct immediate byte/word store.
                }
                Assert.Equal(0x1000u, core.State.ProgramCounter);
                uint actual = 0;
                for (int n = 0; n < width; n++) actual = (actual << 8) | bus.Inner.Memory[bus.Target + (uint)n];
                Assert.Equal(zero ? 0u : value, actual);
                Assert.Equal(0, bus.ReadBytes); Assert.Equal(width, bus.WriteBytes);
                Assert.Equal(0xA5, bus.Inner.Memory[bus.Target - 1]);
                Assert.Equal(0xA5, bus.Inner.Memory[bus.Target + (uint)width]);
                Assert.Equal(0x30000u, core.State.A[6]); Assert.Equal(0x80004u, core.State.A[7]);
            }
        }
    }

    private sealed class StoreBus : IM68kBus
    {
        public readonly TestBus Inner = new(0x100000);
        public uint Target;
        public int Width, ReadBytes, WriteBytes;
        public void ResetExternalDevices(long cycle) => Inner.ResetExternalDevices(cycle);
        private void Read(uint at, int bytes) { if (at >= Target && at < Target + Width) ReadBytes += bytes; }
        private void Write(uint at, int bytes) { if (at >= Target && at < Target + Width) WriteBytes += bytes; }
        public byte ReadByte(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 1); return Inner.ReadByte(at, ref cycle, kind); }
        public ushort ReadWord(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 2); return Inner.ReadWord(at, ref cycle, kind); }
        public uint ReadLong(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 4); return Inner.ReadLong(at, ref cycle, kind); }
        public void WriteByte(uint at, byte value, ref long cycle, M68kBusAccessKind kind) { Write(at, 1); Inner.WriteByte(at, value, ref cycle, kind); }
        public void WriteWord(uint at, ushort value, ref long cycle, M68kBusAccessKind kind) { Write(at, 2); Inner.WriteWord(at, value, ref cycle, kind); }
        public void WriteLong(uint at, uint value, ref long cycle, M68kBusAccessKind kind) { Write(at, 4); Inner.WriteLong(at, value, ref cycle, kind); }
    }
}
