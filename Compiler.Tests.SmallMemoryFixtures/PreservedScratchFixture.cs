using Amiga;
using CopperSharp.Compiler;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class PreservedScratchFixture
{
    public static void Empty() { }

    [M68kInternalRegisterAbi, M68kPreserveScratchRegisters, M68kExport("preserve.branch")]
    public static void Branch([M68kRegister(M68kRegister.A0)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint value)
    {
        if (value == 0) APTR.WriteUInt32(address, 4, 0x13579BDF);
        else Modify(APTR.FromPointer(address.Raw + 16), value + 1, value ^ 0xA55A,
            APTR.FromPointer(address.Raw + 32));
    }

    [M68kInternalRegisterAbi, M68kPreserveScratchRegisters, M68kExport("preserve.tail")]
    public static void Tail() => ClobberNoArguments();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ClobberNoArguments() => Modify(APTR.FromPointer(0x31000),
        0x12345678, 0x87654321, APTR.FromPointer(0x31010));

    [M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    private static void Modify([M68kRegister(M68kRegister.A0)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint value, [M68kRegister(M68kRegister.D1)] uint extra,
        [M68kRegister(M68kRegister.A1)] APTR other)
    {
        APTR.WriteUInt32(address, 0, value + extra);
        APTR.WriteUInt32(other, 0, value ^ extra);
    }

    [M68kInternalRegisterAbi, M68kPreserveScratchRegisters, M68kExport("preserve.aliases")]
    public static void Aliases([M68kRegister(M68kRegister.A0)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint value)
    {
        ProtectedPrivate(address, value);
        PlainPrivate(address, value);
    }

    [M68kInternalRegisterAbi, M68kPreserveScratchRegisters, MethodImpl(MethodImplOptions.NoInlining)]
    private static void ProtectedPrivate([M68kRegister(M68kRegister.A0)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint value) => APTR.WriteUInt32(address, 0, value);

    [M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    private static void PlainPrivate([M68kRegister(M68kRegister.A0)] APTR address,
        [M68kRegister(M68kRegister.D0)] uint value) => APTR.WriteUInt32(address, 0, value);

    [M68kInternalRegisterAbi, M68kPreserveScratchRegisters]
    public static uint InvalidReturn() => 1;

    [M68kPreserveScratchRegisters]
    public static void InvalidAbi() { }
}
