using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kNativeStoreRunTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(1, 8)]
    [InlineData(2, 4)]
    [InlineData(2, 16)]
    [InlineData(4, 2)]
    [InlineData(4, 4)]
    [InlineData(4, 8)]
    [InlineData(4, 16)]
    public void ContiguousStoresRetainMemoryAllRegistersAndFlags(int width, int count)
    {
        foreach (int address in new[] { 0, 3, 6 })
        {
            byte[] before = Build(false, width, count, address);
            byte[] after = Build(true, width, count, address);
            int updateBytes = width * count <= 8 ? 2 : 4;
            Assert.Equal(before.Length - 2 * (count - 1) - updateBytes, after.Length);
            foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
            foreach (uint pattern in new uint[] { 0, 0x80, 0x8000, 0x80000000, uint.MaxValue, 0xA55A00FF })
            foreach (ushort ccr in Enumerable.Range(0, 32).Select(x => (ushort)x))
                Assert.Equal(Run(before, model, address, pattern, ccr), Run(after, model, address, pattern, ccr));
        }
    }

    [Theory]
    [InlineData("interior")]
    [InlineData("update")]
    [InlineData("gap")]
    [InlineData("wrong-update")]
    [InlineData("effects")]
    [InlineData("stack")]
    public void DoesNotFoldObservableOrNoncontiguousRuns(string guard)
    {
        Assert.Equal(Build(false, 4, 4, guard == "stack" ? 7 : 0, guard),
            Build(true, 4, 4, guard == "stack" ? 7 : 0, guard));
    }

    private static byte[] Build(bool enabled, int width, int count, int address, string guard = "")
    {
        var a = new M68kAssembler {
            GeneratedCodeSizeOptions = new() { FoldNativeRamUpdates = enabled },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        int family = width == 1 ? 0x1000 : width == 2 ? 0x3000 : 0x2000;
        a.Mark("start");
        if (guard is "interior" or "update") a.EmitBranch(M68kCondition.Equal, guard);
        for (int n = 0; n < count; n++)
        {
            if (n == 1) a.Mark("interior");
            int offset = a.Offset;
            a.EmitWord((ushort)(family | (address << 9) | (n == 0 ? 0x80 : 0x140) | (n & 7)));
            if (n != 0) a.EmitWord((ushort)(n * width + (guard == "gap" ? width : 0)));
            if (n == 1 && guard == "effects")
                a.SetInstructionEffects(offset, new(0xFF, 0, 0x7F, 0,
                    M68kConditionCodeSet.None, M68kConditionCodeSet.All,
                    M68kMemorySet.None, M68kMemorySet.All, 0, false, false));
        }
        a.Mark("update");
        int bytes = width * count + (guard == "wrong-update" ? width : 0);
        if (bytes <= 8) a.EmitWord((ushort)(0x5088 | ((bytes & 7) << 9) | address));
        else { a.EmitWord((ushort)(0x41E8 | (address << 9) | address)); a.EmitWord((ushort)bytes); }
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(0xFF, 0, 0x7F, 0x80,
            M68kConditionCodeSet.All, M68kConditionCodeSet.None,
            M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end"); a.OptimizeForM68000();
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, int address, uint pattern, ushort ccr)
    {
        var bus = new TestBus(0x20000);
        code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        Array.Fill(bus.Memory, (byte)0x3C, 0x2FFC, 136);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        for (int n = 0; n < 8; n++) cpu.State.D[n] = pattern ^ (uint)(n * 0x01010101);
        for (int n = 0; n < 7; n++) cpu.State.A[n] = (uint)(0x4000 + n * 0x100);
        cpu.State.A[address] = 0x3000;
        for (int n = 0; n < 100 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        Assert.Equal(0x18004u, cpu.State.A[7]);
        return string.Join(",", cpu.State.D) + ":" + string.Join(",", cpu.State.A) + ":" +
            cpu.State.StatusRegister + ":" + Convert.ToHexString(bus.Memory.AsSpan(0x2FFC, 136));
    }
}
