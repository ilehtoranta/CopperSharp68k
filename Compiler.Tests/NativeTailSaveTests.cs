using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeTailSaveTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(80, false)]
    public void ConditionalTransferCanEnterWithTheCallersSave(int paddingWords, bool direct)
    {
        var a = new M68kAssembler();
        a.EmitWord(0x2F03); // save D3
        a.EmitWord(0x4A80); // test D0
        a.Mark("condition");
        a.EmitBranch(M68kCondition.NotEqual, "tail");
        a.EmitWord(0x261F);
        a.EmitWord(0x4E75);
        a.Mark("tail");
        a.EmitWord(0x261F);
        a.EmitJmp("callee", external: false, nativeTailRestore: true);
        for (int i = 0; i < paddingWords; i++) a.EmitWord(0x4E71);
        a.Mark("callee");
        a.RecordNativeEntrySave("callee", a.Offset, M68kRegister.D3);
        a.EmitWord(0x2F03);
        a.EmitWord(0x2601);
        a.EmitWord(0x2003);
        a.EmitWord(0x261F);
        a.EmitWord(0x4E75);
        a.FoldNativeTailRestores();
        var image = a.Link(0x10000, new Dictionary<string, uint>());
        int pc = image.Labels["condition"];
        Assert.Equal(0x66, image.Bytes[pc]);
        Assert.Equal(direct ? image.Labels["callee"] + 2 : image.Labels["tail"],
            pc + 2 + (sbyte)image.Bytes[pc + 1]);
    }

    [Theory]
    [InlineData(0x2601, true)]  // MOVE.L D1,D3 replaces the entire saved value.
    [InlineData(0x3601, false)] // MOVE.W D1,D3 observes the incoming high word.
    [InlineData(0x2603, false)] // MOVE.L D3,D3 observes the saved register itself.
    public void TailAndOrdinaryEntriesRestoreTheCallersRegister(int first, bool folds)
    {
        const uint load = 0x10000, stack = 0x80000, sentinel = 0x91234567, input = 0x65AB4321;
        var assembler = new M68kAssembler();
        assembler.Mark("caller");
        assembler.EmitWord(0x2F03); // Save D3, then use it as scratch.
        assembler.EmitWord(0x7607);
        assembler.EmitWord(0x261F);
        assembler.EmitJmp("callee", external: false, nativeTailRestore: true);
        assembler.Mark("callee");
        assembler.RecordNativeEntrySave("callee", assembler.Offset, M68kRegister.D3);
        assembler.EmitWord(0x2F03);
        assembler.EmitWord((ushort)first);
        assembler.EmitWord(0x2003); // Return the new D3 in D0.
        assembler.EmitWord(0x261F);
        assembler.EmitWord(0x4E75);
        int length = assembler.Offset;
        assembler.FoldNativeTailRestores();
        Assert.Equal(length - (folds ? 2 : 0), assembler.Offset);
        var code = assembler.Link(load, new Dictionary<string, uint>());
        foreach (string entry in new[] { "caller", "callee" })
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68EC020 }) {
            var bus = new TestBus();
            code.Bytes.CopyTo(bus.Memory, load);
            bus.WriteLong(stack, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(load + (uint)code.Labels[entry], stack);
            cpu.State.D[1] = input;
            cpu.State.D[3] = sentinel;
            for (int n = 0; n < 12 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(sentinel, cpu.State.D[3]);
            Assert.Equal(first switch { 0x2601 => input, 0x3601 => 0x91234321u, _ => sentinel }, cpu.State.D[0]);
        }
    }
}
