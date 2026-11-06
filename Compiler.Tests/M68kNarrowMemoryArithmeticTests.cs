using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kNarrowMemoryArithmeticTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000)]
    [InlineData(M68kCpuModel.M68EC020)]
    [InlineData(M68kCpuModel.M68040)]
    public void FoldedNarrowLoadsKeepArithmeticFlagsRegistersAndAccessWidths(M68kCpuModel model)
    {
        foreach (var size in new[] { 0, 1 })
        foreach (var operation in new[] { 0x9000, 0xB000, 0xD000 })
        foreach (var mode in new[] { 2, 3, 4, 5 })
        {
            var before = Code(false, size, operation, mode);
            var after = Code(true, size, operation, mode);
            Assert.Equal(before.Length - 2, after.Length);
            foreach (var value in new uint[] { 0, 1, 0x7F, 0x80, 0xFF, 0x7FFF, 0x8000, 0xFFFF })
            for (ushort flags = 0; flags < 32; flags++)
            {
                var original = Execute(before, model, value, flags);
                var optimized = Execute(after, model, value, flags);
                Assert.Equal(original, optimized);
            }
        }
    }

    [Theory]
    [InlineData("live")] [InlineData("entry")] [InlineData("marker")] [InlineData("effects")] [InlineData("width")]
    public void NarrowForwardingKeepsObservableTemporariesEntriesAndEffects(string guard)
        => Assert.Equal(Code(false, 1, 0xD000, 5, guard), Code(true, 1, 0xD000, 5, guard));

    private static byte[] Code(bool optimize, int size, int operation, int mode, string guard = "")
    {
        var a = new M68kAssembler();
        if (guard == "entry") a.EmitBranch(M68kCondition.Equal, "arithmetic");
        a.EmitWord(0x7000); // MOVEQ #0,D0
        var load = a.Offset;
        a.EmitWord((ushort)((size == 0 ? 0x1000 : 0x3000) | mode << 3));
        if (mode == 5) a.EmitWord(2);
        if (guard == "effects") a.SetInstructionEffects(load, new(0, 1, 1, 0,
            M68kConditionCodeSet.None, M68kConditionCodeSet.All, M68kMemorySet.Unknown,
            M68kMemorySet.None, 0, false, false));
        if (guard is "entry" or "marker") a.Mark("arithmetic");
        a.EmitWord((ushort)(operation | 2 << 9 | (guard == "width" ? 2 : size) << 6));
        var status = a.Offset;
        a.EmitWord(0x40C5); // MOVE.W SR,D5: observe all arithmetic flags
        a.SetInstructionEffects(status, new(0, 32, 0, 0, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.None, M68kMemorySet.None, 0, false, false));
        if (guard != "live") a.EmitWord(0x7000);
        var ret = a.Offset;
        a.EmitWord(0x4E75);
        a.SetInstructionEffects(ret, new(255, 0, 127, 128, M68kConditionCodeSet.All,
            M68kConditionCodeSet.None, M68kMemorySet.Stack, M68kMemorySet.None, 4, true, false));
        if (optimize)
        {
            var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler).GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
            var pass = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
            typeof(M68kPeepholeOptimizer).GetMethod("TryForwardMemoryLoadIntoArithmetic", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(pass, [M68kInstructionDataflow.Analyze(a)]);
        }
        return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
    }

    private static uint[] Execute(byte[] code, M68kCpuModel model, uint value, ushort flags)
    {
        var bus = new AccessBus(); code.CopyTo(bus.Inner.Memory, 0x10000);
        bus.Inner.WriteLong(0x3000, value << 16 | value);
        bus.Inner.WriteLong(0x80000, 0x1000);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(0x10000, 0x80000); cpu.State.StatusRegister = (ushort)(0x2700 | flags);
        for (var n = 0; n < 8; n++) cpu.State.D[n] = 0xA555FFFFu + (uint)n;
        cpu.State.A[0] = 0x3002;
        for (var n = 0; n < 100 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
        return cpu.State.D.ToArray().Concat(cpu.State.A.ToArray()).Append(cpu.State.StatusRegister)
            .Concat(bus.Accesses).ToArray();
    }

    private sealed class AccessBus : IM68kBus
    {
        public TestBus Inner { get; } = new(0x100000);
        public List<uint> Accesses { get; } = [];
        private void Record(uint address, uint width) { if (address is >= 0x3000 and < 0x3010) { Accesses.Add(address); Accesses.Add(width); } }
        public byte ReadByte(uint a, ref long c, M68kBusAccessKind k) { Record(a, 1); return Inner.ReadByte(a, ref c, k); }
        public ushort ReadWord(uint a, ref long c, M68kBusAccessKind k) { Record(a, 2); return Inner.ReadWord(a, ref c, k); }
        public uint ReadLong(uint a, ref long c, M68kBusAccessKind k) { Record(a, 4); return Inner.ReadLong(a, ref c, k); }
        public void WriteByte(uint a, byte v, ref long c, M68kBusAccessKind k) => Inner.WriteByte(a, v, ref c, k);
        public void WriteWord(uint a, ushort v, ref long c, M68kBusAccessKind k) => Inner.WriteWord(a, v, ref c, k);
        public void WriteLong(uint a, uint v, ref long c, M68kBusAccessKind k) => Inner.WriteLong(a, v, ref c, k);
        public void ResetExternalDevices(long cycle) { }
        public bool HasHostGateway(uint address) => false;
        public bool TryInvokeHostGateway(uint pc, uint token, M68kCpuState state) => false;
    }
}
