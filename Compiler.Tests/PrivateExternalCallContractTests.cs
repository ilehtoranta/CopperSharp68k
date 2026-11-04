using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Sdk.Amiga;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class PrivateExternalCallContractTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void VoidPrivateCallSeparatesSetupFromPreservedInputs(bool preservesInputs)
    {
        using var module = new CompilationModule(typeof(PrivateExternalCallFixture).Assembly.Location, [new Resolver(preservesInputs)]);
        var entry = module.ResolveEntryPoint(typeof(PrivateExternalCallFixture).FullName + "::Entry");
        var function = CilMachineIrBuilder.Build(entry, module);
        var call = Assert.Single(function.Blocks.SelectMany(b => b.Instructions)
            .Where(i => i.Operation == M68kMachineOperation.Call && i.PlatformBaseConvention?.Identity == "fixture.library"));
        // Default contracts remain conservative. A known preserving callee
        // has no call clobber; its explicit input copies still define A6.
        Assert.Equal(preservesInputs ? M68kRegisterSet.None : M68kRegisterSet.From(M68kRegister.A6), call.Clobbers);
    }

    [Fact]
    public void AmigaWrapperRejectsAmbiguousExplicitBindings()
    {
        var error = Assert.Throws<M68kCompilationException>(() => AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(PrivateExternalCallFixture).Assembly.Location,
            EntryPoint = typeof(PrivateExternalCallFixture).FullName + "::Entry", IncludedExportNames = [],
            RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
            ExternalCallResolvers = [new Resolver(), new Resolver()]
        }));
        Assert.Contains("Multiple explicit external-call resolvers", error.Message);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, false)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, false)]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000, true)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020, true)]
    public void AmigaWrapperHonorsPrivateContractAndRetainsDefaultPublicClobbers(
        M68kCpuTarget target, M68kCpuModel model, bool privateCall)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint }) {
            var result = AmigaM68kCompiler.Compile(new() {
                AssemblyPath = typeof(PrivateExternalCallFixture).Assembly.Location,
                EntryPoint = typeof(PrivateExternalCallFixture).FullName + "::Entry", IncludedExportNames = [],
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding, OutputFormat = M68kOutputFormat.Hunk,
                ExternalCallResolvers = privateCall ? [new Resolver(true)] : [], PeepholeOptimization = mode
            });
            var bus = new TestBus(); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var reloc in result.Relocations) {
                uint at = 0x10000u + (uint)reloc.Offset; bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            bus.WriteLong(0x80000, 0x1000); bus.WriteLong(0x8000, 0x9000);
            bus.WriteLong(0x9000, 0x12345678); bus.WriteLong(0x9004, 0x3210);
            int calls = 0;
            bus.RegisterGateway(0x6000 - 30, cpu => {
                calls++; Assert.Equal(0x6000u, cpu.A[6]);
                if (!privateCall) { cpu.D[0] = 0xFFFFFFFF; cpu.D[1] = 0xEEEEEEEE; cpu.A[0] = 0xDDDDDDDD; cpu.A[1] = 0xCCCCCCCC; }
            });
            using var core = M68kCoreFactory.Default.Create(model, bus);
            core.Reset(0x10000 + result.EntryPoint, 0x80000);
            for (int n = 0; n < 2000 && core.State.ProgramCounter != 0x1000; n++) core.ExecuteInstruction();
            Assert.Equal(0x1000u, core.State.ProgramCounter); Assert.Equal(1, calls);
            Assert.Equal(0x12348888u, core.State.D[0]); Assert.Equal(0x80004u, core.State.A[7]);
        }
    }

    private sealed class Resolver(bool preservesInputs = false) : IM68kExternalCallResolver
    {
        public bool TryResolve(M68kExternalMethod method, out M68kExternalCallConvention convention)
        {
            convention = null!;
            if (method.DeclaringTypeName != typeof(PrivateExternalCallFixture).FullName || method.Name != "Call") return false;
            new AmigaExternalCallResolver().TryResolve(method, out convention);
            convention = convention with { ClobberedRegisters = [], PreservesInputRegisters = preservesInputs }; return true;
        }
    }
}

public static class PrivateExternalCallFixture
{
    public static uint Entry()
    {
        uint pointer = APTR.ReadUInt32(0x8000u, 0);
        uint value = APTR.ReadUInt32(pointer, 0);
        Call(APTR.FromPointer(0x6000));
        return value + APTR.ReadUInt32(pointer, 4);
    }
    [AmigaLibrary("fixture.library", AmigaLibraryBasePolicy.CallerProvided)]
    [AmigaLvo(-30)]
    public static extern void Call([M68kRegister(M68kRegister.A6)] APTR libraryBase);
}
