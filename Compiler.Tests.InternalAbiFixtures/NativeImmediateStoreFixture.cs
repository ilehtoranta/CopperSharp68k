using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class NativeImmediateStoreFixture
{
    [M68kInternalRegisterAbi, M68kExport("store.long.literal")]
    public static void LiteralLong([M68kRegister(M68kRegister.A1)] APTR pointer)
    {
        APTR.WriteUInt32(pointer, 6, 0x010100FFu);
        APTR.WriteUInt32(APTR.FromPointer(0x30000), 10, 0xABCD0123u);
        APTR.WriteUInt32(pointer, 14, 0x01AA0000u);
        APTR.WriteUInt32(pointer, 18, 0xFFFF0000u);
    }

    [M68kInternalRegisterAbi, M68kExport("store.truncated-integer")]
    public static uint StoreTruncatedInteger([M68kRegister(M68kRegister.A1)] APTR pointer,
        [M68kRegister(M68kRegister.D0)] uint value)
    {
        ushort word = (ushort)value;
        APTR.WriteUInt16(pointer, 0, word);
        APTR.WriteUInt8(pointer, 2, (byte)value);
        return value ^ word;
    }

    [M68kInternalRegisterAbi, M68kExport("store.word.shared-float")]
    public static uint SharedAddressAlongsideFloat()
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt16(pointer, 0, 0x4000);
        StoreBarrier();
        APTR.WriteUInt16(pointer, 0, 0xC000);
        return FloatConstant() == 1.0f ? 0x3F800000u : 0u;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float FloatConstant() => 1.0f;

    [M68kInternalRegisterAbi, M68kExport("store.word.shared-null")]
    public static uint SharedAddressAlongsideNull()
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt16(pointer, 0, 0x4000);
        StoreBarrier();
        APTR.WriteUInt16(pointer, 0, 0xC000);
        return NullConstant() is null ? 0u : 1u;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object? NullConstant() => null;

    [M68kInternalRegisterAbi, M68kExport("store.long.address-register")]
    public static void StoreAddressRegister([M68kRegister(M68kRegister.A0)] APTR value,
        [M68kRegister(M68kRegister.A1)] APTR destination)
    {
        APTR.WriteUInt32(destination, 4, value.Raw);
        APTR.WriteUInt32(destination, 8, value.Raw);
    }

    [M68kInternalRegisterAbi, M68kExport("store.word.shared-address")]
    public static void SharedWordAddress([M68kRegister(M68kRegister.D0)] int mode)
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt16(pointer, 0, 0x4000);
        StoreBarrier();
        if (mode == 0) APTR.WriteUInt16(pointer, 0, 0);
        else if (mode == 1) APTR.WriteUInt16(pointer, 0, 0xC000);
    }

    [M68kInternalRegisterAbi, M68kExport("store.byte.shared-address")]
    public static void SharedByteAddress([M68kRegister(M68kRegister.D0)] int mode)
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt8(pointer, 0, 0x40);
        StoreBarrier();
        if (mode == 0) APTR.WriteUInt8(pointer, 0, 0);
        else if (mode == 1) APTR.WriteUInt8(pointer, 0, 0xC0);
    }

    [M68kInternalRegisterAbi, M68kExport("store.word.shared-loop")]
    public static void SharedWordLoop([M68kRegister(M68kRegister.D0)] int count)
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt16(pointer, 0, 0x4000);
        StoreBarrier();
        for (var n = 0; n < count; n++) APTR.WriteUInt16(pointer, 0, 0xC000);
    }

    [M68kInternalRegisterAbi, M68kExport("store.word.shared-three")]
    public static void SharedWordThree()
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt16(pointer, 0, 0x4000);
        StoreBarrier();
        APTR.WriteUInt16(pointer, 0, 0xC000);
        APTR.WriteUInt16(pointer, 0, 0x8000);
    }

    [M68kInternalRegisterAbi, M68kExport("store.word.shared-read")]
    public static uint SharedWordRead()
    {
        var pointer = APTR.FromPointer(0x30000);
        APTR.WriteUInt16(pointer, 0, 0x4000);
        StoreBarrier();
        var value = APTR.ReadUInt16(pointer, 0);
        APTR.WriteUInt16(pointer, 0, 0xC000);
        return value;
    }

    [M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    private static void StoreBarrier() => APTR.WriteUInt8(APTR.FromPointer(0x34000), 0, 0x12);

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
