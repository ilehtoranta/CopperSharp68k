using System.Runtime.CompilerServices;
using Amiga;
using Copper68k;
using CopperSharp.Targets.Amiga;

namespace CopperSharp.Compiler.Tests;

public sealed class RuntimeArithmeticTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var name in new[] { "Product", "Quotient", "Remainder", "Pair", "SignedQuotient", "SignedRemainder", "SignedPair" })
        foreach (var mode in new[] { M68kPeepholeOptimizationMode.Disabled, M68kPeepholeOptimizationMode.FixedPoint })
        foreach (var shared in new[] { false, true })
            yield return [name, mode, shared];
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void RuntimeArithmeticMatchesIndependentHostOracle(string name, M68kPeepholeOptimizationMode mode, bool shared)
    {
        var compilation = AmigaM68kCompiler.Compile(new M68kCompilationRequest {
            AssemblyPath = typeof(RuntimeArithmeticFixture).Assembly.Location,
            EntryPoint = typeof(RuntimeArithmeticFixture).FullName + "::" + name + "Entry",
            Cpu = M68kCpuTarget.M68000, OutputFormat = M68kOutputFormat.Hunk,
            RuntimeProfile = M68kRuntimeProfile.Resident, MemoryManagement = M68kMemoryManagement.None,
            ExceptionMode = M68kExceptionMode.Yolo, PeepholeOptimization = mode, IncludedExportNames = [],
            CodeSizeOptimizations = shared ? new() { ShareArithmeticCores = true } : null
        });
        if (shared && name != "Product")
            Assert.Contains("pass=ShareArithmeticCores", compilation.Map);
        foreach (var (left, right) in Operands())
        {
            var signed = name.StartsWith("Signed", StringComparison.Ordinal);
            if (right == 0 && name != "Product") continue;
            if (signed && left == 0x80000000 && right == uint.MaxValue) continue;
            var expected = name == "Product" ? unchecked((uint)((ulong)left * right)) :
                name.Contains("Remainder") ? signed ? unchecked((uint)((int)left % (int)right)) : left % right :
                signed ? unchecked((uint)((int)left / (int)right)) : left / right;
            var expectedRemainder = name.EndsWith("Pair", StringComparison.Ordinal)
                ? signed ? unchecked((uint)((int)left % (int)right)) : left % right : 0xa55a1234u;
            var bus = new TestBus(0x100000);
            const uint load = 0x10000, stack = 0x80000, sentinel = 0x1000;
            compilation.Code.CopyTo(bus.Memory.AsSpan((int)load));
            foreach (var relocation in compilation.Relocations) {
                var address = load + (uint)relocation.Offset;
                bus.WriteLong(address, bus.ReadLong(address) + load);
            }
            bus.WriteLong(stack, sentinel);
            bus.WriteLong(0x4000, left);
            bus.WriteLong(0x4004, right);
            bus.WriteLong(0x4008, 0xa55a1234);
            bus.WriteLong(0x400c, 0xdeadbeef);
            using var cpu = M68kCoreFactory.Default.Create(M68kCpuModel.M68000, bus);
            cpu.Reset(load + compilation.EntryPoint, stack);
            for (var register = 0; register < 8; register++) cpu.State.D[register] = 0xa5a50000u + (uint)register;
            for (var step = 0; step < 100000 && cpu.State.ProgramCounter != sentinel; step++) {
                cpu.ExecuteInstruction();
                Assert.False(cpu.State.Halted || cpu.State.Stopped);
            }
            Assert.Equal(sentinel, cpu.State.ProgramCounter);
            Assert.Equal(stack + 4, cpu.State.A[7]);
            Assert.True(expected == cpu.State.D[0], $"{name}({left:X8}, {right:X8}) expected {expected:X8}, got {cpu.State.D[0]:X8}");
            Assert.Equal(expectedRemainder, bus.ReadLong(0x4008));
            Assert.Equal(left, bus.ReadLong(0x4000));
            Assert.Equal(right, bus.ReadLong(0x4004));
            Assert.Equal(0xdeadbeefu, bus.ReadLong(0x400c));
        }
    }

    private static IEnumerable<(uint, uint)> Operands()
    {
        uint[] boundaries = [0, 1, 2, 3, 255, 256, 32767, 32768, 65534, 65535, 65536, 65537, 0x7fffffff, 0x80000000, 0xffff0000, uint.MaxValue];
        foreach (var left in boundaries)
        foreach (var right in boundaries) yield return (left, right);
        // The guarded DIVU path must reject a quotient of 65536, including
        // when the dividend's low word is zero. Adjacent values use hardware.
        foreach (var divisor in new uint[] { 1, 2, 3, 50, 4000, 32768, 65535 }) {
            var boundary = divisor << 16;
            yield return (boundary - 1, divisor);
            yield return (boundary, divisor);
            yield return (boundary + 1, divisor);
        }
        var state = 0x6800c0deu;
        for (var n = 0; n < 256; n++) {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            var left = state;
            state ^= state << 13; state ^= state >> 17; state ^= state << 5;
            yield return (left, state);
            yield return (left, state & 65535);
        }
    }
}

public static class RuntimeArithmeticFixture
{
    private static uint Left() => APTR.ReadUInt32(APTR.FromPointer(0x4000), 0);
    private static uint Right() => APTR.ReadUInt32(APTR.FromPointer(0x4004), 0);
    public static uint ProductEntry() => Product(Left(), Right());
    public static uint QuotientEntry() => Quotient(Left(), Right());
    public static uint RemainderEntry() => Remainder(Left(), Right());
    public static uint PairEntry() => Pair(Left(), Right());
    public static uint SignedQuotientEntry() => SignedQuotient(Left(), Right());
    public static uint SignedRemainderEntry() => SignedRemainder(Left(), Right());
    public static uint SignedPairEntry() => SignedPair(Left(), Right());
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Product(uint left, uint right) => unchecked(left * right);
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Quotient(uint left, uint right) => left / right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Remainder(uint left, uint right) => left % right;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint SignedQuotient(uint left, uint right) => unchecked((uint)((int)left / (int)right));
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint SignedRemainder(uint left, uint right) => unchecked((uint)((int)left % (int)right));
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Pair(uint left, uint right) {
        var quotient = left / right;
        var remainder = left % right;
        APTR.WriteUInt32(APTR.FromPointer(0x4008), 0, remainder);
        return quotient;
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static uint SignedPair(uint left, uint right) {
        var quotient = (int)left / (int)right;
        var remainder = (int)left % (int)right;
        APTR.WriteUInt32(APTR.FromPointer(0x4008), 0, unchecked((uint)remainder));
        return unchecked((uint)quotient);
    }
}
