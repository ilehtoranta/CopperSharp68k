using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Copper68k;
using CopperMod.Amiga.Lightweight;
using CopperSharp.Compiler.BootAdf;
using CopperSharp.Compiler.Tests.NativeOsFixture;
using CopperSharp.Targets.Amiga;
using Hst.Amiga.FileSystems.FastFileSystem;
using Directory = System.IO.Directory;
using File = System.IO.File;

namespace CopperSharp.Compiler.NativeOsRunner;

public sealed record NativeOsResult(string RunId, string StopReason, uint LaunchStatus,
    uint ReturnValue, long Frames, long Cycles, string CpuModel, string ExecutionMode,
    string RomSha256, string HunkSha256, string InitialAdfSha256, string? PayloadOutput,
    string? DiskSnapshotError, int DeferredDiskSnapshots, string? StandardOutput);

public static class NativeOsSession
{
    public static async Task<NativeOsResult> RunAsync(byte[] payload, byte[] rom, string arguments,
        M68kCpuModel cpuModel, bool jit, int maxFrames, TimeSpan timeout, string? saveAdf = null,
        IReadOnlyDictionary<string, byte[]>? inputFiles = null)
    {
        if (maxFrames <= 0 || timeout <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maxFrames));
        if (rom.Length != 524288 || BinaryPrimitives.ReadUInt16BigEndian(rom.AsSpan(12)) < 37)
            throw new ArgumentException("A 512 KiB Kickstart 2.04 or newer ROM is required; the validated profile uses 3.1.");
        if (payload.Length < 4 || BinaryPrimitives.ReadUInt32BigEndian(payload) != 0x3f3)
            throw new ArgumentException("The payload must be an Amiga Hunk executable.");
        if (arguments.Any(c => c > 127 || c is '\0' or '\r' or '\n') || arguments.Length >= 1024)
            throw new ArgumentException("Arguments must be single-line ASCII, at most 1023 bytes.");
        if (jit && cpuModel != M68kCpuModel.M68040) throw new ArgumentException("JIT requires 68040.");
        ValidateInputFiles(inputFiles);

        var runId = Guid.NewGuid().ToString("N");
        var temporaryRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CopperSharpNativeOs"));
        var stage = Path.Combine(temporaryRoot, runId);
        byte[] image;
        Directory.CreateDirectory(Path.Combine(stage, "S"));
        try
        {
            var launcher = AmigaM68kCompiler.Compile(new M68kCompilationRequest
            {
                AssemblyPath = typeof(NativeOsLauncher).Assembly.Location,
                EntryPoint = $"{typeof(NativeOsLauncher).FullName}::Main",
                RuntimeProfile = M68kRuntimeProfile.Application,
                ExceptionMode = M68kExceptionMode.Full,
                IncludedExportNames = []
            }).Image;
            await File.WriteAllBytesAsync(Path.Combine(stage, "LAUNCH"), launcher);
            await File.WriteAllBytesAsync(Path.Combine(stage, "PROGRAM"), payload);
            await File.WriteAllBytesAsync(Path.Combine(stage, "RUN.ID"), Encoding.ASCII.GetBytes(runId));
            await File.WriteAllBytesAsync(Path.Combine(stage, "ARGS"), Encoding.ASCII.GetBytes(arguments + "\n"));
            if (inputFiles is not null)
                foreach (var (name, contents) in inputFiles)
                    await File.WriteAllBytesAsync(Path.Combine(stage, name), contents);
            await File.WriteAllBytesAsync(Path.Combine(stage, "S", "startup-sequence"), Encoding.ASCII.GetBytes("LAUNCH\n"));
            image = await FileSystemAdf.CreateAsync(stage, "CopperSharpNative");
        }
        finally
        {
            // Only remove the unique directory this session created under our staging root.
            if (!Path.GetFullPath(stage).StartsWith(temporaryRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Staging directory escaped its root.");
            Directory.Delete(stage, recursive: true);
        }

        var executionMode = jit ? M68kExecutionMode.Jit : M68kExecutionMode.Interpreter;
        using var machine = new LightweightA500Machine(new LightweightA500Configuration
        {
            CpuModel = cpuModel, CpuExecutionMode = executionMode,
            ChipRamBytes = 512 * 1024, SlowRamBytes = 512 * 1024
        });
        machine.LoadKickstart(rom);
        machine.MountAdf(image);
        machine.SetDriveWriteProtected(0, false);
        machine.Reset();
        var watch = Stopwatch.StartNew();
        var reason = "FrameLimitReached";
        uint status = uint.MaxValue, answer = 0;
        var lastImage = image;
        string? diskError = null;
        var deferredSnapshots = 0;
        // Keep all machine operations on its owner thread, including disk export.
        for (var frame = 0; frame < maxFrames; frame++)
        {
            machine.ExecuteFrame();
            if (frame % 10 == 9 && machine.IsDriveDirty(0))
            {
                var snapshot = TryExportAdf(machine, out diskError);
                if (snapshot is null) deferredSnapshots++;
                else
                {
                    lastImage = snapshot;
                    var bytes = TryReadDiskFile(snapshot, "RESULT.BIN", out diskError);
                    if (diskError is not null) deferredSnapshots++;
                    if (TryReadResult(bytes, runId, out status, out answer))
                    {
                        reason = status == 0 ? "ProgramReturned" : "LaunchFailed";
                        break;
                    }
                }
            }
            if (machine.Cpu.Halted) { reason = "CpuHalted"; break; }
            if (watch.Elapsed >= timeout) { reason = "HostTimeout"; break; }
        }
        var finalImage = TryExportAdf(machine, out diskError);
        var frames = machine.CompletedFrames;
        var cycles = machine.Cycle;
        var output = TryReadDiskFile(finalImage ?? lastImage, "PAYLOAD.OK", out var payloadError);
        var standardOutput = TryReadDiskFile(finalImage ?? lastImage, "STDOUT.TXT", out var outputError);
        diskError ??= payloadError ?? outputError;
        if (saveAdf is not null && finalImage is not null)
        {
            var path = Path.GetFullPath(saveAdf);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, finalImage);
        }
        return new(runId, reason, status, answer, frames, cycles, cpuModel.ToString(), executionMode.ToString(),
            Convert.ToHexString(SHA256.HashData(rom)), Convert.ToHexString(SHA256.HashData(payload)),
            Convert.ToHexString(SHA256.HashData(image)), output is null ? null : Encoding.ASCII.GetString(output),
            diskError, deferredSnapshots, standardOutput is null ? null : Encoding.Latin1.GetString(standardOutput));
    }

