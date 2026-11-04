using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kConstantAddressReloadTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void ReusesAddressAcrossStoresWithoutChangingRegistersMemoryOrFlags(int address)
    {
        byte[] before = Build(false, address), after = Build(true, address);
        Assert.Equal(before.Length - 12, after.Length);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (ushort ccr in Enumerable.Range(0, 32).Select(n => (ushort)n))
            Assert.Equal(Run(before, model, ccr), Run(after, model, ccr));
    }

    [Fact]
    public void OutgoingConditionalBranchPreservesBothPaths()
    {
        byte[] before = Build(false, 0, "outgoing"), after = Build(true, 0, "outgoing");
        Assert.Equal(before.Length - 12, after.Length);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (ushort ccr in Enumerable.Range(0, 32).Select(n => (ushort)n))
            Assert.Equal(Run(before, model, ccr), Run(after, model, ccr));
    }

    [Theory]
    [InlineData("entry")]
    [InlineData("clobber")]
    [InlineData("postincrement")]
    [InlineData("effects")]
    [InlineData("different")]
    [InlineData("stack")]
    public void KeepsReloadWhenItsValueOrEntryIsNotGuaranteed(string guard)
    {
        Assert.Equal(Build(false, guard == "stack" ? 7 : 0, guard),
            Build(true, guard == "stack" ? 7 : 0, guard));
    }

    private static byte[] Build(bool enabled, int address, string guard = "")
    {
        var a = new M68kAssembler {
            GeneratedCodeSizeOptions = new() { RemoveRedundantTransport = enabled },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "reload");
        int count = guard.Length == 0 || guard == "outgoing" ? 3 : 2;
        for (int n = 0; n < count; n++)
        {
            if (n == 1 && guard == "entry") a.Mark("reload");
            if (n == 1 && guard.Length == 0) a.Mark("unreferenced");
            a.EmitWord((ushort)(0x207C | address << 9));
            a.EmitLong((uint)(0x13000 + (guard == "different" && n == 1 ? 16 : 0)));
            if (n == 0 && guard == "outgoing") a.EmitBranch(M68kCondition.NotEqual, "return");
            int store = a.Offset;
            a.EmitWord((ushort)(0x3140 | address << 9)); // MOVE.W D0,disp(An)
            a.EmitWord((ushort)(n * 2));
            if (n == 0 && guard == "effects")
                a.SetInstructionEffects(store, new(1, 0, (ushort)(1 << address), 0,
                    M68kConditionCodeSet.None, M68kConditionCodeSet.All,
                    M68kMemorySet.None, M68kMemorySet.All, 0, false, false));
            if (n == 0 && guard == "clobber") {
                a.EmitWord((ushort)(0x2041 | address << 9)); // MOVEA.L D1,An
                a.EmitWord((ushort)(0x3080 | address << 9)); // observe the changed address
            }
            if (n == 0 && guard == "postincrement") a.EmitWord((ushort)(0x30C0 | address << 9));
        }
        a.Mark("return"); int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(0xFF, 0, 0x7F, 0x80,
            M68kConditionCodeSet.All, M68kConditionCodeSet.None,
            M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end"); a.OptimizeForM68000();
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, ushort ccr)
    {
        var bus = new TestBus(0x20000);
        code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        Array.Fill(bus.Memory, (byte)0x3C, 0x12FFC, 32);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA55AFF00u ^ (uint)n;
        for (int n = 0; n < 7; n++) cpu.State.A[n] = (uint)(0x4000 + n * 0x100);
        for (int n = 0; n < 100 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return string.Join(",", cpu.State.D) + ":" + string.Join(",", cpu.State.A) + ":" +
            cpu.State.StatusRegister + ":" + Convert.ToHexString(bus.Memory.AsSpan(0x12FFC, 32));
    }
}
