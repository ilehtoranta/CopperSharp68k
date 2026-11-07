using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class SmallMemoryHelperInliningTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void NarrowValuesStoredAsLongwordsRetainZeroExtensionAndLegalRegisterBanks(M68kCpuTarget target, M68kCpuModel model)
    {
        var result = AmigaM68kCompiler.Compile(new()
        {
            AssemblyPath = typeof(SmallMemoryHelperFixture).Assembly.Location,
            EntryPoint = typeof(SmallMemoryHelperFixture).FullName + "::NativeNarrowLongStore",
            IncludedExportNames = [], Cpu = target, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo
        });
        Assert.Empty(result.FrameworkAnalysis.ManagedAllocationSites);
        var bus = new TestBus(0x100000);
        result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
        foreach (var relocation in result.Relocations)
        {
            var address = 0x10000u + (uint)relocation.Offset;
            bus.WriteLong(address, bus.ReadLong(address) + 0x10000);
        }
        foreach (var value in new byte[] { 0, 0x80, 0xFF })
        {
            bus.Memory.AsSpan(0x4FFC, 16).Fill(0xA5);
            bus.Memory[0x5010] = value; bus.WriteWord(0x5012, (ushort)(0x8000 | value));
            bus.WriteLong(0x80000, 0x1000);
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x10000 + result.EntryPoint, 0x80000);
            for (var i = 0; i < 10000 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter);
            Assert.Equal((uint)value, bus.ReadLong(0x5000));
            Assert.Equal((uint)(0x8000 | value), bus.ReadLong(0x5004));
            Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x4FFC));
            Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x5008));
        }
    }
    [Fact]
    public void InlinedLongStoresKeepAddressRegistersAvailableAfterDisplacementFolding()
    {
        using var module = new CompilationModule(typeof(SmallMemoryHelperFixture).Assembly.Location);
        var name = typeof(SmallMemoryHelperFixture).FullName;
        var entry = module.ResolveEntryPoint(name + "::LongStorePair");
        var helper = module.ResolveEntryPoint(name + "::StoreLong");
        var caller = CilMachineIrBuilder.Build(entry, module);
        var target = CilMachineIrBuilder.Build(helper, module);
        M68kMachineModuleOptimizer.Run([entry, helper],
            new Dictionary<CilMethodIdentity, M68kMachineFunction>
            { [entry.Identity] = caller, [helper.Identity] = target },
            module, M68kCpuTarget.M68000, new HashSet<CilMethodIdentity> { entry.Identity });
        var stores = caller.Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.Operation == M68kMachineOperation.Call && i.Origin is { } origin &&
                module.ResolveMethodToken((int)origin.SourceInstruction.Operand!, origin.SourceMethod,
                    origin.SourceInstruction.Offset).ImportName == "intrinsic:aptr-write-uint32").ToArray();
        Assert.Equal(2, stores.Length);
        foreach (var store in stores)
        {
            Assert.NotNull(store.Immediate);
            Assert.Equal(M68kRegisterSet.DataOrAddress, caller.Values[store.Uses[^1]].AllowedRegisters);
        }
        M68kMachineIrVerifier.Verify(caller);
    }
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void RepeatedSmallPrivateMemoryHelperRespectsSizeAndNoInliningPolicies(bool sizeFirst, bool noInlining, bool inline)
    {
        using var module = new CompilationModule(typeof(SmallMemoryHelperFixture).Assembly.Location);
        var type = typeof(SmallMemoryHelperFixture).FullName;
        var entry = module.ResolveEntryPoint($"{type}::" + (noInlining ? "NoInlineEntry" : "Entry"));
        var helper = module.ResolveEntryPoint($"{type}::" + (noInlining ? "NoInlineDecrement" : "Decrement"));
        var caller = CilMachineIrBuilder.Build(entry, module);
        var target = CilMachineIrBuilder.Build(helper, module);
        var statistics = M68kMachineModuleOptimizer.Run([entry, helper],
            new Dictionary<CilMethodIdentity, M68kMachineFunction>
            {
                [entry.Identity] = caller, [helper.Identity] = target,
            }, module, M68kCpuTarget.M68000, new HashSet<CilMethodIdentity> { entry.Identity }, sizeFirstCosts: sizeFirst);
        Assert.True(statistics.InlinedCalls == (inline ? 2 : 0),
            $"Inlined={statistics.InlinedCalls}; helper:\n" + string.Join("\n", target.Blocks.SelectMany(b => b.Instructions)
                .Select(i => $"{i.Operation} {i.MemoryEffect} uses={string.Join(',', i.Uses)}")));
        Assert.Equal(!inline, caller.Blocks.SelectMany(b => b.Instructions)
            .Any(i => i.LogicalCall?.ResolvedTargets.Contains(helper.Identity) == true));
        if (inline)
        {
            var accesses = caller.Blocks.SelectMany(b => b.Instructions)
                .Where(i => i.Operation == M68kMachineOperation.Call).Select(i =>
                {
                    var origin = i.Origin!;
                    return module.ResolveMethodToken((int)origin.SourceInstruction.Operand!,
                        origin.SourceMethod, origin.SourceInstruction.Offset).ImportName;
                }).ToArray();
            Assert.Equal(new[] { "intrinsic:aptr-read-uint16", "intrinsic:aptr-write-uint16",
                "intrinsic:aptr-read-uint16", "intrinsic:aptr-write-uint16" }, accesses);
        }
        M68kMachineIrVerifier.Verify(caller);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void NativeReadWriteHelpersPreserveWrappingValuesAndCallerRegisters(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var generic in new[] { false, true })
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new()
            {
                AssemblyPath = typeof(SmallMemoryHelperFixture).Assembly.Location,
                EntryPoint = typeof(SmallMemoryHelperFixture).FullName + (generic ? "::GenericNativeEntry" : "::NativeEntry"),
                IncludedExportNames = [generic ? "small-memory-helper.generic" : "small-memory-helper.direct"],
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
            });
            if (generic) Assert.DoesNotContain(result.Symbols, s => s.Name.Contains("::GenericDecrement<", StringComparison.Ordinal));
            var bus = new TestBus(0x100000);
            result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations)
            {
                var address = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(address, bus.ReadLong(address) + 0x10000);
            }
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            foreach (var value in new ushort[] { 0, 1, 0x8000, 0xFFFF })
            {
                bus.Memory.AsSpan(0x4FFC, 12).Fill(0xA5);
                bus.WriteWord(0x5000, value);
                bus.WriteWord(0x5002, (ushort)(value ^ 0xAAAA));
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.EntryPoint, 0x80000);
                for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
                for (var i = 2; i < 7; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
                for (var i = 0; i < 10000 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(unchecked((ushort)(value - 1)), bus.ReadWord(0x5000));
                Assert.Equal(unchecked((ushort)((value ^ 0xAAAA) - 1)), bus.ReadWord(0x5002));
                Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x4FFC));
                Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x5004));
                for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
                for (var i = 2; i < 7; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
                Assert.Equal(0x80004u, cpu.State.A[7]);
            }
        }
    }
}

