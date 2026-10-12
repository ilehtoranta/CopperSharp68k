using System.Buffers.Binary;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Output;

namespace CopperSharp.Compiler.Tests;

public sealed class ResidentHunkCallRangeTests
{
    [Theory]
    [InlineData(32760, false)]
    [InlineData(32762, false)]
    [InlineData(32764, false)]
    [InlineData(32768, false)]
    [InlineData(32760, true)]
    [InlineData(32762, true)]
    [InlineData(32764, true)]
    [InlineData(32768, true)]
    public void RelocatedHunkCallsRespectBothSignedWordBoundaries(int paddingBytes, bool backwards)
    {
        var assembler = new M68kAssembler();
        void Entry() {
            assembler.Mark("entry"); assembler.EmitCall("method:callee"); assembler.EmitWord(0x4e75);
        }
        void Callee() {
            assembler.Mark("method:callee"); assembler.EmitWord(0x702a); assembler.EmitWord(0x4e75);
        }
        if (backwards) Callee(); else Entry();
        for (var i = 0; i < paddingBytes; i += 2) assembler.EmitWord(0x4e71);
        if (backwards) Entry(); else Callee();
        assembler.RenderAssembly(M68kCpuTarget.M68000);
        var linked = assembler.Link(0, new Dictionary<string, uint>());
        var absolute = paddingBytes > 32762;
        Assert.Equal(absolute ? 0x4eb9 : 0x6100,
            BinaryPrimitives.ReadUInt16BigEndian(linked.Bytes.AsSpan(linked.Labels["entry"], 2)));
        Assert.Equal(absolute ? 1 : 0, linked.Relocations.Count);
        var hunk = HunkWriter.Write(linked.Bytes, linked.Relocations, [], linked.Labels,
            linked.Bytes.Length, new HashSet<string>(), new HunkOutputOptions { IncludeSymbols = false });
        foreach (var load in new uint[] { 0x10000, 0x20000 })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020, M68kCpuModel.M68040 })
        {
            var bus = new TestBus(0x100000);
            ResidentCodeSizeExecutionTests.LoadImage(hunk, load).CopyTo(bus.Memory.AsSpan((int)load));
            bus.WriteLong(0x80000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(load + (uint)linked.Labels["entry"], 0x80000);
            for (var step = 0; step < 10 && cpu.State.ProgramCounter != 0x1000; step++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal(42u, cpu.State.D[0]); Assert.Equal(0x80004u, cpu.State.A[7]);
        }
    }
}
