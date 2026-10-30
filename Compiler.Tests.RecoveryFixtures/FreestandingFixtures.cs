using System.Runtime.CompilerServices;
using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class FreestandingClusterEntryFixture
{
    public static uint Entry() => global::Amiga.Exec.FindTask(CString.FromPointer(0)).Raw + 3;

    [M68kExport("freestanding.cluster.hot")]
    [return: M68kRegister(M68kRegister.D0)]
    public static uint Hot([M68kRegister(M68kRegister.D0)] uint value) => Mix(value) + Mix(value + 1) + Mix(value + 2) +
        Mix(value + 3) + Mix(value + 4) + Mix(value + 5) + Mix(value + 6) + Mix(value + 7);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static uint Mix(uint value)
    {
        for (var i = 0; i < 32; i++) value = (value ^ (value >> 3)) * 17 + (uint)i;
        return value;
    }
}

public static unsafe class FreestandingStateFixture
{
    private static uint counter;
    public static uint Entry()
    {
        *(uint*)0x40000 = APTR.ExportAddress("freestanding.state.add").Raw;
        *(uint*)0x40004 = APTR.ExportAddress("freestanding.state.read").Raw;
        return Add(1);
    }
    [M68kExport("freestanding.state.add")]
    [return: M68kRegister(M68kRegister.D0)]
    public static uint Add([M68kRegister(M68kRegister.D0)] uint value) => counter += value;
    [M68kExport("freestanding.state.read")]
    [return: M68kRegister(M68kRegister.D0)]
    public static uint Read() => counter;
}
