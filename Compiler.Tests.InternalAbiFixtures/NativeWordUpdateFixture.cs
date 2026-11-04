using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class NativeWordUpdateFixture
{
    public static void Empty() { }
    [M68kExport("word.inc.dynamic")]
    public static bool Increment([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] int offset) => APTR.IncrementWordIsZero(pointer, offset);
    [M68kExport("word.dec.dynamic")]
    public static bool Decrement([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] int offset) => APTR.DecrementWordIsNegative(pointer, offset);
    [M68kExport("word.inc.constant")]
    public static bool IncrementConstant([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] int offset) => APTR.IncrementWordIsZero(pointer, 6);
    [M68kExport("word.dec.constant")]
    public static bool DecrementConstant([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] int offset) => APTR.DecrementWordIsNegative(pointer, 6);
    [M68kExport("word.inc.discarded")]
    public static uint IncrementDiscarded([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] int offset)
    {
        _ = APTR.IncrementWordIsZero(pointer, offset);
        return APTR.ReadUInt16(pointer, offset);
    }
    [M68kExport("word.dec.discarded")]
    public static uint DecrementDiscarded([M68kRegister(M68kRegister.D0)] uint pointer, [M68kRegister(M68kRegister.D1)] int offset)
    {
        _ = APTR.DecrementWordIsNegative(pointer, offset);
        return APTR.ReadUInt16(pointer, offset);
    }
    [M68kExport("word.inc.address"), M68kInternalRegisterAbi]
    public static uint IncrementAddress([M68kRegister(M68kRegister.A0)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
    {
        _ = APTR.IncrementWordIsZero(pointer, offset);
        return APTR.ReadUInt16(pointer, offset);
    }
    [M68kExport("word.dec.address"), M68kInternalRegisterAbi]
    public static uint DecrementAddress([M68kRegister(M68kRegister.A0)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
    {
        _ = APTR.DecrementWordIsNegative(pointer, offset);
        return APTR.ReadUInt16(pointer, offset);
    }
}
