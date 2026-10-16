using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class LoopPhiAddressCoalescingTests
{
    [Theory]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void FlexiblePointersAcrossCallsDoNotAllBecomeAddressOnly(int count)
    {
        var legacy = Allocate(count, simplifyLoopPhis: false, addressOnly: false);
        var candidate = Allocate(count, simplifyLoopPhis: true, addressOnly: false);
        Assert.NotEmpty(legacy.Allocation.SpilledValues);
        Assert.Empty(candidate.Allocation.SpilledValues);
        Assert.Contains(candidate.Sources, value => candidate.Allocation.Registers[value].Register <= M68kRegister.D7);
        Assert.All(candidate.Constraints, value => Assert.InRange(candidate.Allocation.Registers[value].Register, M68kRegister.A0, M68kRegister.A6));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SafeLowPressureAddressCopiesStillCoalesce(int count)
    {
        var result = Allocate(count, simplifyLoopPhis: true, addressOnly: false);
        Assert.Empty(result.Allocation.SpilledValues);
        for (var index = 0; index < count; index++)
            Assert.Equal(result.Allocation.Registers[result.Sources[index]], result.Allocation.Registers[result.Constraints[index]]);
    }

    [Fact]
    public void GenuineAddressOnlyPressureStillSpills()
    {
        var result = Allocate(4, simplifyLoopPhis: true, addressOnly: true);
        Assert.NotEmpty(result.Allocation.SpilledValues);
        Assert.All(result.Allocation.Registers.Values, location => Assert.InRange(location.Register, M68kRegister.A0, M68kRegister.A6));
    }

    private static (M68kAllocationResult Allocation, int[] Sources, int[] Constraints) Allocate(int count, bool simplifyLoopPhis, bool addressOnly)
    {
        var function = new M68kMachineFunction("loop-invariant-address-pressure", 0) {
            SimplifyLoopPhis = simplifyLoopPhis,
            ReservedRegisters = M68kRegisterSet.From(M68kRegister.A5)
        };
        var block = new M68kMachineBlock(0, 0);
        function.Blocks.Add(block);
        var sources = Enumerable.Range(0, count).Select(_ => function.CreateValue(CilStackValueKind.ManagedPointer,
            M68kMachineValueWidth.Long, addressOnly ? M68kRegisterSet.Address : M68kRegisterSet.DataOrAddress).Id).ToArray();
        var constraints = Enumerable.Range(0, count).Select(_ => function.CreateValue(CilStackValueKind.ManagedPointer,
            M68kMachineValueWidth.Long, M68kRegisterSet.Address).Id).ToArray();
        foreach (var source in sources)
            block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 0, definitions: [source]));
        // A5 is resident invocation state. This call leaves only A2-A4 as
        // possible address-register homes, while D2-D7 remain available.
        block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Call, 1,
            clobbers: M68kRegisterSet.From(M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1, M68kRegister.A6)));
        for (var index = 0; index < count; index++)
        {
            block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy, index+2,
                uses: [sources[index]], definitions: [constraints[index]]));
            block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, index+2, uses: [constraints[index]]));
        }
        block.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, count+2, uses: sources));
        M68kMachineCostAnalysis.Apply(function);
        var live = M68kLivenessAnalysis.Analyze(function);
        var graph = M68kInterferenceBuilder.Build(function, live);
        var allocation = M68kGraphColoringAllocator.Allocate(function, graph);
        M68kGraphColoringAllocator.VerifyAllocation(function, graph, allocation, M68kLivenessAnalysis.AnalyzeInstructions(function, live));
        Assert.All(allocation.Registers.Values, location => Assert.NotEqual(M68kRegister.A5, location.Register));
        foreach (var source in sources)
            if (allocation.Registers.TryGetValue(source, out var location))
                Assert.DoesNotContain(location.Register, new[] { M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1, M68kRegister.A6 });
        return (allocation, sources, constraints);
    }
}
