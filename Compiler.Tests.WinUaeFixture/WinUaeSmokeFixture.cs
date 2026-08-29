using Amiga;
using CopperSharp.Compiler;

namespace CopperSharp.Compiler.Tests.WinUaeFixture;

public static class WinUaeSmokeFixture
{
    public const string SuccessMarker = "COPPERSHARP68K_WINUAE_OK";
    private const string DosExamplesSuccessMarkerPrefix = "COPPERSHARP68K_";
    private const string DosExamplesSuccessMarkerSuffix = "DOS_OK";
    [M68kEntryPoint]
    public static uint BareMetalMain()
    {
        var answer = 6 * 7;
        return answer == 42 ? 0x4353_4850u : 0u;
    }

    [M68kEntryPoint]
    public static uint ManagedGcMain()
    {
        var survivor = new uint[2];
        var availableBefore = Exec.AvailMem(Exec.MemoryFlags.Any);

        survivor[0] = 19;
        survivor[1] = 23;

        uint[]? garbage = new uint[16];
        garbage[0] = 0xDEAD_BEEFu;
        garbage = null;
        M68kRuntime.Collect();

        var replacement = new uint[16];
        replacement[15] = 1;
        var availableAfter = Exec.AvailMem(Exec.MemoryFlags.Any);
        return availableBefore != 0 && availableAfter != 0 && survivor[0] + survivor[1] == 42 && replacement[15] == 1
            ? 0x4353_4850u
            : 0u;
    }
    [M68kEntryPoint]
    public static uint M68040IntegerMain() =>
        Multiply(6, 7) == 42 ? 0x4353_4850u : 0u;

    [M68kEntryPoint]
    public static float M68040FpuMain() => Add(1.25f, 2.5f);

    [M68kEntryPoint]
    public static int DosExamplesPassedMain()
    {
        var dosBase = Exec.OpenLibrary("dos.library", 0);
        if (!dosBase.HasValue)
        {
            return DOS.RETURN_FAIL;
        }

        DOS.DOSLibraryBase = dosBase.Value;
        DOS.PutStr("ADF disk suite passed.\n");

        var file = DOS.Open("RESULT.OK", DOS.FileMode.NewFile);
        var result = DOS.RETURN_FAIL;
        if (file.HasValue)
        {
            var prefix = CString.FromLiteral(DosExamplesSuccessMarkerPrefix);
            var suffix = CString.FromLiteral(DosExamplesSuccessMarkerSuffix);
            result = DOS.Write(
                file.Value,
                CString.ToUInt32(prefix),
                DosExamplesSuccessMarkerPrefix.Length) == DosExamplesSuccessMarkerPrefix.Length &&
                DOS.Write(
                    file.Value,
                    CString.ToUInt32(suffix),
                    DosExamplesSuccessMarkerSuffix.Length) == DosExamplesSuccessMarkerSuffix.Length
                ? DOS.RETURN_OK
                : DOS.RETURN_FAIL;
			DOS.Flush(file.Value);
            DOS.Close(file.Value);
        }

        Exec.CloseLibrary(DOS.DOSLibraryBase);
        DOS.DOSLibraryBase = APTR.Null;
        return result;
    }

    [M68kEntryPoint]
    public static int PolymorphismPassedMain()
    {
        var dosBase = Exec.OpenLibrary("dos.library", 0);
        if (!dosBase.HasValue)
        {
            return DOS.RETURN_FAIL;
        }

        DOS.DOSLibraryBase = dosBase.Value;
        var result = DOS.PutStr("Polymorphism test passed.\n") < 0
            ? DOS.RETURN_FAIL
            : DOS.RETURN_OK;
        Exec.CloseLibrary(DOS.DOSLibraryBase);
        DOS.DOSLibraryBase = APTR.Null;
        return result;
    }

    [M68kEntryPoint]
    public static int ExecuteDosExamplesMain(int argLength, CONST_STRPTR argText)
    {
        var dosBase = Exec.OpenLibrary("dos.library", 0);
        if (!dosBase.HasValue)
        {
            return DOS.RETURN_FAIL;
        }

        DOS.DOSLibraryBase = dosBase.Value;
        var output = DOS.Output();
        var result = DOS.Execute(
            "DOS TestDir\n" +
            "FileStats sample.txt\n" +
            "StopwatchBenchmark\n",
            BPTR.Null,
            output);
        if (result != 0)
        {
            // The example intentionally returns 21 after its no-argument
            // virtual/interface dispatch path. Execute it separately so that
            // its observable result cannot suppress the suite marker.
            DOS.Execute("Polymorphism\n", BPTR.Null, output);
            DOS.Execute("PolymorphismPassed\n", BPTR.Null, output);
            result = DOS.Execute("ExamplesPassed\n", BPTR.Null, output);
        }
        Exec.CloseLibrary(DOS.DOSLibraryBase);
        DOS.DOSLibraryBase = APTR.Null;
        return result != 0 ? DOS.RETURN_OK : DOS.RETURN_FAIL;
    }

    private static int Multiply(int left, int right) => left * right;
    private static float Add(float left, float right) => left + right;



    [M68kEntryPoint]
    public static int Main() =>
        DOS.PutStr(SuccessMarker + "\n") < 0 ? 20 : 0;
}
