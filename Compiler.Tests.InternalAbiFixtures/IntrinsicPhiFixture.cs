using Amiga;

namespace CopperSharp.Compiler.Tests;

public static class IntrinsicPhiFixture
{
    public static void Empty() { }

    [M68kInternalRegisterAbi, M68kExport("intrinsic.phi.load")]
    public static uint TakeHead([M68kRegister(M68kRegister.A0)] APTR list)
    {
        var head = APTR.ReadUInt32(list, 0);
        var next = APTR.ReadUInt32(APTR.FromPointer(head), 0);
        if (next == 0) head = 0;
        else
        {
            APTR.WriteUInt32(list, 0, next);
            APTR.WriteUInt32(APTR.FromPointer(next), 4, list.Raw);
        }
        return head;
    }
}
