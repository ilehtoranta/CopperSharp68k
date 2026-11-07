using Amiga;
using CopperSharp.Compiler;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class SmallMemoryHelperFixture
{
    public static void NativeNarrowLongStore() => NarrowLongStore(APTR.FromPointer(0x5000),
        APTR.ReadUInt8(APTR.FromPointer(0x5010), 0), APTR.ReadUInt16(APTR.FromPointer(0x5012), 0));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NarrowLongStore(APTR address, byte narrow, ushort word)
    {
        APTR.WriteUInt32(address, 0, narrow);
        APTR.WriteUInt32(address, 4, word);
    }
    public static void LongStorePair(APTR address, uint value)
    {
        StoreLong(address, 0, value);
        StoreLong(address, 4, value);
    }
    private static void StoreLong(APTR address, int offset, uint value) => APTR.WriteUInt32(address, offset, value);
    [M68kExport("small-memory-helper.direct")]
    public static void NativeEntry() => Entry(APTR.FromPointer(0x5000));

    [M68kExport("small-memory-helper.generic")]
    public static void GenericNativeEntry()
    {
        var memory = default(SmallWordMemory);
        GenericDecrement(ref memory, APTR.FromPointer(0x4FFC));
        GenericDecrement(ref memory, APTR.FromPointer(0x4FFE));
    }

    private static void GenericDecrement<T>(ref T memory, APTR address) where T : struct, ISmallWordMemory
    {
        var value = memory.Read(address, 4);
        memory.Write(address, 4, unchecked((ushort)(value - 1)));
    }

    public static void NoInlineEntry(APTR address)
    {
        NoInlineDecrement(address);
        NoInlineDecrement(APTR.FromPointer(address.Raw + 2));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void NoInlineDecrement(APTR address)
    {
        var value = APTR.ReadUInt16(address, 0);
        APTR.WriteUInt16(address, 0, unchecked((ushort)(value - 1)));
    }

    public static void Entry(APTR address)
    {
        Decrement(address);
        Decrement(APTR.FromPointer(address.Raw + 2));
    }

    private static void Decrement(APTR address)
    {
        var value = APTR.ReadUInt16(address, 0);
        APTR.WriteUInt16(address, 0, unchecked((ushort)(value - 1)));
    }
}

public interface ISmallWordMemory
{
    ushort Read(APTR address, int offset);
    void Write(APTR address, int offset, ushort value);
}

public readonly struct SmallWordMemory : ISmallWordMemory
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort Read(APTR address, int offset) => APTR.ReadUInt16(address, offset);
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(APTR address, int offset, ushort value) => APTR.WriteUInt16(address, offset, value);
}
