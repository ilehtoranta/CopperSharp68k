using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kMemoryBitTestTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(7)]
    public void ByteReadAndTestRetainResultsMemoryAndLiveFlags(int mode)
    {
        for (int bit = 0; bit < 8; bit++) {
            byte[] before = Build(false, mode, bit), after = Build(true, mode, bit);
            Assert.True(after.Length < before.Length);
            foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
            foreach (byte value in new byte[] { 0, 1, 0x40, 0x80, 0xA5, 0xFF })
            foreach (ushort ccr in Enumerable.Range(0, 32).Select(n => (ushort)n))
                Assert.Equal(Run(before, model, value, ccr), Run(after, model, value, ccr));
        }
    }

    [Theory]
    [InlineData("value")]
    [InlineData("flags")]
    [InlineData("entry")]
    [InlineData("wide-bit")]
    public void KeepsObservableLoad(string guard) => Assert.Equal(Build(false, 5, 6, guard), Build(true, 5, 6, guard));

    private static byte[] Build(bool enabled, int mode, int bit, string guard = "")
    {
        var a = new M68kAssembler {
            GeneratedCodeSizeOptions = new() { RemoveRedundantTransport = enabled },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        a.Mark("start");
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "test");
        a.EmitWord(0x7000); // MOVEQ #0,D0
        a.EmitWord((ushort)(0x1000 | mode << 3 | (mode == 7 ? 1 : 0)));
        if (mode == 5) a.EmitWord(4);
        if (mode == 7) a.EmitLong(0x13004);
        if (guard == "entry") a.Mark("test");
        a.EmitWord(0x0800); a.EmitWord((ushort)(guard == "wide-bit" ? 8 : bit));
        a.EmitWord(0x57C1); // SEQ D1: make the bit-test result observable
        if (guard != "value" && guard != "flags") a.EmitWord(0x7007); // retire D0/NZVC
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(0xFF, 0, 0x7F, 0x80, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end"); a.OptimizeForM68000();
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, byte value, ushort ccr)
    {
        var bus = new TestBus(0x20000); code.CopyTo(bus.Memory, 0x10000);
        bus.WriteLong(0x18000, 0x1000); bus.Memory[0x13000] = value; bus.Memory[0x13004] = value;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA55AFF00u ^ (uint)n;
        for (int n = 0; n < 7; n++) cpu.State.A[n] = (uint)(0x13000 + n * 0x100);
        for (int n = 0; n < 100 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return string.Join(",", cpu.State.D) + ":" + string.Join(",", cpu.State.A) + ":" +
            cpu.State.StatusRegister + ":" + Convert.ToHexString(bus.Memory.AsSpan(0x13000, 16));
    }
}
