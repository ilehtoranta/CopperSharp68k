using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kLoadedCopyTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000)] [InlineData(M68kCpuModel.M68020)] [InlineData(M68kCpuModel.M68040)]
    public void CombinedPostincrementCopiesPreserveEveryAccessAndAllFlags(M68kCpuModel model)
    {
        var before = PostincrementCopy(false); var after = PostincrementCopy(true);
        Assert.True(after.Length < before.Length);
        foreach (int distance in new[] { -4, 0, 4, 128 })
        for (ushort flags = 0; flags < 32; flags++)
        {
            var original = Execute(before, model, distance, flags); var optimized = Execute(after, model, distance, flags);
            Assert.Equal(original.Memory, optimized.Memory); Assert.Equal(original.Registers, optimized.Registers);
            Assert.Equal(original.Flags, optimized.Flags); Assert.Equal(original.Accesses, optimized.Accesses);
        }
    }

    [Theory]
    [InlineData("value")] [InlineData("entry")] [InlineData("effects")] [InlineData("base")]
    public void CombinedCopiesKeepObservableTemporariesAndIndependentEntries(string guard)
        => Assert.Equal(PostincrementCopy(false, guard), PostincrementCopy(true, guard));

    private static byte[] PostincrementCopy(bool enabled, string guard = "")
    {
        var a = new M68kAssembler { GeneratedCodeSizeOptions = new() { CompactGuestMemory = enabled },
            GeneratedCodeSizeRanges = [("start", "end")] };
        a.Mark("start"); if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "store");
        int load = a.Offset; a.EmitWord(0x2018); // MOVE.L (A0)+,D0
        if (guard == "effects") a.SetInstructionEffects(load, new(0, 1, 1, 1, M68kConditionCodeSet.None,
            M68kConditionCodeSet.All, M68kMemorySet.Unknown, M68kMemorySet.None, 0, false, false));
        a.Mark(guard == "entry" ? "store" : "method:fixture:IL_0002");
        a.EmitWord((ushort)(guard == "base" ? 0x20C0 : 0x22C0));
        int status = a.Offset; a.EmitWord(0x40C5);
        a.SetInstructionEffects(status, new(0, 32, 0, 0, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.None, M68kMemorySet.None, 0, false, false));
        if (guard != "value") a.EmitWord(0x7000);
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(255, 0, 127, 128, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end");
        if (enabled)
        {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            typeof(M68kPeepholeOptimizer).GetMethod("TryFoldPostincrementLongCopy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass, [M68kInstructionDataflow.Analyze(a)]);
        }
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    [Fact]
    public void CompiledCSharpPointerAdvancesUsePostincrementInTheFullPipeline()
    {
        var result = AmigaM68kCompiler.Compile(new()
        {
            AssemblyPath = typeof(LoadedCopyFixture).Assembly.Location,
            EntryPoint = typeof(LoadedCopyFixture).FullName + "::EntryIncrementing", IncludedExportNames = [],
            Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
            CodeSizeOptimizations = new() { CompactGuestMemory = true, RemoveRedundantTransport = true,
                InlineMemoryHelpers = true, EliminateRedundantInitialization = true, SimplifyLoopPhis = true }
        });
        Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
        var method = result.Symbols.Single(s => s.Name.EndsWith("LoadedCopyFixture::CopyIncrementing"));
        var a = new M68kAssembler();
        for (var n = (int)method.Address; n < method.Address + method.Size; n += 2)
            a.EmitWord((ushort)((result.Code[n] << 8) | result.Code[n + 1]));
        var text = a.RenderAssembly(M68kCpuTarget.M68000);
        Assert.Matches(@"move\.l\s+\(a[0-6]\)\+,\(a[0-6]\)\+", text);
    }

    [Theory]
    [InlineData(M68kCpuModel.M68000, false)] [InlineData(M68kCpuModel.M68000, true)]
    [InlineData(M68kCpuModel.M68020, false)] [InlineData(M68kCpuModel.M68020, true)]
    [InlineData(M68kCpuModel.M68040, false)] [InlineData(M68kCpuModel.M68040, true)]
    public void PointerAdvancesPreserveEveryFlagRegisterAndBusAccess(M68kCpuModel model, bool store)
    {
        foreach (bool lea in new[] { false, true })
        {
            var before = PointerAdvance(false, store, lea);
            var after = PointerAdvance(true, store, lea);
            Assert.True(after.Length < before.Length);
            for (ushort flags = 0; flags < 32; flags++)
            {
                var original = Execute(before, model, 128, flags);
                var optimized = Execute(after, model, 128, flags);
                Assert.Equal(original.Memory, optimized.Memory); Assert.Equal(original.Registers, optimized.Registers);
                Assert.Equal(original.Flags, optimized.Flags); Assert.Equal(original.Accesses, optimized.Accesses);
            }
        }
    }

    [Theory]
    [InlineData("entry")] [InlineData("effects")] [InlineData("base")] [InlineData("stack")]
    public void PointerAdvanceKeepsIndependentEntriesAndBaseAliases(string guard)
        => Assert.Equal(PointerAdvance(false, false, false, guard), PointerAdvance(true, false, false, guard));

    private static byte[] PointerAdvance(bool enabled, bool store, bool lea, string guard = "")
    {
        var a = new M68kAssembler { GeneratedCodeSizeOptions = new() { CompactGuestMemory = enabled },
            GeneratedCodeSizeRanges = [("start", "end")] };
        a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "advance");
        int address = guard == "stack" ? 7 : store ? 1 : 0;
        int move = a.Offset;
        a.EmitWord((ushort)(guard == "base" ? 0x2050 : store ? 0x2280 : 0x2010 | address));
        if (guard == "effects") a.SetInstructionEffects(move, new(0, 1, 1, 0, M68kConditionCodeSet.None,
            M68kConditionCodeSet.All, M68kMemorySet.Unknown, M68kMemorySet.None, 0, false, false));
        if (guard == "entry") a.Mark("advance");
        else a.Mark("method:fixture:IL_0002");
        a.EmitWord((ushort)(lea ? 0x41E8 | address << 9 | address : 0x5888 | address));
        if (lea) a.EmitWord(4);
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(255, 0, 127, 128, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end");
        if (enabled)
        {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            typeof(M68kPeepholeOptimizer).GetMethod("TryFoldLongMemoryPointerAdvance", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass, [M68kInstructionDataflow.Analyze(a)]);
        }
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    [Fact]
    public void CompiledCSharpCopiesUseGroupedStoresInTheFullPipeline()
    {
        M68kCompilationResult Compile(bool enabled) => AmigaM68kCompiler.Compile(new()
        {
            AssemblyPath = typeof(LoadedCopyFixture).Assembly.Location,
            EntryPoint = typeof(LoadedCopyFixture).FullName + "::Entry", IncludedExportNames = [],
            Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
            CodeSizeOptimizations = new() { CompactGuestMemory = enabled, RemoveRedundantTransport = true,
                InlineMemoryHelpers = true, EliminateRedundantInitialization = true, SimplifyLoopPhis = true }
        });
        var before = Compile(false); var after = Compile(true);
        Assert.Empty(after.FrameworkAnalysis.ManagedAllocationSites);
        var oldMethod = before.Symbols.Single(s => s.Name.EndsWith("LoadedCopyFixture::Copy"));
        var newMethod = after.Symbols.Single(s => s.Name.EndsWith("LoadedCopyFixture::Copy"));
        Assert.True(newMethod.Size < oldMethod.Size, after.Map);
        var a = new M68kAssembler();
        for (var n = (int)newMethod.Address; n < newMethod.Address + newMethod.Size; n += 2)
            a.EmitWord((ushort)((after.Code[n] << 8) | after.Code[n + 1]));
        Assert.Matches(@"movem\.l\s+[^\r\n]+,\(a[0-6]\)", a.RenderAssembly(M68kCpuTarget.M68000));
    }

    [Theory]
    [InlineData(M68kCpuModel.M68000)]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68040)]
    public void GroupedCopiesPreserveBytesRegistersFlagsAndEveryBusAccess(M68kCpuModel model)
    {
        foreach (var offsets in new[] { (0, 0), (4, 8), (-8, 4) })
        {
            var before = Build(false, offsets.Item1, offsets.Item2);
            var after = Build(true, offsets.Item1, offsets.Item2);
            Assert.True(after.Length < before.Length);
            foreach (var distance in new[] { -16, -4, 0, 4, 16, 128 })
            foreach (ushort flags in new ushort[] { 0, 0x1F })
            {
                var original = Execute(before, model, distance, flags);
                var optimized = Execute(after, model, distance, flags);
                Assert.Equal(original.Memory, optimized.Memory);
                Assert.Equal(original.Registers, optimized.Registers);
                Assert.Equal(original.Flags, optimized.Flags);
                Assert.Equal(original.Accesses, optimized.Accesses);
            }
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("flags")]
    [InlineData("entry")]
    [InlineData("effects")]
    [InlineData("base")]
    public void LiveStateAndIndependentEntriesKeepTheOriginalSequence(string guard)
        => Assert.Equal(Build(false, 0, 0, guard), Build(true, 0, 0, guard));

    private static byte[] Build(bool enabled, int sourceOffset, int destinationOffset, string guard = "")
    {
        var a = new M68kAssembler
        {
            GeneratedCodeSizeOptions = new() { CompactGuestMemory = enabled },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "interior");
        int[] registers = guard == "base" ? [8, 3, 1, 0] : [4, 3, 1, 0];
        for (var n = 0; n < 4; n++)
        {
            if (guard == "entry" && n == 2) a.Mark("interior");
            var offset = sourceOffset + n * 4;
            var mode = offset == 0 ? 2 : 5;
            int at = a.Offset;
            a.Mark($"method:fixture:IL_{at:X4}");
            a.EmitWord((ushort)(0x2000 | (registers[n] & 7) << 9 | (registers[n] >> 3) << 6 | mode << 3));
            if (mode == 5) a.EmitWord(unchecked((ushort)offset));
            if (guard == "effects" && n == 1)
                a.SetInstructionEffects(at, new(0, 0, 1, 0, M68kConditionCodeSet.None,
                    M68kConditionCodeSet.All, M68kMemorySet.Unknown, M68kMemorySet.None, 0, false, false));
        }
        for (var n = 0; n < 4; n++)
        {
            var offset = destinationOffset + n * 4;
            var mode = offset == 0 ? 2 : 5;
            a.Mark($"method:fixture:IL_{a.Offset:X4}");
            a.EmitWord((ushort)(0x2000 | 1 << 9 | mode << 6 | (registers[n] >> 3) << 3 | (registers[n] & 7)));
            if (mode == 5) a.EmitWord(unchecked((ushort)offset));
        }
        if (guard == "flags")
        {
            int at = a.Offset; a.EmitWord(0x40C5); // Observe all CCR bits before retiring the values.
            a.SetInstructionEffects(at, new(0, 32, 0, 0, M68kConditionCodeSet.All,
                M68kConditionCodeSet.None, M68kMemorySet.None, M68kMemorySet.None, 0, false, false));
        }
        foreach (var register in new[] { 0, 1, 3, 4 })
            if (guard != "value" || register != 4) a.EmitWord((ushort)(0x7000 | register << 9));
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(255, 0, 127, 128, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end");
        if (enabled)
        {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            var flow = M68kInstructionDataflow.Analyze(a, new HashSet<int>());
            typeof(M68kPeepholeOptimizer).GetMethod("TryGroupLoadedLongMemoryCopy", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(pass, [flow]);
        }
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private sealed record Result(byte[] Memory, uint[] Registers, ushort Flags, (uint, int, bool)[] Accesses);
    private static Result Execute(byte[] code, M68kCpuModel model, int distance, ushort flags)
    {
        var bus = new TraceBus(); code.CopyTo(bus.Inner.Memory, 0x10000);
        for (var n = 0; n < 512; n++) bus.Inner.Memory[0x3000 + n] = (byte)(n * 31 + 7);
        bus.Inner.WriteLong(0x80000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x80000); cpu.State.StatusRegister = (ushort)(0x2700 | flags);
        for (var n = 0; n < 8; n++) cpu.State.D[n] = 0xA5550000u + (uint)n;
        for (var n = 0; n < 7; n++) cpu.State.A[n] = 0xA7770000u + (uint)n;
        cpu.State.A[0] = 0x3040; cpu.State.A[1] = (uint)(0x3040 + distance);
        for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return new(bus.Inner.Memory.AsSpan(0x3000, 512).ToArray(), cpu.State.D.ToArray().Concat(cpu.State.A.ToArray()).ToArray(),
            cpu.State.StatusRegister, bus.Accesses.ToArray());
    }

    private sealed class TraceBus : IM68kBus
    {
        public TestBus Inner { get; } = new();
        public List<(uint, int, bool)> Accesses { get; } = [];
        private void Record(uint a, int width, bool write) { if (a is >= 0x3000 and < 0x3200) Accesses.Add((a, width, write)); }
        public byte ReadByte(uint a, ref long c, M68kBusAccessKind k) { Record(a, 1, false); return Inner.ReadByte(a, ref c, k); }
        public ushort ReadWord(uint a, ref long c, M68kBusAccessKind k) { Record(a, 2, false); return Inner.ReadWord(a, ref c, k); }
        public uint ReadLong(uint a, ref long c, M68kBusAccessKind k) { Record(a, 4, false); return Inner.ReadLong(a, ref c, k); }
        public void WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) { Record(a, 1, true); Inner.WriteByte(a, v, ref c, k); }
        public void WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) { Record(a, 2, true); Inner.WriteWord(a, v, ref c, k); }
        public void WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) { Record(a, 4, true); Inner.WriteLong(a, v, ref c, k); }
        public void ResetExternalDevices(long cycle) { }
        public bool HasHostGateway(uint address) => false;
        public bool TryInvokeHostGateway(uint pc, uint token, M68kCpuState state) => false;
    }
}

public static class LoadedCopyFixture
{
    public static void EntryIncrementing() => CopyIncrementing((APTR)APTR.ReadUInt32((APTR)0x2000, 0), (APTR)APTR.ReadUInt32((APTR)0x2004, 0));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CopyIncrementing(APTR sourceArgument, APTR destinationArgument)
    {
        var source = sourceArgument; var destination = destinationArgument;
        var a = APTR.ReadUInt32(source, 0); source = APTR.FromPointer(source.Raw + 4);
        APTR.WriteUInt32(destination, 0, a); destination = APTR.FromPointer(destination.Raw + 4);
        var b = APTR.ReadUInt32(source, 0); source = APTR.FromPointer(source.Raw + 4);
        APTR.WriteUInt32(destination, 0, b); destination = APTR.FromPointer(destination.Raw + 4);
        var c = APTR.ReadUInt32(source, 0); source = APTR.FromPointer(source.Raw + 4);
        APTR.WriteUInt32(destination, 0, c); destination = APTR.FromPointer(destination.Raw + 4);
        var d = APTR.ReadUInt32(source, 0);
        APTR.WriteUInt32(destination, 0, d);
    }

    public static void Entry() => Copy((APTR)APTR.ReadUInt32((APTR)0x2000, 0), (APTR)APTR.ReadUInt32((APTR)0x2004, 0));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Copy(APTR source, APTR destination)
    {
        var a = APTR.ReadUInt32(source, 0); var b = APTR.ReadUInt32(source, 4);
        var c = APTR.ReadUInt32(source, 8); var d = APTR.ReadUInt32(source, 12);
        APTR.WriteUInt32(destination, 0, a); APTR.WriteUInt32(destination, 4, b);
        APTR.WriteUInt32(destination, 8, c); APTR.WriteUInt32(destination, 12, d);
    }
}
