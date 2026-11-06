using Amiga;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class NativeMemoryCompareFixture
{
    [M68kInternalRegisterAbi, M68kExport("compare.byte.default")]
    public static uint DefaultByte([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
        => DefaultByteBody(pointer, mask);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultByteBody(APTR pointer, uint mask)
        => APTR.ReadUInt8(pointer, 15) == 4 ? mask & APTR.ReadUInt32(pointer, 22) : mask;
    [M68kInternalRegisterAbi, M68kExport("compare.word.default")]
    public static uint DefaultWord([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
        => DefaultWordBody(pointer, mask);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultWordBody(APTR pointer, uint mask)
        => APTR.ReadUInt16(pointer, 14) == 0x4000 ? mask & APTR.ReadUInt32(pointer, 22) : mask;
    [M68kInternalRegisterAbi, M68kExport("compare.changed.default")]
    public static uint DefaultChanged([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
        => DefaultChangedBody(pointer, mask);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultChangedBody(APTR pointer, uint mask)
    {
        var value = APTR.ReadUInt8(pointer, 15);
        APTR.WriteUInt8(pointer, 15, 4);
        return value == 4 ? mask & APTR.ReadUInt32(pointer, 22) : mask;
    }
    [M68kInternalRegisterAbi, M68kExport("compare.live.default")]
    public static uint DefaultLive([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
        => DefaultLiveBody(pointer, mask);
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint DefaultLiveBody(APTR pointer, uint mask)
    {
        var value = APTR.ReadUInt8(pointer, 15);
        return value == 4 ? mask + value : mask ^ value;
    }
    public static void Empty() { }
    [M68kInternalRegisterAbi, M68kExport("compare.byte")]
    public static uint Byte([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
        => APTR.ReadUInt8(pointer, 15) == 4 ? mask & APTR.ReadUInt32(pointer, 22) : mask;
    [M68kInternalRegisterAbi, M68kExport("compare.word")]
    public static uint Word([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
        => APTR.ReadUInt16(pointer, 14) == 0x4000 ? mask & APTR.ReadUInt32(pointer, 22) : mask;
    [M68kInternalRegisterAbi, M68kExport("compare.changed")]
    public static uint Changed([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
    {
        var value = APTR.ReadUInt8(pointer, 15);
        APTR.WriteUInt8(pointer, 15, 4);
        return value == 4 ? mask & APTR.ReadUInt32(pointer, 22) : mask;
    }
    [M68kInternalRegisterAbi, M68kExport("compare.live")]
    public static uint Live([M68kRegister(M68kRegister.A1)] APTR pointer, [M68kRegister(M68kRegister.D0)] uint mask)
    {
        var value = APTR.ReadUInt8(pointer, 15);
        return value == 4 ? mask + value : mask ^ value;
    }
}
