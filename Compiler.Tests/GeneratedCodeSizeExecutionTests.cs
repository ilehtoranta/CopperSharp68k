using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;
using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public sealed class GeneratedCodeSizeExecutionTests
{
    public static IEnumerable<object[]> RawCases()
    {
        foreach (var words in new ushort[][] {
            [0x2009, 0x2002, 0x4e75], // overwritten full register move, X preserved
            [0x206f, 12, 0x2f48, 12, 0x7000, 0x4e75], // private stack writeback
            [0x2f6f, 12, 12, 0x7000, 0x4e75], // labeled self-move
            [0x2f6f, 12, 12, 0x4e75], // live long flags become a shorter test
            [0x3f6f, 12, 12, 0x4e75], // live word flags
            [0x1f6f, 12, 12, 0x4e75], // live byte flags
            [0x2248, 0xd3c2, 0x7000, 0x1011, 0x4e75], // indexed canonical byte read
            [0x1010, 0x4a00, 0x4e75], // loaded value has only a test consumer
            [0x0280, 0, 255, 0x1280, 0x4e75], // low-byte store needs no wider normalization
            [0x0280, 0, 65535, 0x3280, 0x4e75], // low-word store
            [0x2009, 0x3002, 0x4e75], // partial definition: keep upper bits
            [0x206f, 12, 0x2f48, 12, 0x4e75], // live NZVC: retain writeback
            [0x0280, 0, 255, 0x2280, 0x4e75], // full-width consumer retains mask
            [0x0280, 0, 255, 0x1280, 0x2200, 0x4e75], // later full consumer
        }) foreach (var flags in Enumerable.Range(0, 32))
            yield return [words, flags];
    }

    [Theory, MemberData(nameof(RawCases))]
    public void RegistersFlagsMemoryAndStackAgree(ushort[] words, int flags)
    {
        var before = Raw(words, false);
        var after = Raw(words, true);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 }) {
            var a = Run(before, model, flags);
            var b = Run(after, model, flags);
            // D0 is dead in the test/store fixtures, and A1 is an address
            // temporary only in the indexed fixture. Every other output agrees.
            Assert.Equal(a.Flags, b.Flags);
            Assert.Equal(a.Memory, b.Memory);
            Assert.Equal(a.Stack, b.Stack);
            Assert.Equal(a.Reserved, b.Reserved);
            Assert.Equal(a.Accesses, b.Accesses);
            if (words[0] is not (0x1010 or 0x0280)) Assert.Equal(a.D0, b.D0);
        }
    }

    [Fact]
    public void RewritesHaveIndependentControlsAndRetainEntryLabels()
    {
        var words = new ushort[] { 0x2f6f, 12, 12, 0x7000, 0x4e75 };
        var before = Raw(words, false);
        var after = Raw(words, true);
        Assert.True(after.Bytes.Length < before.Bytes.Length);
        Assert.Equal(0, after.Labels["start"]);
        Assert.False(new M68kCodeSizeOptions().RemoveRedundantTransport);
        Assert.False(new M68kCodeSizeOptions().CompactGuestMemory);
        Assert.False(new M68kCodeSizeOptions().NarrowOperations);
    }

    [Theory]
    [InlineData("RemoveRedundantTransport")]
    [InlineData("CompactGuestMemory")]
    [InlineData("NarrowOperations")]
    [InlineData("EliminateRedundantInitialization")]
    [InlineData("SizeFirstCosts")]
    [InlineData("InlineMemoryHelpers")]
    [InlineData("ShareArithmeticCores")]
    [InlineData("Combined")]
    public void ResidentHunkRelocationAliasingAndRepeatedInvocationAgree(string pass)
    {
        var request = new M68kCompilationRequest {
            AssemblyPath = typeof(GeneratedSizeFixtures).Assembly.Location,
            EntryPoint = typeof(GeneratedSizeFixtures).FullName + "::Entry",
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Resident,
            OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None, IncludedExportNames = [],
            Hunk = new() { IncludeSymbols = false }
        };
        var before = AmigaM68kCompiler.Compile(request);
        var policy = new M68kCodeSizeOptions {
            RemoveRedundantTransport = pass is "Combined" or "RemoveRedundantTransport",
            CompactGuestMemory = pass is "Combined" or "CompactGuestMemory",
            NarrowOperations = pass is "Combined" or "NarrowOperations",
            EliminateRedundantInitialization = pass is "Combined" or "EliminateRedundantInitialization",
            SizeFirstCosts = pass is "Combined" or "SizeFirstCosts",
            InlineMemoryHelpers = pass is "Combined" or "InlineMemoryHelpers",
            ShareArithmeticCores = pass is "Combined" or "ShareArithmeticCores"
        };
        var after = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = policy });
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var input in new uint[] { 0, 1, 127, 128, 255, 256, uint.MaxValue }) {
            var a = Guest(before, model, load, input);
            var b = Guest(after, model, load, input);
            Assert.Equal(a, b);
        }
        foreach (var target in new[] { M68kCpuTarget.M68020, M68kCpuTarget.M68040 }) {
            Assert.Equal(AmigaM68kCompiler.Compile(request with { Cpu = target, CodeSizeOptimizations = new() }).Image,
                AmigaM68kCompiler.Compile(request with { Cpu = target, CodeSizeOptimizations = policy }).Image);
        }
    }

    [Theory]
    [InlineData("ArithmeticEntry", "ShareArithmeticCores")]
    [InlineData("UnsignedArithmeticEntry", "ShareArithmeticCores")]
    [InlineData("BoundedArithmeticEntry", "ShareArithmeticCores")]
    [InlineData("HelperEntry", "InlineMemoryHelpers")]
    public void SharedArithmeticAndBranchedMemoryHelpersPreserveBoundaryValues(string entry, string pass)
    {
        var request = new M68kCompilationRequest {
            AssemblyPath = typeof(GeneratedSizeFixtures).Assembly.Location,
            EntryPoint = typeof(GeneratedSizeFixtures).FullName + "::" + entry,
            Cpu = M68kCpuTarget.M68000, RuntimeProfile = M68kRuntimeProfile.Resident,
            OutputFormat = M68kOutputFormat.Hunk, ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None, IncludedExportNames = [], Hunk = new() { IncludeSymbols = false }
        };
        var before = AmigaM68kCompiler.Compile(request);
        var after = AmigaM68kCompiler.Compile(request with { CodeSizeOptimizations = new() {
            ShareArithmeticCores = pass == "ShareArithmeticCores", InlineMemoryHelpers = pass == "InlineMemoryHelpers" } });
        Assert.True(after.Image.Length < before.Image.Length);
        Assert.Contains("pass=" + (entry == "BoundedArithmeticEntry" ? "BoundedRegisterDivision" : pass), after.Map);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var a in new uint[] { 0, 1, 97, 127, 128, 255, 65535, 0x7fffffff, 0x80000000, uint.MaxValue })
        foreach (var b in new uint[] { 1, 3, 10, 65535, 65536, 0x80000000, uint.MaxValue }) {
            // Copper68k 1.5.1 has no exact 020/040 timing for immediate
            // BCHG in the unchanged signed-negative-divisor prefix. The
            // unsigned fixture covers those same core operands on all CPUs.
            if (entry == "ArithmeticEntry" && model != M68kCpuModel.M68000 && b >= 0x80000000) continue;
            Assert.Equal(Guest(before, model, load, a, b), Guest(after, model, load, a, b));
        }
    }

    private static LinkedCode Raw(ushort[] words, bool enabled)
    {
        var assembler = new M68kAssembler {
            GeneratedCodeSizeOptions = enabled ? new() { RemoveRedundantTransport = true,
                CompactGuestMemory = true, NarrowOperations = true } : null,
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        assembler.Mark("start");
        foreach (var word in words) assembler.EmitWord(word);
        assembler.SetInstructionEffects(assembler.Offset - 2, new(0x00fc, 0, 0x00fc, 0x80,
            M68kConditionCodeSet.All, M68kConditionCodeSet.None, M68kMemorySet.Stack,
            M68kMemorySet.None, 4, true, false));
        assembler.Mark("end");
        assembler.MarkDataStart();
        assembler.EmitAddress("start");
        assembler.OptimizeForM68000();
        return assembler.Link(0x10000, new Dictionary<string, uint>());
    }

    private sealed record Observation(uint D0, ushort Flags, uint Stack, byte[] Memory, uint[] Reserved, uint[] Accesses);
    private static Observation Run(LinkedCode code, M68kCpuModel model, int flags)
    {
        var bus = new CountedBus();
        code.Bytes.CopyTo(bus.Memory, 0x10000);
        bus.WriteLong(0x80000, 0x1000); bus.WriteLong(0x8000c, 0x40000);
        bus.WriteLong(0x40000, 0x80ffabcd);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x80000); cpu.State.StatusRegister = (ushort)(0x2700 | flags);
        cpu.State.D[0] = 0xabcdef80; cpu.State.D[2] = 1;
        cpu.State.A[0] = cpu.State.A[1] = 0x40000;
        cpu.State.A[5] = 0x5a5a5a5a; cpu.State.A[6] = 0x6a6a6a6a;
        for (var step = 0; step < 100 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return new(cpu.State.D[0], cpu.State.StatusRegister, cpu.State.A[7],
            bus.Memory.AsSpan(0x40000, 16).ToArray(), [cpu.State.A[5], cpu.State.A[6]], bus.Accesses.ToArray());
    }

    private static uint[] Guest(M68kCompilationResult result, M68kCpuModel model, uint load, uint input, uint divisor = 0)
    {
        var bus = new CountedBus();
        ResidentCodeSizeExecutionTests.LoadImage(result.Image, load).CopyTo(bus.Memory, (int)load);
        bus.WriteLong(0x40000, input); bus.WriteLong(0x40004, divisor);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        var observations = new List<uint>();
        for (var invocation = 0; invocation < 3; invocation++) {
            bus.WriteLong(0x80000, 0x1000); cpu.Reset(load + result.EntryPoint, 0x80000);
            for (var step = 0; step < 10000 && cpu.State.ProgramCounter != 0x1000; step++) {
                cpu.ExecuteInstruction(); Assert.False(cpu.State.Halted);
            }
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal(0x80004u, cpu.State.A[7]);
            observations.Add(cpu.State.D[0]); observations.Add(bus.ReadLong(0x40004));
            observations.AddRange(bus.Memory.AsSpan(0x40008, 16).ToArray().Select(v => (uint)v));
        }
        return observations.Concat(bus.Accesses).ToArray();
    }

    private sealed class CountedBus : IM68kBus
    {
        private readonly TestBus inner = new(0x100000);
        internal byte[] Memory => inner.Memory;
        internal List<uint> Accesses { get; } = [];
        internal void WriteLong(uint address, uint value) => inner.WriteLong(address, value);
        internal uint ReadLong(uint address) => inner.ReadLong(address);
        private void Record(uint address, int width, bool write) {
            if (address >= 0x40000 && address < 0x40020) Accesses.Add(address | ((uint)width << 24) | (write ? 0x80000000u : 0));
        }
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) {
            Record(address, 1, false); return inner.ReadByte(address, ref cycle, kind);
        }
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) {
            Record(address, 2, false); return inner.ReadWord(address, ref cycle, kind);
        }
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) {
            Record(address, 4, false); return inner.ReadLong(address, ref cycle, kind);
        }
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) {
            Record(address, 1, true); inner.WriteByte(address, value, ref cycle, kind);
        }
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) {
            Record(address, 2, true); inner.WriteWord(address, value, ref cycle, kind);
        }
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) {
            Record(address, 4, true); inner.WriteLong(address, value, ref cycle, kind);
        }
        public void ResetExternalDevices(long cycle) => inner.ResetExternalDevices(cycle);
    }
}

