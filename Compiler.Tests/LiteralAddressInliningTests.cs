using Copper68k;
using CopperSharp.Targets.Amiga;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class LiteralAddressInliningTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LiteralAdapterRespectsSizePolicy(bool sizeFirst)
    {
        using var module = new CompilationModule(typeof(LiteralAddressFixture).Assembly.Location);
        var type = typeof(LiteralAddressFixture).FullName;
        var entry = module.ResolveEntryPoint(type + "::Entry");
        var helper = module.ResolveEntryPoint(type + "::Address");
        var caller = CilMachineIrBuilder.Build(entry, module);
        var target = CilMachineIrBuilder.Build(helper, module);
        var statistics = M68kMachineModuleOptimizer.Run([entry, helper],
            new Dictionary<CilMethodIdentity, M68kMachineFunction> { [entry.Identity] = caller, [helper.Identity] = target },
            module, M68kCpuTarget.M68000, new HashSet<CilMethodIdentity> { entry.Identity }, sizeFirstCosts: sizeFirst);
        Assert.True(statistics.InlinedCalls == (sizeFirst ? 0 : 2),
            $"Inlined={statistics.InlinedCalls}; helper:\n" + string.Join("\n", target.Blocks.SelectMany(b => b.Instructions)
                .Select(i => $"{i.Operation} {i.MemoryEffect} throw={i.MayThrow} safe={i.IsSafepoint} frame={i.RequiresLiveCallerFrame} origin={i.Origin?.SourceInstruction} call={i.LogicalCall?.DispatchKind}")));
        M68kMachineIrVerifier.Verify(caller);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, false)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, false)]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, true)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, true)]
    public void LiteralAdaptersInlineWithoutLosingTheirAssemblyOrPreservedRegisters(M68kCpuTarget target, M68kCpuModel model, bool crossAssembly)
    {
        var fixture = crossAssembly ? typeof(CrossAssemblyLiteralFixture) : typeof(LiteralAddressFixture);
        string[] exports = crossAssembly ? ["literal-address.cross-assembly"] : ["literal-address.inline", "literal-address.retained"];
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new()
            {
                AssemblyPath = fixture.Assembly.Location,
                ManagedAssemblyPaths = crossAssembly ? [typeof(LiteralAddressFixture).Assembly.Location] : [],
                EntryPoint = fixture.FullName + "::Empty",
                IncludedExportNames = exports,
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            });
            Assert.DoesNotContain(result.Symbols, s => s.Name.EndsWith("::Address"));
            if (!crossAssembly) Assert.Contains(result.Symbols, s => s.Name.EndsWith("::RetainedAddress"));
            var bus = new TestBus(0x100000);
            result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations)
            {
                var address = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(address, bus.ReadLong(address) + 0x10000);
            }
            foreach (var export in exports)
            {
                using var cpu = M68kCoreFactory.Default.Create(model, bus);
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + Assert.Single(result.Symbols, s => s.Name == export).Address, 0x80000);
                for (var i = 2; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
                for (var i = 2; i < 7; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
                for (var n = 0; n < 10000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(export == "literal-address.retained" ? (uint)('C' + 'X') : (uint)('A' + 'Y'), cpu.State.D[0]);
                Assert.Equal(0x80004u, cpu.State.A[7]);
                for (var i = 2; i < 8; i++) Assert.Equal(0xD0000000u + (uint)i, cpu.State.D[i]);
                for (var i = 2; i < 7; i++) Assert.Equal(0xA0000000u + (uint)i, cpu.State.A[i]);
            }
        }
    }
}
