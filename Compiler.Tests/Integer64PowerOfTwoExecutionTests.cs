using Copper68k;
using CopperSharp.Runtime;

namespace CopperSharp.Compiler.Tests;

public sealed partial class CompilerExecutionTests
{
    [Theory]
    [MemberData(nameof(CpuTargets))]
    public void PowerOfTwoRuntimeDivisionExecutesWithFullWidthValues(M68kCpuTarget target, M68kCpuModel model)
    {
        var result = Compile(target, M68kOutputFormat.Hunk,
            "CopperSharp.Compiler.Tests.Integer64PowerOfTwoFixture::Run");
        Assert.Equal(Integer64PowerOfTwoFixture.Run(), ExecuteHunk(result, model));
    }
}

public static class Integer64PowerOfTwoFixture
{
    public static uint Run()
    {
        uint hash = 17;
        ulong value = 0xFEDCBA9876543210;
        for (var bit = 0; bit < 64; bit++)
        {
            var divisor = 1UL << bit;
            var quotient = Integer64Arithmetic.DivideUnsigned(value, divisor);
            var remainder = Integer64Arithmetic.RemainderUnsigned(value, divisor);
            hash = unchecked(hash * 31 + (uint)quotient + (uint)(quotient >> 32));
            hash = unchecked(hash * 31 + (uint)remainder + (uint)(remainder >> 32));
            var signed = Integer64Arithmetic.DivideSigned(unchecked((long)value), unchecked((long)divisor));
            hash ^= (uint)signed ^ (uint)(signed >> 32);
            value = (value << 1) ^ (value >> 63) ^ 0xA521;
        }
        // Exercise the general restoring-division fallthrough in the same HUNK.
        return hash ^ (uint)Integer64Arithmetic.DivideUnsigned(0x1FFFFFFFF, 513)
            ^ (uint)Integer64Arithmetic.RemainderUnsigned(0x1FFFFFFFF, 513);
    }
}
