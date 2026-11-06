using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class NativeImmediateStoreFixture
{
    [M68kInternalRegisterAbi, M68kExport("store.byte.absolute")]
    public static void AbsoluteByte([M68kRegister(M68kRegister.D1)] bool zero) => AbsoluteByteBody(zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsoluteByteBody(bool zero)
    { if (zero) APTR.WriteUInt8(APTR.FromPointer(0x30000), -170, 0); else APTR.WriteUInt8(APTR.FromPointer(0x30000), -170, 0xEF); }
    [M68kInternalRegisterAbi, M68kExport("store.word.absolute")]
    public static void AbsoluteWord([M68kRegister(M68kRegister.D1)] bool zero) => AbsoluteWordBody(zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void AbsoluteWordBody(bool zero)
    { if (zero) APTR.WriteUInt16(APTR.FromPointer(0x30000), -170, 0); else APTR.WriteUInt16(APTR.FromPointer(0x30000), -170, 0xC000); }

    [M68kInternalRegisterAbi, M68kExport("store.byte.default")]
    public static void DefaultByte([M68kRegister(M68kRegister.A0)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero) => DefaultByteBody(pointer, offset, zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefaultByteBody(APTR pointer, int offset, bool zero)
    { if (zero) APTR.WriteUInt8(pointer, offset, 0); else APTR.WriteUInt8(pointer, offset, 0xEF); }
    [M68kInternalRegisterAbi, M68kExport("store.word.default")]
    public static void DefaultWord([M68kRegister(M68kRegister.A0)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero) => DefaultWordBody(pointer, offset, zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefaultWordBody(APTR pointer, int offset, bool zero)
    { if (zero) APTR.WriteUInt16(pointer, offset, 0); else APTR.WriteUInt16(pointer, offset, 0xC000); }
    [M68kInternalRegisterAbi, M68kExport("store.long.default")]
    public static void DefaultLong([M68kRegister(M68kRegister.A0)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero) => DefaultLongBody(pointer, offset, zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefaultLongBody(APTR pointer, int offset, bool zero)
    { if (zero) APTR.WriteUInt32(pointer, offset, 0); else APTR.WriteUInt32(pointer, offset, 0x89ABCDEF); }
    [M68kInternalRegisterAbi, M68kExport("store.byte.fixed.default")]
    public static void DefaultFixedByte([M68kRegister(M68kRegister.A0)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero) => DefaultFixedByteBody(pointer, offset, zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefaultFixedByteBody(APTR pointer, int offset, bool zero)
    { if (zero) APTR.WriteUInt8(pointer, 170, 0); else APTR.WriteUInt8(pointer, 170, 0xEF); }
    [M68kInternalRegisterAbi, M68kExport("store.word.fixed.default")]
    public static void DefaultFixedWord([M68kRegister(M68kRegister.A0)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero) => DefaultFixedWordBody(pointer, offset, zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefaultFixedWordBody(APTR pointer, int offset, bool zero)
    { if (zero) APTR.WriteUInt16(pointer, 170, 0); else APTR.WriteUInt16(pointer, 170, 0xC000); }
    [M68kInternalRegisterAbi, M68kExport("store.long.fixed.default")]
    public static void DefaultFixedLong([M68kRegister(M68kRegister.A0)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero) => DefaultFixedLongBody(pointer, offset, zero);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DefaultFixedLongBody(APTR pointer, int offset, bool zero)
    { if (zero) APTR.WriteUInt32(pointer, 170, 0); else APTR.WriteUInt32(pointer, 170, 0x89ABCDEF); }
    [M68kInternalRegisterAbi, M68kExport("store.byte.fixed")]
    public static void FixedByte([M68kRegister(M68kRegister.A6)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero)
    {
        if (zero) APTR.WriteUInt8(pointer, 170, 0);
        else APTR.WriteUInt8(pointer, 170, 0xEF);
    }
    [M68kInternalRegisterAbi, M68kExport("store.word.fixed")]
    public static void FixedWord([M68kRegister(M68kRegister.A6)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero)
    {
        if (zero) APTR.WriteUInt16(pointer, 170, 0);
        else APTR.WriteUInt16(pointer, 170, 0xC000);
    }
    [M68kInternalRegisterAbi, M68kExport("store.long.fixed")]
    public static void FixedLong([M68kRegister(M68kRegister.A6)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero)
    {
        if (zero) APTR.WriteUInt32(pointer, 170, 0);
        else APTR.WriteUInt32(pointer, 170, 0x89ABCDEF);
    }
    public static void Empty() { }
    [M68kInternalRegisterAbi, M68kExport("store.byte")]
    public static void Byte([M68kRegister(M68kRegister.A6)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero)
    {
        if (zero) APTR.WriteUInt8(pointer, offset, 0);
        else APTR.WriteUInt8(pointer, offset, 0xEF);
    }
    [M68kInternalRegisterAbi, M68kExport("store.word")]
    public static void Word([M68kRegister(M68kRegister.A6)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero)
    {
        if (zero) APTR.WriteUInt16(pointer, offset, 0);
        else APTR.WriteUInt16(pointer, offset, 0xC000);
    }
    [M68kInternalRegisterAbi, M68kExport("store.long")]
    public static void Long([M68kRegister(M68kRegister.A6)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] int offset, [M68kRegister(M68kRegister.D1)] bool zero)
    {
        if (zero) APTR.WriteUInt32(pointer, offset, 0);
        else APTR.WriteUInt32(pointer, offset, 0x89ABCDEF);
    }
}
