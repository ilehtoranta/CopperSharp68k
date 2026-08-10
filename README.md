# CopperSharp68k

CopperSharp68k is a closed-world CIL-to-Motorola 68000-family ahead-of-time
compiler for C# and other .NET languages.

The current target emits 68k code with Amiga HUNK executable, Kickstart ROM, and
assembler output support. Amiga platform bindings and library-call resolution
live in the SDK and Amiga integration projects; the compiler core is kept
separate so additional targets such as MorphOS/PPC can be added later.

## Projects

- `Compiler` - compiler core, 68k backend, HUNK/ROM/assembly output.
- `Compiler.Cli` - command-line driver.
- `Targets.Amiga` - Amiga library-call resolver and platform helpers.
- `Sdk.Amiga` - Amiga ABI declarations for compiled code.
- `Compiler.Tests` - compiler tests. These use a sibling
  `MedPlayer/Copper68k` checkout when available, otherwise the `Copper68k`
  package.

## Build

```powershell
dotnet build CopperSharp68k.slnx
dotnet test CopperSharp68k.slnx
```

## .NET 10 compatibility profile

CopperSharp compiles an explicitly bounded subset of ordinary `net10.0`
assemblies using exact framework bindings, verified implementation packs, a
private managed runtime, and an Amiga platform layer. It never discovers an
installed host runtime implicitly. Start with the consolidated
[.NET 10 profile documentation](Compiler/Framework/README.md); the exact member
inventory remains machine-readable in the profile manifest.

