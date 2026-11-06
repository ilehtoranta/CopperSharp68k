using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class NativeAtomicOrFixture
{
    public static void Empty() { }

    [M68kInternalRegisterAbi, M68kExport("or.fixed")]
    public static uint Fixed([M68kRegister(M68kRegister.A1)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint bits)
    { APTR.OrUInt32(address, 26, bits); return address.Raw; }

    [M68kInternalRegisterAbi, M68kExport("or.dynamic")]
    public static uint Dynamic([M68kRegister(M68kRegister.A1)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint bits, [M68kRegister(M68kRegister.D1)] int offset)
    { APTR.OrUInt32(address, offset, bits); return address.Raw; }

    [M68kInternalRegisterAbi, M68kExport("or.default")]
    public static uint Default([M68kRegister(M68kRegister.A1)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint bits, [M68kRegister(M68kRegister.D1)] int offset)
        => DefaultBody(address, offset, bits);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultBody(APTR address, int offset, uint bits)
    { APTR.OrUInt32(address, offset, bits); return address.Raw; }
}
