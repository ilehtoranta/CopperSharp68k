using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class PrivateInitializationSizeTests
{
    [Theory]
    [InlineData(false, false, false, 8)]
    [InlineData(true, false, false, 8)]
    [InlineData(false, true, false, 0)]
    [InlineData(false, false, true, 0)]
    [InlineData(false, false, false, 2)]
    public void CallsConditionalWritesAndPartialWritesAreProvenPerHome(
        bool earlyReturn, bool readBeforeWrite, bool escapeBeforeCall, int writeSize)
    {
        var function = new M68kMachineFunction("private-initialization", 0);
        function.LocalHomes.Add(0, new(0, 8, false));
        var entry = new M68kMachineBlock(0, 0); var written = new M68kMachineBlock(1, 10);
        var exit = new M68kMachineBlock(2, 20);
        function.Blocks.AddRange([entry, written, exit]);
        function.AddEdge(entry, written, M68kMachineEdgeKind.Normal);
        if (earlyReturn) function.AddEdge(entry, exit, M68kMachineEdgeKind.Normal);
        function.AddEdge(written, exit, M68kMachineEdgeKind.Normal);
        var address = function.CreateValue(CilStackValueKind.ManagedPointer, M68kMachineValueWidth.Long, M68kRegisterSet.Address).Id;
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalAddress, 0, definitions: [address], argumentIndex: 0));
        if (readBeforeWrite) entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalLoad, 1, argumentIndex: 0));
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call, 2,
            uses: escapeBeforeCall ? [address] : [], memoryEffect: M68kMachineMemoryEffect.Read | M68kMachineMemoryEffect.Write));
        written.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore, 10, argumentIndex: 0, memorySize: writeSize));
        if (!earlyReturn) exit.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalLoad, 20, argumentIndex: 0));
        var actual = M68kPrivateInitializationAnalysis.FindOverwrites(function, i => i.MemorySize);
        Assert.Equal(!readBeforeWrite && !escapeBeforeCall && writeSize == 8 ? new[] { (0, 0), (0, 4) } : [], actual.OrderBy(v => v.Offset));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void ExcludedHomesRemainInitialized(bool gc, bool eh, bool dynamic)
    {
        var function = new M68kMachineFunction("excluded", 0) { HasExceptionHandlers = eh, HasDynamicStackAllocation = dynamic };
        function.LocalHomes.Add(0, new(0, 8, gc));
        var entry = new M68kMachineBlock(0, 0); function.Blocks.Add(entry);
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore, 0, argumentIndex: 0, memorySize: 8));
        Assert.Empty(M68kPrivateInitializationAnalysis.FindOverwrites(function, i => i.MemorySize));
    }

    [Fact]
    public void LoopWithoutDefiniteWriteRetainsInitialization()
    {
        var function = new M68kMachineFunction("loop", 0); function.LocalHomes.Add(0, new(0, 8, false));
        var entry = new M68kMachineBlock(0, 0); function.Blocks.Add(entry);
        function.AddEdge(entry, entry, M68kMachineEdgeKind.Normal);
        Assert.Empty(M68kPrivateInitializationAnalysis.FindOverwrites(function, i => i.MemorySize));
    }

    [Fact]
    public void CompactClearMayCostMoreCyclesButMustCostFewerBytes()
    {
        var positions = Enumerable.Range(0, 14).Select(i => i * 4).Concat([60]).ToArray();
        var compact = M68kFrameClearRunPlanner.Create(positions, true, M68kFrameClearLoopKind.Scratch, sizeFirst: true);
        Assert.NotNull(compact); Assert.True(compact!.PlannedBytes < compact.OriginalBytes);
        Assert.True(compact.PlannedCycles > compact.OriginalCycles);
        Assert.True(M68kTargetCostModel.Accept(new(22, 100, 0), new(12, 200, 0), M68kCpuTarget.M68000, sizeFirst: true));
        Assert.False(M68kTargetCostModel.Accept(new(12, 200, 0), new(22, 100, 0), M68kCpuTarget.M68000, sizeFirst: true));
    }

    [Theory]
    [InlineData("single-contiguous")]
    [InlineData("quick-counter")]
    [InlineData("long-counter")]
    [InlineData("mixed")]
    public void CompactClearPreservesFrameContentsAndSavedRegisters(string shape)
    {
        foreach (var residue in new uint[] { 0, 2 }) {
            var before = M68kFrameClearRunsExecutionTests.Measure(shape, "free", M68kCpuTarget.M68000,
                M68kClrPolicy.Auto, M68kPeepholeOptimizationMode.FixedPoint, false, residue);
            var after = M68kFrameClearRunsExecutionTests.Measure(shape, "free", M68kCpuTarget.M68000,
                M68kClrPolicy.Auto, M68kPeepholeOptimizationMode.FixedPoint, false, residue, sizeFirst: true);
            Assert.Equal(before.FrameSHA256, after.FrameSHA256);
            Assert.Equal(before.FrameBytes, after.FrameBytes);
            Assert.True(after.CodeBytes <= before.CodeBytes);
        }
    }
}
