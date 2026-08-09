# FileStats

`FileStats` is a small YOLO-style `dos.library` example. It opens a file with
the library base managed explicitly by the program, reads it through
`FGetC()`, and reports its byte count, line count, average byte value, two
rolling checksums, and a 32-bit file fingerprint.

The counters intentionally use all three 68k integer widths:

- `byte` for the byte checksum and input character
- `ushort` for the line count and a rotate-and-add checksum
- `uint` for the total byte count, byte sum, average, and multiplicative hash

The rotate-and-add checksum exercises left and right shifts at 16-bit width.
The FNV-style fingerprint exercises 32-bit XOR and multiplication, while the
average byte value introduces unsigned 32-bit division. Together these make
the example useful for comparing arithmetic lowering and optimization across
CPU targets.

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
