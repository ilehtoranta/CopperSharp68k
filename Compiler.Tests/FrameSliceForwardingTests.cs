using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class FrameSliceForwardingTests
{
    [Fact]
    public void AdjacentFieldsForwardTheirOwnValues()
    {
        var f = new Fixture();
        f.Store(f.First, 0, 4);
        f.Store(f.Second, 4, 4);
        f.Load(0, 4);
        f.Optimize();
        Assert.Equal(M68kMachineOperation.Copy, f.Block.Instructions[^1].Operation);
        Assert.Equal([f.First.Id], f.Block.Instructions[^1].Uses);
    }

    [Fact]
    public void PartialOverwriteInvalidatesAFullWidthValue()
    {
        var f = new Fixture();
        f.Store(f.First, 0, 4);
        f.Store(f.Second, 2, 2);
        f.Load(0, 4);
        f.Optimize();
        Assert.Equal(M68kMachineOperation.LocalLoad, f.Block.Instructions[^1].Operation);
    }

    [Fact]
    public void TakingAHomeAddressEscapesEveryField()
    {
        var f = new Fixture();
        f.Store(f.First, 4, 4);
        var address = f.Function.CreateValue(CilStackValueKind.ManagedPointer,
            M68kMachineValueWidth.Long, M68kRegisterSet.Address);
        f.Block.Instructions.Add(f.Function.CreateInstruction(M68kMachineOperation.LocalAddress,
            0, definitions: [address.Id], argumentIndex: 0));
        f.Load(4, 4);
        f.Optimize();
        Assert.Equal(M68kMachineOperation.LocalLoad, f.Block.Instructions[^1].Operation);
    }

    [Fact]
    public void NarrowStoredBitsCannotBypassLoadNormalization()
    {
        var f = new Fixture();
        f.Store(f.First, 0, 2);
        f.Load(0, 2);
        f.Optimize();
        Assert.Equal(M68kMachineOperation.LocalLoad, f.Block.Instructions[^1].Operation);
    }

    [Fact]
    public void PartialReadPreservesTheEarlierFullWidthStore()
    {
        var f = new Fixture();
        f.Store(f.First, 0, 4);
        f.Load(2, 2);
        f.Store(f.Second, 0, 4);
        f.Optimize();
        Assert.Equal(2, f.Block.Instructions.Count(i => i.Operation == M68kMachineOperation.LocalStore));
    }

    private sealed class Fixture
    {
        public readonly M68kMachineFunction Function = new("frame-slices", 0);
        public readonly M68kMachineBlock Block = new(0, 0);
        public readonly M68kMachineValue First;
        public readonly M68kMachineValue Second;

        public Fixture()
        {
            Function.Blocks.Add(Block);
            Function.LocalHomes[0] = new M68kFrameHome(0, 8, false);
            First = Value(); Second = Value();
        }

        private M68kMachineValue Value() => Function.CreateValue(CilStackValueKind.Int32,
            M68kMachineValueWidth.Long, M68kRegisterSet.Data);

        public void Store(M68kMachineValue value, int offset, int size) =>
            Block.Instructions.Add(Function.CreateInstruction(M68kMachineOperation.LocalStore, 0,
                uses: [value.Id], memoryEffect: M68kMachineMemoryEffect.Write,
                argumentIndex: 0, memoryOffset: offset, memorySize: size));

        public void Load(int offset, int size) =>
            Block.Instructions.Add(Function.CreateInstruction(M68kMachineOperation.LocalLoad, 0,
                definitions: [Value().Id], memoryEffect: M68kMachineMemoryEffect.Read,
                argumentIndex: 0, memoryOffset: offset, memorySize: size));

        public void Optimize() => M68kMachineMemoryAnalysis.Optimize(Function,
            new M68kMachineOptimizer.MutableStatistics());
    }
}
