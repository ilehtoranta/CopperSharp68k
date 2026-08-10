# CopperSharp.Compiler.Cli

Command-line driver for the optional `CopperSharp.Compiler` CIL-to-Motorola 68k
ahead-of-time compiler.

```text
dotnet tool install --global CopperSharp.Compiler.Cli --version 0.1.0-preview.1
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68000 \
  --format hunk --output Firmware.hunk
copper68kc Application.dll --entry Application.Program::Main \
  --platform amiga --format hunk --output Application
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --format asm --runtime application --output Firmware.s
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --framework-report framework-compatibility.json
```

`--framework-report <file|->` performs closed-world analysis without generating
target code. It writes the exact reachable .NET framework members, shortest
rooting paths, call sites, compatibility status, binding, effects, required
runtime features, and pinned reference-pack contract as JSON. Use `-` for
standard output. The command returns a non-zero exit code when
the reachable graph contains a `deferred` or `unsupported` member.

`--platform amiga` enables Amiga SDK library-vector resolution and automatic
library-base handling. The default `generic` platform remains freestanding.
`--managed-assemblies <file>` adds resolved dependency assemblies from one
UTF-8 path per line. `@response-manifest` accepts the versioned SDK `key=value`
format, including repeated `managed-assembly` entries, so dependency paths and
publish settings do not expand the shell command.
`--compatibility-report` writes the exact framework analysis already produced
by a successful compilation; it does not run a second reachability pass.

`--framework-implementation-manifest <file>` opts into compiler-approved bodies
from an explicitly pinned framework implementation pack. The same setting is
available as `framework-implementation-manifest=<file>` in a response manifest.
The compiler does not inspect installed host runtimes: it verifies the assembly
identity, MVID, and SHA-256 declared by the supplied manifest, and records that
provenance in the map and compatibility JSON. Implementation assemblies remain
separate from `--managed-assembly` dependency inputs.

For the supported Stopwatch instance slice, pinned CoreLib is preferred when a
verified manifest is available: it provides the official implementation and
currently beats the shadow implementation in image size, generated code size,
and measured execution cycles. Omitting the option keeps `ShadowStopwatch` as
the deterministic fallback; the CLI never discovers a host runtime on its own.

Run `copper68kc --help` for ROM layout and fixed-address import options.
Use `--fpu disabled|040|68882|soft` to select floating-point generation. `040`
requires `--cpu 68040` or `--cpu 68060`; `68882` requires `--cpu 68020`.
Floating point is disabled by default.
`--clr auto` enables `CLR` for known-readable frame and stack slots and
automatically for all memory on 68020/040/060 output; use `--clr always` to opt in
for other 68000-compatible memory targets.
HUNK output defaults to the terminating `application` lifetime. Assembly
output defaults to `freestanding`; pass `--runtime application` only when the
entry is invoked once and private image storage becomes unobservable after it
returns. ROM output is persistent and never receives terminal-store removal.
HUNK output includes method symbols by default. Pass `--symbols off` to omit
`HUNK_SYMBOL` records from release executables. The equivalent response
manifest entry is `symbols=off`.
