using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using System.Reflection;

namespace CopperSharp.Compiler.Tests;

public sealed class PromotedLocalEntryArgumentTests
{
    [Fact]
    public void RejectedPromotionSeedPreservesInputDuringNativeEntry()
    {
        using var module = new CompilationModule(typeof(PromotedLocalEntryArgumentFixture).Assembly.Location);
        var method = module.ResolveEntryPoint(typeof(PromotedLocalEntryArgumentFixture).FullName + "::Capture");
        Assert.True(method.InitializeLocals);
        // Pin the rejected-promotion MIR shape: a separate volatile boundary
        // keeps the frame home authoritative and leaves its unused zero seed.
        // Metadata supplies the real scalar ABI and local type; allocation and
        // machine emission both run through the production compiler.
        var function = new M68kMachineFunction(method.DisplayName, 0, method);
        var entry = new M68kMachineBlock(0, 0);
        function.Blocks.Add(entry);
        function.LocalHomes.Add(0, new M68kFrameHome(0, 4, false));
        int Value(M68kRegister? fixedRegister = null) => function.CreateValue(
            CilStackValueKind.Int32, M68kMachineValueWidth.Long,
            fixedRegister is { } r ? M68kRegisterSet.From(r) : M68kRegisterSet.Data,
            precoloredRegister: fixedRegister).Id;
        var incoming = Value(M68kRegister.D0);
        var argument = Value();
        var loaded = Value();
        var memory = new M68kMemoryObject(M68kMemoryObjectKind.FrameSlot, "0", Offset: 0, Size: 4);
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Argument, 0,
            definitions: [incoming], argumentIndex: 0));
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Copy, 0,
            uses: [incoming], definitions: [argument]));
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalStore, 1,
            uses: [argument], argumentIndex: 0, memorySize: 4,
            memoryEffect: M68kMachineMemoryEffect.Write,
            exactMemoryAccesses: [new(memory, M68kExactMemoryAccessKind.Write, argument)]));
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Other, 2,
            memoryEffect: M68kMachineMemoryEffect.Volatile));
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.LocalLoad, 3,
            definitions: [loaded], argumentIndex: 0, memorySize: 4,
            memoryEffect: M68kMachineMemoryEffect.Read,
            exactMemoryAccesses: [new(memory, M68kExactMemoryAccessKind.Read, loaded)]));
        entry.Instructions.Add(function.CreateInstruction(M68kMachineOperation.Return, 4,
            uses: [loaded], sourceInstruction: method.Instructions.Last()));
        var promoted = M68kMemoryPromotionPass.Run(function, new M68kMemoryPromotionContext(
            method, module, new Dictionary<CilMethodIdentity, M68kMethodMemorySummary>(),
            new HashSet<M68kMemoryObject>(), new Dictionary<int, M68kHeapOwnerFacts>(), function));
        Assert.False(promoted.Changed);
        Assert.Single(entry.Instructions.Where(i => i.Operation == M68kMachineOperation.Constant));
        var generator = new M68kCodeGenerator(module, new M68kCompilationRequest
        {
            AssemblyPath = typeof(PromotedLocalEntryArgumentFixture).Assembly.Location, Cpu = M68kCpuTarget.M68000,
            RuntimeProfile = M68kRuntimeProfile.Resident,
            ExceptionMode = M68kExceptionMode.Yolo,
            MemoryManagement = M68kMemoryManagement.None,
            PeepholeOptimization = M68kPeepholeOptimizationMode.Disabled,
            IncludedExportNames = []
        }, []);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(M68kCodeGenerator).GetMethod("CompileMethod", flags)!.Invoke(generator, [method, function]);
        var assembler = (M68kAssembler)typeof(M68kCodeGenerator).GetField("_assembler", flags)!.GetValue(generator)!;
        const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000, input = 0x0026F198;
        var linked = assembler.Link(load, new Dictionary<string, uint>());
        var bus = new TestBus();
        linked.Bytes.CopyTo(bus.Memory.AsSpan((int)load));
        bus.WriteLong(stack, sentinel);
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
        cpu.Reset(load, stack);
        cpu.State.D[0] = input;
        for (var count = 0; count < 20000 && cpu.State.ProgramCounter != sentinel; count++)
        {
            cpu.ExecuteInstruction();
            Assert.False(cpu.State.Halted);
        }
        Assert.Equal(sentinel, cpu.State.ProgramCounter);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        Assert.Equal(input, cpu.State.D[0]);
    }

    [Theory]
    [InlineData(M68kPeepholeOptimizationMode.Disabled)]
    [InlineData(M68kPeepholeOptimizationMode.FixedPoint)]
    public void ExportedInputSurvivesInitializedLocalPromotion(M68kPeepholeOptimizationMode mode)
    {
        var result = AmigaM68kCompiler.Compile(new M68kCompilationRequest
        {
            AssemblyPath = typeof(PromotedLocalEntryArgumentFixture).Assembly.Location,
            EntryPoint = typeof(PromotedLocalEntryArgumentFixture).FullName + "::ImageEntry",
            Cpu = M68kCpuTarget.M68000,
            OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Resident,
            MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo,
            ClrPolicy = M68kClrPolicy.Always,
            PeepholeOptimization = mode,
            IncludedExportNames = ["test.promoted-local-input"],
            Imports = new Dictionary<string, uint> { ["test.promoted-local-observer"] = 0x2000 }
        });
        const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000, record = 0x40000;
        var bus = new TestBus();
        result.Code.CopyTo(bus.Memory.AsSpan((int)load));
        foreach (var relocation in result.Relocations)
        {
            var address = load + (uint)relocation.Offset;
            bus.WriteLong(address, bus.ReadLong(address) + load);
        }
        var symbol = Assert.Single(result.Symbols, item => item.Name == "test.promoted-local-input");
        bus.WriteLong(record, 0x4E445331);
        bus.WriteLong(record + 4, 144);
        bus.WriteLong(record + 8, 0xDEADBEEF);
        bus.WriteLong(stack, sentinel);
        bus.WriteWord(0x2000, 0x4E75); // Ordinary opaque call boundary; native RTS.
        using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
        cpu.Reset(load + symbol.Address, stack);
        cpu.State.A[0] = record;
        for (var count = 0; count < 20000 && cpu.State.ProgramCounter != sentinel; count++)
        {
            cpu.ExecuteInstruction();
            Assert.False(cpu.State.Halted);
        }
        Assert.Equal(sentinel, cpu.State.ProgramCounter);
        Assert.Equal(stack + 4, cpu.State.A[7]);
        Assert.Equal(record, cpu.State.D[0]);
        Assert.Equal(record, bus.ReadLong(record + 8));
        Assert.Equal(0x4E445331u, bus.ReadLong(record));
        Assert.Equal(144u, bus.ReadLong(record + 4));
    }
}
