using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class IndirectPreservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptInPreservesNonvolatileInputsButKeepsEveryScratchClobber(bool known)
    {
        using var module = new CompilationModule(typeof(IndirectPreservationFixture).Assembly.Location,
            [new AmigaExternalCallResolver()]);
        var entry = module.ResolveEntryPoint(typeof(IndirectPreservationFixture).FullName + (known ? "::Known" : "::Unknown"));
        var function = CilMachineIrBuilder.Build(entry, module);
        var call = Assert.Single(function.Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.PlatformBaseConvention?.Identity.StartsWith("amiga-indirect:") == true));
        Assert.Equal(known ? M68kRegisterSet.From(M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1)
            : M68kRegisterSet.From(M68kRegister.D0, M68kRegister.D1, M68kRegister.A0, M68kRegister.A1, M68kRegister.A6), call.Clobbers);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void KnownAndUnknownCallsKeepLiveInputsAcrossRealScratchClobbers(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new()
            {
                AssemblyPath = typeof(IndirectPreservationFixture).Assembly.Location,
                EntryPoint = typeof(IndirectPreservationFixture).FullName + "::Empty",
                IncludedExportNames = ["indirect-preservation.known", "indirect-preservation.unknown"],
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
            });
            var knownBody = Assert.Single(result.Symbols, s => s.Name.EndsWith("::Known"));
            var unknownBody = Assert.Single(result.Symbols, s => s.Name.EndsWith("::Unknown"));
            Assert.NotEqual(knownBody.Address, unknownBody.Address);
            Assert.True(knownBody.Size < unknownBody.Size);
            var bus = new TestBus(0x100000);
            result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations)
            {
                var at = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            var calls = 0;
            var preservesInputs = false;
            bus.RegisterGateway(0x6000, state =>
            {
                calls++; Assert.Equal(0x9000u, state.A[6]);
                state.D[0] = 0xDEAD0000; state.D[1] = 0xDEAD0001;
                state.A[0] = 0xBAD00000; state.A[1] = 0xBAD00001;
                if (!preservesInputs) state.A[6] = 0xBAD00006;
            });
            foreach (var known in new[] { false, true })
            {
                bus.WriteLong(0x80000, 0x1000); bus.WriteLong(0x9000, 0x12345678); bus.WriteLong(0x9004, 0x3210);
                calls = 0; preservesInputs = known;
                using var cpu = M68kCoreFactory.Default.Create(model, bus);
                cpu.Reset(0x10000 + (known ? knownBody.Address : unknownBody.Address), 0x80000);
                for (var n = 2; n < 8; n++) cpu.State.D[n] = 0xD0000000u + (uint)n;
                for (var n = 2; n < 6; n++) cpu.State.A[n] = 0xA0000000u + (uint)n;
                cpu.State.A[6] = 0x9000;
                for (var n = 0; n < 10000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(1, calls);
                Assert.Equal(0x12348888u, cpu.State.D[0]); Assert.Equal(0x9000u, cpu.State.A[6]);
                Assert.Equal(0x80004u, cpu.State.A[7]);
                for (var n = 2; n < 8; n++) Assert.Equal(0xD0000000u + (uint)n, cpu.State.D[n]);
                for (var n = 2; n < 6; n++) Assert.Equal(0xA0000000u + (uint)n, cpu.State.A[n]);
            }
        }
    }
}
