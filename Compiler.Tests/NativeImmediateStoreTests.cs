using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeImmediateStoreTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000)]
    [InlineData(M68kCpuModel.M68EC020)]
    public void LongLiteralsKeepBothWordsForRelativeAndAbsoluteStores(M68kCpuModel model)
    {
        const string export = "store.long.literal";
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
            EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
            Cpu = M68kCpuTarget.M68000, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
            PeepholeOptimization = M68kPeepholeOptimizationMode.Disabled
        });
        var bus = new StoreBus { Target = 0x30006, Width = 16 };
        result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
        foreach (var reloc in result.Relocations) {
            uint at = 0x10000u + (uint)reloc.Offset;
            bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
        }
        bus.Inner.Memory.AsSpan(0x30005, 18).Fill(0xA5);
        bus.Inner.WriteLong(0x80000, 0x1000);
        using var core = M68kCoreFactory.Default.Create(model, bus);
        core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
        core.State.A[1] = 0x30000;
        var executed = new List<ushort>();
        for (int i = 0; i < 100 && core.State.ProgramCounter != 0x1000; i++) {
            executed.Add(bus.Inner.ReadWord(core.State.ProgramCounter));
            core.ExecuteInstruction();
        }
        Assert.Equal(0x1000u, core.State.ProgramCounter);
        Assert.Equal(3, executed.Count(op => op == 0x237C)); // MOVE.L #imm,d16(A1)
        Assert.Contains((ushort)0x23FC, executed); // MOVE.L #imm,abs.L
        Assert.Equal(0x010100FFu, bus.Inner.ReadLong(0x30006));
        Assert.Equal(0xABCD0123u, bus.Inner.ReadLong(0x3000A));
        Assert.Equal(0x01AA0000u, bus.Inner.ReadLong(0x3000E));
        Assert.Equal(0xFFFF0000u, bus.Inner.ReadLong(0x30012));
        Assert.Equal(0, bus.ReadBytes);
        Assert.Equal(16, bus.WriteBytes);
        Assert.Equal(0xA5, bus.Inner.Memory[0x30005]);
        Assert.Equal(0xA5, bus.Inner.Memory[0x30016]);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68000)]
    [InlineData(M68kCpuModel.M68EC020)]
    public void NarrowStoresPreserveTheFullInputAndOtherConsumersOfTheConversion(M68kCpuModel model)
    {
        const string export = "store.truncated-integer";
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
            EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
            Cpu = M68kCpuTarget.M68000, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
        });
        var bus = new StoreBus { Target = 0x30000, Width = 4 };
        result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
        bus.Inner.WriteLong(bus.Target, 0xA5A5A5A5);
        bus.Inner.WriteLong(0x80000, 0x1000);
        using var core = M68kCoreFactory.Default.Create(model, bus);
        core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
        core.State.A[1] = bus.Target;
        core.State.D[0] = 0x89ABCDEF;
        for (int i = 0; i < 64 && core.State.ProgramCounter != 0x1000; i++) core.ExecuteInstruction();
        Assert.Equal(0x1000u, core.State.ProgramCounter);
        Assert.Equal(0x89AB0000u, core.State.D[0]);
        Assert.Equal(0xCDEFEFA5u, bus.Inner.ReadLong(bus.Target));
    }

    [Theory]
    [InlineData("float", 0x3F800000u)]
    [InlineData("null", 0u)]
    public void SharedAddressSelectionDoesNotDecodeOtherConstantKindsAsAddresses(string kind, uint expected)
    {
        var export = "store.word.shared-" + kind;
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
            EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
            Cpu = M68kCpuTarget.M68000, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            FloatingPoint = M68kFloatingPointMode.SoftFloat,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
        });
        var bus = new StoreBus { Target = 0x30000, Width = 2 };
        result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
        foreach (var relocation in result.Relocations) {
            var at = 0x10000u + (uint)relocation.Offset;
            bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
        }
        bus.Inner.WriteLong(0x80000, 0x1000);
        using var core = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
        core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
        for (var n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) core.ExecuteInstruction();
        Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(expected, core.State.D[0]);
        Assert.Equal(new uint[] { 0x4000, 0xC000 }, bus.Writes.Select(w => w.Value));
        Assert.Equal(0, bus.ReadBytes); Assert.Equal(4, bus.WriteBytes);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    [InlineData(M68kCpuTarget.M68040, M68kCpuModel.M68040)]
    public void LongStoresUseIncomingAddressRegisterValuesWithoutStaging(M68kCpuTarget target, M68kCpuModel model)
    {
        const string export = "store.long.address-register";
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
                EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
                Cpu = target, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new StoreBus { Target = 0x30000, Width = 12 };
            result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations) {
                var at = 0x10000u + (uint)relocation.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            foreach (var value in new uint[] { 0, 0x30000, 0x12345678, 0x89ABCDEF }) {
                bus.Inner.Memory.AsSpan((int)bus.Target, bus.Width).Fill(0xA5);
                bus.Inner.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
                core.State.A[0] = value; core.State.A[1] = bus.Target;
                for (var r = 2; r < 8; r++) core.State.D[r] = 0xD0000000u + (uint)r;
                for (var r = 2; r < 7; r++) core.State.A[r] = 0xA0000000u + (uint)r;
                bus.ReadBytes = bus.WriteBytes = 0; bus.Writes.Clear();
                for (var n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    var opcode = bus.Inner.ReadWord(core.State.ProgramCounter);
                    var before = bus.WriteBytes; core.ExecuteInstruction();
                    if (bus.WriteBytes != before) Assert.Equal(8, opcode & 0x3F); // MOVE.L A0,<memory>
                }
                Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(0x80004u, core.State.A[7]);
                Assert.Equal(0, bus.ReadBytes); Assert.Equal(8, bus.WriteBytes);
                Assert.Equal(new[] { (bus.Target + 4, 4, value), (bus.Target + 8, 4, value) }, bus.Writes);
                Assert.Equal(0xA5A5A5A5u, bus.Inner.ReadLong(bus.Target));
                for (var r = 2; r < 8; r++) Assert.Equal(0xD0000000u + (uint)r, core.State.D[r]);
                for (var r = 2; r < 7; r++) Assert.Equal(0xA0000000u + (uint)r, core.State.A[r]);
            }
        }
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void SharedConstantAddressKeepsEverySizedWriteInOrder(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var width in new[] { 1, 2 })
        {
            var export = "store." + (width == 1 ? "byte" : "word") + ".shared-address";
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
                EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
                Cpu = target, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new StoreBus { Target = 0x30000, Width = width };
            result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations) {
                var at = 0x10000u + (uint)relocation.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            for (var choice = 0; choice < 3; choice++) {
                bus.Inner.WriteLong(0x80000, 0x1000);
                core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
                for (var r = 2; r < 8; r++) core.State.D[r] = 0xD0000000u + (uint)r;
                for (var r = 2; r < 7; r++) core.State.A[r] = 0xA0000000u + (uint)r;
                core.State.D[0] = (uint)choice;
                bus.ReadBytes = bus.WriteBytes = 0; bus.Writes.Clear();
                for (var n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                    var opcode = bus.Inner.ReadWord(core.State.ProgramCounter);
                    var before = bus.WriteBytes; core.ExecuteInstruction();
                    if (bus.WriteBytes != before) Assert.Equal(width == 1 ? 0x13FC : 0x33FC, opcode);
                }
                Assert.Equal(0x1000u, core.State.ProgramCounter);
                Assert.Equal(0x80004u, core.State.A[7]);
                Assert.Equal(0, bus.ReadBytes);
                uint first = width == 1 ? 0x40u : 0x4000u, last = choice == 0 ? 0u : width == 1 ? 0xC0u : 0xC000u;
                var expected = choice == 2 ? new[] { first } : new[] { first, last };
                Assert.Equal(expected, bus.Writes.Select(w => w.Value));
                Assert.All(bus.Writes, w => { Assert.Equal(bus.Target, w.Address); Assert.Equal(width, w.Bytes); });
                for (var r = 2; r < 8; r++) Assert.Equal(0xD0000000u + (uint)r, core.State.D[r]);
                for (var r = 2; r < 7; r++) Assert.Equal(0xA0000000u + (uint)r, core.State.A[r]);
            }
        }
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void RepeatedStoresAndObservedReadsRetainTheSharedAddress(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var scenario in new[] { "loop", "three", "read" })
        {
            var export = "store.word.shared-" + scenario;
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(NativeImmediateStoreFixture).Assembly.Location,
                EntryPoint = typeof(NativeImmediateStoreFixture).FullName + "::Empty", IncludedExportNames = [export],
                Cpu = target, MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                PeepholeOptimization = mode
            });
            var bus = new StoreBus { Target = 0x30000, Width = 2 };
            result.Code.CopyTo(bus.Inner.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations) {
                var at = 0x10000u + (uint)relocation.Offset;
                bus.Inner.WriteLong(at, bus.Inner.ReadLong(at) + 0x10000);
            }
            using var core = M68kCoreFactory.Default.Create(model, bus);
            bus.Inner.WriteLong(0x80000, 0x1000);
            core.Reset(0x10000 + result.Symbols.Single(s => s.Name == export).Address, 0x80000);
            for (var r = 2; r < 8; r++) core.State.D[r] = 0xD0000000u + (uint)r;
            for (var r = 2; r < 7; r++) core.State.A[r] = 0xA0000000u + (uint)r;
            core.State.D[0] = 2;
            bus.ReadBytes = bus.WriteBytes = 0; bus.Writes.Clear();
            for (var n = 0; n < 1000 && core.State.ProgramCounter != 0x1000; n++) {
                var opcode = bus.Inner.ReadWord(core.State.ProgramCounter);
                var before = bus.WriteBytes; core.ExecuteInstruction();
                if (bus.WriteBytes != before) Assert.NotEqual(0x33FC, opcode);
            }
            Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(0x80004u, core.State.A[7]);
            var expected = scenario == "loop" ? new uint[] { 0x4000, 0xC000, 0xC000 } :
                scenario == "three" ? new uint[] { 0x4000, 0xC000, 0x8000 } : new uint[] { 0x4000, 0xC000 };
            Assert.Equal(expected, bus.Writes.Select(w => w.Value));
            Assert.All(bus.Writes, w => { Assert.Equal(bus.Target, w.Address); Assert.Equal(2, w.Bytes); });
            Assert.Equal(scenario == "read" ? 2 : 0, bus.ReadBytes);
            if (scenario == "read") Assert.Equal(0x4000u, core.State.D[0]);
            for (var r = 2; r < 8; r++) Assert.Equal(0xD0000000u + (uint)r, core.State.D[r]);
            for (var r = 2; r < 7; r++) Assert.Equal(0xA0000000u + (uint)r, core.State.A[r]);
        }
    }

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
        public readonly List<(uint Address, int Bytes, uint Value)> Writes = new();
        public void ResetExternalDevices(long cycle) => Inner.ResetExternalDevices(cycle);
        private void Read(uint at, int bytes) { if (at >= Target && at < Target + Width) ReadBytes += bytes; }
        private void Write(uint at, int bytes) { if (at >= Target && at < Target + Width) WriteBytes += bytes; }
        public byte ReadByte(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 1); return Inner.ReadByte(at, ref cycle, kind); }
        public ushort ReadWord(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 2); return Inner.ReadWord(at, ref cycle, kind); }
        public uint ReadLong(uint at, ref long cycle, M68kBusAccessKind kind) { Read(at, 4); return Inner.ReadLong(at, ref cycle, kind); }
        public void WriteByte(uint at, byte value, ref long cycle, M68kBusAccessKind kind) { if (at >= Target && at < Target + Width) Writes.Add((at, 1, value)); Write(at, 1); Inner.WriteByte(at, value, ref cycle, kind); }
        public void WriteWord(uint at, ushort value, ref long cycle, M68kBusAccessKind kind) { if (at >= Target && at < Target + Width) Writes.Add((at, 2, value)); Write(at, 2); Inner.WriteWord(at, value, ref cycle, kind); }
        public void WriteLong(uint at, uint value, ref long cycle, M68kBusAccessKind kind) { if (at >= Target && at < Target + Width) Writes.Add((at, 4, value)); Write(at, 4); Inner.WriteLong(at, value, ref cycle, kind); }
    }
}
