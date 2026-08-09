using Amiga;
using CopperSharp.Compiler;

namespace CopperSharpAmiga;

public static class Program
{
    [M68kEntryPoint]
    public static int Main()
    {
        var dosBase = Exec.OpenLibrary("dos.library", 33);
        if (dosBase is null)
        {
            return DOS.RETURN_FAIL;
        }

        DOS.DOSLibraryBase = dosBase.Value;
        DOS.PutStr("Hello from CopperSharp.\n");
        Exec.CloseLibrary(DOS.DOSLibraryBase);
        DOS.DOSLibraryBase = APTR.Null;
        return DOS.RETURN_OK;
    }
}
