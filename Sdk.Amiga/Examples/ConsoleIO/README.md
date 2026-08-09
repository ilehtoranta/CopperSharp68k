# ConsoleIO

Portable `System.Console` input/output example for CopperSharp68k on AmigaOS.
It reads one line with `Console.Read()`, echoes the characters, and prints the
character count. The source uses no Amiga SDK calls; the Amiga platform layer
binds the supported `System.Console` members to `dos.library` during AOT
compilation.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\ConsoleIO\ConsoleIO.csproj
```

Build and run the bootable ADF test in WinUAE:

```powershell
.\scripts\run-winuae-dos-examples.ps1
```

The ADF startup sequence redirects a known input file into `ConsoleIO`, captures
its output, and runs a separately compiled verifier that checks the complete
output before writing the host-visible success marker.