Pinned CoreLib is the recommended `Stopwatch` implementation when a verified
pack is available, with `ShadowStopwatch` retained as a deterministic fallback.
The retained cross-CPU comparison is in
[compatibility and performance testing](Compiler/Framework/CompatibilityTesting.md#pinned-stopwatch-implementation-comparison).

## Admitted `System.Math` surface

The net10.0/10.0.9 compatibility ledger admits the exact integral overloads of
`Abs`, `Min`, `Max`, `Clamp`, and `Sign` that fit the current scalar ABI, plus
the 32-by-32-bit signed and unsigned `BigMul` forms. It also admits the
`float`/`double` overloads of `Abs`, `Min`, `Max`, `Clamp`, and `Sign`,
`Math.CopySign(double, double)`, the core `Single`/`Double` IEEE classifiers,
and `Floor`, `Ceiling`, `Truncate`, `Round`, and `Sqrt` for `double`.

The portable implementations operate on IEEE bit patterns and run in
`SoftFloat` mode without a floating-point arithmetic runtime. Native FPU modes
lower `Sqrt` and `Truncate` leaves to `FSQRT` and `FINTRZ`; the other rounding
modes use the deterministic software path so their result does not depend on
ambient FPCR rounding state. Decimal, `Int128`/`UInt128`, tuple-returning
`DivRem`, digit-count `Round` overloads, `MathF`, and transcendental functions
remain fail-closed.

## CopperScreen emulator tests

`Compiler.Tests` contains end-to-end tests that compile `CompilerFixtures` to
Amiga HUNK executables and run them with CopperScreen's deterministic headless
runner. The matrix checks arithmetic/control flow and exception handling on
accurate-interpreter and JIT paths for MC68000 output, plus MC68040 output on
the MC68040 JIT. Focused cases cover managed runtime faults and cross-method
unwinding, 64-bit/multiword ABI behavior, virtual/interface dispatch,
capturing delegates across forced collection, static type initialization,
managed arrays and strings, and native MC68040 floating-point execution. The
managed-pool cases force collection and verify that live caller-frame and
closure roots survive.

When the sibling `MedPlayer/CopperScreen.Headless.Cli` project exists, it is
built with the tests and its Debug or Release output is discovered
automatically. Otherwise, point to the executable or managed DLL explicitly:

```powershell
$env:COPPERSCREEN_HEADLESS_CLI = 'D:\Koodit\GIT\MedPlayer\CopperScreen.Headless.Cli\bin\Debug\net10.0\CopperScreen.Headless.Cli.exe'
dotnet test .\Compiler.Tests\CopperSharp.Compiler.Tests.csproj --filter 'Category=Emulator'
```

The emulator process is bounded by both instruction/frame limits and a host
timeout. Missing CopperScreen binaries skip this optional category; an invalid
explicit path fails with a configuration error.

## WinUAE smoke tests

The slower machine-boot smoke layer builds a bootable 880 KiB ADF containing a
small custom bootblock and a managed CopperSharp68k fixture. It boots the
same ADF with Kickstart 1.3 and 3.1 in WinUAE; no Workbench disk, filesystem
commands, or `dos.library` startup is required. The managed fixture obtains its
arena through Kickstart Exec `AllocMem`, drops an allocation, forces a
mark/sweep collection, verifies a live root and replacement allocation, and
releases the arena through `FreeMem`; explicit `AvailMem` calls additionally
check the public Exec register ABI. It returns a fixed value, and the host
reads it from guest memory through WinUAE's headless IPC
endpoint with a strict timeout. A marker in the final ADF sector remains as a
fallback for emulators that flush guest writes directly to the image.

Run both locally available ROM profiles:

```powershell
.\scripts\run-winuae-smoke.ps1
```

The defaults discover WinUAE under `C:\Program Files\WinUAE` and the sibling
CopperScreen Kickstart 1.3/3.1 ROMs. Portable CI agents should provide paths
explicitly or set `COPPERSHARP_KICKSTART13_ROM` and
`COPPERSHARP_KICKSTART31_ROM`:

```powershell
.\scripts\run-winuae-smoke.ps1 `
  -WinUaePath 'C:\Tools\WinUAE\winuae64.exe' `
  -Kickstart13Path 'D:\ROMs\kick13.rom' `
  -Kickstart31Path 'D:\ROMs\kick31.rom'
```

Use `-Kickstart 1.3` or `-Kickstart 3.1` for one profile, `-DryRun` to build
and inspect generated configuration without launching WinUAE, and
`-KeepArtifacts` to retain the ADF and `.uae` file. Kickstart 3.1 also runs
freestanding MC68040 integer and native-FPU probes; use `-M68040Only` to run
only those two profiles. Each profile receives its own generated ADF. Every
image is also a normal bootable ADF that can be mounted in CopperScreen;
CopperScreen's current automated integration uses the faster direct-HUNK
headless path described above.

To produce a reusable image without launching an emulator:

```powershell
.\scripts\build-amiga-smoke-adf.ps1 `
  -OutputPath .\artifacts\CopperSharp68k-Smoke.adf
```

The AmigaDOS example test uses a separate, genuine bootable OFS image. It
contains the compiled `DOS`, `FileStats`, portable `ConsoleIO`, `Polymorphism`,
and `StopwatchBenchmark` examples,
successful test inputs, `S:startup-sequence`, and a minimal `C:Execute`
implementation compiled from C#. The startup sequence redirects input and
output for `ConsoleIO`, then a compiled verifier checks every captured byte.
The WinUAE harness boots to the Kickstart CLI, enters the startup command, and
waits for a result file written only after all checks pass:

```powershell
.\scripts\run-winuae-dos-examples.ps1
```

Use `-Kickstart 1.3` or `-Kickstart 3.1` to select one ROM. Build the reusable
filesystem image without launching WinUAE with:

```powershell
.\scripts\build-amiga-dos-examples-adf.ps1 `
  -OutputPath .\artifacts\CopperSharp68k-DOS.adf
```

The low-level PAL OCS `CopperBars` demo has its own AmigaDOS ADF test. The
WinUAE harness runs Kickstart 3.1 on an A500-compatible, cycle-exact MC68000
profile, clicks the left mouse button to exercise the demo's clean-exit path,
and waits for the startup sequence to write a success marker after control
returns:

```powershell
.\scripts\run-winuae-copper-bars.ps1
```

Build the standalone demo image without launching WinUAE with:

```powershell
.\scripts\build-amiga-copper-bars-adf.ps1 `
  -OutputPath .\artifacts\CopperSharpCopperBars.adf
```

This layer is intended for nightly or pre-release validation rather than the
normal unit test gate.
