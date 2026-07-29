# FileStats

`FileStats` is a small YOLO-style `dos.library` example. It opens a file with
the library base managed explicitly by the program, reads it through
`FGetC()`, and reports its byte count, line count, and two rolling checksums.

The counters intentionally use all three 68k integer widths:

- `byte` for the byte checksum and input character
- `ushort` for the line count and word checksum
- `uint` for the total byte count

There are no managed exception handlers. Errors and cleanup are handled with
explicit branches, which keeps the generated startup and failure paths small
for a YOLO build.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\FileStats\FileStats.csproj
```

To generate assembler, compile the assembly through
`CopperSharp.Targets.Amiga.AmigaM68kCompiler` with
`ExceptionMode = M68kExceptionMode.Yolo` and
`OutputFormat = M68kOutputFormat.Assembly`. The Amiga target is required so
the SDK's `[AmigaLibrary]` and `[AmigaLvo]` declarations are resolved.
