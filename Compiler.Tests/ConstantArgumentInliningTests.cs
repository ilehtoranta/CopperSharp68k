using System.Reflection;
using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class ConstantArgumentInliningTests
{
    [Theory]
    [InlineData(nameof(ConstantArgumentInliningFixture.Enabled), nameof(ConstantArgumentInliningFixture.WriteGate), true)]
    [InlineData(nameof(ConstantArgumentInliningFixture.Disabled), nameof(ConstantArgumentInliningFixture.WriteGate), true)]
    [InlineData(nameof(ConstantArgumentInliningFixture.Dynamic), nameof(ConstantArgumentInliningFixture.WriteGate), false)]
    [InlineData(nameof(ConstantArgumentInliningFixture.BranchEnabled), nameof(ConstantArgumentInliningFixture.WriteBranch), true)]
    [InlineData(nameof(ConstantArgumentInliningFixture.BranchDisabled), nameof(ConstantArgumentInliningFixture.WriteBranch), true)]
    [InlineData(nameof(ConstantArgumentInliningFixture.Infinite), nameof(ConstantArgumentInliningFixture.Loop), false)]
    [InlineData(nameof(ConstantArgumentInliningFixture.LiteralEnabled), nameof(ConstantArgumentInliningFixture.WriteLiteral), true)]
    [InlineData(nameof(ConstantArgumentInliningFixture.LiteralDisabled), nameof(ConstantArgumentInliningFixture.WriteLiteral), true)]
    public void ConstantCallerSelectsOneHelperPathWithoutChangingTheSharedBody(string name, string helperName, bool inline)
    {
        using var module = new CompilationModule(Assembly.GetExecutingAssembly().Location);
        var type = typeof(ConstantArgumentInliningFixture).FullName;
        var entry = module.ResolveEntryPoint($"{type}::{name}");
        var helper = module.ResolveEntryPoint($"{type}::{helperName}");
        var caller = CilMachineIrBuilder.Build(entry, module);
        var target = CilMachineIrBuilder.Build(helper, module);
        var statistics = M68kMachineModuleOptimizer.Run([entry, helper], new Dictionary<CilMethodIdentity, M68kMachineFunction>
        {
            [entry.Identity] = caller, [helper.Identity] = target
        }, module, M68kCpuTarget.M68000, new HashSet<CilMethodIdentity> { entry.Identity, helper.Identity });
        Assert.True(inline == (statistics.InlinedCalls > 0), $"Inlining={statistics.InlinedCalls}; caller:\n{Dump(caller)}\nhelper:\n{Dump(target)}");
        Assert.Equal(!inline, caller.Blocks.SelectMany(b => b.Instructions)
            .Any(i => i.LogicalCall?.ResolvedTargets.Contains(helper.Identity) == true));
        Assert.Contains(target.Blocks.SelectMany(b => b.Instructions), i => i.Operation == M68kMachineOperation.ConditionalBranch);
        M68kMachineIrVerifier.Verify(caller);
        M68kMachineIrVerifier.Verify(target);
        static string Dump(M68kMachineFunction function) => string.Join("\n", function.Blocks.Select(b =>
            $"B{b.Id} -> {string.Join(',', b.Successors)}; phi {string.Join(';', b.Phis.Select(p => $"{p.Definition}={string.Join(',', p.Inputs)}"))}\n" +
            string.Join("\n", b.Instructions.Select(i => $"{i.Operation} defs={string.Join(',', i.Definitions)} uses={string.Join(',', i.Uses)} arg={i.ArgumentIndex} constant={i.ConstantValue} source={i.SourceInstruction?.OpCode}"))));
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void ConstantAndDynamicCallsKeepTheSelectedWordStoreAndPreserveSurroundingMemory(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new()
            {
                AssemblyPath = Assembly.GetExecutingAssembly().Location,
                EntryPoint = typeof(ConstantArgumentInliningFixture).FullName + "::Entry",
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode
            });
            var bus = new TestBus(0x100000);
            result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations)
            {
                var address = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(address, bus.ReadLong(address) + 0x10000);
            }
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            foreach (var enabled in new uint[] { 0, 1, 0x80000000, 0xFFFFFFFF })
            {
                bus.Memory.AsSpan(0x4FFC, 24).Fill(0xA5);
                bus.WriteLong(0x4000, enabled);
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.EntryPoint, 0x80000);
                for (var n = 0; n < 10000 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(0xC000, bus.ReadWord(0x5000));
                Assert.Equal(0x4000, bus.ReadWord(0x5002));
                Assert.Equal(enabled == 0 ? 0x4000 : 0xC000, bus.ReadWord(0x5004));
                Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x4FFC));
                Assert.Equal(0xA5A5, bus.ReadWord(0x5006));
                Assert.Equal(0xC000, bus.ReadWord(0x5008));
                Assert.Equal(0xA5A5, bus.ReadWord(0x500A));
                Assert.Equal(0xA5A5, bus.ReadWord(0x500C));
                Assert.Equal(0x4000, bus.ReadWord(0x500E));
                Assert.Equal(0xC000, bus.ReadWord(0x5010));
                Assert.Equal(0xA5A5, bus.ReadWord(0x5012));
                Assert.Equal(0x80004u, cpu.State.A[7]);
            }
        }
    }
}

public static class ConstantArgumentInliningFixture
{
    public static void Enabled(APTR address) => WriteGate(address, true);
    public static void Disabled(APTR address) => WriteGate(address, false);
    public static void Dynamic(APTR address, bool enabled) => WriteGate(address, enabled);
    public static void BranchEnabled(APTR address) => WriteBranch(address, true);
    public static void BranchDisabled(APTR address) => WriteBranch(address, false);
    public static void Infinite(APTR address) => Loop(address, true);
    public static void LiteralEnabled() => WriteLiteral(true);
    public static void LiteralDisabled() => WriteLiteral(false);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteLiteral(bool enabled) =>
        APTR.WriteUInt16(APTR.FromPointer(0x5010), 0, enabled ? (ushort)0xC000 : (ushort)0x4000);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteGate(APTR address, bool enabled) =>
        APTR.WriteUInt16(address, 0, enabled ? (ushort)0xC000 : (ushort)0x4000);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void WriteBranch(APTR address, bool enabled)
    {
        if (enabled) APTR.WriteUInt16(address, 0, 0xC000);
        else APTR.WriteUInt16(address, 2, 0x4000);
    }
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Loop(APTR address, bool forever)
    {
        while (forever) APTR.WriteUInt16(address, 0, 0xC000);
    }
    public static void Entry()
    {
        Enabled(APTR.FromPointer(0x5000));
        Disabled(APTR.FromPointer(0x5002));
        Dynamic(APTR.FromPointer(0x5004), APTR.ReadUInt32(APTR.FromPointer(0x4000), 0) != 0);
        BranchEnabled(APTR.FromPointer(0x5008));
        BranchDisabled(APTR.FromPointer(0x500C));
        LiteralEnabled();
    }
}
