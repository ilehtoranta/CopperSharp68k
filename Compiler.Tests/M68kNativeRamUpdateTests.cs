using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kNativeRamUpdateTests
{
    [Theory]
    [InlineData(1, 0x1000)]
    [InlineData(2, 0x3000)]
    [InlineData(4, 0x2000)]
    public void NativeStoreIncrementRetainsMemoryRegistersAndEveryCcr(int width, int family)
    {
        byte[] BuildStore(bool enabled, int address = 0, bool interiorEntry = false, int increment = 0)
        {
            var a = new M68kAssembler {
                GeneratedCodeSizeOptions = new() { FoldNativeRamUpdates = enabled },
                GeneratedCodeSizeRanges = [("start", "end")]
            };
            a.Mark("start");
            if (interiorEntry) a.EmitBranch(M68kCondition.Equal, "increment");
            a.EmitWord((ushort)(family | (address << 9) | 0x81)); // D1,(An)
            a.Mark("increment");
            a.EmitWord((ushort)(0x5088 | ((increment == 0 ? width : increment) << 9) | address));
            a.EmitWord(0x4E75);
            a.SetInstructionEffects(a.Offset - 2, new(0xFF, 0, 0x7F, 0x80,
                M68kConditionCodeSet.All, M68kConditionCodeSet.None,
                M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
            a.Mark("end"); a.OptimizeForM68000();
            return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
        }
        var before = BuildStore(false); var after = BuildStore(true);
        Assert.Equal(before.Length - 2, after.Length);
        Assert.Equal(BuildStore(false, 7), BuildStore(true, 7));
        Assert.Equal(BuildStore(false, interiorEntry: true), BuildStore(true, interiorEntry: true));
        Assert.Equal(BuildStore(false, increment: 8), BuildStore(true, increment: 8));
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (uint value in new uint[] { 0, 1, 0x80, 0x8000, 0x80000000, uint.MaxValue })
        foreach (ushort ccr in Enumerable.Range(0, 32).Select(x => (ushort)x))
            Assert.Equal(Run(before, model, value, ccr), Run(after, model, value, ccr));
    }

    [Fact]
    public void CarriesAddressButPublishesFinalDataPointer()
    {
        byte[] BuildRows(bool enabled)
        {
            var a = new M68kAssembler {
                GeneratedCodeSizeOptions = new() { FoldNativeRamUpdates = enabled },
                GeneratedCodeSizeRanges = [("start", "end")]
            };
            a.Mark("start"); a.Mark("rows");
            a.EmitWord(0x2044); // MOVEA.L D4,A0
            a.EmitWord(0xC390); // AND.L D1,(A0)
            a.EmitWord(0xD883); // ADD.L D3,D4
            a.EmitDbra(2, "rows");
            a.EmitWord(0x2004); // MOVE.L D4,D0: observe final pointer
            a.EmitWord(0x5285); // ADDQ.L #1,D5: overwrite dead loop flags
            a.EmitWord(0x4E75);
            a.SetInstructionEffects(a.Offset - 2, new(0xFF, 0, 0x7C, 0x80,
                M68kConditionCodeSet.All, M68kConditionCodeSet.None,
                M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
            a.Mark("end"); a.OptimizeForM68000();
            return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
        }
        var before = BuildRows(false); var after = BuildRows(true);
        Assert.Contains("D1C3", Convert.ToHexString(after)); // ADDA.L D3,A0
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (int rows in new[] { 1, 2, 8, 16 })
        {
            string RunRows(byte[] code)
            {
                var bus = new TestBus(0x20000); code.CopyTo(bus.Memory, 0x10000);
                bus.WriteLong(0x18000, 0x1000);
                Array.Fill(bus.Memory, (byte)0xA5, 0x3000, 256);
                using var cpu = M68kCoreFactory.Default.Create(model, bus);
                cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = 0x271F;
                cpu.State.D[1] = 0x1FFFFFE0; cpu.State.D[2] = (uint)(rows - 1);
                cpu.State.D[3] = 8; cpu.State.D[4] = 0x3000;
                for (int i = 0; i < 200 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal((uint)(0x3000 + 8 * rows), cpu.State.D[0]);
                return string.Join(",", cpu.State.D) + ":" + cpu.State.StatusRegister + ":" +
                    Convert.ToHexString(bus.Memory.AsSpan(0x3000, 256));
            }
            Assert.Equal(RunRows(before), RunRows(after));
        }
    }

    [Theory]
    [InlineData(0x8000)]
    [InlineData(0xB000)]
    [InlineData(0xC000)]
    public void PreservesPixelsFlagsAndLiveRegisters(int operation)
    {
        foreach (int size in new[] { 0, 1, 2 })
        foreach (bool liveTemporary in new[] { false, true })
        {
            byte[] before = Build(operation, size, false, liveTemporary);
            byte[] after = Build(operation, size, true, liveTemporary);
            Assert.Equal(liveTemporary ? before.Length : before.Length - 4, after.Length);
            foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
            foreach (uint mask in new uint[] { 0, 1, 0x8000, 0x80000000, 0x1FFFFFE0, uint.MaxValue })
            foreach (ushort ccr in Enumerable.Range(0, 32).Select(x => (ushort)x))
                Assert.Equal(Run(before, model, mask, ccr), Run(after, model, mask, ccr));
        }
    }

    private static byte[] Build(int op, int size, bool enabled, bool live)
    {
        var a = new M68kAssembler {
            GeneratedCodeSizeOptions = new() { FoldNativeRamUpdates = enabled },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        int family = size == 0 ? 0x1000 : size == 1 ? 0x3000 : 0x2000;
        a.Mark("start");
        a.EmitWord((ushort)(family | 0x10)); // MOVE.(width) (A0),D0
        a.EmitWord((ushort)(op | (size << 6) | (op == 0xB000 ? 0x300 : 1)));
        a.EmitWord((ushort)(family | 0x80)); // MOVE.(width) D0,(A0)
        if (live) a.EmitWord(0x2400); // Observe temporary, preventing fold.
        a.EmitWord(0x4E75);
        a.SetInstructionEffects(a.Offset - 2, new(0xFE, 0, 0x7F, 0x80,
            M68kConditionCodeSet.All, M68kConditionCodeSet.None,
            M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end");
        a.OptimizeForM68000();
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, uint mask, ushort ccr)
    {
        var bus = new TestBus(0x20000);
        code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(0x18000, 0x1000);
        bus.WriteLong(0x3000, 0xF23489AB); bus.WriteLong(0x3004, 0xDEADBEEF);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        cpu.State.D[0] = 0xA5A5A5A5; cpu.State.D[1] = mask; cpu.State.A[0] = 0x3000;
        for (int i = 0; i < 30 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(0x18004u, cpu.State.A[7]);
        return string.Join(",", cpu.State.D.Skip(1)) + ":" + string.Join(",", cpu.State.A) + ":" +
            cpu.State.StatusRegister + ":" + Convert.ToHexString(bus.Memory.AsSpan(0x3000, 8));
    }
}
