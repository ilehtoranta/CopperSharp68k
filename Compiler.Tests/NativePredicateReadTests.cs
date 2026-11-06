using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class NativePredicateReadTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void SelectedPredicatesRetainEveryReadAndItsWidth(M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location], IncludedExportNames = ["abi.readbits"],
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, CompactGuestMemory = true, NarrowOperations = true },
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        var bus = new ReadBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Inner.Memory, 0x10000);
        uint entry = result.Symbols.Single(s => s.Name == "abi.readbits").Address;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        foreach (byte first in new byte[] { 0, 0x7F, 0x80, 0xFF })
        foreach (ushort second in new ushort[] { 0, 0x80, 0x7FFF, 0x8000, 0xFFFE, 0xFFFF }) {
            const uint stack = 0x80002;
            bus.Inner.WriteLong(stack, 0x1000); bus.Inner.WriteWord(0x3000, 0xABCD);
            bus.Inner.Memory[0x3002] = first; bus.Inner.WriteWord(0x3004, second);
            cpu.Reset(0x10000 + entry, stack); cpu.State.StatusRegister = 0x2700;
            for (int n=0;n<8;n++) cpu.State.D[n]=0xA5550000u+(uint)n;
            for (int n=0;n<7;n++) cpu.State.A[n]=0xA7770000u+(uint)n;
            cpu.State.A[1]=0x3002;
            var d=cpu.State.D.ToArray(); var a=cpu.State.A.ToArray(); bus.Reads.Clear();
            for (int n=0;n<2000 && cpu.State.ProgramCounter!=0x1000;n++) cpu.ExecuteInstruction();
            // The 68000 exposes a long read as two ordered 16-bit bus accesses.
            var expectedReads = new List<(uint Address, int Width)> { (0x3000, 2), (0x3002, 1), (0x3004, 2), (0x3006, 1) };
            if (model == M68kCpuModel.M68000) { expectedReads.Add((0x3008, 2)); expectedReads.Add((0x300A, 2)); }
            else expectedReads.Add((0x3008, 4));
            Assert.Equal(expectedReads, bus.Reads);
            Assert.Equal((uint)(((first&0x80)!=0?1:0)|((second&0x80)!=0?2:0)|((first&1)!=0?4:0)|((short)second>=-1?8:0)),cpu.State.D[0]);
            Assert.Equal(0x10000u, bus.Inner.ReadLong(0x3010));
            Assert.Equal((uint)second, bus.Inner.ReadLong(0x300C)); // Full-value consumers still need canonical upper bits.
            Assert.Equal(0x1000u,cpu.State.ProgramCounter);Assert.Equal(stack+4,cpu.State.A[7]);
            Assert.Equal(d.Skip(2),cpu.State.D.Skip(2));Assert.Equal(a.Skip(2).Take(5),cpu.State.A.Skip(2).Take(5));
            Assert.Equal(code,bus.Inner.Memory.AsSpan(0x10000,code.Length).ToArray());
        }
    }
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void RegisterFreeEntryTestsTheFlagWithoutMaterializingAMask(M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            IncludedExportNames = ["abi.bitflag"], RuntimeProfile = M68kRuntimeProfile.Freestanding,
            MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, NarrowOperations = true },
            Hunk = new() { IncludeSymbols = false }
        });
        var bus = new TestBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Memory, 0x10000);
        uint entry = 0x10000 + result.Symbols.Single(s => s.Name == "abi.bitflag").Address;
        Assert.Equal(0x0801, bus.ReadWord(entry)); // BTST #0,D1, no saved-register frame
        Assert.Equal(0, bus.ReadWord(entry + 2));
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        foreach (uint flags in new uint[] { 0, 1, 2, 0x80000000, 0xFFFFFFFE, 0xFFFFFFFF }) {
            bus.WriteLong(0x80002, 0x1000); cpu.Reset(entry, 0x80002);
            cpu.State.D[1] = flags;
            for (int n = 2; n < 8; n++) cpu.State.D[n] = 0xABC00000u + (uint)n;
            var saved = cpu.State.D.Skip(2).ToArray();
            for (int n = 0; n < 100 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x80006u, cpu.State.A[7]);
            Assert.Equal((flags & 1) != 0 ? 7u : 9u, cpu.State.D[0]);
            Assert.Equal(saved, cpu.State.D.Skip(2));
        }
    }
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void AddressComparisonsRetainUnsignedBoundaryBehavior(M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            IncludedExportNames = ["abi.addresspredicate"], RuntimeProfile = M68kRuntimeProfile.Freestanding,
            MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, NarrowOperations = true },
            Hunk = new() { IncludeSymbols = false }
        });
        var bus = new TestBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Memory, 0x10000);
        uint entry = 0x10000 + result.Symbols.Single(s => s.Name == "abi.addresspredicate").Address;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        bool directCompare = false;
        foreach (uint address in new uint[] { 0, 0x1FFFFF, 0x200000, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF }) {
            bus.WriteLong(0x80002, 0x1000); bus.WriteLong(0x3000, 0xDEADBEEF); cpu.Reset(entry, 0x80002);
            for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xABC00000u + (uint)n;
            cpu.State.A[0] = 0x3000; cpu.State.A[1] = address;
            var saved = cpu.State.D.Skip(2).ToArray();
            for (int n = 0; n < 200 && cpu.State.ProgramCounter != 0x1000; n++) {
                directCompare |= bus.ReadWord(cpu.State.ProgramCounter) == 0xB3FC;
                cpu.ExecuteInstruction();
            }
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x80006u, cpu.State.A[7]);
            Assert.Equal(address < 0x200000 ? 7u : 0xDEADBEEFu, bus.ReadLong(0x3000));
            Assert.Equal(11u, cpu.State.D[0]);
            Assert.Equal(saved, cpu.State.D.Skip(2));
        }
        Assert.True(directCompare, "Expected CMPA.L immediate on the incoming A1.");
    }
    private sealed class ReadBus : IM68kBus
    {
        public TestBus Inner { get; } = new();
        public List<(uint Address,int Width)> Reads { get; } = [];
        private void Record(uint address,int width) { if(address is >=0x3000 and <0x3100) Reads.Add((address,width)); }
        public byte ReadByte(uint address,ref long cycle,M68kBusAccessKind kind) { Record(address,1);return Inner.ReadByte(address,ref cycle,kind); }
        public ushort ReadWord(uint address,ref long cycle,M68kBusAccessKind kind) { Record(address,2);return Inner.ReadWord(address,ref cycle,kind); }
        public uint ReadLong(uint address,ref long cycle,M68kBusAccessKind kind) { Record(address,4);return Inner.ReadLong(address,ref cycle,kind); }
        public void WriteByte(uint address,byte value,ref long cycle,M68kBusAccessKind kind)=>Inner.WriteByte(address,value,ref cycle,kind);
        public void WriteWord(uint address,ushort value,ref long cycle,M68kBusAccessKind kind)=>Inner.WriteWord(address,value,ref cycle,kind);
        public void WriteLong(uint address,uint value,ref long cycle,M68kBusAccessKind kind)=>Inner.WriteLong(address,value,ref cycle,kind);
        public void ResetExternalDevices(long cycle) { }
        public bool HasHostGateway(uint address)=>false;
        public bool TryInvokeHostGateway(uint pc,uint token,M68kCpuState state)=>false;
    }
}
