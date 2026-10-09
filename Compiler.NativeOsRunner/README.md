# Native AmigaDOS Hunk runner

This optional runner boots a standard OFS floppy in the current CopperScreen
Lightweight engine. The 901,120-byte ADF is mounted from host memory and stays
in memory during execution. No disk image is saved unless `--save-adf` is given.
The source files used to construct it are staged in a unique temporary directory
and removed before emulation starts. ROM bytes are supplied by the user and
are never put on the ADF.

The disk contains `S/startup-sequence`, `LAUNCH`, `PROGRAM`, `RUN.ID`, and `ARGS`.
Kickstart starts the shell's startup sequence normally. The compiled launcher
opens `dos.library`, uses native `LoadSeg`, runs the payload through native
`RunCommand` with a 64 KiB stack, unloads it, and writes `RESULT.BIN` through DOS.
No host Hunk loader, fake library vectors, guest-memory result injection, or
host-directory mount participates in this path.

The launcher opens `STDOUT.TXT` through DOS and selects it as the payload's
standard output. It restores the previous output, flushes and closes the file
before publishing completion. JSON `StandardOutput` contains its Latin-1 text;
`PayloadOutput` still contains the separate `PAYLOAD.OK` probe file. Launch status
2 means output could not be opened; status 3 means flushing or closing failed.

The completion record has a magic, version, a fresh 32-byte run identifier,
launch status, and the full 32-bit return value, all integers in big-endian form.
Only an exact matching record counts as completion. Floppy snapshots may be
temporarily unreadable during a native DMA write; polling retries these and
reports a persistent unreadable track as `DiskSnapshotError`.
The same bounded polling also retries filesystem snapshots whose directory
references have been written before their entry sectors. Persistent filesystem
errors remain visible in `DiskSnapshotError`; the runner never repairs them.

## Build and verify

Use the repository's pinned .NET SDK and an adjacent `CopperScreen` checkout.
The project accepts `-p:CopperScreenRoot=ABSOLUTE_PATH` for other locations.
The current engine's CPU package may use its local development feed. Restore
with that checkout's NuGet configuration, rather than changing its package pin:

```powershell
dotnet restore Compiler.NativeOsRunner/CopperSharp.Compiler.NativeOsRunner.csproj --configfile ../CopperScreen/NuGet.Config
dotnet build Compiler.NativeOsRunner/CopperSharp.Compiler.NativeOsRunner.csproj -c Release --no-restore
$env:COPPERSHARP_KICKSTART31_ROM = 'C:/path/to/kickstart-3.1-a500.rom'
./scripts/test-native-os.ps1
./scripts/test-native-os.ps1 -IncludeCompilerIntegration
```

The script requires a ROM and runs both protocol and native execution tests.
FileStats executions compare Disabled and FixedPoint optimizer modes against
exact reports for empty, high-bit and binary data, a quoted filename, a missing
file, an extra argument and an empty command line. The script saves their Hunk
images and per-run records under `FileStats` in its results directory. Set
`COPPERSHARP_NATIVE_FILESTATS_RESULTS` to retain the same evidence when invoking
the test project directly.
Directly running the test project without the ROM runs the protocol tests and
explicitly skips native execution. This optional project is separate from the
main test suite so compiler unit tests do not require an emulator checkout.

`-IncludeCompilerIntegration` also runs `CopperScreenHeadlessIntegrationTests`
through this runner. It configures `COPPERSHARP_NATIVE_OS_RUNNER` for the test
process and saves JSON records and their Hunk images under `CompilerIntegration`.
Records include compiler, fixture, runner and Hunk hashes, expected return values,
and the native result. The script restores its environment settings on completion.
Set `COPPERSHARP_NATIVE_OS_INTEGRATION_RESULTS` to retain this evidence when
invoking the compiler test project directly.

The integration cases use the 68000 interpreter, the 68040 interpreter running
68000 payloads, and the 68040 JIT running 68040 payloads. There is no 68000 JIT
equivalent in the current engine. An absent ROM configuration explicitly skips
these optional cases; configured missing ROM or runner paths fail. The retired
FileStats payload-only cost test stays skipped until instruction and cycle
measurement can isolate the payload. A separate test bounds the native boot.

```powershell
dotnet Compiler.NativeOsRunner/bin/Release/net10.0/CopperSharp.Compiler.NativeOsRunner.dll --fixture DosProbe --expect-d0 42
dotnet Compiler.NativeOsRunner/bin/Release/net10.0/CopperSharp.Compiler.NativeOsRunner.dll --hunk program.hunk --arguments 'OK' --expect-d0 0xDEADBEEF --save-adf run.adf
```

`--rom` overrides the environment variable. `--fixture` accepts `Answer`,
`HighBitAnswer`, `DosProbe`, and `ArgumentProbe` (the latter expects `--arguments OK`).
Arguments must be single-line ASCII, at most 1023 bytes; the launcher supplies
the terminating newline. Decimal expected values accept signed Int32 or unsigned
UInt32 ranges; hexadecimal accepts all 32 bits. The CLI emits JSON and exits
0 on a successful matching return, 2 on execution failure or expectation mismatch,
and 1 on input/build errors. `LaunchFailed`, `FrameLimitReached`, `HostTimeout`,
and `CpuHalted` cannot satisfy the success check.

`--input-file HOST_FILE` copies one file into the ADF root under its filename.
The session API accepts multiple files through `inputFiles`. Names must be
unique ignoring case, ASCII, at most 30 characters, and valid host filenames;
paths, volume prefixes, trailing dots/spaces and runner filenames are rejected.
The files are copied before native boot and included in `InitialAdfSha256`.
They must fit on the floppy together with the launcher and payload.

For example, run a compiled FileStats Hunk against a host input file:

```powershell
dotnet Compiler.NativeOsRunner/bin/Release/net10.0/CopperSharp.Compiler.NativeOsRunner.dll --hunk filestats.hunk --input-file INPUT.BIN --arguments INPUT.BIN --expect-d0 0
```

FileStats uses DOS [`ReadArgs`](https://wiki.amigaos.net/wiki/Basic_Input_and_Output_Programming) with `FILE/A` for newline-terminated command
lines, including quoted filenames with spaces. Direct callers supplying a
filename without the DOS newline retain their existing pointer contract.
It requests `dos.library` version 37 or newer and releases both the parser's
attached buffers and the supplied argument structure after reporting.

## Profile and limits

The initial validated profile is Kickstart 3.1 A500, PAL OCS, 512 KiB Chip RAM
and 512 KiB Slow RAM, 68000 interpreter. `RunCommand` requires DOS v37 or newer;
Kickstart 1.3 is not supported by this launcher. The runner accepts 68020 and
68040 configurations, with JIT restricted to 68040 by the current engine.
The compiler integration matrix also validates 68040 interpreter and JIT
execution; 68020 native execution remains unvalidated. Accelerator timing is
approximate, and there is no 68000 JIT backend in this engine.

The default bounds are 1500 frames and 120 seconds of host emulation time.
Reported frames and cycles include boot, floppy access, payload execution,
and completion polling. They are not payload-only costs or replacements for
the retired CLI's instruction/cycle baselines. Hashes identify the ROM, input
Hunk, and initial ADF; the run identifier and filesystem timestamps vary per run.
