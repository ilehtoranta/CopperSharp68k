using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Sdk.Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class InternalRegisterAbiTests
{
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void NativeAndManagedCallsPreserveRegistersAcrossForwardingAndChangedArguments(
        M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location],
            IncludedExportNames = ["abi.direct", "abi.forward", "abi.changed", "abi.managed", "abi.external"],
            ExternalCallResolvers = [new PreservingResolver()],
            RuntimeProfile = M68kRuntimeProfile.Resident, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        foreach (var (symbol, method, expected) in new[] {
            ("abi.direct", "Direct", 42u), ("abi.forward", "Forward", 42u),
            ("abi.changed", "Changed", 72u), ("abi.managed", "Entry", 72u), ("abi.external", "External", 42u) })
        foreach (uint stack in new uint[] { 0x80000, 0x80002 })
        {
            uint entry = symbol == "" ? result.EntryPoint : result.Symbols.Single(s => s.Name == symbol).Address;
            if (symbol != "") Assert.Equal(entry, result.Symbols.Single(s => s.Name ==
                typeof(InternalRegisterAbiFixture).FullName + "::" + method).Address);
            var bus = new TestBus();
            var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
            code.CopyTo(bus.Memory, 0x10000); bus.WriteLong(stack, 0x1000);
            bus.WriteLong(0x3000, 30); bus.WriteLong(0x3004, 60);
            int externalCalls = 0;
            bus.RegisterGateway(0x3000 - 30, state => { Assert.Equal(0x3000u, state.A[6]); externalCalls++; });
            bus.RegisterGateway(0x3004 - 30, state => { Assert.Equal(0x3004u, state.A[6]); externalCalls++; });
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            cpu.Reset(0x10000 + entry, stack); cpu.State.StatusRegister = 0x2700;
            for (int i = 0; i < 8; i++) cpu.State.D[i] = 0xA5550000u + (uint)i;
            for (int i = 0; i < 7; i++) cpu.State.A[i] = 0xA7770000u + (uint)i;
            cpu.State.D[0] = 5; cpu.State.D[1] = 7; cpu.State.A[1] = 0x4000;
            cpu.State.A[6] = symbol == "abi.managed" ? 0x12345678u : 0x3000u;
            var d = cpu.State.D.ToArray(); var a = cpu.State.A.ToArray();
            for (int n = 0; n < 2000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(expected, bus.ReadLong(0x4000));
            Assert.Equal(symbol == "abi.external" ? 2 : 0, externalCalls);
            Assert.Equal(d.Skip(2), cpu.State.D.Skip(2));
            Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
        }
    }
    private sealed class PreservingResolver : IM68kExternalCallResolver
    {
        public bool TryResolve(M68kExternalMethod method, out M68kExternalCallConvention convention)
        {
            convention = null!;
            if (method.DeclaringTypeName != typeof(InternalRegisterAbiFixture).FullName || method.Name != "PreservingCall") return false;
            new AmigaExternalCallResolver().TryResolve(method, out convention);
            convention = convention with { ClobberedRegisters = [], PreservesInputRegisters = true };
            return true;
        }
    }
}
