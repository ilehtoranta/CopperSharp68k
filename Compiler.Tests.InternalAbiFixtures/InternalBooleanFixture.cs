using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class InternalBooleanFixture
{
    public static void Empty() { }

    [M68kInternalRegisterAbi, M68kExport("bool.native"), MethodImpl(MethodImplOptions.NoInlining)]
    public static bool Matches([M68kRegister(M68kRegister.A0)] APTR source,
        [M68kRegister(M68kRegister.D0)] uint expected) => APTR.ReadUInt32(source, 0) == expected;

    [M68kInternalRegisterAbi, M68kExport("bool.caller")]
    public static uint Caller([M68kRegister(M68kRegister.A0)] APTR source,
        [M68kRegister(M68kRegister.D0)] uint expected) => Matches(source, expected) ? 0x12345678u : 0x87654321u;
}
