# CopperSharp.Compiler.Cli

Command-line driver for the optional `CopperSharp.Compiler` CIL-to-Motorola 68k
ahead-of-time compiler.

```text
dotnet tool install CopperSharp.Compiler.Cli
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68000 \
  --format hunk --output Firmware.hunk
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --format asm --runtime application --output Firmware.s
```

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
