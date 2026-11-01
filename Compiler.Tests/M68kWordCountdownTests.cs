using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kWordCountdownTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CountdownPreservesWordWrapAndLiveFlags(bool observeCarry)
    {
        var before = Build(false, observeCarry);
        var after = Build(true, observeCarry);
        Assert.Equal(!observeCarry, after.AsSpan().IndexOf(new byte[] { 0x51, 0xC9 }) >= 0);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (uint count in new uint[] { 0, 1, 7, 0xFFFF, 0xA5A50000 })
            Assert.Equal(Run(before, model, count), Run(after, model, count));
    }

    private static byte[] Build(bool optimize, bool carry)
    {
        var a = new M68kAssembler();
        a.Mark("method:countdown"); a.EmitWord(0x7400); // MOVEQ #0,D2
        a.Mark("loop"); a.EmitWord(0x5282); // ADDQ.L #1,D2
        a.EmitWord(0x5341); // SUBQ.W #1,D1
        a.EmitWord(0x203C); a.EmitLong(0xFFFF); // MOVE.L #$FFFF,D0
        a.EmitWord(0xB240); // CMP.W D0,D1
        a.EmitBranch(M68kCondition.NotEqual, "loop");
        if (carry) a.EmitWord(0x55C4); // SCS D4 observes flags from the comparison.
        a.EmitWord(0x5283); // ADDQ.L #1,D3 overwrites all otherwise dead flags.
        a.EmitWord(0x2002); // MOVE.L D2,D0
        a.EmitWord(0x4E75);
        a.SetInstructionEffects(a.Offset - 2, new(0xFF, 0, 0x7F, 0x80,
            M68kConditionCodeSet.All, M68kConditionCodeSet.None,
            M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("method:countdown:end");
        if (optimize) a.OptimizeForM68000();
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, uint count)
    {
        var bus = new TestBus(0x20000); code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = 0x271F;
        cpu.State.D[1] = count;
        for (int i = 0; i < 400000 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x18004u, cpu.State.A[7]);
        Assert.Equal((count & 0xFFFF) + 1, cpu.State.D[0]);
        Assert.Equal((count & 0xFFFF0000) | 0xFFFF, cpu.State.D[1]);
        return string.Join(",", cpu.State.D) + ":" + cpu.State.StatusRegister;
    }
}
