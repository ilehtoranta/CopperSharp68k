using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kPollingFenceTests
{
    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 2)]
    [InlineData(6, 6)]
    public void AbsoluteFenceRetainsWordReadEveryPollAndAllObservableState(int an, int dn)
    {
        var before = Build(false, an, dn); var after = Build(true, an, dn);
        Assert.Equal(before.Length - 6, after.Length);
        foreach (var cpu in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (ushort ccr in Enumerable.Range(0, 32).Select(n => (ushort)n))
        foreach (ushort word in new ushort[] { 0, 1, 0x8000, 0xFFFF })
        foreach (int busy in new[] { 0, 1, 7 }) {
            Assert.Equal(Run(before, cpu, ccr, word, busy), Run(after, cpu, ccr, word, busy));
        }
    }

    [Theory]
    [InlineData("data-live")]
    [InlineData("address-live")]
    [InlineData("entry")]
    [InlineData("effects")]
    [InlineData("other-address")]
    [InlineData("wide-bit")]
    [InlineData("byte-read")]
    [InlineData("stack")]
    [InlineData("straddling-address")]
    public void RejectsObservableOrUnsupportedPatterns(string guard) =>
        Assert.Equal(Build(false, guard == "stack" ? 7 : 0, 0, guard),
            Build(true, guard == "stack" ? 7 : 0, 0, guard));

    private static byte[] Build(bool optimize, int an, int dn, string guard = "")
    {
        var a = new M68kAssembler {
            GeneratedCodeSizeOptions = new() { RemoveRedundantTransport = true },
            GeneratedCodeSizeRanges = [("start", "end")]
        };
        a.Mark("start");
        ushort restore = (ushort)((1 << dn) | (1 << (an + 8)));
        ushort save = (ushort)((1 << (15 - dn)) | (1 << (7 - an)));
        a.EmitWord(0x48E7); a.EmitWord(save);
        a.EmitWord((ushort)(0x207C | an << 9)); a.EmitLong(0x13000);
        a.EmitWord((ushort)(0x7000 | dn << 9));
        a.Mark("read"); int read = a.Offset;
        a.EmitWord((ushort)((guard == "byte-read" ? 0x1010 : 0x3010) | dn << 9 | an));
        a.Mark("loop"); a.EmitWord((ushort)(0x207C | an << 9));
        a.EmitLong(guard == "other-address" ? 0x13002u : 0x13000u);
        a.EmitWord((ushort)(0x0810 | an)); a.MarkAnalysisAnchor("operand-marker");
        a.EmitWord((ushort)(guard == "wide-bit" ? 8 : 6));
        a.EmitBranch(M68kCondition.NotEqual, "loop");
        if (guard == "data-live") a.EmitWord((ushort)(0x2200 | dn));
        if (guard == "address-live") a.EmitWord((ushort)(0x2248 | an));
        a.EmitWord(0x4CDF); a.EmitWord(restore);
        int ret = a.Offset; a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(0xFF, 0, 0x7F, 0x80, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "read");
        if (guard == "effects") a.SetInstructionEffects(read, new(0, 0, 0, 0,
            M68kConditionCodeSet.All, M68kConditionCodeSet.All, M68kMemorySet.All,
            M68kMemorySet.All, 0, true, false));
        a.Mark("end");
        if (guard == "straddling-address") a.EmitAddress("start", addend: a.Offset + 4);
        if (optimize) {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
                .GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            var fold = typeof(M68kPeepholeOptimizer).GetMethod("TryFoldAbsolutePollingFence",
                BindingFlags.Instance | BindingFlags.NonPublic)!.CreateDelegate<Func<M68kInstructionDataflow, bool>>(pass);
            Assert.Equal(guard.Length == 0, fold(M68kInstructionDataflow.Analyze(a)));
        }
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static string Run(byte[] code, M68kCpuModel model, ushort ccr, ushort word, int busy)
    {
        var bus = new PollBus(word, busy); code.CopyTo(bus.Inner.Memory, 0x10000);
        bus.Inner.WriteLong(0x18000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = (ushort)(0x2700 | ccr);
        for (int i = 0; i < 8; i++) cpu.State.D[i] = 0x12340000u + (uint)i;
        for (int i = 0; i < 7; i++) cpu.State.A[i] = 0x23450000u + (uint)i;
        for (int i = 0; i < 150 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        Assert.Equal("W" + new string('B', busy + 1), bus.Reads);
        return string.Join(",", cpu.State.D) + ":" + string.Join(",", cpu.State.A) + ":" +
            cpu.State.StatusRegister + ":" + Convert.ToHexString(bus.Inner.Memory.AsSpan(0x12FFC, 32));
    }

    private sealed class PollBus(ushort word, int busy) : IM68kBus
    {
        internal readonly TestBus Inner = new(0x20000);
        internal string Reads = "";
        public byte ReadByte(uint a, ref long c, M68kBusAccessKind k) {
            if (a != 0x13000) return Inner.ReadByte(a, ref c, k);
            Reads += "B"; return busy-- > 0 ? (byte)0x40 : (byte)0;
        }
        public ushort ReadWord(uint a, ref long c, M68kBusAccessKind k) {
            if (a != 0x13000) return Inner.ReadWord(a, ref c, k);
            Reads += "W"; return word;
        }
        public uint ReadLong(uint a, ref long c, M68kBusAccessKind k) => Inner.ReadLong(a, ref c, k);
        public void WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) => Inner.WriteByte(a, v, ref c, k);
        public void WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) => Inner.WriteWord(a, v, ref c, k);
        public void WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) => Inner.WriteLong(a, v, ref c, k);
        public void ResetExternalDevices(long c) { }
    }
}
