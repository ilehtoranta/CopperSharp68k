using Amiga;
using CopperSharp.Compiler;
using System.Runtime.CompilerServices;

namespace CopperSharp.Compiler.Tests;

public static class LiteralAddressFixture
{
    public static void Empty() { }

    [M68kExport("literal-address.inline")]
    public static uint Entry() => (uint)APTR.ReadUInt8(Address(), 0) + APTR.ReadUInt8(Address(), 3);

    [M68kExport("literal-address.retained")]
    public static uint RetainedEntry() => (uint)APTR.ReadUInt8(RetainedAddress(), 0) + APTR.ReadUInt8(RetainedAddress(), 3);

    public static APTR Address() => APTR.FromPointer(CString.ToUInt32(CString.FromLiteral("AZBY")));

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static APTR RetainedAddress() => APTR.FromPointer(CString.ToUInt32(CString.FromLiteral("CZBX")));
}
