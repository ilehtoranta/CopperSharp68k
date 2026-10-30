using Amiga;

namespace CopperSharp.Compiler.Tests.NativeOsFixture;

public static class NativeOsFixture
{
    public static int Answer() => 42;
    public static int HighBitAnswer() => unchecked((int)0xDEADBEEF);

    public static int ArgumentProbe()
    {
        var library = Exec.OpenLibrary("dos.library", 37);
        if (!library.HasValue) return 10;
        DOS.DOSLibraryBase = library.Value;
        var text = DOS.GetArgStr().Address;
        var valid = !text.IsNull && APTR.ReadUInt8(text, 0) == (byte)'O' &&
            APTR.ReadUInt8(text, 1) == (byte)'K' && APTR.ReadUInt8(text, 2) == (byte)'\n';
        Exec.CloseLibrary(library.Value);
        DOS.DOSLibraryBase = APTR.Null;
        return valid ? 42 : 13;
    }

    public static int DosProbe()
    {
        var library = Exec.OpenLibrary("dos.library", 37);
        if (!library.HasValue) return 10;
        DOS.DOSLibraryBase = library.Value;
        var file = DOS.Open("PAYLOAD.OK", DOS.FileMode.NewFile);
        var result = 11;
        if (file.HasValue)
        {
            var text = CString.FromLiteral("native DOS payload\n");
            result = DOS.Write(file.Value, CString.ToUInt32(text), 19) == 19 ? 42 : 12;
            DOS.Flush(file.Value);
            DOS.Close(file.Value);
        }
        Exec.CloseLibrary(library.Value);
        DOS.DOSLibraryBase = APTR.Null;
        return result;
    }
}

// Runs as a normal DOS command under Kickstart 3.1. The native loader and
// RunCommand establish the payload's segment list, process context and stack.
public static class NativeOsLauncher
{
    // Header + 1024 argument bytes + zero padding for string readers.
    private const uint StorageSize = 1080;

    public static int Main()
    {
        var library = Exec.OpenLibrary("dos.library", 37);
        if (!library.HasValue) return 20;
        DOS.DOSLibraryBase = library.Value;
        var storage = Exec.AllocMem(StorageSize, Exec.MemoryFlags.Public | Exec.MemoryFlags.Clear);
        if (storage.IsNull)
        {
            Exec.CloseLibrary(library.Value);
            DOS.DOSLibraryBase = APTR.Null;
            return 21;
        }
        var runId = DOS.Open("RUN.ID", DOS.FileMode.OldFile);
        var idLength = -1;
        if (runId.HasValue)
        {
            idLength = DOS.Read(runId.Value, storage.Raw + 8, 32);
            DOS.Close(runId.Value);
        }
        if (idLength != 32)
        {
            Exec.FreeMem(storage, StorageSize);
            Exec.CloseLibrary(library.Value);
            DOS.DOSLibraryBase = APTR.Null;
            return 22;
        }
        APTR.WriteUInt32(storage, 0, 0x43534F53); // CSOS
        APTR.WriteUInt32(storage, 4, 1);

        var arguments = DOS.Open("ARGS", DOS.FileMode.OldFile);
        var length = -1;
        if (arguments.HasValue)
        {
            length = DOS.Read(arguments.Value, storage.Raw + 48, 1024);
            DOS.Close(arguments.Value);
        }
        var status = 1u;
        var answer = 0u;
        if (length >= 0)
        {
            var segment = DOS.LoadSeg("PROGRAM");
            if (segment.HasValue)
            {
                var standardOutput = DOS.Open("STDOUT.TXT", DOS.FileMode.NewFile);
                if (standardOutput.HasValue)
                {
                    var previousOutput = DOS.SelectOutput(standardOutput.Value);
                    answer = unchecked((uint)DOS.RunCommand(segment.Value, 65536,
                        APTR.FromPointer(storage.Raw + 48), length));
                    DOS.DOSLibraryBase = library.Value;
                    // Publish output completely before the completion record.
                    var flushed = DOS.Flush(standardOutput.Value);
                    DOS.SelectOutput(previousOutput);
                    var closed = DOS.Close(standardOutput.Value);
                    status = flushed != 0 && closed != 0 ? 0u : 3u;
                }
                else status = 2;
                DOS.UnLoadSeg(segment.Value);
            }
        }
        // The payload may modify library-base state. Restore our own context.
        DOS.DOSLibraryBase = library.Value;
        APTR.WriteUInt32(storage, 40, status);
        APTR.WriteUInt32(storage, 44, answer);
        var output = DOS.Open("RESULT.BIN", DOS.FileMode.NewFile);
        var result = 23;
        if (output.HasValue)
        {
            result = DOS.Write(output.Value, storage.Raw, 48) == 48 ? 0 : 24;
            DOS.Flush(output.Value);
            DOS.Close(output.Value);
        }
        Exec.FreeMem(storage, StorageSize);
        Exec.CloseLibrary(library.Value);
        DOS.DOSLibraryBase = APTR.Null;
        return result;
    }
}
