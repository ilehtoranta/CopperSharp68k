using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Reflection;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kDeadMaskTestTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000)] [InlineData(M68kCpuModel.M68020)] [InlineData(M68kCpuModel.M68040)]
    public void DeadMaskedValueKeepsEveryFlagAndBothBranchOutcomes(M68kCpuModel model)
    {
        foreach (uint mask in new uint[] { 3, 511, 0x7FFF })
        foreach (bool materialized in new[] { false, true })
        {
            var before = Build(false, mask, materialized: materialized); var after = Build(true, mask, materialized: materialized);
            Assert.Equal(before.Length - (materialized ? mask <= 127 ? 0 : 4 : 2), after.Length);
            Assert.Equal(0x0240, BinaryPrimitives.ReadUInt16BigEndian(after));
            foreach (uint value in new uint[] { 0, 1, 0x80000000, 0xFFFF0000, 0x12345678, 0x80008000, uint.MaxValue })
            for (ushort flags = 0; flags < 32; flags++)
                Assert.Equal(Execute(before, model, value, flags), Execute(after, model, value, flags));
        }
    }

    [Theory]
    [InlineData("live")] [InlineData("source")] [InlineData("effects")] [InlineData("negative")] [InlineData("fixup")]
    public void ObservableValuesAndNonEquivalentFlagsKeepTheLongOperation(string guard)
        => Assert.Equal(Build(false, guard == "negative" ? 0x8000u : 511u, guard, guard == "source"),
            Build(true, guard == "negative" ? 0x8000u : 511u, guard, guard == "source"));

    [Fact]
    public void CompiledCSharpAlignmentBranchUsesAWordMask()
    {
        M68kCompilationResult Compile(bool narrow) => AmigaM68kCompiler.Compile(new()
        {
            AssemblyPath = typeof(DeadMaskFixture).Assembly.Location,
            EntryPoint = typeof(DeadMaskFixture).FullName + "::Entry", IncludedExportNames = [],
            Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = M68kPeepholeOptimizationMode.FixedPoint,
            CodeSizeOptimizations = new() { NarrowOperations = narrow, RemoveRedundantTransport = true }
        });
        var before = Compile(false); var after = Compile(true);
        Assert.Empty(after.FrameworkAnalysis.ManagedAllocationSites);
        var oldMethod = before.Symbols.Single(s => s.Name.EndsWith("DeadMaskFixture::Aligned"));
        var newMethod = after.Symbols.Single(s => s.Name.EndsWith("DeadMaskFixture::Aligned"));
        Assert.True(newMethod.Size < oldMethod.Size, Convert.ToHexString(before.Code) + "/" + Convert.ToHexString(after.Code));
        Assert.Contains(Enumerable.Range((int)newMethod.Address, (int)newMethod.Size - 3).Where(n => n % 2 == 0), n =>
            (BinaryPrimitives.ReadUInt16BigEndian(after.Code.AsSpan(n, 2)) & 0xFFF8) == 0x0240 &&
            BinaryPrimitives.ReadUInt16BigEndian(after.Code.AsSpan(n + 2, 2)) == 511);
        foreach (var result in new[] { before, after })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        foreach (uint value in new uint[] { 0, 1, 511, 512, 4096, 0xFFFF0000, 0xFFFF01FF, uint.MaxValue })
        {
            var bus = new TestBus(0x20000); result.Code.CopyTo(bus.Memory, 0x10000);
            foreach (var relocation in result.Relocations) bus.WriteLong(0x10000u + (uint)relocation.Offset, bus.ReadLong(0x10000u + (uint)relocation.Offset) + 0x10000);
            bus.WriteLong(0x2000, value); bus.WriteLong(0x18000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus); cpu.Reset(0x10000 + result.EntryPoint, 0x18000);
            for (var step = 0; step < 100 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal((value & 511) == 0 ? 1u : 2u, cpu.State.D[0]);
        }
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void UpperWordStoreOnlyDropsDeadUnobservedBits(bool useLong, bool indexed, bool shouldFold)
    {
        var a = new M68kAssembler { GeneratedCodeSizeOptions = new() { NarrowOperations = true },
            GeneratedCodeSizeRanges = [("start", "end")] };
        a.Mark("start");
        a.EmitWord(0x4240); // CLR.W D0
        a.EmitWord(0x4840); // SWAP D0
        a.EmitWord(indexed ? (ushort)0x3180 : (ushort)0x3080); // MOVE.W D0,(A0[,D0.L])
        if (indexed) a.EmitWord(0x0800);
        if (useLong) a.EmitWord(0x2280); // MOVE.L D0,(A1)
        a.EmitWord(0x7000); a.EmitWord(0x4E75); a.Mark("end");
        var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
        var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
        var changed = typeof(M68kPeepholeOptimizer).GetMethod("TryFoldDeadWordOperands", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(pass, [M68kInstructionDataflow.Analyze(a)]);
        Assert.Equal(shouldFold, changed);
        Assert.Equal(shouldFold ? 0x4840 : 0x4240, BinaryPrimitives.ReadUInt16BigEndian(a.Link(0x10000, new Dictionary<string, uint>()).Bytes));
    }
    private static byte[] Build(bool enabled, uint mask, string guard = "", bool materialized = false)
    {
        var a = new M68kAssembler { GeneratedCodeSizeOptions = new() { NarrowOperations = enabled },
            GeneratedCodeSizeRanges = [("start", "end")] };
        a.Mark("start"); var at = a.Offset;
        if (materialized)
        {
            if (mask <= 127) a.EmitWord((ushort)(0x7400 | mask));
            else { a.EmitWord(0x243C); a.EmitLong(mask); }
            a.EmitWord(0xC082);
        }
        else
        {
            a.EmitWord(0x0280);
            if (guard == "fixup") a.EmitAddress("mask", external: true); else a.EmitLong(mask);
        }
        if (guard == "effects") a.SetInstructionEffects(at, new(1, 1, 0, 0,
            M68kConditionCodeSet.None, M68kConditionCodeSet.All, M68kMemorySet.None, M68kMemorySet.None, 0, false, false));
        at = a.Offset; a.EmitWord(0x40C5); // Observe all SR bits before branching.
        a.SetInstructionEffects(at, new(0, 32, 0, 0, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.None, M68kMemorySet.None, 0, false, false));
        a.EmitBranch(M68kCondition.NotEqual, "nonzero"); a.EmitWord(0x7201);
        a.EmitBranch(M68kCondition.True, "done"); a.Mark("nonzero"); a.EmitWord(0x7202);
        a.Mark("done"); if (guard != "live") a.EmitWord(0x7000);
        if (materialized && guard != "source") a.EmitWord(0x7400);
        at = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(at, new(255, 0, 127, 128, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end");
        if (enabled)
        {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            typeof(M68kPeepholeOptimizer).GetMethod("TryNarrowDeadMaskTest", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(pass, [M68kInstructionDataflow.Analyze(a)]);
        }
        return a.Link(0x10000, new Dictionary<string, uint> { ["mask"] = mask }).Bytes;
    }

    private static string Execute(byte[] code, M68kCpuModel model, uint value, ushort flags)
    {
        var bus = new TestBus(0x20000); code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus); cpu.Reset(0x10000, 0x18000);
        cpu.State.StatusRegister = (ushort)(0x2700 | flags);
        for (var n = 0; n < 8; n++) cpu.State.D[n] = 0xA5550000u + (uint)n;
        cpu.State.D[0] = value;
        for (var step = 0; step < 100 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return string.Join(",", cpu.State.D) + ":" + string.Join(",", cpu.State.A) + ":" + cpu.State.StatusRegister;
    }
}

public static class DeadMaskFixture
{
    public static int Entry() => Aligned(APTR.ReadUInt32((APTR)0x2000, 0));
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Aligned(uint value) => (value & 511) == 0 ? 1 : 2;
}
