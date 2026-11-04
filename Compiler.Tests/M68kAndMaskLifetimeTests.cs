using System.Reflection;
using Copper68k;
using CopperSharp.Compiler.Backend;

namespace CopperSharp.Compiler.Tests;

public sealed class M68kAndMaskLifetimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void NarrowAndRetainsMaskObservedBeforeOrAfter(bool observeBefore, bool observeAfter)
    {
        byte[] Build(bool optimize)
        {
            var a = new M68kAssembler();
            a.EmitWord(0x7C00); // MOVEQ #0,D6
            a.EmitWord(0x3C05); // MOVE.W D5,D6
            a.EmitWord(0x203C); a.EmitLong(0x240); // MOVE.L #$240,D0
            if (observeBefore) a.EmitWord(0x2400); // MOVE.L D0,D2
            a.EmitWord(0xCC80); // AND.L D0,D6
            if (observeAfter) {
                a.EmitWord(0xBC80); // CMP.L D0,D6
                a.EmitWord(0x57C3); // SEQ D3: observe comparison after mask folding
            }
            a.EmitWord(0x7000); // overwrite mask after its final use
            a.EmitWord(0x4E75);
            if (optimize) {
                var buffer = (M68kAssemblyBuffer)typeof(M68kAssembler)
                    .GetField("_buffer", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(a)!;
                var optimizer = new M68kPeepholeOptimizer(a, buffer, M68kCpuTarget.M68000, M68kClrPolicy.Auto, []);
                var rewrite = typeof(M68kPeepholeOptimizer).GetMethod("TryNarrowZeroExtendedRegisterAnd",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                Assert.True(rewrite.CreateDelegate<Func<M68kInstructionDataflow, bool>>(optimizer)
                    (M68kInstructionDataflow.Analyze(a)));
            }
            return a.Link(0x10000, new Dictionary<string, uint>()).Bytes;
        }
        foreach (var model in new[] { M68kCpuModel.M68000, M68kCpuModel.M68020 })
        foreach (uint value in new uint[] { 0, 0x200, 0x240, 0xFFFF })
        {
            string Run(byte[] code)
            {
                var bus = new TestBus(0x20000); code.CopyTo(bus.Memory, 0x10000);
                bus.WriteLong(0x18000, 0x1000);
                using var cpu = M68kCoreFactory.Default.Create(model, bus);
                cpu.Reset(0x10000, 0x18000); cpu.State.StatusRegister = 0x2700;
                cpu.State.D[0] = 4; cpu.State.D[5] = value;
                for (int n = 0; n < 40 && cpu.State.ProgramCounter != 0x1000; n++) cpu.ExecuteInstruction();
                Assert.Equal(0x1000u, cpu.State.ProgramCounter);
                return string.Join(",", cpu.State.D) + ":" + cpu.State.StatusRegister;
            }
            Assert.Equal(Run(Build(false)), Run(Build(true)));
        }
    }
}
