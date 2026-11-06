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
            IncludedExportNames = ["abi.direct", "abi.forward", "abi.changed", "abi.managed", "abi.external", "abi.livecall"],
            ExternalCallResolvers = [new PreservingResolver()],
            RuntimeProfile = M68kRuntimeProfile.Resident, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, CompactGuestMemory = true },
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        foreach (var (symbol, method, expected) in new[] {
            ("abi.direct", "Direct", 42u), ("abi.forward", "Forward", 42u),
            ("abi.changed", "Changed", 72u), ("abi.managed", "Entry", 72u), ("abi.external", "External", 42u), ("abi.livecall", "LiveCall", 42u) })
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
            if (symbol == "abi.livecall") {
                Assert.Equal(5u, bus.ReadLong(0x4004));
                Assert.Equal(7u, bus.ReadLong(0x4008));
            }
            Assert.Equal(symbol == "abi.external" ? 2 : 0, externalCalls);
            Assert.Equal(d.Skip(2), cpu.State.D.Skip(2));
            Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
        }
    }
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void IncomingHomesSurviveBranchingAdmissionAndPrivateTailCalls(
        M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location],
            IncludedExportNames = ["abi.admission"],
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, CompactGuestMemory = true },
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        var bus = new TestBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Memory, 0x10000);
        uint entry = result.Symbols.Single(s => s.Name == "abi.admission").Address;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        foreach (uint count in new uint[] { 0, 16, 512, 513, 0x01000002 })
        foreach (uint flags in new uint[] { 0, 1, 2, 6, 0xA55A0005 })
        foreach (ushort control in new ushort[] { 0, 0x240 })
        foreach (uint destination in new uint[] { 0x4000, 0x204000 })
        foreach (uint stack in new uint[] { 0x80000, 0x80002 }) {
            bus.WriteLong(stack, 0x1000); bus.WriteLong(0x3000, 30);
            bus.WriteWord(0x3008, control); bus.WriteLong(destination, 0xDEADBEEF);
            cpu.Reset(0x10000 + entry, stack); cpu.State.StatusRegister = 0x2700;
            for (int i = 0; i < 8; i++) cpu.State.D[i] = 0xA5550000u + (uint)i;
            for (int i = 0; i < 7; i++) cpu.State.A[i] = 0xA7770000u + (uint)i;
            cpu.State.D[0] = count; cpu.State.D[1] = flags;
            cpu.State.A[1] = destination; cpu.State.A[6] = 0x3000;
            var d = cpu.State.D.ToArray(); var a = cpu.State.A.ToArray();
            for (int n = 0; n < 2000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
            uint size = (flags & 2) != 0 ? (count >> 16) * (ushort)count : count;
            uint expected = size == 0 ? 0xDEADBEEF : size >= 512 && (size & 1) == 0 &&
                destination < 0x200000 && control == 0x240 ? unchecked(30 + size + flags) : size ^ flags;
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(expected, bus.ReadLong(destination));
            Assert.Equal(d.Skip(2), cpu.State.D.Skip(2));
            Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
        }
    }
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void SelectedPredicatesPreserveNarrowReadsHighBitsAndReusedResults(
        M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location], IncludedExportNames = ["abi.predicates"],
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, CompactGuestMemory = true, NarrowOperations = true },
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        var bus = new TestBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Memory, 0x10000);
        uint entry = result.Symbols.Single(s => s.Name == "abi.predicates").Address;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        foreach (uint value in new uint[] { 0, 0x240, 0x8000, 0xFFFF, 0xF0018000, uint.MaxValue })
        foreach (uint flags in new uint[] { 0, 2, 0x80000000, uint.MaxValue })
        foreach (byte input in new byte[] { 0, 0x80, 0xFF })
        foreach (ushort word in new ushort[] { 0, 0x240, 0xFFFF }) {
            const uint stack = 0x80002;
            bus.WriteLong(stack, 0x1000); bus.Memory[0x3000] = input; bus.WriteWord(0x3002, word);
            cpu.Reset(0x10000 + entry, stack); cpu.State.StatusRegister = 0x2700;
            for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA5550000u + (uint)n;
            for (int n = 0; n < 7; n++) cpu.State.A[n] = 0xA7770000u + (uint)n;
            cpu.State.D[0] = value; cpu.State.D[1] = flags; cpu.State.A[1] = 0x3000;
            var d = cpu.State.D.ToArray(); var a = cpu.State.A.ToArray();
            for (int n = 0; n < 2000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
            uint masked = value & 0xFFFF;
            uint expected = (input & 0x80) != 0 && (flags & 0x80000000) != 0 ? value ^ flags :
                (word & 0x240) == 0x240 ? unchecked(value + word) :
                masked == 0x8000 ? masked : masked == 0x240 ? value :
                masked < 0x8000 ? masked + 1 : unchecked((uint)(int)(short)masked);
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(expected, cpu.State.D[0]); Assert.Equal(d.Skip(2), cpu.State.D.Skip(2));
            Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
        }
    }
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void ConditionalScratchRestoresAcrossSharedReturns(
        M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location], IncludedExportNames = ["abi.conditionalreturn"],
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, CompactGuestMemory = true, NarrowOperations = true },
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        var bus = new TestBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Memory, 0x10000);
        uint entry = result.Symbols.Single(s => s.Name == "abi.conditionalreturn").Address;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        foreach (uint value in new uint[] { 0, 1, 0xFFFF, 0x80000000, uint.MaxValue })
        foreach (uint flags in new uint[] { 0, 1 })
        foreach (uint stack in new uint[] { 0x80000, 0x80002 }) {
            bus.WriteLong(stack, 0x1000); bus.WriteLong(0x3000, 0xDEADBEEF); bus.WriteLong(0x3004, 0xDEADBEEF);
            cpu.Reset(0x10000 + entry, stack); cpu.State.StatusRegister = 0x2700;
            for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA5550000u + (uint)n;
            for (int n = 0; n < 7; n++) cpu.State.A[n] = 0xA7770000u + (uint)n;
            cpu.State.D[0] = value; cpu.State.D[1] = flags; cpu.State.A[1] = 0x3000;
            var d = cpu.State.D.ToArray(); var a = cpu.State.A.ToArray();
            uint lowestStack = stack;
            for (int n = 0; n < 2000 && cpu.State.ProgramCounter != 0x1000; n++) {
                cpu.ExecuteInstruction(); lowestStack = Math.Min(lowestStack, cpu.State.A[7]);
            }
            uint product = unchecked(value * 17);
            Assert.Equal(flags == 0 ? 0xDEADBEEFu : product, bus.ReadLong(0x3000));
            Assert.Equal(flags == 0 || product == 0 ? 0xDEADBEEFu : product, bus.ReadLong(0x3004));
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(d.Skip(2), cpu.State.D.Skip(2)); Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            if (flags == 0) Assert.Equal(stack, lowestStack);
            Assert.Equal(code, bus.Memory.AsSpan(0x10000, code.Length).ToArray());
        }
    }
    [Theory]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68000, M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.FixedPoint)]
    [InlineData(M68kCpuModel.M68020, M68kPeepholeOptimizationMode.Disabled)]
    public void ConditionalScratchStaysOutsideTheLoop(
        M68kCpuModel model, M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new() {
            AssemblyPath = typeof(InternalRegisterAbiFixture).Assembly.Location,
            EntryPoint = typeof(InternalRegisterAbiFixture).FullName + "::Empty",
            ManagedAssemblyPaths = [typeof(APTR).Assembly.Location], IncludedExportNames = ["abi.conditionalloop"],
            RuntimeProfile = M68kRuntimeProfile.Freestanding, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, OutputFormat = M68kOutputFormat.Hunk,
            CodeSizeOptimizations = new() { RemoveRedundantTransport = true, CompactGuestMemory = true, NarrowOperations = true },
            PeepholeOptimization = mode, Hunk = new() { IncludeSymbols = false }
        });
        var bus = new TestBus();
        var code = ResidentCodeSizeExecutionTests.LoadImage(result.Image, 0x10000);
        code.CopyTo(bus.Memory, 0x10000);
        uint entry = result.Symbols.Single(s => s.Name == "abi.conditionalloop").Address;
        using var cpu = M68kCoreFactory.Default.Create(model, bus);
        foreach (uint value in new uint[] { 0, 1, 0xFFFF, 0x80000000, uint.MaxValue })
        foreach (uint flags in new uint[] { 0, 1 })
        foreach (uint stack in new uint[] { 0x80000, 0x80002 }) {
            bus.WriteLong(stack, 0x1000); bus.WriteLong(0x3000, (value & 1) != 0 ? 0x80000000u : 0); bus.WriteLong(0x3004, 0xDEADBEEF);
            cpu.Reset(0x10000 + entry, stack); cpu.State.StatusRegister = 0x2700;
            for (int n = 0; n < 8; n++) cpu.State.D[n] = 0xA5550000u + (uint)n;
            for (int n = 0; n < 7; n++) cpu.State.A[n] = 0xA7770000u + (uint)n;
            cpu.State.D[0] = value; cpu.State.D[1] = flags; cpu.State.A[1] = 0x3000;
            var d = cpu.State.D.ToArray(); var a = cpu.State.A.ToArray();
            uint lowestStack = stack;
            for (int n = 0; n < 2000 && cpu.State.ProgramCounter != 0x1000; n++) {
                if (n == 20) bus.Memory[0x3000] = 0; // device clears its busy byte
                cpu.ExecuteInstruction(); lowestStack = Math.Min(lowestStack, cpu.State.A[7]);
            }
            Assert.Equal(0u, bus.ReadLong(0x3000));
            Assert.Equal(flags == 0 ? 0xDEADBEEFu : unchecked(value * 17), bus.ReadLong(0x3004));
            Assert.Equal(0x1000u, cpu.State.ProgramCounter); Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.Equal(d.Skip(2), cpu.State.D.Skip(2)); Assert.Equal(a.Skip(2).Take(5), cpu.State.A.Skip(2).Take(5));
            if (flags == 0) Assert.Equal(stack, lowestStack);
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
