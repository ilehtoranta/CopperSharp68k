using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kPollingAddressTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(6)]
    public void PollingRetainsEveryReadRegisterAndFlag(int register)
    {
        byte[] before = Build(false, register), after = Build(true, register);
        Assert.NotEqual(before, after);
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (ushort ccr in Enumerable.Range(0, 32).Select(n => (ushort)n))
        foreach (int busyReads in new[] { 0, 1, 7 }) {
            var old = Run(before, model, ccr, busyReads);
            var current = Run(after, model, ccr, busyReads);
            Assert.Equal(old.State, current.State);
            Assert.Equal(busyReads + 1, current.Reads);
            if (busyReads != 0) Assert.True(current.Cycles < old.Cycles);
        }
    }

    [Theory]
    [InlineData("address-effects")]
    [InlineData("test-effects")]
    [InlineData("branch-effects")]
    [InlineData("postincrement")]
    [InlineData("other-register")]
    [InlineData("wide-bit")]
    [InlineData("stack")]
    [InlineData("interior-entry")]
    public void KeepsUnsupportedOrAnnotatedLoops(string guard) =>
        Assert.Equal(Build(false, guard == "stack" ? 7 : 0, guard),
            Build(true, guard == "stack" ? 7 : 0, guard));

    private static byte[] Build(bool enabled, int register, string guard = "")
    {
        var a = new M68kAssembler {
            GeneratedCodeSizeOptions = new() { RemoveRedundantTransport = enabled },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        a.Mark("start");
        a.EmitBranch(M68kCondition.Equal, "poll"); // forward entry must still load An
        a.EmitWord(0x4E71);
        a.Mark("poll"); int address = a.Offset;
        a.EmitWord((ushort)(0x207C | register << 9)); a.EmitLong(0x13000);
        int test = a.Offset;
        a.EmitWord((ushort)((guard == "postincrement" ? 0x0818 : 0x0810) |
            (guard == "other-register" ? 1 : register)));
        a.Mark("bit-operand"); // an unreferenced source marker left by byte-load folding
        a.EmitWord((ushort)(guard == "wide-bit" ? 8 : 6));
        int branch = a.Offset; a.EmitBranch(M68kCondition.NotEqual, "poll");
        int annotated = guard switch { "address-effects" => address, "test-effects" => test,
            "branch-effects" => branch, _ => -1 };
        if (annotated >= 0) a.SetInstructionEffects(annotated, new(0, 0, 0, 0,
            M68kConditionCodeSet.All, M68kConditionCodeSet.All, M68kMemorySet.All,
            M68kMemorySet.All, 0, true, false));
        int ret = a.Offset; a.EmitWord(0x4E75);
        if (guard == "interior-entry") a.EmitBranch(M68kCondition.Equal, "bit-operand");
        a.SetInstructionEffects(ret, new(0xFF, 0, 0x7F, 0x80, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        a.Mark("end"); a.OptimizeForM68000();
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static (string State, int Reads, long Cycles) Run(byte[] code, M68kCpuModel model, ushort ccr, int busyReads)
    {
        var bus = new PollingBus(busyReads);
        code.CopyTo(bus.Inner.Memory, 0x10000); bus.Inner.WriteLong(0x18000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA55AFF00u ^ (uint)n;
        for (int n = 0; n < 7; n++) cpu.State.A[n] = (uint)(0x4000 + n * 0x100);
        for (int n = 0; n < 100 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return (string.Join(",", cpu.State.D) + ":" + string.Join(",", cpu.State.A) + ":" +
            cpu.State.StatusRegister + ":" + Convert.ToHexString(bus.Inner.Memory.AsSpan(0x12FFC, 32)),
            bus.Reads, cpu.State.Cycles);
    }

    private sealed class PollingBus(int busyReads) : IM68kBus
    {
        internal readonly TestBus Inner = new(0x20000);
        internal int Reads;
        public byte ReadByte(uint address, ref long cycle, M68kBusAccessKind kind) =>
            address == 0x13000 ? (byte)(Reads++ < busyReads ? 0x40 : 0) : Inner.ReadByte(address, ref cycle, kind);
        public ushort ReadWord(uint address, ref long cycle, M68kBusAccessKind kind) => Inner.ReadWord(address, ref cycle, kind);
        public uint ReadLong(uint address, ref long cycle, M68kBusAccessKind kind) => Inner.ReadLong(address, ref cycle, kind);
        public void WriteByte(uint address, byte value, ref long cycle, M68kBusAccessKind kind) => Inner.WriteByte(address, value, ref cycle, kind);
        public void WriteWord(uint address, ushort value, ref long cycle, M68kBusAccessKind kind) => Inner.WriteWord(address, value, ref cycle, kind);
        public void WriteLong(uint address, uint value, ref long cycle, M68kBusAccessKind kind) => Inner.WriteLong(address, value, ref cycle, kind);
        public void ResetExternalDevices(long cycle) { }
    }
}
