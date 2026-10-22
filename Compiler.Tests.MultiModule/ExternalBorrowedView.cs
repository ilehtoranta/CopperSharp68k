using System.Runtime.CompilerServices;
using Amiga;

namespace CopperSharp.Compiler.Tests.MultiModule;

public interface IExternalViewMemory
{
    uint Read(APTR address, int offset);
    void Write(APTR address, int offset, uint value);
}

public readonly ref struct ExternalBorrowedView<T> where T : struct, IExternalViewMemory
{
    private readonly ref T memory;
    private readonly APTR address;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ExternalBorrowedView(ref T memory, APTR address)
    {
        this.memory = ref memory;
        this.address = address;
    }

    public uint Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => memory.Read(address, 0);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => memory.Write(address, 0, value);
    }
}
