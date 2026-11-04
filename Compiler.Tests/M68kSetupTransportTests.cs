using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kSetupTransportTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BooleanBranchesPreserveEveryCondition(bool inverted)
    {
        for (int condition = 2; condition < 16; condition++) {
            byte[] before = Boolean(false, condition, inverted), after = Boolean(true, condition, inverted);
            Assert.True(after.Length < before.Length);
            foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
            for (ushort ccr = 0; ccr < 32; ccr++)
                Assert.Equal(Run(before, model, ccr, 0), Run(after, model, ccr, 0));
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("flags")]
    [InlineData("entry")]
    public void BooleanFoldKeepsObservableStateAndInteriorEntries(string guard)
        => Assert.Equal(Boolean(false, 6, false, guard), Boolean(true, 6, false, guard));

    [Fact]
    public void LongImmediateComparesPreserveValuesAndAllFlags()
    {
        foreach (uint constant in new uint[] { 512, 704, 1024, 0x12345678, 0x80000000, 0xFFFFFFFF }) {
            byte[] before = Compare(false, constant), after = Compare(true, constant);
            Assert.True(after.Length <= before.Length);
            if (constant == 0x12345678) Assert.True(after.Length < before.Length);
            foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
            foreach (uint value in new uint[] { 0, constant, constant - 1, constant + 1, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF })
            for (ushort ccr = 0; ccr < 32; ccr++)
                Assert.Equal(Run(before, model, ccr, value), Run(after, model, ccr, value));
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("entry")]
    [InlineData("same")]
    public void CompareFoldKeepsObservableConstantAndIndependentEntries(string guard)
        => Assert.Equal(Compare(false, 0x12345678, guard), Compare(true, 0x12345678, guard));

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void CopiedArgumentBitTestKeepsTheOriginalSource(int bit)
    {
        byte[] before = BitTest(false, bit), after = BitTest(true, bit);
        Assert.True(after.Length < before.Length);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (uint value in new uint[] { 0, 1u << bit, 0x80000000, uint.MaxValue, 0xA5A55A5A })
        for (ushort ccr = 0; ccr < 32; ccr++)
            Assert.Equal(Run(before, model, ccr, value), Run(after, model, ccr, value));
    }

    [Theory]
    [InlineData("value")]
    [InlineData("mask")]
    [InlineData("flags")]
    [InlineData("entry")]
    public void BitFoldKeepsLiveTemporaryMaskFlagsAndEntries(string guard)
        => Assert.Equal(BitTest(false, 3, guard), BitTest(true, 3, guard));

    private static byte[] BitTest(bool enabled, int bit, string guard = "")
    {
        var a = Start(enabled); a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "and");
        a.EmitWord(0x2401); // MOVE.L D1,D2
        a.EmitWord((ushort)(0x7000 | 1 << bit)); // MOVEQ #mask,D0
        if (guard == "entry") a.Mark("and");
        a.EmitWord(0xC480); // AND.L D0,D2
        a.EmitWord(0x57C4); // SEQ D4 observes Z
        if (guard == "flags") a.EmitWord(0x55C5); // SCS D5 also observes C
        if (guard != "value") a.EmitWord(0x7400);
        if (guard != "mask") a.EmitWord(0x7000);
        return Finish(a, "TryFoldCopiedArgumentBitTest");
    }

    private static M68kAssembler Start(bool enabled) => new() {
        GeneratedCodeSizeOptions = new() { RemoveRedundantTransport = enabled },
        GeneratedCodeSizeRanges = [("start", "end")]
    };

    private static byte[] Boolean(bool enabled, int condition, bool inverted, string guard = "")
    {
        var a = Start(enabled); a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "interior");
        a.EmitWord((ushort)(0x50C0 | condition << 8)); // Scc D0
        if (guard == "entry") a.Mark("interior");
        a.EmitWord(0x4880); a.EmitWord(0x48C0); a.EmitWord(0x4480); // EXT.W/L; NEG.L
        a.EmitWord(0x4A00); // TST.B D0
        a.EmitBranch(inverted ? M68kCondition.Equal : M68kCondition.NotEqual, "yes");
        a.EmitWord(0x7207); a.EmitBranch(M68kCondition.True, "done");
        a.Mark("yes"); a.EmitWord(0x7209); a.Mark("done");
        if (guard != "value") a.EmitWord(0x7000); // retire the materialized value
        if (guard != "flags") a.EmitWord(0x5283); // ADDQ.L #1,D3 retires all CCR including X
        return Finish(a);
    }

    private static byte[] Compare(bool enabled, uint constant, string guard = "")
    {
        var a = Start(enabled); a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "compare");
        a.EmitWord(0x203C); a.EmitLong(constant); // MOVE.L #constant,D0
        if (guard == "entry") a.Mark("compare"); a.EmitWord((ushort)(guard == "same" ? 0xB080 : 0xB280)); // CMP.L D0,D1
        int status = a.Offset;
        a.EmitWord(0x40C4); // MOVE SR,D4 observes N/Z/V/C/X before any overwrite
        a.SetInstructionEffects(status, new(16, 16, 0, 0, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.None, M68kMemorySet.None, 0, false, false));
        if (guard != "value") a.EmitWord(0x7000);
        return Finish(a, "TryFoldLongConstantCompare");
    }

    private static byte[] Finish(M68kAssembler a, string? isolatedPass = null)
    {
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(0xFF, 0, 0x7F, 0x80, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end");
        if (isolatedPass is null) a.OptimizeForM68000();
        else if (a.GeneratedCodeSizeOptions?.RemoveRedundantTransport == true) {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var optimizer = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            typeof(M68kPeepholeOptimizer).GetMethod(isolatedPass, BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(optimizer, [M68kInstructionDataflow.Analyze(a)]);
        }
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, ushort ccr, uint value)
    {
        var bus = new TestBus(0x20000); code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA55AFF00u ^ (uint)n;
        for (int n = 0; n < 7; n++) cpu.State.A[n] = 0x13000u + (uint)n * 0x100;
        cpu.State.D[1] = value;
        for (int n = 0; n < 128 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x18004u, cpu.State.A[7]);
        return string.Join(',', cpu.State.D) + ":" + string.Join(',', cpu.State.A) + ":" + cpu.State.StatusRegister;
    }
}
