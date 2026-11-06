using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class NativeAtomicByteFixture
{
    [M68kInternalRegisterAbi, M68kExport("ByteBranchDefaultDecrement")]
    public static uint BranchDefaultDecrement([M68kRegister(M68kRegister.A0)] APTR pointer) => DefaultBranchBody(pointer);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultBranchBody(APTR pointer)
        => APTR.DecrementByteIsNegative(pointer, 171) ? 17u : 29u;
    [M68kInternalRegisterAbi, M68kExport("ByteDiscardDefaultDecrement")]
    public static uint DiscardDefaultDecrement([M68kRegister(M68kRegister.A0)] APTR pointer) => DefaultDiscardBody(pointer);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultDiscardBody(APTR pointer)
    { _ = APTR.DecrementByteIsNegative(pointer, 171); return 17; }
    [M68kInternalRegisterAbi, M68kExport("ByteDiscardIncrement")]
    public static uint DiscardIncrement([M68kRegister(M68kRegister.A6)] APTR pointer)
    { _ = APTR.IncrementByteIsZero(pointer, 171); return 17; }
    [M68kInternalRegisterAbi, M68kExport("ByteDiscardDecrement")]
    public static uint DiscardDecrement([M68kRegister(M68kRegister.A6)] APTR pointer)
    { _ = APTR.DecrementByteIsNegative(pointer, 171); return 17; }
    public static void Empty() { }
    [M68kInternalRegisterAbi]
    [M68kExport("ByteIncrement")]
    public static uint Increment([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.IncrementByteIsZero(pointer, 171) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("ByteDecrement")]
    public static uint Decrement([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.DecrementByteIsNegative(pointer, 171) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("ByteDynamicIncrement")]
    public static uint DynamicIncrement([M68kRegister(M68kRegister.A6)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
        => APTR.IncrementByteIsZero(pointer, offset) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("ByteDynamicDecrement")]
    public static uint DynamicDecrement([M68kRegister(M68kRegister.A6)] APTR pointer, [M68kRegister(M68kRegister.D1)] int offset)
        => APTR.DecrementByteIsNegative(pointer, offset) ? 1u : 0u;
    [M68kInternalRegisterAbi]
    [M68kExport("ByteBranch")]
    public static uint Branch([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.IncrementByteIsZero(pointer, 171) ? 17u : 29u;
    [M68kInternalRegisterAbi, M68kExport("ByteBranchDecrement")]
    public static uint BranchDecrement([M68kRegister(M68kRegister.A6)] APTR pointer) => APTR.DecrementByteIsNegative(pointer, 171) ? 17u : 29u;
    [M68kInternalRegisterAbi, M68kExport("ByteBranchInverted")]
    public static uint BranchInverted([M68kRegister(M68kRegister.A6)] APTR pointer) => !APTR.IncrementByteIsZero(pointer, 171) ? 17u : 29u;
}
