using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class PromotedLocalEntryArgumentFixture
{
    public static uint ImageEntry() => 0;

    [M68kExport("test.promoted-local-input")]
    [return: M68kRegister(M68kRegister.D0)]
    public static uint Capture([M68kRegister(M68kRegister.A0)] uint address)
    {
        var result = APTR.FromPointer(address);
        if (!Valid(result)) return 0;
        Observe(result.Raw);
        APTR.WriteUInt32(result, 8, result.Raw);
        return result.Raw;
    }

    private static bool Valid(APTR result) => result.IsNotNull &&
        APTR.ReadUInt32(result, 0) == 0x4E445331 && APTR.ReadUInt32(result, 4) == 144;

    [M68kImport("test.promoted-local-observer")]
    private static extern void Observe([M68kRegister(M68kRegister.A0)] uint address);
}