    internal static void ValidateInputFiles(IReadOnlyDictionary<string, byte[]>? files)
    {
        if (files is null) return;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string[] reserved = ["S", "LAUNCH", "PROGRAM", "RUN.ID", "ARGS", "RESULT.BIN", "STDOUT.TXT", "PAYLOAD.OK"];
        foreach (var (name, contents) in files)
        {
            if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.Length > 30 ||
                name.EndsWith('.') || name.EndsWith(' ') ||
                name.Any(c => c < 32 || c > 126 || c is '/' or '\\' or ':' || Path.GetInvalidFileNameChars().Contains(c)) ||
                reserved.Contains(name, StringComparer.OrdinalIgnoreCase) || !names.Add(name) || contents is null)
                throw new ArgumentException("Input files require unique ASCII root filenames of at most 30 characters, excluding runner files.", nameof(files));
        }
    }

    private static byte[]? TryReadDiskFile(byte[] image, string name, out string? error)
    {
        error = null;
        try { return ReadDiskFileAsync(image, name).GetAwaiter().GetResult(); }
        catch (IOException exception)
        {
            // DOS can publish a directory pointer before the next sector write
            // has persisted its entry. Only retry observation; never repair data.
            error = exception.Message;
            return null;
        }
    }

    private static byte[]? TryExportAdf(LightweightA500Machine machine, out string? error)
    {
        error = null;
        try { return machine.ExportAdf(); }
        catch (InvalidOperationException exception) when (exception.Message.StartsWith("Track ", StringComparison.Ordinal) &&
            exception.Message.Contains("valid AmigaDOS sectors; it cannot be saved as a standard ADF.", StringComparison.Ordinal))
        {
            // A DMA write can temporarily leave a track without eleven complete sectors.
            // Retry on later frames; a persistent problem remains visible in the result.
            error = exception.Message;
            return null;
        }
    }

    public static bool TryReadResult(byte[]? bytes, string runId, out uint status, out uint answer)
    {
        status = uint.MaxValue;
        answer = 0;
        if (bytes is null || bytes.Length != 48 || runId.Length != 32 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes) != 0x43534f53 ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(4)) != 1 ||
            !bytes.AsSpan(8, 32).SequenceEqual(Encoding.ASCII.GetBytes(runId))) return false;
        status = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(40));
        answer = BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(44));
        return true;
    }

    public static async Task<byte[]?> ReadDiskFileAsync(byte[] image, string name)
    {
        using var stream = new MemoryStream(image);
        await using var volume = await FastFileSystemVolume.MountAdf(stream);
        var entries = await volume.ListEntries();
        if (!entries.Any(entry => string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase))) return null;
        await using var file = await volume.OpenFile(name, Hst.Amiga.FileSystems.FileMode.Read);
        using var contents = new MemoryStream();
        await file.CopyToAsync(contents);
        return contents.ToArray();
    }
}
