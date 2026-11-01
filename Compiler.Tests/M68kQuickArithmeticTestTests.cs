using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kQuickArithmeticTestTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0x40)]
    [InlineData(0x80)]
    public void PreservesResultsAndObservableFlags(int size)
    {
        foreach (bool subtract in new[] { false, true })
        foreach (bool observeCarry in new[] { false, true })
        foreach (bool observeOverflow in new[] { false, true })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        {
            var original = Fixture(size, subtract, observeCarry, observeOverflow);
            var optimized = Fixture(size, subtract, observeCarry, observeOverflow);
            optimized.OptimizeForCpu(M68kCpuTarget.M68000);
            var before = original.Link(0x10000, new Dictionary<string, uint>()).Bytes;
            var after = optimized.Link(0x10000, new Dictionary<string, uint>()).Bytes;
            if (!observeCarry && !observeOverflow) Assert.True(after.Length < before.Length);
            foreach (uint input in new uint[] { 0, 1, 0x7F, 0x80, 0xFF, 0x7FFF, 0x8000, 0xFFFF, 0x7FFFFFFF, 0x80000000, 0xFFFFFFFF })
            foreach (ushort ccr in Enumerable.Range(0, 32).Select(x => (ushort)x))
                Assert.Equal(Run(before, model, input, ccr), Run(after, model, input, ccr));
        }
    }

    private static M68kAssembler Fixture(int size, bool subtract, bool carry, bool overflow)
    {
        var a = new M68kAssembler();
        a.Mark("method:quick-test");
        a.EmitWord((ushort)((subtract ? 0x5300 : 0x5200) | size));
        a.EmitWord((ushort)(0x4A00 | size));
        a.EmitWord(0x57C1); // SEQ D1
        if (carry) a.EmitWord(0x55C2); // SCS D2
        if (overflow) a.EmitWord(0x59C3); // SVS D3
        a.EmitWord(0x2080); // MOVE.L D0,(A0): makes final NZVC independent of removed TST
        a.EmitWord(0x4E75);
        a.Mark("method:quick-test:end");
        return a;
    }

    private static string Run(byte[] code, M68kCpuModel model, uint input, ushort ccr)
    {
        var bus = new TestBus(0x20000);
        code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000);
        cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        cpu.State.D[0] = input;
        cpu.State.D[1] = cpu.State.D[2] = cpu.State.D[3] = 0xA5A5A5A5;
        cpu.State.A[0] = 0x3000;
        for (int i = 0; i < 32 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        Assert.Equal(0x18004u, cpu.State.A[7]);
        return string.Join(",", cpu.State.D.Take(4)) + ":" + cpu.State.StatusRegister + ":" + bus.ReadLong(0x3000);
    }
}
