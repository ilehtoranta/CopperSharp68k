using System.Reflection;
using System.Text.Json;
using Copper68k;
using CopperSharp.Compiler.Backend;


namespace CopperSharp.Compiler.Tests;

public sealed class M68kLocalSuffixExecutionTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000)]
    [InlineData(M68kCpuModel.M68020)]
    [InlineData(M68kCpuModel.M68040)]
    public void DifferentReturnValuesShareOnlyTheStackRestoreSequence(M68kCpuModel model)
    {
        var control = Create(4);
        var candidate = Create(4);
        var stats = Merge(candidate);
        Assert.Equal(1, stats.Groups);
        Assert.Equal(3, stats.MergedCopies);
        Assert.Equal(3, stats.Trampolines);
        Assert.Equal(18, stats.NetBytesSaved);
        Assert.Equal(control.Buffer.Bytes.Count - 18, candidate.Buffer.Bytes.Count);
        Assert.DoesNotContain(candidate.Buffer.Labels.Keys, key => key.StartsWith("__c68k_local", StringComparison.Ordinal));
        foreach (var value in new uint[] { 0, 1, 2, 3, 0x80000000, 0xffffffff })
        foreach (var residue in new uint[] { 0, 2 })
        foreach (var flags in new ushort[] { 0, 31 })
        {
            var before = Execute(control, model, value, residue, flags);
            var after = Execute(candidate, model, value, residue, flags);
            Assert.Equal(value < 3 ? 10u + value : 42u, after.Data[0]);
            Assert.Equal(before.Data, after.Data);
            Assert.Equal(before.Addresses, after.Addresses);
            Assert.Equal(before.Flags, after.Flags);
            if (value >= 3) Assert.Equal(before.Cycles, after.Cycles);
            if (model == M68kCpuModel.M68000)
                Assert.InRange(after.Cycles - before.Cycles, 0L, 10L);
        }
    }

    [Theory]
    [InlineData("anchor")]
    [InlineData("effect")]
    [InlineData("address")]
    [InlineData("pc-relative")]
    [InlineData("alignment")]
    [InlineData("label")]
    [InlineData("address-taken-block")]
    public void ProtectedRestoreSequencesAreNotMoved(string protection)
    {
        var fixture = Create(2);
        var buffer = fixture.Buffer;
        var start = buffer.Labels["error0"] + 2; // After its distinct MOVEQ result.
        switch (protection)
        {
            case "anchor": buffer.AnalysisAnchors.Add("protected", start + 2); break;
            case "effect":
                var instruction = fixture.Assembler.GetInstructionStream(start).First();
                buffer.InstructionEffectOverrides.Add(start, M68kInstructionDataflow.GetEffects(instruction)); break;
            case "address": buffer.Addresses.Add(new AddressFixup(start + 2, "external", true)); break;
            case "pc-relative": buffer.PcRelative.Add(new PcRelativeFixup(start + 2, "entry")); break;
            case "alignment":
                buffer.Labels.Add("aligned", start); fixture.Assembler.RequestLongAlignment("aligned"); break;
            case "label": buffer.Labels.Add("protected", start + 4); break;
            case "address-taken-block": fixture.Assembler.EmitAddress("error0"); break;
        }
        var before = Snapshot(buffer);
        Assert.Equal(0, Merge(fixture).MergedCopies);
        Assert.Equal(before, Snapshot(buffer));
    }

    [Fact]
    public void ADisabledSuffixPassDoesNotMergeDifferentCompleteReturnBlocks()
    {
        var fixture = Create(4);
        var before = Snapshot(fixture.Buffer);
        Assert.Equal(0, new M68kTerminalEpilogueMerger(fixture.Assembler, fixture.Buffer, true).Run().MergedCopies);
        Assert.Equal(before, Snapshot(fixture.Buffer));
    }

    private static M68kTerminalEpilogueMerger.Statistics Merge(Fixture fixture) =>
        new M68kTerminalEpilogueMerger(fixture.Assembler, fixture.Buffer,
            enableMethodLocalReuse: true, enableStackRestoreSuffixReuse: true).Run();

    private sealed record Fixture(M68kAssembler Assembler, M68kAssemblyBuffer Buffer);
    private static Fixture Create(int returns)
    {
        var a = new M68kAssembler();
        a.Mark("entry");
        a.EmitWord(0x48e7); a.EmitWord(0x3f3e); // D2-D7/A2-A6.
        a.EmitWord(0x4fef); a.EmitWord(unchecked((ushort)-20));
        a.EmitWord(0x243c); a.EmitLong(0x12345678);
        a.EmitWord(0x247c); a.EmitLong(0x55555555);
        for (var index = 0; index < returns - 1; index++)
        {
            a.EmitWord(0x0c80); a.EmitLong((uint)index);
            a.EmitBranch(M68kCondition.NotEqual, "continue" + index);
            a.Mark("error" + index);
            a.EmitWord((ushort)(0x7000 + 10 + index));
            Restore(a);
            a.Mark("continue" + index);
        }
        a.EmitWord(0x702a);
        Restore(a);
        a.Mark("entry:end");
        a.MethodLocalTerminalRanges = [("entry", "entry:end")];
        var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
            .GetField("_buffer", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(a)!;
        return new(a, buffer);
    }
    private static void Restore(M68kAssembler a)
    {
        a.EmitWord(0x4fef); a.EmitWord(20);
        a.EmitWord(0x4cdf); a.EmitWord(0x7cfc);
        a.EmitWord(0x4e75);
    }
    private sealed record Observation(uint[] Data, uint[] Addresses, ushort Flags, long Cycles);
    private static Observation Execute(Fixture fixture, M68kCpuModel model, uint value, uint residue, ushort flags)
    {
        const uint load = 0x10000, sentinel = 0x1000;
        var stack = 0x80000u + residue;
        var linked = fixture.Assembler.Link(load, new Dictionary<string, uint>());
        var bus = new TestBus(0x100000);
        linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
        bus.WriteLong(stack, sentinel); bus.WriteLong(stack + 4, 0xa55a3cc3); bus.WriteLong(stack - 68, 0x11223344);
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        cpu.Reset(load + (uint)linked.Labels["entry"], stack);
        for (var index = 1; index < 8; index++) cpu.State.D[index] = 0xdada0000u + (uint)index;
        for (var index = 0; index < 7; index++) cpu.State.A[index] = 0xa0a00000u + (uint)index;
        cpu.State.D[0] = value; cpu.State.StatusRegister = (ushort)(0x2000 | flags);
        for (var step = 0; step < 200 && cpu.State.ProgramCounter != sentinel; step++)
        {
            cpu.ExecuteInstruction(); Assert.False(cpu.State.Halted);
        }
        Assert.Equal(sentinel, cpu.State.ProgramCounter);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        Assert.Equal(0xa55a3cc3u, bus.ReadLong(stack + 4)); Assert.Equal(0x11223344u, bus.ReadLong(stack - 68));
        for (var index = 2; index < 8; index++) Assert.Equal(0xdada0000u + (uint)index, cpu.State.D[index]);
        for (var index = 2; index < 7; index++) Assert.Equal(0xa0a00000u + (uint)index, cpu.State.A[index]);
        Assert.Equal(linked.Bytes, bus.Memory.AsSpan((int)load, linked.Bytes.Length).ToArray());
        return new(cpu.State.D.ToArray(), cpu.State.A.ToArray(), (ushort)(cpu.State.StatusRegister & 31), cpu.State.Cycles);
    }
    private static string Snapshot(M68kAssemblyBuffer b) => JsonSerializer.Serialize(new {
        Bytes = b.Bytes.ToArray(), Labels = b.Labels.ToArray(), Anchors = b.AnalysisAnchors.ToArray(),
        Branches = b.Branches.ToArray(), Addresses = b.Addresses.ToArray(), PcRelative = b.PcRelative.ToArray(),
        Effects = b.InstructionEffectOverrides.ToArray()
    });
}
