# CopperBars

`CopperBars` is a low-level PAL OCS demo for an A500-class Amiga with a
Motorola 68000. It blanks the current view, owns the blitter, disables task
switching and interrupts, and drives the custom chips directly. Four shaded
Copper bars bounce vertically over a dark background. Click the left mouse
button to exit.

![CopperBars running in WinUAE](../../../docs/assets/CopperBars-WinUAE.png)

The setup and cleanup paths use `exec.library` and `graphics.library`. The
actual demo loop runs after `Forbid()` and `Disable()` and makes no OS calls:
it accesses the custom-chip registers at `$DFF000`, reads the left mouse button
from CIA-A at `$BFE001`, patches the wait positions in a Copper list in chip
RAM before vertical blank, and lets COP1 restart it at the hardware frame
boundary. On exit it restores the graphics startup Copper list plus the saved
DMA, interrupt-enable, pending-interrupt, and audio/disk-control state before
returning to the saved view.

Build the managed example assembly:

```powershell
dotnet build .\Sdk.Amiga\Examples\CopperBars\CopperBars.csproj
```

Compile it as a YOLO-mode Amiga HUNK executable:

```powershell
dotnet run --project .\Compiler.Cli -- `
  .\Sdk.Amiga\Examples\CopperBars\bin\Debug\net10.0\CopperBars.dll `
  --entry CopperBarsExample.Program::Main `
  --output .\Sdk.Amiga\Examples\CopperBars\CopperBars `
  --platform amiga --cpu 68000 --format hunk --runtime application `
  --exceptions yolo --symbols off
```

This example keeps the bar waits below line 256. At PAL line 280, the Copper
strobes its interrupt-request bit to tell the CPU that the list is safely
parked and can be updated. The interrupt remains disabled, so the CPU polls and
clears the request without invoking an interrupt handler. It is not an NTSC
example. Run it from a shell on a PAL OCS/ECS machine or emulator; if
experimental compiler output or hardware state prevents the cleanup path from
running, reset the machine.

The dedicated ADF test builds and runs the demo with Kickstart 3.1 on
cycle-exact PAL OCS WinUAE, clicks the left mouse button, and verifies that
startup resumes after the clean exit:

```powershell
.\scripts\run-winuae-copper-bars.ps1
```
