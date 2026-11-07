using Amiga;
using Copper68k;
using CopperSharp.Compiler.Metadata;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class PreservedScratchRegistersTests
{
    [Theory]
    [InlineData(M68kCpuTarget.M68000, M68kCpuModel.M68000)]
    [InlineData(M68kCpuTarget.M68020, M68kCpuModel.M68020)]
    public void BranchesNestedCallsAndTailCandidatesPreserveEveryIntegerRegister(M68kCpuTarget target, M68kCpuModel model)
    {
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        {
            var result = AmigaM68kCompiler.Compile(new()
            {
                AssemblyPath = typeof(PreservedScratchFixture).Assembly.Location,
                EntryPoint = typeof(PreservedScratchFixture).FullName + "::Empty",
                IncludedExportNames = ["preserve.branch", "preserve.tail", "preserve.aliases"],
                MemoryManagement = M68kMemoryManagement.None, ExceptionMode = M68kExceptionMode.Yolo,
                Cpu = target, RuntimeProfile = M68kRuntimeProfile.Freestanding,
                OutputFormat = M68kOutputFormat.Hunk, PeepholeOptimization = mode,
            });
            var protectedBody = result.Symbols.Single(s => s.Name.EndsWith("::ProtectedPrivate", StringComparison.Ordinal));
            var plainBody = result.Symbols.Single(s => s.Name.EndsWith("::PlainPrivate", StringComparison.Ordinal));
            Assert.NotEqual(protectedBody.Address, plainBody.Address);
            var bus = new TestBus(0x100000);
            result.Code.CopyTo(bus.Memory.AsSpan(0x10000));
            foreach (var relocation in result.Relocations)
            {
                var address = 0x10000u + (uint)relocation.Offset;
                bus.WriteLong(address, bus.ReadLong(address) + 0x10000);
            }
            using var cpu = M68kCoreFactory.Default.Create(model, bus);
            foreach (var name in new[] { "preserve.branch", "preserve.tail", "preserve.aliases" })
            foreach (var value in new uint[] { 0, 1, 0x80000000, uint.MaxValue })
            {
                bus.Memory.AsSpan(0x30000, 64).Fill(0xA5);
                bus.WriteLong(0x80000, 0x1000);
                cpu.Reset(0x10000 + result.Symbols.Single(s => s.Name == name).Address, 0x80000);
                for (var i = 0; i < 8; i++) cpu.State.D[i] = 0xD0000000u + (uint)i;
                for (var i = 0; i < 7; i++) cpu.State.A[i] = 0xA0000000u + (uint)i;
                cpu.State.D[0] = value; cpu.State.A[0] = 0x30000;
                var data = cpu.State.D.ToArray(); var addresses = cpu.State.A.Take(7).ToArray();
                for (var i = 0; i < 10000 && cpu.State.ProgramCounter != 0x1000; i++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                Assert.Equal(data, cpu.State.D); Assert.Equal(addresses, cpu.State.A.Take(7));
                Assert.Equal(0x80004u, cpu.State.A[7]);
                if (name == "preserve.branch")
                {
                    Assert.Equal(value == 0 ? 0x13579BDFu : 0xA5A5A5A5u, bus.ReadLong(0x30004));
                    if (value != 0) Assert.Equal(unchecked(value + 1 + (value ^ 0xA55A)), bus.ReadLong(0x30010));
                }
                else if (name == "preserve.tail") Assert.Equal(0x99999999u, bus.ReadLong(0x31000));
                else Assert.Equal(value, bus.ReadLong(0x30000));
                Assert.Equal(0xA5A5A5A5u, bus.ReadLong(0x3003C));
            }
        }
    }

    [Theory]
    [InlineData(nameof(PreservedScratchFixture.InvalidReturn))]
    [InlineData(nameof(PreservedScratchFixture.InvalidAbi))]
    public void IncompatibleDeclarationsAreRejected(string method)
    {
        using var module = new CompilationModule(typeof(PreservedScratchFixture).Assembly.Location);
        Assert.Throws<M68kCompilationException>(() => module.ResolveEntryPoint(typeof(PreservedScratchFixture).FullName + "::" + method));
    }
}
