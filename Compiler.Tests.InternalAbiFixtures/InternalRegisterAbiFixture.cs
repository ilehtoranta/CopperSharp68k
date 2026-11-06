using System.Runtime.CompilerServices;
using Amiga;
using CopperSharp.Sdk.Amiga;
namespace CopperSharp.Compiler.Tests;

public static class InternalRegisterAbiFixture
{
    public static void Empty() { }
    [M68kExport("abi.managed")]
    public static void Entry() => Changed(5, 7, 0x4000u, 0x3000u);
    [M68kExport("abi.direct"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void Direct([M68kRegister(M68kRegister.D0)] uint count,
        [M68kRegister(M68kRegister.D1)] uint value, [M68kRegister(M68kRegister.A1)] APTR destination,
        [M68kRegister(M68kRegister.A6)] APTR basis) =>
        APTR.WriteUInt32(destination, 0, APTR.ReadUInt32(basis, 0) + count + value);
    [M68kExport("abi.forward"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void Forward([M68kRegister(M68kRegister.D0)] uint count,
        [M68kRegister(M68kRegister.D1)] uint value, [M68kRegister(M68kRegister.A1)] APTR destination,
        [M68kRegister(M68kRegister.A6)] APTR basis) => Direct(count, value, destination, basis);
    [M68kExport("abi.changed"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void Changed([M68kRegister(M68kRegister.D0)] uint count,
        [M68kRegister(M68kRegister.D1)] uint value, [M68kRegister(M68kRegister.A1)] APTR destination,
        [M68kRegister(M68kRegister.A6)] APTR basis) => Direct(count, value, destination, (APTR)((uint)basis + 4));
    [M68kExport("abi.external"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void External([M68kRegister(M68kRegister.D0)] uint count,
        [M68kRegister(M68kRegister.D1)] uint value, [M68kRegister(M68kRegister.A1)] APTR destination,
        [M68kRegister(M68kRegister.A6)] APTR basis)
    {
        PreservingCall(basis);
        PreservingCall((APTR)((uint)basis + 4));
        Direct(count, value, destination, basis);
    }
    [M68kExport("abi.livecall"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void LiveCall([M68kRegister(M68kRegister.D0)] uint count,
        [M68kRegister(M68kRegister.D1)] uint value, [M68kRegister(M68kRegister.A1)] APTR destination,
        [M68kRegister(M68kRegister.A6)] APTR basis)
    {
        uint result = ValueCall(count, value, basis);
        APTR.WriteUInt32(destination, 0, result);
        APTR.WriteUInt32(destination, 4, count);
        APTR.WriteUInt32(destination, 8, value);
    }
    [M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    private static uint ValueCall([M68kRegister(M68kRegister.D3)] uint count,
        [M68kRegister(M68kRegister.D4)] uint value, [M68kRegister(M68kRegister.A6)] APTR basis)
        => APTR.ReadUInt32(basis, 0) + count + value;
    [M68kExport("abi.admission"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void Admission([M68kRegister(M68kRegister.D0)] uint count,
        [M68kRegister(M68kRegister.D1)] uint flags, [M68kRegister(M68kRegister.A1)] APTR destination,
        [M68kRegister(M68kRegister.A6)] APTR basis)
    {
        uint size = (flags & 2) != 0 ? (count >> 16) * (ushort)count : count;
        if (size == 0) return;
        if (size >= 512 && (size & 1) == 0 && (uint)destination < 0x200000 &&
            (APTR.ReadUInt16(basis, 8) & 0x240) == 0x240) {
            Direct(size, flags, destination, basis);
            return;
        }
        APTR.WriteUInt32(destination, 0, size ^ flags);
    }
    [M68kExport("abi.predicates"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static uint Predicates([M68kRegister(M68kRegister.D0)] uint value,
        [M68kRegister(M68kRegister.D1)] uint flags, [M68kRegister(M68kRegister.A1)] APTR input)
    {
        if ((APTR.ReadUInt8(input, 0) & 0x80) != 0 && (flags & 0x80000000u) != 0)
            return value ^ flags;
        ushort word = APTR.ReadUInt16(input, 2);
        if ((word & 0x240) == 0x240) return value + word;
        uint masked = value & 0xFFFF;
        if (masked == 0x8000) return masked;
        if (masked == 0x240) return value;
        if (masked < 0x8000) return masked + 1;
        return (uint)(int)(short)masked;
    }
    [M68kExport("abi.conditionalreturn"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void ConditionalReturn([M68kRegister(M68kRegister.D0)] uint value,
        [M68kRegister(M68kRegister.D1)] uint flags, [M68kRegister(M68kRegister.A1)] APTR output)
    {
        if ((flags & 1) != 0) {
            uint product = value * 17;
            APTR.WriteUInt32(output, 0, product);
            if (product == 0) return;
            APTR.WriteUInt32(output, 4, product);
        }
    }
    [M68kExport("abi.conditionalloop"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static void ConditionalLoop([M68kRegister(M68kRegister.D0)] uint value,
        [M68kRegister(M68kRegister.D1)] uint flags, [M68kRegister(M68kRegister.A1)] APTR output)
    {
        while ((APTR.ReadUInt8(output, 0) & 0x80) != 0) { }
        if ((flags & 1) != 0) APTR.WriteUInt32(output, 4, value * 17);
    }
    [M68kExport("abi.readbits"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static uint ReadBits([M68kRegister(M68kRegister.A1)] APTR input)
    {
        _ = APTR.ReadUInt16(input, -2);
        uint value = 0;
        byte first = APTR.ReadUInt8(input, 0);
        if ((first & 0x80) != 0) value = 1;
        ushort second = APTR.ReadUInt16(input, 2);
        if ((second & 0x80) != 0) value |= 2;
        APTR.WriteUInt32(input, 10, second);
        APTR.WriteUInt32(input, 14, 0x10000);
        if ((short)second >= -1) value |= 8;
        if ((first & 1) != 0) value |= 4;
        _ = APTR.ReadUInt8(input, 4);
        _ = APTR.ReadUInt32(input, 6);
        return value;
    }
    [M68kExport("abi.bitflag"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static uint BitFlag([M68kRegister(M68kRegister.D1)] uint flags)
    {
        if ((flags & 1) != 0) return 7;
        return 9;
    }
    [M68kExport("abi.addresspredicate"), M68kInternalRegisterAbi, MethodImpl(MethodImplOptions.NoInlining)]
    public static uint AddressPredicate([M68kRegister(M68kRegister.A1)] APTR address,
        [M68kRegister(M68kRegister.A0)] APTR output)
    {
        if ((uint)address < 0x200000) APTR.WriteUInt32(output, 0, 7);
        return 11;
    }
    [AmigaLibrary("fixture.library", AmigaLibraryBasePolicy.CallerProvided), AmigaLvo(-30)]
    public static extern void PreservingCall([M68kRegister(M68kRegister.A6)] APTR basis);
}
