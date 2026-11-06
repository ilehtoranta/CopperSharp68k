using Amiga;
using CopperSharp.Sdk.Amiga;

namespace CopperSharp.Compiler.Tests;

public static class NativeWaitLoopFixture
{
    public static void Empty() { }
    [M68kInternalRegisterAbi, M68kExport("wait.loop")]
    public static uint Loop([M68kRegister(M68kRegister.D0)] uint mask,
        [M68kRegister(M68kRegister.A6)] APTR state)
    {
        while (true)
        {
            var delivered = APTR.ReadUInt32(state, 0) & mask;
            if (delivered != 0) return delivered;
            delivered = Invoke(APTR.FromPointer(0x3000), mask, state);
            if (delivered != 0) return delivered;
        }
    }

    [AmigaIndirectCall(M68kRegister.A3)]
    [return: M68kRegister(M68kRegister.D0)]
    private static extern uint Invoke([M68kRegister(M68kRegister.A3)] APTR entry,
        [M68kRegister(M68kRegister.D0)] uint mask, [M68kRegister(M68kRegister.A6)] APTR state);
}
