using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class GeneratedHelperContinuationTests
{
    public static IEnumerable<object[]> Layouts()
    {
        foreach (var padding in new[] { 0, 120, 128, 32748 })
        foreach (var absolute in new[] { false, true })
        foreach (var helperFirst in new[] { false, true })
            if (padding < 32748 || absolute) yield return [padding, absolute, helperFirst];
    }

    [Theory, MemberData(nameof(Layouts))]
    public void InliningPreservesCallEntryAndContinuationLabels(int padding, bool absolute, bool helperFirst)
    {
        foreach (var enabled in new[] { false, true })
        {
            var assembler = new M68kAssembler {
                GeneratedCodeSizeOptions = enabled ? new() { InlineMemoryHelpers = true } : null,
                GeneratedCodeSizeRanges = [("first", "first:end"), ("second", "second:end")],
                GeneratedPrivateHelperRanges = [("helper", "helper:end")]
            };
            void Helper()
            {
                assembler.Mark("helper");
                foreach (var word in new ushort[] { 0x2080, 0x2280, 0x4e75 }) assembler.EmitWord(word);
                assembler.Mark("helper:end");
            }
            if (helperFirst) Helper();
            foreach (var name in new[] { "first", "second" })
            {
                assembler.Mark(name);
                assembler.EmitWord(0x4a81); // TST.L D1
                assembler.EmitBranch(M68kCondition.Equal, name + "-next");
                for (var offset = 0; offset < padding; offset += 2) assembler.EmitWord(0x4e71);
                assembler.Mark(name + "-call");
                if (absolute) assembler.EmitJsr("helper", false); else assembler.EmitBsr("helper");
                assembler.Mark(name + "-next");
                assembler.Mark(name + "-alias");
                assembler.EmitWord(0x702a); // MOVEQ #42,D0
                assembler.EmitWord(0x4e75);
                assembler.Mark(name + ":end");
            }
            if (!helperFirst) Helper();
            assembler.MarkDataStart();
            assembler.Mark("pointers");
            foreach (var name in new[] { "first", "second" })
            {
                assembler.EmitAddress(name + "-call");
                assembler.EmitAddress(name + "-next");
            }
            assembler.OptimizeForM68000();
            if (enabled) Assert.Equal(1, assembler.GeneratedCodeSizeStatistics[nameof(M68kCodeSizeOptions.InlineMemoryHelpers)].Rewrites);
            foreach (var load in new uint[] { 0x10000, 0x20000 })
            {
                var code = assembler.Link(load, new Dictionary<string, uint>());
                foreach (var name in new[] { "first", "second" })
                {
                    Assert.Equal(code.Labels[name + "-next"], code.Labels[name + "-alias"]);
                    Assert.Equal((ushort)0x702a, BinaryPrimitives.ReadUInt16BigEndian(code.Bytes.AsSpan(code.Labels[name + "-next"])));
                    if (enabled) Assert.Equal((ushort)0x2080, BinaryPrimitives.ReadUInt16BigEndian(code.Bytes.AsSpan(code.Labels[name + "-call"])));
                    var pointerOffset = code.Labels["pointers"] + (name == "first" ? 0 : 8);
                    Assert.Equal(load + (uint)code.Labels[name + "-call"], BinaryPrimitives.ReadUInt32BigEndian(code.Bytes.AsSpan(pointerOffset)));
                    Assert.Equal(load + (uint)code.Labels[name + "-next"], BinaryPrimitives.ReadUInt32BigEndian(code.Bytes.AsSpan(pointerOffset + 4)));
                    foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
                    foreach (var input in new uint[] { 0, 1 })
                    {
                        var bus = new TestBus(0x100000);
                        code.Bytes.CopyTo(bus.Memory, load);
                        bus.WriteLong(0x80000, 0x1000);
                        bus.WriteLong(0x40000, 0x12345678); bus.WriteLong(0x40004, 0x87654321);
                        using var cpu = M68kCoreFactory.Default.Create(model, bus);
                        cpu.Reset(load + (uint)code.Labels[name], 0x80000);
                        cpu.State.StatusRegister = 0x2710;
                        cpu.State.D[0] = 0; cpu.State.D[1] = input; cpu.State.D[2] = 0x23456789;
                        cpu.State.A[0] = 0x40000; cpu.State.A[1] = 0x40004; cpu.State.A[5] = 0x55555555; cpu.State.A[6] = 0x66666666;
                        for (var step = 0; step < 20000 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
                        Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                        Assert.Equal(42u, cpu.State.D[0]); Assert.Equal(input, cpu.State.D[1]);
                        Assert.Equal(0x23456789u, cpu.State.D[2]);
                        Assert.Equal(0x40000u, cpu.State.A[0]); Assert.Equal(0x40004u, cpu.State.A[1]); Assert.Equal(0x55555555u, cpu.State.A[5]); Assert.Equal(0x66666666u, cpu.State.A[6]);
                        Assert.Equal(0x80004u, cpu.State.A[7]); Assert.Equal((ushort)0x2710, cpu.State.StatusRegister);
                        Assert.Equal(input == 0 ? 0x12345678u : 0, bus.ReadLong(0x40000));
                        Assert.Equal(input == 0 ? 0x87654321u : 0, bus.ReadLong(0x40004));
                    }
                }
            }
        }
    }
}
