using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;
using CopperSharp.Compiler.Backend;
using CopperSharp.Compiler.Metadata;

namespace CopperSharp.Compiler.Tests;

public sealed class NativeBitTestAndClearTests
{
    [Fact]
    public void DiscardedBitResultKeepsTheWriteWithoutAllocatingABoolean()
    {
        using var module = new CompilationModule(typeof(NativeBitTestAndClearFixture).Assembly.Location);
        var method = module.ResolveEntryPoint(typeof(NativeBitTestAndClearFixture).FullName + "::Discarded");
        var function = CilMachineIrBuilder.Build(method, module);
        var update = Assert.Single(function.Blocks.SelectMany(b => b.Instructions).Where(i =>
            i.Operation == M68kMachineOperation.Call && i.Origin is { SourceInstruction.Operand: int } origin &&
            module.ResolveMethodToken((int)origin.SourceInstruction.Operand!, origin.SourceMethod,
                origin.SourceInstruction.Offset).ImportName == "intrinsic:aptr-bit-test-and-clear8"));
        Assert.Empty(update.Definitions);
        Assert.NotEqual(M68kMachineMemoryEffect.None, update.MemoryEffect);
        M68kMachineIrVerifier.Verify(function);
    }

    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void ByteClearIsOneInstructionAndReportsThePreviousBit(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var method in new[] { "ClearDynamic", "ClearConstant", "ClearDiscarded", "ClearBranch", "ClearBranchInverted" })
        {
            var result = AmigaM68kCompiler.Compile(new()
            {
                AssemblyPath = typeof(NativeBitTestAndClearFixture).Assembly.Location,
                EntryPoint = typeof(NativeBitTestAndClearFixture).FullName + "::Empty",
                IncludedExportNames = [method], Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
                ExceptionMode = M68kExceptionMode.Yolo, MemoryManagement = M68kMemoryManagement.None
            });
            var bus = new TestBus(); result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations)
            {
                var at = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(at, bus.ReadLong(at) + 0x10000);
            }
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            foreach (var bit in new uint[] { 0, 1, 7, 8, 31, uint.MaxValue })
            foreach (byte before in new byte[] { 0, 1, 0x7F, 0x80, 0xFE, 0xFF })
            {
                bus.Memory[0x9000] = 0xA5; bus.Memory[0x9001] = before; bus.Memory[0x9002] = 0x5A;
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == method).Address, 0x80000);
                for (var n = 0; n < 8; n++) cpu.State.D[n] = 0xD5000000u + (uint)n;
                for (var n = 0; n < 7; n++) cpu.State.A[n] = 0xA5000000u + (uint)n;
                cpu.State.D[0] = 0x9001; cpu.State.D[1] = bit;
                var data = cpu.State.D.ToArray(); var addresses = cpu.State.A.Take(7).ToArray();
                int clears = 0;
                for (var n = 0; n < 1000 && cpu.State.ProgramCounter != 0x1000; n++)
                {
                    var op = bus.ReadWord(cpu.State.ProgramCounter);
                    var isClear = (op & 0xF1C0) == 0x0180 || (op & 0xFFC0) == 0x0880;
                    if (isClear) clears++;
                    var old = bus.Memory[0x9001]; cpu.ExecuteInstruction();
                    if (old != bus.Memory[0x9001]) Assert.True(isClear, $"Split byte update: {op:X4}");
                    if (isClear && method.StartsWith("ClearBranch", StringComparison.Ordinal))
                        Assert.Equal(0x6000, bus.ReadWord(cpu.State.ProgramCounter) & 0xF000);
                }
                var mask = 1u << (method == "ClearDynamic" ? (int)(bit & 7) : method.StartsWith("ClearBranch", StringComparison.Ordinal) ? 1 : 0);
                var after = (byte)(before & ~mask);
                var condition = (before & mask) != 0;
                if (method == "ClearBranchInverted") condition = !condition;
                var expected = method == "ClearDiscarded" ? after : method.StartsWith("ClearBranch", StringComparison.Ordinal)
                    ? condition ? 17u : 29u : condition ? 1u : 0u;
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(1, clears); Assert.Equal(after, bus.Memory[0x9001]);
                Assert.Equal(expected, cpu.State.D[0]);
                Assert.Equal(0xA5, bus.Memory[0x9000]); Assert.Equal(0x5A, bus.Memory[0x9002]);
                Assert.Equal(data.Skip(2), cpu.State.D.Skip(2));
                Assert.Equal(addresses.Skip(2), cpu.State.A.Skip(2).Take(5));
                Assert.Equal(0x80004u, cpu.State.A[7]);
            }
        }
    }
}

public static class NativeBitTestAndClearFixture
{
    public static void Empty() { }
    [M68kExport("ClearDynamic")]
    public static bool Dynamic([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => APTR.BitTestAndClear8(pointer, (int)bit);
    [M68kExport("ClearConstant")]
    public static bool Constant([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => APTR.BitTestAndClear8(pointer, 0);
    [M68kExport("ClearDiscarded")]
    public static uint Discarded([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit)
    { _ = APTR.BitTestAndClear8(pointer, 0); return APTR.ReadUInt8(pointer, 0); }
    [M68kExport("ClearBranch")]
    public static uint Branch([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => APTR.BitTestAndClear8(pointer, 1) ? 17u : 29u;
    [M68kExport("ClearBranchInverted")]
    public static uint BranchInverted([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] uint bit) => !APTR.BitTestAndClear8(pointer, 1) ? 17u : 29u;
}
