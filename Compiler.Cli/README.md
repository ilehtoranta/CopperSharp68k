# CopperSharp.Compiler.Cli

Command-line driver for the optional `CopperSharp.Compiler` CIL-to-Motorola 68k
ahead-of-time compiler.

```text
dotnet tool install CopperSharp.Compiler.Cli
copper68kc Firmware.dll --entry Firmware.Boot::Main --cpu 68000 \
  --format hunk --output Firmware.hunk
copper68kc Firmware.dll --entry Firmware.Boot::Main \
  --format asm --output Firmware.s
```

Run `copper68kc --help` for ROM layout and fixed-address import options.
