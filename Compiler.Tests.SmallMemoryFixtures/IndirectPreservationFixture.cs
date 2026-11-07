using Amiga;
using CopperSharp.Compiler;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Compiler.Tests;

public static class IndirectPreservationFixture
{
    public static void Empty() { }

    [M68kInternalRegisterAbi, M68kExport("indirect-preservation.known")]
    public static uint Known([M68kRegister(M68kRegister.A6)] APTR address)
    {
        var value = APTR.ReadUInt32(address, 0);
        PreservingCall(APTR.FromPointer(0x6000), address);
        return value + APTR.ReadUInt32(address, 4);
    }

    [M68kInternalRegisterAbi, M68kExport("indirect-preservation.unknown")]
    public static uint Unknown([M68kRegister(M68kRegister.A6)] APTR address)
    {
        var value = APTR.ReadUInt32(address, 0);
        UnknownCall(APTR.FromPointer(0x6000), address);
        return value + APTR.ReadUInt32(address, 4);
    }

    [AmigaIndirectCall(M68kRegister.A0, preservesNonvolatileInputs: true)]
    public static extern void PreservingCall([M68kRegister(M68kRegister.A0)] APTR target,
        [M68kRegister(M68kRegister.A6)] APTR address);

    [AmigaIndirectCall(M68kRegister.A0)]
    public static extern void UnknownCall([M68kRegister(M68kRegister.A0)] APTR target,
        [M68kRegister(M68kRegister.A6)] APTR address);
}
