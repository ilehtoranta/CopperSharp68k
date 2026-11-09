using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class UInt32SpanBlockCopyTests
{
    [Theory]
    [InlineData("ByteEntry", 64)]
    public void CopyCounterLimitAndLargerScalarFallbackPreserveAllBytes(string entry, int groupBytes)
    {
        var compiled = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(UInt32SpanBlockFixture).Assembly.Location,
            EntryPoint = typeof(UInt32SpanBlockFixture).FullName + "::" + entry, IncludedExportNames = [],
            Cpu = M68kCpuTarget.M68020, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint
        });
        foreach (var length in new[] { groupBytes * 65536 - 1, groupBytes * 65536, groupBytes * 65536 + 1 })
        {
            var bus = new TestBus(); compiled.Code.CopyTo(bus.Memory, 0x10000);
            const int source = 0x200000, destination = 0x800000;
            for (var n = 0; n < length; n++) bus.Memory[source + n] = (byte)(n * 17 + 3);
            bus.Memory.AsSpan(destination - 4, length + 8).Fill(0xA9);
            bus.WriteLong(0x2000, source); bus.WriteLong(0x2004, destination); bus.WriteLong(0x2008, (uint)length);
            bus.WriteLong(0x80000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68020, bus);
            cpu.Reset(0x10000 + compiled.EntryPoint, 0x80000);
            var steps = 0;
            while (cpu.State.ProgramCounter != 0x1000 && steps++ < 50_000_000) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.True(bus.Memory.AsSpan(source, length).SequenceEqual(bus.Memory.AsSpan(destination, length)));
            Assert.All(bus.Memory.AsSpan(destination - 4, 4).ToArray(), b => Assert.Equal(0xA9, b));
            Assert.All(bus.Memory.AsSpan(destination + length, 4).ToArray(), b => Assert.Equal(0xA9, b));
        }
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68040)]
    public void BorrowedUIntCopiesPreserveOverlapRegistersAndRequestedMemoryBounds(M68kCpuTarget target, M68kCpuModel model)
        => RunCopies(target, model, false);

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68040)]
    public void BorrowedByteCopiesPreserveOverlapRegistersAndRequestedMemoryBounds(M68kCpuTarget target, M68kCpuModel model)
        => RunCopies(target, model, true);

    private static void RunCopies(M68kCpuTarget target, M68kCpuModel model, bool bytes)
    {
        var compiled = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(UInt32SpanBlockFixture).Assembly.Location,
            EntryPoint = typeof(UInt32SpanBlockFixture).FullName + (bytes ? "::ByteEntry" : "::Entry"), IncludedExportNames = [],
            Cpu = target, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint
        });
        Assert.Empty(compiled.FrameworkAnalysis.ManagedAllocationSites);
        foreach (var count in bytes ? new[] { 0, 1, 7, 31, 32, 63, 64, 65, 127, 128, 129, 255, 256, 257, 512, 4096 } : new[] { 0, 1, 7, 8, 9, 15, 16, 17, 31, 32, 33, 128, 1024 })
        foreach (var alignment in bytes ? new[] { 0, 1, 2 } : new[] { 0, 2 })
        foreach (var distance in bytes ? new[] { -33, -32, -4, -1, 0, 1, 4, 32, 33, 8192 } : new[] { -32, -4, 0, 4, 32, 8192 })
        {
            var length = count * (bytes ? 1 : 4);
            var bus = new CopyBus();
            compiled.Code.CopyTo(bus.Inner.Memory, 0x10000);
            const uint start = 0x10000;
            var source = 0x3040 + alignment;
            var destination = source + distance;
            for (var n = 0x3000; n < 0x9000; n++) bus.Inner.Memory[n] = (byte)(n * 17 + 31);
            var expected = bus.Inner.Memory.AsSpan(0x3000, 0x6000).ToArray();
            Array.Copy(expected, source - 0x3000, expected, destination - 0x3000, length);
            bus.Inner.WriteLong(0x2000, (uint)source);
            bus.Inner.WriteLong(0x2004, (uint)destination);
            bus.Inner.WriteLong(0x2008, (uint)count);
            bus.Inner.WriteLong(0x80000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(start + compiled.EntryPoint, 0x80000);
            for (var n = 4; n < 8; n++) cpu.State.D[n] = 0xA55A0000u + (uint)n;
            var steps = 0;
            while (cpu.State.ProgramCounter != 0x1000 && steps++ < 100000)
            {
                cpu.ExecuteInstruction();
                Assert.False(cpu.State.Halted);
            }
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal(expected, bus.Inner.Memory.AsSpan(0x3000, 0x6000).ToArray());
            for (var n = 4; n < 8; n++) Assert.Equal(0xA55A0000u + (uint)n, cpu.State.D[n]);
            Assert.All(bus.Reads, access => Assert.True(access.Address >= source && access.Address + access.Bytes <= source + length,
                $"Read {access.Address:X}/{access.Bytes} outside {source:X}+{length}"));
            Assert.All(bus.Writes, access => Assert.True(access.Address >= destination && access.Address + access.Bytes <= destination + length));
        }
    }

    private sealed class CopyBus : IM68kBus
    {
        public TestBus Inner { get; } = new(0x100000);
        public List<(uint Address, int Bytes)> Reads { get; } = [];
        public List<(uint Address, int Bytes)> Writes { get; } = [];
        private void Record(uint address, int bytes, bool write) { if (address >= 0x3000 && address < 0x9000) (write ? Writes : Reads).Add((address, bytes)); }
        public byte ReadByte(uint a, ref long c, M68kBusAccessKind k) { Record(a,1,false);return Inner.ReadByte(a,ref c,k); }
        public ushort ReadWord(uint a, ref long c, M68kBusAccessKind k) { Record(a,2,false);return Inner.ReadWord(a,ref c,k); }
        public uint ReadLong(uint a, ref long c, M68kBusAccessKind k) { Record(a,4,false);return Inner.ReadLong(a,ref c,k); }
        public void WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) { Record(a,1,true);Inner.WriteByte(a,v,ref c,k); }
        public void WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) { Record(a,2,true);Inner.WriteWord(a,v,ref c,k); }
        public void WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) { Record(a,4,true);Inner.WriteLong(a,v,ref c,k); }
        public void ResetExternalDevices(long cycle) { }
        public bool HasHostGateway(uint address) => false;
        public bool TryInvokeHostGateway(uint pc,uint token,M68kCpuState state) => false;
    }
}

public static class UInt32SpanBlockFixture
{
    public static unsafe void ByteEntry()
    {
        var source = APTR.ReadUInt32((APTR)0x2000, 0);
        var destination = APTR.ReadUInt32((APTR)0x2004, 0);
        var count = (int)APTR.ReadUInt32((APTR)0x2008, 0);
        new Span<byte>((void*)source, count).CopyTo(new Span<byte>((void*)destination, count));
    }
    public static unsafe void Entry()
    {
        var source = APTR.ReadUInt32((APTR)0x2000, 0);
        var destination = APTR.ReadUInt32((APTR)0x2004, 0);
        var count = (int)APTR.ReadUInt32((APTR)0x2008, 0);
        new Span<uint>((void*)source, count).CopyTo(new Span<uint>((void*)destination, count));
    }
}
