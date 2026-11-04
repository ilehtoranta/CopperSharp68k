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
    [AmigaLibrary("fixture.library", AmigaLibraryBasePolicy.CallerProvided), AmigaLvo(-30)]
    public static extern void PreservingCall([M68kRegister(M68kRegister.A6)] APTR basis);
}