public static unsafe class GeneratedSizeFixtures
{
    public static uint Entry() {
        var input = *(uint*)0x40000;
        var p = APTR.FromPointer(0x40008);
        APTR.WriteUInt8(p, (int)(input & 7), (byte)input);
        APTR.WriteUInt8(p, 0, (byte)input);
        APTR.WriteUInt16(p, 2, (ushort)input);
        APTR.WriteUInt32(p, 4, input);
        var a = Read(p, input & 3);
        *(uint*)0x40004 += a;
        return a + APTR.ReadUInt16(p, 2) + APTR.ReadUInt32(p, 4);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Read(APTR p, uint offset) => APTR.ReadUInt8(p, (int)offset);
    public static uint ArithmeticEntry() {
        var a = *(uint*)0x40000; var b = *(uint*)0x40004;
        return Divide(a, b) ^ Remainder(a + 0x12345678, b) ^ (uint)Signed((int)a, (int)b);
    }
    public static uint UnsignedArithmeticEntry() {
        var a = *(uint*)0x40000; var b = *(uint*)0x40004;
        return Divide(a, b) ^ Remainder(a + 0x12345678, b);
    }
    public static uint BoundedArithmeticEntry() {
        var a = *(uint*)0x40000 & 65535; var b = (*(uint*)0x40004 & 255) + 1;
        return a / b + a % b;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Divide(uint a, uint b) => a / b;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Remainder(uint a, uint b) => a % b;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int Signed(int a, int b) => a / b + a % b;
    public static uint HelperEntry() {
        var a = *(uint*)0x40000;
        var p = APTR.FromPointer(0x40008);
        var value = Choose(a);
        Pair(p, value);
        return (uint)APTR.ReadUInt8(p, 0) + APTR.ReadUInt8(p, 1) + value;
    }
    private static uint Choose(uint a) => a < 128 ? a + 7 : a - 19;
    private static void Pair(APTR p, uint value) {
        APTR.WriteUInt8(p, 0, (byte)value);
        APTR.WriteUInt8(p, 1, (byte)(value + 1));
    }
}
