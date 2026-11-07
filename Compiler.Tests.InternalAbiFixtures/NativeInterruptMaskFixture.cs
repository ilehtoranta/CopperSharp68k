namespace CopperSharp.Compiler.Tests;

public static class NativeInterruptMaskFixture
{
    public static void Empty() { }

    [M68kImport("intrinsic:copperstart-disable-interrupts")]
    private static extern uint DisableInterrupts();

    [M68kInternalRegisterAbi, M68kExport("mask.discard")]
    public static uint Discard([M68kRegister(M68kRegister.D0)] uint liveValue)
    {
        _ = DisableInterrupts();
        return liveValue;
    }
}
