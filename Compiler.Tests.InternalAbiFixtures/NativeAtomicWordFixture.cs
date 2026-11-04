using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class NativeAtomicWordFixture
{
    public static void Empty() { }
    [M68kInternalRegisterAbi]
    [M68kExport("Increment")]
    public static uint Increment([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.IncrementWordIsZero(pointer, 170) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("Decrement")]
    public static uint Decrement([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.DecrementWordIsNegative(pointer, 170) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("DynamicIncrement")]
    public static uint DynamicIncrement([M68kRegister(M68kRegister.A6)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
        => APTR.IncrementWordIsZero(pointer, offset) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("DynamicDecrement")]
    public static uint DynamicDecrement([M68kRegister(M68kRegister.A6)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
        => APTR.DecrementWordIsNegative(pointer, offset) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("Branch")]
    public static uint Branch([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.IncrementWordIsZero(pointer, 170) ? 17u : 29u;
}
